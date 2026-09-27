# Day 68 — RabbitMQ Event Akışı: Bir İsteğin Uçtan Uca Yolculuğu

Bu doküman, `day-67.md`/`day-68.md`'nin yanında, tek bir somut örnek üzerinden "istek `Complete`'e nereden girdi, `_eventPublisher.PublishAsync(...)` satırı gerçekte hangi kodu çalıştırıyor, mesaj RabbitMQ'dan nasıl çıkıp tekrar bizim kodumuza giriyor, bildirim en son nerede, hangi satırda gönderiliyor" sorusuna, hiçbir adımı atlamadan cevap veriyor. Aşağıdaki kod parçaları gerçek dosyalardan alınmış.

**Senaryo:** İş emri `id=7`, Org 1'e ait, `CustomerId=3` (bir müşterisi var), `Title="Fix the boiler"`, atanan çalışan `employeeId=2`. O çalışan, kendi işini tamamlıyor:
```
POST /api/workorders/7/complete
X-Organization-Id: 1
X-Employee-Id: 2
```

---

## 0. İstek `WorkOrdersController.Complete`'e ulaşıyor

Day 55'in pipeline'ından (`CorrelationIdMiddleware` → ... → `MapControllers`) geçip `Complete` action'ına geliyor. `id=7`, `organizationId=1`, `actingEmployeeId=2` parametre olarak bağlanıyor.

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
// is emri 7'nin AssignedEmployeeId'si = 2, cagiran da employee 2 -> ownershipError = null, devam

var updated = _workOrderDirectory.Complete(id);
if (updated is null)
{
    return BadRequest($"Work order {id} must be InProgress before it can be completed.");
}
// is emri InProgress durumundaydi -> basariyla Completed'e geciyor
// updated = { Id=7, OrganizationId=1, CustomerId=3, Title="Fix the boiler", Status=Completed, ... }

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
    // yeni event: WorkOrderCompletedEvent(7, 1, 3, "Fix the boiler", 2026-09-27T10:15:00Z)
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to publish WorkOrderCompletedEvent for work order {WorkOrderId}", id);
}
```

`_eventPublisher`'ın **tipi** `IEventPublisher` (bir arayüz) — ama arayüzlerin kendi kod gövdesi yoktur, sadece bir "sözleşme"dir. Bu yüzden soru şu: `.PublishAsync(...)` çağrıldığında **gerçekte hangi sınıfın kodu** çalışıyor? Cevabı bulmak için geriye doğru gidiyoruz.

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
    // buraya HANGİ NESNENİN geleceğine WorkOrdersController hiç karar vermiyor —
    // bu kararı ASP.NET Core'un DI (Dependency Injection) konteyneri veriyor
}
```

### 2b. DI konteyneri bu kararı nereden alıyor — `Program.cs`

`src/FieldOps.Api/Program.cs`:

```csharp
var rabbitMqHostName = builder.Configuration["RabbitMq:HostName"] ?? "localhost";
// appsettings.Development.json'dan "localhost" okunuyor (yerel calistirmada)

builder.Services.AddSingleton<IEventPublisher>(_ => new RabbitMqEventPublisher(rabbitMqHostName));
// ISTE BURADA KARAR VERILIYOR: "birisi IEventPublisher isterse, ona her zaman
// AYNI, tek bir RabbitMqEventPublisher('localhost') nesnesini ver (Singleton)"
```

**Bu satırdan sonra artık kesin olarak biliyoruz:** `WorkOrdersController`'daki `_eventPublisher`, çalışma zamanında gerçekte bir **`RabbitMqEventPublisher`** nesnesidir. `.PublishAsync(...)` çağrısı, o sınıfın kendi metoduna gidiyor.

### 2c. `RabbitMqEventPublisher.PublishAsync<TEvent>` — asıl iş burada oluyor

`src/FieldOps.Api/Application/RabbitMqEventPublisher.cs`:

