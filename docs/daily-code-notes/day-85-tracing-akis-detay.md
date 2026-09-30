# Day 85 — Distributed Tracing Akışı: Bir Mesajın Yayınlanmasından İki Ayrı Process'teki Tüketilmesine

Bu doküman, `day-71-rabbitmq-akis-detay.md` ve `day-80-elasticsearch-akis-detay.md` ile **birebir aynı formatta** — her yeni metot/fonksiyon, "bu NEREDEN geliyor, NE işe yarıyor, çağrıldığı anda veri NE HALDE" sorularına cevap verilerek, adım adım takip ediliyor. Bugünkü fark: bu sefer RabbitMQ'nun kendisini değil, **trace verisinin** (TraceId/SpanId) bu akış boyunca nasıl taşındığını izliyoruz — ve rakamlar **uydurma değil**, bu oturumda gerçekten çalıştırılıp loglara düşen **gerçek** değerler.

**Senaryo:** İş emri `id=36`, Org 1, tamamlanıyor:
```
POST /api/workorders/36/complete
X-Organization-Id: 1
X-Employee-Id: 2
```

---

## 0. ÖNCE, hiçbir istek gelmeden: `Program.cs`'te kurulan "dinleme" mekanizması

Bu adım, istekten ÖNCE, uygulama `dotnet run` ile ayağa kalkarken **bir kere** çalışıyor — ama bundan sonraki HER ŞEYİN çalışması buna bağlı, o yüzden en başta anlaşılması gerekiyor.

`src/FieldOps.Api/Program.cs`:
```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("FieldOps.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddSource(FieldOpsTracing.MessagingSourceName)
        .AddConsoleExporter());
```
* `AddOpenTelemetry()` — OpenTelemetry SDK'sının DI konteynerine kendini kaydetmesi.
* `.ConfigureResource(resource => resource.AddService("FieldOps.Api"))` — üretilecek HER trace verisinin üzerine "bu, `FieldOps.Api` process'inden geldi" etiketini yapıştırıyor. Bu etiket olmasaydı, iki process'in trace'lerini birbirinden AYIRT EDEMEZDİK (aşağıda 5b'de bu etiketin gerçek kanıtı var).
* `.AddSource(FieldOpsTracing.MessagingSourceName)` — **kritik satır.** `"FieldOps.Messaging"` isimli bir `ActivitySource`'u (aşağıda 3'te göreceğiz) OpenTelemetry SDK'sına "bunu DİNLE" diyor. **Bu satır olmasaydı**, kod içinde `ActivitySource.StartActivity(...)` çağrılsa bile, HİÇBİR ŞEY toplanmaz, hiçbir yere yazılmazdı — `ActivitySource`, .NET'in kendi, OpenTelemetry'den BAĞIMSIZ, dinleyen biri olmadıkça sessizce çalışan bir mekanizma.
* `.AddAspNetCoreInstrumentation()` — gelen HER HTTP isteği için OTOMATİK bir span üretiyor (bugünkü akışta bunu KULLANMIYORUZ ama neden olduğunu bilmek önemli — aşağıda 1'de değineceğiz).
* `.AddConsoleExporter()` — üretilen trace'lerin nereye GİDECEĞİ: bugün, konsola (loglara).

`FieldOps.NotificationService/Program.cs`'te de AYNI kayıt var, sadece `AddAspNetCoreInstrumentation()` YOK (bu servisin hiç HTTP endpoint'i yok) ve `AddService("FieldOps.NotificationService")` — FARKLI bir isimle.

---

## 1. İstek `WorkOrdersController.Complete`'e ulaşıyor — bugün YENİ bir şey YOK

`Complete`, Day 71-80'de kurduğumuz akışın aynısı: SQL'e yazar, outbox satırı(nı) bırakır, HEMEN `200 OK` döner. **`AddAspNetCoreInstrumentation()`'ın ürettiği span, bu isteğin kendisi için** — ama bu span, bu isteğin BİTMESİYLE sona eriyor, ve outbox satırı henüz RabbitMQ'ya hiç gitmedi. Yani bu span, bugün izlemek istediğimiz "mesajın yolculuğu" ile **BAĞLANTISIZ** — tam olarak bu yüzden bugünkü trace, birazdan göreceğimiz gibi, BAŞKA bir noktadan başlıyor.

---

## 2. Bambaşka bir zaman çizelgesinde: `OutboxPublisher` tikinde mesajı buluyor

Day 71'in aynı mekanizması — 5 saniyelik `PeriodicTimer`, `GetUnpublishedOutboxMessages()`, `eventPublisher.PublishAsync(domainEvent, message.Id.ToString(), cancellationToken)` çağrısı. Bugün YENİ bir şey yok — ama BURADAN SONRASI, tamamen yeni.

---

