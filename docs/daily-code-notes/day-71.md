# Day 71 — Kod Notları

Faz 4, Hafta 14, Gün 71. Konu: **Outbox pattern** — veritabanı yazması ile event yayınlamayı atomik hâle getirmek. Şu ana kadar `Complete`, "iş emrini tamamla" (veritabanı) ve "event yayınla" (RabbitMQ) işlemlerini **iki ayrı, garantisiz** adım olarak yapıyordu. Bugün bu ikisi tek bir veritabanı işleminde birleştiriliyor.

---

## 1. `src/FieldOps.Modules.WorkOrders/Domain/OutboxMessage.cs` (yeni) — outbox tablosunun kendisi

```csharp
internal class OutboxMessage
{
    public int Id { get; set; }
    public string EventType { get; set; }
// hangi event tipi oldugu -- bu modul bunun ne anlama geldigini HIC bilmiyor, sadece saklıyor
    public string Payload { get; set; }
// event'in JSON'a cevrilmis hali -- yine bu modul icin anlamsiz bir metin, sadece saklıyor
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
// null ise "henuz yayinlanmadi" demek -- OutboxPublisher (host tarafinda) bunu ne zaman
// gercekten RabbitMQ'ya gonderdigini buraya yazacak

    public OutboxMessage(string eventType, string payload)
    {
        EventType = eventType;
        Payload = payload;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
```

**Neden bu şekilde yazıldı:** Bu tablo, **bilerek** `WorkOrderCompletedEvent`'e özel bir sütun (örn. `CustomerId`, `Title`) içermiyor — sadece genel bir `EventType` (string) + `Payload` (JSON string) çifti tutuyor. Bunun sebebi ADR 0002'nin zaten kurduğu kural: bu modülün (`FieldOps.Modules.WorkOrders`), host'un (`FieldOps.Api`) `WorkOrderCompletedEvent` tipine dair **hiçbir referansı olmamalı**. Modül, "bir gün bir event yayınlanacak" bilgisini saklıyor ama o event'in ne olduğunu hiç bilmiyor/bilmesine gerek yok.

---

## 2. `src/FieldOps.Modules.WorkOrders/OutboxMessageSummary.cs` (yeni) — dışa açılan, güvenli şekil

```csharp
public record OutboxMessageSummary(int Id, string EventType, string Payload);
```

**Neden bu şekilde yazıldı:** `WorkOrderSummary`/`WorkOrder` ayrımıyla birebir aynı desen (Day 40'tan beri) — host, modülün `internal OutboxMessage` sınıfını asla görmüyor, sadece bu `public` DTO'yu.

---

## 3. `WorkOrdersDbContext.cs` — yeni tablo, AYNI DbContext

```csharp
public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
// WorkOrders ile TAM OLARAK ayni DbContext, ayni veritabani icinde -- bugunun tum
// mantigi buna dayaniyor: ikisi ayni SaveChanges cagrisiyla, birlikte commit edilecek
```

**Neden bu şekilde yazıldı:** Outbox pattern'ın **tüm gücü** burada gizli — eğer `OutboxMessages` ayrı bir veritabanında (hatta ayrı bir `DbContext`'te bile) olsaydı, iki yazma işlemi arasında yine bir garanti boşluğu olurdu. Aynı `DbContext`, aynı `SaveChanges()` çağrısı = tek, bölünemez bir veritabanı işlemi (transaction).

---

## 4. `EfWorkOrderDirectory.Complete` — asıl atomiklik burada oluyor

```csharp
public WorkOrderSummary? Complete(int workOrderId, string outboxEventType, string outboxPayload)
{
    var workOrder = _dbContext.WorkOrders.FirstOrDefault(w => w.Id == workOrderId);
    if (workOrder is null || workOrder.Status != WorkOrderStatus.InProgress)
    {
        return null;
    }

    workOrder.Status = WorkOrderStatus.Completed;
// 1. degisiklik: is emrinin durumu -- EF Core bunu "izliyor" (change tracking), henuz veritabanina gitmedi

    _dbContext.OutboxMessages.Add(new OutboxMessage(outboxEventType, outboxPayload));
// 2. degisiklik: yeni bir outbox satiri -- bu da sadece EF Core'un hafizasinda, henuz veritabaninda degil

    _dbContext.SaveChanges();
// ISTE BURADA IKISI DE, TEK BIR SQL TRANSACTION'INDA, BIRLIKTE veritabanina yaziliyor --
// ya ikisi de basarili olur, ya da (bir hata olursa) IKISI DE geri alinir. Ara bir durum YOK.
    return ToSummary(workOrder);
}
```

**Neden bu şekilde yazıldı:** Bu, bugünün asıl kod satırı. `workOrder.Status` değişikliği ile `OutboxMessages.Add(...)`, aynı `_dbContext` nesnesinin izlediği iki ayrı değişiklik — `SaveChanges()` çağrıldığında EF Core bunların **hepsini tek bir SQL transaction'ında** gönderiyor. Uygulama bu iki satır arasında çökse bile, ya hiçbiri veritabanına yazılmamış olur (transaction hiç commit olmadı) ya da **ikisi birden** yazılmış olur — asla "iş emri Completed ama outbox satırı yok" durumu oluşamaz.

