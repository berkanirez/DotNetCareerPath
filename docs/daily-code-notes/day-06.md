# Day 6 — Kod Notları

Faz 1, Hafta 2, Gün 6. Konu: roadmap/phase/project/milestone ilişkileri, EF Core relationships, veritabanı kısıtlamaları.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `src/RoadmapOS.Web/Domain/RoadmapPhase.cs`, `Project.cs`, `Milestone.cs`

```csharp
public class RoadmapPhase
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Order { get; set; }
    public List<Project> Projects { get; set; } = new();

    public RoadmapPhase(string name, int order)
    {
        Name = name;
        Order = order;
    }
}

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int RoadmapPhaseId { get; set; }
    public RoadmapPhase? RoadmapPhase { get; set; }
    public List<Milestone> Milestones { get; set; } = new();

    public Project(string name, int roadmapPhaseId)
    {
        Name = name;
        RoadmapPhaseId = roadmapPhaseId;
    }
}

public class Milestone
{
    public int Id { get; set; }
    public string Name { get; set; }
    public bool IsCompleted { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public Milestone(string name, int projectId)
    {
        Name = name;
        ProjectId = projectId;
    }
}
```

**Neden:** RoadmapOS'un asıl amacı — `docs/ROADMAP.md`/`CURRENT_STATE.md`'de elle tuttuğumuz faz/proje/milestone yapısını gerçek, sorgulanabilir veriye dönüştürmek. Üç seviyeli bir zincir kurduk: bir `RoadmapPhase` birden fazla `Project` içerebilir, bir `Project` birden fazla `Milestone` içerebilir. Ayrı bir "Roadmap" entity'si eklemedik çünkü bu uygulama tek kullanıcılı ve tek bir roadmap'i takip ediyor — `RoadmapPhase`'i en üst seviye yapmak, gereksiz bir soyutlama katmanından (over-engineering) kaçınmak için bilinçli bir tercih.

**Nasıl çalışıyor — navigation property'ler:**
* `List<Project> Projects` (RoadmapPhase üzerinde) — **collection navigation**: "bu faza ait tüm projeler". Prisma'daki `projects Project[]` ile birebir aynı fikir.
* `RoadmapPhase? RoadmapPhase` (Project üzerinde) — **reference navigation**: "bu projenin ait olduğu tek faz". Nullable çünkü EF Core bu property'yi bazen (örn. sorgu sırasında ilişkiyi hiç yüklemediysen) `null` bırakabilir — "bu ilişki her zaman dolu olmak zorunda" ile "bu C# property'si bazen boş olabilir" birbirinden farklı şeyler.
* `int RoadmapPhaseId` — **foreign key property**. EF Core, isim convention'ını (`<NavigationPropertyAdı>Id`) tanıyıp bunu otomatik olarak foreign key kabul ediyor — ayrıca elle bir şey belirtmemize gerek kalmadı (aşağıda Fluent API'de zaten belirtiyoruz ama bu netlik için, convention zaten yeterliydi).
* Constructor'lar yine sadece **zorunlu** alanları alıyor (`name`, `order` / `roadmapPhaseId`); `Id`, koleksiyon navigation'lar ve `IsCompleted` gibi alanlar constructor dışında, varsayılan değerleriyle bırakılıyor — Day 2'deki `Skill` tasarımıyla aynı prensip.

---

## 2. `src/RoadmapOS.Web/Data/RoadmapOSDbContext.cs` — `DbSet`'ler ve `OnModelCreating`

```csharp
public DbSet<Skill> Skills => Set<Skill>();
public DbSet<RoadmapPhase> RoadmapPhases => Set<RoadmapPhase>();
public DbSet<Project> Projects => Set<Project>();
public DbSet<Milestone> Milestones => Set<Milestone>();

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Skill>(entity =>
    {
        entity.Property(s => s.Name).HasMaxLength(100);
        entity.Property(s => s.Category).HasMaxLength(100);
        entity.Property(s => s.Notes).HasMaxLength(500);
    });

    modelBuilder.Entity<RoadmapPhase>(entity =>
    {
        entity.Property(p => p.Name).IsRequired().HasMaxLength(150);
    });

    modelBuilder.Entity<Project>(entity =>
    {
        entity.Property(p => p.Name).IsRequired().HasMaxLength(150);

        entity.HasOne(p => p.RoadmapPhase)
            .WithMany(ph => ph.Projects)
            .HasForeignKey(p => p.RoadmapPhaseId)
            .OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<Milestone>(entity =>
    {
        entity.Property(m => m.Name).IsRequired().HasMaxLength(150);

        entity.HasOne(m => m.Project)
            .WithMany(p => p.Milestones)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    });
}
```

