# Day 16 — Kod Notları

Faz 2, Hafta 4, Gün 1 (Pazartesi). Konu: StockPilot'a EF Core + SQL Server — RoadmapOS Day 3→4 geçişinin tekrarı.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. NuGet paketleri + connection string

```
dotnet add package Microsoft.EntityFrameworkCore.SqlServer --version 10.0.11
dotnet add package Microsoft.EntityFrameworkCore.Design --version 10.0.11
```

RoadmapOS Day 4'teki **birebir aynı** paketler. `appsettings.Development.json`'a `StockPilotDb` connection string'i eklendi — RoadmapOS ile **aynı SQL Server instance'ı** (`localhost\SQLEXPRESS`), ama **ayrı bir veritabanı** (`Database=StockPilot`). İki proje, aynı sunucuda ama tamamen izole veritabanlarında yaşıyor — birbirine hiç karışmıyorlar.

---

## 2. `Data/StockPilotDbContext.cs`

```csharp
public class StockPilotDbContext : DbContext
{
    public StockPilotDbContext(DbContextOptions<StockPilotDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Sku).IsRequired().HasMaxLength(50);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Price).HasPrecision(18, 2);
        });
    }
}
```

RoadmapOS'un `RoadmapOSDbContext`'iyle birebir aynı yapı. **Bugün yeni olan tek şey:** `entity.Property(p => p.Price).HasPrecision(18, 2);`.

### Beklenmedik, gerçek bir uyarı

İlk `dotnet ef migrations add` denememde şu uyarı çıktı:
```
warn: No store type was specified for the decimal property 'Price' on entity type
'Product'. This will cause values to be silently truncated if they do not fit in
the default precision and scale.
```

**Ne demek:** `decimal Price` property'sine hiçbir hassasiyet/ölçek (`precision`/`scale`) bilgisi vermeden bırakırsak, EF Core bir varsayılana güveniyor ama bu, **bazı değerlerin sessizce kesilebileceği** anlamına geliyor (örn. `19.999` gibi bir değer, hiç hata vermeden `20.00`'a yuvarlanabilirdi). `HasPrecision(18, 2)` — "toplamda 18 basamak, bunun 2'si ondalık kısım" diyerek bunu **açıkça** belirledik. Migration'ı silip (`dotnet ef migrations remove`) düzelttikten sonra yeniden oluşturdum — uyarı kayboldu, üretilen SQL'de `decimal(18,2)` net bir şekilde göründü.

**Neden önemli:** Day 12'de `19.99m`, `79.99m` gibi fiyatları hiç sorgulamadan yazmıştık — bugün bunun **gerçek bir DB kısıtlaması** gerektirdiğini gördük. `dotnet ef` komutlarının verdiği uyarıları görmezden gelmemenin (RoadmapOS Day 16'nın kendisindeki güvenlik uyarısı gibi) tekrar kanıtı.

---

## 3. `Data/EfProductStore.cs`

```csharp
public class EfProductStore : IProductStore
{
    private readonly StockPilotDbContext _context;

    public EfProductStore(StockPilotDbContext context) => _context = context;

    public IReadOnlyList<Product> GetAll() => _context.Products.ToList();
    public Product? GetById(int id) => _context.Products.Find(id);

    public Product Add(Product product)
    {
        _context.Products.Add(product);
        _context.SaveChanges();
        return product;
    }

    public bool Remove(int id)
    {
        var product = GetById(id);
        if (product is null) return false;
        _context.Products.Remove(product);
        _context.SaveChanges();
        return true;
    }
}
```

**Neden/Nasıl:** `EfSkillCatalog`'un (RoadmapOS Day 4) birebir StockPilot karşılığı — `IProductStore`'un dördüncü implementasyonu (`InMemoryProductStore`'dan sonra). Dikkat çeken tek fark: `GetAll()`'da hiç sıralama yok — çünkü artık **sıralama işini `ProductsController.GetAll()` zaten kendisi** yapıyor (Day 15'ten beri, `.OrderBy(...)` orada). `EfProductStore`, sadece ham veriyi `IReadOnlyList<Product>` olarak sağlıyor; filtreleme/sıralama/sayfalama mantığı controller'da kalıyor.

**Bilinen bir basitleştirme:** `ProductsController.GetAll()`, `_productStore.GetAll().AsEnumerable()` çağırıp filtre/sıralama/sayfalamayı **bellek içinde** yapıyor — yani `EfProductStore.GetAll()` **tüm tabloyu** çekiyor, sonra `Where`/`OrderBy`/`Skip`/`Take` C# tarafında çalışıyor. Gerçek bir production sisteminde (binlerce satır varsa), bu filtrelemenin SQL sorgusunun **kendisine** taşınması (`IQueryable<Product>` üzerinde doğrudan `Where`/`OrderBy` çağırıp SQL Server'a "sadece gereken satırları getir" dedirtmek) çok daha verimli olurdu. Bugün bunu **bilerek** değiştirmedik — `IProductStore`'un imzasını (ve dolayısıyla controller'ı) değiştirmemek için.

---

## 4. `Data/DbSeeder.cs`

RoadmapOS Day 9'daki `DbSeeder` deseninin StockPilot'a taşınması — `if (context.Products.Any()) return;` ile idempotent, 3 başlangıç ürünüyle.

## 5. `Program.cs` — DI değişikliği

```csharp
builder.Services.AddDbContext<StockPilotDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("StockPilotDb")));
builder.Services.AddScoped<IProductStore, EfProductStore>();
```

`AddSingleton<IProductStore, InMemoryProductStore>()` → `AddScoped<IProductStore, EfProductStore>()`. `Scoped` seçimi RoadmapOS Day 4'teki **aynı gerekçe**: `DbContext` thread-safe değil, her istek kendi `DbContext`'ini almalı.

**`InMemoryProductStore` silinmedi** — DI kaydından çıkarıldı ama dosya duruyor, çünkü `ProductsControllerTests.cs` hâlâ onu **doğrudan** kullanıyor (test double olarak) — bu yüzden bugün mevcut testler hiç bozulmadı.

---

## Doğrulanan davranış

```
dotnet build (StockPilot.slnx) → 0 Hata, 0 Uyarı
dotnet test (StockPilot)       → 8/8 (degisiklikten etkilenmedi)

Migration + database update    → StockPilot veritabani + Products tablosu
                                  (decimal(18,2) dogru sekilde)

GET /api/products    → seed verisi (3 urun), gercek DB'den
POST /api/products   → 201, Location: /api/Products/4
GET /api/products/4  → yeni urun, gercek DB'den geri geldi

Uygulama tamamen kapatildiktan sonra dogrudan SQL sorgusu:
  1 | SKU-001 | Wireless Mouse | 19,99
  2 | SKU-002 | Mechanical Keyboard | 79,99
  3 | SKU-003 | USB-C Hub | 34,50
  4 | SKU-004 | Webcam | 45,00
  → kalicilik kanitlandi, InMemoryProductStore'un aksine veri artik gercekten kaliyor
```
