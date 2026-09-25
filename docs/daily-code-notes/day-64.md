# Day 64 — Kod Notları

Faz 3, Hafta 12, Gün 64. Konu: **başarısızlık senaryoları — AI sağlayıcı hataları**. Week 12'nin roadmap listesindeki "failure scenarios" maddesi, dün eklenen `IAiProvider` seam'i üzerinden ele alındı.

---

## 1. `src/FieldOps.Api/Controllers/WorkOrdersController.cs` — `GetSummary`'ye `try`/`catch` eklenmesi

```csharp
try
{
    var summary = await _workOrderNoteSummaryService.SummarizeAsync(workOrder.EvidenceNotes, cancellationToken);
    return Ok(summary);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to generate AI summary for work order {WorkOrderId}", id);
    return StatusCode(StatusCodes.Status503ServiceUnavailable, "AI summary service is temporarily unavailable. Please try again later.");
}
```

**Neden bu şekilde yazıldı:** Dün (Day 63) `GetSummary` action'ı yazıldığında, `_workOrderNoteSummaryService.SummarizeAsync(...)` çağrısının etrafında hiç hata yakalama yoktu — çünkü `FakeAiProvider` hiçbir zaman hata fırlatmıyor. Ama gerçek bir AI sağlayıcısı (ileride) ağ hatası, zaman aşımı veya kota aşımı yüzünden kolayca hata fırlatabilir. Bu olmadan, böyle bir hata Day 13'ün genel `ProblemDetails` 500 mekanizmasına düşerdi — teknik olarak "çökmüyor" ama kullanıcıya hiçbir şey söylemeyen, genel bir hata.

Bu, Day 51'in `Complete` action'ındaki bildirim hatası yönetimiyle **kasıtlı olarak ters** bir yaklaşım: Day 51'de "bildirim başarısız olursa, zaten başarılı olmuş asıl işlemi (Complete) asla bozma" vardı — bildirim, ana işlemin bir *yan etkisiydi*, o yüzden hatası yutulup loglanıyordu, kullanıcıya hiç yansıtılmıyordu. Burada ise özet, action'ın **tek amacı** — o yüzden hata "sessizce yutulamaz"; kullanıcıya anlamlı bir durum bildirilmesi gerekiyor. Bu yüzden `catch` bloğu Day 51'deki gibi normal bir `Ok(...)` dönmüyor, bilinçli olarak `503 Service Unavailable` dönüyor.

**`503` seçimi neden `200 + fallback metin` değil:** Eğer `200 OK` içinde "özet şu an kullanılamıyor" gibi bir metin dönseydik, bu metin gerçek bir özetmiş gibi görünüp bir arayüz tarafından yanlışlıkla gösterilebilirdi. `503`, isteğin **gerçekten başarısız olduğunu** açıkça işaretliyor — bir istemci bunu "tekrar dene" mantığıyla ayırt edebilir, gerçek bir özetle karıştırmaz.

**Nasıl çalışır:** `catch (Exception ex)` — burada bilinçli olarak geniş bir `Exception` yakalanıyor (belirli bir exception tipi değil), çünkü bugün gerçek bir AI sağlayıcısı olmadığı için hangi spesifik exception türlerinin (timeout, HTTP hatası, vs.) fırlatılacağı henüz bilinmiyor; `_logger.LogWarning(ex, ...)` — Day 55'in yapılandırılmış loglama altyapısına (`CorrelationIdMiddleware`, JSON console logging) otomatik olarak dahil olan bir log satırı, hatanın tüm detayını (`ex`) ve hangi iş emri için olduğunu (`{WorkOrderId}`) kaydediyor. `StatusCode(StatusCodes.Status503ServiceUnavailable, "...")`, bu controller'daki diğer tüm el ile yazılmış durum kodu dönüşleriyle (`StatusCode(StatusCodes.Status403Forbidden, "...")` gibi) aynı üslupta.

---