**Neden:** İki ayrı görev üstleniyor bu kod: (1) Day 4/5'ten beri ertelediğimiz `Skill.Name`/`Category`/`Notes` için gerçek DB uzunluk sınırlarını **şimdi tamamlıyoruz** (Day 5'in `SkillFormModel`'indeki `[StringLength]` değerleriyle birebir aynı sayıları kullanarak); (2) yeni üç entity için hem zorunlu/uzunluk kısıtlamalarını hem de aralarındaki ilişkiyi **açıkça** tanımlıyoruz.

**Nasıl çalışıyor:**
* `OnModelCreating(ModelBuilder modelBuilder)` — `DbContext`'in, EF Core "model"ini (hangi entity'ler, hangi sütunlar, hangi kısıtlamalar/ilişkiler) kurarken **bir kere** çağırdığı override edilebilir metot. Buraya yazdığımız her şey, Data Annotation'larla ifade edemeyeceğimiz veya daha esnek/merkezi kontrol istediğimiz kuralları temsil ediyor — buna **Fluent API** deniyor (Prisma'daki `schema.prisma`'nın, kod içine gömülü hali gibi düşünebilirsin).
* `entity.Property(s => s.Name).HasMaxLength(100)` — bir lambda ifadesiyle "hangi property'den bahsettiğimizi" derleyici kontrollü şekilde belirtiyoruz (yazım hatasına karşı, düz string `"Name"` yazmaktan daha güvenli), ve bu property'nin SQL sütun tipini `nvarchar(100)` yapmasını istiyoruz.
* `entity.HasOne(p => p.RoadmapPhase).WithMany(ph => ph.Projects).HasForeignKey(p => p.RoadmapPhaseId)` — bu üç zincirlenmiş çağrı, ilişkiyi **her iki taraftan da** tam olarak tanımlıyor: "bir `Project`'in **bir** `RoadmapPhase`'i var (`HasOne`), bir `RoadmapPhase`'in **birden fazla** `Project`'i var (`WithMany`), ve bu ilişkiyi veritabanında `RoadmapPhaseId` sütunu temsil ediyor (`HasForeignKey`)."
* `.OnDelete(DeleteBehavior.Cascade)` — "bir `RoadmapPhase` silinirse, ona ait tüm `Project`'ler de otomatik silinsin" demek. Alternatifi `DeleteBehavior.Restrict` olurdu — "altında hâlâ proje varken fazı silmeye izin verme, önce hata ver." Bugünkü, tek kullanıcılı öğrenme uygulaması için Cascade daha anlamlı: yarım kalmış bir fazı silersen, altındaki projeler/milestone'lar zaten anlamsızlaşır.

---

## 3. Migration ve veritabanına uygulama

```
dotnet ef migrations add AddPhaseProjectMilestone
dotnet ef database update
```

**Neden:** Yukarıdaki model değişikliklerini gerçek SQL Server şemasına yansıtmak.