---

## 5. `IWorkOrderDirectory.cs` — genişletilen sözleşme

```csharp
WorkOrderSummary? Complete(int workOrderId, string outboxEventType, string outboxPayload);
// Complete artik iki string daha istiyor -- host bunlari saglamak zorunda

IReadOnlyList<OutboxMessageSummary> GetUnpublishedOutboxMessages();
// PublishedAtUtc'si null olan satirlari okumak icin -- OutboxPublisher bunu kullanacak

void MarkOutboxMessagePublished(int outboxMessageId);
// bir satiri "artik yayinlandi" olarak isaretlemek icin
```

**Neden bu şekilde yazıldı:** Modülün dışa açık arayüzü, host'un outbox'ı okuyup güncelleyebilmesi için genişletildi — ama hâlâ sadece **genel, event-tipinden-bağımsız** bir sözleşme (string'ler, sayılar). Modül hâlâ "bunlar ne anlama geliyor" sorusuna cevap vermiyor.

---

## 6. `WorkOrdersController.Complete` — artık RabbitMQ'yu hiç bilmiyor

```csharp
var ownershipError = ValidateOwnership(id, organizationId, actingEmployeeId, out var workOrderBeforeCompletion);
// DEGISTI: artik "out _" degil, is emrinin TAMAMLANMADAN ONCEKI halini de aliyoruz --
// Title/CustomerId'e ihtiyacimiz var ve bunlar Complete cagrilmadan once zaten biliniyor
if (ownershipError is not null)
{
    return ownershipError;
}

var eventPayload = JsonSerializer.Serialize(
    new WorkOrderCompletedEvent(id, organizationId!.Value, workOrderBeforeCompletion!.CustomerId, workOrderBeforeCompletion.Title, DateTime.UtcNow));
// event, Complete cagrilmadan ONCE, elimizdeki bilgilerle JSON'a cevriliyor

var updated = _workOrderDirectory.Complete(id, nameof(WorkOrderCompletedEvent), eventPayload);
// artik _eventPublisher.PublishAsync(...) YOK -- sadece bu satir var. RabbitMQ'ya
// hicbir dogrudan cagri kalmadi; event, veritabaniyla BIRLIKTE, atomik olarak yaziliyor
if (updated is null)
{
    return BadRequest($"Work order {id} must be InProgress before it can be completed.");
}

_workOrderReportService.InvalidateCache(organizationId!.Value);
_auditLogWriter.Record(organizationId!.Value, id, "Completed", "Employee", actingEmployeeId!.Value);

return Ok(ToDto(updated));
// action artik async bile degil -- await edilecek hicbir sey kalmadi
```

**Neden bu şekilde yazıldı:** `Complete`, artık `IEventPublisher`'ın var olduğunu bile bilmiyor (constructor'dan da kaldırıldı) — Day 68'de `INotificationSender`'ı kaldırdığımız aynı mantık, bir seviye daha derine indi. `nameof(WorkOrderCompletedEvent)` kullanılması bilinçli: literal bir `"WorkOrderCompletedEvent"` string'i yazmak yerine, tip adı değişirse (yeniden adlandırılırsa) derleyicinin bunu otomatik güncellemesini sağlıyor.

---

## 7. `src/FieldOps.Api/Application/OutboxPublisher.cs` (yeni) — outbox'ı gerçekten okuyup yayınlayan arka plan servisi

```csharp
public class OutboxPublisher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
// Day 50'nin WorkOrderReportCacheWarmer'iyla ayni: periyodik bir dongu

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PublishUnpublishedMessagesAsync(stoppingToken);
// TUM tick tek bir try/catch icinde -- Day 52'nin dersi bastan uygulaniyor
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outbox publishing tick failed");
            }
        }
    }

    private async Task PublishUnpublishedMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
// IWorkOrderDirectory/IEventPublisher Scoped -- Singleton bir BackgroundService'in
// bunlari dogrudan tutamayacagi Day 50'de zaten ogrenilmisti
        var workOrderDirectory = scope.ServiceProvider.GetRequiredService<IWorkOrderDirectory>();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        foreach (var message in workOrderDirectory.GetUnpublishedOutboxMessages())
        {
            try
            {
                if (message.EventType == nameof(WorkOrderCompletedEvent))
                {
                    var domainEvent = JsonSerializer.Deserialize<WorkOrderCompletedEvent>(message.Payload);
// payload, orijinal tipe geri cevriliyor -- artik host, ne oldugunu biliyor
                    if (domainEvent is not null)
                    {
                        await eventPublisher.PublishAsync(domainEvent, cancellationToken);
                        workOrderDirectory.MarkOutboxMessagePublished(message.Id);
// SADECE basariyla yayinlandiktan SONRA isaretleniyor
                    }
                }
                else
                {
                    _logger.LogWarning("Outbox message {OutboxMessageId} has unrecognized EventType {EventType}", message.Id, message.EventType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish outbox message {OutboxMessageId}", message.Id);
// isaretlenmiyor -- bu mesaj outbox'ta KALIYOR, bir sonraki tick'te TEKRAR denenecek
            }
        }
    }
}
```

