# Day 76 — Kod Notları

Faz 4, Hafta 15, Gün 76. Konu: **gerçek servis çıkarımı** — ADR 0005'in (Day 75) kağıt üzerindeki sınırını, gerçekten ayrı bir `.NET` projesine, kendi veritabanına ve kendi çalışan process'ine dönüştürmek. Bugüne kadarki her şey tek bir `FieldOps.Api` process'i içindeydi; bugünden itibaren bildirim gönderme işi **tamamen ayrı bir process** (`FieldOps.NotificationService`) içinde yaşıyor.

---

## 1. Yeni proje: `FieldOps.NotificationService`

```
dotnet new worker -n FieldOps.NotificationService -o src/FieldOps.NotificationService
# "worker" sablonu -- ASP.NET Core (HTTP endpoint'leri) DEGIL, sadece
# arka planda surekli calisan bir .NET process'i icin. Bu servisin hicbir
# HTTP endpoint'i yok -- sadece RabbitMQ'yu dinliyor, kimse ona istek atmiyor.
```

`FieldOps.NotificationService.csproj`'ye eklenen paketler:
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.11" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.11" />
<PackageReference Include="RabbitMQ.Client" Version="7.2.2" />
<!-- FieldOps.Api'nin projesine hic referans YOK -- ProjectReference satiri yok.
     Bu, ADR 0005'in tum amaci: iki proje ayni cozumde (solution) yasasa bile,
     birbirine derleme-zamaninda hicbir bagimliligi olmamali. -->
```

**Neden bu şekilde yazıldı:** Bir "gerçek" servis çıkarımı, sadece bir arayüz (`IInboxStore`) tanımlamakla bitmiyor — ayrı bir `.csproj`, ayrı bir `Program.cs`, ayrı bir process, ayrı bir veritabanı gerektiriyor. Bugünün asıl kanıtı, kodun derlenmesi değil, **iki ayrı `dotnet run`'ın, aralarında hiçbir C# referansı olmadan, sadece RabbitMQ üzerinden konuşabildiğini** canlı göstermek.

---

## 2. `WorkOrderCompletedEvent.cs` ve `EventQueueNaming.cs` — bilerek KOPYALANDI, taşınmadı

```csharp
namespace FieldOps.NotificationService;

public record WorkOrderCompletedEvent(
    int WorkOrderId,
    int OrganizationId,
    int? CustomerId,
    string Title,
    DateTime CompletedAtUtc);
