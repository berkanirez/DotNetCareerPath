# Day 69 — RabbitMQ Fanout Exchange Akışı: Bir İsteğin İki Bağımsız Tüketiciye Ulaşması

Bu doküman, `day-68-rabbitmq-akis-detay.md` ile **birebir aynı formatta**, Day 69'un yeni akışını (tek kuyruk yerine gerçek bir `fanout` exchange, ve artık İKİ bağımsız consumer) tek bir somut örnek üzerinden, hiçbir adımı atlamadan gösteriyor. Aşağıdaki kod parçaları gerçek dosyalardan alınmış.

**Senaryo:** İş emri `id=9`, Org 1'e ait, `CustomerId=5` (bir müşterisi var), `Title="Fix the generator"`, atanan çalışan `employeeId=2`. O çalışan, kendi işini tamamlıyor:
```
POST /api/workorders/9/complete
X-Organization-Id: 1
X-Employee-Id: 2
```

---

## 0. İstek `WorkOrdersController.Complete`'e ulaşıyor

Day 55'in pipeline'ından geçip `Complete` action'ına geliyor. `id=9`, `organizationId=1`, `actingEmployeeId=2` parametre olarak bağlanıyor.

---

## 1. `WorkOrdersController.Complete` — kontroller ve gerçek durum değişikliği

`src/FieldOps.Api/Controllers/WorkOrdersController.cs`:

```csharp
var membershipError = ValidateMembership(organizationId, actingEmployeeId);
if (membershipError is not null)
{
    return membershipError;
}
// organizationId=1, actingEmployeeId=2 -> employee 2 gercekten Org 1'de -> membershipError = null, devam

var ownershipError = ValidateOwnership(id, organizationId, actingEmployeeId, out _);
if (ownershipError is not null)
{
    return ownershipError;
}
// is emri 9'un AssignedEmployeeId'si = 2, cagiran da employee 2 -> ownershipError = null, devam

var updated = _workOrderDirectory.Complete(id);
if (updated is null)
{
    return BadRequest($"Work order {id} must be InProgress before it can be completed.");
}
// is emri InProgress durumundaydi -> basariyla Completed'e geciyor
// updated = { Id=9, OrganizationId=1, CustomerId=5, Title="Fix the generator", Status=Completed, ... }

_workOrderReportService.InvalidateCache(organizationId!.Value);
// Redis'teki Org 1'in rapor cache'i siliniyor (Day 48/49)
_auditLogWriter.Record(organizationId!.Value, id, "Completed", "Employee", actingEmployeeId!.Value);
// AuditLogs veritabanina bir satir ekleniyor (Day 52), sessiz, log yok
```

**Bu noktada durum:** İş emri veritabanında zaten gerçekten `Completed`. Bir sonraki satır, bugünün asıl konusu.

---

## 2. `await _eventPublisher.PublishAsync(...)` — bu satır GERÇEKTE ne çalıştırıyor?

```csharp
try
{
    await _eventPublisher.PublishAsync(
        new WorkOrderCompletedEvent(updated.Id, updated.OrganizationId, updated.CustomerId, updated.Title, DateTime.UtcNow),
        cancellationToken);
    // yeni event: WorkOrderCompletedEvent(9, 1, 5, "Fix the generator", 2026-09-27T11:00:00Z)
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to publish WorkOrderCompletedEvent for work order {WorkOrderId}", id);
}
```

`_eventPublisher`'ın **tipi** `IEventPublisher` (bir arayüz). Day 68'deki aynı yöntemle geriye doğru gidiyoruz.

### 2a. `_eventPublisher` nereden geliyor — `WorkOrdersController`'ın kendi constructor'ı

```csharp
private readonly IEventPublisher _eventPublisher;
// ...
public WorkOrdersController(
    /* ...diğer parametreler... */
    IEventPublisher eventPublisher,
    ILogger<WorkOrdersController> logger)
{
    // ...
    _eventPublisher = eventPublisher;
}
```

### 2b. DI konteyneri bu kararı nereden alıyor — `Program.cs`

```csharp
var rabbitMqHostName = builder.Configuration["RabbitMq:HostName"] ?? "localhost";
builder.Services.AddSingleton<IEventPublisher>(_ => new RabbitMqEventPublisher(rabbitMqHostName));
// ISTE BURADA KARAR VERILIYOR: IEventPublisher istenirse, her zaman AYNI RabbitMqEventPublisher('localhost') nesnesi verilecek
```

### 2c. `RabbitMqEventPublisher.PublishAsync<TEvent>` — artık kuyruğa değil, EXCHANGE'e yayınlıyor

