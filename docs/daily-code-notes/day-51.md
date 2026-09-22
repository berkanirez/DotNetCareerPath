# Day 51 — Kod Notları

Faz 3, Hafta 10, Gün 51. Konu: **Notification abstraction** — bir iş emri tamamlandığında, ona bağlı müşteriye "onayını bekliyor" diye haber vermek, ama gerçek bir e-posta/SMS sağlayıcısına bağlanmadan.

---

## 1. `INotificationSender` — kanal-bağımsız arayüz

```csharp
public interface INotificationSender
{
    Task NotifyAsync(string message, CancellationToken cancellationToken);
}
```

**Neden bu kadar sade:** Bilerek e-posta adresi, telefon numarası, "Kime" gibi kanal-özel hiçbir kavram yok. Hangi gerçek kanalı (email mi, SMS mi, push mu) kullanacağımıza henüz karar vermedik — bu arayüz sadece "bir şey oldu, birine haber ver" taahhüdünü veriyor. Week 12'de yapılacak "AI provider abstraction" da tam olarak aynı şekli tekrarlayacak: önce arayüz + sahte implementasyon, gerçek sağlayıcı sonra.

`Task`/`CancellationToken` imzası bilinçli — gerçek bir sağlayıcı (bir HTTP çağrısıyla email gönderen bir servis gibi) kesinlikle asenkron olurdu, bu yüzden arayüz baştan asenkron şekilde tasarlandı.

---

## 2. `LoggingNotificationSender` — bugünün tek implementasyonu

```csharp
public class LoggingNotificationSender : INotificationSender
{
    private readonly ILogger<LoggingNotificationSender> _logger;

    public LoggingNotificationSender(ILogger<LoggingNotificationSender> logger)
    {
        _logger = logger;
    }

    public Task NotifyAsync(string message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Notification: {Message}", message);
        return Task.CompletedTask;
    }
}
```

Gerçek hiçbir I/O yapmıyor — sadece `ILogger`'a yazıp `Task.CompletedTask` döndürüyor. Bu, StockPilot Day 16'nın `InMemoryProductStore`'unun yaptığı ile aynı numara: arayüz asenkron ama implementasyon senkron bir işi asenkron bir imzaya sarmalıyor, çünkü gerçekten beklenecek (await edilecek) hiçbir şey yok.

---

## 3. `WorkOrdersController.Complete` — tek async action

```csharp
[HttpPost("{id}/complete")]
public async Task<ActionResult<WorkOrderDto>> Complete(
    int id,
    [FromHeader(Name = "X-Organization-Id")] int? organizationId,
    [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId,
    CancellationToken cancellationToken)
{
    // ... membership + ownership kontrolleri, mutasyon (degismedi) ...

    _workOrderReportService.InvalidateCache(organizationId!.Value);

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
            _logger.LogWarning(ex, "Failed to send completion notification for work order {WorkOrderId}", id);
        }
    }

    return Ok(ToDto(updated));
}
```

**Neden sadece `Complete` `async`, controller'ın tamamı değil:** Bu, bugün `await` etmesi gereken **tek** action. Day 48'de kurulan "directory'ler senkron kalsın" kararı hâlâ geçerli — bu, o kararı bozmuyor, sadece gerçekten asenkron bir şey (bildirim gönderimi) olan tek noktada minimal bir istisna açıyor. `CancellationToken cancellationToken` parametresi — StockPilot Day 17'de öğrendiğimiz gibi, ASP.NET Core bunu otomatik olarak `HttpContext.RequestAborted`'dan doldurur, hiçbir attribute gerekmiyor.

**`if (updated.CustomerId is not null)`** — bildirim sadece bağlı bir müşterisi olan iş emirleri için deneniyor; müşterisiz bir iş emrinde bildirilecek kimse yok.

**`try`/`catch` ile hata izolasyonu:** Bu en önemli tasarım kararı. `updated` değişkeni, bu satıra gelindiğinde iş emrinin **zaten gerçekten** `Completed` olduğunu gösteriyor (mutasyon başarıyla tamamlandı, veritabanına yazıldı). Bildirim göndermek başarısız olsa bile, bu gerçeği geri almanın hiçbir anlamı yok — bu yüzden hatayı yutup sadece loglamak doğru: asıl iş (iş emrini tamamlamak) her koşulda korunuyor. `_logger.LogWarning(ex, "...", id)` — `ex`'i ayrı parametre vermek stack trace'i saklar, `{WorkOrderId}` yapılandırılmış logging placeholder'ı.

**Constructor'a eklenenler:** `INotificationSender _notificationSender` ve `ILogger<WorkOrdersController> _logger` — controller ilk kez bir logger alıyor.

`Program.cs`: `builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();`

---

## 4. Canlı kanıt — üç senaryo