// FieldOps.Api'deki WorkOrderCompletedEvent (Day 67) ile BIREBIR AYNI alanlar,
// ama BASKA bir dosya, BASKA bir projede -- iki proje arasinda bunu paylasan
// TEK bir tip yok. Ikisi de, RabbitMQ'nun tasidigi JSON'un ayni sekle sahip
// olacagina, sadece KARSILIKLI GUVENEREK anlasiyor.
```

```csharp
internal static class EventQueueNaming
{
    public static string ExchangeNameFor<TEvent>() => $"fieldops.events.{typeof(TEvent).Name}";
    public static string QueueNameFor<TEvent>(string consumerName) => $"{ExchangeNameFor<TEvent>()}.{consumerName}";
    public static string DeadLetterQueueNameFor<TEvent>(string consumerName) =>
        $"{QueueNameFor<TEvent>(consumerName)}.dead-letter";
}
// Day 68'de ogrendigimiz gercek hala gecerli: producer ve consumer'i birbirine
// baglayan TEK sey, ayni HESAPLANMIS string. Bu servis kendi EventQueueNaming
// kopyasini hesapliyor, FieldOps.Api de kendi kopyasini -- ikisi de AYNI
// "fieldops.events.WorkOrderCompletedEvent.notifications" adina ulasiyor,
// ama hicbiri digerinin kodunu GORMUYOR bile.
```

**Neden bu şekilde yazıldı — kritik nokta:** İlk bakışta bu "kod tekrarı" gibi görünüyor ve normalde bu repo'da "aynı şeyi iki kere yazma" kuralına aykırı gibi. Ama burada **yanlış bir paylaşım, doğru bir tekrardan daha kötü** — eğer bu iki proje `WorkOrderCompletedEvent`'i gerçekten paylaşan ortak bir `.csproj`'a referans verseydi, bu iki servis artık **birlikte derlenmek zorunda** kalırdı, yani gerçek anlamda ayrı deploy edilemezlerdi. ADR 0005'in ertelediği karar ("gerçek bir `FieldOps.Contracts` paylaşımı nerede yaşayacak") tam olarak bu yüzden bugüne değil, gerçekten ihtiyaç duyulduğu güne bırakıldı.

---

## 3. `IInboxStore.cs` ve `EventConsumerBase.cs` — aynı mantık, farklı arka uç

```csharp
public interface IInboxStore
{
    bool HasProcessed(string consumerName, string messageId);
    void MarkProcessed(string consumerName, string messageId);
    int RecordFailedAttempt(string consumerName, string messageId);
}
// FieldOps.Api'deki IInboxStore (Day 73) ile AYNI uc metot -- ama bu,
// FieldOps.NotificationService'in KENDI arayuzu. EventConsumerBase<TEvent>
// asagida SADECE bunu biliyor, IWorkOrderDirectory'yi hic HATIRLAMIYOR bile.
```

`EventConsumerBase<TEvent>` — Day 69-74'ün connect/retry/inbox/dead-letter mantığının, `FieldOps.NotificationService`'e taşınmış birebir kopyası. Aşağıda tüm sınıf, satır satır:

### 3.1. Alanlar ve constructor

```csharp
public abstract class EventConsumerBase<TEvent> : BackgroundService
// abstract -- bu sinifin KENDISI hicbir zaman dogrudan orneklenmiyor (new
// EventConsumerBase<...>() yazamazsiniz). Sadece WorkOrderCompletedEventConsumer
// gibi somut bir alt sinif uzerinden kullanilabiliyor.
// BackgroundService -- .NET'in kendi taban sinifi; ExecuteAsync'i override eden
// herhangi bir sinif, uygulama ayaga kalkinca otomatik baslatilan, arka planda
// surekli calisan bir "worker" haline geliyor.
// <TEvent> -- generic: bu sinif SADECE WorkOrderCompletedEvent icin degil,
// gelecekte baska herhangi bir event tipi icin de yeniden kullanilabilir.
{
    private readonly string _hostName;
    // RabbitMQ'nun hangi makinede/konteynerde oldugu (orn. "localhost" ya da
    // "rabbitmq") -- constructor'dan geliyor, sabit kodlanmis degil.
    private readonly string _consumerName;
    // Bu consumer'in KENDI kimligi (bugun: "notifications") -- kendi kuyruk
    // adini olusturmak ve Inbox/dead-letter kayitlarinda "hangi consumer"
    // sorusuna cevap vermek icin kullaniliyor.
    private readonly IServiceScopeFactory _scopeFactory;
    // Bu sinif bir BackgroundService, yani SINGLETON (uygulama boyunca tek
    // ornek). Ama IInboxStore, Scoped bir DbContext'e bagli -- bu yuzden her
    // mesaj icin AYRI, kisa omurlu bir "scope" acmak gerekiyor (Day 50'nin
    // ayni deseni).
    protected readonly ILogger Logger;
    // protected -- alt siniflarin (WorkOrderCompletedEventConsumer) da
    // kendi loglarini basabilmesi icin erisilebilir, ama disaridan erisilemez.

    protected EventConsumerBase(string hostName, string consumerName, IServiceScopeFactory scopeFactory, ILogger logger)
    {
        _hostName = hostName;
        _consumerName = consumerName;
        _scopeFactory = scopeFactory;
        Logger = logger;
        // Sadece parametreleri alanlara atiyor -- hicbir baglanti burada
        // ACILMIYOR. Gercek RabbitMQ baglantisi ExecuteAsync icinde, uygulama
        // gercekten baslarken kuruluyor.
    }

    protected abstract Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
    // abstract metot -- bu sinifin GOVDESI yok, sadece "her alt sinif BUNU
    // kendi yazmak ZORUNDA" diyor. WorkOrderCompletedEventConsumer icin bu,
    // "bildirim gonder" anlamina geliyor; baska bir consumer icin baska
    // bir sey olabilir. Template Method deseninin (Day 69) tam da bu.

    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    // Day 72'nin exponential backoff sinirlari: ilk bekleme 1 saniye,
    // en fazla 30 saniyeye kadar katlanarak buyuyebilir.
    private const int MaxDeliveryAttempts = 3;
    // Day 74'un dead-letter esigi: bir mesaj 3 denemeden sonra hala
    // basarisiz olursa, sonsuza kadar denemek yerine ayri bir kuyruga tasiniyor.
```

### 3.2. `ExecuteAsync` — bağlan, dinle, kopunca yeniden dene

```csharp
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = InitialRetryDelay;
        // Her yeni baslangicta (veya basarili bir baglantidan sonra) bekleme
        // suresi hep en kucuk degerden basliyor.

        while (!stoppingToken.IsCancellationRequested)
        // Day 72'nin dis dongusu -- uygulama kapatilmadigi surece, bir
        // baglanti koptugunda buraya GERI DONULUYOR, ExecuteAsync hemen
        // bitmiyor.
        {
            IConnection? connection = null;
            IChannel? channel = null;
            // finally blogunda Dispose edebilmek icin try disinda tanimlaniyor.
            try
            {
                var factory = new ConnectionFactory { HostName = _hostName };
                connection = await factory.CreateConnectionAsync(stoppingToken);
                channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                // RabbitMQ'ya gercek TCP baglantisi burada aciliyor -- bu
                // satirlar RabbitMQ ayakta degilse (veya henuz hazir degilse)
                // BURADA exception firlatiyor, asagidaki catch'e dusuyor.

                var exchangeName = EventQueueNaming.ExchangeNameFor<TEvent>();
                await channel.ExchangeDeclareAsync(
                    exchange: exchangeName, type: ExchangeType.Fanout, durable: false, autoDelete: false, cancellationToken: stoppingToken);
                // "Bu isimde bir fanout exchange var mi? Yoksa olustur, varsa
                // dokunma" -- idempotent bir cagri, birden fazla process
                // (FieldOps.Api ve bu servis) ayni exchange'i GUVENLE
                // birbirinden BAGIMSIZ olarak deklare edebiliyor.

                var queueName = EventQueueNaming.QueueNameFor<TEvent>(_consumerName);
                await channel.QueueDeclareAsync(
                    queue: queueName, durable: false, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
                // Bu consumer'in KENDI kuyrugu -- "notifications" adini
                // tasiyor, baska hicbir consumer'in kuyrugu bununla cakismiyor.
                await channel.QueueBindAsync(
                    queue: queueName, exchange: exchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);
                // Kuyrugu exchange'e BAGLIYOR -- fanout oldugu icin routingKey
                // onemsiz (bos string), exchange'e gelen HER mesaj bu kuyruga
                // da dusecek.

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, ea) => { /* asagida 3.3 */ };
                // Mesaj geldiginde calisacak olay isleyicisi (event handler)
                // burada TANIMLANIYOR ama henuz CALISTIRILMIYOR -- RabbitMQ
                // kutuphanesi, her yeni mesaj geldiginde bu lambda'yi kendisi
                // cagiracak.

                await channel.BasicConsumeAsync(
                    queue: queueName, autoAck: false, consumerTag: string.Empty,
                    noLocal: false, exclusive: false, arguments: null,
                    consumer: consumer, cancellationToken: stoppingToken);
                // "Bu kuyrugu DINLEMEYE basla" -- autoAck: false onemli:
                // RabbitMQ mesaji OTOMATIK silmiyor, biz ReceivedAsync icinde
                // acikca BasicAckAsync/BasicNackAsync cagirana kadar kuyrukta
                // "teslim edildi ama onaylanmadi" durumunda bekliyor.

                retryDelay = InitialRetryDelay;
                // Baglanti GERCEKTEN basarili oldugu icin, bir SONRAKI kopma
                // da yine hizli (1 saniyelik) ilk denemeyle baslasin diye
                // sifirlaniyor -- onceki basarisizlik gecmisi unutuluyor.

                await Task.Delay(Timeout.Infinite, stoppingToken);
                // Sonsuza kadar bekle -- bu satir sadece stoppingToken iptal
                // edildiginde (uygulama kapanirken) OperationCanceledException
                // firlatip asagi duser. Mesajlarin GERCEK islenmesi, yukarida
                // kaydedilen ReceivedAsync olay isleyicisi araciligiyla,
                // arka planda, bu satirdan BAGIMSIZ olarak gerceklesiyor.
            }
            catch (OperationCanceledException)
            {
                break;
                // Normal kapanis (Ctrl+C, host durduruluyor) -- dongudden
                // TAMAMEN cikiliyor, tekrar denemiyor.
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "... retrying in {RetryDelaySeconds}s", ...);
                // Baglanti hic kurulamadi VEYA sonradan koptu -- her iki durum
                // da BURAYA dusuyor, ayni sekilde ele aliniyor.
                try { await Task.Delay(retryDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
                // Beklerken TAM O SIRADA kapatma istenirse, bu ikinci
                // break olmasaydi, zaten calismakta olan bir catch blogunun
                // ICINDEN firlayan bir exception YAKALANMADAN disariya sizardi.

                var doubledSeconds = retryDelay.TotalSeconds * 2;
                retryDelay = TimeSpan.FromSeconds(Math.Min(doubledSeconds, MaxRetryDelay.TotalSeconds));
                // Bekleme suresini ikiye katliyor, ama MaxRetryDelay'i (30sn)
                // asmiyor -- "exponential backoff, capped."
            }
            finally
            {
                if (channel is not null) { await channel.DisposeAsync(); }
                if (connection is not null) { await connection.DisposeAsync(); }
                // Basarili da olsa basarisiz da olsa, acilmis olan her
                // baglanti/kanal duzgunce kapatiliyor -- bir sonraki dongu
                // turunde SIFIRDAN yeni bir baglanti aciliyor.
            }
        }
    }
```

### 3.3. `ReceivedAsync` — her mesaj geldiğinde çalışan kod

```csharp
consumer.ReceivedAsync += async (_, ea) =>
{
    var messageId = ea.BasicProperties.MessageId;
    // RabbitMqEventPublisher'in mesaji yayinlarken sundugu MessageId --
    // outbox satirinin kendi Id'si. try DISINDA taniml, cunku asagidaki
    // catch blogu da buna erismek zorunda.
    try
    {
        using var scope = _scopeFactory.CreateScope();
        var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        // Bu MESAJ icin ozel, kisa omurlu bir DI scope'u -- bugun bu servis
        // icin NotificationServiceInboxStore doner (Program.cs'teki kayit
        // sayesinde), ama bu sinif hangi implementasyonun geldigini HIC bilmiyor.

        if (!string.IsNullOrEmpty(messageId) && inboxStore.HasProcessed(_consumerName, messageId))
        {
            Logger.LogInformation("... skipping already-processed ...");
            // Day 73'un Inbox kontrolu: bu mesaj DAHA ONCE islenmisse
            // (RabbitMQ'nun "at-least-once" garantisi geregi tekrar
            // gelmis olabilir), HandleAsync'i TEKRAR CAGIRMADAN atlaniyor.
        }
        else
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            var domainEvent = JsonSerializer.Deserialize<TEvent>(json);
            // Mesajin ham baytlari (ea.Body) once UTF8 metne, sonra JSON'dan
            // gercek TEvent nesnesine (bugun: WorkOrderCompletedEvent)
            // donusturuluyor.
            if (domainEvent is not null)
            {
                await HandleAsync(domainEvent, stoppingToken);
                // ISTE burasi -- alt sinifin (WorkOrderCompletedEventConsumer)
                // yazdigi GERCEK is mantigi burada calisiyor.
            }

            if (!string.IsNullOrEmpty(messageId))
            {
                inboxStore.MarkProcessed(_consumerName, messageId);
                // SADECE HandleAsync basariyla bittikten SONRA kaydediliyor --
                // "gercek is bitmeden 'bitti' deme" (OutboxPublisher'daki
                // ayni disiplin).
            }
        }

        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        // Basari yoluna SADECE buraya kadar sorunsuz gelinirse ulasiliyor --
        // RabbitMQ'ya "bu mesaji GUVENLE sil, bir daha gondermene gerek yok"
        // deniyor.
    }
    catch (Exception ex)
    {
        Logger.LogWarning(ex, "... failed to process ...");
        // HandleAsync icinde, deserialize sirasinda, ya da Inbox kontrolu
        // sirasinda HERHANGI bir hata -- hepsi buraya dusuyor.
        try
        {
            await HandleDeliveryFailureAsync(channel, ea, messageId, stoppingToken);
            // Asil karar (tekrar dene mi, dead-letter'a mi tasi) 3.4'te.
        }
        catch (Exception handlingEx)
        {
            Logger.LogWarning(handlingEx, "... failed to handle its own delivery failure ...");
            // Bu ic ic gecmis try/catch bile korunuyor -- HICBIR SEY bu
            // ReceivedAsync olay isleyicisinden, yakalanmadan disariya
            // sizmamali (Day 50/52'nin dersi).
        }
    }
};
```

### 3.4. `HandleDeliveryFailureAsync` — tekrar mı dene, dead-letter'a mı taşı

```csharp
private async Task HandleDeliveryFailureAsync(IChannel channel, BasicDeliverEventArgs ea, string? messageId, CancellationToken cancellationToken)
{
    if (string.IsNullOrEmpty(messageId))
    {
        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        return;
        // Kimligi olmayan bir mesajin kac kez denendigi SAYILAMAZ -- bu
        // yuzden "hicbir zaman bitmeyecek bir dongude tutmaktansa, bu tek
        // seferlik kaybi kabul et" denip mesaj burada BIRAKILIYOR (ack).
    }

    using var scope = _scopeFactory.CreateScope();
    var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();
    var attemptCount = inboxStore.RecordFailedAttempt(_consumerName, messageId);
    // Bu basarisizlik KAYDEDILIYOR ve GUNCEL toplam sayi geri aliniyor --
    // ayni IInboxStore, ama farkli bir metodu (RecordFailedAttempt).

    if (attemptCount < MaxDeliveryAttempts)
    {
        Logger.LogWarning("... will retry ... (attempt {AttemptCount} of {MaxDeliveryAttempts})", ...);
        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        // NACK + requeue:true -- "bunu ISLEYEMEDIM ama kuyruga GERI KOY,
        // hemen yeniden dene" -- BasicAckAsync'in tam tersi, mesaj kuyruktan
        // SILINMIYOR.
    }
    else
    {
        Logger.LogWarning("... exhausted {MaxDeliveryAttempts} attempts ...; moving it to the dead-letter queue", ...);
        var deadLetterQueueName = EventQueueNaming.DeadLetterQueueNameFor<TEvent>(_consumerName);
        await channel.QueueDeclareAsync(queue: deadLetterQueueName, ...);
        await channel.BasicPublishAsync(
            exchange: string.Empty, routingKey: deadLetterQueueName,
            mandatory: false, basicProperties: new BasicProperties { MessageId = messageId },
            body: ea.Body, cancellationToken: cancellationToken);
        // Orijinal mesajin BAYTLARI (ea.Body), OLDUGU GIBI, ayri bir
        // dead-letter kuyruguna yayinlaniyor -- kaybolmuyor, sadece
        // GORULEBILIR bir yere tasiniyor.

        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        // ORIJINAL mesaj simdi ack ediliyor -- normal kuyruktan CIKARILIYOR,
        // artik yeniden denenmeyecek.
    }
}
```

**Neden bu şekilde yazıldı:** Bu, ADR 0005'in Day 75'te önceden söylediği tam olay: *"`EventConsumerBase<TEvent>`, `IWorkOrderDirectory`'ye hiç bağlı olmadığı için, çıkarım sırasında hiç değişmesine gerek yok."* Bugün gerçekten oldu — bu dosya, Day 69-74'teki hâliyle **satır satır aynı** kaldı, sadece hangi projede yaşadığı ve `IInboxStore`'un hangi implementasyona bağlandığı değişti.

**Nasıl çalışıyor — üç katmanlı hata toleransı:** Bu tek sınıf, aslında üç ayrı, birbirinden bağımsız hata senaryosuna karşı üç ayrı savunma katmanı taşıyor: (1) RabbitMQ'nun kendisi ulaşılamazsa → `ExecuteAsync`'in dış döngüsü, exponential backoff ile sonsuza kadar yeniden dener (Day 72); (2) aynı mesaj birden fazla teslim edilirse → Inbox kontrolü, ikinci işlemeyi engeller (Day 73); (3) mesajın kendisi kalıcı olarak işlenemezse (bozuk veri, sürekli hata) → dead-letter kuyruğu, sonsuz döngüyü önler (Day 74). Üçü de aynı anda, aynı sınıfın içinde, birbirine karışmadan çalışıyor.

---

## 4. `NotificationServiceDbContext.cs` — bu servisin TÜM veritabanı

```csharp
internal class NotificationServiceDbContext : DbContext
{
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<FailedMessageAttempt> FailedMessageAttempts => Set<FailedMessageAttempt>();
    // BASKA hicbir DbSet yok -- WorkOrder yok, Employee yok, Organization yok,
    // Customer yok. ADR 0005'in "asla sahip olmayacak" listesi, burada
    // gercekten YAZILAMAYAN kod olarak somutlasiyor -- bu DbContext'e bir
    // WorkOrder tablosu eklemek isteseniz bile, bu servisin WorkOrder'in ne
    // oldugunu bilecek HICBIR YOLU yok.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            entity.HasIndex(m => new { m.ConsumerName, m.MessageId }).IsUnique();
            // Day 73'un ayni unique index'i -- ama artik WorkOrdersDbContext'te
            // degil, bu servisin KENDI, ayri veritabaninda.
        });
        // FailedMessageAttempts icin de ayni index...
    }
}
```

```
cd src/FieldOps.NotificationService
dotnet ef migrations add InitialCreate
dotnet ef database update
# Ilk defa, bu repo'da migration'lar FieldOps.Api'nin bes modulunden HICBIRINE
# ait olmayan, tamamen YENI, altinci bir veritabani (FieldOpsNotifications)
# uretiyor.
```

**Neden bu şekilde yazıldı:** ADR 0003'ün "database-per-module" kuralı burada bir seviye yukarı taşındı: "database-per-**service**". `WorkOrdersDbContext` beş modülden biri için tek başınaydı; `NotificationServiceDbContext` artık tamamen farklı bir deployment birimi için tek başına.

---

## 5. `NotificationServiceInboxStore.cs` — `IWorkOrderDirectory` YOK, doğrudan `DbContext`

```csharp
internal class NotificationServiceInboxStore : IInboxStore
{
    private readonly NotificationServiceDbContext _dbContext;