## 3. `RabbitMqEventPublisher.PublishAsync` — trace'in GERÇEKTEN DOĞDUĞU an

```csharp
using var activity = FieldOpsTracing.MessagingSource.StartActivity(
    $"publish {typeof(TEvent).Name}", ActivityKind.Producer);
```
* `FieldOpsTracing.MessagingSource` — bölüm 0'da `.AddSource(...)` ile "dinlemeye alınan" AYNI `ActivitySource` nesnesi (`src/FieldOps.Api/Application/FieldOpsTracing.cs`'teki `static readonly` alan).
* `.StartActivity("publish WorkOrderCompletedEvent", ActivityKind.Producer)` — **YENİ bir `Activity` (span) nesnesi yaratıyor.** Bu satır çalıştığı anda:
  * `Activity.Current` (o anki çağrı zincirinde "şu an aktif olan" span'ı tutan, .NET'in kendi, ambient/örtük bir değişkeni) kontrol ediliyor. Bugünkü akışta bu **`null`** — çünkü `OutboxPublisher`'ın tiki, hiçbir HTTP isteğinin "içinde" çalışmıyor, kendi bağımsız arka plan döngüsü. Ebeveyn (parent) YOK.
  * Ebeveyn olmadığı için, .NET **rastgele, yepyeni bir `TraceId`** üretiyor — bugünkü canlı çalıştırmada bu **`fce452fa288daebab70b6af69c8d0e56`** oldu.
  * Bu `Activity`'ye kendi, yeni bir `SpanId`'si de veriliyor — bugün: **`b931e9e1b98f3728`**.
  * `ActivityKind.Producer` — bu span'ın "bir mesajlaşma sisteminin GÖNDEREN tarafı" olduğunu OpenTelemetry sözleşmesine göre etiketliyor (SDK'nın/exporter'ın span'ı doğru yorumlaması için).
* `using var activity = ...` — bu `activity` değişkeni, bu metodun sonuna kadar (`}` parantezine kadar) "açık" kalıyor. Metot bitip `Dispose()` çağrıldığında, `Activity`'nin `Duration`'ı (ne kadar sürdüğü) kaydediliyor VE bölüm 0'daki konsol exporter'a "bu span BİTTİ, şimdi yazdır" sinyali gidiyor.