```csharp
public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
{
// TEvent = WorkOrderCompletedEvent, domainEvent = { Id=7, OrgId=1, CustomerId=3, Title="Fix the boiler", CompletedAtUtc=... }
    var factory = new ConnectionFactory { HostName = _hostName };
// _hostName = "localhost" -> RabbitMQ'ya nereden baglanilacagi belirleniyor
    await using var connection = await factory.CreateConnectionAsync(cancellationToken);
// GERCEK bir TCP baglantisi RabbitMQ sunucusuna aciliyor (ag uzerinden, disariya cikan bir cagri)
    await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
// o baglanti uzerinde bir kanal aciliyor

    var queueName = EventQueueNaming.QueueNameFor<TEvent>();
// EventQueueNaming.cs'teki formul calisiyor: $"fieldops.{typeof(TEvent).Name}"
// TEvent = WorkOrderCompletedEvent oldugu icin -> queueName = "fieldops.WorkOrderCompletedEvent"
    await channel.QueueDeclareAsync(
        queue: queueName, durable: false, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
// RabbitMQ'ya "fieldops.WorkOrderCompletedEvent" adinda bir kuyruk oldugundan emin ol deniyor
// (zaten varsa -consumer uygulama baslarken olusturmustu- hicbir sey yapmiyor)

    var json = JsonSerializer.Serialize(domainEvent);
// domainEvent nesnesi JSON metnine cevriliyor:
// {"WorkOrderId":7,"OrganizationId":1,"CustomerId":3,"Title":"Fix the boiler","CompletedAtUtc":"2026-09-27T10:15:00Z"}
    var body = Encoding.UTF8.GetBytes(json);
// bu JSON metni, RabbitMQ'nun tasiyabilecegi ham bayt dizisine (byte[]) cevriliyor

    await channel.BasicPublishAsync(
        exchange: string.Empty,
        routingKey: queueName,
        mandatory: false,
        basicProperties: new BasicProperties(),
        body: (ReadOnlyMemory<byte>)body,
        cancellationToken: cancellationToken);
// MESAJ GERCEKTEN GONDERILIYOR: bos exchange -> RabbitMQ'nun varsayilan exchange'i
// -> routingKey ("fieldops.WorkOrderCompletedEvent") ile AYNI ISIMDEKI kuyruga dogrudan dusuyor
} // <- await using'ler burada devreye girip connection/channel'i kapatiyor
```

**Bu noktada RabbitMQ sunucusunun kendi içinde:** `"fieldops.WorkOrderCompletedEvent"` adlı kuyrukta, yukarıdaki JSON içerikli **bir mesaj** bekliyor. Bizim .NET uygulamamızın bu mesajla artık hiçbir bağlantısı yok — mesaj artık tamamen RabbitMQ'nun sorumluluğunda.

---

## 3. `Complete`, consumer'ı HİÇ BEKLEMEDEN devam ediyor ve yanıt dönüyor

```csharp
// PublishAsync tamamlandi (await bitti), try blogu sorunsuz kapandi
return Ok(ToDto(updated));
// istemciye 200 OK + iş emri verisi dönüyor — TAM BURADA
```

**Kritik nokta:** İstemci, bu `200 OK` yanıtını, **consumer mesajı işlemeden çok önce** alıyor. `PublishAsync`, mesajı RabbitMQ'ya "bıraktığını" doğruladığı an tamamlanıyor — mesajın ne zaman okunacağını, hatta okunup okunmayacağını hiç beklemiyor. Bu, Day 66'nın "posta kutusuna bırak, git" analojisinin birebir kod karşılığı.

---

## 4. Bambaşka bir zaman çizelgesinde: `WorkOrderCompletedEventConsumer` zaten dinliyordu

Bu kısım, `Complete` isteğiyle **eş zamanlı değil** — uygulama **başladığı andan beri**, tamamen bağımsız çalışıyordu.

`src/FieldOps.Api/Program.cs`:
```csharp
builder.Services.AddHostedService<WorkOrderCompletedEventConsumer>();
// uygulama basladiginda ASP.NET Core bu servisi otomatik calistiriyor
```

