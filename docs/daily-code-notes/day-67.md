# Day 67 — Kod Notları

Faz 4, Hafta 13, Gün 67. Konu: **gerçek bir domain event — `WorkOrderCompletedEvent`**. Day 66'da RabbitMQ'nun çalıştığını "Hello World" seviyesinde bir mesajla kanıtlamıştık; bugün bunu gerçek bir iş bilgisine bağlıyoruz ve geçici demo kodunu tamamen kaldırıyoruz.

---

## 1. `src/FieldOps.Api/Application/WorkOrderCompletedEvent.cs`

```csharp
public record WorkOrderCompletedEvent(
    int WorkOrderId,
    int OrganizationId,
    int? CustomerId,
    string Title,
    DateTime CompletedAtUtc);
```

**Neden bu şekilde yazıldı:** Bu, FieldOps'un **ilk domain event'i** — "sistemde gerçekten olmuş bir şey"in kaydı (bir iş emri tamamlandı), bir komut ya da istek değil. Kasıtlı olarak **düz, kendi kendine yeten bir kayıt (record)**: ileride bunu okuyacak herhangi bir tüketici (bildirim servisi, raporlama servisi), FieldOps.Api'ye geri bir çağrı yapmadan, sadece bu veriyle işini yapabilmeli. `CustomerId`'in `int?` olması bilinçli — Day 47'den beri bir iş emrinin müşterisi olmayabiliyor.

**Nasıl çalışır:** Sıradan bir C# `record` — Day 43'ten beri bu workspace'te tekrarlanan "veri taşıyan, değişmez (immutable) bir tip" deseni.

---

## 2. `src/FieldOps.Api/Application/IEventPublisher.cs`

```csharp
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken);
}
```

**Neden bu şekilde yazıldı:** Day 51'in `INotificationSender`'ı ve Day 63'ün `IAiProvider`'ıyla **birebir aynı desen** — `WorkOrdersController`, RabbitMQ'nun var olduğunu bile bilmiyor, sadece "bu event'i yayınla" diyor. `<TEvent>` generic tutuldu (`WorkOrderCompletedEvent`'e özel değil) — ileride farklı bir domain event (örneğin `WorkOrderReopenedEvent`) çıktığında, bu arayüze ikinci, neredeyse aynı bir kopya eklemeye gerek kalmasın diye.

---

## 3. `src/FieldOps.Api/Application/RabbitMqEventPublisher.cs`

```csharp
public class RabbitMqEventPublisher : IEventPublisher
{
    private readonly string _hostName;
// bağlanılacak RabbitMQ sunucusunun adresi (örn. "localhost" ya da "rabbitmq"), yapıcıdan alınıp burada saklanıyor — her PublishAsync çağrısında yeniden kullanılacak

    public RabbitMqEventPublisher(string hostName) => _hostName = hostName;
// yapıcı, sadece dışarıdan verilen hostName'i _hostName alanına atıyor; başka hiçbir şey yapmıyor (henüz bağlantı açılmıyor)

    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken)
    {
// <TEvent> ile bu metot her tür domain event için genel kullanılabiliyor (bugün: WorkOrderCompletedEvent) — sınıfın kendisi hiçbir event tipini adıyla bilmiyor
        var factory = new ConnectionFactory { HostName = _hostName };
// RabbitMQ'ya nasıl bağlanılacağını tarif eden bir "fabrika" nesnesi oluşturuluyor — Day 66'daki demo bloğuyla birebir aynı ilk adım
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
// gerçek bir TCP bağlantısı açılıyor; "await using" sayesinde bu metot bittiğinde bağlantı otomatik kapanıyor — bilinçli basitleştirme: her çağrıda yeni bir bağlantı, Day 48'in Redis'i gibi paylaşılan tek bir bağlantı değil
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
// o bağlantı üzerinden bir kanal (channel) açılıyor — asıl yayınlama işlemi bu kanal üzerinden yapılacak

        var queueName = QueueNameFor<TEvent>();
// bu event tipi için kullanılacak kuyruğun adı, aşağıdaki yardımcı metotla hesaplanıyor (örn. "fieldops.WorkOrderCompletedEvent")
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
// bu isimde bir kuyruk RabbitMQ'da yoksa oluşturuluyor, zaten varsa hiçbir şey yapmıyor (idempotent) — Day 66'daki demo kuyruğuyla aynı basitleştirmeler (durable:false)

        var json = JsonSerializer.Serialize(domainEvent);
// gelen domainEvent nesnesi (örn. bir WorkOrderCompletedEvent kaydı), System.Text.Json ile bir JSON metnine çevriliyor — böylece RabbitMQ'nun taşıyabileceği, herhangi bir tüketicinin okuyabileceği düz bir formata dönüşüyor
        var body = Encoding.UTF8.GetBytes(json);
// RabbitMQ mesajları metin değil, ham bayt dizisi (byte[]) olarak taşıdığı için, JSON metni UTF-8 formatında baytlara çevriliyor
        await channel.BasicPublishAsync(
            exchange: string.Empty, routingKey: queueName, mandatory: false,
            basicProperties: new BasicProperties(), body: (ReadOnlyMemory<byte>)body, cancellationToken: cancellationToken);
// mesaj gerçekten RabbitMQ'ya gönderiliyor; exchange boş bırakıldığı için RabbitMQ'nun varsayılan exchange'i kullanılıyor — bu da routingKey'i (queueName) doğrudan aynı isimdeki kuyruğa eşliyor, Day 66'da öğrendiğimiz mekanizmanın aynısı
    }

    private static string QueueNameFor<TEvent>() => $"fieldops.{typeof(TEvent).Name}";
// event tipinin adından (örn. "WorkOrderCompletedEvent"), "fieldops." önekiyle birlikte, o event'e özel bir kuyruk adı türetiliyor — her yeni domain event tipi, ayrı bir isim yazmaya gerek kalmadan otomatik olarak kendi kuyruğuna sahip oluyor
}
```

