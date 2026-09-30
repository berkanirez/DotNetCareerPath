# Day 85 — Kod Notları

Faz 4, **Hafta 17**, Gün 85 — Faz 4'ün son haftasının ilk günü. Konu: **OpenTelemetry / dağıtık izleme (distributed tracing)**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Gerçek problem:** Day 76'dan beri, FieldOps artık **tek bir process değil**. Bir iş emri tamamlandığında: `FieldOps.Api` bir olay yazıyor → RabbitMQ bunu taşıyor → `FieldOps.NotificationService` (BAŞKA bir process, belki BAŞKA bir makinede) bunu okuyup bildirim gönderiyor. Bir şey ters giderse (örn. bildirim gitmedi), elimizde İKİ AYRI process'in İKİ AYRI log dosyası var — ve bu iki log'u **elle** birbirine bağlamak zorundayız: "acaba bu bildirim hatası, hangi iş emrinden geldi?"

**Bunun çözümü ne:** Dağıtık izleme (distributed tracing), her bir "olayın" (bir iş emrinin tamamlanması gibi) tüm process'ler boyunca **AYNI takip numarasını** taşımasını sağlıyor — `FieldOps.Api`'nin logunda VE `FieldOps.NotificationService`'in logunda **aynı numara** görünüyor, ikisini elle eşleştirmek gerekmiyor.

**Neden bugüne kadar buna ihtiyacımız olmadı, şimdi neden var:** Day 55'te zaten bir "correlation ID" (`CorrelationIdMiddleware`) ekledik — ama o SADECE tek bir HTTP isteği içinde işe yarıyor, çünkü o istek başlayıp bitene kadar hep AYNI process'teyiz. Day 76'dan beri FieldOps birden fazla process'e yayıldığı için, artık process SINIRLARINI AŞAN bir takip numarasına ihtiyacımız var.

**Dikkat — bugün öğrenilen, planı düzelten önemli bir gerçek:** İlk düşüncemiz "orijinal HTTP isteğinin takip numarasını RabbitMQ mesajına taşıyalım" şeklindeydi. Ama bu **YANLIŞ** — çünkü Day 71'in Outbox pattern'i sayesinde, `Complete` isteği HEMEN bitiyor, ve gerçek RabbitMQ yayınlaması **5 saniye SONRA**, `OutboxPublisher`'ın **tamamen ayrı, ilgisiz** bir arka plan turunda oluyor. Yani orijinal HTTP isteğinin "takip numarası" o noktada zaten **kaybolmuş** durumda (context'i sona ermiş). Bunun yerine: **bugünkü trace, RabbitMQ'ya GERÇEKTEN yayınlama anında başlıyor** — yani "bu event, RabbitMQ'ya yayınlandığından consumer tarafından işlenene kadar" izleniyor, "orijinal HTTP isteğinden itibaren" değil. Bu, varsaymadan, düşünerek bulduğumuz gerçek bir tasarım düzeltmesi.

---

## 1. Paketler

```bash
dotnet add src/FieldOps.Api package OpenTelemetry.Extensions.Hosting
dotnet add src/FieldOps.Api package OpenTelemetry.Instrumentation.AspNetCore
dotnet add src/FieldOps.Api package OpenTelemetry.Exporter.Console

dotnet add src/FieldOps.NotificationService package OpenTelemetry.Extensions.Hosting
dotnet add src/FieldOps.NotificationService package OpenTelemetry.Exporter.Console
```
`FieldOps.NotificationService`'e `Instrumentation.AspNetCore` EKLENMEDİ — bu servisin hiçbir HTTP endpoint'i yok, otomatik enstrümante edilecek bir istek yok.

---

## 2. `FieldOpsTracing.cs` — Day 76'nın `EventQueueNaming`'i gibi, KASITLI olarak İKİ KOPYA