`src/FieldOps.Api/Application/WorkOrderCompletedEventConsumer.cs` — uygulama başlarken (`Complete` çağrılmadan çok önce) çalışan kısım:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    // ... baglanti/kanal acildi (RabbitMqEventPublisher'inkinden FARKLI, kendi ayri baglantisi) ...

    var queueName = EventQueueNaming.QueueNameFor<WorkOrderCompletedEvent>();
    // AYNI FORMUL, AYNI SONUC: "fieldops.WorkOrderCompletedEvent"
    await channel.QueueDeclareAsync(queue: queueName, /* ... */);
    // bu kuyrugu ilk defa BU olusturmus olabilir (uygulama ilk kez basliyorsa)

    var consumer = new AsyncEventingBasicConsumer(channel);
    consumer.ReceivedAsync += async (_, ea) => { /* -- asagida adim 5 -- */ };

    await channel.BasicConsumeAsync(queue: queueName, autoAck: false, /* ... */ consumer: consumer, /* ... */);
    // ISTE BURADA: RabbitMQ'ya "bu kuyruga bir mesaj duserse bana haber ver" denildi
    // BU SATIR, Complete cagrilmadan COK ONCE, uygulama baslarken CALISTI

    await Task.Delay(Timeout.Infinite, stoppingToken);
    // ve buradan sonra sonsuza kadar bekliyor — tam da adim 2c'deki mesaj gelene kadar
}
```

---

## 5. RabbitMQ, mesajı bekleyen consumer'a teslim ediyor — `ReceivedAsync` tetikleniyor

Adım 2c'de RabbitMQ'ya bırakılan mesaj, adım 4'te zaten kayıtlı olan bu consumer'a **anında** (mesaj kuyruğa düştüğü an) teslim ediliyor:

```csharp
consumer.ReceivedAsync += async (_, ea) =>
{
    try
    {
        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
        // ea.Body = adim 2c'de gonderilen AYNI baytlar
        // json = {"WorkOrderId":7,"OrganizationId":1,"CustomerId":3,"Title":"Fix the boiler","CompletedAtUtc":"2026-09-27T10:15:00Z"}

        var domainEvent = JsonSerializer.Deserialize<WorkOrderCompletedEvent>(json);
        // domainEvent = yeni bir WorkOrderCompletedEvent nesnesi: { WorkOrderId=7, OrganizationId=1, CustomerId=3, Title="Fix the boiler", CompletedAtUtc=... }
        // DIKKAT: bu, adim 2c'deki orijinal nesneyle AYNI NESNE DEGIL -- JSON uzerinden
        // yeniden olusturulmus, ama ayni veriye sahip, YENI bir nesne

        if (domainEvent?.CustomerId is not null)
        {
            // domainEvent.CustomerId = 3 -> null degil -> bildirim gonderilecek
            await _notificationSender.NotifyAsync(
                $"Work order '{domainEvent.Title}' has been completed and is awaiting your approval.",
                stoppingToken);
            // mesaj: "Work order 'Fix the boiler' has been completed and is awaiting your approval."
        }
    }
    catch (Exception ex) { _logger.LogWarning(ex, "..."); }
    finally
    {
        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        // RabbitMQ'ya "bu mesaji basariyla islendim" bilgisi veriliyor -- adim 5-6 basariyla tamamlandi
    }
};
```

---

## 6. `_notificationSender.NotifyAsync(...)` — bu satır GERÇEKTE ne çalıştırıyor?

Aynı soru, aynı yöntem: `_notificationSender`'ın tipi `INotificationSender` (arayüz) — gerçek sınıf `Program.cs`'te belli oluyor:

```csharp
builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();
```

Yani `_notificationSender.NotifyAsync(...)` çağrısı, gerçekte `LoggingNotificationSender.NotifyAsync`'e gidiyor:

`src/FieldOps.Api/Application/LoggingNotificationSender.cs`:
```csharp
public Task NotifyAsync(string message, CancellationToken cancellationToken)
{
    _logger.LogInformation("Notification: {Message}", message);
    // ISTE BURASI -- BUGUNKU AKISIN SON DURAGI. Loglanan satir:
    // "Notification: Work order 'Fix the boiler' has been completed and is awaiting your approval."
    return Task.CompletedTask;
}
```

Bu, Day 67'de canlı olarak da doğrulanmış, gerçek bir log çıktısı (o gün farklı bir başlık kullanılmıştı, ama mekanizma birebir aynı):
```json
{"Category":"FieldOps.Api.Application.LoggingNotificationSender",
 "Message":"Notification: Work order 'Fix the elevator' has been completed and is awaiting your approval."}
