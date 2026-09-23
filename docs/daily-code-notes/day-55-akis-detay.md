# Day 55 — Correlation ID: Bir İsteğin Uçtan Uca Akışı

Bu doküman, `day-55.md`'nin yanında, tek bir somut örnek üzerinden "istek nereden girdi, hangi kod hangi sırayla çalıştı, log'a ne zaman ne eklendi, yanıt nasıl döndü" sorusuna adım adım cevap veriyor. Aşağıdaki kod parçaları gerçek dosyalardan alınmış; adımların numaralandırılması ve aralarındaki oklar, akışı takip etmen için eklendi.

**Senaryo:** İstemci, `X-Correlation-Id: manual-test-123` header'ıyla `POST /api/workorders/18/complete` çağırıyor (bu iş emrinin bir müşterisi var, `CustomerId` set edilmiş).

---

## 0. İstek, Kestrel'e ulaşıyor

```
POST /api/workorders/18/complete HTTP/1.1
X-Organization-Id: 1
X-Employee-Id: 2
X-Correlation-Id: manual-test-123
```

ASP.NET Core, bu isteği `Program.cs`'te tanımlanan middleware zincirine sokuyor — zincirin sırası tam olarak `Program.cs`'te yazıldığı sıra:

```csharp
app.UseMiddleware<CorrelationIdMiddleware>();   // 1. adım
app.UseHttpsRedirection();                      // 2. adım
app.UseRateLimiter();                           // 3. adım
app.UseAuthorization();                         // 4. adım
app.MapControllers();                           // 5. adım -> Controller
```

---

## 1. `CorrelationIdMiddleware.InvokeAsync` çalışıyor — İLK durak

```csharp
public async Task InvokeAsync(HttpContext context)
{
    var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) && !string.IsNullOrWhiteSpace(existing)
        ? existing.ToString()          // <-- BURADA: "manual-test-123" bulundu, bu kullanilacak
        : Guid.NewGuid().ToString();

    context.Response.Headers[HeaderName] = correlationId;   // <-- yanit header'ina SIMDIDEN yazildi

    using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
    {
        // <-- BURADAN ITIBAREN, "manual-test-123" artik aktif bir "scope" --
        //     bu using bloğu kapanana kadar (yani istek tamamen bitene kadar),
        //     hangi kod calisirsa calissin, uretilen HER log satiri bu
        //     scope'u otomatik tasiyacak.
        await _next(context);   // <-- 2. adima (UseHttpsRedirection) devrediliyor
        // <-- istek TAMAMLANIP buraya geri dondugunde using bloğu kapanacak
    }
}
```

**Bu noktada durum:** `correlationId = "manual-test-123"`, yanıt header'ı zaten set edildi (ama yanıt henüz istemciye gönderilmedi — sadece "gönderileceği zaman bu header'la gönder" diye işaretlendi). Log altyapısına "aktif scope: `{CorrelationId: manual-test-123}`" bilgisi eklendi. `await _next(context)` çağrısı, kontrolü bir sonraki middleware'e devrediyor — ama **hâlâ bu `using` bloğunun içindeyiz**, `_next(context)`'in çalışması bitene kadar (yani zincirin geri kalanının tamamı çalışıp bitene kadar) buradan çıkılmayacak.

---

## 2-4. `UseHttpsRedirection` → `UseRateLimiter` → `UseAuthorization`

Bu üç middleware, bu senaryoda isteği **olduğu gibi geçiriyor** (HTTPS'e yönlendirme gerekmiyor, org 1'in rate limit kovasında yer var, gerçek bir authentication şeması yok). Hiçbiri kendi log satırı üretmiyor bu örnekte. Önemli olan: hâlâ 1. adımdaki `using` bloğunun **içindeyiz** — scope hâlâ aktif.

---

## 5. `MapControllers` → `WorkOrdersController.Complete` çalışıyor

```csharp
public async Task<ActionResult<WorkOrderDto>> Complete(
    int id, /* ... */ CancellationToken cancellationToken)
{
    var membershipError = ValidateMembership(organizationId, actingEmployeeId);   // gecti
    var ownershipError = ValidateOwnership(id, organizationId, actingEmployeeId, out _);  // gecti

    var updated = _workOrderDirectory.Complete(id);   // <-- GERCEK DB YAZMASI: Status -> Completed
    _workOrderReportService.InvalidateCache(organizationId!.Value);   // Redis'ten cache silindi
    _auditLogWriter.Record(organizationId!.Value, id, "Completed", "Employee", actingEmployeeId!.Value);
    // <-- EfAuditLogWriter.Record, kendi AuditLogsDbContext'ine bir satir ekliyor (log satiri YOK, sessiz)

    if (updated.CustomerId is not null)   // <-- bu is emrinin bir musterisi var, true
    {
        try
        {
            await _notificationSender.NotifyAsync(
                $"Work order '{updated.Title}' has been completed and is awaiting your approval.",
                cancellationToken);
            // <-- BURADA bir LOG SATIRI URETILIYOR, asagida devami var
        }
        catch (Exception ex) { /* ... */ }
    }

    return Ok(ToDto(updated));   // <-- 200 OK + WorkOrderDto, ama henuz istemciye gitmedi
}
```

