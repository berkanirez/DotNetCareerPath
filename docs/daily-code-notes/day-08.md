# Day 8 — Kod Notları

Faz 1, Hafta 2, Gün 8. Konu: LINQ, dashboard sorguları, genel ve kategori bazlı ilerleme.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `src/RoadmapOS.Web/Domain/CategoryProgress.cs`

```csharp
namespace RoadmapOS.Web.Domain;

public record CategoryProgress(string Category, double Percentage);
```

**Neden:** Kategori bazlı ilerlemeyi temsil eden, "bu kategori, şu yüzdede" gibi basit bir değer çifti. Day 2'deki `SkillSnapshot` gibi record — kimliği olmayan, sadece bir andaki hesaplanmış değeri taşıyan bir sonuç.

---

## 2. `ProgressCalculator.CalculateCategoryProgress` — Red aşaması

```csharp
public IReadOnlyList<CategoryProgress> CalculateCategoryProgress(IReadOnlyList<Skill> skills)
{
    throw new NotImplementedException();
}
```

Day 7'deki gibi, önce imzayı yazıp gövdeyi bilerek boş bıraktık, testleri yazıp **gerçekten kırmızıya düştüklerini** gördük (2 test, `NotImplementedException` ile).

## 3. `tests/RoadmapOS.Web.Tests/ProgressCalculatorTests.cs` — yeni testler

```csharp
[Fact]
public void CalculateCategoryProgress_EmptyList_ReturnsEmptyResult()
{
    var skills = new List<Skill>();
    var result = _calculator.CalculateCategoryProgress(skills);
    Assert.Empty(result);
}

[Fact]
public void CalculateCategoryProgress_TwoCategories_ReturnsOnePercentagePerCategory()
{
    var skills = new List<Skill>
    {
        new("C#", "Language", SkillLevel.CanImplementWithGuidance, SkillLevel.CanExplainProduction),
        new("ASP.NET Core", "Framework", SkillLevel.CanExplainPurpose, SkillLevel.CanImplementIndependently),
        new("EF Core", "Framework", SkillLevel.NotStudied, SkillLevel.CanImplementIndependently)
    };

    var result = _calculator.CalculateCategoryProgress(skills);

    Assert.Equal(2, result.Count);

    var language = result.Single(c => c.Category == "Language");
    Assert.Equal(50, language.Percentage);

    var framework = result.Single(c => c.Category == "Framework");
    Assert.Equal(16.67, Math.Round(framework.Percentage, 2));
}
```

**Neden/Nasıl:** İkinci test, iki farklı kategoriye ait 3 skill veriyor — beklenti: sonuçta **tam olarak 2** kategori olmalı (`Language`, `Framework`), her biri kendi yüzdesiyle. `Assert.Empty(result)` — xUnit'in hazır bir başka assertion'ı, koleksiyonun boş olduğunu kontrol ediyor. `result.Single(c => c.Category == "Language")` — bu bir LINQ metodu (`Single`): koşula uyan **tam olarak bir** eleman bulmasını bekliyor, yoksa (0 ya da 2+ bulursa) exception fırlatıyor — "bu listede kesinlikle bir tane olmalı" garantisi.

## 4. Gerçek implementasyon (Green aşaması)

```csharp
public IReadOnlyList<CategoryProgress> CalculateCategoryProgress(IReadOnlyList<Skill> skills)
{
    return skills
        .GroupBy(skill => skill.Category)
        .Select(group => new CategoryProgress(group.Key, CalculateOverallProgress(group.ToList())))
        .ToList();
}
```

**Neden:** İş kuralı: skill'leri kategoriye göre grupla, her grup için **zaten var olan** `CalculateOverallProgress` mantığını o gruba özel uygula. Sıfırdan yeni bir hesaplama yazmadık — aynı formülü daha küçük bir veri kümesine (bir kategorinin skill'lerine) tekrar uyguladık.

