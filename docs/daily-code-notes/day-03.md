# Day 3 — Kod Notları

Faz 1, Hafta 1, Gün 3. Konu: ASP.NET Core request lifecycle, middleware, routing, controller, view, dependency injection ve ilk read-only vertical slice.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. `src/RoadmapOS.Web/Views/_ViewImports.cshtml`

```cshtml
@using RoadmapOS.Web
@using RoadmapOS.Web.Domain
@using RoadmapOS.Web.Models
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

**Neden:** Bugün ilk kez bir Razor view (`Views/Skills/Index.cshtml`), `Domain` klasöründeki `Skill` tipini kullanacak. Her view dosyasının başına tek tek `@using RoadmapOS.Web.Domain` yazmak yerine, bunu **tüm view'lar için bir kere** burada tanımlıyoruz.

**Nasıl çalışıyor:** `_ViewImports.cshtml`, Razor'a özel bir dosya — kendisi bir sayfa render etmez, aynı klasördeki (ve alt klasörlerdeki) tüm `.cshtml` dosyalarına otomatik olarak uygulanan ortak ayarları tutar. `@using` satırları, C# dosyalarındaki `using` ile birebir aynı işi görür — namespace import eder. TS'teki bir `tsconfig.json`'da global tip tanımlarını tek yerden ayarlamaya benzetebilirsin: her dosyada tekrar etmek yerine bir kere merkezi olarak tanımlanıyor.

---

## 2. `src/RoadmapOS.Web/Domain/ISkillCatalog.cs`

```csharp
namespace RoadmapOS.Web.Domain;

public interface ISkillCatalog
{
    IReadOnlyList<Skill> GetAll();
}
```

**Neden:** `SkillsController`'ın skill verisine ihtiyacı var, ama controller'ın bu veri **nereden geldiğini** (bugün: bellekte hardcoded; Day 4'ten sonra: SQL Server) bilmesine gerek yok — hatta bilmemesi daha iyi. Bu interface, "skill verisi isteyen biri, `GetAll()` diye bir metot çağırabilir" sözleşmesini tanımlıyor; controller sadece bu sözleşmeye güvenecek, hangi sınıfın bunu gerçekleştirdiğiyle ilgilenmeyecek.

**Nasıl çalışıyor:** `IReadOnlyList<Skill>` dönüş tipi bilinçli bir seçim: çağıran taraf listeyi **okuyabilir** ama üzerine eleman ekleyip çıkaramaz (`List<Skill>`'in aksine) — yani `InMemorySkillCatalog`'un kendi iç verisinin dışarıdan yanlışlıkla bozulmasını derleme zamanında engelliyor. Bu, TS'teki `readonly Skill[]` veya `ReadonlyArray<Skill>` ile aynı fikir.

---

## 3. `src/RoadmapOS.Web/Domain/InMemorySkillCatalog.cs`

```csharp
namespace RoadmapOS.Web.Domain;

public class InMemorySkillCatalog : ISkillCatalog
{
    private readonly List<Skill> _skills = new()
    {
        new("C#", "Language", SkillLevel.CanImplementWithGuidance, SkillLevel.CanExplainProduction),
        new("ASP.NET Core", "Framework", SkillLevel.CanExplainPurpose, SkillLevel.CanImplementIndependently),
        new("EF Core", "Framework", SkillLevel.NotStudied, SkillLevel.CanImplementIndependently)
        {
            Notes = "Not started yet"
        },
        new("SQL Server", "Database", SkillLevel.NotStudied, SkillLevel.CanImplementWithGuidance)
    };