## 2. `tests/FieldOps.Api.Tests/WorkOrdersAuthorizationIntegrationTests.cs` — yeni `using`'ler

```csharp
using FieldOps.Api.Application;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
```

**Neden bu şekilde yazıldı:** `IAiProvider`'a erişmek için `FieldOps.Api.Application`; `ConfigureTestServices` uzantı metodunu kullanabilmek için `Microsoft.AspNetCore.TestHost`; `services.AddSingleton<...>()` uzantı metodunu kullanabilmek için `Microsoft.Extensions.DependencyInjection`.

---

## 3. `GetSummary_WhenAiProviderFails_ReturnsServiceUnavailable` testi ve canlı hata ayıklama hikâyesi

```csharp
[Fact]
public async Task GetSummary_WhenAiProviderFails_ReturnsServiceUnavailable()
{
    var brokenAiClient = _factory
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IAiProvider, ThrowingAiProvider>()))
        .CreateClient();
    brokenAiClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
    brokenAiClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");

    var createResponse = await brokenAiClient.PostAsJsonAsync("/api/workorders", new { Title = $"AI-Failure-{Guid.NewGuid():N}" });
    var created = await createResponse.Content.ReadFromJsonAsync<WorkOrderDto>();
    await brokenAiClient.PostAsJsonAsync($"/api/workorders/{created!.Id}/assign", new { EmployeeId = 1 });
    await brokenAiClient.PostAsJsonAsync($"/api/workorders/{created.Id}/evidence", new { Note = "Checked the pump." });

    var summaryResponse = await brokenAiClient.GetAsync($"/api/workorders/{created.Id}/summary");

    Assert.Equal(HttpStatusCode.ServiceUnavailable, summaryResponse.StatusCode);
}

private class ThrowingAiProvider : IAiProvider
{
    public Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Simulated AI provider outage.");
}
```

**Neden bu şekilde yazıldı (genel amaç):** `FakeAiProvider` hiçbir zaman hata fırlatmadığı için, controller'daki `try`/`catch`'in gerçekten çalıştığını kanıtlamanın tek yolu, testlerde **geçici olarak** hata fırlatan bir `IAiProvider` implementasyonu (`ThrowingAiProvider`) DI konteynerine sokmak. `_factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(...))`, ASP.NET Core'un test altyapısının tam olarak bu senaryo için sunduğu standart mekanizma: paylaşılan `_factory`'yi (bu sınıftaki her testin kullandığı) hiç değiştirmeden, sadece BU testin kullandığı ayrı, tek seferlik bir host oluşturuyor; `ConfigureTestServices`, uygulamanın kendi `Program.cs`'indeki servis kayıtlarından **sonra** çalışacak şekilde tasarlanmış özel bir uzantı metodu — bu yüzden `AddSingleton<IAiProvider, ThrowingAiProvider>()` çağrısı, gerçek `FakeAiProvider` kaydını geçersiz kılıyor (.NET'in DI konteyneri, aynı arayüz için birden fazla kayıt olduğunda **sonuncusunu** kullanır).

### Canlı olarak yakalanan gerçek bir hata: testin ilk hâli yanlış nedenle geçiyordu

Bu testin **ilk yazılan hâli**, iş emrini oluşturduktan hemen sonra, hiç evidence notu eklemeden doğrudan `/summary`'yi çağırıyordu. Çalıştırıldığında beklenmedik şekilde `200 OK` döndü — `ThrowingAiProvider` kayıtlı olmasına rağmen. İlk şüphe "DI override çalışmıyor" oldu; bunu doğrulamak için **canlı bir deney** yapıldı: `FakeAiProvider`'ın kendisi geçici olarak *her zaman* hata fırlatacak şekilde değiştirildi ve tüm `GetSummary` testleri tekrar çalıştırıldı. Sonuç çok öğreticiydi:
- Evidence notu **ekleyen** `GetSummary_WithEvidenceNotes_ReturnsFakeAiProviderSummary` testi doğru şekilde `503`'e döndü (yani `try`/`catch` gerçekten çalışıyordu).
- Evidence notu **eklemeyen** yeni testimiz yine `200 OK` döndü — kayıtlı sağlayıcı ne olursa olsun.