    public NotificationServiceInboxStore(NotificationServiceDbContext dbContext)
    {
        _dbContext = dbContext;
        // FieldOps.Api'deki WorkOrderInboxStore (Day 73), IWorkOrderDirectory
        // araciligiyla dolayli calisiyordu -- cunku modul sinirini (ADR 0001/
        // 0002) korumasi gerekiyordu. Burada boyle bir sinir yok: bu KUCUK
        // servisin, DbContext'ini saklamasi gereken baska hicbir modul yok.
    }

    public bool HasProcessed(string consumerName, string messageId) =>
        _dbContext.ProcessedMessages.Any(m => m.ConsumerName == consumerName && m.MessageId == messageId);

    public void MarkProcessed(string consumerName, string messageId)
    {
        try
        {
            _dbContext.ProcessedMessages.Add(new ProcessedMessage(consumerName, messageId));
            _dbContext.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Day 19/73'un ayni iki katmanli savunmasi -- burada da gecerli.
        }
    }
}
```

**Neden bu şekilde yazıldı:** Bu, ADR 0005'in tam olarak önceden yazdığı değişim — `WorkOrderInboxStore`'un yerini, `IWorkOrderDirectory` yerine kendi `DbContext`'ine bağlı **yeni bir implementasyon** aldı. `EventConsumerBase`'in kendisi bunu hiç fark etmedi bile, çünkü zaten sadece `IInboxStore`'u biliyordu.

---

## 6. `FieldOps.NotificationService/Program.cs` — tamamen ayrı bir `Host`

```csharp
var builder = Host.CreateApplicationBuilder(args);
// WebApplication.CreateBuilder DEGIL -- bu servisin HTTP dinleyecegi hicbir
// port yok, sadece bir "worker" (arka plan process'i).