```
1) Musterili is emri (customerId=1) -> Complete -> log'da:
   "Notification: Work order 'Day51 notification test' has been completed
    and is awaiting your approval."

2) Musterisiz is emri (customerId yok) -> Complete -> log'da YENI bir
   "Notification:" satiri YOK (toplam sayisi degismedi) -> dogru, hic
   denenmedi.

3) LoggingNotificationSender gecici olarak "throw" edecek sekilde
   degistirildi, uygulama yeniden baslatildi:
   -> Musterili bir is emri Complete edildi -> HTTP 200 OK (!)
   -> log'da: "Failed to send completion notification for work order 5"
   -> Bildirim gercekten patladi, ama is emrinin tamamlanmasi ETKILENMEDI.
   -> Kod geri alindi, tekrar dogrulandi (normal bildirim davranisina donuldu).
```

Üçüncü senaryo, bu günün en önemli kanıtı: hata izolasyonunun **gerçekten** çalıştığı, sadece kod okunarak varsayılmadı.

---

## 5. Demo basitleştirmesi vs. üretim gereksinimi

- Bugün hiçbir gerçek kanal yok — sadece log. Üretimde `LoggingNotificationSender` yerine gerçek bir sağlayıcı (SendGrid, Twilio) `Program.cs`'teki **tek bir satır** değiştirilerek takılır, `WorkOrdersController`'a hiç dokunulmaz.
- Bildirim bugün **senkron ve inline** (aynı HTTP isteği içinde) gönderiliyor. Gerçek bir sağlayıcı yavaş olabilir, bu da `Complete` isteğini gereksiz yere geciktirir. Üretimde bu muhtemelen bir kuyruğa (Phase 4'ün mesajlaşma konusu) atılır ya da arka planda (Day 50'nin `BackgroundService`'i gibi) işlenir. Bugün bu basitleştirme kabul edildi, ama hata izolasyonuyla en azından "bildirim yavaşsa/patlarsa asıl işi bozmasın" garantisi sağlandı.

---

## 6. Aynı oturumda bulunan gerçek bir ikinci eksiklik — `InvalidateCache` de korumasızdı

Anlama sorularını cevaplarken şu soru geldi: "`NotifyAsync`'e uyguladığımız hata izolasyonunu `InvalidateCache`'e de uygulasaydık mantıklı olur muydu?" İlk cevap "hayır, farklı" oldu, ama doğrusu **tam tersi**: `InvalidateCache` de aynı korumaya ihtiyaç duyuyordu, ve şu ana kadar hiç yoktu.

**Problem:** `Complete` (ve `Create`/`Assign`/`Start`/`Unassign`/`Reopen`) çalıştığında, iş emri veritabanında **zaten gerçekten** güncellenmiş oluyor. Sonra `_workOrderReportService.InvalidateCache(...)` çağrılıyor — eğer o anda Redis erişilemezse, bu satır bir exception fırlatır, hiçbir şey onu yakalamıyor, ve istemciye muhtemelen bir `500` döner. Ama iş emri **aslında başarıyla tamamlanmıştı** — istemci "işlem başarısız" sanır, oysa tek başarısız olan şey bir yan etki (cache temizleme). Bu, `NotifyAsync` için Day 51'de çözdüğümüz problemin birebir aynısı, ve 6 farklı çağrı noktasının (her `Status`-değiştiren action) hepsini etkiliyordu.

**Çözüm — controller'da değil, servisin kendi içinde:**

```csharp
public void InvalidateCache(int organizationId)
{
    try
    {
        var db = _redis.GetDatabase();
        db.KeyDelete($"workorders:report:{organizationId}");
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to invalidate work order report cache for organization {OrganizationId}", organizationId);
    }
}
```

**Neden controller'daki 6 çağrı noktasının her birine ayrı `try`/`catch` eklemek yerine, servisin kendi içine:** Hepsi aynı korumaya ihtiyaç duyduğu için, koruma tek bir yerde (riskli işlemin kendisinde) olmalı — hem kod tekrarı olmaz, hem de gelecekte yeni bir action eklendiğinde bu korumayı unutma riski ortadan kalkar. `WorkOrderReportService`, ilk kez bir `ILogger<WorkOrderReportService>` alıyor.

**Canlı kanıt — gerçekten Redis'i kapatarak:**
```
1) Is emri olusturulup Assign+Start edildi (Redis ayakta).
2) docker stop fieldops-redis -> Redis GERCEKTEN kapatildi.
3) Complete cagrildi -> HTTP 200 OK (!), is emri gercekten Completed oldu.
4) Log'da: "Failed to invalidate work order report cache for organization 1"
5) Redis tekrar baslatildi (docker start fieldops-redis), dogrulandi (PONG).
```
Düzeltmeden önce bu senaryo bir `500` ile sonuçlanırdı — canlı olarak denenmedi ama kod okunarak kesin (o zaman `try`/`catch` hiç yoktu, `KeyDelete`'in fırlattığı exception doğrudan controller'dan dışarı taşardı).

---

## Regresyon

```
dotnet test FieldOps.slnx    → 49/49 (degismedi)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```
