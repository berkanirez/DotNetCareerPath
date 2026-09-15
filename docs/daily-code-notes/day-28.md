# Day 28 — Kod Notları

Faz 2, Hafta 6, Gün 28. Konu: Test-database isolation (Testcontainers).

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `tests/StockPilot.Api.Tests/StockPilot.Api.Tests.csproj` — yeni paket

```
Testcontainers.MsSql
```

**Neden:** Bu paket, testlerimizin içinden **gerçek** bir SQL Server'ı, Docker konteyneri içinde, programatik olarak başlatıp durdurabilmemizi sağlıyor.

---

## 2. `tests/StockPilot.Api.Tests/StockPilotApiFactory.cs` — yeni dosya, asıl mekanizma

```csharp
public class StockPilotApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _dbContainer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
```

**Neden `WebApplicationFactory<Program>`'ı miras alıyoruz:** Day 27'de doğrudan `WebApplicationFactory<Program>` kullanıyorduk. Bugün kendi **özel** versiyonumuzu yazıyoruz — çünkü uygulamanın normalde kullandığı veritabanı bağlantısını **değiştirmemiz** gerekiyor, bunun için `WebApplicationFactory`'nin genişletilebilir noktalarına ihtiyacımız var.

**`IAsyncLifetime` nedir:** xUnit'in "bu sınıfın testler başlamadan **önce** çalışacak bir kurulum (`InitializeAsync`), testler bittikten **sonra** çalışacak bir temizlik (`DisposeAsync`) metodu var" deseni.

```csharp
public async Task InitializeAsync()
{
    await _dbContainer.StartAsync();

    var options = new DbContextOptionsBuilder<StockPilotDbContext>()
        .UseSqlServer(_dbContainer.GetConnectionString())
        .Options;

    await using var context = new StockPilotDbContext(options);
    await context.Database.MigrateAsync();
    DbSeeder.Seed(context);
}
```

Adım adım:
1. `_dbContainer.StartAsync()` — Docker'da gerçek bir SQL Server konteyneri başlatıyor (birkaç saniye sürebilir; imaj zaten indirilmişse hızlı).
2. `_dbContainer.GetConnectionString()` — Testcontainers, konteyneri **rastgele boş bir portta** başlatıyor; bu metot o portu içeren gerçek bir bağlantı dizesi veriyor.
3. Bu bağlantı dizesiyle **geçici** bir `StockPilotDbContext` kuruyoruz — sadece şemayı hazırlamak için.
4. `context.Database.MigrateAsync()` — `Migrations/` klasöründeki **gerçek, aynı** migration'ları (`InitialCreate`, `AddUniqueSkuIndex`, `AddProductRowVersion`) bu tertemiz konteynere uyguluyor. Bu, gerçek dev veritabanımızın şemasıyla **birebir aynı** bir şema demek.
5. `DbSeeder.Seed(context)` — `Program.cs`'in Development modunda çağırdığı **aynı** seed metodu, aynı 3 örnek ürünü bu izole veritabanına da ekliyor.

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureServices(services =>
    {
        var realDbContextDescriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(DbContextOptions<StockPilotDbContext>));
        if (realDbContextDescriptor is not null)
        {
            services.Remove(realDbContextDescriptor);
        }

        services.AddDbContext<StockPilotDbContext>(options =>
            options.UseSqlServer(_dbContainer.GetConnectionString()));
    });
}
```

**Neden önce kaldırıp sonra tekrar ekliyoruz:** `Program.cs`, `StockPilotDbContext`'i zaten `appsettings.Development.json`'daki **gerçek** bağlantı dizesiyle kaydediyor (`builder.Services.AddDbContext<StockPilotDbContext>(...)`). Test uygulamayı ayağa kaldırırken bu kayıt hâlâ orada duruyor. `ConfigureWebHost`, bu gerçek kaydı **bulup siliyor** (`services.Remove(...)`), sonra **aynı tip için**, ama **konteynerin** bağlantı dizesiyle **yeni bir kayıt ekliyor**. Sonuç: uygulama kodu (`AuthController`, `ProductsController`, `EfProductStore`) hiç değişmeden, hangi veritabanına konuştuğu tamamen değişiyor.

```csharp
public new async Task DisposeAsync()
{
    await _dbContainer.DisposeAsync();
    await ((IAsyncDisposable)this).DisposeAsync();
}
```

**Neden `override` değil `new`:** İlk denememde `override` yazdım ama derleyici hata verdi — `WebApplicationFactory`'nin kendi `DisposeAsync()`'i zaten var (`IAsyncDisposable`'dan geliyor), ama o `ValueTask` döndürüyor; xUnit'in `IAsyncLifetime`'ı ise `Task` döndüren bir `DisposeAsync()` istiyor. Aynı isimli ama **uyumsuz** iki metot — biri diğerinin üzerine yazılamıyor (`override` edilemiyor), bu yüzden `new` ile **ayrı, bağımsız** bir metot tanımlamak gerekiyor. `((IAsyncDisposable)this).DisposeAsync()` satırı, gizlenmiş olan **orijinal** `WebApplicationFactory` temizliğini (test sunucusu, `HttpClient` gibi kaynaklar) de elle çağırıyor — böylece hem konteyner hem de temel sınıfın kendi kaynakları temizleniyor.

---

## 3. `tests/StockPilot.Api.Tests/ProductsAuthorizationIntegrationTests.cs` — küçük bir değişiklik

```csharp
public class ProductsAuthorizationIntegrationTests : IClassFixture<StockPilotApiFactory>
{
    private readonly StockPilotApiFactory _factory;

