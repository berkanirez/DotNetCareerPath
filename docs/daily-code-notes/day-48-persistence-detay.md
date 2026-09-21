# Day 48 — Persistence Kurulumu: Referans Notu

Bu, `day-48.md`'nin yanında, sadece "veritabanı bağlantısını nasıl kurduk, neyi neyle değiştirdik, neler ekledik" sorusuna odaklanan ayrı bir referans notu — Day 24'ün `day-24-refresh-akisi-detay.md`'siyle aynı gerekçeyle (ana anlatı yeterince net değilse, ayrı ve odaklı bir detay dosyası).

---

## 1. Neden yaptık (tek cümle)

Redis'in (Week 10) koruyacağı **gerçek bir şey** olsun diye — önbellekleme, sadece bellekten daha yavaş bir kaynak varsa anlamlıdır, ve FieldOps'un o ana kadar hiç veritabanı yoktu.

---

## 2. Veritabanı bağlantısı nasıl kuruldu — adım adım

### 2.1. Connection string'ler nerede tanımlı

`src/FieldOps.Api/appsettings.Development.json`:

```json
"ConnectionStrings": {
  "FieldOpsOrganizationsDb": "Server=localhost\\SQLEXPRESS;Database=FieldOpsOrganizations;Trusted_Connection=True;TrustServerCertificate=True;",
  "FieldOpsEmployeesDb": "Server=localhost\\SQLEXPRESS;Database=FieldOpsEmployees;Trusted_Connection=True;TrustServerCertificate=True;",
  "FieldOpsWorkOrdersDb": "Server=localhost\\SQLEXPRESS;Database=FieldOpsWorkOrders;Trusted_Connection=True;TrustServerCertificate=True;",
  "FieldOpsCustomersDb": "Server=localhost\\SQLEXPRESS;Database=FieldOpsCustomers;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

**4 ayrı veritabanı adı, aynı SQL Server örneği** (`localhost\SQLEXPRESS` — RoadmapOS/StockPilot'un da kullandığı örnek). Farklı sunucular değil, farklı veritabanları.

### 2.2. `Program.cs`'te bu string'lerin okunup modüllere verilmesi

```csharp
string RequireConnectionString(string name) =>
    builder.Configuration.GetConnectionString(name) ?? throw new InvalidOperationException($"Missing connection string: {name}");

