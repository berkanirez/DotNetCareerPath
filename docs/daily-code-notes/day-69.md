# Day 69 — Kod Notları

Faz 4, Hafta 13, Gün 69. Konu: **gerçek exchange/routing key tasarımı — aynı event'i birden fazla bağımsız tüketiciye ulaştırmak**. Day 67/68'in tasarımı, tek bir isimli kuyruğa doğrudan yazıyordu — bu, ikinci bir tüketici eklendiğinde mesajların ikisi arasında **paylaştırılmasına** (her mesaj sadece birine gitmesine) yol açardı. Bugün gerçek bir `fanout` exchange kuruluyor ve bunu somut olarak kanıtlamak için **ikinci, bağımsız bir consumer** ekleniyor.

---

## 1. `src/FieldOps.Api/Application/EventQueueNaming.cs` — exchange adı + tüketiciye özel kuyruk adı

```csharp
public static class EventQueueNaming
{
    public static string ExchangeNameFor<TEvent>() => $"fieldops.events.{typeof(TEvent).Name}";
// her event tipi için TEK bir exchange adı — örn. "fieldops.events.WorkOrderCompletedEvent"

    public static string QueueNameFor<TEvent>(string consumerName) =>
        $"{ExchangeNameFor<TEvent>()}.{consumerName}";
// ARTIK parametre alıyor: consumerName. Her tüketici KENDİ, benzersiz kuyruk adını alıyor —
// örn. "fieldops.events.WorkOrderCompletedEvent.notifications" ve "...audit"
}
```

**Neden bu şekilde yazıldı:** Day 68'de `QueueNameFor<TEvent>()` parametresizdi — tek bir consumer olduğu için tek bir kuyruk yeterliydi. Bugün ikinci bir consumer eklenince, ikisinin **aynı kuyruğu paylaşması yanlış olurdu** (RabbitMQ mesajları ikisi arasında bölerdi, ikisine de kopyalamazdı) — bu yüzden her consumer'ın **kendi kuyruğu** olması gerekiyor, ama hepsi **aynı exchange'e** bağlı olmalı ki aynı mesajı alabilsinler.

---

## 2. `src/FieldOps.Api/Application/RabbitMqEventPublisher.cs` — artık kuyruğa değil, exchange'e yayınlıyor

```csharp
var exchangeName = EventQueueNaming.ExchangeNameFor<TEvent>();
// "fieldops.events.WorkOrderCompletedEvent"
await channel.ExchangeDeclareAsync(
    exchange: exchangeName, type: ExchangeType.Fanout, durable: false, autoDelete: false, cancellationToken: cancellationToken);
// bu isimde, FANOUT tipinde bir exchange yoksa oluşturuluyor. Fanout = "routing key'e hiç
// bakmadan, bana bağlı (bound) HER kuyruğa bir kopya gönder" demek

var json = JsonSerializer.Serialize(domainEvent);
var body = Encoding.UTF8.GetBytes(json);
await channel.BasicPublishAsync(
    exchange: exchangeName,
// artık string.Empty (varsayılan exchange) DEĞİL, kendi isimli exchange'imiz
    routingKey: string.Empty,
// fanout exchange routing key'i zaten hiç kullanmıyor, bu yüzden boş bırakılıyor
    mandatory: false,
    basicProperties: new BasicProperties(),
    body: (ReadOnlyMemory<byte>)body,
    cancellationToken: cancellationToken);
```

**Neden bu şekilde yazıldı:** Day 66/67/68 boyunca kullanılan "varsayılan exchange, routing key = kuyruk adı" kısayolu, **tek kuyruk** senaryosu için pratikti ama birden fazla bağımsız tüketiciye dağıtımı desteklemiyordu. Gerçek, isimli bir `fanout` exchange, mesajı **kendisine bağlı her kuyruğa kopyalayarak** gönderiyor — kaç tane consumer/kuyruk bağlı olduğunu yayıncı hiç bilmek zorunda değil.

---

## 3. `src/FieldOps.Api/Application/EventConsumerBase.cs` (yeni) — ortak consumer iskeletinin çıkarılması