    public IReadOnlyList<Skill> GetAll()
    {
        var sorted = new List<Skill>(_skills);
        sorted.Sort();
        return sorted;
    }
}
```

**Neden:** `ISkillCatalog` sözleşmesinin bugünkü, gerçek bir veritabanı olmadan çalışan implementasyonu. Day 2'de `Program.cs` içinde geçici olarak test ettiğimiz dört skill'i buraya, kalıcı ve gerçekten kullanılan bir yere taşıdık.

**Nasıl çalışıyor:**
* `private readonly List<Skill> _skills = new() { ... }` — `private`, bu listenin sınıf dışından **hiç erişilemeyeceği** anlamına geliyor (encapsulation); `readonly`, alanın constructor'dan sonra başka bir listeyle **değiştirilemeyeceği** anlamına geliyor (listenin içeriği değil, listeye atanan referans sabitleniyor). `new()` yine target-typed new — sol taraftan (`List<Skill>`) tipi zaten biliniyor.
* `public IReadOnlyList<Skill> GetAll() { ... }` — dışarıya `_skills`'in **kendisini değil**, her çağrıda `new List<Skill>(_skills)` ile alınmış **yeni bir kopyasını**, sıralanmış halde döndürüyor. Neden kopya: eğer doğrudan `_skills`'i sıralayıp döndürseydik, `Sort()` orijinal iç listeyi kalıcı olarak değiştirirdi — her çağrıda "kirlenen" bir state'e sahip olurduk. Kopya üzerinde sıralama yapmak, `GetAll()`'u her çağrıldığında bağımsız ve öngörülebilir kılıyor.
* `sorted.Sort()` — Day 2'de `Skill`'e uyguladığımız `IComparable<Skill>` burada gerçek işine yarıyor: seviyeye, eşitlikte isme göre sıralama.

---

## 4. `src/RoadmapOS.Web/Program.cs` — DI kaydı ve geçici bloğun kaldırılması

```csharp
using RoadmapOS.Web.Domain;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<ISkillCatalog, InMemorySkillCatalog>();

var app = builder.Build();

// Configure the HTTP request pipeline.
```

**Neden:** Day 2'nin geçici konsol doğrulama bloğu tamamen silindi — artık gerçek bir controller/view akışımız olduğu için, konsola yazdırıp gözlemlemeye gerek kalmadı (o günün çıktısı zaten `docs/daily-code-notes/day-02.md`'de kalıcı olarak kayıtlı). Yerine tek satırlık gerçek bir DI kaydı geldi.

**Nasıl çalışıyor:** `builder.Services`, uygulama ayağa kalkmadan önce "hangi interface istenirse hangi somut sınıf verilecek" bilgisini tutan DI container'ın kayıt arayüzü. `AddSingleton<ISkillCatalog, InMemorySkillCatalog>()` şu anlama geliyor: "biri constructor'ında `ISkillCatalog` isterse, ona `InMemorySkillCatalog`'un **tek bir örneğini** ver, uygulama boyunca aynı örneği tekrar kullan." `Singleton` seçildi çünkü veri sabit ve her HTTP isteğinde yeniden oluşturmaya gerek yok — Day 4'te EF Core `DbContext` geldiğinde bu `Scoped` olacak (her istekte taze bir context), o zaman nedenini ayrıca açıklayacağız.

---

## 5. `src/RoadmapOS.Web/Controllers/SkillsController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Controllers;

public class SkillsController : Controller
{
    private readonly ISkillCatalog _skillCatalog;

    public SkillsController(ISkillCatalog skillCatalog)
    {
        _skillCatalog = skillCatalog;
    }

    public IActionResult Index()
    {
        var skills = _skillCatalog.GetAll();
        return View(skills);
    }
}
```

**Neden:** İlk gerçek read-only vertical slice'ın controller katmanı. Bu controller, verinin nereden geldiğiyle **hiç ilgilenmiyor** — sadece "bana `ISkillCatalog` sözleşmesini uygulayan bir şey ver" diyor. Day 4'te `InMemorySkillCatalog` yerine EF Core tabanlı bir implementasyon `Program.cs`'te kaydedildiğinde, bu dosyanın **tek satırı bile değişmeyecek**.

**Nasıl çalışıyor:**
* `private readonly ISkillCatalog _skillCatalog;` + constructor parametresi — bu, **constructor injection**. ASP.NET Core'un DI container'ı, bir `SkillsController` oluşturması gerektiğinde, constructor'ın `ISkillCatalog` istediğini görür, `Program.cs`'te kayıtlı `InMemorySkillCatalog` örneğini otomatik olarak buraya verir. Biz hiçbir yerde `new SkillsController(...)` yazmıyoruz — framework bunu senin yerine, her HTTP isteğinde yapıyor.
* `Index()` — Day 1'de gördüğümüz convention'ın (route → `{controller=Home}/{action=Index}/{id?}`) devamı: bu action, `/Skills` veya `/Skills/Index` adresinden erişilebilir olacak, çünkü controller adı "Skills", action adı "Index" (varsayılan action).
* `return View(skills)` — Day 1'deki `return View()`'dan farkı: burada view'a bir **model** (`skills` listesi) veriyoruz. Convention yine aynı şekilde çalışıyor: `Views/Skills/Index.cshtml` aranıyor.

---

## 6. `src/RoadmapOS.Web/Views/Skills/Index.cshtml`

```cshtml
@model IReadOnlyList<Skill>