    public ProductsAuthorizationIntegrationTests(StockPilotApiFactory factory)
```

Day 27'deki `IClassFixture<WebApplicationFactory<Program>>` → `IClassFixture<StockPilotApiFactory>`. Testlerin **hiçbirinin içi değişmedi** — hâlâ aynı `client.DeleteAsync(...)`, aynı `LoginAsync(...)`. Tek fark, artık `_factory`'nin arkasında gerçek dev veritabanı değil, izole bir konteyner duruyor.

---

## Bağımsız görev — `InitializeAsync` gerçekten "kapıcı" mı, canlı kanıt

`InitializeAsync`'e geçici olarak `throw new Exception("test");` eklenip testler çalıştırıldı:

```
StockPilot.Api.Tests.ProductsAuthorizationIntegrationTests.Delete_AdminToken_...            [FAIL]
StockPilot.Api.Tests.ProductsAuthorizationIntegrationTests.GetAll_NoToken_ReturnsOk          [FAIL]
StockPilot.Api.Tests.ProductsAuthorizationIntegrationTests.Delete_NoToken_ReturnsUnauthorized [FAIL]
StockPilot.Api.Tests.ProductsAuthorizationIntegrationTests.Delete_EmployeeToken_ReturnsForbidden [FAIL]

Hata Iletisi (dorduncusu de dahil, HEPSI AYNI):
   System.Exception : test
   at StockPilot.Api.Tests.StockPilotApiFactory.InitializeAsync() ... line 25
```

**Sonuç:** Sınıftaki **4 testin de tamamı** başarısız oldu, hepsi **aynı** hatayla ve **aynı** satırdan — hiçbiri kendi asıl kodunu (login, HTTP isteği, assert) çalıştırmaya bile fırsat bulamadı. Bu, `InitializeAsync`'in gerçekten bir "kapıcı" gibi çalıştığını kanıtlıyor: sınıftaki testlerden **önce** çalışıyor, başarısız olursa **hiçbir test** kendi mantığına ulaşamıyor. Satır geri alındı, `dotnet test` tekrar 26/26 verdi (StockPilot) — regresyon yok.

---

## Canlı kanıt

```
Test calisirken:  docker ps  → gecici bir SQL Server konteyneri (ve Testcontainers'in
                                 kendi "Ryuk" temizlik gozlemci konteyneri) gorunuyor
Testler bitince:  docker ps -a → SQL Server test konteyneri TAMAMEN SILINMIS
                                  (durdurulmus degil, hic listede yok)

sqlcmd ile gercek StockPilotDb kontrolu:
  Test oncesi urun sayisi: 7
  Test sonrasi urun sayisi: 7   ← HIC DEGISMEDI, testler artik ona hic dokunmuyor

dotnet test (StockPilot) → 26/26 basarili (ayni testler, artik izole konteynere karsi)
dotnet test (RoadmapOS)  → 8/8 (etkilenmedi)
```

**Bugünkü asıl kazanım:** Day 27'nin testleri "paylaşılan dükkanda alışveriş yapıp temizlik bırakmamaya" dayanıyordu (benzersiz SKU'lar). Bugünden itibaren testler **kendi özel, tek kullanımlık dükkanlarında** çalışıyor — gerçek dükkana (dev veritabanı) hiç girmiyorlar bile.