```csharp
public static class FieldOpsTracing
{
    public const string MessagingSourceName = "FieldOps.Messaging";
    public static readonly ActivitySource MessagingSource = new(MessagingSourceName);
}
// Bu sinif, HEM FieldOps.Api'de HEM FieldOps.NotificationService'te AYRI AYRI
// var -- aralarinda hicbir C# referansi yok (Day 76'nin EventQueueNaming'inin
// AYNI mantigi). Iki servisin anlasmasi gereken TEK sey, ayni STRING
// ("FieldOps.Messaging") -- kod paylasimina gerek yok.

// ActivitySource -- .NET'in KENDI, ekstra paket gerektirmeyen izleme API'si
// (System.Diagnostics). "Bu isimde span'lar uretecegim" diye bir kaynak
// tanimliyor. OpenTelemetry SDK'si, Program.cs'te ".AddSource(bu isim)"
// diyerek bu kaynagi "dinlemeye" basliyor -- dinleyen kimse olmasaydi,
// StartActivity() cagrilari SESSIZCE HICBIR SEY URETMEZDI.
```

---

## 3. `RabbitMqEventPublisher.cs` — trace'in BAŞLADIĞI yer

```csharp
using var activity = FieldOpsTracing.MessagingSource.StartActivity(
    $"publish {typeof(TEvent).Name}", ActivityKind.Producer);
// Yeni bir span (trace'in tek bir adimi) BASLATIYOR. ActivityKind.Producer --
// OpenTelemetry'nin "ben bir mesajlasma sisteminin GONDEREN tarafiyim"
// demenin standart yolu.

// ... exchange declare, JSON serialize ...

var headers = new Dictionary<string, object?>();
if (activity is not null)
{
    DistributedContextPropagator.Current.Inject(
        activity,
        headers,
        static (carrier, key, value) => ((Dictionary<string, object?>)carrier!)[key] = value);
}
// ISTE ASIL "TRACE CONTEXT PROPAGATION": bu satir, yukarida baslatilan
// activity'nin kimligini (trace ID + span ID), standart bir formatta
// (W3C "traceparent" header'i), headers sozlugune YAZIYOR.

await channel.BasicPublishAsync(..., basicProperties: new BasicProperties { MessageId = messageId, Headers = headers }, ...);
// Bu headers, mesajin GERCEK RabbitMQ AMQP header'lari olarak gonderiliyor --
// mesaji ALAN taraf, bu header'lari OKUYARAK ayni trace'e KATILABILIYOR.
```

---

## 4. `EventConsumerBase.cs` — trace'in DEVAM ETTİĞİ yer (her iki kopyada da)

```csharp
using var activity = StartConsumerActivity(ea.BasicProperties.Headers);
// try bloğundan ONCE cagriliyor -- HandleAsync'in tum calismasi, bu span'in
// ICINDE gecsin diye.

private static Activity? StartConsumerActivity(IDictionary<string, object?>? headers)
{
    DistributedContextPropagator.Current.ExtractTraceIdAndState(
        headers,
        static (carrier, fieldName, out fieldValue, out fieldValues) =>
        {
            // headers'taki deger, GONDERILIRKEN string yazilmis olsa bile,
            // TESLIM EDILDIKTEN sonra byte[] olarak geri geliyor -- bu,
            // VARSAYMADAN, canli test ederek dogrulanan bir RabbitMQ/AMQP
            // davranisi. Bu yuzden burada elle UTF8 decode ediliyor.
            fieldValue = value switch { byte[] b => Encoding.UTF8.GetString(b), string s => s, _ => null };
        },
        out var traceParent, out var traceState);

    ActivityContext.TryParse(traceParent, traceState, out var parentContext);

    return FieldOpsTracing.MessagingSource.StartActivity(
        $"consume {typeof(TEvent).Name}", ActivityKind.Consumer, parentContext);
    // parentContext veriliyor -- YENI, ILGISIZ bir trace DEGIL, YAYINLAYANIN
    // ACTIVITY'SININ DEVAMI olarak baslatiliyor. Iste tam olarak bu satir,
    // iki process'in loglarinin AYNI trace ID'yi paylasmasini SAGLIYOR.
}
```

---

