(() => {
  const segmenter = typeof Intl !== "undefined" && Intl.Segmenter
    ? new Intl.Segmenter("en", { granularity: "grapheme" })
    : null;

  function graphemes(text) {
    if (!text) return [];
    if (segmenter) return [...segmenter.segment(text)].map((s) => s.segment);
    return Array.from(text);
  }

  function key(id) {
    return `${id.replica}:${id.seq}`;
  }

  function noteSeq(doc, id) {
    const seen = doc.version.has(id.replica) ? doc.version.get(id.replica) : -1;
    if (id.seq > seen) doc.version.set(id.replica, id.seq);
  }

  function indexOf(doc, id) {
    const k = key(id);
    for (let i = 0; i < doc.content.length; i++) {
      if (key(doc.content[i].id) === k) return i;
    }
    return -1;
  }

  function findItemAtPos(doc, pos) {
    let i = 0;
    for (; i < doc.content.length; i++) {
      if (doc.content[i].isDeleted) continue;
      if (pos === 0) return i;
      pos--;
    }
    if (pos === 0) return i;
    throw new Error("position past end of document");
  }

  function canInsert(doc, op) {
    const last = doc.version.has(op.id.replica) ? doc.version.get(op.id.replica) : -1;
    if (op.id.seq !== last + 1) return false;
    if (op.originLeft && !doc.known.has(key(op.originLeft))) return false;
    if (op.originRight && !doc.known.has(key(op.originRight))) return false;
    return true;
  }

  function integrateYata(doc, op) {
    const left = op.originLeft ? indexOf(doc, op.originLeft) : -1;
    let destIdx = left + 1;
    const right = op.originRight ? indexOf(doc, op.originRight) : doc.content.length;
    let scanning = false;

    for (let i = destIdx; ; i++) {
      if (!scanning) destIdx = i;
      if (i === doc.content.length || i === right) break;

      const other = doc.content[i];
      const oleft = other.originLeft ? indexOf(doc, other.originLeft) : -1;
      const oright = other.originRight ? indexOf(doc, other.originRight) : doc.content.length;

      if (oleft < left) break;
      if (oleft === left) {
        if (op.id.replica > other.id.replica) {
          scanning = false;
          continue;
        }
        if (oright === right) break;
        scanning = true;
        continue;
      }
    }

    doc.content.splice(destIdx, 0, {
      id: op.id,
      originLeft: op.originLeft ?? null,
      originRight: op.originRight ?? null,
      content: op.content,
      isDeleted: false
    });
    doc.known.add(key(op.id));
    noteSeq(doc, op.id);
    doc.length += 1;
  }

  function tryApply(doc, op, bufferIfStuck) {
    if (op.kind === "insert") {
      if (doc.known.has(key(op.id))) return true;
      if (!canInsert(doc, op)) {
        if (bufferIfStuck) doc.pending.push(op);
        return false;
      }
      integrateYata(doc, op);
      return true;
    }

    if (op.kind === "delete" || op.kind === "revive") {
      const at = indexOf(doc, op.target);
      if (at < 0) {
        if (bufferIfStuck) doc.pending.push(op);
        return false;
      }
      const atom = doc.content[at];
      if (op.kind === "delete" && !atom.isDeleted) {
        atom.isDeleted = true;
        doc.length -= 1;
      }
      if (op.kind === "revive" && atom.isDeleted) {
        atom.isDeleted = false;
        doc.length += 1;
      }
      return true;
    }

    throw new Error(`unknown op ${op.kind}`);
  }

  function drain(doc) {
    let progress;
    do {
      progress = false;
      for (let i = doc.pending.length - 1; i >= 0; i--) {
        if (tryApply(doc, doc.pending[i], false)) {
          doc.pending.splice(i, 1);
          progress = true;
        }
      }
    } while (progress);
  }

  window.OverlockYata = {
    graphemes,
    utf16ToGrapheme(text, utf16) {
      return graphemes(text.slice(0, utf16)).length;
    },
    graphemeToUtf16(text, index) {
      return graphemes(text).slice(0, index).join("").length;
    },
    text(doc) {
      let out = "";
      for (const atom of doc.content) {
        if (!atom.isDeleted) out += atom.content;
      }
      return out;
    },
    create(replicaId) {
      return {
        replicaId,
        content: [],
        version: new Map(),
        known: new Set(),
        pending: [],
        length: 0
      };
    },
    fromSnapshot(replicaId, atoms) {
      const doc = window.OverlockYata.create(replicaId);
      for (const atom of atoms) {
        doc.content.push({
          id: atom.id,
          originLeft: atom.originLeft ?? null,
          originRight: atom.originRight ?? null,
          content: atom.content,
          isDeleted: atom.isDeleted
        });
        doc.known.add(key(atom.id));
        noteSeq(doc, atom.id);
        if (!atom.isDeleted) doc.length += 1;
      }
      return doc;
    },
    apply(doc, op) {
      tryApply(doc, op, true);
      drain(doc);
    },
    insert(doc, index, text) {
      const ops = [];
      let cursor = index;
      for (const g of graphemes(text)) {
        const i = findItemAtPos(doc, cursor);
        const last = doc.version.has(doc.replicaId) ? doc.version.get(doc.replicaId) : -1;
        const op = {
          kind: "insert",
          id: { replica: doc.replicaId, seq: last + 1 },
          originLeft: i > 0 ? doc.content[i - 1].id : null,
          originRight: i < doc.content.length ? doc.content[i].id : null,
          content: g,
          target: null
        };
        window.OverlockYata.apply(doc, op);
        ops.push(op);
        cursor += 1;
      }
      return ops;
    },
    delete(doc, index, count) {
      const ops = [];
      for (let n = 0; n < count; n++) {
        const i = findItemAtPos(doc, index);
        const op = { kind: "delete", id: null, originLeft: null, originRight: null, content: null, target: doc.content[i].id };
        window.OverlockYata.apply(doc, op);
        ops.push(op);
      }
      return ops;
    },
    inverse(op) {
      if (op.kind === "insert") {
        return { kind: "delete", id: null, originLeft: null, originRight: null, content: null, target: op.id };
      }
      if (op.kind === "delete") {
        return { kind: "revive", id: null, originLeft: null, originRight: null, content: null, target: op.target };
      }
      if (op.kind === "revive") {
        return { kind: "delete", id: null, originLeft: null, originRight: null, content: null, target: op.target };
      }
      return null;
    }
  };
})();