var connectionString = builder.Configuration.GetConnectionString("FieldOpsNotificationsDb")
    ?? throw new InvalidOperationException("Missing connection string: FieldOpsNotificationsDb");
builder.Services.AddDbContext<NotificationServiceDbContext>(options => options.UseSqlServer(connectionString));
// FieldOps.Api'nin ConnectionStrings:FieldOps*Db'lerinden HICBIRINE bu servis
// erisemiyor -- kendi appsettings.Development.json'inda SADECE kendi
// FieldOpsNotificationsDb'si var.

builder.Services.AddScoped<IInboxStore, NotificationServiceInboxStore>();
builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();
builder.Services.AddHostedService<WorkOrderCompletedEventConsumer>();

var host = builder.Build();
host.Run();
```

**Neden bu şekilde yazıldı:** `FieldOps.Api`'nin `Program.cs`'i beş modül + Redis + rate limiting + health check'lerle kalabalık; bu servisin `Program.cs`'i sadece dört satır kayıt — bu fark, çıkarımın ne kadar "gerçek" olduğunun bir başka kanıtı: bu servis, FieldOps.Api'nin karmaşıklığının HİÇBİRİNİ taşımıyor.

---

## 7. `FieldOps.Api/Program.cs`'ten kaldırılanlar

```csharp
// KALDIRILDI:
// builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();
// builder.Services.AddHostedService<WorkOrderCompletedEventConsumer>();
//
// KALAN (audit consumer hala burada, cunku o Day 76'da tasinmiyor):
builder.Services.AddHostedService<WorkOrderCompletedAuditConsumer>();
builder.Services.AddScoped<IInboxStore, WorkOrderInboxStore>();
```

**Neden bu şekilde yazıldı:** `WorkOrderCompletedEventConsumer.cs`, `INotificationSender.cs`, `LoggingNotificationSender.cs` dosyalarının kendisi de `FieldOps.Api/Application/`'dan tamamen **silindi** — orada artık hiçbir iz yok. Audit consumer'ı bilerek yerinde bırakıldı: bugünün dilimi sadece bildirim tarafını çıkarmaktı (planın 4. maddesi), audit tarafı Day 77+'nin konusu.

---

## 8. `FieldOpsApiFactory.cs` — artık olmayan bir tipi kaldırmayı bırakmak

```csharp
var hostedServicesToRemove = services
    .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
        && descriptor.ImplementationType == typeof(WorkOrderCompletedAuditConsumer))
    .ToList();