**Nasıl çalışıyor (LINQ zinciri, soldan sağa okunur):**
* `.GroupBy(skill => skill.Category)` — listeyi `Category` değerine göre gruplara ayırıyor. Sonuç, her biri bir **grup** olan bir koleksiyon; her grubun bir `Key` (grup anahtarı — burada kategori adı) ve kendisi de bir koleksiyon olan (`IEnumerable<Skill>`) içeriği var.
* `.Select(group => new CategoryProgress(group.Key, CalculateOverallProgress(group.ToList())))` — her grubu, **başka bir şeye dönüştürüyor** (JS'teki `.map()`): `group.Key` (kategori adı) ve `CalculateOverallProgress(group.ToList())` (o gruptaki skill'lerin ilerlemesi) ile yeni bir `CategoryProgress` üretiyor. `group.ToList()` — grup, LINQ'un "henüz çalıştırılmamış bir sorgu tanımı" (`IEnumerable`) olduğu için, `CalculateOverallProgress`'in beklediği somut `IReadOnlyList<Skill>` tipine çeviriyoruz.
* `.ToList()` (en sonda) — tüm zincirin sonucunu, tembel bir sorgu tanımı olarak değil, **gerçekten hesaplanmış bir liste** olarak materyalize ediyor.

**Test sonucu:** 8/8 test geçti.

---

## 5. `Program.cs` — `ProgressCalculator`'ın ilk kez DI'a kaydedilmesi

```csharp
builder.Services.AddSingleton<ProgressCalculator>();
```

**Neden:** Day 7'de bu servisin **hiç gerçek kullanıcısı (consumer)** yoktu — testler onu doğrudan `new ProgressCalculator()` ile oluşturup çağırıyordu, DI'a ihtiyaç yoktu. Bugün ilk kez gerçek bir controller (`DashboardController`) buna ihtiyaç duyuyor — DI'a kaydetmenin **gerekçesi bugün ortaya çıktı**, önceden değil.

**Nasıl çalışıyor:** İnterface yok, doğrudan somut sınıf (`ProgressCalculator`) kaydediliyor — Day 3'te konuştuğumuz gibi, DI interface şart koşmuyor. `AddSingleton` seçildi çünkü bu servis **tamamen stateless** (hiçbir alanı/durumu yok, sadece hesaplama yapıyor) — paylaşılmasının hiçbir riski yok, her istekte yeniden oluşturmaya gerek yok.

---

## 6. `src/RoadmapOS.Web/Models/DashboardViewModel.cs`

```csharp
using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Models;

public record DashboardViewModel(double OverallProgress, IReadOnlyList<CategoryProgress> CategoryProgress);
```

**Neden:** View'ın ihtiyacı olan iki şeyi (genel ilerleme + kategori listesi) tek bir pakette taşıyan görüntüleme modeli. Day 5'teki `SkillFormModel`'den farkı: bu **sadece okuma (read-only)** için, hiçbir validation attribute'u yok — over-posting riski yok çünkü kullanıcıdan hiçbir veri almıyoruz, sadece gösteriyoruz.

---

## 7. `src/RoadmapOS.Web/Controllers/DashboardController.cs`

```csharp
public class DashboardController : Controller
{
    private readonly ISkillCatalog _skillCatalog;
    private readonly ProgressCalculator _progressCalculator;

    public DashboardController(ISkillCatalog skillCatalog, ProgressCalculator progressCalculator)
    {
        _skillCatalog = skillCatalog;
        _progressCalculator = progressCalculator;
    }

    public IActionResult Index()
    {
        var skills = _skillCatalog.GetAll();
        var overallProgress = _progressCalculator.CalculateOverallProgress(skills);
        var categoryProgress = _progressCalculator.CalculateCategoryProgress(skills);

        var viewModel = new DashboardViewModel(overallProgress, categoryProgress);

        return View(viewModel);
    }
}
```

