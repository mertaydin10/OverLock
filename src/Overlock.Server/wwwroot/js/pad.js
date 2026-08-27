(() => {
  const Y = window.OverlockYata;
  const pad = document.getElementById("pad");
  const nameEl = document.getElementById("name");
  const roomEl = document.getElementById("room");
  const statusEl = document.getElementById("status");
  const presenceEl = document.getElementById("presence");
  const countsEl = document.getElementById("counts");
  const caretsEl = document.getElementById("carets");
  const shareEl = document.getElementById("share");

  const peers = new Map();
  const undoStack = [];
  const redoStack = [];
  let doc = null;
  let applying = false;
  let hub = null;

  function randomRoom() {
    const alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
    return Array.from({ length: 6 }, () => alphabet[Math.floor(Math.random() * alphabet.length)]).join("");
  }

  const params = new URLSearchParams(location.search);
  const room = (params.get("room") || "").trim() || randomRoom();
  roomEl.value = room;
  if (!params.get("room")) {
    history.replaceState(null, "", `?room=${encodeURIComponent(room)}`);
  }

  nameEl.value = localStorage.getItem("overlock.name") || "";

  function setStatus(state, label) {
    statusEl.dataset.state = state;
    statusEl.textContent = label;
  }

  function renderText(caretGrapheme = null) {
    const text = Y.text(doc);
    applying = true;
    pad.value = text;
    if (caretGrapheme != null) {
      const utf16 = Y.graphemeToUtf16(text, Math.max(0, Math.min(caretGrapheme, doc.length)));
      pad.selectionStart = pad.selectionEnd = utf16;
    }
    applying = false;
    countsEl.textContent = `${doc.length} characters · ${peers.size + 1} peer${peers.size === 0 ? "" : "s"}`;
    renderPresence();
    renderCarets();
  }

  function renderPresence() {
    const items = [{ name: nameEl.value || "You", color: "#c9893a", you: true }, ...peers.values()];
    presenceEl.innerHTML = items.map((p) =>
      `<div class="peer"><span class="dot" style="background:${p.color}"></span><span>${escapeHtml(p.name)}${p.you ? " (you)" : ""}</span></div>`
    ).join("");
  }

  function escapeHtml(value) {
    return value.replace(/[&<>"']/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch]));
  }

  function renderCarets() {
    const text = pad.value;
    caretsEl.innerHTML = "";
    const cs = getComputedStyle(pad);
    for (const peer of peers.values()) {
      const utf16 = Y.graphemeToUtf16(text, peer.caret ?? 0);
      const mirror = document.createElement("div");
      mirror.style.cssText = `position:absolute;inset:0;padding:${cs.padding};font:${cs.font};line-height:${cs.lineHeight};white-space:pre-wrap;word-wrap:break-word;visibility:hidden;`;
      const before = document.createElement("span");
      before.textContent = text.slice(0, utf16);
      const mark = document.createElement("i");
      mark.className = "caret";
      mark.style.background = peer.color;
      mark.style.setProperty("--peer", peer.color);
      mark.style.visibility = "visible";
      mark.innerHTML = `<span>${escapeHtml(peer.name)}</span>`;
      mirror.append(before, mark);
      caretsEl.append(mirror);
    }
  }

  async function sendOps(ops) {
    for (const op of ops) {
      await hub.invoke("Submit", op);
    }
  }

  function pushUndo(ops) {
    undoStack.push(ops.map((op) => Y.inverse(op)).reverse());
    redoStack.length = 0;
  }

  async function applyLocal(ops, caret, recordUndo = true) {
    if (recordUndo) pushUndo(ops);
    await sendOps(ops);
    renderText(caret);
  }

  pad.addEventListener("beforeinput", async (event) => {
    if (!doc || applying) return;
    event.preventDefault();

    const startG = Y.utf16ToGrapheme(pad.value, pad.selectionStart);
    const endG = Y.utf16ToGrapheme(pad.value, pad.selectionEnd);
    const type = event.inputType;

    if (type === "historyUndo") {
      const inverse = undoStack.pop();
      if (!inverse) return;
      for (const op of inverse) Y.apply(doc, op);
      redoStack.push(inverse.map((op) => Y.inverse(op)).reverse());
      await sendOps(inverse);
      renderText();
      return;
    }

    if (type === "historyRedo") {
      const forward = redoStack.pop();
      if (!forward) return;
      for (const op of forward) Y.apply(doc, op);
      undoStack.push(forward.map((op) => Y.inverse(op)).reverse());
      await sendOps(forward);
      renderText();
      return;
    }

    const ops = [];
    let caret = startG;
    if (endG > startG) {
      ops.push(...Y.delete(doc, startG, endG - startG));
      caret = startG;
    }

    if (type.startsWith("insert")) {
      let text = event.data ?? "";
      if (type === "insertLineBreak" || type === "insertParagraph") text = "\n";
      if (type === "insertFromPaste" || type === "insertFromDrop") {
        text = event.dataTransfer ? event.dataTransfer.getData("text/plain") : text;
      }
      if (text) {
        ops.push(...Y.insert(doc, startG, text));
        caret = startG + Y.graphemes(text).length;
      }
    } else if (type.startsWith("delete") && endG === startG) {
      if ((type === "deleteContentBackward" || type === "deleteByCut") && startG > 0) {
        ops.push(...Y.delete(doc, startG - 1, 1));
        caret = startG - 1;
      } else if (type === "deleteContentForward" && startG < doc.length) {
        ops.push(...Y.delete(doc, startG, 1));
        caret = startG;
      }
    }

    if (ops.length) await applyLocal(ops, caret);
  });

  pad.addEventListener("keyup", () => {
    if (!hub || !doc) return;
    const index = Y.utf16ToGrapheme(pad.value, pad.selectionStart);
    hub.invoke("Caret", { index }).catch(() => {});
  });

  shareEl.addEventListener("click", async () => {
    const url = `${location.origin}/?room=${encodeURIComponent(roomEl.value.trim())}`;
    await navigator.clipboard.writeText(url);
    shareEl.textContent = "Copied";
    setTimeout(() => { shareEl.textContent = "Copy link"; }, 1200);
  });

  nameEl.addEventListener("change", () => {
    localStorage.setItem("overlock.name", nameEl.value.trim());
  });

  roomEl.addEventListener("change", () => {
    const next = roomEl.value.trim();
    if (next) location.search = `?room=${encodeURIComponent(next)}`;
  });

  async function connect() {
    hub = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/pad")
      .withAutomaticReconnect()
      .build();

    hub.on("Snapshot", (snapshot) => {
      doc = Y.fromSnapshot(snapshot.replicaId, snapshot.atoms);
      peers.clear();
      for (const peer of snapshot.peers) {
        if (peer.replicaId !== snapshot.replicaId) peers.set(peer.replicaId, peer);
      }
      renderText();
      setStatus("on", "live");
    });

    hub.on("Op", (op) => {
      if (!doc) return;
      Y.apply(doc, op);
      renderText();
    });

    hub.on("PeerJoined", (peer) => {
      peers.set(peer.replicaId, peer);
      renderPresence();
      renderCarets();
    });

    hub.on("PeerLeft", (replicaId) => {
      peers.delete(replicaId);
      renderPresence();
      renderCarets();
    });

    hub.on("CaretMoved", (replicaId, index) => {
      const peer = peers.get(replicaId);
      if (!peer) return;
      peer.caret = index;
      renderCarets();
    });

    hub.on("Rejected", (reason) => {
      setStatus("off", reason);
    });

    hub.onreconnecting(() => setStatus("off", "reconnecting"));
    hub.onreconnected(() => join());

    await hub.start();
    await join();
  }

  async function join() {
    setStatus("off", "joining");
    await hub.invoke("Join", { roomId: roomEl.value.trim(), name: nameEl.value.trim() || "Guest" });
  }

  connect().catch((err) => {
    setStatus("off", "failed");
    console.error(err);
  });
})();