```csharp
public abstract class EventConsumerBase<TEvent> : BackgroundService
{
    private readonly string _hostName;
    private readonly string _consumerName;
// her consumer'ın kendi kimliği — kuyruk adının bir parçası olacak (örn. "notifications", "audit")
    protected readonly ILogger Logger;
// alt sınıfların da kullanabilmesi için protected

    protected EventConsumerBase(string hostName, string consumerName, ILogger logger)
    {
        _hostName = hostName;
        _consumerName = consumerName;
        Logger = logger;
    }

    protected abstract Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
// TEK soyut (abstract) metot — her somut consumer'ın FARKLI olan tek şeyi bu: event geldiğinde
// NE YAPILACAĞI. Bağlanma/kuyruk açma/dinleme mekaniği hepsinde birebir aynı, bu yüzden burada.

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IConnection? connection = null;
        IChannel? channel = null;
        try
        {
            var factory = new ConnectionFactory { HostName = _hostName };
            connection = await factory.CreateConnectionAsync(stoppingToken);
            channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

            var exchangeName = EventQueueNaming.ExchangeNameFor<TEvent>();
            await channel.ExchangeDeclareAsync(
                exchange: exchangeName, type: ExchangeType.Fanout, durable: false, autoDelete: false, cancellationToken: stoppingToken);
// yayıncı (RabbitMqEventPublisher) zaten oluşturmuş olabilir, olmasa bile burada oluşuyor —
// hangisi önce çalışırsa çalışsın fark etmiyor (idempotent)

            var queueName = EventQueueNaming.QueueNameFor<TEvent>(_consumerName);
// BU consumer'a ait, BENZERSİZ kuyruk adı — örn. "...WorkOrderCompletedEvent.audit"
            await channel.QueueDeclareAsync(
                queue: queueName, durable: false, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
            await channel.QueueBindAsync(
                queue: queueName, exchange: exchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);
// ISTE BURASI YENI: kuyruk, exchange'e BAĞLANIYOR (binding). Bu satır olmadan kuyruk var olurdu
// ama exchange'e hiç mesaj bırakılsa bile bu kuyruğa hiçbir kopya düşmezdi.

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var domainEvent = JsonSerializer.Deserialize<TEvent>(json);
                    if (domainEvent is not null)
                    {
                        await HandleAsync(domainEvent, stoppingToken);
                    }
// BURADA soyut metot çağrılıyor — hangi somut sınıf olduğuna göre FARKLI kod çalışacak
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "{ConsumerName} failed to process a {EventType} message", _consumerName, typeof(TEvent).Name);
                }
                finally
                {
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                }
            };

            await channel.BasicConsumeAsync(
                queue: queueName, autoAck: false, consumerTag: string.Empty,
                noLocal: false, exclusive: false, arguments: null, consumer: consumer,
                cancellationToken: stoppingToken);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { /* normal kapanma */ }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{ConsumerName} could not connect to RabbitMQ; it will not receive {EventType} messages until the app restarts.", _consumerName, typeof(TEvent).Name);
        }
        finally
        {
            if (channel is not null) await channel.DisposeAsync();
            if (connection is not null) await connection.DisposeAsync();
        }
    }
}
```

**Neden bu şekilde yazıldı:** Day 68'in `WorkOrderCompletedEventConsumer`'ının **tamamı** (bağlan, kuyruk aç, dinle, ack et, hata yönetimi) ikinci bir consumer yazılırken **birebir kopyalanacaktı**. Bu, tam olarak "aynı kodu iki kere yazma" durumu — bu yüzden ortak kısım bir **soyut temel sınıfa (`abstract class`)** çıkarıldı. `TEvent` generic tutuldu ki ileride farklı bir event tipi için de bu temel sınıf yeniden kullanılabilsin.

---

## 4. `WorkOrderCompletedEventConsumer.cs` — artık sadece "ne yapılacağını" söylüyor

```csharp
public class WorkOrderCompletedEventConsumer : EventConsumerBase<WorkOrderCompletedEvent>
{
    private readonly INotificationSender _notificationSender;

    public WorkOrderCompletedEventConsumer(
        IConfiguration configuration,
        INotificationSender notificationSender,
        ILogger<WorkOrderCompletedEventConsumer> logger)
        : base(configuration["RabbitMq:HostName"] ?? "localhost", "notifications", logger)
// "notifications" -> bu consumer'ın kuyruk adı bu isimle bitecek
    {
        _notificationSender = notificationSender;
    }

    protected override async Task HandleAsync(WorkOrderCompletedEvent domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.CustomerId is not null)
        {
            await _notificationSender.NotifyAsync(
                $"Work order '{domainEvent.Title}' has been completed and is awaiting your approval.",
                cancellationToken);
        }
    }
}
```

