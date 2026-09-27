# Day 71 — Outbox Pattern + RabbitMQ Akışı: Bir İsteğin Dört Ayrı Zaman Çizelgesine Yayılması

Bu doküman, `day-68-rabbitmq-akis-detay.md` ve `day-69-rabbitmq-akis-detay.md` ile **birebir aynı formatta**, ama bugün Outbox pattern eklendiği için akış **köklü şekilde değişti**. Her arayüz çağrısında, "bu metot gerçekte hangi sınıfa gidiyor" sorusu `Program.cs`/`WorkOrdersModule.cs`'teki DI kaydına bakılarak **adım adım** takip ediliyor — hiçbir yer atlanmıyor.

**Senaryo:** İş emri `id=12`, Org 1'e ait, `CustomerId=7`, `Title="Fix the pump"`, atanan çalışan `employeeId=2`. O çalışan, kendi işini tamamlıyor:
```
POST /api/workorders/12/complete
X-Organization-Id: 1
X-Employee-Id: 2
```

**En baştan söyleyelim — bugün DÖRT ayrı zaman çizelgesi var:**
1. HTTP isteğinin kendisi (saniyeler içinde biter)
2. `OutboxPublisher` — veritabanını periyodik olarak yoklayan arka plan döngüsü
3. `WorkOrderCompletedEventConsumer` ("notifications") — RabbitMQ'yu dinleyen arka plan döngüsü
4. `WorkOrderCompletedAuditConsumer` ("audit") — RabbitMQ'yu dinleyen başka bir arka plan döngüsü

1. numaralı çizelge, 2. numaralıyı **doğrudan çağırmıyor** — sadece veritabanına bir iz bırakıyor, 2. numaralı çizelge bu izi **kendi zamanında** buluyor. 2. numaralı çizelge de 3/4'ü doğrudan çağırmıyor — RabbitMQ üzerinden, aynı Day 69'daki gibi, dolaylı olarak bağlanıyorlar.

---

## 0. İstek `WorkOrdersController.Complete`'e ulaşıyor

Day 55'in pipeline'ından geçip `Complete` action'ına geliyor. `id=12`, `organizationId=1`, `actingEmployeeId=2` parametre olarak bağlanıyor.

---

## 1. `WorkOrdersController.Complete` — kontroller, event verisinin hazırlanması

`src/FieldOps.Api/Controllers/WorkOrdersController.cs`:

```csharp
var membershipError = ValidateMembership(organizationId, actingEmployeeId);
if (membershipError is not null)
{
    return membershipError;
}
// employee 2 gercekten Org 1'de -> devam

var ownershipError = ValidateOwnership(id, organizationId, actingEmployeeId, out var workOrderBeforeCompletion);
if (ownershipError is not null)
{
    return ownershipError;
}
// workOrderBeforeCompletion = { Id=12, Title="Fix the pump", CustomerId=7, AssignedEmployeeId=2, Status=InProgress, ... }
// -- BU, Complete cagrilmadan ONCEKI hal. Title/CustomerId, Complete calisirken DEGISMEYECEK
// alanlar oldugu icin, event'i hazirlamak icin simdiden yeterli veri var.

var eventPayload = JsonSerializer.Serialize(
    new WorkOrderCompletedEvent(id, organizationId!.Value, workOrderBeforeCompletion!.CustomerId, workOrderBeforeCompletion.Title, DateTime.UtcNow));
// eventPayload = "{\"WorkOrderId\":12,\"OrganizationId\":1,\"CustomerId\":7,\"Title\":\"Fix the pump\",\"CompletedAtUtc\":\"2026-09-27T14:00:00Z\"}"

var updated = _workOrderDirectory.Complete(id, nameof(WorkOrderCompletedEvent), eventPayload);
// nameof(WorkOrderCompletedEvent) = "WorkOrderCompletedEvent" (derleme zamaninda hesaplanan bir string)
```

