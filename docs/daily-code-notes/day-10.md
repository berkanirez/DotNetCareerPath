# Day 10 — Kod Notları

Faz 1, Hafta 2, Gün 10 (RoadmapOS'un son günü). Konu: refactoring, build/test doğrulaması, İngilizce README, demo, RoadmapOS V1 release.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `src/RoadmapOS.Web/Views/Skills/_SkillForm.cshtml` — refactor

```cshtml
@model SkillFormModel

<div asp-validation-summary="All" class="text-danger"></div>

<div class="mb-3">
    <label asp-for="Name" class="form-label"></label>
    <input asp-for="Name" class="form-control" />
    <span asp-validation-for="Name" class="text-danger"></span>
</div>
@* ... Category, CurrentLevel, TargetLevel, Notes aynı desende ... *@

<button type="submit" class="btn btn-primary">Save</button>
<a asp-action="Index" class="btn btn-secondary">Cancel</a>
```

**Neden:** Day 5'ten beri `Create.cshtml` ve `Edit.cshtml`, form alanları açısından **neredeyse birebir aynıydı** — bilerek böyle bırakmıştık, "bugün (Day 10) refactor edeceğiz" diye not düşmüştük. 9 gün boyunca bu kopya kod hiç sorun çıkarmadı, ama artık gerçek bir bakım riski: biri değişip diğeri unutulabilirdi.

**Nasıl çalışıyor:** `_SkillForm.cshtml`, kendi `<form>` etiketini **içermiyor** — sadece formun **içeriğini** (alanlar + validation mesajları + butonlar) taşıyor. `<form>` etiketinin kendisi `Create.cshtml`/`Edit.cshtml`'de kalmaya devam ediyor, çünkü ikisinin `<form>` açılışı **farklı**: `Create` sadece `asp-action="Create"`, `Edit` ise ayrıca `asp-route-id` ve gizli bir `Id` alanı taşıyor. Yani "ortak olan" ile "farklı olan" net bir sınırla ayrıldı.

## 2. `Create.cshtml` / `Edit.cshtml` — sadeleşme

```cshtml
@* Create.cshtml *@
<form asp-action="Create" method="post">
    <partial name="_SkillForm" />
</form>

@* Edit.cshtml *@
<form asp-action="Edit" asp-route-id="@Model.Id" method="post">
    <input type="hidden" asp-for="Id" />
    <partial name="_SkillForm" />
</form>
```

**Nasıl çalışıyor:** `<partial name="_SkillForm" />`, Day 1'den beri kullandığımız `_ValidationScriptsPartial`'la aynı mekanizma — Razor'a "bu dosyanın içeriğini burada render et" diyor, `Model`'i (o an elindeki `SkillFormModel`) otomatik olarak partial'a da geçiriyor.

**Davranış değişmedi mi? Canlı doğrulandı:**
* `GET /Skills/Edit/1` → form hâlâ `value="C#"` ile doğru dolduruluyor.
* Geçersiz veri ile `POST /Skills/Create` → hâlâ HTTP 200 (yönlendirme yok), "The Name field is required" hatası aynen görünüyor.
* Refactor **davranışı değil, sadece yapıyı** değiştirdi — tam da refactoring'in tanımı.

---

## 3. Temiz build doğrulaması

```
rm -rf src/RoadmapOS.Web/bin src/RoadmapOS.Web/obj tests/RoadmapOS.Web.Tests/bin tests/RoadmapOS.Web.Tests/obj
dotnet build → 0 Hata, 0 Uyarı
dotnet test  → 8/8 Başarılı
```

**Neden:** `bin`/`obj` klasörleri, önceki derlemelerden kalma dosyalar taşıyor — bunları silip sıfırdan derlemek, "bu repo'yu yeni klonlayan biri (veya CI sunucusu) gerçekten sıfırdan çalıştırabilir mi" sorusuna gerçek bir cevap veriyor. Sadece "üzerimde çalışıyor" değil, "temiz bir ortamda da çalışıyor" kanıtı.

## 4. Uçtan uca demo (canlı doğrulama)

```
GET /              → 200
GET /Home/Privacy  → 200
GET /Skills        → 200
GET /Skills/Create → 200
GET /Skills/Edit/1 → 200
GET /Dashboard     → 200
```

Tüm ana sayfalar tek tek gezildi — RoadmapOS'un 10 günlük tüm parçalarının (MVC, EF Core, LINQ, validation, logging) **birlikte** çalıştığının son kanıtı.

## 5. `README.md` güncellemesi

* `Current status` bölümü, Day 0'dan kalma donmuş haliyle **9 gündür güncellenmemişti** — gerçek duruma (Phase 1, Day 10, V1 released) çekildi.
* Yeni bir "RoadmapOS (Phase 1 project)" bölümü eklendi: önkoşullar, `dotnet restore` / `dotnet ef database update` / `dotnet run` adımları, hangi sayfaların ne işe yaradığı, ve **dürüstçe** bugüne kadarki basitleştirmelerin listesi ("Known simplifications").

**Neden "Known simplifications" bölümü:** Bir README, sadece "her şey harika çalışıyor" demek değil — projeyi devralacak (ya da bir iş görüşmesinde inceleyecek) birinin, neyin bilinçli bir sınırlama olduğunu, neyin eksik olduğunu görebilmesi gerekiyor. Bu, `CLAUDE.md`'nin "demo simplifications ile production requirements'ı ayrı açıkla" prensibinin, artık koddan dokümana taşınmış hâli.

---

## Phase 1 completion gate — tek tek kontrol

`docs/ROADMAP.md`'deki 7 maddelik liste:

| Kriter | Durum | Kanıt |
|---|---|---|
| Application builds and runs | ✅ | Bugün: temiz `bin`/`obj` silinip sıfırdan build + run |
| Data persists in SQL Server | ✅ | Day 4'ten beri, en son Day 9'da bağımsız SQL sorgusuyla doğrulandı |
| Skills and roadmap items can be managed | ⚠️ Kısmi | Skills: tam CRUD (Create/Edit) var. RoadmapPhase/Project/Milestone: ilişkiler ve kısıtlamalar gerçek ama **yönetim UI'ı yok**, sadece seed/SSMS üzerinden — roadmap Day 6-9 planı bunu hiç istemedi, bilinçli bir kapsam sınırı |
| Progress calculation is tested | ✅ | Day 7-8, xUnit, 8/8 test |
| MVC request flow can be explained | ❓ Berkan'ın kendisi doğrulamalı | Bugünün anlama sorularında soruluyor |
| Dependency injection and EF Core can be explained | ❓ Berkan'ın kendisi doğrulamalı | Bugünün anlama sorularında soruluyor |
| Repository contains an English README | ✅ | Bugün güncellendi |

Son iki madde, bir dosyaya bakarak değil, **Berkan'ın kendi cümleleriyle** kanıtlanması gereken maddeler — bu yüzden bugünkü anlama soruları, her zamankinden farklı olarak, gerçekten V1'in kapanıp kapanmayacağına etki ediyor.
