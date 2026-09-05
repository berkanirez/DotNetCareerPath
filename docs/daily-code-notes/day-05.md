# Day 5 — Kod Notları

Faz 1, Hafta 1, Gün 5. Konu: model binding, validation, `Skill` create/edit flow, hata gösterimi, manuel doğrulama.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `src/RoadmapOS.Web/Domain/ISkillCatalog.cs` — sözleşmenin genişletilmesi

```csharp
namespace RoadmapOS.Web.Domain;

public interface ISkillCatalog
{
    IReadOnlyList<Skill> GetAll();
    Skill? GetById(int id);
    void Add(Skill skill);
    void Update(Skill skill);
}
```

**Neden:** Şimdiye kadar `ISkillCatalog` sadece "okuma" (`GetAll`) biliyordu. Create/Edit flow'u yazabilmek için sözleşmeye üç yeni yetenek eklemek gerekiyordu: tek bir kaydı ID'yle bulmak (`GetById`, edit formunu doldurmak için), yeni kayıt eklemek (`Add`), var olanı güncellemek (`Update`).

**Nasıl çalışıyor:** Bir interface'e yeni üye eklemek, onu uygulayan **her** sınıfı (`EfSkillCatalog` **ve** `InMemorySkillCatalog`) bu üç metodu da yazmaya **zorluyor** — aksi halde proje derlenmez. Bu, interface'in bir "sözleşme" olmasının somut sonucu: sözleşmeyi değiştirirsen, imzalayan herkes yeni şartları karşılamak zorunda.

---

## 2. `src/RoadmapOS.Web/Data/EfSkillCatalog.cs` — EF Core implementasyonu

```csharp
public Skill? GetById(int id)
{
    return _context.Skills.Find(id);
}

public void Add(Skill skill)
{
    _context.Skills.Add(skill);
    _context.SaveChanges();
}

public void Update(Skill skill)
{
    // 'skill' was already loaded (and is being tracked) by this same DbContext
    // via GetById earlier in the same request, so its changed properties are
    // already known to the change tracker — SaveChanges() is all that's needed.
    _context.SaveChanges();
}
```

**Neden/Nasıl:**
* `_context.Skills.Find(id)` — EF Core'un primary key'e göre tekil kayıt arama metodu; önce **o `DbContext`'in zaten bellekte tuttuğu (tracked) nesneler** arasına bakar, yoksa SQL Server'a `SELECT` atar. Bulamazsa `null` döner (dönüş tipi neden `Skill?`).
* `Add`: `_context.Skills.Add(skill)` yeni nesneyi change tracker'a "eklenecek" olarak işaretliyor, `SaveChanges()` gerçek `INSERT`'i tetikliyor.
* `Update`: Buradaki en can alıcı nokta — **hiçbir yerde `_context.Skills.Update(skill)` çağırmıyoruz**, sadece `SaveChanges()`. Sebep: `Update` metoduna gelen `skill` nesnesi, `SkillsController.Edit(POST)` içinde birkaç satır önce **aynı `DbContext`**'in `GetById` metoduyla (yani `Find(id)` ile) yüklenmişti. `AddScoped` sayesinde bir HTTP isteği boyunca **hep aynı `DbContext` örneği** kullanıldığı için, bu nesne zaten change tracker tarafından "izleniyor" (tracked) durumda. Controller'da `skill.Name = model.Name` gibi property atamaları yaptığımızda, EF Core bu değişiklikleri **otomatik olarak** fark ediyor. `SaveChanges()` çağrıldığında, değişen alanlar için gereken `UPDATE` SQL'i kendiliğinden üretiliyor. (`_context.Skills.Update(skill)` çağrısı, bunun aksine, **hiç izlenmeyen** — örneğin bir API'den JSON olarak deserialize edilmiş — "kopuk" (disconnected) bir nesne için kullanılır; bugünkü senaryomuz bu değil.)

---

## 3. `src/RoadmapOS.Web/Domain/InMemorySkillCatalog.cs` — bellek-içi implementasyonun tamamlanması