**Neden/Nasıl:** İki bağımlılık birden constructor injection ile geliyor — DI container'ın **birden fazla** servisi aynı anda enjekte edebildiğinin ilk örneği bizim kodumuzda. `Index()`, verileri çekip iki hesaplamayı çağırıyor, sonuçları `DashboardViewModel`'e paketleyip view'a gönderiyor — controller'ın kendisi hiçbir hesaplama **yapmıyor**, sadece orkestra ediyor (domain servisine devrediyor).

---

## 8. `Views/Dashboard/Index.cshtml` — ve gerçek bir kültür (culture) hatası

İlk yazdığımız hâliyle:
```cshtml
<div class="progress-bar" style="width: @Model.OverallProgress%;">
```

**Ne oldu:** Uygulamayı çalıştırıp gerçek çıktıyı kontrol ettiğimizde, üretilen HTML'de `style="width: 16,666666666666664%;"` gördük — **virgülle**. Sunucunun işletim sistemi kültürü (culture) Türkçe (`tr-TR`) olduğu için, `double.ToString()` varsayılan olarak ondalık ayıracı olarak **virgül** kullanıyor. Ama CSS, `width` değeri için **sadece nokta** kabul ediyor — tarayıcı `16,66...%` gibi geçersiz bir CSS değerini sessizce yok sayıyor, progress bar görsel olarak bozuk/eksik görünürdü.

**Düzeltme:**
```cshtml
@using System.Globalization
@{
    string FormatPercentage(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
...
style="width: @(FormatPercentage(Model.OverallProgress))%;"
```

**Nasıl çalışıyor:** `CultureInfo.InvariantCulture`, ".NET'in hiçbir spesifik ülke/dil ayarına bağlı olmayan, sabit" kültürü — ondalık ayıracı her zaman nokta. CSS gibi, kültüre duyarlı olmaması gereken çıktılar için (sayısal veri formatları, dosya formatları, API'ler) bu her zaman doğru tercih; kullanıcıya gösterilecek metin için kültüre duyarlı formatlamak (`tr-TR` gibi) uygun olabilir, ama bir CSS değeri asla "kullanıcı arayüzü metni" değil. `@{ string FormatPercentage(...) => ...; }` — Razor içinde küçük, yerel bir yardımcı fonksiyon tanımlama şekli (C#'ın "local function" özelliği), tekrar eden `ToString(...)` çağrısını tek yerden yönetmek için.

Bu, gerçek bir production hatasının canlı örneği: kod **doğru** çalışıyordu (sayı doğru hesaplanmıştı), ama **görüntüleme** katmanında, sunucunun işletim sistemi ayarına bağlı, ortama özgü bir hata vardı — sadece "çalıştır ve elle bak" ile yakalanabilecek türden bir sorun.

---

## 9. `Views/Shared/_Layout.cshtml` — navigasyon

`Dashboard` ve (daha önce hiç eklenmemiş olan) `Skills` linkleri navbar'a eklendi — artık her iki sayfa da menüden erişilebilir.

---

## Doğrulanan davranış

```
dotnet test  → 8/8 (2 yeni CalculateCategoryProgress testi dahil)
dotnet build → 0 Hata, 0 Uyarı

Mevcut Skills verisi:
  C# (Language): 2/4    ASP.NET Core (Framework): 1/3    EF Core (Framework): 0/3
  SQL Server (Database): 0/2    Git (Tools): 3/3

Elle hesaplanan beklenti:
  Genel: (2+1+0+0+3)/(4+3+3+2+3) = 6/15 = %40
  Language: 2/4 = %50 | Framework: 1/6 ≈ %16.7 | Database: 0/2 = %0 | Tools: 3/3 = %100

GET /Dashboard → HTTP 200, gerçek çıktı:
  width: 40.0%   (Genel)
  width: 50.0%   (Language)
  width: 16.7%   (Framework)
  width: 0.0%    (Database)
  width: 100.0%  (Tools)
  → elle hesapladığımızla birebir eşleşti, ayrıca geçerli (nokta ondalıklı) CSS üretildiği doğrulandı

GET /, GET /Skills → HTTP 200 (regresyon yok)
```