`src/FieldOps.Api/Application/RabbitMqEventPublisher.cs`:

```csharp
public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
{
// TEvent = WorkOrderCompletedEvent, domainEvent = { Id=9, OrgId=1, CustomerId=5, Title="Fix the generator", CompletedAtUtc=... }
    var factory = new ConnectionFactory { HostName = _hostName };
    await using var connection = await factory.CreateConnectionAsync(cancellationToken);
    await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
// baglanti + kanal aciliyor (Day 67'den beri ayni)

    var exchangeName = EventQueueNaming.ExchangeNameFor<TEvent>();
// EventQueueNaming.cs'teki formul calisiyor: $"fieldops.events.{typeof(TEvent).Name}"
// -> exchangeName = "fieldops.events.WorkOrderCompletedEvent"
    await channel.ExchangeDeclareAsync(
        exchange: exchangeName, type: ExchangeType.Fanout, durable: false, autoDelete: false, cancellationToken: cancellationToken);
// RabbitMQ'ya "bu isimde, FANOUT tipinde bir exchange oldugundan emin ol" deniyor
// (consumer'lar zaten uygulama baslarken olusturmus olabilir -- idempotent)

    var json = JsonSerializer.Serialize(domainEvent);
// {"WorkOrderId":9,"OrganizationId":1,"CustomerId":5,"Title":"Fix the generator","CompletedAtUtc":"2026-09-27T11:00:00Z"}
    var body = Encoding.UTF8.GetBytes(json);

    await channel.BasicPublishAsync(
        exchange: exchangeName,
        routingKey: string.Empty,
        mandatory: false,
        basicProperties: new BasicProperties(),
        body: (ReadOnlyMemory<byte>)body,
        cancellationToken: cancellationToken);
// MESAJ GERCEKTEN GONDERILIYOR -- ama bu sefer DOGRUDAN bir kuyruga degil, "fieldops.events.WorkOrderCompletedEvent"
// EXCHANGE'INE. routingKey bos -- fanout exchange zaten routing key'e hic bakmiyor.
} // <- connection/channel kapatiliyor
```

**Bu noktada RabbitMQ sunucusunun kendi içinde:** `"fieldops.events.WorkOrderCompletedEvent"` exchange'i, kendisine **bağlı (bound) her kuyruğa** bu mesajın bir kopyasını **anında** dağıtıyor. Kaç kuyruk bağlı olduğunu (bugün: 2) yayıncı hiç bilmiyor, hiç umursamıyor.

---

## 3. `Complete`, hiçbir consumer'ı beklemeden devam ediyor ve yanıt dönüyor

```csharp
return Ok(ToDto(updated));
// istemciye 200 OK + iş emri verisi dönüyor — TAM BURADA, aşağıdaki adım 4/5'ten ÖNCE
```

İstemci, iki consumer'dan **hiçbiri** mesajı işlemeden bu yanıtı alıyor.

---

## 4. Bambaşka İKİ ayrı zaman çizelgesinde: iki consumer da zaten dinliyordu

Bu kısım, `Complete` isteğiyle eş zamanlı değil — uygulama **başladığı andan beri**, birbirinden de habersiz, iki ayrı akış olarak çalışıyordu.

`src/FieldOps.Api/Program.cs`:
```csharp
builder.Services.AddHostedService<WorkOrderCompletedEventConsumer>();
builder.Services.AddHostedService<WorkOrderCompletedAuditConsumer>();
// ASP.NET Core uygulama basladiginda IKISINI DE otomatik, birbirinden bagimsiz baslatiyor
```

