# Day 55 — Kod Notları

Faz 3, Hafta 11, Gün 55. Konu: **Structured Logging & Correlation ID** — Week 11'in ilk konusu. Her HTTP isteğine bir "takip numarası" atayıp, o isteği işlerken üretilen her log satırının bu numarayı otomatik taşımasını sağlamak.

---

## 1. Gerçek problem

Şu ana kadar FieldOps'taki birkaç `ILogger` çağrısı (warmer, idempotency, audit writer, notification sender) düz, birbirinden **kopuk** log satırları üretiyordu. Gerçek üretimde, aynı anda onlarca istek işlenirken bir şey ters giderse, "bu **belirli** istekte tam olarak ne oldu?" sorusuna cevap vermek neredeyse imkansız — hangi log satırının hangi isteğe ait olduğunu ayırt edecek hiçbir şey yok.

---

## 2. `CorrelationIdMiddleware` — satır satır

```csharp
public class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) && !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString();

        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
```

**Bu bir "middleware" — controller değil, filter değil.** ASP.NET Core'da bir middleware, `RequestDelegate next` alan bir constructor ve `InvokeAsync(HttpContext)` metodu olan **herhangi bir sınıf** olabilir — özel bir arayüz implemente etmesi gerekmiyor, framework bunu "convention" (isimlendirme kuralı) ile tanıyor. `RequestDelegate next`, "bu middleware'den sonra çalışacak, pipeline'ın geri kalanı" anlamına geliyor.

- **`context.Request.Headers.TryGetValue(HeaderName, out var existing)`** — gelen istekte `X-Correlation-Id` header'ı var mı diye bakıyoruz. `TryGetValue`, `Dictionary`'lerden tanıdığımız aynı desen: header varsa `true` döner ve değeri `existing`'e yazar.
- **`&& !string.IsNullOrWhiteSpace(existing)`** — header teknik olarak gönderilmiş ama boş bir string olabilir; bu durumda da "yok" sayıyoruz.
- **`? existing.ToString() : Guid.NewGuid().ToString()`** — varsa istemcinin gönderdiği ID'yi **aynen** kullanıyoruz (gelecekte, Phase 4'te, başka bir servisten gelen bir isteğin kendi ID'sini taşımaya devam etmesi için); yoksa yeni bir `Guid` üretiyoruz.
- **`context.Response.Headers[HeaderName] = correlationId`** — bu ID'yi **yanıt** header'ına da ekliyoruz. Böylece istemci (ya da bir sonraki hop) "bu isteğin takip numarası neydi" bilgisini alabiliyor — kendi loglarıyla sunucunun loglarını eşleştirebiliyor.
- **`_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId })`** — asıl sihir burada. `BeginScope`, ASP.NET Core'un loglama altyapısının **yerleşik** bir özelliği: verdiğin nesneyi (burada bir `Dictionary`) "şu an aktif olan scope" olarak işaretliyor. `using (...)` bloğu içinde çalışan **her** `_logger.LogInformation(...)`/`LogWarning(...)` çağrısı (biz hiç müdahale etmesek bile, `WorkOrderReportCacheWarmer`'daki, `EfAuditLogWriter`'daki, `LoggingNotificationSender`'daki çağrılar dahil), bu scope bilgisini **otomatik olarak** log satırına ekliyor.
- **`await _next(context)`** — pipeline'ın geri kalanını (rate limiting, authorization, controller'lar, her şey) bu `using` bloğunun **içinde** çalıştırıyoruz — bu yüzden isteğin **tamamı boyunca** üretilen her log satırı, bu correlation ID'yi taşıyor. `using` bloğu bitince (istek tamamlanınca), scope da otomatik olarak kapanıyor.

---

## 3. `Program.cs` — iki değişiklik

**a) Loglamayı JSON'a çevirmek ve scope'ları açmak:**
```csharp
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
});
```
`BeginScope` çağrısı **tek başına hiçbir şey görünür kılmıyor** — varsayılan ayarlarla, konsola yazdırılan loglar scope bilgisini hiç göstermez. `IncludeScopes = true` bunu açıyor. `AddJsonConsole` (düz metin yerine) seçildi çünkü .NET'in JSON log formatlayıcısı, bir `Dictionary<string, object>` scope'unu **gerçek, ayrı JSON alanları** olarak yazıyor (`"CorrelationId": "..."` gibi) — düz metin formatlayıcısı sadece `.ToString()` çağırırdı, işe yaramaz bir çıktı verirdi.

**b) Middleware'i pipeline'a eklemek:**
```csharp
app.UseMiddleware<CorrelationIdMiddleware>();
```
Bu satır, **her şeyden önce** (rate limiting, authorization'dan bile önce) ekleniyor — çünkü scope'un pipeline'ın **tamamını** sarması gerekiyor, sadece controller'ları değil.

---

## 4. Canlı kanıt

```
1) X-Correlation-Id: manual-test-123 ile tam bir yasam dongusu calistirildi
   (create -> assign -> start -> complete).
2) Complete cagrisinin yaniti kontrol edildi:
   -> Response header'inda "X-Correlation-Id: manual-test-123" AYNEN geri geldi.
3) Log dosyasi incelendi -> Notification log satirinda:
   "CorrelationId":"manual-test-123"
   -> Gercekten yapilandirilmis, aranabilir bir alan olarak orada.

4) Header GONDERILMEDEN bir istek yapildi:
   -> Response header'inda otomatik uretilen bir Guid dondu
      ("bb7b7864-ea86-47d1-8fae-21df97967137").

5) IKI FARKLI istek, iki farkli ID ile (req-AAA, req-BBB):
   -> Her biri kendi ID'sini aynen geri aldi, hic karismadi.

6) IKINCI bir tam yasam dongusu, "req-XXX" ID'siyle calistirildi:
   -> Log'da iki ayri "Notification:" satiri bulundu, HER BIRI kendi
      dogru CorrelationId'sini tasiyordu:
      "Day55 correlation test" -> CorrelationId: manual-test-123
      "Second correlation test" -> CorrelationId: req-XXX
```

Bu son adım en önemli kanıt: iki farklı isteğin logları **hiç karışmadan**, doğru şekilde ayrıştırılabiliyor.

---

## 5. Demo basitleştirmesi vs. üretim gereksinimi

- Bugünkü doğrulama, log dosyasını `grep` ile gözle incelemek şeklinde oldu. Gerçek üretimde bu JSON log satırları merkezi bir log toplama sistemine (Seq, ELK, Application Insights) gönderilir, orada `CorrelationId` gerçekten **sorgulanabilir/filtrelenebilir** bir alan olur ("bana `CorrelationId=X` olan her satırı göster" gibi bir arama).
- Bugünkü correlation ID sadece **HTTP isteği** kapsamında yaşıyor — Redis/SQL Server çağrılarının kendi iç loglarına ya da (Phase 4'ün konusu olan) servisler arası mesajlaşmaya hiç taşınmıyor. Gerçek bir dağıtık sistemde, bu ID mesaj kuyruklarına da eklenip birden fazla servisin logunu tek bir hikayede birleştirmek için kullanılırdı.

---

## Regresyon

```
dotnet test FieldOps.slnx    → 49/49 (degismedi)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```
