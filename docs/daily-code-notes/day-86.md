# Day 86 — Kod Notları

Faz 4, Hafta 17, Gün 86. Konu: **metrikler (metrics)**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Gerçek problem:** Day 85'in trace'leri, HER ZAMAN **tek bir** olayın hikayesini anlatıyor — "iş emri 36 tamamlandığında ne oldu." Ama gerçek bir üretim ortamında sorulan sorular genelde **toplu**: "bugün kaç iş emri tamamlandı", "outbox mesajları genelde ne kadar sürede RabbitMQ'ya ulaşıyor, bu süre artıyor mu?" Bu sorulara cevap vermek için binlerce trace'i teker teker okumak gerekirdi — bu mantıksız.

**Çözüm:** **Metrikler** — sadece SAYILARI tutan, zamanla toplanan (aggregate edilen) veri. Bugün iki tane ekliyoruz: (1) kaç iş emri tamamlandı (basit bir sayaç), (2) bir outbox mesajının yazılmasından gerçekten yayınlanmasına kadar ne kadar sürdü (bir "gecikme" ölçümü) — bu ikincisi, gerçek üretim sistemlerinde "outbox lag" diye bilinen, GERÇEKTEN izlenen bir metrik: bu sayı artıyorsa, RabbitMQ ya da Elasticsearch'ün yavaşladığının erken bir işareti.

**Trace ile metrik nasıl birbirini tamamlıyor:** Trace = "BU belirli olayda ne oldu" (tekil). Metrik = "genel olarak durum ne" (toplu). İkisi birlikte: metrik "outbox lag arttı" der, sen o anki trace'lere bakıp "hangi mesaj yavaşladı, neden" diye detaya inersin.

---

## 1. `OutboxMessageSummary.cs` — `CreatedAtUtc` eklendi

```csharp
public record OutboxMessageSummary(int Id, string EventType, string Payload, DateTime CreatedAtUtc);
// Day 71'den beri bu kayitta CreatedAtUtc YOKTU -- OutboxPublisher, "bu mesaj
// NE ZAMAN yazildi" bilgisine hic ihtiyac duymamisti. Bugun "lag" (gecikme)
// olcmek icin BU bilgiye ihtiyacimiz var -- OutboxMessage entity'sinde zaten
// vardi, sadece DISARIYA (OutboxPublisher'a) hic tasinmiyordu.
```

---

## 2. `FieldOpsMetrics.cs` (yeni) — Day 85'in `FieldOpsTracing`'i gibi, ama metrikler için

```csharp
public static class FieldOpsMetrics
{
    public const string MeterName = "FieldOps.Api";
    private static readonly Meter Meter = new(MeterName);
    // Meter -- ActivitySource'un (Day 85) metrik karsiligi. "Bu isimde
    // metrikler uretecegim" diye bir kaynak taniyor.

    public static readonly Counter<long> WorkOrdersCompleted =
        Meter.CreateCounter<long>("workorders.completed", description: "...");
    // Counter -- SADECE artan bir sayac. Azaltilamaz, sifirlanamaz (bizim
    // kodumuzdan). "Toplam kac tamamlandi" sorusuna cevap.

    public static readonly Histogram<double> OutboxPublishLagSeconds =
        Meter.CreateHistogram<double>("outbox.publish.lag_seconds", unit: "s", description: "...");
    // Histogram -- bir DEGERIN DAGILIMINI olcer. "Ortalama 2 saniye" demek
    // yetmez -- "cogu 1 saniyenin altinda ama bazilari 30 saniye surdu" gibi
    // bir bilgiyi de tasimasi gerekiyor. Histogram, degerleri "kovalara"
    // (buckets) ayirip bunu yapiyor.
}
```

---

## 3. `WorkOrdersController.Complete` — sayaç artırılıyor

```csharp
_workOrderReportService.InvalidateCache(organizationId!.Value);
_auditLogWriter.Record(organizationId!.Value, id, "Completed", "Employee", actingEmployeeId!.Value);

FieldOpsMetrics.WorkOrdersCompleted.Add(1);
// SADECE basari yolunda -- validasyon hatalarinda (BadRequest donuslerinde)
// bu satira hic ulasilmiyor. "Add(1)" -- Counter'in TEK yaptigi sey, verilen
// degeri toplam sayaca EKLEMEK.

return Ok(ToDto(updated));
```

