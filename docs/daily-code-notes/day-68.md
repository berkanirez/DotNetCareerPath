# Day 68 — Kod Notları

Faz 4, Hafta 13, Gün 68. Konu: **gerçek bir consumer — `WorkOrderCompletedEventConsumer`**. Dün "RabbitMQ'nun ne işe yaradığı" sorusuna dürüst cevap şuydu: henüz hiçbir işe yaramıyordu, çünkü yayınlanan event'i okuyan kimse yoktu. Bugün bu boşluk kapanıyor — Day 66/67'nin sorduğu sorunun cevabı bugün gerçekleşiyor.

---

## 1. `src/FieldOps.Api/Application/EventQueueNaming.cs`

```csharp
public static class EventQueueNaming
{
    public static string QueueNameFor<TEvent>() => $"fieldops.{typeof(TEvent).Name}";
// tek bir yerde, tek bir formülle hesaplanıyor — RabbitMqEventPublisher (yayıncı) ve
// WorkOrderCompletedEventConsumer (tüketici) artık ikisi de bu AYNI metodu çağırıyor,
// aynı formülü iki yerde ayrı ayrı yazmıyorlar
}
```

**Neden bu şekilde yazıldı:** Day 67'de bu mantık `RabbitMqEventPublisher`'ın içinde `private` bir metottu. Bugün aynı hesaplamayı **hem yayıncı (publisher) hem tüketici (consumer)** yapması gerektiği için, tek bir yerde tutulması gerekiyordu — aksi hâlde ikisi zamanla farklı bir kuyruk adı hesaplamaya başlarsa (biri "fieldops.WorkOrderCompletedEvent" yazarken diğeri farklı bir formata geçerse), yayıncı ve tüketici **birbirini asla bulamayan iki farklı kuyruğa** konuşur hâle gelirdi — sessiz, fark edilmesi zor bir hata sınıfı. Bu yüzden bu küçük, paylaşılan statik sınıfa çıkarıldı.

---

## 2. `src/FieldOps.Api/Application/WorkOrderCompletedEventConsumer.cs`

