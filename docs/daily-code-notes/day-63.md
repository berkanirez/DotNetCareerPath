# Day 63 — Kod Notları

Faz 3, Hafta 12, Gün 63. Konu: **AI provider abstraction ve iş emri not özetleme**. Day 62'de tamamlanan yetkilendirme denetiminden sonra, Week 12'nin roadmap listesindeki bir sonraki iki madde birlikte ele alındı: "AI provider abstraction" (genel yetenek) ve "work-order note summarization" (bu yeteneği kullanan somut özellik).

---

## 1. `src/FieldOps.Api/Application/IAiProvider.cs`

```csharp
public interface IAiProvider
{
    Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken);
}
```

**Neden bu şekilde yazıldı:** Day 51'deki `INotificationSender` ile birebir aynı mantık: gerçek bir bağımlılığı (bugün: gerçek bir AI sağlayıcısı — OpenAI, Anthropic vb.) doğrudan kullanmak yerine, bu bağımlılığın **arkasına bir arayüz** koyuyoruz. Böylece:
- Testlerde gerçek bir API çağrısı yapılmaz (maliyet yok, ağ bağımlılığı yok, sonuç her zaman aynı — non-determinism yok).
- Gerçek bir API anahtarı bugün hiç gerekmez.
- İleride gerçek bir sağlayıcı eklenmek istendiğinde, sadece bu arayüzün yeni bir implementasyonu yazılır; `WorkOrderNoteSummaryService` ve `WorkOrdersController`'da **hiçbir şey değişmez**.

Arayüz bilinçli olarak **genel** tutuldu: "bir prompt ver, bir tamamlama (completion) al" — "iş emri notlarını özetle" değil. Bu ayrım roadmap'in kendisinde de var: "AI provider abstraction" ve "work-order note summarization" iki ayrı madde. Genel arayüz ileride başka özellikler (örneğin bir açıklama oluşturma, bir soruya cevap üretme) için de yeniden kullanılabilir; onu tek bir özelliğe (`SummarizeWorkOrderNotes` gibi) kilitlemek, gereksiz bir daraltma olurdu.

**Nasıl çalışır:** Sıradan bir C# arayüzü. `Task<string>` dönüş tipi, ileride gerçek bir sağlayıcının network üzerinden asenkron çalışacağını baştan varsayıyor — bugünkü sahte implementasyon senkron çalışsa bile (aşağıda görülecek), arayüz gerçek kullanım şeklini yansıtıyor. `CancellationToken cancellationToken` parametresi, bu workspace'in Day 40'tan beri süregelen kuralı: anlamlı her asenkron sınırda iptal desteği.

---

## 2. `src/FieldOps.Api/Application/FakeAiProvider.cs`

```csharp
public class FakeAiProvider : IAiProvider
{
    public Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken)
    {
        return Task.FromResult($"[Fake AI summary] {prompt}");
    }
}
```

**Neden bu şekilde yazıldı:** Bugünün **tek** implementasyonu — `LoggingNotificationSender`'ın (Day 51) tam karşılığı. Gerçek bir AI çağrısı yok, gerçek bir API anahtarı yok. Kasıtlı olarak **deterministik**: aynı prompt her zaman aynı sonucu üretir (bu durumda, prompt'un kendisini bir önek ile birlikte geri döndürüyor). Bu, hem bugünkü canlı manuel kontrolü hem de yarınki/gelecekteki unit testleri **öngörülebilir** kılıyor — gerçek bir AI modeli kullansaydık, her çalıştırmada farklı bir metin dönebilir, bu da testleri kırılgan hale getirirdi.

`$"[Fake AI summary] {prompt}"` seçimi bilinçli: dönen metnin içinde hem "bu sahte bir sağlayıcıdan geldi" bilgisini (`[Fake AI summary]` etiketi) hem de gerçekten gönderilen prompt'un kendisini (dolayısıyla orijinal notları) taşıyor — bu da testlerde "gerçekten doğru veriyle mi çağrıldı" sorusunu kolayca doğrulanabilir kılıyor.

