# Day 66 — Kod Notları

Faz 4, Hafta 13, Gün 66 — **Distributed FieldOps'un ilk günü**. Konu: **senkron vs. asenkron iletişim, RabbitMQ kurulumu**. Bugün henüz gerçek bir domain event yayınlanmıyor — sadece RabbitMQ'nun kendisi ayağa kaldırılıp, FieldOps.Api'den tek bir mesajın gerçekten gidip geldiği kanıtlanıyor.

Bu bölüm, "RabbitMQ nedir, ne işe yarar" sorusunu sıfırdan, hiç bilmediğinizi varsayarak baştan anlatıyor — daha sonra "bugün koda tam olarak ne eklendi" kısmına geçiliyor.

---

## 0. RabbitMQ nedir? Neden ihtiyacımız var? (Sıfırdan anlatım)

### 0.1. Önce gerçek problemi hatırlayalım: `WorkOrdersController.Complete` (Day 51)

En somut örnek zaten kendi kodumuzda var. `Complete` action'ına (bir iş emri tamamlandığında çalışan kod) bakalım:

```csharp
public async Task<ActionResult<WorkOrderDto>> Complete(...)
{
    // ... iş emri Completed yapılıyor ...

    if (updated.CustomerId is not null)
    {
        try
        {
            await _notificationSender.NotifyAsync(
                $"Work order '{updated.Title}' has been completed and is awaiting your approval.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send completion notification...");
        }
    }

    return Ok(ToDto(updated));
}
```

Burada `await _notificationSender.NotifyAsync(...)` satırına dikkat edin. Bu, **senkron bir çağrı** — yani `Complete` isteğini yapan istemci (mesela bir mobil uygulama), sunucunun bildirim gönderme işlemini **bitirmesini bekliyor**, ancak ondan sonra kendi cevabını (`200 OK`) alıyor. Bugün bu bildirim sadece bir log satırı yazdığı için (Day 51'in `LoggingNotificationSender`'ı) bu hiç fark edilmiyor — göz açıp kapayıncaya kadar bitiyor.

