# Day 4 — Kod Notları

Faz 1, Hafta 1, Gün 4. Konu: SQL Server, Entity Framework Core, `DbContext`, entity'ler, migration'lar; ilk kaydın kalıcı hale getirilmesi.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. NuGet paketleri

```
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Microsoft.EntityFrameworkCore.Design
```

**Neden:** İki paketin de ayrı, gerekçeli bir işi var:
* `Microsoft.EntityFrameworkCore.SqlServer` — EF Core'un "provider"ı; EF Core'un genel sorgu/kaydetme mantığını **SQL Server'a özel SQL'e** çeviren katman. Provider olmadan EF Core hangi veritabanı ailesiyle (SQL Server mı, PostgreSQL mi, SQLite mı) konuşacağını bilemez.
* `Microsoft.EntityFrameworkCore.Design` — sadece **tasarım zamanında** (yani `dotnet ef migrations add` gibi komutları çalıştırırken) gereken araçlar. Uygulama çalışırken bu pakete ihtiyaç yok, sadece migration üretirken.

**Nasıl çalışıyor:** `dotnet add package`, `.csproj` dosyasına bir `<PackageReference>` satırı ekliyor ve paketi NuGet'ten indirip `~/.nuget/packages` altına önbelleğe alıyor — npm'de `npm install --save` ile `package.json`'a bağımlılık eklemenin birebir karşılığı.

---

## 2. `src/RoadmapOS.Web/appsettings.Development.json` — connection string

```json
{
  "Logging": { ... },
  "ConnectionStrings": {
    "RoadmapOSDb": "Server=localhost\\SQLEXPRESS;Database=RoadmapOS;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

**Neden:** Uygulamanın hangi veritabanına, nasıl bağlanacağını söyleyen bilgi — kod içine gömülmek yerine config'de tutuluyor ki ortam değiştiğinde (Development/Staging/Production) kod değişmeden farklı bir connection string kullanılabilsin.

**Nasıl çalışıyor:**
* `Server=localhost\SQLEXPRESS` — kurduğumuz SQL Server Express instance'ının adresi. JSON'da tek bir ters eğik çizgi (`\`) özel bir karakter olduğu için (escape karakteri), gerçek bir `\` yazmak için `\\` yazmamız gerekiyor.
* `Database=RoadmapOS` — henüz var olmayan bir veritabanı adı; birazdan migration bunu **kendisi oluşturacak**.
* `Trusted_Connection=True` — kullanıcı adı/şifre yerine Windows oturumunun kimliğiyle bağlan (Windows Authentication). Şifre yönetmemize gerek bırakmıyor, yerel geliştirme için ideal.
* `TrustServerCertificate=True` — yerel SQL Server'ın kendinden imzalı sertifikasını sorgulamadan kabul et (yerel geliştirme için güvenli bir basitleştirme; gerçek bir production sunucusunda geçerli bir sertifika olurdu).

---

## 3. `src/RoadmapOS.Web/Data/RoadmapOSDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Data;

public class RoadmapOSDbContext : DbContext
{
    public RoadmapOSDbContext(DbContextOptions<RoadmapOSDbContext> options) : base(options)
    {
    }

    public DbSet<Skill> Skills => Set<Skill>();
}
```

**Neden:** Veritabanına açılan "oturum" nesnesi — hangi entity'lerin (tabloların) var olduğunu EF Core'a bildiren merkezi sınıf. Bu sınıf var olmadan EF Core'un `Skill` class'ından bir tablo çıkarması mümkün değil.

**Nasıl çalışıyor:**
* `: DbContext` — EF Core'un temel sınıfından kalıtım alıyoruz; `SaveChanges()`, sorgulama altyapısı gibi tüm EF Core mekanizması buradan geliyor.
* `public RoadmapOSDbContext(DbContextOptions<RoadmapOSDbContext> options) : base(options)` — constructor, hangi veritabanına, hangi ayarlarla bağlanılacağı bilgisini (`options`) dışarıdan (DI container'dan) alıyor ve base class'a (`DbContext`) iletiyor. Bu ayarları biz `Program.cs`'te (`UseSqlServer(...)`) vereceğiz.
* `public DbSet<Skill> Skills => Set<Skill>();` — "Skills tablosu, `Skill` class'ından üretilir" demenin yolu. `DbSet<Skill>`, SQL'deki bir tabloyu C# tarafında bir koleksiyon gibi kullanmanı sağlıyor (`_context.Skills.ToList()` gibi). `=> Set<Skill>()` expression-bodied bir property (Day 2'de gördüğümüz `IsAtTarget`'a benzer syntax) — her erişimde `DbContext`'in içindeki `Skill` tipi için olan takip edilen set'i döndürüyor.

---

## 4. Migration üretimi

```
dotnet ef migrations add InitialCreate
```

**Neden:** `RoadmapOSDbContext` ve `Skill` class'ına bakıp, veritabanı şemasının **nasıl olması gerektiğini** kod olarak üretmek için. Bu komut henüz veritabanına dokunmuyor — sadece "şu değişiklik yapılmalı" diyen bir C# dosyası (migration) üretiyor.

**Nasıl çalışıyor (üretilen dosyadan):**
```csharp
migrationBuilder.CreateTable(
    name: "Skills",
    columns: table => new
    {
        Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
        Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
        Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
        CurrentLevel = table.Column<int>(type: "int", nullable: false),
        TargetLevel = table.Column<int>(type: "int", nullable: false),
        Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
    },
    constraints: table => { table.PrimaryKey("PK_Skills", x => x.Id); });