```csharp
public Skill? GetById(int id)
{
    foreach (var skill in _skills)
    {
        if (skill.Id == id)
        {
            return skill;
        }
    }
    return null;
}

public void Add(Skill skill)
{
    var nextId = 1;
    foreach (var existing in _skills)
    {
        if (existing.Id >= nextId)
        {
            nextId = existing.Id + 1;
        }
    }
    skill.Id = nextId;
    _skills.Add(skill);
}

public void Update(Skill skill)
{
    // No-op: 'skill' returned by GetById is the same in-memory instance
    // already stored in _skills, so changes to it are already reflected.
}
```

**Neden:** `InMemorySkillCatalog` şu an DI'da kayıtlı değil (Day 4'te çıkarmıştık), ama derlenebilmesi için `ISkillCatalog`'un tüm üyelerini uygulaması **zorunlu**. Bu, ileride (Day 7) test double olarak kullanılacağı için önemli — o gün gerçek DB olmadan `SkillsController`'ı test ederken bu sınıf devreye girecek.

**Nasıl çalışıyor:**
* `GetById`: `foreach` ile listeyi gezip `Id`'si eşleşeni arıyor — LINQ'un `FirstOrDefault`'unu henüz tanıtmadığımız için (Day 8) bilinçli olarak düz bir döngü kullandık.
* `Add`: EF Core'daki `Identity` (auto-increment) davranışını taklit ediyor — mevcut en büyük `Id`'yi bulup bir fazlasını yeni kayda atıyor.
* `Update`: Burada gerçekten **hiçbir şey yapmaya gerek yok** — çünkü `GetById`'nin döndürdüğü `Skill` nesnesi, `_skills` listesinin içindeki **aynı referans**. Controller onun property'lerini değiştirdiğinde, liste içindeki nesne zaten değişmiş oluyor; ayrıca bir "kaydet" adımına ihtiyaç yok. (EF Core'daki `Update`'in neden `SaveChanges()`'e ihtiyaç duyduğuyla, buradaki neden hiçbir şeye ihtiyaç duymadığı arasındaki fark: biri gerçek bir veritabanı bağlantısı, diğeri sadece bellekteki bir referans.)

---

## 4. `src/RoadmapOS.Web/Models/SkillFormModel.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Models;

public class SkillFormModel
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Category { get; set; } = string.Empty;

    [Required]
    public SkillLevel CurrentLevel { get; set; }

    [Required]
    public SkillLevel TargetLevel { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
```

**Neden:** `Skill` domain entity'sini doğrudan forma bağlamak yerine, sadece bu formun ihtiyacı olan alanları ve **kullanıcı girdisiyle ilgili** kuralları taşıyan ayrı bir sınıf. Bunun iki somut faydası var: (1) `Skill`, form/validation kaygılarından tamamen bağımsız, temiz bir domain modeli olarak kalıyor; (2) **over-posting** riskine kapanıyoruz — eğer form doğrudan `Skill`'e bağlansaydı, kötü niyetli biri form alanlarına olmayan bir `Id` veya (ileride ekleyeceğimiz) yetki alanı gönderip, olmaması gereken bir property'yi değiştirmeye çalışabilirdi. `SkillFormModel`, sadece **izin verdiğimiz** alanları içeriyor.

**Nasıl çalışıyor:**
* `[Required]` — bu alan boş/null gelirse `ModelState`'e bir hata eklenir.
* `[StringLength(100)]` — 100 karakterden uzun bir değer gelirse hata eklenir. (Bu, veritabanı seviyesinde bir kısıtlama değil — hatırlarsan Day 4'te migration `nvarchar(max)` üretmişti; bugünkü sınır sadece **uygulama seviyesinde**, kullanıcıya hemen geri bildirim vermek için. Gerçek DB kısıtlamaları Day 6'nın konusu.)
* `string Name { get; set; } = string.Empty;` — nullable reference types açıkken, non-nullable bir `string` property'sinin constructor'sız (parametresiz `new SkillFormModel()`) güvenle oluşturulabilmesi için varsayılan bir başlangıç değeri (`string.Empty`) veriyoruz — yoksa derleyici CS8618 uyarısı verirdi (Day 2'de gördüğümüz nullable uyarısının bir başka türü).

---

## 5. `src/RoadmapOS.Web/Controllers/SkillsController.cs` — yeni action'lar

```csharp
[HttpGet]
public IActionResult Create()
{
    return View(new SkillFormModel());
}

[HttpPost]
[ValidateAntiForgeryToken]
public IActionResult Create(SkillFormModel model)
{
    if (!ModelState.IsValid)
    {
        return View(model);
    }

    var skill = new Skill(model.Name, model.Category, model.CurrentLevel, model.TargetLevel)
    {
        Notes = model.Notes
    };

    _skillCatalog.Add(skill);

    return RedirectToAction(nameof(Index));
}
```

**Neden:** Aynı isimde (`Create`) iki metot var çünkü tarayıcı bu URL'e **iki farklı amaçla** istek atıyor: formu ilk açarken (GET — "bana boş formu göster") ve formu gönderirken (POST — "bu veriyi kaydet"). Bunları tek bir metotta karıştırmak yerine ayırmak, her birinin sorumluluğunu net tutuyor.

**Nasıl çalışıyor:**
* `[HttpGet]` / `[HttpPost]` — C#'ın kendisi aynı isimli iki metodu parametrelerine göre ayırt eder (overload), ama ASP.NET Core routing'in "hangi HTTP metoduyla gelen isteğin hangi action'a gideceğini" bilmesi için bu attribute'lar gerekiyor. Bu attribute'lar olmasaydı, ASP.NET Core her iki `Create` metodunu da GET **ve** POST için "aday" görür, hangisini seçeceğini bilemezdi (aslında parametre farkına göre bir ölçüde ayırt edebilir, ama HTTP metoduna göre **açıkça** belirtmek, niyetin okunabilir ve garantili olmasını sağlıyor).
* `[ValidateAntiForgeryToken]` — CSRF (Cross-Site Request Forgery) koruması. Formun `<form asp-action="...">` tag helper'ı, görünmez bir `__RequestVerificationToken` alanı otomatik ekliyor; bu attribute, POST isteğinde bu token'ın geçerli olup olmadığını kontrol ediyor. Token yoksa/yanlışsa istek reddediliyor — böylece başka bir siteden, senin oturumunu kötüye kullanarak sahte bir POST gönderilmesi engelleniyor.
* `if (!ModelState.IsValid) { return View(model); }` — `SkillFormModel model` parametresi model binder tarafından doldurulduktan **hemen sonra**, `[Required]`/`[StringLength]` gibi attribute'lar otomatik kontrol edilmiş ve sonuç `ModelState`'e yazılmış oluyor. Geçersizse, kullanıcının **girdiği veriyle birlikte** (`model`) aynı view'ı tekrar gösteriyoruz — böylece formu yeniden doldurması gerekmiyor, sadece hatalı alanı düzeltiyor.
* `new Skill(model.Name, ...) { Notes = model.Notes }` — DTO'dan (`SkillFormModel`) domain entity'ye (`Skill`) **elle** dönüşüm. Bu adım gerekli çünkü ikisi farklı sınıflar — form modelinin doğrudan veritabanına yazılması söz konusu değil.
* `RedirectToAction(nameof(Index))` — kayıt başarılıysa tarayıcıyı `/Skills`'e yönlendiriyor. `nameof(Index)`, `"Index"` string'ini elle yazmak yerine derleyicinin kontrol ettiği bir referans veriyor — `Index` metodunun adı değişirse, burası da derleme hatası verir (yazım hatasına karşı koruma).

`Edit(int id)` (GET) ve `Edit(int id, SkillFormModel model)` (POST) aynı mantıkla çalışıyor; tek fark, `Edit(POST)`'ta önce `id != model.Id` kontrolü var (URL'deki id ile formdaki gizli `Id` alanının **eşleştiğinden** emin olmak için) ve `_skillCatalog.GetById(id)` ile var olan kaydı bulup üzerine yazıyoruz, yeni bir `Skill` oluşturmuyoruz.

---

## 6. `Views/Skills/Create.cshtml` ve `Edit.cshtml`

```cshtml
@model SkillFormModel

<form asp-action="Create" method="post">
    <div asp-validation-summary="All" class="text-danger"></div>

    <div class="mb-3">
        <label asp-for="Name" class="form-label"></label>
        <input asp-for="Name" class="form-control" />
        <span asp-validation-for="Name" class="text-danger"></span>
    </div>
    ...
    <select asp-for="CurrentLevel" asp-items="Html.GetEnumSelectList<SkillLevel>()" class="form-select"></select>
    ...
</form>

@section Scripts {
    <partial name="_ValidationScriptsPartial" />
}
```

**Neden:** Kullanıcının veri girip göndereceği gerçek HTML form. `Create.cshtml` ve `Edit.cshtml` neredeyse birebir aynı — bunu **bilerek** iki ayrı dosyada tuttuk, ortak bir partial'a çıkarmadık; bu kod tekrarını Day 10'un (Refactoring) konusuna bilinçli olarak bıraktık.

**Nasıl çalışıyor:**
* `asp-for="Name"` — tag helper; hem `<input name="Name" id="Name" value="...">` gibi doğru HTML attribute'larını otomatik üretiyor hem de `SkillFormModel.Name`'e bağlı validation kurallarını (`data-val-required` gibi HTML data-attribute'ları) client-side JavaScript doğrulaması için gömüyor.
* `asp-validation-for="Name"` — sunucunun (`ModelState`'e yazdığı) veya client-side script'in bulduğu hata mesajını, o alanın **hemen altında** gösteren boş bir `<span>`.
* `asp-validation-summary="All"` — tüm hataları tek bir yerde (genelde formun üstünde) toplu listeleyen özet.
* `Html.GetEnumSelectList<SkillLevel>()` — `SkillLevel` enum'unun tüm üyelerini otomatik olarak bir `<select>` dropdown'ının seçeneklerine çeviren yardımcı metot — enum üyeleri değişirse (yeni bir seviye eklenirse), dropdown'ı elle güncellemene gerek kalmıyor.
* `<partial name="_ValidationScriptsPartial" />` — Day 1'de şablonun bize hazır verdiği, jQuery Validation kütüphanesini (zaten `wwwroot/lib` altında vendored) yükleyen partial view; bu sayede validation hataları sayfa hiç sunucuya gitmeden, **tarayıcıda anında** da gösterilebiliyor (client-side validation), sunucu tarafı kontrol (`ModelState.IsValid`) ise her koşulda ayrıca çalışıyor (JavaScript kapalı olsa bile güvenlik burada).

---

## Doğrulanan davranış

```
dotnet build                          → 0 Hata, 0 Uyarı

GET /Skills/Create                    → HTTP 200

POST /Skills/Create (Name boş)        → HTTP 200 (redirect YOK), "The Name field is required" hatası göründü

POST /Skills/Create (gecerli veri: "Docker")
                                       → HTTP 302 → /Skills
                                       → /Skills'te "Toplam 6 skill.", Docker listede

GET /Skills/Edit/6                    → HTTP 200, form "Docker" değeriyle onceden doldurulmus

POST /Skills/Edit/6 (Notes eklenerek) → HTTP 302 → /Skills

Uygulama kapatildiktan sonra dogrudan SQL sorgusu:
  6 | Docker | Tools | 0 | 1 | Phase 3'te islenecek
  → Edit'in gercekten kalici oldugu kanitlandi

(Test verisi olan "Docker" kaydi, tabloyu temiz birakmak icin sonradan silindi.)
```

Bu, hem `[Required]` validation'ının gerçekten sunucu tarafında (curl ile, hiç JavaScript çalışmadan) devreye girdiğini, hem de CSRF token doğrulamasının (`[ValidateAntiForgeryToken]`) doğru token olmadan isteği reddettiğini (token'ı forma her fetch'te yeniden çekmemiz gerekmesinden anlaşılıyor) kanıtlıyor.