## 5. `Program.cs` — SDK'yı gerçekten devreye sokmak

```csharp
// FieldOps.Api:
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("FieldOps.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()   // HTTP istekleri OTOMATIK izleniyor
        .AddSource(FieldOpsTracing.MessagingSourceName)  // BIZIM elle yazdigimiz span'lar
        .AddConsoleExporter());           // trace'ler konsola YAZDIRILIYOR (bugunun basit cozumu)

// FieldOps.NotificationService:
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("FieldOps.NotificationService"))
    .WithTracing(tracing => tracing
        .AddSource(FieldOpsTracing.MessagingSourceName)
        .AddConsoleExporter());
// AddAspNetCoreInstrumentation YOK -- bu servisin hic HTTP endpoint'i yok.
```
`ConfigureResource(...AddService("..."))` — her iki process'in ürettiği trace'lerin, konsol çıktısında **hangi servise ait olduğu** bilgisini taşıması için (`FieldOps.Api` mi, `FieldOps.NotificationService` mi).

---

## Regresyon (Day 85)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65, 8dk26sn (normal)
```

## Canlı doğrulama — asıl kanıt

İki ayrı process (`FieldOps.Api`, `FieldOps.NotificationService`), gerçek bir RabbitMQ'ya karşı, gerçek bir iş emri tamamlama senaryosu ile çalıştırıldı. Konsola yazdırılan trace çıktılarından:

```
FieldOps.Api'de (yayınlama):
  Activity.TraceId:      fce452fa288daebab70b6af69c8d0e56
  Activity.SpanId:       b931e9e1b98f3728
  Activity.DisplayName:  publish WorkOrderCompletedEvent
  service.name:          FieldOps.Api

FieldOps.Api'de (audit consumer, AYNI process, FARKLI consumer):
  Activity.TraceId:      fce452fa288daebab70b6af69c8d0e56   <- AYNI
  Activity.ParentSpanId: b931e9e1b98f3728                    <- yukaridaki publish'e bagli
  Activity.DisplayName:  consume WorkOrderCompletedEvent
  service.name:          FieldOps.Api

FieldOps.NotificationService'te (GERCEKTEN BASKA bir process):
  Activity.TraceId:      fce452fa288daebab70b6af69c8d0e56   <- YINE AYNI
  Activity.ParentSpanId: b931e9e1b98f3728                    <- AYNI publish'e bagli
  Activity.DisplayName:  consume WorkOrderCompletedEvent
  service.name:          FieldOps.NotificationService         <- FARKLI process, KANITLI (service.name farkli)
```
Üç ayrı span, üçü de **aynı `TraceId`**'yi taşıyor, ikisi de doğru şekilde **aynı** `publish` span'ına bağlı — biri `FieldOps.Api`'nin kendi içinde (audit consumer), diğeri **gerçekten farklı bir process** olan `FieldOps.NotificationService`'te. `publish` span'ının kendisinin hiçbir `ParentSpanId`'si yok — bu da bugün öğrendiğimiz gerçeği doğruluyor: bu trace, orijinal HTTP isteğinden değil, `OutboxPublisher`'ın gerçek yayınlama anından başlıyor.

## Demo basitleştirmesi vs. üretim gereksinimi

* **Demo bugün:** Trace'ler konsola yazdırılıyor — gerçekte okunması/aranması zor. Üretimde bu Jaeger, Zipkin, ya da bir bulut izleme servisine (Application Insights gibi) gönderilirdi, gerçek bir görsel arayüzle.
* Sadece `WorkOrderCompletedEvent` yayınlama/tüketme izleniyor — SOAP çağrısı (Day 83) ya da Elasticsearch çağrıları (Day 79-81) bugün enstrümante edilmedi (ama `AddAspNetCoreInstrumentation` zaten gelen HTTP isteklerini kapsıyor, ve OpenTelemetry'nin HTTP client enstrümantasyonu eklenirse SOAP/Elasticsearch'ün kendi HTTP çağrıları da otomatik izlenebilirdi — bugünün kapsamı dışında bırakıldı).