Bu, gerçek sebebi ortaya çıkardı: `WorkOrderNoteSummaryService.SummarizeAsync` (Day 63), notlar listesi **boşsa** `IAiProvider`'ı hiç çağırmadan sabit bir mesaj döndürüyor. Testimiz hiç evidence notu eklemediği için, bu "boş liste" kısayoluna düşüyordu — `IAiProvider` (ister `FakeAiProvider`, ister `ThrowingAiProvider`) **hiçbir zaman çağrılmıyordu**. Yani DI override'ın kendisi baştan beri doğru çalışıyordu; asıl hata testin senaryosundaydı.

Bu, **Day 62'nin canlı yakaladığı "boş body" hatasıyla birebir aynı sınıftan bir hata**: bir test, gerçekte kanıtlamak istediği şeyi kanıtlamadan yeşile dönüyordu. Düzeltme: iş emri kendi kendine atanıp (`assign` ile `EmployeeId: 1`, aynı client'ın hem admin hem atanan kişi olarak devam edebilmesi için) bir evidence notu eklendikten **sonra** `/summary` çağrılıyor — böylece çağrı gerçekten `IAiProvider`'a ulaşıyor ve `ThrowingAiProvider` üzerinden gerçekten başarısız oluyor.

**Nasıl çalışır (düzeltilmiş hâliyle):**
- `WithWebHostBuilder(...)`, orijinal `_factory`'nin (Testcontainers SQL Server bağlantısı dahil) tüm yapılandırmasını miras alan, ama `ConfigureTestServices` ile ek bir DI değişikliği içeren **yeni, bağımsız bir host** üretiyor.
- İş emri kendi kendine atanıyor (`EmployeeId: 1`, aynı `brokenAiClient`'ın çalıştığı kişi), böylece `AddEvidence`'ın `ValidateOwnership` kontrolü (Day 42) geçiliyor.
- `ThrowingAiProvider`, sadece bu test dosyasına özel, private, iç içe (nested) bir sınıf — `WorkOrderNoteSummaryServiceTests.cs`'teki `ThrowingAiProvider` ile aynı isim ama farklı dosyada, farklı bir amaç için (orada "hiç çağrılmadı" kanıtı, burada "çağrıldı ve patladı" kanıtı).

---

## 4. Canlı Red→Green kanıtı

`WorkOrdersController.GetSummary`'deki `try`/`catch` geçici olarak kaldırıldı:
```
GetSummary_WhenAiProviderFails_ReturnsServiceUnavailable -> FAIL (Expected: ServiceUnavailable, Actual: InternalServerError)
```
Geri eklenince yeşile döndü. Bu, testin gerçekten `try`/`catch`'in varlığına bağlı olduğunu, kodun başka bir yerinden yanlışlıkla geçmediğini kanıtlıyor.

---

## Regresyon (Day 64)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65 (64 -> 65, 1 yeni test)
dotnet build StockPilot.slnx  → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx   → 0 Hata, 0 Uyarı
```

## Demo basitleştirmesi vs. üretim gereksinimi

Bugün genel bir `catch (Exception ex)` kullanıldı — gerçek bir AI sağlayıcısının fırlatabileceği spesifik hata türleri (timeout, rate-limit, geçersiz yanıt) ayırt edilmedi; hepsi aynı `503` mesajına yönlendiriliyor. Gerçek bir sağlayıcıyla, hata türüne göre farklı davranış (örneğin rate-limit için farklı bir mesaj/durum kodu, ya da otomatik yeniden deneme) ileride gerekebilir — bugünün kapsamı sadece "herhangi bir hata, isteği çökertmesin" ilkesini kanıtlamak.