```

Dikkat çeken en önemli nokta: `Name`/`Category` → `nullable: false`, `Notes` → `nullable: true`. Bunu biz elle yazmadık — EF Core, `Skill` class'ındaki `string` (nullable olmayan) ile `string?` (nullable) ayrımını **otomatik olarak** SQL'in `NOT NULL` / `NULL` kısıtlamasına çevirdi. Day 2'de öğrendiğimiz nullable reference types kavramı, burada gerçek bir veritabanı kısıtlamasına dönüşmüş oldu. `Id` alanı `int` olduğu ve adı "Id" olduğu için EF Core, convention gereği bunu otomatik olarak primary key + auto-increment (`Identity`) kabul etti — hiçbir yerde elle "bu primary key'dir" demedik.

---

## 5. Veritabanına uygulama

```
dotnet ef database update
```

**Neden:** Az önce üretilen migration dosyasını gerçekten çalıştırıp, `RoadmapOS` veritabanını ve `Skills` tablosunu **gerçek SQL Server'da** oluşturmak için.

**Nasıl çalışıyor:** Komut, `CREATE DATABASE [RoadmapOS];` ve ardından `CREATE TABLE [Skills] (...)` SQL komutlarını doğrudan SQL Server'a gönderdi (konsol çıktısında bunu gördük). Ayrıca `__EFMigrationsHistory` adında EF Core'un kendi kullandığı bir tablo da oluşturuldu — bu tablo, "hangi migration'lar zaten uygulandı" bilgisini tutuyor, böylece `dotnet ef database update`'i tekrar çalıştırırsak EF Core aynı migration'ı iki kez uygulamaya çalışmıyor.

---

## 6. `src/RoadmapOS.Web/Data/EfSkillCatalog.cs`

```csharp
using RoadmapOS.Web.Domain;

namespace RoadmapOS.Web.Data;

public class EfSkillCatalog : ISkillCatalog
{
    private readonly RoadmapOSDbContext _context;

    public EfSkillCatalog(RoadmapOSDbContext context)
    {
        _context = context;
    }

    public IReadOnlyList<Skill> GetAll()
    {
        var skills = _context.Skills.ToList();
        skills.Sort();
        return skills;
    }
}
```

**Neden:** `ISkillCatalog` sözleşmesinin bugünkü, gerçek veritabanı kullanan implementasyonu — Day 3'te `InMemorySkillCatalog` için yazdığımız sözleşmenin **aynısını**, farklı bir veri kaynağıyla dolduruyoruz.

**Nasıl çalışıyor:**
* `RoadmapOSDbContext context` constructor injection — Day 3'teki `ISkillCatalog` injection'ıyla birebir aynı mekanizma, sadece bu sefer DI container'ın verdiği şey bir `DbContext`.
* `_context.Skills.ToList()` — `_context.Skills`, SQL'e henüz çevrilmemiş bir "sorgu tanımı" (`IQueryable<Skill>`); `.ToList()` çağrıldığı an EF Core bunu gerçek bir `SELECT * FROM Skills` SQL sorgusuna çevirip SQL Server'a gönderiyor, sonucu `Skill` nesnelerine geri eşleyip bir `List<Skill>` olarak döndürüyor. `ToList()` teknik olarak bir LINQ metodu — bugün sadece "sorguyu çalıştır ve belleğe çek" anlamında dar kapsamda kullanıyoruz, tam LINQ konusunu Day 8'de işleyeceğiz.
* `skills.Sort()` — Day 2'den beri değişmeyen `IComparable<Skill>` mantığı; artık veritabanından gelen gerçek veri üzerinde çalışıyor.

---

## 7. `src/RoadmapOS.Web/Program.cs` — DI kaydı ve geçici seed bloğu

```csharp
using Microsoft.EntityFrameworkCore;
using RoadmapOS.Web.Data;
using RoadmapOS.Web.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<RoadmapOSDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("RoadmapOSDb")));
builder.Services.AddScoped<ISkillCatalog, EfSkillCatalog>();

