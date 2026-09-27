# Day 73 — Kod Notları

Faz 4, Hafta 14, Gün 73. Konu: **Inbox pattern** — consumer'ları idempotent (aynı mesajı iki kez işlemeye dayanıklı) hale getirmek. RabbitMQ **"en az bir kere teslim"** garantisi veriyor, **"tam olarak bir kere"** değil — yani aynı mesaj teorik olarak iki kez teslim edilebilir. Bugün, bu olduğunda bildirimin/audit kaydının **iki kere** oluşmasını önlüyoruz.

---

## 1. `IEventPublisher.cs` / `RabbitMqEventPublisher.cs` — mesaja kalıcı bir kimlik eklendi

```csharp
Task PublishAsync<TEvent>(TEvent domainEvent, string messageId, CancellationToken cancellationToken);
// arayuze yeni bir parametre eklendi: messageId
```

```csharp
await channel.BasicPublishAsync(
    exchange: exchangeName,
    routingKey: string.Empty,
    mandatory: false,
    basicProperties: new BasicProperties { MessageId = messageId },
// RabbitMQ mesajlarinin KENDI, yerlesik bir "MessageId" ozelligi var -- bugune kadar hic
// doldurmamistik. Artik outbox satirinin kendi Id'sini buraya yaziyoruz.
    body: (ReadOnlyMemory<byte>)body,
    cancellationToken: cancellationToken);
```

