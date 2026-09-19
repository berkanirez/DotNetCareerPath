# Day 37 — Kod Notları

Faz 3, Hafta 8, Gün 37. Konu: **membership** (bir çalışanın kendi organizasyonu içindeki rolü) ve buna dayanan ilk **granular RBAC** kuralı — sadece Admin rolündeki bir çalışan yeni çalışan oluşturabilir.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. `src/FieldOps.Modules.Employees/EmployeeRole.cs` — yeni dosya

```csharp
public enum EmployeeRole
{
    Admin,
    Member
}
```

**Neden:** Day 35'e kadar sistem sadece "hangi organizasyon" (`X-Organization-Id`) sorusuna cevap veriyordu. Ama gerçek bir SaaS'ta her organizasyonun içinde de roller farklıdır — StockPilot Day 25'teki Admin/Employee ayrımının **tenant-farkında** (her organizasyonun kendi Admin'i olan) karşılığı burada başlıyor.

**Önemli fark:** Bu `EmployeeRole`, StockPilot'un sistem-geneli rolünden farklı — iki farklı organizasyondaki iki `Admin` çalışanın birbiriyle hiçbir ilişkisi yok, rol sadece kendi `OrganizationId`'si bağlamında anlam taşıyor.

---

## 2. `src/FieldOps.Modules.Employees/Domain/Employee.cs` — `Role` eklendi

```csharp
internal class Employee
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int OrganizationId { get; set; }
    public EmployeeRole Role { get; set; }

    public Employee(string name, int organizationId, EmployeeRole role)
    {
        Name = name;
        OrganizationId = organizationId;
        Role = role;
    }
}
```

Sadece yeni bir alan ve constructor parametresi — mimari olarak yeni bir şey yok, Day 33'ten beri kurulu `internal` domain / `public` DTO ayrımı aynen devam ediyor.

---

## 3. `EmployeeSummary.cs` / `IEmployeeDirectory.cs` — sözleşme genişletildi

```csharp
public record EmployeeSummary(int Id, string Name, int OrganizationId, EmployeeRole Role);

public interface IEmployeeDirectory
{
    IReadOnlyList<EmployeeSummary> GetAll();
    EmployeeSummary? GetById(int id);                              // yeni
    EmployeeSummary Create(string name, int organizationId, EmployeeRole role);  // Role eklendi
}
```

**`GetById` neden gerekli oldu:** RBAC kuralı, "bu isteği yapan çalışan Admin mi?" sorusuna cevap vermek için, `X-Employee-Id` header'ındaki ID'ye karşılık gelen çalışanı **bulabilmesi** gerekiyor — `GetAll()` üzerinden filtrelemek yerine, `OrganizationsModule`'ün zaten Day 32'de sahip olduğu `GetById` desenini burada da tekrarladık.

---

## 4. `InMemoryEmployeeDirectory.cs` — seed verisi eklendi (ilk kez!)

```csharp
private readonly List<Employee> _employees = new()
{
    new Employee("Org1 Admin", organizationId: 1, EmployeeRole.Admin) { Id = 1 },
    new Employee("Org1 Member", organizationId: 1, EmployeeRole.Member) { Id = 2 },
    new Employee("Org2 Admin", organizationId: 2, EmployeeRole.Admin) { Id = 3 },
    new Employee("Org2 Member", organizationId: 2, EmployeeRole.Member) { Id = 4 }
};
private int _nextId = 5;
```

**Neden bu bir zorunluluktu, sadece kolaylık değil:** Day 33'ten beri `InMemoryEmployeeDirectory` **boş** başlıyordu — hiç çalışan yoktu, hepsi API üzerinden yaratılıyordu. Ama bugün "sadece Admin rolündeki bir çalışan yeni çalışan oluşturabilir" kuralını eklersek, ve sistemde hiç Admin yoksa, **hiç kimse asla ilk çalışanı bile oluşturamaz** — kilitlenme (bootstrap problemi). Bu yüzden her organizasyon için önceden var olan bir Admin + bir Member seed edildi; `InMemoryOrganizationDirectory`'nin (Day 32) zaten seed ettiği `Id=1`/`Id=2` organizasyonlarıyla eşleşiyor.

`GetById` da eklendi:
```csharp
public EmployeeSummary? GetById(int id)
{
    var employee = _employees.FirstOrDefault(e => e.Id == id);
    return employee is null ? null : ToSummary(employee);
}
```

---

## 5. `EmployeeApplicationService.CreateEmployee` — yeni çalışanın rolü sabitlendi

```csharp
var employee = _employeeDirectory.Create(name, organizationId, EmployeeRole.Member);
```

**Bilinçli sınırlama:** API üzerinden oluşturulan her çalışan **her zaman Member** olarak başlıyor — yeni bir Admin oluşturmak bugünün kapsamı dışında. Bugün var olan tek Admin'ler, seed edilmiş olanlar. (Bir Admin'in bir Member'ı Admin'e terfi ettirmesi gibi bir akış, gelecekte ayrı bir görev.)

---

## 6. `EmployeesController.Create` — asıl RBAC kapısı

