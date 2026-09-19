# Day 40 — Kod Notları

Faz 3, **Hafta 9**'un ilk günü. Konu: **Work Orders modülünün temeli** — FieldOps'un asıl var oluş amacı olan iş emirlerinin ilk hali. Week 8'in üç gerçek açıkla (Day 35, 38, 39) öğrettiği tenant izolasyonu + üyelik deseni, bu sefer **baştan doğru** kuruldu.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. Yeni modül — `FieldOps.Modules.WorkOrders`

`dotnet new classlib` ile oluşturuldu, `FieldOps.slnx`'e eklendi, `Organizations`/`Employees`'in kurduğu **aynı** desen tekrarlandı:

```csharp
// Domain/WorkOrder.cs — internal, host bunu asla doğrudan göremez
internal class WorkOrder
{
    public int Id { get; set; }
    public string Title { get; set; }
    public int OrganizationId { get; set; }   // plain int, Organization'a referans değil
    public WorkOrderStatus Status { get; set; }
}

// WorkOrderStatus.cs — public enum, şimdilik tek değer
public enum WorkOrderStatus { Open }

// WorkOrderSummary.cs — public DTO
public record WorkOrderSummary(int Id, string Title, int OrganizationId, WorkOrderStatus Status);

// IWorkOrderDirectory.cs — modülün tek public sözleşmesi
public interface IWorkOrderDirectory
{
    IReadOnlyList<WorkOrderSummary> GetAll();
    WorkOrderSummary Create(string title, int organizationId);
}

// InMemoryWorkOrderDirectory.cs — internal implementasyon, seed verisi yok
// (Employees'teki gibi bir "bootstrap" sorunu burada yok — iş emri oluşturmak
// için zaten var olan organizasyon + çalışan üyeliği yeterli)

// WorkOrdersModule.cs — public static AddWorkOrdersModule() extension
```

**Neden bu kadar tanıdık geliyor:** Day 32/33'te `Organizations`/`Employees` için kurduğumuz "internal domain + public DTO + public interface + modül-özel DI kaydı" deseni, üçüncü modülde de **aynen** tekrar ediyor — bu, bir mimari desenin gerçekten genellenebilir olduğunun kanıtı.

---

## 2. `WorkOrdersController` — Week 8'in dersleri baştan doğru

```csharp
[HttpGet]
public ActionResult<IReadOnlyList<WorkOrderDto>> GetAll(...)
{
    var membershipError = ValidateMembership(organizationId, actingEmployeeId);
    if (membershipError is not null) { return membershipError; }
    ...
}

[HttpPost]
public ActionResult<WorkOrderDto> Create(...)
{
    var membershipError = ValidateMembership(organizationId, actingEmployeeId);
    if (membershipError is not null) { return membershipError; }
    ...
}

private ActionResult? ValidateMembership(int? organizationId, int? actingEmployeeId)
{
    if (organizationId is null) { return BadRequest("X-Organization-Id header is required."); }
    if (actingEmployeeId is null) { return BadRequest("X-Employee-Id header is required."); }

    var actingEmployee = _employeeDirectory.GetById(actingEmployeeId.Value);
    if (actingEmployee is null) { return BadRequest($"Employee {actingEmployeeId} does not exist."); }

    if (actingEmployee.OrganizationId != organizationId)
    {
        return StatusCode(StatusCodes.Status403Forbidden, "You can only act within your own organization.");
    }

    return null;
}
```

**Bugünün en önemli farkı — bu bir düzeltme değil, baştan doğru tasarım:** `EmployeesController`, bu üç kontrole (header yok, çalışan yok, organizasyon eşleşmiyor) **üç ayrı günde, üç ayrı açık bulduktan sonra** ulaştı (Day 35, 38, 39). `WorkOrdersController`, aynı üç kontrolü **ilk yazıldığı günden itibaren** içeriyor.

**Neden bu sefer bir yardımcı metoda (`ValidateMembership`) çıkardık, ama `EmployeesController`'da çıkarmamıştık (Day 39):**
- `EmployeesController`'da `Create` ve `GetAll` **tam aynı değildi** — `Create`'in ek bir rol kontrolü vardı, `GetAll`'ın yoktu. Farklı olan iki şeyi zorla ortaklaştırmak, yarım bir soyutlama olurdu.
- Burada `Create` ve `GetAll`, **aynı dosyada, aynı anda yazılırken**, birebir aynı üç kontrolü istiyor — hiçbir fark yok. Bu, "rule of three"nin katı bir sayı kuralı olmadığını, asıl kriterin **gerçekten aynı olup olmadığı** olduğunu gösteriyor: iki farklı zamanda ortaya çıkan, kısmen farklı iki kullanım (Employees) vs. aynı anda yazılan, tamamen özdeş iki kullanım (WorkOrders) — ikincisi soyutlamayı hemen haklı çıkarıyor.

---

## 3. Canlı doğrulama

```
GET /api/workorders (hic header yok)                          → 400
POST /api/workorders, Org1 Admin (id=1)                        → 201, status: Open (0)
GET /api/workorders, Org1 calisani Org2'yi istiyor              → 403
GET /api/workorders, Org1 kendi listesini goruyor                → 200, olusturulan is emri gorunuyor
```

Dördü de ilk denemede beklenen sonucu verdi — Day 35/38/39'un aksine, hiçbir sürpriz yok, çünkü kontroller baştan doğru yazıldı.

---

## 4. Otomatik testler + Red→Green kanıtı

`tests/FieldOps.Api.Tests/WorkOrdersAuthorizationIntegrationTests.cs` — 4 test: kimliksiz istek, çalışan header'ı yok, yanlış organizasyon, oluştur+listele (tenant izolasyonu).

Kanıt için organizasyon-eşleşme kontrolünü yorum satırına aldım:
```
dotnet test --filter GetAll_ByEmployeeFromAnotherOrganization_ReturnsForbidden
→ BAŞARISIZ (Expected: Forbidden, Actual: OK)
```
Geri getirdim → `dotnet test FieldOps.slnx` → **15/15 başarılı** (11 eski + 4 yeni).

---

## 5. Regresyon

```
dotnet test FieldOps.slnx    → 15/15
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## Bugünün bilinçli sınırı

Bugün sadece **oluşturma + listeleme** var, sabit bir `Open` durumuyla. Atama (assignment), durum geçişleri (`Assigned`, `InProgress`, `Completed`), dosya kanıtı (file evidence), müşteri onayı (customer approval) — hepsi Week 9'un ilerleyen günlerine bilerek bırakıldı. Bugünün amacı, modülün **temelini** Week 8'in derslerini içine gömerek kurmaktı.