---

## 6. `LoggingNotificationSender.NotifyAsync` — LOG SATIRI TAM BURADA ÜRETİLİYOR

```csharp
public Task NotifyAsync(string message, CancellationToken cancellationToken)
{
    _logger.LogInformation("Notification: {Message}", message);   // <-- ISTE BU SATIR
    return Task.CompletedTask;
}
```

**Bu satır çalıştığı an, loglama altyapısı şunu yapıyor:** "Bu log satırını yazacağım, ama önce şu an aktif olan tüm scope'ları (varsa) toplayıp satıra ekleyeyim." Aktif scope yığınına bakıyor — ve 1. adımda `CorrelationIdMiddleware`'in eklediği `{CorrelationId: "manual-test-123"}` hâlâ orada duruyor (çünkü hâlâ o `using` bloğunun içindeyiz — çağrı zinciri hiç kırılmadı: `CorrelationIdMiddleware` → `UseHttpsRedirection` → `UseRateLimiter` → `UseAuthorization` → `Complete` → `NotifyAsync`, hepsi iç içe, senkron bir çağrı zinciri).

**Gerçekten, canlı olarak yakalanan çıktı (bugünkü doğrulamadan, uydurma değil):**
```json
{
  "EventId": 0,
  "LogLevel": "Information",
  "Category": "FieldOps.Api.Application.LoggingNotificationSender",
  "Message": "Notification: Work order 'Day55 correlation test' has been completed and is awaiting your approval.",
  "State": {
    "Message": "Work order 'Day55 correlation test' has been completed and is awaiting your approval.",
    "{OriginalFormat}": "Notification: {Message}"
  },
  "Scopes": [
    { "...": "ASP.NET Core'un kendi eklediği SpanId/TraceId scope'u" },
    { "...": "ASP.NET Core'un kendi eklediği ConnectionId scope'u" },
    { "...": "ASP.NET Core'un kendi eklediği RequestPath/RequestId scope'u" },
    { "Message": "System.Collections.Generic.Dictionary`2[System.String,System.Object]", "CorrelationId": "manual-test-123" },
    { "...": "ASP.NET Core'un kendi eklediği ActionName scope'u" }
  ]
}
```

Dikkat: `"CorrelationId": "manual-test-123"` alanı, **bizim** middleware'imizin eklediği scope — diğerleri (SpanId, RequestPath, ActionName) ASP.NET Core'un kendi framework'ünün otomatik eklediği scope'lar. Hepsi aynı mekanizmayla (aktif scope yığını) birikiyor, biz sadece kendi katmanımızı ekledik.

---

## 7. Kontrol geri sarılıyor — pipeline'dan çıkış

`Complete` metodu `Ok(dto)` döndürdü → `MapControllers` bunu bir HTTP yanıtına çeviriyor → kontrol sırasıyla geri dönüyor: `UseAuthorization` → `UseRateLimiter` → `UseHttpsRedirection` → **1. adımdaki `await _next(context)` satırı artık tamamlandı** → `using` bloğu **kapanıyor** → `{CorrelationId: "manual-test-123"}` scope'u yığından **çıkarılıyor** (bundan sonraki, bu isteğe ait olmayan hiçbir log satırı bu ID'yi taşımayacak).

---

## 8. Yanıt istemciye gönderiliyor

```
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
X-Correlation-Id: manual-test-123     <-- 1. adimda set edilen header, simdi gercekten gonderiliyor
```

İstemci, kendi gönderdiği `X-Correlation-Id`'nin aynen geri geldiğini görüyor — kendi loglarıyla sunucunun loglarını bu ID üzerinden eşleştirebiliyor.

---

## Özet — tek bakışta akış

```
İstemci (X-Correlation-Id: manual-test-123)
   │
   ▼
CorrelationIdMiddleware ─┐  ID okunur/uretilir, response header'a yazilir,
   │ (await _next)       │  BeginScope ile "aktif scope" baslatilir
   ▼                     │
UseHttpsRedirection      │
   │                     │  <-- hepsi bu "using" bloğunun İÇİNDE çalışıyor,
   ▼                     │      scope hâlâ aktif
UseRateLimiter           │
   │                     │
   ▼                     │
UseAuthorization         │
   │                     │
   ▼                     │
WorkOrdersController     │
  .Complete()            │
   │                     │
   ▼                     │
LoggingNotificationSender│
  .NotifyAsync()         │
   │  _logger.LogInformation(...)
   │  ──> LOG SATIRI: {..., "Scopes": [{"CorrelationId":"manual-test-123"}]}
   │                     │
   ▼                     │
(Complete geri doner, 200 OK)
   │                     │
   ▼                     │
... pipeline geri sarilir ...
   │                     │
   ▼                     ┘  using blogu kapanir, scope yigindan cikar
İstemciye yanıt: 200 OK + X-Correlation-Id: manual-test-123
```