```csharp
var headers = new Dictionary<string, object?>();
if (activity is not null)
{
    DistributedContextPropagator.Current.Inject(
        activity,
        headers,
        static (carrier, key, value) => ((Dictionary<string, object?>)carrier!)[key] = value);
}
```
* `DistributedContextPropagator` — .NET'in kendi (OpenTelemetry'ye ait DEĞİL, `System.Diagnostics` içinde, çekirdek .NET kütüphanesinin bir parçası), bir trace'in kimliğini **standart bir metin formatına** (W3C "traceparent" başlığı — `00-{TraceId}-{SpanId}-{flags}` şeklinde) çevirip taşıyan sınıf.
* `.Current` — .NET'in varsayılan olarak sağladığı, bu W3C formatını bilen hazır bir örnek.
* `.Inject(activity, headers, setter)` — üç parametre: (1) hangi `Activity`'nin kimliğini taşıyacağız (yukarıda yarattığımız `activity`), (2) bu kimliği NEREYE yazacağız (`headers`, bir `Dictionary`), (3) NASIL yazacağız (`setter` — bir lambda, "bana bir anahtar/değer ver, ben `headers[key] = value` yaparım" diyen küçük bir fonksiyon). `Inject`, içeride `activity.Id`'yi (traceparent formatındaki string'i) hesaplayıp, verdiğimiz `setter`'ı çağırarak `headers["traceparent"] = "00-fce452fa288daebab70b6af69c8d0e56-b931e9e1b98f3728-01"` gibi bir satır YAZIYOR.
* Bu satırdan SONRA, `headers` sözlüğünde artık trace'in kimliği VAR.

```csharp
await channel.BasicPublishAsync(
    ..., basicProperties: new BasicProperties { MessageId = messageId, Headers = headers }, ...);
```
Bu, Day 67'den beri tanıdığımız çağrı — TEK farkı, `Headers = headers` eklenmiş olması. Mesaj artık RabbitMQ'ya, içinde `traceparent` başlığıyla BİRLİKTE gidiyor.

---

## 4. RabbitMQ — mesajı, header'larıyla BİRLİKTE, iki kuyruğa da kopyalıyor

Day 69'dan beri bildiğimiz fanout mekanizması — hiç değişmedi. Ama bugün, kopyalanan mesajın **header'ları da dahil** kopyalandığını fark etmek önemli: `traceparent` değeri, HER İKİ kuyruğa da, DEĞİŞMEDEN ulaşıyor.

---

## 5a. `FieldOps.Api`'nin audit consumer'ı — AYNI process, farklı bir "tüketici"

`EventConsumerBase.cs`'in `ReceivedAsync` işleyicisi:
```csharp
using var activity = StartConsumerActivity(ea.BasicProperties.Headers);
```
`ea.BasicProperties.Headers` — RabbitMQ'nun bize teslim ettiği, `traceparent` değerini İÇEREN sözlük. Bu satır, aşağıdaki yardımcı metodu çağırıyor:

```csharp
private static Activity? StartConsumerActivity(IDictionary<string, object?>? headers)
{
    DistributedContextPropagator.Current.ExtractTraceIdAndState(
        headers,
        static (carrier, fieldName, out fieldValue, out fieldValues) =>
        {
            fieldValues = null; fieldValue = null;
            if (carrier is IDictionary<string, object?> dict && dict.TryGetValue(fieldName, out var value))
            {
                fieldValue = value switch { byte[] b => Encoding.UTF8.GetString(b), string s => s, _ => null };
            }
        },
        out var traceParent, out var traceState);

    ActivityContext.TryParse(traceParent, traceState, out var parentContext);

    return FieldOpsTracing.MessagingSource.StartActivity(
        "consume WorkOrderCompletedEvent", ActivityKind.Consumer, parentContext);
}
```
* `ExtractTraceIdAndState(headers, getter, out traceParent, out traceState)` — `Inject`'in TAM TERSİ: `Inject` bir `Activity`'yi metne ÇEVİRİRKEN, bu, metni GERİ OKUYUP ayrıştırıyor. `getter` lambda'sı çağrılıyor, `headers["traceparent"]`'ı okuyor — DİKKAT: değer burada `byte[]` olarak geliyor (RabbitMQ, teslim edilen bir mesajın header'larını AMQP tel formatından .NET'e byte dizisi olarak çözüyor — biz `Inject`'te bir `string` yazmış olsak bile). Bu yüzden `byte[] b => Encoding.UTF8.GetString(b)` satırı ŞART — **bu, canlı test edilerek keşfedilen gerçek bir davranış**, varsayılmadı.
* Sonuç: `traceParent = "00-fce452fa288daebab70b6af69c8d0e56-b931e9e1b98f3728-01"` — yayınlayanın YAZDIĞI DEĞERİN AYNISI, hiç bozulmadan.
* `ActivityContext.TryParse(traceParent, traceState, out parentContext)` — bu METİN string'ini, tekrar yapılandırılmış bir `ActivityContext` (TraceId + SpanId + flag'ler) NESNESİNE çeviriyor.
* `StartActivity("consume ...", ActivityKind.Consumer, parentContext)` — **işte kritik fark:** bölüm 3'teki çağrıdan farklı olarak, bu sefer BİR `parentContext` VERİLİYOR. .NET artık "yepyeni bir TraceId üretme, bunun yerine BU trace'in DEVAMI ol" diyor:
  * Yeni `Activity`'nin `TraceId`'si: **`fce452fa288daebab70b6af69c8d0e56`** — bölüm 3'teki İLE AYNI.
  * Yeni, kendi `SpanId`'si: bugün gerçekten **`324ec350077eed18`** çıktı.
  * `ParentSpanId`'si: **`b931e9e1b98f3728`** — bölüm 3'teki `publish` span'ının SpanId'si.

`HandleAsync` (audit consumer'ın gerçek işi — sadece log basmak), bu `activity`'nin `using` bloğunun İÇİNDE çalışıyor — yani bu iş sırasında `Activity.Current`, bu yeni consume span'ını gösteriyor.

---

## 5b. `FieldOps.NotificationService`'teki consumer — GERÇEKTEN BAŞKA bir process

Bu servisin KENDİ `EventConsumerBase.cs` kopyasındaki `StartConsumerActivity`, **satır satır AYNI** — ama bambaşka bir process'in belleğinde, bambaşka bir `FieldOpsTracing.MessagingSource` nesnesi üzerinde çalışıyor (Day 76'nın "aynı string, sıfır kod paylaşımı" ilkesi, bugün trace ID'ler için de geçerli).

`ea.BasicProperties.Headers`'ı okuyunca, bu process de **AYNI** `traceParent` string'ini (`"00-fce452fa...-b931e9e1...-01"`) buluyor — çünkü RabbitMQ, aynı mesajı, header'ları DEĞİŞTİRMEDEN, bu kuyruğa da kopyalamıştı (bölüm 4). Sonuç, bugün gerçekten gözlenen:
* `TraceId`: **`fce452fa288daebab70b6af69c8d0e56`** — YİNE AYNI, üçüncü kez.
* Kendi, yeni `SpanId`'si: **`89cad0193bcaf326`**.
* `ParentSpanId`: **`b931e9e1b98f3728`** — YİNE bölüm 3'teki `publish` span'ına bağlı.
* Ama bu sefer, konsol çıktısındaki `service.name` alanı **`FieldOps.NotificationService`** — bölüm 0'daki `.AddService("FieldOps.NotificationService")` kaydının, bu span'ın GERÇEKTEN farklı bir process'ten geldiğinin somut kanıtı.

---

## 6. Span'lar kapanıyor, konsola yazdırılıyor — "response"a kadar

Bu akışın bir HTTP "response"ı yok (adım 1'deki `Complete` isteği, bu üç span daha DOĞMADAN çoktan bitmişti) — ama her `using var activity = ...` bloğunun SONU (metodun/lambda'nın kapanış parantezi), o span'ın **doğal "bitişi"**:
1. `RabbitMqEventPublisher.PublishAsync` biter → `publish` span'ı kapanır → konsola yazdırılır.
2. `FieldOps.Api`'nin `ReceivedAsync` işleyicisi biter → audit'in `consume` span'ı kapanır → konsola yazdırılır.
3. `FieldOps.NotificationService`'in `ReceivedAsync` işleyicisi biter → onun `consume` span'ı kapanır → KENDİ konsoluna yazdırılır.

---

## Özet — tek bakışta akış (bugünkü GERÇEK trace/span kimlikleriyle)

```
[Program.cs, ISTEKTEN ONCE — bir kere]
AddOpenTelemetry().WithTracing(t => t.AddSource("FieldOps.Messaging").AddConsoleExporter())
   -> "FieldOps.Messaging" ActivitySource'u ARTIK DINLENIYOR.


[ZAMAN CIZELGESI 1 — HTTP istegi, saniyeler icinde biter, bugunku trace'le ILGISIZ]
POST /api/workorders/36/complete -> SQL + outbox satiri -> 200 OK


[ZAMAN CIZELGESI 2 — OutboxPublisher tiki, 5 saniyede bir]
GetUnpublishedOutboxMessages() -> satiri buluyor
   |
RabbitMqEventPublisher.PublishAsync
   |  StartActivity("publish...", Producer)
   |     -> Activity.Current = null (ebeveyn YOK)
   |     -> TraceId = fce452fa288daebab70b6af69c8d0e56  (YENI, rastgele)
   |     -> SpanId  = b931e9e1b98f3728                   (YENI)
   |  DistributedContextPropagator.Inject(activity, headers, setter)
   |     -> headers["traceparent"] = "00-fce452fa...-b931e9e1...-01"
   |  BasicPublishAsync(..., Headers: headers)
   ▼
RabbitMQ (fanout exchange) -- mesaji, header'lariyla BIRLIKTE, IKI kuyruga kopyaliyor


[ZAMAN CIZELGESI 3 — "audit" consumer, FieldOps.Api'nin KENDI icinde]
ReceivedAsync -> StartConsumerActivity(ea.BasicProperties.Headers)
   |  ExtractTraceIdAndState -> traceParent = "00-fce452fa...-b931e9e1...-01" (AYNI deger, byte[]'den decode edildi)
   |  ActivityContext.TryParse(traceParent, ...) -> parentContext
   |  StartActivity("consume...", Consumer, parentContext)
   |     -> TraceId = fce452fa288daebab70b6af69c8d0e56  (AYNI!)
   |     -> SpanId  = 324ec350077eed18                   (YENI)
   |     -> ParentSpanId = b931e9e1b98f3728               (publish'e BAGLI)
   ▼
HandleAsync (log basar) -> activity Dispose -> konsola yazdirilir (service.name: FieldOps.Api)


[ZAMAN CIZELGESI 4 — FieldOps.NotificationService, GERCEKTEN BASKA bir process]
ReceivedAsync -> StartConsumerActivity(ea.BasicProperties.Headers)  <- KENDI kopyasi, ayni mantik
   |  ayni traceParent'i BULUYOR (RabbitMQ header'i BOZMADAN tasidi)
   |  StartActivity("consume...", Consumer, parentContext)
   |     -> TraceId = fce452fa288daebab70b6af69c8d0e56  (YINE AYNI!)
   |     -> SpanId  = 89cad0193bcaf326                   (YENI)
   |     -> ParentSpanId = b931e9e1b98f3728               (YINE publish'e BAGLI)
   ▼
HandleAsync (bildirim) -> activity Dispose -> KENDI konsoluna yazdirilir (service.name: FieldOps.NotificationService)
```

**En önemli çıkarım:** Üç span, üç farklı anda, İKİ farklı process'te doğdu — ama HİÇBİRİ birbirini DOĞRUDAN çağırmadı (RabbitMQ hâlâ aradaki tek bağlantı, Day 68'in ilk günden beri söylediği gibi). Onları **AYNI trace**'e bağlayan tek şey, `Inject`'in yazdığı, `ExtractTraceIdAndState`'in okuduğu, RabbitMQ'nun sadakatle taşıdığı o **tek metin satırı**: `traceparent`.