---

## 4. `OutboxPublisher.cs` — gecikme ölçülüyor

```csharp
await eventPublisher.PublishAsync(domainEvent, message.Id.ToString(), cancellationToken);
workOrderDirectory.MarkOutboxMessagePublished(message.Id);
RecordPublishLag(message.CreatedAtUtc);
// Basarili yayinlamadan HEMEN sonra cagriliyor -- Elasticsearch/RabbitMQ
// istegi tekrar tekrar basarisiz olup en sonunda basarili olsa bile,
// SADECE gercekten basarili oldugu anda olculuyor.

private static void RecordPublishLag(DateTime createdAtUtc)
{
    var lag = DateTime.UtcNow - createdAtUtc;
    FieldOpsMetrics.OutboxPublishLagSeconds.Record(lag.TotalSeconds);
    // Record(deger) -- Histogram'in TEK yaptigi sey, bu TEK olcumu
    // dagilima EKLEMEK. "lag" burda, mesajin YAZILDIGI an ile SU AN
    // arasindaki GERCEK farktir -- eger bir mesaj 3 kez basarisiz olup
    // 15 saniye sonra basarili olduysa, kaydedilen deger GERCEKTEN 15
    // saniyeye yakin olacak, ilk denemedeki 0.1 saniye DEGIL.
}
```
Aynı satır, `WorkOrderSearchDocument` (Elasticsearch) dalında da tekrarlanıyor — hem RabbitMQ hem Elasticsearch'e yayınlama için AYNI histogram kullanılıyor (bugünlük, ikisini ayırmak için ayrı bir "etiket" eklemedik — bu, bilinçli bir basitleştirme).

---

## 5. `Program.cs` — `.WithMetrics(...)` kaydı

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("FieldOps.Api"))
    .WithTracing(tracing => tracing...)      // Day 85
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()      // HTTP istek sayisi/suresi OTOMATIK
        .AddMeter(FieldOpsMetrics.MeterName) // BIZIM Meter'imiz DINLENIYOR
        .AddConsoleExporter());              // metrikler konsola yazdiriliyor
```
`AddOpenTelemetry()`'nin döndürdüğü nesne, `.WithTracing(...)` VE `.WithMetrics(...)`'i ZİNCİRLEME kabul ediyor — ikisi de aynı kaydın, farklı iki "türü" (tracing vs metrics).

---

## Regresyon (Day 86)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65, 8dk34sn (normal)
```

## Canlı doğrulama

Gerçek bir RabbitMQ'ya karşı, gerçek bir iş emri (`id=37`) oluşturulup tamamlandı. OpenTelemetry'nin varsayılan 60 saniyelik toplama penceresi bekletildikten sonra, konsola gerçekten düşen değerler:
```
Metric Name: workorders.completed, Metric Type: LongSum
Value: 1

Metric Name: outbox.publish.lag_seconds, Metric Type: Histogram
Value: Sum: 13.9337528  Count: 1  Min: 13.9337528  Max: 13.9337528
```
`workorders.completed` gerçekten **1**'e çıktı — sayaç çalışıyor. `outbox.publish.lag_seconds`'ın **13.93 saniye** gibi (5 saniyelik tick aralığından belirgin şekilde yüksek) bir değer taşıması da tesadüf değil: bu sırada Elasticsearch KAPALIYDI (log'da tekrarlayan "Failed to publish outbox message" hataları var) — RabbitMQ'ya başarılı yayınlanan mesaj için ölçülen bu gecikme, tam olarak bugünün "outbox lag artışı, bir bağımlılığın sorunlu olduğunun erken işaretidir" iddiasını **canlı olarak** kanıtlıyor.

## Demo basitleştirmesi vs. üretim gereksinimi

* Metrikler konsola yazdırılıyor — üretimde bu **Prometheus**'a gönderilip **Grafana**'da grafikleştirilirdi (Day 85'teki gibi, sadece exporter satırı değişirdi).
* RabbitMQ ve Elasticsearch'e yayınlama, AYNI histogram'ı paylaşıyor — üretimde muhtemelen "hangi hedefe yayınlandığı" bir etiket (tag/dimension) olarak eklenirdi, ikisini ayrı ayrı izleyebilmek için.