@{
    ViewData["Title"] = "Skills";
}

<h1>Skills</h1>

<table class="table">
    <thead>
        <tr>
            <th>Name</th>
            <th>Category</th>
            <th>Current Level</th>
            <th>Target Level</th>
            <th>At Target?</th>
        </tr>
    </thead>
    <tbody>
        @foreach (var skill in Model)
        {
            <tr>
                <td>@skill.Name</td>
                <td>@skill.Category</td>
                <td>@skill.CurrentLevel</td>
                <td>@skill.TargetLevel</td>
                <td>@(skill.IsAtTarget ? "Yes" : "No")</td>
            </tr>
        }
    </tbody>
</table>
```

**Neden:** Kullanıcının (şimdilik sadece biz) gerçekten göreceği HTML çıktısı — controller'ın hazırladığı veriyi tabloya döken katman.

**Nasıl çalışıyor:**
* `@model IReadOnlyList<Skill>` — bu view'ın **hangi tipte bir model beklediğini** derleyiciye bildiriyor (strongly-typed view). Controller'da `View(skills)` çağrıldığında, `skills`'in tipi bununla eşleşmezse **derleme hatası** alırız — TS'teki bir fonksiyon parametresinin tipini belirtmek gibi, "yanlış şekli" derleme zamanında yakalıyor.
* `@{ ViewData["Title"] = "Skills"; }` — `_Layout.cshtml`'in `<title>` etiketinde kullandığı bir sözlüğe (`ViewData`) değer yazıyor; bu yüzden tarayıcı sekmesinde "Skills - RoadmapOS.Web" görünüyor.
* `@foreach (var skill in Model)` — `Model`, yukarıda `@model` ile tipi belirtilen, controller'dan gelen gerçek veri (`skills` listesi). Razor içinde `@` işareti, HTML'in ortasında "buradan itibaren C# kodu başlıyor" demek.
* `@skill.Name`, `@skill.CurrentLevel` gibi tekil `@` kullanımları, bir C# ifadesinin **değerini** o noktaya HTML olarak basıyor — JS/TS'teki JSX'in `{skill.name}` yazımına çok yakın bir mantık.
* `@(skill.IsAtTarget ? "Yes" : "No")` — parantezli `@(...)` formu, ifade biraz daha karmaşık olduğunda (burada bir ternary) Razor'a "bunun tamamı tek bir C# ifadesi, HTML karışmasın" demenin yolu.

---

## Doğrulanan davranış

```
dotnet build → 0 Hata, 0 Uyarı
GET /        → HTTP 200 (regresyon yok, ana sayfa hâlâ çalışıyor)
GET /Skills  → HTTP 200, tabloda tam beklenen sırayla 4 skill:
               EF Core → SQL Server → ASP.NET Core → C#
               (Day 2'deki IComparable<Skill> mantığı, artık gerçek bir HTTP
               isteği üzerinden, InMemorySkillCatalog.GetAll() içinde çalıştı)
```

Bu, `IComparable<Skill>`'in Day 2'de sadece bir konsol çıktısında gördüğümüz davranışının, bugün gerçek bir controller → view akışında **aynı sonucu** verdiğinin kanıtı.
