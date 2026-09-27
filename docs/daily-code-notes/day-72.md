# Day 72 — Kod Notları

Faz 4, Hafta 14, Gün 72. Konu: **retry + exponential backoff** — consumer'lara gerçek yeniden bağlanma mantığı. Dün canlı olarak gördük: `OutboxPublisher` bir event'i başarıyla yayınladı ama hiçbir consumer onu almadı, çünkü consumer'lar RabbitMQ hazır olmadan bağlanmaya çalışıp **kalıcı olarak** pes etmişti. Bugün bu kapatılıyor.

---

## `EventConsumerBase.ExecuteAsync` — bağlantı kurma mantığı bir döngüye alındı

```csharp
private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
// ilk basarisizlikta ne kadar beklenecek
private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
// bekleme suresi bunun ustune asla cikmayacak -- sonsuza kadar katlanmasin diye bir tavan

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var retryDelay = InitialRetryDelay;
// bekleme suresi, basarisiz her denemeden sonra buyuyecek bir DEGISKEN (sabit degil artik)

    while (!stoppingToken.IsCancellationRequested)
    {
// DUNKU koddan farkli: butun baglanma+dinleme mantigi artik bir while DONGUSU icinde.
// Uygulama kapanmadigi surece, bu dongu HICBIR ZAMAN kendiliginden bitmiyor.
        IConnection? connection = null;
        IChannel? channel = null;
        try
        {
            var factory = new ConnectionFactory { HostName = _hostName };
            connection = await factory.CreateConnectionAsync(stoppingToken);
            channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

            var exchangeName = EventQueueNaming.ExchangeNameFor<TEvent>();
            await channel.ExchangeDeclareAsync(exchange: exchangeName, type: ExchangeType.Fanout, /* ... */);

            var queueName = EventQueueNaming.QueueNameFor<TEvent>(_consumerName);
            await channel.QueueDeclareAsync(queue: queueName, /* ... */);
            await channel.QueueBindAsync(queue: queueName, exchange: exchangeName, /* ... */);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) => { /* Day 68/69'la ayni islem mantigi */ };

            await channel.BasicConsumeAsync(queue: queueName, /* ... */ consumer: consumer, /* ... */);

            retryDelay = InitialRetryDelay;
// BASARIYLA baglanip dinlemeye basladik -- bekleme suresi 1 saniyeye SIFIRLANIYOR.
// Neden: eger ILERIDE (baglantiyi kurduktan cok sonra) bir kopma olursa, o zaman da
// kucuk bir bekleme ile baslamak istiyoruz, onceki basarisizlik serisinin sisirdigi
// uzun bir sureyle degil.

            await Task.Delay(Timeout.Infinite, stoppingToken);
// baglanti ayaktayken burada bekliyoruz -- Day 68'deki ile ayni
        }
        catch (OperationCanceledException)
        {
            break;
// uygulama gercekten kapaniyor -- dongudan TAMAMEN cikiliyor, bu normal
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "{ConsumerName} could not connect to (or lost its connection to) RabbitMQ; retrying in {RetryDelaySeconds}s",
                _consumerName, retryDelay.TotalSeconds);
// DUNKU koddan farkli: artik "app yeniden baslatilana kadar" DEGIL, "su kadar saniye
// sonra tekrar denenecek" deniyor -- ve GERCEKTEN tekrar denenecek

            try
            {
                await Task.Delay(retryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
// bekleme SIRASINDA uygulama kapanirsa, oradan da cikilabilmeli
            }

            var doubledSeconds = retryDelay.TotalSeconds * 2;
            retryDelay = TimeSpan.FromSeconds(Math.Min(doubledSeconds, MaxRetryDelay.TotalSeconds));
// ISTE EXPONENTIAL BACKOFF BURADA: bekleme suresi ikiye katlaniyor (1->2->4->8->16->30,
// 30'dan sonra Math.Min sayesinde 30'da SABIT kaliyor, sonsuza kadar buyumuyor)
        }
        finally
        {
            if (channel is not null) await channel.DisposeAsync();
            if (connection is not null) await connection.DisposeAsync();
// basarili da olsa basarisiz da olsa, acilmis olabilecek baglanti/kanal kapatiliyor --
// dongu bir sonraki turunde SIFIRDAN, temiz bir baglanti deneyecek
        }
    }
// dongu sonu -- while kosulu tekrar kontrol ediliyor, hala calisiliyorsa (ve kapanma
// istenmemisse) baglanma denemesi BASTAN basliyor
}
```