**Neden bu şekilde yazıldı:** Bugünün tek `IEventPublisher` implementasyonu. **Bilinçli bir basitleştirme:** her `PublishAsync` çağrısında **yeni bir bağlantı açılıp kapatılıyor** — gerçek bir üretim sistemi, Day 48'in `IConnectionMultiplexer`'ı gibi tek, uzun ömürlü bir bağlantıyı yeniden kullanır. Bunun yerine bugün her seferinde yeni bağlantı açılmasının sebebi: RabbitMQ.Client 7.x'in bağlantı API'leri **tamamen asenkron** — Day 48'in `ConnectionMultiplexer.Connect(...)` gibi senkron bir eşdeğeri yok. Senkron bir eşdeğer olmadığı için, `Program.cs`'te `AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(...))`'in yaptığı gibi basit bir senkron fabrika fonksiyonu yazılamıyor; gerçek bir "async Singleton" kurmak (ilk kullanımda bir kere bağlanıp sonra hep aynı bağlantıyı paylaşmak) daha karmaşık bir kilitleme/başlatma mantığı gerektiriyor. Bugünün tek, düşük frekanslı event'i için bu karmaşıklığa girmek yerine, "her seferinde yeni bağlantı" kabul edilebilir bir hızdan-fedakarlık.

`QueueNameFor<TEvent>()`, kuyruk adını event tipinin adından türetiyor (`"fieldops.WorkOrderCompletedEvent"`) — bugün hâlâ Day 66'daki gibi **tek bir kuyruğa, varsayılan exchange ile** yayınlanıyor; birden fazla bağımsız tüketiciye dağıtım (gerçek exchange/routing key tasarımı) ileri bir günün konusu.

---

## 4. `WorkOrdersController.Complete` — event yayınlama

```csharp
try
{
    await _eventPublisher.PublishAsync(
        new WorkOrderCompletedEvent(updated.Id, updated.OrganizationId, updated.CustomerId, updated.Title, DateTime.UtcNow),
        cancellationToken);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to publish WorkOrderCompletedEvent for work order {WorkOrderId}", id);
}
```

**Neden bu şekilde yazıldı:** Day 51'in bildirim kodundaki **aynı fail-open mantığı** — event yayınlama başarısız olursa, iş emri zaten gerçekten `Completed` durumuna geçmiş oluyor; bu başarıyı bir mesajlaşma altyapısı sorunu yüzünden geri almak/kullanıcıya hata döndürmek yanlış olurdu. Bu çağrı, mevcut `_notificationSender.NotifyAsync(...)` çağrısının **yanına** eklendi, yerine değil — bugün ikisi de çalışıyor; ileride bildirim mantığının bizzat bu event'i **tüketen ayrı bir servise** taşınması (gerçek ayrıştırma) düşünülebilir, ama bu bugünün kapsamı dışında.

---

## 5. `src/FieldOps.Api/Program.cs` — Day 66'nın geçici bloğunun kaldırılması + gerçek kayıt

Day 66'nın "TEMP demonstration block"u (bir kuyruk oluşturup "Hello from FieldOps.Api" yazan geçici kod) **tamamen silindi** — zaten yorum satırında "removed once a real producer/consumer replaces it" yazıyordu, ve bugün tam olarak bu oldu. Yerine:

```csharp
var rabbitMqHostName = builder.Configuration["RabbitMq:HostName"] ?? "localhost";
builder.Services.AddSingleton<IEventPublisher>(_ => new RabbitMqEventPublisher(rabbitMqHostName));
```

Day 51/63'ün aynı kayıt deseni: arayüz için somut implementasyon, `WorkOrdersController` hiçbir zaman `RabbitMqEventPublisher`'ı adıyla bilmiyor.

---

## 6. Canlı yakalanan gerçek bir regresyon: test paketi neden yavaşladı, ve nasıl düzeltildi

Bu değişiklikten sonra `dotnet test FieldOps.slnx` çalıştırıldığında, tüm testler geçti (65/65) ama **toplam süre ~15-28 saniyeden 59 saniyeye çıktı**. Sebep: `Complete` action'ını çağıran **her test**, artık gerçekten bir RabbitMQ bağlantısı açmayı deniyor — ama test ortamında (Testcontainers sadece SQL Server sağlıyor, RabbitMQ yok) bu bağlantı **asla başarılı olamıyor**, ve `try`/`catch` bunu yakalasa da, RabbitMQ.Client'ın kendi bağlantı zaman aşımı dolana kadar **her çağrı gerçekten bekliyor**.

**Çözüm, Day 53'ün rate-limiting override'ıyla ve Day 64'ün `ConfigureTestServices` keşfiyle aynı desen:** `tests/FieldOps.Api.Tests/FieldOpsApiFactory.cs`'in `ConfigureWebHost`'una, sadece testler için geçerli olan bir DI geçersiz kılma eklendi:

```csharp
builder.ConfigureServices(services =>
{
    services.AddSingleton<IEventPublisher, NoOpEventPublisher>();
});

private class NoOpEventPublisher : IEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

Bu, `Program.cs`'in kendi `AddSingleton<IEventPublisher>(...)` kaydından **sonra** çalıştığı için (Day 64'te `WithWebHostBuilder`/`ConfigureTestServices` için doğrulanan "sonradan eklenen kayıt kazanır" kuralı), testlerde artık gerçek RabbitMQ'ya hiç bağlanılmıyor — `NoOpEventPublisher` hiçbir şey yapmadan anında geri dönüyor. Düzeltmeden sonra süre tekrar **~15-18 saniyeye** düştü.

**Bu, Day 51/63'ün zaten kurduğu ilkenin doğal bir devamı:** dış bir bağımlılığa (RabbitMQ) gerçekten ihtiyacı olmayan testler, o bağımlılığın gerçek, yavaş, başarısız olabilen halini değil, hızlı bir sahte/no-op halini kullanmalı — `WorkOrdersController`'ın kendi davranışını test etmek istiyoruz, RabbitMQ'nun bağlanabilirliğini değil (o zaten Day 66/67'de ayrı, canlı olarak kanıtlandı).

---

## 7. Canlı demonstrasyon — gerçek event, gerçek veriyle

`docker compose up --build -d`, migrasyonlar, ardından tam bir yaşam döngüsü:
```
POST /api/workorders (CustomerId: 1) → id=1
POST /api/workorders/1/assign        → Status=Assigned
POST /api/workorders/1/start         → Status=InProgress
POST /api/workorders/1/complete      → Status=Completed
```

RabbitMQ'nun yönetim API'sinden mesajın **gerçek içeriği** okundu:
```
curl -u guest:guest -X POST http://localhost:15672/api/queues/%2F/fieldops.WorkOrderCompletedEvent/get -d '{"count":1,"ackmode":"ack_requeue_true","encoding":"auto"}'
→ payload: {"WorkOrderId":1,"OrganizationId":1,"CustomerId":1,"Title":"Fix the elevator","CompletedAtUtc":"2026-09-26T14:21:00.959858Z"}
```
Bu, Day 66'nın anlamsız test metninden farklı olarak, **gerçek iş emri verisinin** RabbitMQ'ya doğru şekilde ulaştığının birebir kanıtı. Doğrulamadan sonra `docker compose down` ile yığın kapatıldı.

---

## Regresyon (Day 67)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65 (~59s → ~15-18s, NoOpEventPublisher düzeltmesinden sonra)
dotnet build StockPilot.slnx  → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx   → 0 Hata, 0 Uyarı
```

## Demo basitleştirmesi vs. üretim gereksinimi

Bugün: her yayında yeni bağlantı (Singleton, uzun ömürlü bağlantı yerine), tek kuyruk + varsayılan exchange (gerçek exchange/routing key tasarımı yok), gerçek bir tüketici servisi yok (sadece RabbitMQ'nun kendisinde biriken mesajlar, yönetim API'siyle okundu), event versiyonlama yok. Bunların hepsi Week 13/14'ün ilerleyen konuları.