`_eventPublisher` YOK artık — Day 68'de `_notificationSender`'ın kaldırıldığı gibi, bugün `_eventPublisher` de `WorkOrdersController`'dan tamamen kaldırıldı. Controller, RabbitMQ'nun var olduğunu **hiç bilmiyor**.

---

## 2. `_workOrderDirectory.Complete(...)` — bu satır GERÇEKTE ne çalıştırıyor?

`_workOrderDirectory`'nin **tipi** `IWorkOrderDirectory` (bir arayüz). Geriye doğru gidiyoruz.

### 2a. DI konteyneri hangi sınıfı verdiğine nasıl karar veriyor

`src/FieldOps.Modules.WorkOrders/WorkOrdersModule.cs`:
```csharp
public static IServiceCollection AddWorkOrdersModule(this IServiceCollection services, string connectionString)
{
    services.AddDbContext<WorkOrdersDbContext>(options => options.UseSqlServer(connectionString));
    return services.AddScoped<IWorkOrderDirectory, EfWorkOrderDirectory>();
    // ISTE BURADA: IWorkOrderDirectory istenirse, EfWorkOrderDirectory ornegi verilecek (Scoped -- her HTTP istegi icin yeni)
}
```
Bu metot, `src/FieldOps.Api/Program.cs`'te çağrılıyor:
```csharp
builder.Services.AddWorkOrdersModule(RequireConnectionString("FieldOpsWorkOrdersDb"));
```

**Bu noktada kesin olarak biliyoruz:** `_workOrderDirectory.Complete(...)` çağrısı, gerçekte **`EfWorkOrderDirectory.Complete`**'e gidiyor.

### 2b. `EfWorkOrderDirectory.Complete` — atomik yazmanın gerçekleştiği yer

`src/FieldOps.Modules.WorkOrders/Data/EfWorkOrderDirectory.cs`:
```csharp
public WorkOrderSummary? Complete(int workOrderId, string outboxEventType, string outboxPayload)
{
    var workOrder = _dbContext.WorkOrders.FirstOrDefault(w => w.Id == workOrderId);
    if (workOrder is null || workOrder.Status != WorkOrderStatus.InProgress)
    {
        return null;
    }
    // workOrderId=12 bulundu, Status=InProgress -> devam

    workOrder.Status = WorkOrderStatus.Completed;
    // EF Core'un "change tracker"ina 1. degisiklik kaydedildi -- HENUZ veritabanina gitmedi

    _dbContext.OutboxMessages.Add(new OutboxMessage(outboxEventType, outboxPayload));
    // outboxEventType = "WorkOrderCompletedEvent", outboxPayload = yukaridaki JSON
    // change tracker'a 2. degisiklik (yeni bir satir) kaydedildi -- bu da HENUZ veritabaninda degil

    _dbContext.SaveChanges();
    // ISTE BURADA: IKI degisiklik de, TEK bir SQL transaction'inda, BIRLIKTE veritabanina gonderiliyor.
    // Bu satirdan SONRA: is emri VE outbox satiri, ikisi de gercekten veritabaninda, ya da (bir hata
    // olsaydi) IKISI DE veritabaninda degil -- ara bir durum matematiksel olarak imkansiz.

    return ToSummary(workOrder);
}
```

---

## 3. `Complete`, hiçbir arka plan sürecini beklemeden yanıt dönüyor

```csharp
_workOrderReportService.InvalidateCache(organizationId!.Value);
_auditLogWriter.Record(organizationId!.Value, id, "Completed", "Employee", actingEmployeeId!.Value);

return Ok(ToDto(updated));
// istemciye 200 OK -- TAM BURADA. Ne RabbitMQ'ya baglanildi, ne bir consumer beklendi.
```

**Bu noktada veritabanının içinde:** `OutboxMessages` tablosunda `{ Id: X, EventType: "WorkOrderCompletedEvent", Payload: "...", PublishedAtUtc: NULL }` satırı **gerçekten var** — ama RabbitMQ bundan **henüz hiç haberdar değil**. HTTP isteği bitti; event hâlâ sadece veritabanında bekliyor.