**Neden bu şekilde yazıldı:** İki mesajın "aynı mesaj mı, farklı mı" olduğunu ayırt edebilmek için önce bir **kimlik** gerekiyor. `OutboxMessage.Id` (Day 71'den beri zaten var, benzersiz, otomatik artan bir sayı) bu iş için mükemmel — yeni bir GUID üretmeye hiç gerek yok, zaten elimizde olan bir kimlik yeniden kullanılıyor.

`OutboxPublisher.cs`'te çağrı:
```csharp
await eventPublisher.PublishAsync(domainEvent, message.Id.ToString(), cancellationToken);
// outbox satirinin Id'si, string'e cevrilip mesajin kimligi olarak taniniyor
```

---

## 2. `ProcessedMessage.cs` (yeni) — Inbox tablosunun kendisi

```csharp
internal class ProcessedMessage
{
    public int Id { get; set; }
    public string ConsumerName { get; set; }
// HANGI consumer isledi -- "notifications" ve "audit" birbirinden bagimsiz kayit tutuyor
    public string MessageId { get; set; }
// HANGI mesaji isledi -- RabbitMqEventPublisher'in yazdigi ayni kimlik
    public DateTime ProcessedAtUtc { get; set; }

    public ProcessedMessage(string consumerName, string messageId)
    {
        ConsumerName = consumerName;
        MessageId = messageId;
        ProcessedAtUtc = DateTime.UtcNow;
    }
}
```

**Neden `ConsumerName` de kimliğin bir parçası:** Eğer sadece `MessageId` saklansaydı, "notifications" consumer'ı bir mesajı işledikten sonra, "audit" consumer'ı **aynı mesajı ilk kez** görmesine rağmen "zaten işlenmiş" sanıp atlardı — bu YANLIŞ olurdu, çünkü ikisi tamamen bağımsız, kendi işini yapan ayrı consumer'lar. `(ConsumerName, MessageId)` ikilisi, "bu SPESİFİK consumer, bu SPESİFİK mesajı işledi mi" sorusuna doğru cevap veriyor.

---

## 3. `WorkOrdersDbContext.cs` — gerçek bir eşsizlik (unique) kısıtlaması

```csharp
modelBuilder.Entity<ProcessedMessage>(entity =>
{
    entity.Property(m => m.ConsumerName).IsRequired().HasMaxLength(200);
    entity.Property(m => m.MessageId).IsRequired().HasMaxLength(200);

    entity.HasIndex(m => new { m.ConsumerName, m.MessageId }).IsUnique();
// ISTE BURASI ONEMLI: veritabaninin KENDISI, ayni (ConsumerName, MessageId) ciftinden
// IKINCI bir satira asla izin vermiyor -- bu sadece bizim kodumuzun "kontrol et, sonra
// ekle" mantigina guvenmiyor, veritabani seviyesinde GARANTI ediyor
});
```

**Neden bu şekilde yazıldı:** Uygulama kodunda "önce var mı diye bak, yoksa ekle" (check-then-insert) mantığı, **iki eş zamanlı** işlem aynı anda çalışırsa (ki bu tür yarış durumları/race condition'lar dağıtık sistemlerde gerçekten olur) yine de iki kayıt eklenebilir. Veritabanının kendi `unique index`'i, bu yarışın **kesin kazananını** belirliyor — kodumuzun yanılma ihtimaline bırakmıyor.

---

## 4. `EfWorkOrderDirectory.cs` — Day 19'un aynı iki katmanlı deseni

```csharp
public bool HasProcessedMessage(string consumerName, string messageId)
{
    return _dbContext.ProcessedMessages.Any(m => m.ConsumerName == consumerName && m.MessageId == messageId);
}
// KATMAN 1: normal, beklenen yol -- once kontrol et

public void MarkMessageProcessed(string consumerName, string messageId)
{
    try
    {
        _dbContext.ProcessedMessages.Add(new ProcessedMessage(consumerName, messageId));
        _dbContext.SaveChanges();
    }
    catch (DbUpdateException)
    {
// KATMAN 2: nadir yaris durumu icin guvenlik agi -- Day 19'da SKU'lar icin
// yazdigimiz AYNI iki katmanli savunma, burada mesaj kimlikleri icin
    }
}
```

---

## 5. `IInboxStore.cs` / `WorkOrderInboxStore.cs` (yeni) — `EventConsumerBase`'i genel tutan soyutlama

```csharp
public interface IInboxStore
{
    bool HasProcessed(string consumerName, string messageId);
    void MarkProcessed(string consumerName, string messageId);
}
```
```csharp
public class WorkOrderInboxStore : IInboxStore
{
    private readonly IWorkOrderDirectory _workOrderDirectory;
    // ... IWorkOrderDirectory'nin yeni metotlarina INCE bir kaplama (wrapper) ...
}
```

**Neden ayrı bir arayüz, neden doğrudan `IWorkOrderDirectory` kullanılmadı:** `EventConsumerBase<TEvent>` (Day 69), **her türlü** event tipi için yeniden kullanılabilir olacak şekilde tasarlanmıştı — `WorkOrders` modülüne özel hiçbir şey bilmiyordu. Eğer doğrudan `IWorkOrderDirectory`'ye bağımlı olsaydı, bu genel tasarım bozulurdu. `IInboxStore`, `EventConsumerBase`'in "birisi bana bu mesajın işlenip işlenmediğini söylesin" ihtiyacını, **kimin, nasıl** sakladığından tamamen soyutluyor — bugün bu, `WorkOrders` modülünün veritabanı; yarın farklı bir event tipi için tamamen başka bir depolama olabilir.

---

## 6. `EventConsumerBase.cs` — `ReceivedAsync` içine eklenen kontrol

```csharp
var messageId = ea.BasicProperties.MessageId;
// RabbitMqEventPublisher'in yazdigi kimlik, RabbitMQ'dan GERI okunuyor
using var scope = _scopeFactory.CreateScope();
var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();

if (!string.IsNullOrEmpty(messageId) && inboxStore.HasProcessed(_consumerName, messageId))
{
    Logger.LogInformation("{ConsumerName} skipping already-processed {EventType} message {MessageId}", /* ... */);
// DAHA ONCE islendiyse, HandleAsync'e HIC GIRILMIYOR -- bildirim/audit tekrar tetiklenmiyor
}
else
{
    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
    var domainEvent = JsonSerializer.Deserialize<TEvent>(json);
    if (domainEvent is not null)
    {
        await HandleAsync(domainEvent, stoppingToken);
    }

    if (!string.IsNullOrEmpty(messageId))
    {
        inboxStore.MarkProcessed(_consumerName, messageId);
// SADECE HandleAsync basariyla bittikten SONRA isaretleniyor
    }
}
```

**Neden `IServiceScopeFactory` üzerinden `scope` açılıyor:** `EventConsumerBase`, bir `BackgroundService` olduğu için **Singleton** yaşam süresine sahip. `IInboxStore`'un tek implementasyonu ise `IWorkOrderDirectory`'ye (Scoped bir servis) bağımlı. Day 50'den beri tekrarlanan aynı kural: bir Singleton, Scoped bir servisi doğrudan tutamaz — bu yüzden her mesaj için **taze bir scope** açılıyor.

---

## 7. Canlı demonstrasyon — aynı mesajı iki kez "teslim etmek"

Gerçek bir RabbitMQ yeniden-teslimini tetiklemek yerine (ki bu, bağlantı kesintisi gibi kontrol edilmesi zor bir durum gerektirirdi), **aynı sonucu** doğuran, daha basit bir canlı deney yapıldı: bir iş emri normal şekilde tamamlandı (her iki consumer da doğru işledi), sonra outbox satırı **elle** `PublishedAtUtc = NULL` yapılarak "sanki hiç yayınlanmamış gibi" işaretlendi:
```sql
UPDATE OutboxMessages SET PublishedAtUtc = NULL WHERE Id = 1
```
`OutboxPublisher`'ın bir sonraki tick'i bu satırı **tekrar** buldu ve **aynı `MessageId` ("1") ile** tekrar RabbitMQ'ya yayınladı — tam olarak bir "yeniden teslim" senaryosunun eşdeğeri. Sonuç, loglarda net şekilde görüldü:
```json
{"Category":"...WorkOrderCompletedEventConsumer","Message":"notifications skipping already-processed WorkOrderCompletedEvent message 1"}
{"Category":"...WorkOrderCompletedAuditConsumer","Message":"audit skipping already-processed WorkOrderCompletedEvent message 1"}
```
Ve en önemlisi: gerçek bildirim (`LoggingNotificationSender`) ve audit log satırları, tüm işlem boyunca **tam olarak bir kez** üretildi (ikinci teslimde tekrarlanmadı) — `docker compose logs` üzerinde bu satırların sayısı elle sayılarak doğrulandı (`1` ve `1`).

---

## Regresyon (Day 73)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65 (~8.5dk, bilinen ortam yavaşlığı, yeni bir regresyon yok)
dotnet build StockPilot.slnx  → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx   → 0 Hata, 0 Uyarı
```

## Demo basitleştirmesi vs. üretim gereksinimi

`ProcessedMessages` tablosu süresiz büyüyecek — gerçek bir sistemde eski kayıtların bir noktada temizlenmesi (retention policy) gerekir, bugünün kapsamı dışında. Dead-letter queue (kalıcı olarak işlenemeyen mesajlar için ayrı bir kuyruk) hâlâ ele alınmadı — Week 14'ün son roadmap konusu.