**Nasıl çalışır:** `Task.FromResult(...)`, zaten elde var olan bir değeri bir `Task`'a sarmanın standart yolu — gerçek bir asenkron işlem (network çağrısı, disk I/O) olmadığında `async`/`await` kullanmaya gerek yok, bu sadece senkron bir değeri asenkron arayüze uydurmanın en ucuz yolu.

---

## 3. `src/FieldOps.Api/Application/WorkOrderNoteSummaryService.cs`

```csharp
public class WorkOrderNoteSummaryService
{
    private readonly IAiProvider _aiProvider;

    public WorkOrderNoteSummaryService(IAiProvider aiProvider)
    {
        _aiProvider = aiProvider;
    }

    public Task<string> SummarizeAsync(IReadOnlyList<string> evidenceNotes, CancellationToken cancellationToken)
    {
        if (evidenceNotes.Count == 0)
        {
            return Task.FromResult("No evidence notes have been added to this work order yet.");
        }

        var numberedNotes = evidenceNotes.Select((note, index) => $"{index + 1}. {note}");
        var prompt = "Summarize the following field service evidence notes in one or two sentences:\n"
            + string.Join("\n", numberedNotes);

        return _aiProvider.SummarizeAsync(prompt, cancellationToken);
    }
}
```

**Neden bu şekilde yazıldı:** Bu, `IAiProvider`'ın **genel** yeteneğini kullanan **somut** özellik. "İş emri notlarını nasıl bir prompt'a çeviririz" bilgisi sadece burada yaşıyor — `IAiProvider`'ın kendisi bunu hiç bilmiyor, `WorkOrdersController` da bilmiyor. Bu ayrım, ileride "başka bir şeyi özetle" (örneğin müşteri yorumlarını özetle) ihtiyacı doğduğunda, aynı `IAiProvider`'ın üzerine ikinci, bağımsız bir servis daha yazılabileceği anlamına geliyor — `IAiProvider`'a dokunmadan.

**Boş liste kontrolü kasıtlı olarak en başta:** Not yoksa, `IAiProvider` hiç çağrılmıyor — ne gerçek bir sağlayıcıda gereksiz bir istek/maliyet olurdu, ne de anlamsız bir prompt ("Summarize the following notes:\n" + hiçbir şey) gönderilmiş olurdu. Bu davranış `WorkOrderNoteSummaryServiceTests.cs`'de özel olarak kanıtlanıyor (aşağıda).

**Nasıl çalışır:**
- `evidenceNotes.Select((note, index) => $"{index + 1}. {note}")` — LINQ'un iki parametreli `Select` aşırı yüklemesi (overload); `index` parametresi elemanın listedeki sırasını (0'dan başlayarak) veriyor, `index + 1` ile 1'den başlayan numaralı bir liste oluşturuluyor (`"1. Checked the pump"`, `"2. Replaced the filter"` gibi).
- `string.Join("\n", numberedNotes)` — bu numaralı satırları alt alta, satır sonlarıyla birleştiriyor.
- Metot **kendisi `async` değil** — doğrudan `_aiProvider.SummarizeAsync(...)`'in döndürdüğü `Task<string>`'i geri veriyor (tail call / pass-through). Bu, gereksiz bir `async`/`await` state machine'i kurmadan asenkron zinciri sürdürmenin standart, hafif yolu; sadece boş-liste dalında `Task.FromResult` ile senkron bir sonucu aynı imzaya uydurmak gerekiyor.

---

## 4. `src/FieldOps.Api/Controllers/WorkOrdersController.cs` — yapıcı (constructor) değişikliği

```csharp
private readonly WorkOrderNoteSummaryService _workOrderNoteSummaryService;

public WorkOrdersController(
    // ...mevcut parametreler...
    WorkOrderNoteSummaryService workOrderNoteSummaryService,
    ILogger<WorkOrdersController> logger)
{
    // ...
    _workOrderNoteSummaryService = workOrderNoteSummaryService;
    _logger = logger;
}
```