---

## 4. Bambaşka bir zaman çizelgesinde: `OutboxPublisher` zaten çalışıyordu

Bu, uygulama başladığından beri, `Complete`'ten tamamen habersiz çalışan bir döngü.

`src/FieldOps.Api/Program.cs`:
```csharp
builder.Services.AddHostedService<OutboxPublisher>();
```

`src/FieldOps.Api/Application/OutboxPublisher.cs`:
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    using var timer = new PeriodicTimer(Interval); // Interval = 5 saniye
    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
        try
        {
            await PublishUnpublishedMessagesAsync(stoppingToken);
            // HER 5 SANIYEDE BIR bu satir calisiyor -- Complete'in ne zaman cagrildigindan tamamen bagimsiz
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Outbox publishing tick failed"); }
    }
}
```

Bir tick sırasında (adım 3'teki veritabanı yazmasından sonraki ilk 5 saniyelik pencerede):
```csharp
private async Task PublishUnpublishedMessagesAsync(CancellationToken cancellationToken)
{
    using var scope = _scopeFactory.CreateScope();
    var workOrderDirectory = scope.ServiceProvider.GetRequiredService<IWorkOrderDirectory>();
    // ISTE YINE AYNI ARAYUZ -- ve YINE ayni DI kaydi sayesinde, bu da bir EfWorkOrderDirectory ornegi
    // (Scoped oldugu icin BASKA bir ornek, ama AYNI sinif, ayni veritabani)
    var eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
    // bu da asagida adim 5b'de takip edilecek

    foreach (var message in workOrderDirectory.GetUnpublishedOutboxMessages())
    {
        // EfWorkOrderDirectory.GetUnpublishedOutboxMessages() calisiyor:
        //   _dbContext.OutboxMessages.Where(m => m.PublishedAtUtc == null)...
        // -> adim 2b'de yazilan satiri BULUYOR: { Id=X, EventType="WorkOrderCompletedEvent", Payload="..." }
```

---

## 5. `OutboxPublisher`, bulduğu mesajı gerçekten yayınlıyor

```csharp
        try
        {
            if (message.EventType == nameof(WorkOrderCompletedEvent))
            {
                // "WorkOrderCompletedEvent" == "WorkOrderCompletedEvent" -> eslesti
                var domainEvent = JsonSerializer.Deserialize<WorkOrderCompletedEvent>(message.Payload);
                // JSON metni, adim 1'deki ORIJINAL WorkOrderCompletedEvent'in ayni verilerle YENI bir kopyasina donusturuluyor

                if (domainEvent is not null)
                {
                    await eventPublisher.PublishAsync(domainEvent, cancellationToken);
                    // -- ASAGIDA 5a/5b'de takip ediliyor --

                    workOrderDirectory.MarkOutboxMessagePublished(message.Id);
                    // SADECE yukaridaki satir basariyla tamamlandiysa buraya geliniyor
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish outbox message {OutboxMessageId}", message.Id);
            // BURAYA dusulurse MarkOutboxMessagePublished HIC CAGRILMAZ -- mesaj outbox'ta
            // "yayinlanmamis" olarak KALIR, bir sonraki 5 saniyelik tick'te TEKRAR denenecek
        }
```

### 5a. `eventPublisher.PublishAsync(...)` — yine aynı yöntemle geriye gidiyoruz

`Program.cs`:
```csharp
builder.Services.AddSingleton<IEventPublisher>(_ => new RabbitMqEventPublisher(rabbitMqHostName));
```
→ `eventPublisher.PublishAsync(...)` gerçekte **`RabbitMqEventPublisher.PublishAsync`**'e gidiyor.

### 5b. `RabbitMqEventPublisher.PublishAsync<TEvent>` — mesaj RabbitMQ'ya gerçekten gidiyor

`src/FieldOps.Api/Application/RabbitMqEventPublisher.cs`:
```csharp
public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
{
    // TEvent burada WorkOrderCompletedEvent olarak CIKARSANIYOR (OutboxPublisher'in cagrisindan)
    var factory = new ConnectionFactory { HostName = _hostName };
    await using var connection = await factory.CreateConnectionAsync(cancellationToken);
    await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

    var exchangeName = EventQueueNaming.ExchangeNameFor<TEvent>();
    // "fieldops.events.WorkOrderCompletedEvent"
    await channel.ExchangeDeclareAsync(exchange: exchangeName, type: ExchangeType.Fanout, /* ... */);

    var json = JsonSerializer.Serialize(domainEvent);
    // domainEvent, adim 5'te deserialize edilen YENI nesne -- burada TEKRAR JSON'a cevriliyor
    var body = Encoding.UTF8.GetBytes(json);

    await channel.BasicPublishAsync(exchange: exchangeName, routingKey: string.Empty, /* ... */ body: (ReadOnlyMemory<byte>)body, /* ... */);
    // MESAJ SIMDI GERCEKTEN RabbitMQ'ya ULASTI -- adim 3'ten bu yana GECEN SURE: 0-5 saniye arasi
}
```

---

## 6. Bambaşka İKİ ayrı zaman çizelgesinde: iki consumer da zaten dinliyordu

`Program.cs`:
```csharp
builder.Services.AddHostedService<WorkOrderCompletedEventConsumer>();
builder.Services.AddHostedService<WorkOrderCompletedAuditConsumer>();
```

Her ikisi de (`EventConsumerBase<TEvent>.ExecuteAsync` içinde, kendi `_consumerName`'leriyle) **uygulama başladığından beri** kendi kuyruklarını exchange'e bağlamış, `BasicConsumeAsync` ile dinlemeye başlamış durumda — Day 69'un akış dosyasında detaylandırıldığı gibi.

Adım 5b'deki `BasicPublishAsync`, `"fieldops.events.WorkOrderCompletedEvent"` exchange'ine mesajı bıraktığı an, RabbitMQ bu exchange'e bağlı **her iki kuyruğa da** bir kopya dağıtıyor:

```
"fieldops.events.WorkOrderCompletedEvent" (fanout exchange)
    ├── bound ──> "...notifications" kuyruğu → WorkOrderCompletedEventConsumer dinliyor
    └── bound ──> "...audit" kuyruğu         → WorkOrderCompletedAuditConsumer dinliyor
```

---

## 6a. `WorkOrderCompletedEventConsumer.HandleAsync` — bildirim yolu

```csharp
protected override async Task HandleAsync(WorkOrderCompletedEvent domainEvent, CancellationToken cancellationToken)
{
    if (domainEvent.CustomerId is not null)
    {
        // domainEvent.CustomerId = 7 -> null degil
        await _notificationSender.NotifyAsync(
            $"Work order '{domainEvent.Title}' has been completed and is awaiting your approval.",
            cancellationToken);
    }
}
```
`_notificationSender`'ın gerçek tipi, `Program.cs`'teki `AddSingleton<INotificationSender, LoggingNotificationSender>()` kaydından geliyor:
```csharp
public Task NotifyAsync(string message, CancellationToken cancellationToken)
{
    _logger.LogInformation("Notification: {Message}", message);
    // "Notification: Work order 'Fix the pump' has been completed and is awaiting your approval."
    return Task.CompletedTask;
}
```

## 6b. `WorkOrderCompletedAuditConsumer.HandleAsync` — audit yolu (TAMAMEN BAĞIMSIZ, AYNI ANDA)

```csharp
protected override Task HandleAsync(WorkOrderCompletedEvent domainEvent, CancellationToken cancellationToken)
{
    Logger.LogInformation(
        "Audit: work order {WorkOrderId} ('{Title}') completed at {CompletedAtUtc}",
        domainEvent.WorkOrderId, domainEvent.Title, domainEvent.CompletedAtUtc);
    return Task.CompletedTask;
}
```

---

## Özet — tek bakışta akış (DÖRT ayrı zaman çizelgesi, bugünkü canlı ölçümlerle)

```
[ZAMAN ÇİZELGESİ 1 — HTTP isteği, saniyeler içinde biter]

İstemci: POST /api/workorders/12/complete
   ▼
WorkOrdersController.Complete
   │  ValidateOwnership -> workOrderBeforeCompletion (Title/CustomerId onceden biliniyor)
   │  eventPayload = JsonSerializer.Serialize(new WorkOrderCompletedEvent(...))
   ▼
_workOrderDirectory.Complete(id, "WorkOrderCompletedEvent", eventPayload)
   ▼
EfWorkOrderDirectory.Complete
   │  workOrder.Status = Completed          ┐
   │  _dbContext.OutboxMessages.Add(...)    ┤─ TEK SaveChanges() -> ATOMIK
   ▼
Complete -> return Ok(dto) -> İstemciye 200 OK   <-- BURADA BİTER (RabbitMQ'ya hiç dokunmadan)


[ZAMAN ÇİZELGESİ 2 — OutboxPublisher, uygulama başladığından beri, her 5 saniyede bir]

(bekliyordu) ... PeriodicTimer tick ...
   ▼
GetUnpublishedOutboxMessages() -> Zaman Cizelgesi 1'in yazdigi satiri BULUYOR
   ▼
JsonSerializer.Deserialize<WorkOrderCompletedEvent>(message.Payload)
   ▼
eventPublisher.PublishAsync(domainEvent) -> RabbitMqEventPublisher -> RabbitMQ'ya BasicPublishAsync
   │  (canli denemede: RabbitMQ o an kapaliysa BURADA hata olusur, mesaj İŞARETLENMEZ, tekrar denenir)
   ▼
BAŞARILI ise: MarkOutboxMessagePublished(message.Id) -> PublishedAtUtc = now


[ZAMAN ÇİZELGESİ 3 — "notifications" consumer, uygulama başladığından beri]

(bekliyordu) ... RabbitMQ mesaji "...notifications" kuyruguna kopyaladi (Zaman Cizelgesi 2'nin son adimindan) ...
   ▼
ReceivedAsync -> HandleAsync -> INotificationSender.NotifyAsync -> LoggingNotificationSender
   ▼
LOG: "Notification: Work order 'Fix the pump' has been completed..."


[ZAMAN ÇİZELGESİ 4 — "audit" consumer, TAMAMEN BAĞIMSIZ, uygulama başladığından beri]

(bekliyordu) ... RabbitMQ mesaji "...audit" kuyruguna da, AYNI ANDA, AYRICA kopyaladi ...
   ▼
ReceivedAsync -> HandleAsync -> dogrudan Logger.LogInformation
   ▼
LOG: "Audit: work order 12 ('Fix the pump') completed at ..."
```

**En önemli çıkarım — neden kafanız karıştı olabilir:** Önceki günlerde (68/69) `Complete`'in kendisi RabbitMQ'ya **doğrudan** yayınlıyordu — tek bir "el değiştirme" vardı (HTTP → RabbitMQ). Bugün araya **bir el değiştirme daha** girdi: HTTP artık sadece **veritabanına** yazıyor (`OutboxMessages` tablosu), RabbitMQ'ya yayınlamayı **bambaşka, zamanlanmış bir süreç** (`OutboxPublisher`) yapıyor. Yani bugünkü zincir, dünkünden **bir halka daha uzun**: `Complete` → **veritabanı (outbox satırı)** → `OutboxPublisher` (kendi zamanında) → RabbitMQ → iki consumer. Aradaki yeni halka (veritabanı + `OutboxPublisher`), tam olarak "RabbitMQ o an kapalı olsa bile event kaybolmasın" garantisini sağlamak için var.