**Nasıl çalışıyor (üretilen migration'dan önemli parçalar):**
```csharp
migrationBuilder.AlterColumn<string>(
    name: "Name", table: "Skills",
    type: "nvarchar(100)", maxLength: 100, nullable: false,
    oldType: "nvarchar(max)");
```
Bu satır, migration komutunun bizi neden uyardığını açıklıyor ("veri kaybına yol açabilir"): `Skills.Name` sütunu `nvarchar(max)`'tan `nvarchar(100)`'e **daraltılıyor**. Eğer tabloda 100 karakterden uzun bir isim olsaydı, bu migration onu keserdi (truncate) — bizim verimiz kısa olduğu için sorun çıkmadı, ama EF Core'un bunu **önceden bizi uyararak** yapması, gerçek bir production senaryosunda çok değerli bir güvenlik ağı.

```csharp
table.ForeignKey(
    name: "FK_Projects_RoadmapPhases_RoadmapPhaseId",
    column: x => x.RoadmapPhaseId,
    principalTable: "RoadmapPhases",
    principalColumn: "Id",
    onDelete: ReferentialAction.Cascade);
```
Bu, C# tarafındaki `OnDelete(DeleteBehavior.Cascade)` kararının, gerçek SQL Server foreign key constraint'ine (`ON DELETE CASCADE`) birebir çevrilmiş hali.

`dotnet ef database update` çalıştığında bu SQL'ler gerçekten SQL Server'a gönderildi — üç yeni tablo, iki foreign key, ve FK sütunları için otomatik oluşturulan iki index (`IX_Projects_RoadmapPhaseId`, `IX_Milestones_ProjectId` — EF Core, sorgu performansı için her FK sütununa otomatik index ekliyor) oluştu.

---

## 4. `src/RoadmapOS.Web/Program.cs` — seed bloğunun genişletilmesi

```csharp
if (!context.RoadmapPhases.Any())
{
    var phase = new RoadmapPhase("Phase 1 — RoadmapOS", 1);
    context.RoadmapPhases.Add(phase);
    context.SaveChanges();

    var project = new Project("RoadmapOS", phase.Id);
    context.Projects.Add(project);
    context.SaveChanges();

    context.Milestones.AddRange(
        new Milestone("Day 1-5: environment, domain model, EF Core, create/edit flow", project.Id) { IsCompleted = true },
        new Milestone("Day 6: relationships and database constraints", project.Id) { IsCompleted = true },
        new Milestone("Day 7-10: domain service, LINQ, evidence tracking, release", project.Id) { IsCompleted = false }
    );
    context.SaveChanges();
}
```

**Neden:** Bu ilişkili yapıyı gerçek, anlamlı veriyle (gerçekten bu roadmap'in kendisiyle!) doldurmak için — geçen günlerdeki gibi Day 9'a kadar geçerli bir placeholder.

**Nasıl çalışıyor — neden üç ayrı `SaveChanges()` çağrısı:** `phase.Id`, veritabanı tarafından (IDENTITY/auto-increment ile) üretiliyor — yani `phase` nesnesi SQL Server'a gerçekten yazılıp geri dönene kadar `Id`'si `0`. `Project`'i oluştururken constructor'ımız gerçek bir `roadmapPhaseId` (int) istiyor; bu yüzden önce `phase`'i kaydedip (`SaveChanges()`) gerçek `Id`'sini almamız, sonra bu gerçek `Id` ile `Project`'i oluşturmamız gerekiyor. Aynı mantık `project.Id` ile `Milestone`'lar arasında da geçerli. Bu, **"parent önce kaydedilmeli ki child'ın FK'si dolu olsun"** kuralının somut bir örneği.

---

## Doğrulanan davranış

```
dotnet build                → 0 Hata, 0 Uyarı
migration + database update → RoadmapPhases, Projects, Milestones tabloları + FK'ler oluştu
GET /, GET /Skills           → HTTP 200 (regresyon yok)

Uygulama calisirken otomatik seed edilen veri (dogrudan SQL ile dogrulandi):
  RoadmapPhases: 1 | Phase 1 — RoadmapOS | 1
  Projects:      1 | RoadmapOS | PhaseId=1
  Milestones:    3 kayit, ProjectId=1, ikisi IsCompleted=True

Canli kisitlama testleri (SSMS/dogrudan SQL ile, uygulamayi hic kullanmadan):
  Test 1 — Gecersiz FK (RoadmapPhaseId=999) ile INSERT
    → REDDEDILDI: "FOREIGN KEY constraint FK_Projects_RoadmapPhases_RoadmapPhaseId" ihlali

  Test 2 — 200 karakterlik Name ile INSERT (sinir: 150)
    → REDDEDILDI: "String or binary data would be truncated"

  Test 3 — RoadmapPhase (Id=1) silme
    → Once: Phases=1 Projects=1 Milestones=3
    → Sonra: Phases=0 Projects=0 Milestones=0  (Cascade calisti)
    → Uygulama tekrar calistirilinca seed blogu bosluğu fark edip veriyi otomatik geri olusturdu
```

Bu üç test, bugünün en önemli fikrini kanıtlıyor: bu kısıtlamalar sadece migration dosyasında yazılı kalmadı, **gerçekten SQL Server tarafından uygulanıyor** — uygulamanın kendi kodunu hiç çalıştırmadan, doğrudan veritabanına saldırarak bile bunları kırmak mümkün değil.