// Day 69'dan beri burada IKI tipi (WorkOrderCompletedEventConsumer VE
// WorkOrderCompletedAuditConsumer) kaldiriyorduk. WorkOrderCompletedEventConsumer
// artik FieldOps.Api'de HIC YOK -- kod bile artik onu tanimiyor, bu yuzden
// referans TAMAMEN silindi (yoksa derleme hatasi olurdu).
```

**Neden bu şekilde yazıldı:** Test host'unun artık `WorkOrderCompletedEventConsumer`'ı hiç kaydetmediğini (çünkü sınıf hiç yok) — bu, "çıkarımın gerçek olduğunun" test tarafındaki kanıtı.

---

## 9. `docker-compose.yml` — yeni, bağımsız bir servis girişi

```yaml
fieldops-notification-service:
  build:
    context: .
    dockerfile: src/FieldOps.NotificationService/Dockerfile
  depends_on:
    - sqlserver
    - rabbitmq
    # Redis YOK burada -- fieldops-api'nin depends_on listesindeki redis'e
    # bu servisin hicbir ihtiyaci yok, cunku IdempotencyService/WorkOrderReportService
    # gibi seyler bu serviste hic yok.
  environment:
    ConnectionStrings__FieldOpsNotificationsDb: "Server=sqlserver;Database=FieldOpsNotifications;..."
    RabbitMq__HostName: "rabbitmq"
    # fieldops-api'nin BES ConnectionStrings__* satirindan HICBIRI burada yok.
