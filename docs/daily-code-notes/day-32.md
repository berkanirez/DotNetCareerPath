# Day 32 — Kod Notları

Faz 3, Hafta 7, Gün 32 — FieldOps'un ilk günü. Konu: modular monolith sınırları, bağımlılık yönü, ilk ADR.

Bu doküman, bugün oluşturulan her parçayı **yazılma sırasına göre** gezer.

---

## 1. `FieldOps.slnx` + iki proje

```
FieldOps.slnx
├── src/FieldOps.Modules.Organizations/   ← modül (class library)
└── src/FieldOps.Api/                     ← host (ASP.NET Core Web API)
```

**Neden iki ayrı proje, tek bir proje değil:** RoadmapOS ve StockPilot'ta her şey **tek** bir web projesindeydi (`RoadmapOS.Web`, `StockPilot.Api`). FieldOps'ta bilerek farklı gidiyoruz — `Organizations`, kendi başına **ayrı bir derlenebilir birim** (class library). Bunun somut anlamı: `FieldOps.Api`, `Organizations`'ın içine **istese bile** dolaylı yollardan giremiyor, sadece o modülün **dışarı açtığı** şeye erişebiliyor (aşağıya bakınız).

---

## 2. `src/FieldOps.Modules.Organizations/Domain/Organization.cs`

```csharp
public class Organization
{
    public int Id { get; set; }
    public string Name { get; set; }

    public Organization(string name)
    {
        Name = name;
    }
}
```

**Neden bu kadar basit:** RoadmapOS Day 2'nin `Skill` sınıfıyla aynı başlangıç noktası — henüz kalıcılık yok, sadece domain kavramının kendisi (`Organization` = bir kiracı/tenant firma).

---

## 3. `src/FieldOps.Modules.Organizations/IOrganizationDirectory.cs` — modülün "ön kapısı"

```csharp
public interface IOrganizationDirectory
{
    IReadOnlyList<Organization> GetAll();
}
```

**Bu, bugünün en önemli dosyası.** Bu arayüz, `Organizations` modülünün **dışarıya açtığı tek şey**. `FieldOps.Api` (ya da ileride başka bir modül), `Organization` sınıfını veya `InMemoryOrganizationDirectory`'nin nasıl çalıştığını **bilmek zorunda değil** — sadece bu arayüzü biliyor. Bu, StockPilot'taki `IProductStore`'un `EfProductStore`'u `ProductsController`'dan gizlemesiyle **aynı prensip** — burada fark, bunun tek bir sınıf seviyesinde değil, **tüm bir modül** seviyesinde uygulanması.

---

## 4. `src/FieldOps.Modules.Organizations/InMemoryOrganizationDirectory.cs`

```csharp
public class InMemoryOrganizationDirectory : IOrganizationDirectory
{
    private readonly List<Organization> _organizations = new()
    {
        new Organization("Acme Field Services") { Id = 1 },
        new Organization("Blue Ridge Maintenance") { Id = 2 }
    };

    public IReadOnlyList<Organization> GetAll()
    {
        return _organizations;
    }
}
```

RoadmapOS Day 3'ün `InMemorySkillCatalog`'u ile birebir aynı desen — kalıcılık olmadan, gerçek bir implementasyon.

---

## 5. `src/FieldOps.Api/Program.cs` — modülün DI ile bağlanması

```csharp
using FieldOps.Modules.Organizations;
...
builder.Services.AddSingleton<IOrganizationDirectory, InMemoryOrganizationDirectory>();
```

Host, modülün **gerçek implementasyonunu** (`InMemoryOrganizationDirectory`) sadece burada, tek bir satırda biliyor — geri kalan her yerde (`OrganizationsController` dahil) sadece `IOrganizationDirectory` arayüzü kullanılıyor.

---

## 6. `src/FieldOps.Api/Models/OrganizationDto.cs` + `Controllers/OrganizationsController.cs`

```csharp
public record OrganizationDto(int Id, string Name);
```
```csharp
[HttpGet]
public ActionResult<IReadOnlyList<OrganizationDto>> GetAll()
{
    var organizations = _organizationDirectory.GetAll();
    var dtos = organizations.Select(o => new OrganizationDto(o.Id, o.Name)).ToList();
    return Ok(dtos);
}
```

Tanıdık desen — `Organization` domain nesnesi doğrudan dışarı verilmiyor, `OrganizationDto`'ya çevriliyor (StockPilot'un `ProductDto`'suyla aynı disiplin).

---

## Canlı kanıt — bağımlılık yönü kuralı gerçekten uygulanıyor mu?