**Neden bu şekilde yazıldı:** Dependency Injection — controller, `WorkOrderNoteSummaryService`'i nasıl oluşturacağını bilmiyor, sadece ihtiyaç duyduğunu bildiriyor; ASP.NET Core'un DI konteyneri (Program.cs'de kayıtlı) bunu çalışma zamanında sağlıyor. Bu, bu controller'daki her bağımlılığın (Day 40'tan beri) izlediği aynı desen.

**Nasıl çalışır:** `WorkOrderNoteSummaryService` doğrudan somut sınıf olarak enjekte ediliyor (bir arayüz üzerinden değil) — çünkü kendisi zaten `WorkOrderReportService`, `WorkOrderAssignmentService`, `IdempotencyService` gibi bu projede "bir arayüzü olmayan, ama yine de DI ile yönetilen" servislerle aynı kategoride: gerçek bir alternatif implementasyonu olmayan, sadece iş mantığını bir araya toplayan bir sınıf.

---

## 5. `src/FieldOps.Api/Controllers/WorkOrdersController.cs` — yeni `GetSummary` action

```csharp
[HttpGet("{id}/summary")]
public async Task<ActionResult<string>> GetSummary(
    int id,
    [FromHeader(Name = "X-Organization-Id")] int? organizationId,
    [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId,
    CancellationToken cancellationToken)
{
    var membershipError = ValidateMembership(organizationId, actingEmployeeId);
    if (membershipError is not null)
    {
        return membershipError;
    }

    var workOrder = _workOrderDirectory.GetById(id);
    if (workOrder is null || workOrder.OrganizationId != organizationId)
    {
        return BadRequest($"Work order {id} does not exist.");
    }

    var summary = await _workOrderNoteSummaryService.SummarizeAsync(workOrder.EvidenceNotes, cancellationToken);
    return Ok(summary);
}
```

**Neden bu şekilde yazıldı:** Yetkilendirme seviyesi bilinçli bir seçim: `ValidateOwnership` (sadece atanan çalışan) değil, sadece `ValidateMembership` (organizasyondaki herhangi biri) kullanılıyor. Gerekçe: zaten kaydedilmiş notların bir özetini **okumak**, `GetAll`/`GetStatusReport` gibi genel bir okuma işlemi — `Start`/`Complete`/`AddEvidence` gibi "işi bizzat yapan kişiye özel" bir eylem değil. Bir organizasyon içindeki herhangi bir çalışanın, o organizasyonun herhangi bir iş emrini görüntüleyebilmesi zaten `GetAll`'da kabul edilmiş bir kural; bu action o kuralı bozmuyor.

`workOrder is null || workOrder.OrganizationId != organizationId` kontrolü, bu dosyadaki diğer birçok action'da (Day 41'den beri) tekrarlanan "var olmayan" ile "başka organizasyona ait" durumlarını **ayırt edilemez** kılma deseni — bilgi sızdırmama (information hiding) ilkesi.

**Nasıl çalışır:** Action `async` — çünkü `_workOrderNoteSummaryService.SummarizeAsync(...)`'i `await` ediyor (ki o da kendi içinde `IAiProvider.SummarizeAsync`'i `await`'liyor, ya da senkron `Task.FromResult` dönüyor). `ActionResult<string>` dönüş tipi, hem `BadRequest(...)` gibi hata durumlarını hem de `Ok(summary)` gibi başarı durumunu tek bir metot imzasında taşıyabilmeyi sağlıyor — bu controller'daki her action'ın kullandığı standart ASP.NET Core deseni.

---

## 6. `src/FieldOps.Api/Program.cs` — DI kaydı

```csharp
builder.Services.AddSingleton<IAiProvider, FakeAiProvider>();
builder.Services.AddSingleton<WorkOrderNoteSummaryService>();
```

**Neden bu şekilde yazıldı:** `INotificationSender`/`LoggingNotificationSender` (Day 51) ile birebir aynı kayıt şekli — arayüz için `AddSingleton<Arayüz, Somut>`. `WorkOrderNoteSummaryService` de Singleton, çünkü kendisinin hiçbir "istek başına" durumu (per-request state) yok; tek bağımlılığı olan `IAiProvider` de zaten Singleton (`FakeAiProvider` durumsuz — stateless). Bu, `IdempotencyService`'in (Day 54) "durumsuzsa Singleton güvenlidir" mantığının aynısı.

**Nasıl çalışır:** ASP.NET Core'un yerleşik DI konteyneri, `builder.Services` üzerinden yapılan bu kayıtları okuyarak, `WorkOrdersController`'ın yapıcısındaki `WorkOrderNoteSummaryService` parametresini otomatik olarak dolduruyor; `WorkOrderNoteSummaryService`'in kendi yapıcısındaki `IAiProvider` parametresini de aynı şekilde, kayıtlı `FakeAiProvider` örneğiyle dolduruyor — DI zinciri (constructor injection) burada iki seviyeye çıkıyor.

---

## 7. `tests/FieldOps.Api.Tests/WorkOrderNoteSummaryServiceTests.cs` — yeni unit test dosyası

```csharp
[Fact]
public async Task SummarizeAsync_WithNotes_BuildsPromptFromNotesAndReturnsProviderResult()
{
    var provider = new RecordingAiProvider();
    var service = new WorkOrderNoteSummaryService(provider);

    var summary = await service.SummarizeAsync(
        new List<string> { "Checked the pump", "Replaced the filter" },
        CancellationToken.None);

    Assert.Contains("Checked the pump", provider.ReceivedPrompt);
    Assert.Contains("Replaced the filter", provider.ReceivedPrompt);
    Assert.Equal($"[Recorded] {provider.ReceivedPrompt}", summary);
}

[Fact]
public async Task SummarizeAsync_WithNoNotes_ReturnsPlaceholderWithoutCallingProvider()
{
    var service = new WorkOrderNoteSummaryService(new ThrowingAiProvider());

    var summary = await service.SummarizeAsync(new List<string>(), CancellationToken.None);

    Assert.Equal("No evidence notes have been added to this work order yet.", summary);
}

private class RecordingAiProvider : IAiProvider
{
    public string ReceivedPrompt { get; private set; } = string.Empty;

    public Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken)
    {
        ReceivedPrompt = prompt;
        return Task.FromResult($"[Recorded] {prompt}");
    }
}

private class ThrowingAiProvider : IAiProvider
{
    public Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("IAiProvider must not be called when there are no evidence notes.");
}
```

**Neden bu şekilde yazıldı:** Day 33'ün `FakeOrganizationDirectory`/`FakeEmployeeDirectory` deseniyle aynı gerekçe: `IAiProvider` tek metotlu, küçük bir arayüz — gerçek (ama minimal) bir sahte implementasyon yazmak, Moq gibi bir mock kütüphanesi kurmaktan daha basit ve daha okunabilir.

**İki farklı sahte sınıf, iki farklı amaç için:**
- `RecordingAiProvider`: gerçekten çağrıldığında ne aldığını **hatırlıyor** (`ReceivedPrompt`), böylece test "servis doğru prompt'u mu oluşturdu" sorusunu somut olarak doğrulayabiliyor — sadece "bir string döndü" değil.
- `ThrowingAiProvider`: çağrılırsa **patlıyor**. Bu, "not yoksa `IAiProvider` hiç çağrılmamalı" iddiasını **gerçekten kanıtlayan** tek yöntem — kodu okuyup "if kontrolü var, demek ki çağrılmıyor" diye varsaymak yerine, çağrılırsa testin kesin olarak patlayacağı bir düzenek kuruluyor.

**Nasıl çalışır:** Her iki sahte sınıf da `IAiProvider`'ı implement ediyor, bu yüzden `WorkOrderNoteSummaryService`'in yapıcısına doğrudan geçirilebiliyorlar — gerçek `FakeAiProvider`'ın (Application katmanındaki, Program.cs'de kayıtlı olan) yerine, sadece bu test dosyasına özel, private, iç içe (nested) sınıflar olarak tanımlanmışlar.