```

**Neden bu şekilde yazıldı:** `fieldops-api` ile `fieldops-notification-service` arasındaki **tek** ortak satır, aynı `RabbitMq__HostName: "rabbitmq"` değeri — ADR 0005'in "tek kanal RabbitMQ" kararının docker-compose'daki karşılığı.

---

## 10. Canlı doğrulama — iki gerçek process, tek RabbitMQ

```
docker run -d --name fieldops-demo-rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
cd src/FieldOps.NotificationService && dotnet ef database update   # FieldOpsNotifications DB'sini local'de olusturdu

# Terminal 1:
cd src/FieldOps.Api && dotnet run --urls http://localhost:5190

# Terminal 2:
cd src/FieldOps.NotificationService && dotnet run
```

Gerçek bir iş akışı sürüldü: `POST /api/workorders` (Create, Org 1/Admin) → `assign` (Employee 2'ye) → `start` → `complete` (customerId=1 ile, bildirim yolunun tetiklenmesi için).

**Sonuç — loglardan birebir:**
```
# FieldOps.NotificationService'in KENDI logu (FieldOps.Api'nin logu DEGIL):
info: FieldOps.NotificationService.LoggingNotificationSender[0]
      Notification: Work order 'Day 76 extraction demo work order' has been completed and is awaiting your approval.