builder.Services.AddOrganizationsModule(RequireConnectionString("FieldOpsOrganizationsDb"));
builder.Services.AddEmployeesModule(RequireConnectionString("FieldOpsEmployeesDb"));
builder.Services.AddWorkOrdersModule(RequireConnectionString("FieldOpsWorkOrdersDb"));
builder.Services.AddCustomersModule(RequireConnectionString("FieldOpsCustomersDb"));
```

`Program.cs` (host), hiçbir zaman bir `DbContext` tipini ya da EF Core'u doğrudan görmüyor — sadece düz bir `string` (connection string) her modülün kendi `Add*Module` metoduna veriliyor.

### 2.3. Her modülün kendi içinde bu string'i nasıl kullandığı

Örnek — `OrganizationsModule.cs` (dördü de birebir aynı desen):

```csharp
public static IServiceCollection AddOrganizationsModule(this IServiceCollection services, string connectionString)
{
    services.AddDbContext<OrganizationsDbContext>(options => options.UseSqlServer(connectionString));
    return services.AddScoped<IOrganizationDirectory, EfOrganizationDirectory>();
}
```

Burada iki şey oluyor:
1. `AddDbContext<OrganizationsDbContext>` — EF Core'a "bu connection string'le bir `OrganizationsDbContext` oluştur" deniyor.
2. `AddScoped<IOrganizationDirectory, EfOrganizationDirectory>` — arayüzün (`IOrganizationDirectory`, hiç değişmedi) gerçek implementasyonunun artık `EfOrganizationDirectory` olduğu söyleniyor.

`OrganizationsDbContext` ve `EfOrganizationDirectory` **`internal`** — bu dosyanın dışından (host dahil) kimse bu isimleri göremez. Host sadece `AddOrganizationsModule(connectionString)` çağırıyor, içeride ne olduğunu bilmiyor.

---

## 3. Her modülde ne neyle değiştirildi

| Modül | Eskisi (silindi) | Yenisi (eklendi) |
|---|---|---|
| Organizations | `InMemoryOrganizationDirectory.cs` | `Data/OrganizationsDbContext.cs`, `Data/EfOrganizationDirectory.cs` |
| Employees | `InMemoryEmployeeDirectory.cs` | `Data/EmployeesDbContext.cs`, `Data/EfEmployeeDirectory.cs` |
| WorkOrders | `InMemoryWorkOrderDirectory.cs` | `Data/WorkOrdersDbContext.cs`, `Data/EfWorkOrderDirectory.cs` |
| Customers | `InMemoryCustomerDirectory.cs` | `Data/CustomersDbContext.cs`, `Data/EfCustomerDirectory.cs` |

**Hiçbir modülün public arayüzü (`IOrganizationDirectory`, `IEmployeeDirectory`, `IWorkOrderDirectory`, `ICustomerDirectory`) değişmedi.** Controller'lar tek satır bile değişmedi. Sadece DI kaydı, "hangi somut sınıf bu arayüzü karşılıyor" sorusunun cevabını değiştirdi — Day 32'nin "modül-özel DI kaydı" deseninin tam olarak neden değerli olduğunun kanıtı.

---

## 4. Modül başına eklenen tüm yeni dosyalar

Her modülde **aynı 5 dosya** eklendi (isimleri modüle göre değişiyor):

1. `Data/<Modül>DbContext.cs` — `internal`, `DbSet<Entity>`, seed veri (`HasData`, sadece Organizations/Employees/Customers'ta — WorkOrders'ın seed verisi yok, zaten boş başlıyordu).
2. `Data/Ef<Modül>Directory.cs` — `internal`, arayüzü gerçek EF Core sorgularıyla uygulayan sınıf.
3. `Data/<Modül>DbContextFactory.cs` — `internal`, sadece `dotnet ef migrations add` komutunun class library'de çalışabilmesi için (tasarım-zamanı factory, uygulama çalışırken hiç kullanılmıyor).
4. `AssemblyInfo.cs` — `[assembly: InternalsVisibleTo("FieldOps.Api.Tests")]` — sadece test projesinin `internal` DbContext'lere erişebilmesi için, dar bir istisna.
5. `Data/Migrations/` klasörü — `dotnet ef migrations add InitialCreate` ile otomatik üretilen migration dosyaları.

**Toplamda:** 4 modül × 5 dosya türü = ~20 yeni dosya, ama hepsi **aynı, tekrar eden desen**.

---

## 5. Test altyapısı nasıl değişti

`tests/FieldOps.Api.Tests/FieldOpsApiFactory.cs` (yeni dosya) — StockPilot Day 28'in `StockPilotApiFactory`'sinin aynısı:

- Testler başlamadan önce **tek bir** gerçek, tek-kullanımlık SQL Server konteyneri (Docker/Testcontainers) açılıyor.
- O konteynerin içinde, gerçek uygulamanın kullandığı **4 ayrı veritabanı adıyla** (farklı sunucu değil) 4 modülün migration'ı uygulanıyor.
- Testler bittiğinde konteyner tamamen siliniyor — gerçek yerel geliştirme veritabanına (`localhost\SQLEXPRESS`) hiç dokunulmuyor.

`EmployeesAuthorizationIntegrationTests` ve `WorkOrdersAuthorizationIntegrationTests`, artık `IClassFixture<WebApplicationFactory<Program>>` yerine `IClassFixture<FieldOpsApiFactory>` kullanıyor.

---

## 6. Bir isteğin baştan sona akışı (örnek: `GET /api/organizations`)

```
1. İstek gelir → OrganizationsController.GetAll()
2. Controller, DI'dan aldığı IOrganizationDirectory'yi çağırır (hangi somut sınıf olduğunu bilmez)
3. Gerçekte çağrılan: EfOrganizationDirectory.GetAll()
4. EfOrganizationDirectory, kendi OrganizationsDbContext'i üzerinden SQL sorgusu çalıştırır
5. OrganizationsDbContext, appsettings.Development.json'dan gelen connection string'le
   FieldOpsOrganizations veritabanına bağlanır
6. Gerçek satırlar döner → OrganizationSummary'lere dönüştürülür → JSON olarak API'den çıkar
```

Bu akış, 4 modülün 4'ü için de birebir aynı — sadece isimler değişiyor.

---

## Kısa özet

**Neyi değiştirdik:** Her modülün bellek-içi listesi yerine gerçek bir SQL Server veritabanı okuyan/yazan bir implementasyon koyduk — arayüzler ve controller'lar hiç değişmedi.

**Neyi ekledik:** Modül başına bir `DbContext`, bir `Ef*Directory`, bir tasarım-zamanı factory, bir dar `InternalsVisibleTo` izni, bir migration klasörü — ve testler için tek, paylaşılan bir Testcontainers altyapısı.

**Nasıl bağladık:** `appsettings.Development.json`'daki 4 connection string → `Program.cs`'teki `RequireConnectionString` → her modülün kendi `Add*Module(connectionString)`'i → modülün kendi `AddDbContext<...>` çağrısı.