```
Organizations modulunun .csproj'u:
  <ItemGroup> yok, hicbir <ProjectReference> yok

Api'nin .csproj'u:
  <ProjectReference Include="..\FieldOps.Modules.Organizations\..." />
```

**Sonuç:** `Organizations`, `Api`'ye referans veremez bile — böyle bir satır eklemeye çalışsak, döngüsel bir referans hatası alırdık (bir proje, kendisine bağımlı olan bir projeye bağımlı olamaz). Kural, sadece bir "iyi niyet" kuralı değil, **derleme zamanında zorlanan gerçek bir kısıtlama**.

```
GET /api/organizations → HTTP 200
[{"id":1,"name":"Acme Field Services"},{"id":2,"name":"Blue Ridge Maintenance"}]

dotnet build FieldOps.slnx   → 0 Hata, 0 Uyari
dotnet build StockPilot.slnx → 0 Hata, 0 Uyari (etkilenmedi)
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyari (etkilenmedi)
```

---

## Sonradan güçlendirilen kısım — "kibarlık kuralı"ndan gerçek zorlamaya

Berkan'ın çok yerinde bir sorusu üzerine ("interface kurup bunun ne artısı var") fark edildi: `Organization` ve `InMemoryOrganizationDirectory` ilk halde **`public`**'ti — yani `FieldOps.Api`, `IOrganizationDirectory`'yi atlayıp bunlara doğrudan erişebilirdi. ADR'da "bunlar dışarıdan erişilmemeli" yazmak, derleyicinin bunu **zorlaması** ile aynı şey değil.

**Adım 1 — `Organization`'ı `internal` yapmayı denedik:**
```csharp
internal class Organization { ... }
```
Derleme **gerçekten** patladı:
```
CS0050: Tutarsız erişilebilirlik: 'IReadOnlyList<Organization>' dönüş türü,
'IOrganizationDirectory.GetAll()' yönteminden daha az erişilebilir
```
Sebep: `IOrganizationDirectory` (public) bir arayüz, `internal` bir tipi döndüremez — dilin kendisi bunu bir çelişki olarak görüyor.

**Doğru düzeltme — modülün kendi public DTO'su:**
```csharp
// OrganizationSummary.cs — modülün DIŞARI verdiği tek şekil
public record OrganizationSummary(int Id, string Name);
```
`IOrganizationDirectory.GetAll()` artık `Organization` değil, `OrganizationSummary` döndürüyor. `Organization` (domain) ve `InMemoryOrganizationDirectory` (implementasyon) tamamen `internal` kalabiliyor.

**Adım 2 — peki `Program.cs`, `InMemoryOrganizationDirectory`'yi nasıl DI'a kaydedecek, o da `internal` değil mi?**
Bunun için modülün kendi "kurulum" fonksiyonunu yazdık:
```csharp
// OrganizationsModule.cs
public static class OrganizationsModule
{
    public static IServiceCollection AddOrganizationsModule(this IServiceCollection services)
    {
        return services.AddSingleton<IOrganizationDirectory, InMemoryOrganizationDirectory>();
    }
}
```
Bu metot **modülün içinde** yazıldığı için `internal` sınıfa erişebiliyor. `Program.cs` artık sadece:
```csharp
builder.Services.AddOrganizationsModule();
```
diyor — somut sınıfın adını **hiç bilmeden**.

**Canlı kanıt — sınırı bilerek ihlal etmeyi denedik:**
```csharp
// Program.cs'e GECICI olarak eklendi:
var demo = new InMemoryOrganizationDirectory();
```
```
CS0122: 'InMemoryOrganizationDirectory' öğesine koruma düzeyi nedeniyle erişilemiyor
```
Gerçekten reddedildi — satır geri alındı, build tekrar temiz.

---

## `docs/adr/0001-modular-monolith-one-way-dependencies.md` — ilk ADR

**Neden bir ADR yazıyoruz, sadece kodu yazıp geçmiyoruz:** Kod, **ne** yaptığımızı gösteriyor ama **neden** böyle yaptığımızı göstermiyor. Birkaç ay sonra (ya da başka biri) bu koda bakıp "neden `Organizations` ayrı bir proje, neden tek bir DbContext'te her şey yok" diye sorduğunda, ADR bu sorunun cevabını (Phase 4'teki gelecekteki bölünme ihtiyacı, alternatiflerin neden reddedildiği) kalıcı olarak saklıyor. Standart ADR formatı: Status, Context (problem neydi), Decision (ne karar verildi), Consequences (bunun bedeli/faydası ne), Alternatives Considered (başka ne düşünüldü, neden seçilmedi).