**Neden bu şekilde yazıldı:** Day 68'deki bağlantı/kuyruk/dinleme kodunun **tamamı** kayboldu — hepsi `EventConsumerBase`'de. Geriye sadece "ben kimim" (`"notifications"`) ve "mesaj gelince ne yaparım" (`HandleAsync`) kaldı. Bu, kalıtımın (`inheritance`) tam olarak ne işe yaradığının somut bir örneği.

---

## 5. `WorkOrderCompletedAuditConsumer.cs` (yeni) — ikinci, bağımsız consumer

```csharp
public class WorkOrderCompletedAuditConsumer : EventConsumerBase<WorkOrderCompletedEvent>
{
    public WorkOrderCompletedAuditConsumer(IConfiguration configuration, ILogger<WorkOrderCompletedAuditConsumer> logger)
        : base(configuration["RabbitMq:HostName"] ?? "localhost", "audit", logger)
// "audit" -> bu consumer'ın kuyruk adı bu isimle bitecek, "notifications"tan farklı
    {
    }

    protected override Task HandleAsync(WorkOrderCompletedEvent domainEvent, CancellationToken cancellationToken)
    {
        Logger.LogInformation(
            "Audit: work order {WorkOrderId} ('{Title}') completed at {CompletedAtUtc}",
            domainEvent.WorkOrderId, domainEvent.Title, domainEvent.CompletedAtUtc);
        return Task.CompletedTask;
// gerçek bir iş değeri taşımıyor bilerek — amaç sadece "iki bağımsız consumer, aynı event'i
// gerçekten alabiliyor mu" sorusunu somut olarak kanıtlamak
    }
}
```

**Neden bu şekilde yazıldı:** Bu, bugünün **asıl kanıtı**. `INotificationSender`'a hiç bağımlı değil — `WorkOrderCompletedEventConsumer`'dan tamamen habersiz, kendi kuyruğunu dinliyor. İkisi de aynı anda çalıştığında, ikisinin de **aynı event'in kendi kopyasını** aldığı (aşağıdaki canlı kanıtta) gösteriliyor.

---

## 6. `Program.cs` — ikinci consumer kaydı

```csharp
builder.Services.AddHostedService<WorkOrderCompletedAuditConsumer>();
// Day 68'deki AddHostedService<WorkOrderCompletedEventConsumer>() satırının yanına eklendi —
// ASP.NET Core artık İKİ ayrı arka plan servisini de otomatik başlatıp çalışır tutuyor
```

---

## 7. Canlı yakalanan ciddi bir regresyon: test paketi 17-59 dakikaya çıktı — ve gerçek sebep

Bugünün değişikliğinden sonra `dotnet test FieldOps.slnx` çalıştırıldığında, testlerin hepsi geçti ama süre **17 dakika 44 saniyeye**, sonra (ikinci consumer geçici olarak kaldırılıp tekrar denendiğinde) **59 dakikaya** çıktı — tamamen tutarsız, consumer sayısıyla orantılı bile değil. Bu ciddiye alınması gereken bir bulguydu.

**İlk, doğru ama YETERSİZ düzeltme:** Day 67'nin `NoOpEventPublisher`'ıyla aynı mantıkla, testlerde bu iki consumer'ın (`WorkOrderCompletedEventConsumer`/`WorkOrderCompletedAuditConsumer`) gerçek bir RabbitMQ bağlantısı denemesini tamamen engellemek gerekiyordu — ama bunlar `IEventPublisher` gibi tekil (`AddSingleton`) bir servis değil, `IHostedService` olarak kayıtlı; `AddSingleton` ile "üzerine yazmak" (Day 64/67'nin işe yaradığı yöntem) burada işe yaramıyor çünkü **kayıtlı her `IHostedService` çalıştırılıyor**, sonuncusu "kazanmıyor". Bu yüzden `tests/FieldOps.Api.Tests/FieldOpsApiFactory.cs`'de bu iki servisin kendi kayıtları (`ServiceDescriptor`'ları) doğrudan **kaldırıldı**:

```csharp
var hostedServicesToRemove = services
    .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
        && (descriptor.ImplementationType == typeof(WorkOrderCompletedEventConsumer)
            || descriptor.ImplementationType == typeof(WorkOrderCompletedAuditConsumer)))
    .ToList();
foreach (var descriptor in hostedServicesToRemove)
{
    services.Remove(descriptor);
}
```

Bu düzeltme **gerçek ve kalıcı** — testlerde bu iki consumer'ın artık hiç çalışmaması doğru bir davranış (Day 66-69 zaten bunların RabbitMQ'ya bağlanabildiğini canlı olarak kanıtladı, testlerin bunu tekrar tekrar kanıtlamasına gerek yok). Ama süreyi **tam olarak** eski hâline (~15-18 saniye) döndürmedi — sadece ~8-9 dakikaya indirdi.

**Asıl kanıt — sorunun büyük kısmı RabbitMQ'yla hiç ilgili değildi:** Sadece `Assign` ile ilgili, RabbitMQ'ya hiç dokunmayan 28 test bile **4 dakika 36 saniye** sürdü. Bu, sorunun geri kalanının **kodla değil, bu oturumdaki Docker/Testcontainers'ın o an neden yavaş çalıştığıyla** ilgili olduğunu kesin olarak kanıtlıyor — canlı olarak SQL Server konteynerinin loglarına bakıldığında, veritabanlarının hazır olmasının o gün normalden çok daha uzun sürdüğü görüldü.

**Ders:** Bir performans regresyonu bulunduğunda, "en son ne değiştirdiysem o suçludur" varsayımı **her zaman doğru değil** — burada gerçek bir kod sorunu (hosted service'lerin testlerde gereksiz yere gerçek RabbitMQ'ya bağlanmaya çalışması) **gerçekten vardı ve düzeltildi**, ama toplam sürenin büyük kısmı tamamen ayrı, ortamla ilgili bir nedenden kaynaklanıyordu. İkisini ayırt etmenin tek yolu, iddiayı **izole edip ölçmekti** (RabbitMQ'ya hiç dokunmayan bir test alt kümesini ayrıca çalıştırmak).

---

## 8. Canlı demonstrasyon — iki bağımsız consumer, aynı event'in kendi kopyasını alıyor

Tam bir iş emri yaşam döngüsü (`create → assign → start → complete`) çalıştırıldı. Loglarda **her iki consumer'dan da**, aynı event için, birbirinden habersiz iki ayrı işlem görüldü:
```
{"Category":"FieldOps.Api.Application.LoggingNotificationSender",
 "Message":"Notification: Work order 'Fix the wiring' has been completed and is awaiting your approval."}

{"Category":"FieldOps.Api.Application.WorkOrderCompletedAuditConsumer",
 "Message":"Audit: work order 1 ('Fix the wiring') completed at 09/27/2026 12:20:07"}
```
RabbitMQ'nun yönetim API'sinden de bağımsız olarak doğrulandı — **iki ayrı kuyruk**, aynı exchange'e bağlı:
```
curl -u guest:guest http://localhost:15672/api/queues
→ "fieldops.events.WorkOrderCompletedEvent.audit"
→ "fieldops.events.WorkOrderCompletedEvent.notifications"
```

---

## Regresyon (Day 69)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65 (bugünkü ortam yavaşlığı nedeniyle süre normalden uzun,
                                       ama sonuç doğru; kod tarafındaki gerçek regresyon
                                       FieldOpsApiFactory düzeltmesiyle giderildi)
dotnet build StockPilot.slnx  → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx   → 0 Hata, 0 Uyarı
```

## Demo basitleştirmesi vs. üretim gereksinimi

`WorkOrderCompletedAuditConsumer` bugün sadece bir kanıt — gerçek bir iş değeri taşımıyor. Hâlâ yeniden bağlanma mantığı yok (Day 68'in bilinen eksikliği, bugün de aynen geçerli, artık iki consumer için). Gerçek exchange/routing key tasarımı `fanout` ile sınırlı — `topic`/`direct` gibi seçici (sadece belirli olaylara abone olma) dağıtım hâlâ ele alınmadı.