```

---

## Özet — tek bakışta akış (iki ayrı zaman çizelgesi)

```
[ZAMAN ÇİZELGESİ 1 — HTTP isteği, "Complete" isteği geldiğinde başlar, saniyeler içinde biter]

İstemci: POST /api/workorders/7/complete
   │
   ▼
WorkOrdersController.Complete
   │  ValidateMembership / ValidateOwnership -> gecti
   │  _workOrderDirectory.Complete(7) -> Status=Completed (DB'de GERCEKTEN degisti)
   │  _workOrderReportService.InvalidateCache(1)
   │  _auditLogWriter.Record(...)
   │
   ▼
_eventPublisher.PublishAsync(new WorkOrderCompletedEvent(7,1,3,"Fix the boiler",...))
   │  (_eventPublisher = Program.cs'te kayitli RabbitMqEventPublisher('localhost'))
   ▼
RabbitMqEventPublisher.PublishAsync<WorkOrderCompletedEvent>
   │  baglan -> queueName = EventQueueNaming.QueueNameFor<...>() = "fieldops.WorkOrderCompletedEvent"
   │  JSON'a cevir -> RabbitMQ'ya BasicPublishAsync ile gonder
   ▼
[RabbitMQ sunucusu: "fieldops.WorkOrderCompletedEvent" kuyruğuna mesaj DÜŞTÜ]
   │
   ▼
Complete -> return Ok(dto)
   │
   ▼
İstemciye 200 OK dönüyor  <-- ZAMAN ÇİZELGESİ 1 BURADA BİTER, consumer'ı hiç beklemedi


[ZAMAN ÇİZELGESİ 2 — arka planda, uygulama başladığından beri zaten çalışıyordu]

(uygulama basladi)
   │
   ▼
WorkOrderCompletedEventConsumer.ExecuteAsync
   │  baglan -> ayni queueName'i hesapla -> BasicConsumeAsync ile "bu kuyrugu dinliyorum" de
   │  Task.Delay(Infinite) -> sonsuza kadar bekle
   │
   ▼
[RabbitMQ, "fieldops.WorkOrderCompletedEvent" kuyruğuna mesaj düşünce -- Zaman Çizelgesi 1'in son adımı --
 bu mesajı hemen bu consumer'a teslim eder]
   │
   ▼
consumer.ReceivedAsync tetiklenir
   │  JSON'dan WorkOrderCompletedEvent'e geri cevir (YENİ bir nesne, orijinaliyle ayni veri)
   │  domainEvent.CustomerId (=3) null degil -> bildirim gonder
   ▼
_notificationSender.NotifyAsync(...)
   │  (_notificationSender = Program.cs'te kayitli LoggingNotificationSender)
   ▼
LoggingNotificationSender.NotifyAsync
   │  _logger.LogInformation("Notification: Work order 'Fix the boiler' has been completed...")
   ▼
[LOG SATIRI ÜRETİLDİ — akışın gerçek sonu]
   │
   ▼
channel.BasicAckAsync(...) -> RabbitMQ'ya "islendi" denildi
```

**En önemli çıkarım:** Bu iki zaman çizelgesi arasında **hiçbir doğrudan çağrı yok** — aralarındaki tek bağlantı, ikisinin de RabbitMQ'ya aynı ismi (`"fieldops.WorkOrderCompletedEvent"`) söylemesi. `WorkOrdersController`, `WorkOrderCompletedEventConsumer`'ın var olup olmadığını bilmiyor; tersi de doğru. İstemci `200 OK`'i aldığında, bildirim henüz gönderilmemiş bile olabilir — birkaç milisaniye (ya da RabbitMQ o an meşgulse biraz daha uzun) sonra, tamamen ayrı, arka planda gerçekleşiyor.