---

## 8. `tests/FieldOps.Api.Tests/WorkOrdersAuthorizationIntegrationTests.cs` — yeni entegrasyon testleri

```csharp
[Fact]
public async Task GetSummary_NoOrganizationHeader_ReturnsBadRequest()
{
    var client = _factory.CreateClient();
    var response = await client.GetAsync("/api/workorders/999999/summary");
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task GetSummary_WithEvidenceNotes_ReturnsFakeAiProviderSummary()
{
    // ... iş emri oluştur, ata, bir evidence notu ekle ...
    var summaryResponse = await org1AdminClient.GetAsync($"/api/workorders/{assigned.Id}/summary");
    var summary = await summaryResponse.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
    Assert.Contains(noteText, summary);
    Assert.Contains("[Fake AI summary]", summary);
}

[Fact]
public async Task GetSummary_WithNoEvidenceNotes_ReturnsPlaceholder()
{
    // ... hiç evidence eklenmemiş bir iş emri ...
    Assert.Contains("No evidence notes", summary);
}
```

**Neden bu şekilde yazıldı:** Day 61/62'nin çıkardığı dersin **bugünden itibaren** uygulanması: yeni bir action yazılırken, kendi "header yok" testi **denetim beklenmeden, en baştan** ekleniyor — Day 62'nin 7 action için sonradan yaptığı düzeltmenin bir daha tekrarlanmaması için.