```csharp
public class WorkOrderCompletedEventConsumer : BackgroundService
{
    private readonly string _hostName;
// bağlanılacak RabbitMQ sunucusunun adresi — appsettings.Development.json'dan ya da docker-compose.yml'in enjekte ettiği değerden okunacak
    private readonly INotificationSender _notificationSender;
// Day 51'in bildirim arayüzü — artık WorkOrdersController.Complete değil, BU sınıf çağırıyor
    private readonly ILogger<WorkOrderCompletedEventConsumer> _logger;
// bu sınıfa özel, yapılandırılmış (Day 55) log yazıcısı

    public WorkOrderCompletedEventConsumer(
        IConfiguration configuration,
        INotificationSender notificationSender,
        ILogger<WorkOrderCompletedEventConsumer> logger)
    {
// üç bağımlılık da DI konteynerinden otomatik enjekte ediliyor — INotificationSender ve ILogger zaten Program.cs'te kayıtlı, IConfiguration ise ASP.NET Core'un kendi yerleşik servisi
        _hostName = configuration["RabbitMq:HostName"] ?? "localhost";
// "RabbitMq:HostName" ayarı okunuyor, hiç yoksa "localhost" varsayılıyor — RabbitMqEventPublisher'daki aynı okuma deseni
        _notificationSender = notificationSender;
        _logger = logger;
// gelen iki bağımlılık, ilgili alanlara atanıyor
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
// BackgroundService'in soyut metodu — ASP.NET Core, uygulama başladığında bunu bir kere çağırıyor ve uygulama kapanana kadar arka planda çalışmasına izin veriyor; stoppingToken, uygulama kapanmaya başladığında iptal ediliyor
        IConnection? connection = null;
        IChannel? channel = null;
// try bloğunun DIŞINDA tanımlanıyorlar ki, en altta finally bloğu bağlantı kurulmuş olsun ya da olmasın bunlara erişip düzgünce kapatabilsin
        try
        {
            var factory = new ConnectionFactory { HostName = _hostName };
// RabbitMQ'ya nasıl bağlanılacağını tarif eden fabrika nesnesi — Day 66/67'deki ile aynı ilk adım
            connection = await factory.CreateConnectionAsync(stoppingToken);
// gerçek bir TCP bağlantısı açılıyor
            channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
// o bağlantı üzerinden bir kanal açılıyor — RabbitMqEventPublisher'ın tersine, bu bağlantı kapatılmıyor, uygulama çalıştığı sürece açık kalacak

            var queueName = EventQueueNaming.QueueNameFor<WorkOrderCompletedEvent>();
// hangi kuyruğun dinleneceği, yayıncıyla (RabbitMqEventPublisher) AYNI paylaşılan yardımcı metotla hesaplanıyor — ikisi asla farklı bir kuyruk adına düşemesin diye
            await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
// bu isimde bir kuyruk yoksa oluşturuluyor, zaten varsa (genelde yayıncı zaten oluşturmuştur) hiçbir şey yapmıyor

            var consumer = new AsyncEventingBasicConsumer(channel);
// "bu kanaldan mesaj gelirse ne yapılacağını" tanımlayacağımız tüketici nesnesi — Day 66'daki demo consumer'ın aynısı
            consumer.ReceivedAsync += async (_, ea) =>
            {
// kuyruğa bir mesaj düştüğünde bu blok otomatik, asenkron olarak çalışıyor — bugünün GERÇEK iş mantığı burada
                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
// mesajın ham baytları, tekrar okunabilir bir JSON metnine çevriliyor
                    var domainEvent = JsonSerializer.Deserialize<WorkOrderCompletedEvent>(json);
// JSON metni, RabbitMqEventPublisher'ın Serialize ettiği aynı tipe (WorkOrderCompletedEvent) geri çevriliyor (deserileştirme)

                    if (domainEvent?.CustomerId is not null)
                    {
// müşterisi olmayan bir iş emri için bildirim göndermenin bir anlamı yok — WorkOrdersController.Complete'in eski kodundaki aynı kontrol, sadece artık burada
                        await _notificationSender.NotifyAsync(
                            $"Work order '{domainEvent.Title}' has been completed and is awaiting your approval.",
                            stoppingToken);
// bildirim, artık HTTP isteğinden tamamen bağımsız, burada, arka planda gönderiliyor
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process a WorkOrderCompletedEvent message");
// mesajı işlerken (deserileştirme ya da bildirim gönderme) bir hata olursa, sadece loglanıyor — mesaj burada henüz reddedilmiyor
                }
                finally
                {
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
// yukarıda hata olsa da olmasa da, mesaj HER ZAMAN onaylanıyor — aksi hâlde kalıcı olarak başarısız olan bir mesaj, RabbitMQ tarafından sonsuza kadar tekrar tekrar teslim edilmeye çalışılırdı
                }
            };

            await channel.BasicConsumeAsync(
                queue: queueName, autoAck: false, consumerTag: string.Empty,
                noLocal: false, exclusive: false, arguments: null, consumer: consumer,
                cancellationToken: stoppingToken);
// yukarıda tanımlanan consumer, artık gerçekten kuyruğu "dinlemeye" başlıyor; autoAck:false, onaylamanın yukarıdaki ReceivedAsync içinde elle yapılacağı anlamına geliyor

            await Task.Delay(Timeout.Infinite, stoppingToken);
// BasicConsumeAsync sadece dinlemeyi BAŞLATIYOR, kendisi beklemiyor — bu satır olmasaydı ExecuteAsync hemen bitip metot (ve onunla birlikte connection/channel) kapanırdı; stoppingToken iptal edilene (uygulama kapanana) kadar burada bekleniyor
        }
        catch (OperationCanceledException)
        {
// Task.Delay(Timeout.Infinite, ...), stoppingToken iptal edildiğinde bu istisnayı fırlatır — bu, uygulamanın normal şekilde kapandığı anlamına geliyor, bir hata değil
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WorkOrderCompletedEventConsumer could not connect to RabbitMQ; completed-work-order notifications will not be sent until the app restarts.");
// RabbitMQ'ya hiç bağlanılamazsa (örn. henüz ayağa kalkmamışsa) buraya düşülüyor — Day 50/52'nin dersi: bu hata burada yakalanmazsa BackgroundService'in varsayılan davranışı TÜM UYGULAMAYI çökertirdi
        }
        finally
        {
            if (channel is not null) await channel.DisposeAsync();
            if (connection is not null) await connection.DisposeAsync();
// ne şekilde çıkılırsa çıkılsın (normal kapanma ya da hata), açılmış olabilecek kanal/bağlantı düzgünce kapatılıyor
        }
    }
}
```

**Neden bu şekilde yazıldı — genel amaç:** Bu, Day 50'nin `WorkOrderReportCacheWarmer`'ıyla **aynı aile**: uygulama başladığında ayağa kalkan, uygulama kapanana kadar arka planda çalışan bir `BackgroundService`. Farkı şu: `WorkOrderReportCacheWarmer` bir **zamanlayıcıyla** ("her X dakikada bir çalış") tetikleniyordu; bu consumer ise **RabbitMQ'nun kendisi tarafından** tetikleniyor ("bir mesaj gelince çalış").

