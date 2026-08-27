# Overlock

**Conflict-free live collaboration for .NET**

Overlock, birden fazla kullanıcının aynı belge üzerinde eşzamanlı olarak çalışmasını sağlayan, çakışmasız (CRDT tabanlı) bir işbirliği motorudur. Google Docs benzeri gerçek zamanlı senkronizasyon ile, kimse kayıp yaşamadan editör paylaşılabilir.

## 🎯 Özellikler

- **CRDT (Conflict-free Replicated Data Types)**: Last-Write-Wins algoritması ile çakışma çözümü
- **SignalR WebSocket**: Gerçek zamanlı, düşük gecikmeli iletişim
- **Presence & Cursors**: Aktif kullanıcıları görme (temel destek)
- **In-Memory Storage**: Hızlı prototipleme için bellek tabanlı depolama
- **.NET 10.0**: Modern .NET teknolojileri

## 📁 Proje Yapısı

```
Overlock/
├── src/
│   ├── Overlock.Core/          # CRDT implementasyonu, Document yönetimi
│   ├── Overlock.Shared/         # DTO'lar, paylaşılan modeller
│   └── Overlock.Server/         # SignalR hub, WebSocket server
├── examples/
│   └── Overlock.Demo.Api/       # Demo API ve HTML client
└── README.md
```

## 🚀 Hızlı Başlangıç

### Gereksinimler

- .NET 10.0 SDK veya üzeri

### Kurulum

```bash
# Repository'yi klonlayın
git clone https://github.com/mertaydin10/OverLock.git
cd OverLock

# Projeyi derleyin
dotnet build

# Server'ı çalıştırın (Overlock.Server)
cd src/Overlock.Server
dotnet run

# Başka bir terminal'de Demo API'yi çalıştırın
cd examples/Overlock.Demo.Api
dotnet run
```

Demo client'a tarayıcıdan `http://localhost:5000` adresinden erişin.

## 🔧 Nasıl Çalışır?

### CRDT Modeli

Overlock, **Last-Write-Wins (LWW)** CRDT yapısı kullanır:

1. Her değişiklik bir **Timestamp** (Lamport clock + ClientId) ile etiketlenir
2. Çakışma durumunda en yüksek timestamp kazanır
3. Tüm client'lar aynı sırayla işlem gördüğünde aynı son duruma ulaşır

### SignalR Hub API

```csharp
// Document'e katıl
await connection.invoke("JoinDocument", documentId, clientId, presence);

// Operasyonları broadcast et
await connection.invoke("BroadcastOperations", {
    documentId: "doc-1",
    operations: [
        { type: "set", field: "title", value: "Yeni Başlık", ... }
    ]
});

// Cursor güncelle
await connection.invoke("UpdateCursor", documentId, { clientId, field, position });
```

## 📚 Örnek Kullanım

### C# Client

```csharp
using Overlock.Core.Document;

var store = new DocumentStore();
var doc = store.GetOrCreate("doc-1", "client-1");

// Veri yaz
doc.Set("title", "Merhaba Dünya");
doc.Set("content", "Bu bir test");

// Veri oku
doc.TryGetValue("title", out var title);

// Tüm veriyi al
var allData = doc.GetAll();
```

### JavaScript Client

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("http://localhost:5001/collab")
    .build();

await connection.start();
await connection.invoke("JoinDocument", "doc-1", "user-123");

connection.on("OperationsReceived", (batch) => {
    batch.operations.forEach(op => {
        console.log(`${op.field} = ${op.value}`);
    });
});
```

## 🛠️ Teknoloji Stack

- **.NET 10.0**: Core framework
- **ASP.NET Core**: Web API ve SignalR host
- **SignalR**: WebSocket iletişim
- **System.Collections.Concurrent**: Thread-safe koleksiyonlar

## 🚧 Gelecek Özellikler

- [ ] Operational Transform (OT) desteği
- [ ] Rich text editing (Quill, ProseMirror entegrasyonu)
- [ ] Redis / PostgreSQL persistence
- [ ] Yjs/Automerge uyumlu protokol
- [ ] Text-based CRDT (character-wise editing)
- [ ] Undo/Redo mekanizması
- [ ] Presence avatarları ve cursor renkleri
- [ ] Document versiyonlama

## 📖 Mimari Notlar

### Timestamp Karşılaştırma

```csharp
public readonly record struct Timestamp(long Counter, string ClientId) : IComparable<Timestamp>
{
    public int CompareTo(Timestamp other)
    {
        // Önce counter, eşitse ClientId (deterministic)
        var counterComparison = Counter.CompareTo(other.Counter);
        return counterComparison != 0 ? counterComparison 
            : string.Compare(ClientId, other.ClientId, StringComparison.Ordinal);
    }
}
```

### LWW Map

```csharp
public class LwwMap : ICrdt
{
    private readonly ConcurrentDictionary<string, (object? Value, Timestamp Timestamp)> _map;

    public void Set(string key, object? value, Timestamp timestamp)
    {
        _map.AddOrUpdate(key, _ => (value, timestamp),
            (_, existing) => timestamp > existing.Timestamp ? (value, timestamp) : existing);
    }
}
```

## 🤝 Katkıda Bulunma

Pull request'ler memnuniyetle karşılanır! Büyük değişiklikler için önce bir issue açarak tartışalım.

## 📄 Lisans

MIT

## 👨‍💻 Geliştirici

[@mertaydin10](https://github.com/mertaydin10)

---

**Not**: Bu proje eğitim amaçlıdır. Production kullanımı için daha kapsamlı testler, güvenlik önlemleri ve persistence katmanı eklenmelidir.