# FieldOps.Api'nin logu (audit consumer, hala orada):
Audit: work order 28 ('Day 76 extraction demo work order') completed at 09/28/2026 09:41:29
```
```sql
-- FieldOpsNotifications veritabaninda (FieldOps.Api'nin veritabaninda DEGIL):
SELECT * FROM ProcessedMessages;
-- Id  ConsumerName    MessageId  ProcessedAtUtc
-- 1   notifications   1          2026-09-28 09:41:32
```

Bu, bugünün tek gerçek kanıtı: bildirim, **`FieldOps.Api`'nin process'i içinde değil**, `FieldOps.NotificationService`'in kendi loguna ve kendi veritabanına düştü — audit consumer ise hâlâ eskisi gibi `FieldOps.Api`'nin kendi logunda. Demo sonunda her iki process ve geçici RabbitMQ container'ı durduruldu/temizlendi (bu, docker-compose'un kalıcı `rabbitmq` servisiyle çakışmaması için sadece bugünün canlı doğrulamasına özel, geçici bir container'dı).

---

## Regresyon (Day 76)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı (FieldOps.NotificationService dahil, 8 proje)
dotnet test FieldOps.slnx     → 65/65 (~8dk 32sn, bilinen ortam yavaşlığı, yeni bir regresyon yok)
```

## Demo basitleştirmesi vs. üretim gereksinimi

* **Demo bugün:** İki proje hâlâ aynı repo/solution içinde, elle iki ayrı terminalde çalıştırılıyor — gerçek bir CI/CD pipeline'ı, ayrı bir repo veya otomatik migration adımı yok.
* **Üretimde gerekli olurdu:** Ayrı bir deployment pipeline'ı, kendi sağlık kontrolü, kendi versiyon numarası, ve muhtemelen `WorkOrderCompletedEvent`'in gerçek bir paylaşılan kontrata (ADR 0005'in ertelediği karar) dönüşmesi.