İkinci test (`GetSummary_WithEvidenceNotes_...`), bu günün gerçek "canlı kanıtı": sadece kod okunarak "DI doğru bağlanmış olmalı" diye varsayılmıyor; gerçek bir HTTP isteği, gerçek ASP.NET Core pipeline'ı, gerçek (Testcontainers) SQL Server üzerinden akıyor ve dönen metnin içinde hem eklenen orijinal not metni hem de `FakeAiProvider`'a özgü `[Fake AI summary]` etiketi aranıyor — bu, "gerçekten `FakeAiProvider` çalıştı, başka bir şey değil" iddiasının somut kanıtı.

Üçüncü test, `WorkOrderNoteSummaryServiceTests`'teki "not yoksa `IAiProvider` çağrılmaz" unit testinin, tam HTTP zincirinden geçen entegrasyon karşılığı.

**Nasıl çalışır:** `summaryResponse.Content.ReadAsStringAsync()` — `GetSummary` action'ı `ActionResult<string>` döndürdüğü ve `Ok(summary)` çağırdığı için, HTTP yanıt gövdesi JSON olarak serileştirilmiş bir string (tırnak içinde). `Assert.Contains` bir alt dize araması yaptığı için, bu JSON tırnaklarının varlığı testi etkilemiyor.

---

## Regresyon (Day 63)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 64/64 (59 -> 64, 5 yeni test: 2 unit + 3 entegrasyon)
dotnet build StockPilot.slnx  → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx   → 0 Hata, 0 Uyarı
```

## Demo basitleştirmesi vs. üretim gereksinimi

Bugün **bilinçli olarak** yapılmayanlar:
- Gerçek bir AI sağlayıcısına (OpenAI/Anthropic) bağlanmak — bunun için gerçek bir API anahtarı (Day 57/59'un kanıtlanmış ortam değişkeni/secret mekanizmasıyla), ağ hatası yönetimi ve maliyet/rate-limit değerlendirmesi gerekir; hepsi ileri bir güne bırakıldı.
- Sahte özetleme mantığının "iyi" bir özet üretmesi — bugünkü `FakeAiProvider` sadece prompt'u geri yansıtıyor; amaç, abstraction'ın doğru bağlandığını kanıtlamak, iyi bir özet üretmek değil.