**Ama gerçek bir bildirim sistemi düşünün** — bir SMS servisi, bir e-posta servisi, bir push notification servisi. Bunlar:
- **Yavaş olabilir** (bir dış API'ye ağ üzerinden istek atmak, veritabanına erişmekten çok daha yavaş olabilir — 2-3 saniye sürebilir).
- **Geçici olarak çökük olabilir** (SMS sağlayıcısının kendi sunucusu o an sorun yaşıyor olabilir).
- **Aynı anda birden fazla farklı sistemin ilgilenmesini gerektirebilir** — mesela iş emri tamamlandığında hem müşteriye SMS atılsın, hem bir raporlama sistemi bu bilgiyi kaydetsin, hem bir arama indeksi güncellensin. Bunların **hepsini** `Complete` action'ının içine, birbiri ardına, senkron olarak yazsaydık, action'ın çalışma süresi bu üç sistemin **toplam** süresi kadar uzardı, ve üçünden biri çökükse (mesela arama servisi bakımda), `Complete` isteğinin **kendisi de** başarısız olurdu — iş emri aslında gayet başarıyla tamamlanmışken!

Bu, **senkron (synchronous) iletişimin** temel zayıflığı: **çağıran, çağrılanın cevap vermesini bekler; çağrılan yavaşsa ya da çökükse, çağıran da bundan doğrudan etkilenir.** İki sistem parçası birbirine **sıkı sıkıya bağlı (tightly coupled)** hâle gelir.

### 0.2. Basit bir analoji: telefon görüşmesi vs. posta kutusu

- **Senkron iletişim = telefon görüşmesi.** Karşı tarafı arıyorsunuz, hat açık kalıyor, o cevap verene/işi bitirene kadar siz de bekliyorsunuz, telefonu kapatamıyorsunuz. Karşı taraf meşgulse ya da telefonu açmıyorsa, sizin işiniz de bloke oluyor.
- **Asenkron / mesaj tabanlı iletişim = bir mektubu postaneye bırakmak.** Mektubu postaneye bırakıyorsunuz, postane mektubu alıyor ("teslim aldım" makbuzu veriyor), siz işinize devam ediyorsunuz. Mektubun ne zaman, kim tarafından okunacağı sizi hiç ilgilendirmiyor. Alıcı o an evde olmasa bile mektup postanede bekliyor, alıcı eve gelince okuyor. Postane aynı zamanda **aynı mektubun kopyasını birden fazla kişiye** de dağıtabilir (örnek: bir sirküler mektup).

**RabbitMQ, işte bu "postane"nin yazılım dünyasındaki karşılığı** — buna **message broker (mesaj aracısı)** deniyor. Bir sistem parçası (üretici/producer — mesela FieldOps.Api) bir mesajı RabbitMQ'ya bırakıyor, RabbitMQ bu mesajı güvenle saklıyor, ve ilgilenen tüketici(ler) (consumer — mesela bildirim servisi) hazır olduğunda bu mesajı alıp işliyor. Üretici ile tüketici **birbirini hiç tanımıyor, birbirine hiç doğrudan bağlanmıyor** — ikisi de sadece RabbitMQ ile konuşuyor.

### 0.3. RabbitMQ'nun temel parçaları — yine postane analojisiyle

| RabbitMQ kavramı | Postane analojisi | Açıklama |
|---|---|---|
| **Producer (üretici)** | Mektup gönderen kişi | Bir mesaj oluşturup RabbitMQ'ya gönderen taraf. Bugün: FieldOps.Api'nin kendisi. |
| **Exchange** | Postanenin sıralama merkezi | Gelen mesajları, bir kurala göre (routing key'e bakarak) doğru kuyruğa/kuyruklara yönlendiren bileşen. Producer, mesajı **doğrudan bir kuyruğa değil**, her zaman bir exchange'e gönderir. |
| **Queue (kuyruk)** | Posta kutusu | Mesajların, bir tüketici onları işleyene kadar **beklediği** yer. Bir kuyruğa birden fazla üretici mesaj bırakabilir, birden fazla tüketici de aynı kuyruktan mesaj okuyabilir (yükü paylaşarak). |
| **Routing key** | Zarftaki adres/posta kodu | Exchange'e "bu mesajı hangi kuyruğa/kuyruklara götüreceğini" söyleyen bir etiket. |
| **Consumer (tüketici)** | Mektubu okuyan alıcı | Bir kuyruğu dinleyip, oraya düşen mesajları işleyen taraf. Bugün: FieldOps.Api'nin içindeki, aynı süreçteki geçici bir demo consumer'ı; gelecekte ayrı bir servis (bildirim servisi gibi) olabilir. |
| **Acknowledgment (ack)** | "Mektubu aldım, okudum" makbuzu | Tüketicinin RabbitMQ'ya "bu mesajı başarıyla işledim, silebilirsin" demesi. Bu makbuz verilmeden RabbitMQ mesajı saklamaya devam eder — tüketici çökerse, mesaj kaybolmaz, başka bir tüketiciye (ya da tüketici yeniden ayağa kalkınca kendisine) tekrar teslim edilir. |

### 0.4. Bu yapı bize gerçekte ne kazandırıyor?

1. **Ayrıştırma (decoupling):** FieldOps.Api, bildirim servisinin (ya da raporlama servisinin) IP adresini, portunu, hatta var olup olmadığını bile bilmek zorunda değil. Sadece "iş emri tamamlandı" mesajını RabbitMQ'ya bırakıyor; kim, ne zaman, nasıl okuyacak — bu, FieldOps.Api'yi hiç ilgilendirmiyor.
2. **Dayanıklılık (buffering):** Bildirim servisi 5 dakikalığına çökse bile, mesajlar RabbitMQ'da (durable bir kuyrukta) birikir; servis geri geldiğinde hepsini sırayla işler. Hiçbir "iş emri tamamlandı" bilgisi kaybolmaz.
3. **Birden fazla bağımsız tüketici (fan-out):** Aynı "iş emri tamamlandı" mesajını, doğru exchange/kuyruk tasarımıyla, hem bildirim servisi hem raporlama servisi hem arama indeksleme servisi **aynı anda, birbirinden habersiz şekilde** alabilir. Bugünkü tek-kuyruk örneğimiz bunu henüz göstermiyor (bu, ilerleyen bir günün — gerçek exchange/routing key tasarımının — konusu), ama RabbitMQ'nun asıl gücü tam olarak burada.
4. **Hız:** `Complete` isteğini yapan istemci, artık sadece "mesaj RabbitMQ'ya bırakıldı mı" diye bekliyor (çok hızlı, milisaniyeler), bildirim servisinin gerçekten SMS göndermesini beklemek zorunda kalmıyor.

### 0.5. Bugün tam olarak ne yapmadık (sınırları netleştirmek için)

- `WorkOrdersController.Complete`'e **hiç dokunmadık** — Day 51'in senkron `_notificationSender.NotifyAsync(...)` çağrısı hâlâ aynı şekilde duruyor. Bugünkü RabbitMQ kodu, controller'dan tamamen bağımsız, geçici bir demo.
- Gerçek bir "iş emri tamamlandı" mesajı **yayınlamadık** — sadece "Hello from FieldOps.Api, ..." gibi anlamsız bir test mesajı gönderdik.
- Birden fazla kuyruğa dağıtım (gerçek exchange/routing key tasarımı) **kurmadık** — bugünkü mesaj, doğrudan tek bir kuyruğa gitti (aşağıda "varsayılan exchange" bölümünde bunun neden böyle olduğu anlatılıyor).

Bu sınır bilinçli — bugünün tek amacı, "RabbitMQ dediğimiz şey gerçekten çalışıyor mu, FieldOps.Api ona gerçekten bağlanabiliyor mu" sorusuna evet diyebilmekti. Gerçek domain event'ler, ilerleyen günlerde, bu sağlam temel üzerine eklenecek.

---

## 1. `docker-compose.yml` — yeni `rabbitmq` servisi

```yaml
rabbitmq:
  image: rabbitmq:3-management
  ports:
    - "5672:5672"
    - "15672:15672"
  networks:
    - fieldops
```

**Neden bu şekilde yazıldı:** Day 59'un `sqlserver`/`redis` servisleriyle **birebir aynı desen**: aynı `fieldops` ağında, servis adıyla (`rabbitmq`) erişilebilir — `localhost` değil, `host.docker.internal` değil, Day 58'in named-instance/discovery sorununun bir benzeri hiç yaşanmıyor. `rabbitmq:3-management` (düz `rabbitmq:3` değil) seçildi çünkü bu imaj, RabbitMQ'nun kendi **web yönetim arayüzünü** (port 15672) de içeriyor — bugünkü kanıtı sadece loglardan değil, görsel olarak da (kuyruk, mesaj sayısı) görebilmek için.

**`fieldops-api`'nin `depends_on` listesine `rabbitmq` eklendi, `Redis__ConnectionString`'in yanına `RabbitMq__HostName: "rabbitmq"` eklendi** — Day 48'in Redis bağlantı dizesi geçersiz kılma desenin aynısı.

---

## 2. `src/FieldOps.Api/appsettings.Development.json` — `RabbitMq:HostName`

```json
"RabbitMq": {
  "HostName": "localhost"
}
```

**Neden bu şekilde yazıldı:** Docker Compose dışında (yerel `dotnet run`) çalıştırıldığında RabbitMQ'ya `localhost`'tan erişilecek — `Redis:ConnectionString`'in izlediği aynı "yapılandırma değeri, ortama göre değişir" deseni.

---

## 3. `RabbitMQ.Client` NuGet paketi (7.2.2)

**Neden bu paket:** RabbitMQ ile konuşmanın resmi .NET istemci kütüphanesi. `dotnet add package RabbitMQ.Client` ile eklendi (versiyon numarası elle yazılmadı, NuGet'in en güncel uyumlu sürümü kendisi seçmesine izin verildi).

**Canlı bir keşif — API'nin tam imzası tahminle değil, ölçülerek bulundu:** RabbitMQ.Client'ın 7.x sürümü, önceki (5.x/6.x) sürümlere göre **tamamen asenkron** bir API'ye geçmiş (`BasicPublishAsync`, `BasicConsumeAsync` gibi) ve imzaları hafızadan tahmin etmek yerine, `/tmp` altında **geçici, atılabilir bir konsol projesi** oluşturulup `System.Reflection` ile `IChannel` arayüzünün gerçek metot imzaları **ölçüldü**:
```csharp
foreach (var m in typeof(RabbitMQ.Client.IChannel).GetMethods().Where(m => m.Name is "BasicPublishAsync" or "BasicConsumeAsync"))
    Console.WriteLine(m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => ...)) + ")");
```
Çıktı, `BasicConsumeAsync`'in `consumerTag`/`noLocal`/`exclusive`/`arguments` parametrelerinin **hiçbirinin varsayılan değeri olmadığını**, ve `BasicPublishAsync`'in bir `TProperties basicProperties` (generic, `IReadOnlyBasicProperties`/`IAmqpHeader` kısıtlı) parametresi gerektirdiğini gösterdi. Bu, derleyicinin ilk denemelerde verdiği "eksik parametre" hatalarını (tahmin-düzelt-tekrar-dene yerine) **kesin olarak** çözdü. Bu, tam olarak bu workspace'in "varsayma, doğrula" ilkesinin bir API'nin kendisine uygulanmış hâli.

---

## 4. `src/FieldOps.Api/Program.cs` — geçici RabbitMQ demonstrasyon bloğu

```csharp
if (app.Environment.IsDevelopment())
{
// bu blok sadece Development ortamında çalışsın diye kontrol ediliyor — Production'da bu geçici demo kodu hiç çalışmayacak
    var rabbitMqHostName = builder.Configuration["RabbitMq:HostName"] ?? "localhost";
// appsettings.Development.json'daki (ya da docker-compose.yml'in enjekte ettiği) "RabbitMq:HostName" değeri okunuyor; hiç yoksa "localhost" varsayılıyor
    var factory = new RabbitMQ.Client.ConnectionFactory { HostName = rabbitMqHostName };
// RabbitMQ sunucusuna nasıl bağlanılacağını tarif eden bir "fabrika" nesnesi oluşturuluyor — bu satırda henüz gerçek bir bağlantı açılmıyor, sadece ayarlar hazırlanıyor

    try
    {
// RabbitMQ o an ayakta olmayabilir (aşağıdaki canlı kanıtta tam olarak bu yaşandı) — bu yüzden tüm RabbitMQ işlemleri try/catch içine alınıyor, bir hata uygulamanın tamamını çökertmesin diye
        await using var connection = await factory.CreateConnectionAsync();
// factory kullanılarak RabbitMQ'ya gerçek bir TCP bağlantısı (connection) açılıyor; "await using" sayesinde bu blok bittiğinde bağlantı otomatik olarak kapatılıyor
        await using var channel = await connection.CreateChannelAsync();
// aynı bağlantı üzerinden bir "channel" (kanal) açılıyor — asıl yayınlama/tüketme işlemleri connection üzerinden değil, channel üzerinden yapılır; connection'lar pahalı, channel'lar ucuzdur, bu yüzden gerçek uygulamalarda tek bir connection üzerinde birden çok channel açılır

        const string queueName = "fieldops.day66.demo";
// bugünkü demo için kullanılacak kuyruğun adı, sabit bir metin olarak tanımlanıyor
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: false, autoDelete: false);
// bu isimde bir kuyruk RabbitMQ'da yoksa oluşturuluyor, zaten varsa hiçbir şey yapmıyor (idempotent); durable:false demek, RabbitMQ yeniden başlarsa bu kuyruğun kaybolacağı anlamına geliyor — bugünkü demo için yeterli bir basitleştirme

        var consumer = new RabbitMQ.Client.Events.AsyncEventingBasicConsumer(channel);
// "bu kanaldan mesaj gelirse ne yapılacağını" tanımlayacağımız bir tüketici (consumer) nesnesi oluşturuluyor
        consumer.ReceivedAsync += async (_, ea) =>
        {
// kuyruğa bir mesaj düştüğü anda RabbitMQ.Client kütüphanesi bu kod bloğunu (bir event handler) otomatik olarak, asenkron şekilde çalıştırıyor
            var received = System.Text.Encoding.UTF8.GetString(ea.Body.ToArray());
// mesaj RabbitMQ'da ham bayt dizisi (byte[]) olarak taşınıyor; burada bu baytlar tekrar okunabilir bir metne (string) çevriliyor
            app.Logger.LogInformation("Day 66 RabbitMQ demo: consumer received message: {Message}", received);
// gelen mesaj, uygulamanın yapılandırılmış (Day 55) log altyapısına bilgi seviyesinde yazılıyor — canlı kanıtın loglarda görünmesini sağlayan satır bu
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
// RabbitMQ'ya "bu mesajı başarıyla işledim, artık saklamana gerek yok, silebilirsin" bilgisi veriliyor (manuel onay/acknowledgment) — bu çağrı yapılmazsa RabbitMQ mesajı elinde tutmaya devam eder ve tekrar teslim etmeye çalışır
        };
        await channel.BasicConsumeAsync(
            queue: queueName, autoAck: false, consumerTag: string.Empty,
            noLocal: false, exclusive: false, arguments: null, consumer: consumer);
// yukarıda tanımlanan consumer, artık gerçekten queueName kuyruğunu "dinlemeye" başlıyor; autoAck:false demek "mesajları otomatik onaylama, ben (yukarıdaki ReceivedAsync içinde) elle onaylayacağım" demek

        var message = $"Hello from FieldOps.Api, {DateTime.UtcNow:O}";
// gönderilecek test mesajının metni oluşturuluyor — her çalıştırmada farklı olsun diye içine o anki UTC zaman damgası ekleniyor
        var body = System.Text.Encoding.UTF8.GetBytes(message);
// RabbitMQ mesajları metin değil, ham bayt dizisi (byte[]) olarak taşıdığı için, metin UTF-8 formatında baytlara çevriliyor
        await channel.BasicPublishAsync(
            exchange: string.Empty, routingKey: queueName, mandatory: false,
            basicProperties: new RabbitMQ.Client.BasicProperties(), body: (ReadOnlyMemory<byte>)body);
// mesaj gerçekten RabbitMQ'ya gönderiliyor (yayınlanıyor); exchange boş bırakıldığı için RabbitMQ'nun "varsayılan exchange"i kullanılıyor — bu da routingKey'i (queueName) doğrudan aynı isimdeki kuyruğa eşliyor
        app.Logger.LogInformation("Day 66 RabbitMQ demo: published message to queue '{Queue}'", queueName);
// mesajın başarıyla gönderildiği bilgisi de log altyapısına yazılıyor
    }
    catch (Exception ex)
    {
// RabbitMQ'ya bağlanırken (ya da yayınlarken/tüketirken) herhangi bir hata olursa çalışma buraya düşüyor
        app.Logger.LogWarning(ex, "Day 66 RabbitMQ demo skipped: RabbitMQ is not reachable at '{HostName}'", rabbitMqHostName);
// hata, uygulamayı çökertmek yerine sadece bir uyarı olarak loglanıyor — böylece RabbitMQ henüz ayakta değilse bile FieldOps.Api'nin geri kalanı (health check'ler, controller'lar) normal şekilde çalışmaya devam ediyor
    }
}
```

**Neden bu şekilde yazıldı:** RoadmapOS Day 2'nin geçici konsol doğrulama bloğuyla **aynı rol**: bugünün amacı kalıcı bir özellik yazmak değil, "mekanizma gerçekten çalışıyor mu" sorusunu kanıtlamak. Bu yüzden:
- Sadece `Development` ortamında çalışıyor.
- `try`/`catch` ile sarılı — RabbitMQ henüz ayakta değilse (aşağıdaki canlı kanıtta tam olarak bu yaşandı), uygulamanın **tamamı çökmesin**, sadece bir uyarı loglayıp normal şekilde devam etsin.
- Yorum satırında açıkça "TEMP" ve "removed once a real producer/consumer replaces it" yazıyor — ileride gerçek bir domain event eklendiğinde bu blok tamamen silinecek, kalıcı bir kod parçası değil.

**Nasıl çalışır:**
- `ConnectionFactory { HostName = ... }` → RabbitMQ sunucusuna TCP bağlantısı kurmak için gereken minimum yapılandırma.
- `CreateConnectionAsync()`/`CreateChannelAsync()` → bir **connection** (TCP bağlantısının kendisi) içinde bir **channel** (asıl işlemlerin — publish/consume — yapıldığı hafif, çoklu-kullanılabilir bir "alt kanal") açılıyor; RabbitMQ istemcilerinde connection'lar pahalı, channel'lar ucuzdur — bu yüzden gerçek uygulamalarda tek bir connection üzerinde birçok channel açılır.
- `QueueDeclareAsync(...)` → kuyruk zaten yoksa oluşturuluyor (idempotent — zaten varsa hata vermiyor); `durable: false` demek, RabbitMQ yeniden başlarsa kuyruğun kaybolacağı anlamına geliyor — bugünkü demo için kabul edilebilir bir basitleştirme.
- `AsyncEventingBasicConsumer` + `ReceivedAsync` olayı → tüketici tarafı: kuyruğa bir mesaj düştüğünde bu callback **asenkron olarak** tetikleniyor. `BasicAckAsync(ea.DeliveryTag, ...)` → RabbitMQ'ya "bu mesajı başarıyla işledim, tekrar gönderme" bilgisini veren **manuel onay (acknowledgment)** — `autoAck: false` seçildiği için bu adım zorunlu; gerçek bir sistemde bu, "mesaj işlenirken uygulama çökerse, mesaj kaybolmasın, tekrar teslim edilsin" garantisinin temelidir (Week 14'ün "idempotent consumer" konusuyla doğrudan ilişkili).
- `BasicPublishAsync(exchange: string.Empty, routingKey: queueName, ...)` → boş exchange adı, RabbitMQ'nun **varsayılan (default) exchange**'i anlamına geliyor — bu özel exchange, `routingKey`'i doğrudan aynı isimdeki kuyruğa eşler, yani bugünkü gibi "tek bir kuyruğa doğrudan gönder" senaryosu için basit bir kısayol. Gerçek exchange/routing key kavramları (birden fazla kuyruğa dağıtım) bu haftanın ilerleyen bir gününün konusu.

---

## 5. Canlı demonstrasyon — ve Day 59/60'ın aynı dersinin bir kez daha yaşanması

`docker compose up --build -d` ile tüm yığın (SQL Server, Redis, RabbitMQ, FieldOps.Api) ayağa kaldırıldı. **İlk çalıştırmada** loglar şunu gösterdi:
```
Day 66 RabbitMQ demo skipped: RabbitMQ is not reachable at 'rabbitmq'
BrokerUnreachableException ... Connection refused
```
Bu, **beklenmedik bir hata değil** — Day 59/60'ın tam olarak öğrettiği şeyin bir tekrarı: `depends_on`, sadece konteynerin **başlatıldığını** garanti eder, içindeki servisin **gerçekten hazır olduğunu** değil. RabbitMQ, SQL Server gibi kendi iç başlatma süresine ihtiyaç duyuyor; `fieldops-api` konteyneri, RabbitMQ tam olarak dinlemeye başlamadan önce ayağa kalkıp demo bloğunu çalıştırdı. Bugünkü `try`/`catch` **tam olarak bunun için** oradaydı — uygulama çökmedi, sadece bir uyarı loglayıp normal şekilde çalışmaya devam etti (health check'ler, controller'lar vs. hiç etkilenmedi).

RabbitMQ'nun başlaması için birkaç saniye beklenip `docker compose restart fieldops-api` ile sadece API konteyneri yeniden başlatıldı — bu sefer:
```
Day 66 RabbitMQ demo: published message to queue 'fieldops.day66.demo'
Day 66 RabbitMQ demo: consumer received message: Hello from FieldOps.Api, 2026-09-26T13:39:02.0102594Z
```
Mesaj gerçekten yayınlandı ve **aynı süreç içinde, ayrı bir asenkron callback üzerinden** geri okundu. Bu, RabbitMQ yönetim API'si üzerinden de bağımsız olarak doğrulandı:
```
curl -u guest:guest http://localhost:15672/api/queues
→ "message_stats":{"ack":1,"deliver":1,...}
```
— tam olarak beklenen: bir mesaj teslim edildi, bir mesaj onaylandı.

Doğrulamadan sonra `docker compose down` ile yığın kapatıldı.

---

## Regresyon (Day 66)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65 (yeni test yok — bugün altyapı/demo günüydü)
dotnet build StockPilot.slnx  → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx   → 0 Hata, 0 Uyarı
```

## Demo basitleştirmesi vs. üretim gereksinimi

Bugün: tek bir statik kuyruk adı, varsayılan exchange, `durable: false`, gerçek bir domain event yok, hata durumunda sadece loglayıp devam etme (retry/dead-letter yok — Week 14'ün konusu). Üretimde gerekecekler: dayanıklı (durable) kuyruklar, gerçek exchange/routing key tasarımı, idempotent tüketim, outbox pattern (Week 14), ve RabbitMQ bağlantı bilgilerinin (kullanıcı adı/şifre) gerçek bir secret mekanizmasından gelmesi (bugün varsayılan `guest`/`guest` kimlik bilgileri kullanıldı — sadece yerel/demo ortamı için kabul edilebilir).