**Neden bu şekilde yazıldı:** Dünkü kod, `try`/`catch`'in **tek seferlik** olmasıydı — başarısız olursa `catch` bloğu çalışıp bir uyarı loglar, sonra `ExecuteAsync` metodunun kendisi **biterdi**. Bir `BackgroundService`'in `ExecuteAsync`'i bittiğinde, o servis bir daha **hiç çalışmaz** (uygulama yeniden başlatılana kadar). Bugün, tüm bu mantık bir `while` döngüsüne alınarak, `catch` bloğunun sonunda **dönguden çıkılmıyor, sadece bekleyip tekrar en başa dönülüyor** — böylece `ExecuteAsync` asla "biten" bir metot olmuyor, uygulama kapanana kadar sürekli ya bağlı ya da yeniden bağlanmaya çalışıyor durumda kalıyor.

**Neden exponential backoff (sabit 1 saniyelik bekleme değil):** Eğer RabbitMQ gerçekten uzun süre kapalıysa (örneğin bakımda), sabit 1 saniyede bir deneme, RabbitMQ'ya (ve loglara) gereksiz bir yoğun trafik/gürültü oluştururdu. Süreyi katlayarak artırmak (ama bir tavana kadar), "ilk birkaç saniyede hızlı dene, uzun sürerse daha seyrek dene" dengesini kuruyor.

---

## Canlı demonstrasyon — dünkü senaryonun aynısı, bu sefer başarıyla

RabbitMQ kapalıyken uygulama başlatıldı. Loglarda, **artan** bekleme süreleriyle tekrar tekrar denemeler görüldü:
```
"notifications" ... retrying in 1s
"audit" ... retrying in 1s
"notifications" ... retrying in 2s
"audit" ... retrying in 2s
"audit" ... retrying in 4s
"notifications" ... retrying in 4s
"audit" ... retrying in 8s
"notifications" ... retrying in 8s
"audit" ... retrying in 16s
```
Tam olarak beklenen `1, 2, 4, 8, 16...` ilerlemesi.

RabbitMQ tekrar açıldı — **API'ye hiç dokunulmadan**. Birkaç saniye içinde "retrying" logları kesildi (consumer'lar başarıyla bağlandı). Ardından bir iş emri tamamlandı ve **her iki consumer da**, hiçbir yeniden başlatma olmadan, event'i doğru şekilde aldı:
```json
{"Category":"FieldOps.Api.Application.LoggingNotificationSender","Message":"Notification: Work order 'Fix the retry demo' has been completed..."}
{"Category":"FieldOps.Api.Application.WorkOrderCompletedAuditConsumer","Message":"Audit: work order 1 ('Fix the retry demo') completed at ..."}
```

---

## Regresyon (Day 72)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65 (~8-9dk, bilinen ortam yavaşlığı, yeni bir regresyon yok —
                                       consumer'lar testlerde zaten FieldOpsApiFactory tarafından
                                       tamamen kaldırılıyor, Day 69'dan beri)
dotnet build StockPilot.slnx  → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx   → 0 Hata, 0 Uyarı
```

## Demo basitleştirmesi vs. üretim gereksinimi

Yeniden deneme sonsuza kadar devam ediyor — bir noktada "artık dene ama alarm seviyesini yükselt" gibi bir mekanizma yok. Tavan (30 saniye) sabit kodlanmış, yapılandırılabilir değil. Idempotent tüketim (aynı mesaj RabbitMQ tarafından birden fazla teslim edilirse ne olur) hâlâ ele alınmadı — Week 14'ün ilerleyen bir konusu.