**Neden `INotificationSender` artık burada:** Day 51'de `_notificationSender.NotifyAsync(...)` `WorkOrdersController.Complete`'in içindeydi — HTTP isteğiyle **aynı anda, aynı thread'de** çalışıyordu. Bugün bu çağrı buraya taşındı: artık **hiçbir HTTP isteğiyle aynı anda çalışmıyor**, tamamen ayrı, arka planda çalışan bir işlemin parçası. `Complete`, artık `INotificationSender`'ın var olduğunu bile bilmiyor (constructor'dan da kaldırıldı) — sadece "iş emri tamamlandı" gerçeğini yayınlıyor, bu gerçeğe ne yapılacağına burada karar veriliyor.

---

## 3. `WorkOrdersController.cs` — `INotificationSender`'ın tamamen kaldırılması

`Complete` action'ındaki eski senkron bildirim bloğu tamamen silindi; `INotificationSender` alanı, constructor parametresi ve ataması da kaldırıldı (artık hiçbir yerde kullanılmıyordu). `Complete` artık **sadece** event yayınlıyor:

```csharp
try
{
    await _eventPublisher.PublishAsync(
        new WorkOrderCompletedEvent(updated.Id, updated.OrganizationId, updated.CustomerId, updated.Title, DateTime.UtcNow),
        cancellationToken);
// Complete'in yaptığı TEK bildirime-ilişkin şey artık bu — "iş emri tamamlandı" gerçeğini
// yayınlamak. Kime, nasıl haber verileceğine dair hiçbir bilgi/karar burada yok.
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to publish WorkOrderCompletedEvent for work order {WorkOrderId}", id);
// yayınlama başarısız olsa bile Complete, zaten gerçekleşmiş olan tamamlanmayı geri almıyor
}
```

**Neden bu şekilde yazıldı:** Bu, dünkü sorunuzun ("RabbitMQ'nun faydası ne oldu") gerçek cevabı — artık `Complete`, bildirim kanalının var olup olmadığından, hızlı ya da yavaş olmasından **tamamen habersiz**. Tek sorumluluğu "bu gerçekten oldu" bilgisini yayınlamak; bu bilgiyle ne yapılacağına (bildirim göndermek, ileride belki raporlama/arama güncellemek) tamamen başka, bağımsız kod parçaları karar veriyor.

---

## 4. `src/FieldOps.Api/Program.cs` — kayıt

```csharp
builder.Services.AddHostedService<WorkOrderCompletedEventConsumer>();
// ASP.NET Core'a "uygulama başladığında bu sınıfın ExecuteAsync'ini çalıştır, uygulama
// kapanana kadar arka planda tut" deniyor — Day 50'nin WorkOrderReportCacheWarmer'ıyla
// birebir aynı kayıt mekanizması
```

Day 50'nin `AddHostedService<WorkOrderReportCacheWarmer>()` kaydıyla aynı mekanizma — ASP.NET Core, uygulama başladığında bu servisi otomatik olarak başlatıyor ve uygulama kapanana kadar arka planda çalıştırıyor.

---

## 5. Canlı demonstrasyon — ve Day 66/67'nin aynı dersinin bir kez daha, bu sefer consumer için yaşanması

`docker compose up --build -d` sonrası, consumer'ın **ilk başlatılma denemesi** yine RabbitMQ tam hazır olmadan çalıştı:
```
WorkOrderCompletedEventConsumer could not connect to RabbitMQ; completed-work-order
notifications will not be sent until the app restarts.
BrokerUnreachableException ... Connection refused
```
Bu satırın kendisi bile önemli bir gerçek sınırlamayı itiraf ediyor: **bugünkü consumer'ın yeniden bağlanma (reconnect) mantığı yok** — bir kere bağlanamazsa, uygulama yeniden başlatılana kadar bir daha hiç denemiyor. Bu, Day 66/67'nin "geçici demo" bağlamında kabul edilebilirken, artık **kalıcı bir arka plan servisi** için gerçek bir üretim eksikliği — ileride ele alınması gereken, bilinçli olarak dokümante edilmiş bir sınır.

RabbitMQ tam başladıktan sonra sadece API konteyneri yeniden başlatıldı (`docker compose restart fieldops-api`), ardından tam bir iş emri yaşam döngüsü çalıştırıldı. Loglarda **gerçek kanıt** görüldü:
```
{"Category":"FieldOps.Api.Application.LoggingNotificationSender",
 "Message":"Notification: Work order 'Fix the elevator' has been completed and is awaiting your approval."}
```
Bu log satırı, Day 67'de olduğu gibi RabbitMQ'nun yönetim API'sinden elle okunan bir veri değil — **uygulamanın kendisinin, arka planda, bir HTTP isteğiyle hiç eş zamanlı olmadan** ürettiği gerçek bir davranış. Bu, dünkü sorunun tam cevabı: RabbitMQ artık gerçekten işe yarıyor.

Doğrulamadan sonra `docker compose down` ile yığın kapatıldı.

---

## Regresyon (Day 68)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65 (~14-18s, ek bir yavaşlama yok)
dotnet build StockPilot.slnx  → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx   → 0 Hata, 0 Uyarı
```

## Demo basitleştirmesi vs. üretim gereksinimi

Bugün **canlı olarak da doğrulanan**, bilinçli, dokümante edilmiş bir eksik: consumer'ın hiç yeniden bağlanma mantığı yok — RabbitMQ, uygulama başlarken hazır değilse, bildirimler uygulama yeniden başlatılana kadar tamamen durur. Ayrıca: her zaman `ack` etme (kalıcı hatalar sonsuza kadar sessizce kayboluyor, hiçbir "başarısız mesajlar" kuyruğuna düşmüyor), tek consumer örneği (yatay ölçeklenme/yük paylaşımı yok). Bunların hepsi Week 14'ün ("retry, exponential backoff, dead-letter queues, duplicate-message handling") tam olarak ele alacağı konular.
