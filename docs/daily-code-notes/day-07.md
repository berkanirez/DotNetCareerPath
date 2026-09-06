# Day 7 — Kod Notları

Faz 1, Hafta 2, Gün 7. Konu: ilerleme hesaplama kuralları, domain servisi, ilk TDD workflow'u, xUnit.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer — bugün ayrıca gerçek bir Red → Green döngüsü yaşadık, bunu da adım adım göstereceğim.

---

## 1. Test projesinin oluşturulması

```
dotnet new xunit -n RoadmapOS.Web.Tests -o tests/RoadmapOS.Web.Tests
dotnet sln add tests/RoadmapOS.Web.Tests/RoadmapOS.Web.Tests.csproj
dotnet add reference ../../src/RoadmapOS.Web/RoadmapOS.Web.csproj
```

**Neden:** Test yazabilmemiz için önce test **kodunu barındıracak ayrı bir proje** gerekiyor — testler asla asıl uygulamayla aynı derlenmiş dosyaya (production DLL'ine) karışmamalı.

**Nasıl çalışıyor:**
* `dotnet new xunit` — xUnit test framework'üne hazır, `Microsoft.NET.Test.Sdk` ve `xunit` paket referanslarını içeren bir proje şablonu üretiyor. Bu, npm dünyasındaki `npm install --save-dev jest` + boş bir test klasörü kurmanın karşılığı.
* `dotnet sln add` — yeni projeyi solution'a (`RoadmapOS.slnx`) ekliyor, böylece `dotnet build`/`dotnet test` tüm solution'ı tek komutla işleyebiliyor.
* `dotnet add reference` — test projesinin, test edeceği gerçek kodu (`RoadmapOS.Web`) **görebilmesi** için ona bir proje referansı ekliyor; NuGet paketi değil, aynı solution içindeki başka bir `.csproj`'a referans.

---

## 2. `src/RoadmapOS.Web/Domain/ProgressCalculator.cs` — bilerek eksik (Red) hâli

```csharp
namespace RoadmapOS.Web.Domain;

public class ProgressCalculator
{
    public double CalculateOverallProgress(IReadOnlyList<Skill> skills)
    {
        throw new NotImplementedException();
    }
}
```

**Neden:** TDD'nin ilk adımı — implementasyonu yazmadan **önce**, metodun imzasını (adı, parametresi, dönüş tipi) belirleyip, gövdesini bilerek boş/hatalı bırakıyoruz. Bu, "önce ne yapmak istediğimizi netleştir, sonra nasıl yapacağını düşün" disiplinini zorluyor.

**Nasıl çalışıyor:** `throw new NotImplementedException();` — .NET'in "bu henüz yazılmadı" demenin standart yolu. Derlenir (metot imzası tam), ama çağrıldığı an patlar — bu bilerek böyle, testlerin **gerçekten çalışıp çalışmadığını** (yanlışlıkla her zaman yeşil çıkan, sahte bir test yazmadığımızı) kanıtlamak için.

**Dikkat — interface yok:** `ISkillCatalog`'un aksine, bu sınıfa bilerek bir interface eklemedik. `ISkillCatalog`'a interface eklememizin somut sebebi vardı: iki farklı implementasyon (in-memory/EF Core) arasında geçiş yapabilmek. `ProgressCalculator`'ın bugün **hiçbir alternatif implementasyonu yok** ve onu doğrudan, kendisini çağırarak test ediyoruz — mock'lamamıza gerek yok. İhtiyaç olmadan interface eklemek gereksiz bir soyutlama (`CLAUDE.md`'nin yasakladığı "speculative abstraction") olurdu.

---

## 3. `tests/RoadmapOS.Web.Tests/ProgressCalculatorTests.cs` — ilk iki test (Red aşaması)

```csharp
using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Tests;

public class ProgressCalculatorTests
{
    private readonly ProgressCalculator _calculator = new();

    [Fact]
    public void CalculateOverallProgress_EmptyList_ReturnsZero()
    {
        var skills = new List<Skill>();

        var result = _calculator.CalculateOverallProgress(skills);

        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateOverallProgress_SingleSkillAtTarget_ReturnsHundred()
    {
        var skills = new List<Skill>
        {
            new("C#", "Language", SkillLevel.CanImplementIndependently, SkillLevel.CanImplementIndependently)
        };

        var result = _calculator.CalculateOverallProgress(skills);

        Assert.Equal(100, result);
    }
}
```

**Neden:** İki basit, net senaryo: hiç skill yoksa ilerleme "0" olmalı (sıfıra bölme hatası **fırlatmamalı**); tek bir skill tam hedefteyse ilerleme "100" olmalı.

**Nasıl çalışıyor — Arrange-Act-Assert (AAA) yapısı:**
* **Arrange** (`var skills = ...`) — test verisini hazırla.
* **Act** (`var result = _calculator.CalculateOverallProgress(skills);`) — asıl test edilen şeyi çağır.
* **Assert** (`Assert.Equal(...)`) — beklenen sonuçla gerçek sonucu karşılaştır; eşleşmezse test kırmızıya düşer.
* `[Fact]` — "parametresiz, tek senaryolu bir test" demek; xUnit test runner'ı bu attribute'a sahip her metodu otomatik bulup çalıştırıyor (Jest'teki `test('...', () => {...})` çağrısına benzer, ama attribute-tabanlı).
* `private readonly ProgressCalculator _calculator = new();` — her test metodundan önce xUnit **yeni bir `ProgressCalculatorTests` nesnesi** oluşturuyor (bu, testlerin birbirinden etkilenmemesini garanti ediyor) — bu yüzden `_calculator` her testte taze bir örnek.

**Bu noktada `dotnet test` çalıştırıldı ve her ikisi de `NotImplementedException` ile kırmızıya düştü** — TDD'nin "Red" adımı, gerçekten yaşandı, yazıya dökülmedi.

---

## 4. `ProgressCalculator` — gerçek implementasyon (Green aşaması)

```csharp
public double CalculateOverallProgress(IReadOnlyList<Skill> skills)
{
    if (skills.Count == 0)
    {
        return 0;
    }

    var currentSum = 0;
    var targetSum = 0;

    foreach (var skill in skills)
    {
        currentSum += (int)skill.CurrentLevel;
        targetSum += (int)skill.TargetLevel;
    }

    if (targetSum == 0)
    {
        return 0;
    }

    var rawPercentage = (double)currentSum / targetSum * 100;

    return Math.Min(100, rawPercentage);
}
```

**Neden:** İş kuralı şu: tüm skill'lerin `CurrentLevel` toplamını, tüm `TargetLevel` toplamına oranlayıp yüzdeye çeviriyoruz — tek bir skill'e değil, **bütüne** bakan bir "genel ilerleme" ölçüsü. İki uç durum bilerek ayrıca ele alınıyor: boş liste (anlamlı bir "ilerleme" kavramı yok, sıfır dön) ve tüm hedeflerin toplamı sıfırsa (kimse için bir hedef belirlenmemiş — sıfıra bölme hatasını önle).

**Nasıl çalışıyor:**
* `(int)skill.CurrentLevel` — enum'u (Day 2'den beri bildiğimiz gibi) alttaki `int` değerine çeviriyor (`explicit cast`), toplanabilir hâle getiriyor.
* `if (targetSum == 0) return 0;` — bölme işleminden **önce** kontrol ederek `DivideByZeroException` (ya da double'larda daha sinsi bir `NaN`/`Infinity` sonucu) almayı engelliyor.
* `Math.Min(100, rawPercentage)` — bir skill'in `CurrentLevel`'i `TargetLevel`'inden yüksek olabilir (hedefini fazlasıyla aşmış olabilirsin), bu durumda ham oran %100'ü geçebilir. İş kuralı: "genel ilerleme" hiçbir zaman %100'ü geçmesin diye burada **bilinçli olarak sınırlıyoruz** (clamp).

**Bu noktada `dotnet test` tekrar çalıştırıldı: 2/2 test yeşile döndü** — TDD'nin "Green" adımı.

---

## 5. Ek uç durum testleri (her biri kendi Red→Green döngüsüyle eklendi)

```csharp
[Fact]
public void CalculateOverallProgress_MultipleSkillsPartialProgress_ReturnsWeightedPercentage()
{
    var skills = new List<Skill>
    {
        new("C#", "Language", SkillLevel.CanImplementWithGuidance, SkillLevel.CanExplainProduction),
        new("ASP.NET Core", "Framework", SkillLevel.CanImplementIndependently, SkillLevel.CanImplementIndependently)
    };

    var result = _calculator.CalculateOverallProgress(skills);

    Assert.Equal(71.43, Math.Round(result, 2));
}
```
**Neden/Nasıl:** Birden fazla skill'in **toplamını** doğru hesaplıyor muyuz, tek tek ortalamasını değil mi diye test ediyor. Hesap: C# (2/4) + ASP.NET Core (3/3) → toplam `(2+3)/(4+3) = 5/7 ≈ %71.43`. `Math.Round(result, 2)` — `double` karşılaştırmalarında ondalık basamak taşması (floating point precision) yüzünden testlerin kırılgan olmaması için sonucu 2 basamağa yuvarlayarak karşılaştırıyoruz.

```csharp
[Fact]
public void CalculateOverallProgress_SkillExceedsTarget_ClampsAtHundred()
{
    var skills = new List<Skill>
    {
        new("C#", "Language", SkillLevel.CanExplainProduction, SkillLevel.CanExplainPurpose)
    };

    var result = _calculator.CalculateOverallProgress(skills);

    Assert.Equal(100, result);
}
```
**Neden/Nasıl:** `CurrentLevel(4)/TargetLevel(1)` → ham oran %400 — `Math.Min(100, ...)` satırının **gerçekten** işe yaradığını kanıtlıyor. Bu satırı çıkarsaydık bu test kırmızıya düşerdi.

```csharp
[Fact]
public void CalculateOverallProgress_AllTargetsAreNotStudied_ReturnsZeroWithoutDivideByZero()
{
    var skills = new List<Skill>
    {
        new("C#", "Language", SkillLevel.NotStudied, SkillLevel.NotStudied),
        new("ASP.NET Core", "Framework", SkillLevel.NotStudied, SkillLevel.NotStudied)
    };

    var result = _calculator.CalculateOverallProgress(skills);

    Assert.Equal(0, result);
}
```
**Neden/Nasıl:** İlginç bir TDD anı — bu test **yazılır yazılmaz ilk denemede geçti** (kırmızıya hiç düşmedi), çünkü `targetSum == 0` kontrolü zaten önceki adımda yazılmıştı. Bu da TDD'nin bir başka faydasını gösteriyor: bazen yeni bir senaryo için test yazarsın ve mevcut implementasyonun **zaten doğru** olduğunu, sadece henüz kanıtlanmamış olduğunu keşfedersin.

---

## Doğrulanan davranış

```
dotnet test → Toplam: 5, Başarılı: 5, Başarısız: 0, Süre: 21 ms
dotnet build (tüm solution) → 0 Hata, 0 Uyarı
```

21 milisaniye — bunun önemi: hiçbir web sunucusu başlatılmadı, hiçbir SQL Server bağlantısı açılmadı. Şu ana kadarki tüm doğrulamalarımız (curl, SQL sorguları) saniyeler sürüyordu; bu testler ise **anlık**. Bu, "neden unit test yazıyoruz" sorusunun en somut cevabı: hızlı, otomatik, tekrar tekrar çalıştırılabilir güvence.