Her ikisi de, `src/FieldOps.Api/Application/EventConsumerBase.cs`'teki **aynı** `ExecuteAsync`'i (kendi `_consumerName`'leriyle) çalıştırıyor:

```csharp
var exchangeName = EventQueueNaming.ExchangeNameFor<TEvent>();
// ikisi de AYNI formulu cagiriyor -> "fieldops.events.WorkOrderCompletedEvent"

var queueName = EventQueueNaming.QueueNameFor<TEvent>(_consumerName);
// WorkOrderCompletedEventConsumer icin: "fieldops.events.WorkOrderCompletedEvent.notifications"
// WorkOrderCompletedAuditConsumer icin: "fieldops.events.WorkOrderCompletedEvent.audit"
// -- ISTE BURADA IKISI AYRILIYOR: her biri KENDI, FARKLI kuyruk adini hesapliyor

await channel.QueueDeclareAsync(queue: queueName, /* ... */);
// her biri KENDI kuyrugunu aciyor (birbirinin kuyrugundan habersiz)

await channel.QueueBindAsync(queue: queueName, exchange: exchangeName, routingKey: string.Empty, /* ... */);
// ISTE BURASI KRITIK: her biri KENDI kuyrugunu, AYNI exchange'e BAGLIYOR (binding)
// Bu satir olmasaydi, exchange'e dusen mesajlar bu kuyruklara hic ulasmazdi.

await channel.BasicConsumeAsync(queue: queueName, /* ... */ consumer: consumer, /* ... */);
// her biri KENDI kuyrugunu dinlemeye basliyor

await Task.Delay(Timeout.Infinite, stoppingToken);
// ve ikisi de, birbirinden bagimsiz, sonsuza kadar bekliyor
```

**Bu noktada RabbitMQ'nun kendi içinde:**
```
"fieldops.events.WorkOrderCompletedEvent" (exchange, fanout)
        │
        ├── bound ──> "fieldops.events.WorkOrderCompletedEvent.notifications" (WorkOrderCompletedEventConsumer dinliyor)
        └── bound ──> "fieldops.events.WorkOrderCompletedEvent.audit"         (WorkOrderCompletedAuditConsumer dinliyor)
```

---

## 5. RabbitMQ, mesajı İKİ kuyruğa da kopyalıyor — İKİ `ReceivedAsync` de tetikleniyor

Adım 2c'de exchange'e bırakılan mesaj, adım 4'te zaten bağlı olan **her iki kuyruğa da bir kopya** olarak düşüyor — bu da **her iki consumer'ın** `ReceivedAsync`'ini, birbirinden bağımsız olarak tetikliyor:

```csharp
consumer.ReceivedAsync += async (_, ea) =>
{
    try
    {
        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
        // HER IKI consumer icin de: ayni bytes, kendi kuyruklarina dusen KENDI kopyalari
        var domainEvent = JsonSerializer.Deserialize<TEvent>(json);
        // her ikisi de kendi WorkOrderCompletedEvent nesnesini olusturuyor (ayni veri, farkli nesne)
        if (domainEvent is not null)
        {
            await HandleAsync(domainEvent, stoppingToken);
            // ISTE BURADA IKISI AYRILIYOR -- her somut sinifin KENDI HandleAsync'i cagriliyor
        }
    }
    catch (Exception ex) { Logger.LogWarning(ex, "..."); }
    finally
    {
        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        // her biri KENDI kanalinda, KENDI mesajini ack ediyor -- birbirinden bagimsiz
    }
};
```

---

## 5a. `WorkOrderCompletedEventConsumer.HandleAsync` — bildirim yolu

`src/FieldOps.Api/Application/WorkOrderCompletedEventConsumer.cs`:

```csharp
protected override async Task HandleAsync(WorkOrderCompletedEvent domainEvent, CancellationToken cancellationToken)
{
    if (domainEvent.CustomerId is not null)
    {
        // domainEvent.CustomerId = 5 -> null degil -> bildirim gonderilecek
        await _notificationSender.NotifyAsync(
            $"Work order '{domainEvent.Title}' has been completed and is awaiting your approval.",
            cancellationToken);
        // mesaj: "Work order 'Fix the generator' has been completed and is awaiting your approval."
    }
}
```

`_notificationSender`'ın gerçek tipi, Day 68'deki gibi `Program.cs`'ten geliyor:
```csharp
builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();
```

`src/FieldOps.Api/Application/LoggingNotificationSender.cs`:
```csharp
public Task NotifyAsync(string message, CancellationToken cancellationToken)
{
    _logger.LogInformation("Notification: {Message}", message);
    // ISTE BURASI -- "notifications" consumer'inin akisinin sonu
    return Task.CompletedTask;
}
```

---

## 5b. `WorkOrderCompletedAuditConsumer.HandleAsync` — audit yolu (TAMAMEN AYRI, AYNI ANDA)

`src/FieldOps.Api/Application/WorkOrderCompletedAuditConsumer.cs`:

```csharp
protected override Task HandleAsync(WorkOrderCompletedEvent domainEvent, CancellationToken cancellationToken)
{
    Logger.LogInformation(
        "Audit: work order {WorkOrderId} ('{Title}') completed at {CompletedAtUtc}",
        domainEvent.WorkOrderId, domainEvent.Title, domainEvent.CompletedAtUtc);
    // ISTE BURASI -- "audit" consumer'inin akisinin sonu. Bildirim yoluyla HICBIR ILISKISI YOK,
    // INotificationSender'i hic bilmiyor, kendi Logger'ina yaziyor.
    return Task.CompletedTask;
}
```

Gerçekten, canlı olarak yakalanan çıktı (bugünkü doğrulamadan, uydurma değil — farklı bir iş emri başlığıyla ama mekanizma birebir aynı):
```json
{"Category":"FieldOps.Api.Application.LoggingNotificationSender",
 "Message":"Notification: Work order 'Fix the wiring' has been completed and is awaiting your approval."}
{"Category":"FieldOps.Api.Application.WorkOrderCompletedAuditConsumer",
 "Message":"Audit: work order 1 ('Fix the wiring') completed at 09/27/2026 12:20:07"}
```

---

## Özet — tek bakışta akış (ÜÇ ayrı zaman çizelgesi)

```
[ZAMAN ÇİZELGESİ 1 — HTTP isteği, saniyeler içinde biter]

İstemci: POST /api/workorders/9/complete
   │
   ▼
WorkOrdersController.Complete
   │  ValidateMembership / ValidateOwnership -> gecti
   │  _workOrderDirectory.Complete(9) -> Status=Completed
   │  _workOrderReportService.InvalidateCache(1) / _auditLogWriter.Record(...)
   │
   ▼
_eventPublisher.PublishAsync(new WorkOrderCompletedEvent(9,1,5,"Fix the generator",...))
   ▼
RabbitMqEventPublisher.PublishAsync<WorkOrderCompletedEvent>
   │  exchangeName = "fieldops.events.WorkOrderCompletedEvent"
   │  ExchangeDeclareAsync (fanout) -> BasicPublishAsync(exchange: exchangeName, routingKey: "")
   ▼
[RabbitMQ: exchange, kendisine bagli HER kuyruga (notifications VE audit) bir kopya dagitiyor]
   │
   ▼
Complete -> return Ok(dto) -> İstemciye 200 OK  <-- BURADA BİTER, hiçbir consumer'ı beklemedi


[ZAMAN ÇİZELGESİ 2 — "notifications" consumer, uygulama başladığından beri çalışıyordu]

WorkOrderCompletedEventConsumer.ExecuteAsync (EventConsumerBase icinde)
   │  kendi kuyrugu: "...WorkOrderCompletedEvent.notifications", exchange'e bound
   │  BasicConsumeAsync -> Task.Delay(Infinite) -> bekliyordu
   ▼
[RabbitMQ mesaji bu kuyruga da kopyaladi -- Zaman Cizelgesi 1'in exchange adimindan]
   ▼
ReceivedAsync tetiklenir -> HandleAsync (WorkOrderCompletedEventConsumer'daki override)
   │  CustomerId (=5) null degil -> _notificationSender.NotifyAsync(...)
   ▼
LoggingNotificationSender.NotifyAsync -> LOG: "Notification: Work order 'Fix the generator'..."
   ▼
BasicAckAsync -> "notifications" kuyrugundaki bu mesaj islendi


[ZAMAN ÇİZELGESİ 3 — "audit" consumer, TAMAMEN BAĞIMSIZ, AYNI ANDA]

WorkOrderCompletedAuditConsumer.ExecuteAsync (AYNI EventConsumerBase, FARKLI _consumerName)
   │  kendi kuyrugu: "...WorkOrderCompletedEvent.audit", AYNI exchange'e bound
   │  BasicConsumeAsync -> Task.Delay(Infinite) -> bekliyordu
   ▼
[RabbitMQ mesaji bu kuyruga da, Zaman Cizelgesi 2'den TAMAMEN BAGIMSIZ olarak kopyaladi]
   ▼
ReceivedAsync tetiklenir -> HandleAsync (WorkOrderCompletedAuditConsumer'daki override)
   │  INotificationSender'i hic cagirmiyor
   ▼
Logger.LogInformation -> LOG: "Audit: work order 9 ('Fix the generator') completed at ..."
   ▼
BasicAckAsync -> "audit" kuyrugundaki bu mesaj islendi
```

**En önemli çıkarım:** Day 68'de sadece iki zaman çizelgesi vardı (HTTP isteği + tek consumer). Bugün **üç** zaman çizelgesi var — ikinci consumer eklenmesi, `WorkOrdersController`'da veya `RabbitMqEventPublisher`'da **hiçbir değişiklik gerektirmedi**; sadece exchange'e yeni bir kuyruk daha bağlandı. Bu, fanout exchange'in asıl gücü: yayıncı tarafı hiç değişmeden, tüketici tarafı istendiği kadar bağımsız kola çoğaltılabiliyor.