**Neden bu şekilde yazıldı:** Bu, outbox pattern'ın **asıl vaadini** yerine getiren parça. `MarkOutboxMessagePublished`'in sadece `PublishAsync` **başarılı olduktan sonra** çağrılması kritik — eğer RabbitMQ o an ulaşılamazsa, `catch` bloğu hatayı yakalıyor ama mesajı **işaretlemiyor**, yani bir sonraki 5 saniyelik tick'te aynı mesaj tekrar denenecek. Mesaj, RabbitMQ gerçekten ayağa kalkana kadar sonsuza kadar (ya da en azından uygulama çalıştığı sürece) yeniden denenmeye devam ediyor.

---

## 8. Canlı demonstrasyon — RabbitMQ kapalıyken tamamlama, sonra kurtarma

**Adım 1 — RabbitMQ kapatıldı, bir iş emri tamamlandı:**
```
docker compose stop rabbitmq
POST .../complete → 200 OK (RabbitMQ'ya hiç dokunmadan, HTTP isteği hiç etkilenmedi)
```
Doğrudan SQL sorgusuyla outbox tablosu kontrol edildi:
```sql
SELECT Id, EventType, CreatedAtUtc, PublishedAtUtc FROM OutboxMessages
→ Id=1, EventType=WorkOrderCompletedEvent, PublishedAtUtc = NULL
```
Loglarda `OutboxPublisher`'ın her 5 saniyede bir gerçekten denediği ve başarısız olduğu (RabbitMQ yok) görüldü — ama satır kaybolmadı.

**Adım 2 — RabbitMQ tekrar açıldı, sonra API yeniden başlatıldı:**
```
docker compose start rabbitmq
docker compose restart fieldops-api
```
**Canlı yakalanan, gerçek bir ince nüans:** İlk mesaj (Id=1) `PublishedAtUtc` olarak işaretlendi, ama **hiçbir consumer** onu almadı — çünkü consumer'lar (Day 68/69'un bilinen "yeniden bağlanma yok" sınırlaması nedeniyle) RabbitMQ henüz hazır değilken zaten pes etmişti, kendi kuyruklarını hiç `exchange`'e bağlayamamışlardı. `OutboxPublisher` mesajı exchange'e başarıyla yayınladı (bu yüzden "yayınlandı" işaretlendi), ama **hiçbir kuyruk bağlı olmadığı için** RabbitMQ'nun `fanout` exchange'i mesajı **sessizce kaybetti**. Bu, outbox pattern'ın "event'in yayınlanmasını garanti eder" dediği şeyin, "bir consumer'ın onu gerçekten alacağını garanti eder" demek olmadığının canlı, somut kanıtı — ikisi ayrı garantiler.

**Adım 3 — ikinci bir iş emri, bu sefer her şey sağlıklıyken tamamlandı:**
```
POST .../complete → 200 OK
```
Bu sefer **her iki consumer da** event'i aldı:
```json
{"Category":"FieldOps.Api.Application.LoggingNotificationSender",
 "Message":"Notification: Work order 'Fix the outbox demo 2' has been completed and is awaiting your approval."}
{"Category":"FieldOps.Api.Application.WorkOrderCompletedAuditConsumer",
 "Message":"Audit: work order 2 ('Fix the outbox demo 2') completed at 09/27/2026 13:42:59"}
```
Ve outbox tablosu, iki satırın da `PublishedAtUtc` dolu olduğunu gösterdi.

---

## Regresyon (Day 71)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65 (~8-9 dakika — bu, Day 69'da tespit edilen,
                                       koddan bağımsız Docker/Testcontainers ortam
                                       yavaşlığı, bugünkü değişikliklerle ilgisi yok)
dotnet build StockPilot.slnx  → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx   → 0 Hata, 0 Uyarı
```

## Demo basitleştirmesi vs. üretim gereksinimi

Bugün canlı olarak da doğrulanan gerçek bir sınır: outbox pattern, **event'in kaybolmamasını** garanti ediyor, ama **bir consumer'ın onu mutlaka almasını** garanti etmiyor — bu, consumer'ların kendi bağlanabilirliğine (Day 68/69'un bilinen "yeniden bağlanma yok" eksikliği) bağlı, ayrı bir problem. Tek bir event tipi için basit bir `if` kontrolü kullanıldı (gerçek, genel bir outbox, bir tip kayıt defteri kullanırdı). 5 saniyelik sabit bir yeniden deneme aralığı var — gerçek bir sistemde exponential backoff (Week 14'ün ilerleyen konusu) gerekir.
