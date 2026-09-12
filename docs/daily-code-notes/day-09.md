# Day 9 — Kod Notları

Faz 1, Hafta 2, Gün 9. Konu: requirement mapping, evidence kayıtları, logging, seed data, temel hata yönetimi.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `src/RoadmapOS.Web/Data/DbSeeder.cs` — seed mantığının taşınması

```csharp
// Runtime, idempotent seeder — chosen over EF Core's migration-based HasData()
// because real data (added/edited through the app itself) already exists in
// this database. HasData bakes fixed IDs into a migration, which risks
// colliding with organically-created rows; this approach only inserts when
// a table is still empty.
public static class DbSeeder
{
    public static void Seed(RoadmapOSDbContext context)
    {
        SeedSkills(context);
        SeedRoadmap(context);
        SeedEvidence(context);
    }
    // ... SeedSkills, SeedRoadmap, SeedEvidence
}
```

**Neden:** `Program.cs` içinde iki iç içe "if tablo boşsa ekle" bloğu vardı — okunması zor, `Program.cs`'in asıl işiyle (uygulamayı kurmak) karışmıştı. Bugün bunu **ayrı, adı kendini açıklayan bir sınıfa** taşıdık.

**Neden `HasData` değil, hâlâ runtime seeder:** EF Core'un asıl "resmi" seed mekanizması `OnModelCreating` içinde `HasData(...)` — bu, migration'a **sabit ID'lerle gömülü** veri ekliyor. Bizim durumumuzda sorun şu: sen zaten Create/Edit formundan gerçek veri ekleyip düzenledin (Git, xUnit, SQL Server'ın değerleri) — veritabanı artık "bakir" değil, **organik olarak büyümüş gerçek veri** içeriyor. `HasData` sabit ID'lerle migration uygulamaya çalışsaydı, senin gerçek verinle **çakışma riski** olurdu (aynı ID'ye iki farklı satır atama girişimi gibi). Bu yüzden bugün "ders kitabı ideali" yerine, **elimizdeki gerçek durumu bozmayan** pratik kararı seçtik — bu da gerçek bir mühendislik kararı: hangi araç, hangi senaryoda doğru.

**Nasıl çalışıyor — `SeedEvidence` metodundaki önemli detay:**
```csharp
var csharpSkill = context.Skills.FirstOrDefault(s => s.Name == "C#");
if (csharpSkill is not null)
{
    context.EvidenceRecords.Add(new Evidence(
        "Skill.cs, SkillLevel.cs, SkillSnapshot.cs implemented and verified (Day 2)",
        new DateOnly(2026, 9, 1),
        csharpSkill.Id));
}
```
Evidence'ı **sabit bir ID** (örn. `1`) ile değil, `FirstOrDefault(s => s.Name == "C#")` ile **ismine göre bulup** o skill'in **gerçek, ne olursa olsun** ID'sini kullanarak bağlıyoruz. Bu, veritabanının geçmişte ne kadar "organik" değiştiğinden bağımsız çalışır — tıpkı `HasData`'dan kaçınma sebebimiz gibi, burada da sabit ID varsayımından kaçınıyoruz.

---

## 2. `src/RoadmapOS.Web/Domain/Evidence.cs`

```csharp
public class Evidence
{
    public int Id { get; set; }
    public string Description { get; set; }
    public DateOnly RecordedOn { get; set; }
    public int SkillId { get; set; }
    public Skill? Skill { get; set; }

    public Evidence(string description, DateOnly recordedOn, int skillId)
    {
        Description = description;
        RecordedOn = recordedOn;
        SkillId = skillId;
    }
}
```

**Neden:** `REQUIREMENTS_MATRIX.md`'de elle tuttuğumuz "Level 3/4 kanıt gerektirir" kuralının, uygulamanın kendisindeki karşılığı. Day 6'daki `Milestone`/`Project` ile **birebir aynı tasarım** — yeni bir kavram yok, sadece aynı deseni (entity + FK + navigation) yeni bir ilişkiye uyguluyoruz.

## 3. `Skill.cs` — `EvidenceRecords` navigation property

```csharp
public List<Evidence> EvidenceRecords { get; set; } = new();
```

**Neden/Nasıl:** Day 6'daki `RoadmapPhase.Projects` ile aynı fikir — "bir skill'in birden fazla kanıtı olabilir" (one-to-many, koleksiyon navigasyonu).

## 4. `RoadmapOSDbContext.cs` — `DbSet` ve ilişki tanımı