```csharp
[HttpPost]
public ActionResult<EmployeeDto> Create(
    CreateEmployeeRequest request,
    [FromHeader(Name = "X-Organization-Id")] int? organizationId,
    [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId)
{
    if (organizationId is null) { return BadRequest("X-Organization-Id header is required."); }
    if (actingEmployeeId is null) { return BadRequest("X-Employee-Id header is required."); }

    var actingEmployee = _employeeDirectory.GetById(actingEmployeeId.Value);
    if (actingEmployee is null) { return BadRequest($"Employee {actingEmployeeId} does not exist."); }

    if (actingEmployee.Role != EmployeeRole.Admin)
    {
        return StatusCode(StatusCodes.Status403Forbidden, "Only an Admin can create employees.");
    }

    var result = _employeeApplicationService.CreateEmployee(request.Name, organizationId.Value);
    ...
}
```

**Akış, adım adım:**
1. `X-Organization-Id` yoksa → `400` (Day 35'ten beri değişmedi).
2. `X-Employee-Id` yoksa → `400` (bugünkü yeni kontrol, aynı desen).
3. `X-Employee-Id` var ama böyle bir çalışan yoksa → `400` ("kim olduğunu iddia ediyorsun ama böyle biri yok").
4. Çalışan var ama `Role != Admin` → **`403`** — kimlik biliniyor ("sen busun"), ama yetki yok. Bu, StockPilot Day 25'in 401-vs-403 dersinin tam burada tekrar uygulanması: `400`/`401` "seni tanımıyorum/isteğin bozuk" derken, `403` "seni tanıyorum ama hayır" der.
5. Hepsi geçtiyse → mevcut akış (organizasyon var mı kontrolü, sonra oluşturma) değişmeden devam ediyor.

**Neden `Forbid()` değil de `StatusCode(403, ...)`:** ASP.NET Core'un `Forbid()` metodu, gerçek bir authentication scheme'e (StockPilot'taki JWT bearer gibi) bağlı çalışır ve onu "challenge" etmeye çalışır. FieldOps'ta hiç `AddAuthentication()` yok — bu yüzden `Forbid()` çağırmak muhtemelen çalışma zamanı hatası verirdi. Zaten `X-Organization-Id`/`X-Employee-Id` kontrolleri de elle yazılmış `BadRequest` çağrıları olduğu için, `403`'ü de aynı elle-yazılmış tarzda (`StatusCode(...)`) döndürmek tutarlı.

**`EmployeeDto`/`GetAll`'a `Role` eklendi** — hem oluşturulan hem listelenen çalışanların rolünü görebilmek için (`EmployeeDto(int Id, string Name, int OrganizationId, EmployeeRole Role)`).

---

## 7. Canlı kanıt — gerçek uygulama çalıştırılıp curl ile 4 senaryo

```
1) Admin (id=1, org1) create        → 201
2) Member (id=2, org1) create       → 403 "Only an Admin can create employees."
3) Var olmayan çalışan (id=999)     → 400 "Employee 999 does not exist."
4) X-Employee-Id hiç yok            → 400 "X-Employee-Id header is required."
```

Dördü de tahmin edilen sonuçla birebir eşleşti — Day 35'in aksine bu sefer sürpriz yok, çünkü kural doğrudan yazılan `if` satırlarının davranışıydı, ASP.NET Core'un örtük bir davranışı değildi.

---

## 8. Mevcut testlerin güncellenmesi

`EmployeeApplicationServiceTests.cs`'in `FakeEmployeeDirectory`'si yeni arayüze uyacak şekilde güncellendi (`GetById` eklendi, `Create` üçüncü bir `EmployeeRole` parametresi aldı).

`EmployeesAuthorizationIntegrationTests.cs`'teki `Create` çağıran testler, artık zorunlu olan `X-Employee-Id` header'ı olmadan **gerçekten kırmızıya düştü** (400 dönüyordu, test 201/başarı bekliyordu) — bu, RBAC kapısının gerçekten devrede olduğunun ekstra bir kanıtı. Üç test de `X-Employee-Id: 1` (seed edilen Org1 Admin) eklenerek düzeltildi; `Create_OrganizationHeader_ReturnsOk` ayrıca `Create_ByAdmin_ReturnsCreated` olarak yeniden adlandırıldı (geçen oturumda not edilen isim tutarsızlığı da bu vesileyle giderildi).

**Sonuç:** `dotnet test FieldOps.slnx` → 7/7 (hiç yeni otomatik test eklenmedi bugün — RBAC'ın otomatik testlerle kanıtlanması, planın kendisinin de belirttiği gibi, Day 38'in işi).

---

## Bugünün bilinçli sınırı

`X-Employee-Id`, `X-Organization-Id` (Day 35) ile aynı sınıftan bir basitleştirme: istemcinin düz bir header'la beyan ettiği, **doğrulanmamış** bir kimlik. İstemci istediği ID'yi yazabilir — bugünün amacı RBAC'ın mekaniğini (rol kontrolü, doğru status kodları) kurmak, kimlik doğrulamasını değil. Üretimde bu, roadmap'in henüz kurulmamış "Identity" modülünden gelen gerçek bir JWT claim'i olurdu.
