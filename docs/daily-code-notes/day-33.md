# Day 33 — Kod Notları

Faz 3, Hafta 7, Gün 33. Konu: ikinci modül (`Employees`) ve modüller arası ilk gerçek referans kararı.

Bu doküman, bugün oluşturulan her parçayı **yazılma sırasına göre** gezer.

---

## 1. `src/FieldOps.Modules.Employees/Domain/Employee.cs`

```csharp
internal class Employee
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int OrganizationId { get; set; }

    public Employee(string name, int organizationId)
    {
        Name = name;
        OrganizationId = organizationId;
    }
}
```

**En önemli satır: `public int OrganizationId`.** Bu, `Organization` tipinde bir alan **değil** — düz bir `int`. Day 32'nin `Organizations` modülüne hiçbir proje referansı olmadan, bir çalışanın "hangi organizasyona ait olduğunu" ifade edebilmemizin yolu bu: tıpkı bir veritabanı tablosundaki foreign key sütununun, işaret ettiği tabloyu "bilmemesi" gibi — sadece bir kimlik numarası taşıyor.

---

## 2. `IEmployeeDirectory.cs` — bilinçli bir eksiklik

```csharp
public interface IEmployeeDirectory
{
    IReadOnlyList<EmployeeSummary> GetAll();
    EmployeeSummary Create(string name, int organizationId);
}
```

**Dikkat:** `Create` metodu, verilen `organizationId`'nin **gerçekten var olan bir organizasyon olup olmadığını hiç kontrol etmiyor.** Bu bir hata değil, bilinçli bir tasarım — bu modülün `Organizations`'a hiç referansı yok, dolayısıyla böyle bir kontrolü **yapabilecek durumda değil**. Bu kontrolü kim yapacak sorusu, bugünün asıl konusu (aşağıya bakınız).

---

## 3. `src/FieldOps.Api/Controllers/EmployeesController.cs` — host'un "orkestrasyon" rolü

```csharp
[HttpPost]
public ActionResult<EmployeeDto> Create(CreateEmployeeRequest request)
{
    var organization = _organizationDirectory.GetById(request.OrganizationId);
    if (organization is null)
    {
        return BadRequest($"Organization {request.OrganizationId} does not exist.");
    }

    var employee = _employeeDirectory.Create(request.Name, request.OrganizationId);
    ...
}
```

**Bu, bugünün en önemli kodu.** `EmployeesController`, hem `IEmployeeDirectory`'yi hem `IOrganizationDirectory`'yi constructor'a alıyor — **iki modülün de arayüzünü** biliyor (ki bu, host için tamamen normal; yasak olan, **modüllerin birbirini** tanıması). Bir çalışan oluşturulmadan **önce**, host önce `Organizations` modülüne soruyor ("bu id var mı"), cevap olumsuzsa `Employees` modülüne hiç gitmiyor bile. İki modül birbirini hiç görmüyor — host, ikisi arasında **köprü** görevi görüyor.

---

## Bağımsız görev — `organizationId` sorgu filtresi

```csharp
[HttpGet]
public ActionResult<IReadOnlyList<EmployeeDto>> GetAll([FromQuery] int? organizationId = null)
{
    var employees = _employeeDirectory.GetAll().AsEnumerable();

    if (organizationId is not null)
    {
        employees = employees.Where(e => e.OrganizationId == organizationId);
    }

    var dtos = employees.Select(e => new EmployeeDto(e.Id, e.Name, e.OrganizationId)).ToList();
    return Ok(dtos);
}
```

StockPilot Day 15'in `search`/`sortBy` deseninin aynısı — `int?` (nullable), verilmezse `null` kalıyor (`0` ile karışmasın diye), verilirse `.Where(...)` ile filtreleniyor. **Önemli:** Bu filtreleme mantığı tamamen `FieldOps.Api` içinde, `Employees` modülünün **dışında** duruyor — modülün kendisi hâlâ `Organizations`'ı hiç bilmiyor, sadece host, elindeki tam listeyi kendi filtreliyor.

Canlı kanıt:
```
POST (org=1, Jane) + POST (org=2, Bob)
GET /api/employees                    → ikisi de
GET /api/employees?organizationId=1   → sadece Jane
```

---

## Canlı kanıt

```
POST /api/employees {"name":"Jane Tech","organizationId":1}    → 201, calisan olusturuldu
POST /api/employees {"name":"Ghost Employee","organizationId":999} → 400 "Organization 999 does not exist."
GET  /api/employees → [{"id":1,"name":"Jane Tech","organizationId":1}]

FieldOps.Modules.Employees.csproj → hicbir <ProjectReference> yok
                                     (Organizations'a hala hicbir bagimlilik yok)

dotnet build FieldOps.slnx   → 0 Hata, 0 Uyari
dotnet build StockPilot.slnx → 0 Hata, 0 Uyari (etkilenmedi)
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyari (etkilenmedi)
```

---

## `docs/adr/0002-cross-module-references-via-host-orchestration.md` — ikinci ADR

Day 32'nin ADR'ı, "modüller birbirini nasıl çağıracak" sorusunu bilerek açık bırakmıştı — bugün gerçek bir ihtiyaç doğunca cevaplandı: **modüller birbirine asla doğrudan referans vermeyecek**, sadece host (`FieldOps.Api`) her iki modülün arayüzünü de bilip aralarında koordinasyon yapacak. Düşünülüp reddedilen alternatif: `Employees`'in `Organizations`'a doğrudan referans vermesi — bu, ADR 0001'in kurallarını ihlal etmezdi ama modüller arasında sınırsız büyüyebilecek bir bağımlılık ağının ilk adımı olurdu; host üzerinden tek bir kurala bağlı kalmak daha basit.