var app = builder.Build();

// TEMPORARY — placeholder seeding until Day 9 introduces a real seed data strategy.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<RoadmapOSDbContext>();
    if (!context.Skills.Any())
    {
        context.Skills.AddRange( /* dört skill */ );
        context.SaveChanges();
    }
}
```

**Neden:**
* `AddDbContext<RoadmapOSDbContext>(...)` — `RoadmapOSDbContext`'i DI container'a kaydediyor, `UseSqlServer(...)` ile "hangi connection string'i kullanacağını" söylüyor. `builder.Configuration.GetConnectionString("RoadmapOSDb")`, `appsettings.Development.json`'daki `ConnectionStrings:RoadmapOSDb` değerini okuyor.
* `AddScoped<ISkillCatalog, EfSkillCatalog>()` — Day 3'teki `AddSingleton<ISkillCatalog, InMemorySkillCatalog>()`'un yerini aldı. `Singleton` değil `Scoped` seçtik çünkü `EfSkillCatalog`, içinde bir `DbContext` taşıyor ve `DbContext`'ler **thread-safe değil** — birden fazla isteğin aynı `DbContext`'i aynı anda paylaşması ciddi hatalara yol açar (Day 3'te konuştuğumuz singleton+concurrency riskinin, EF Core dünyasındaki karşılığı, ama burada çözüm "kopyalamak" değil, "her isteğe kendi DbContext'ini vermek" — `AddDbContext` zaten varsayılan olarak `Scoped` kaydeder, biz de `ISkillCatalog`'u aynı lifetime ile eşleştirdik).
* Seed bloğu — henüz resmi bir seed stratejimiz yok (Day 9'un konusu), ama boş bir tabloyla `/Skills`'i test etmek anlamsız olurdu. Bu yüzden Day 2'den beri kullandığımız aynı 4 skill'i, sadece tablo boşsa (`!context.Skills.Any()`), uygulama her açıldığında bir kere ekliyoruz — geçici, açıkça işaretlenmiş bir çözüm.

**Nasıl çalışıyor:**
* `using var scope = app.Services.CreateScope();` — `AddScoped` olarak kayıtlı `RoadmapOSDbContext`'i, normal bir HTTP isteği dışında (`Program.cs`'in en üst seviyesinde) elle kullanabilmek için kendi "scope"umuzu açıyoruz. `using`, bu scope'un iş bitince otomatik kapatılmasını (dispose edilmesini) sağlıyor.
* `context.Skills.Any()` — tabloda hiç satır var mı diye kontrol eden, `ToList()` gibi minimal düzeyde kullandığımız bir başka LINQ metodu; `SELECT` sorgusunu tüm satırları çekmeden, sadece "var mı yok mu" diye optimize eder.
* `context.Skills.AddRange(...)` — yeni `Skill` nesnelerini, `DbContext`'in "takip ettiği" (tracked) nesneler listesine ekliyor — henüz veritabanına yazılmadı.
* `context.SaveChanges()` — asıl INSERT komutlarını SQL Server'a gönderen çağrı. `AddRange` sadece hafızada işaretliyor, `SaveChanges()` gerçek yazma işlemini tetikliyor.

---

## Doğrulanan davranış

```
dotnet build              → 0 Hata, 0 Uyarı
dotnet ef migrations add  → InitialCreate migration'ı üretildi
dotnet ef database update → RoadmapOS veritabanı + Skills tablosu gerçekten oluşturuldu
GET /Skills                → HTTP 200, 4 skill, doğru sırada, "Toplam 4 skill."
Uygulama kapatıldıktan sonra, doğrudan SQL sorgusuyla (uygulamadan bağımsız):
  1 | C# | Language | 2 | 4 |
  2 | ASP.NET Core | Framework | 1 | 3 |
  3 | EF Core | Framework | 0 | 3 | Not started yet
  4 | SQL Server | Database | 0 | 2 |
```

Son satır özellikle önemli: uygulama tamamen kapalıyken bile veri veritabanında duruyor — bu, `InMemorySkillCatalog`'un aksine, artık **gerçek kalıcılığa** sahip olduğumuzun kanıtı.
