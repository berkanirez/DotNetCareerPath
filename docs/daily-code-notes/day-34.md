# Day 34 — Kod Notları

Faz 3, Hafta 7, Gün 34. Konu: application service katmanı (SRP), FieldOps'un ilk otomatik testi.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. `src/FieldOps.Api/Application/EmployeeCreationResult.cs`

```csharp
public class EmployeeCreationResult
{
    public bool Succeeded { get; }
    public EmployeeSummary? Employee { get; }
    public string? Error { get; }

    private EmployeeCreationResult(bool succeeded, EmployeeSummary? employee, string? error) { ... }

    public static EmployeeCreationResult Success(EmployeeSummary employee) => new(true, employee, null);
    public static EmployeeCreationResult Failure(string error) => new(false, null, error);
}
```

**Neden `ActionResult` değil, kendi sınıfımız:** `ActionResult`, `BadRequest()`, `Ok()` gibi şeyler **ASP.NET Core'a özgü** — HTTP dünyasının kavramları. Bugün yazacağımız `EmployeeApplicationService`'in HTTP'den **hiç haberi olmayacak** (aşağıya bakınız), bu yüzden ona "başarılı mı, değilse neden" diyebileceği kendi, sade sonuç tipine ihtiyacı var.

**Neden constructor `private`, sadece `Success`/`Failure` metotları `public`:** Bu, dışarıdan `new EmployeeCreationResult(true, null, "hata")` gibi **anlamsız/tutarsız** bir kombinasyon oluşturulmasını engelliyor — sadece "başarılı, çalışan var, hata yok" ya da "başarısız, çalışan yok, hata var" durumlarına izin veriliyor.

---

## 2. `src/FieldOps.Api/Application/EmployeeApplicationService.cs` — asıl taşınan mantık

```csharp
public class EmployeeApplicationService
{
    private readonly IEmployeeDirectory _employeeDirectory;
    private readonly IOrganizationDirectory _organizationDirectory;

    public EmployeeApplicationService(IEmployeeDirectory employeeDirectory, IOrganizationDirectory organizationDirectory)
    {
        _employeeDirectory = employeeDirectory;
        _organizationDirectory = organizationDirectory;
    }

    public EmployeeCreationResult CreateEmployee(string name, int organizationId)
    {
        var organization = _organizationDirectory.GetById(organizationId);
        if (organization is null)
        {
            return EmployeeCreationResult.Failure($"Organization {organizationId} does not exist.");
        }

        var employee = _employeeDirectory.Create(name, organizationId);
        return EmployeeCreationResult.Success(employee);
    }
}
```