```csharp
public DbSet<Evidence> EvidenceRecords => Set<Evidence>();

modelBuilder.Entity<Evidence>(entity =>
{
    entity.Property(e => e.Description).IsRequired().HasMaxLength(300);

    entity.HasOne(e => e.Skill)
        .WithMany(s => s.EvidenceRecords)
        .HasForeignKey(e => e.SkillId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

Day 6'daki `Project`/`Milestone` Fluent API konfigürasyonuyla birebir aynı kalıp — yeni terim yok, pekiştirme.

## 5. Migration ve uygulama

```
dotnet ef migrations add AddEvidence
dotnet ef database update
```

`EvidenceRecords` tablosu, `FK_EvidenceRecords_Skills_SkillId` foreign key'i (cascade ile) ve otomatik index gerçek SQL Server'da oluşturuldu.

---

## 6. `EfSkillCatalog.GetAll()` — `.Include()` ile eager loading

```csharp
public IReadOnlyList<Skill> GetAll()
{
    var skills = _context.Skills.Include(s => s.EvidenceRecords).ToList();
    skills.Sort();
    return skills;
}
```

**Neden:** `.Include()` olmadan, `_context.Skills.ToList()` her skill'i çekerdi ama `skill.EvidenceRecords` **hep boş** (`new()`'daki varsayılan boş liste) kalırdı — EF Core, ilişkili verileri **kendiliğinden, sormadan** getirmiyor. Bunu canlı doğrulamak için konsoldaki gerçek SQL'e bak:

```sql
SELECT [s].[Id], [s].[Category], ..., [e].[Id], [e].[Description], [e].[RecordedOn], [e].[SkillId]
FROM [Skills] AS [s]
LEFT JOIN [EvidenceRecords] AS [e] ON [s].[Id] = [e].[SkillId]
ORDER BY [s].[Id]
```

`.Include(s => s.EvidenceRecords)` yazdığımız için EF Core, tek sorguda bir **`LEFT JOIN`** üretti — hem skill'leri hem onların evidence'larını **tek seferde** çekti. Bu satırı silseydik, üretilen SQL'de bu `LEFT JOIN` hiç olmazdı, `EvidenceRecords` her zaman boş listeye düşerdi.

---

## 7. `Views/Skills/Index.cshtml` — Evidence sütunu

```cshtml
<td>@skill.EvidenceRecords.Count</td>
```

Basit — `.Include()` sayesinde artık gerçekten dolu olan `EvidenceRecords` listesinin **eleman sayısını** gösteriyoruz.

---

## 8. `SkillsController.cs` — `ILogger` ve loglama

```csharp
private readonly ILogger<SkillsController> _logger;

public SkillsController(ISkillCatalog skillCatalog, ILogger<SkillsController> logger)
{
    _skillCatalog = skillCatalog;
    _logger = logger;
}
```

**Neden/Nasıl:** `ILogger<T>`, ASP.NET Core'un **hiçbir ek kayıt gerektirmeden** DI ile enjekte edilebilen, hazır bir loglama servisi — `AddControllersWithViews()` çağrısı bunu zaten otomatik kuruyor. `<SkillsController>` jenerik parametresi, log satırlarının **hangi sınıftan geldiğini** otomatik olarak etikete ekliyor (konsol çıktısında `RoadmapOS.Web.Controllers.SkillsController[0]` şeklinde gördük).

```csharp
_logger.LogInformation("Skill {SkillId} ({SkillName}) created.", skill.Id, skill.Name);
```
**`LogInformation`** — normal, beklenen bir olay ("bir şey başarıyla oldu"). `{SkillId}`/`{SkillName}` — string interpolation (`$"..."`) **değil**, "structured logging" adı verilen bir syntax: değerler ayrı parametre olarak geçiliyor, bu sayede log toplama araçları (ileride Phase 3'te göreceğimiz) bu değerleri **filtrelenebilir alanlar** olarak saklayabiliyor (sadece düz metin değil).

```csharp
_logger.LogWarning("Skill {SkillId} not found for edit.", id);
```
**`LogWarning`** — beklenmedik ama uygulamayı çökertmeyen bir durum ("biri olmayan bir kaydı düzenlemeye çalıştı"). `LogInformation` "her şey normal, bilgi amaçlı" derken, `LogWarning` "bu dikkat çekici, ama hata değil" diyor — production'da bu ikisi genelde farklı filtrelerle izlenir (Warning'ler alarm kurulabilecek şeyler, Information sadece kayıt).

---

## 9. Mevcut hata yönetiminin canlı doğrulanması

`SkillsController.Edit(int id)` Day 5'ten beri `if (skill is null) return NotFound();` içeriyordu ama **hiç canlı test etmemiştik**. Bugün doğrudan `curl` ile denedik:

```
GET /Skills/Edit/9999 → HTTP 404
Konsol: warn: RoadmapOS.Web.Controllers.SkillsController[0]
              Skill 9999 not found for edit.
```

Ayrıca `Program.cs`'te Day 1'den beri duran, hiç açıklamadığımız şu satırları bugün gözden geçirdik:
```csharp
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
```
**Ne işe yarıyor:** `Development` **dışında** (yani production'da) beklenmeyen bir exception fırlarsa, ASP.NET Core kullanıcıya ham bir stack trace göstermek yerine `/Home/Error`'a yönlendiriyor (`HomeController.Error()`, Day 1'den beri var olan, `ErrorViewModel` ile `RequestId` gösteren sade bir sayfa). `Development`'ta ise (bizim şu anki ortamımız) ASP.NET Core kendiliğinden çok daha ayrıntılı bir "geliştirici hata sayfası" gösteriyor — kodun tam olarak nerede patladığını görmen için. Bunu bugün canlı tetiklemedik (gerçek veritabanımızı riske atmamak için), ama artık bu satırların ne işe yaradığını biliyoruz — Day 1'de sadece "şablon böyle geliyor" demiştik, bugün gerçek anlamını öğrendik.

---

## Doğrulanan davranış

```
dotnet build → 0 Hata, 0 Uyarı
dotnet test  → 8/8 (regresyon yok)

GET /Skills → Evidence sütunu: C# = 1, EF Core = 1, diğerleri = 0 (seed'e göre doğru)
GET /Skills/Edit/9999 → HTTP 404 + konsolda "Skill 9999 not found for edit." uyarısı
GET /, GET /Dashboard → HTTP 200 (regresyon yok)

Gerçek SQL (konsoldan): .Include() sayesinde LEFT JOIN ile tek sorguda
Skill + EvidenceRecords birlikte çekiliyor.
```