Bu, dünden (`EmployeesController.Create`'in içinde) **birebir aynı mantık** — sadece **taşındı**. Fark: bu sınıf `using Microsoft.AspNetCore.Mvc;` diye bir şey **hiç import etmiyor** — `ControllerBase`, `ActionResult`, `BadRequest` gibi hiçbir ASP.NET Core kavramını bilmiyor. Tamamen düz bir C# sınıfı.

---

## 3. `src/FieldOps.Api/Controllers/EmployeesController.cs` — sadeleşme

```csharp
[HttpPost]
public ActionResult<EmployeeDto> Create(CreateEmployeeRequest request)
{
    var result = _employeeApplicationService.CreateEmployee(request.Name, request.OrganizationId);
    if (!result.Succeeded)
    {
        return BadRequest(result.Error);
    }

    var dto = new EmployeeDto(result.Employee!.Id, result.Employee.Name, result.Employee.OrganizationId);
    return StatusCode(StatusCodes.Status201Created, dto);
}
```

**Öncesi/sonrası karşılaştırması:** Dün bu metot hem "organizasyon var mı" kontrolünü hem "çalışan oluştur"u hem de HTTP durum kodu çevirisini tek bir yerde yapıyordu. Bugün sadece: (1) servise sor, (2) sonucu HTTP'ye çevir. Controller artık **iş kuralını hiç bilmiyor** — sadece "başarılıysa 201, değilse 400" diyor.

**Bu, tam olarak SOLID'in Single Responsibility Principle'ı:** Controller'ın artık **tek bir** değişme sebebi var (HTTP şekli değişirse). İş kuralı değişirse (mesela "organizasyon aktif değilse de reddet" gibi yeni bir kural eklenirse), sadece `EmployeeApplicationService` değişir, controller'a hiç dokunulmaz.

---

## 4. `src/FieldOps.Api/Program.cs` — DI kaydı

```csharp
builder.Services.AddScoped<EmployeeApplicationService>();
```

Modüllerin aksine (`AddOrganizationsModule()`/`AddEmployeesModule()` gibi kendi kayıt fonksiyonları var), `EmployeeApplicationService` doğrudan host'un kendi sınıfı olduğu için, host kendi `Program.cs`'inde doğrudan kaydediyor — gizlenecek bir `internal` implementasyonu yok, zaten `FieldOps.Api`'nin kendi parçası.

---

## 5. `tests/FieldOps.Api.Tests/` — FieldOps'un **ilk** otomatik testi

```csharp
public void CreateEmployee_ExistingOrganization_ReturnsSuccessWithEmployee()
{
    var service = new EmployeeApplicationService(
        new FakeEmployeeDirectory(),
        new FakeOrganizationDirectory(existingOrganizationId: 1));

    var result = service.CreateEmployee("Jane Tech", 1);

    Assert.True(result.Succeeded);
    ...
}
```

**Bugünün en somut kanıtı:** Bu test, `new EmployeeApplicationService(...)` diyerek **doğrudan** bir C# nesnesi oluşturuyor — hiçbir HTTP isteği, hiçbir `WebApplicationFactory`, hiçbir controller yok. `FakeOrganizationDirectory`/`FakeEmployeeDirectory`, StockPilot'un `InMemoryProductStore`'u gibi **elle yazılmış**, gerçek (ama basit) implementasyonlar — Moq değil, çünkü arayüzler bu kadar küçükken elle yazmak daha basit (`CLAUDE.md`'nin "gerekmedikçe mock'lama" ruhuna uygun).

**Bunun StockPilot Day 14 ile paralelliği:** Day 14'te, `ProductsController`'ın `static` bir listeye doğrudan bağımlı olması testleri imkansızlaştırıyordu — `IProductStore` soyutlaması bunu çözmüştü. Bugün de benzer bir motivasyon var, ama farkı: sorun "test edilemiyor" değildi (controller'ı zaten doğrudan test edebiliyorduk StockPilot'ta), sorun **controller'ın çok fazla iş yapması**ydı. Çözüm de farklı: soyutlama eklemek değil, **mantığı taşımak**.

---

## Canlı kanıt

```
dotnet test FieldOps.slnx → 2/2 basarili (FieldOps'un ilk otomatik testleri!)

POST /api/employees (organizationId=1, gecerli) → 201 (dunden hic degismedi)
POST /api/employees (organizationId=999, gecersiz) → 400 (dunden hic degismedi)

dotnet build FieldOps.slnx   → 0 Hata, 0 Uyari
dotnet test  RoadmapOS.slnx  → 8/8 (etkilenmedi)
dotnet test  StockPilot.slnx → 23/27 basarili, 4 basarisiz
   (basarisiz olanlar Day 28'in Testcontainers testleri — Docker Desktop
    su an bu makinede kapali, bugunku FieldOps degisiklikleriyle ILGISIZ,
    tamamen ortamsal bir durum, dogru sekilde not dusuldu)
```

---

## Ek: `FieldOps.slnx` CI'a eklendi (Day 32'den beri açık duran not)

`.github/workflows/ci.yml`'e, `RoadmapOS`/`StockPilot`'un aynısı üç adım eklendi: `Restore`/`Build`/`Test FieldOps`. FieldOps'un testleri (bugünkü `EmployeeApplicationServiceTests`) tamamen elle yazılmış sahte nesnelerle çalıştığı için, StockPilot'un aksine **hiçbir Docker/veritabanı hazırlığına ihtiyaç duymuyor** — CI'a eklenmesi tek satırlık bir iş oldu.
