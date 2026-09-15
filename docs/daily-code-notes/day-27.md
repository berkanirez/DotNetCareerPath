# Day 27 — Kod Notları

Faz 2, Hafta 6, Gün 27 — Hafta 6'nın (ve Phase 2'nin) ilk günü. Konu: `WebApplicationFactory` ile ilk gerçek entegrasyon testi.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `Program.cs` — sonuna eklenen tek satır

```csharp
public partial class Program { }
```

**Neden:** `Program.cs` bir "top-level statement" dosyası — yani `class Program { static void Main() { ... } }` yazmıyoruz, doğrudan kod satırları yazıyoruz. Derleyici arka planda bizim için **görünmez** bir `Program` sınıfı üretiyor, ama bu sınıf varsayılan olarak `internal` (sadece bu proje içinden erişilebilir). Test projesi (`StockPilot.Api.Tests`), `WebApplicationFactory<Program>` yazarken bu sınıfa **dışarıdan** (başka bir proje/assembly'den) erişmek zorunda — bu da sınıfın `public` olmasını gerektiriyor. Bu satır, **hiçbir çalışma zamanı davranışını değiştirmiyor** — sadece görünürlüğü açıyor, `internal` yerine `public` yapıyor.

---

## 2. `tests/StockPilot.Api.Tests/StockPilot.Api.Tests.csproj` — yeni paket

```
Microsoft.AspNetCore.Mvc.Testing
```

**Neden:** `WebApplicationFactory` sınıfı bu paketten geliyor — testlerin gerçek bir ASP.NET Core uygulamasını (tüm middleware'iyle) hafızada ayağa kaldırabilmesini sağlayan araç.

---

## 3. `tests/StockPilot.Api.Tests/ProductsAuthorizationIntegrationTests.cs` — yeni test dosyası

```csharp
public class ProductsAuthorizationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProductsAuthorizationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }
```

**`IClassFixture<T>` nedir:** xUnit'in "bu pahalı kaynağı (gerçek uygulamayı ayağa kaldırmak) her test için değil, bu test sınıfındaki **tüm testler için bir kere** kur" deseni. `WebApplicationFactory`, constructor'a otomatik olarak (xUnit tarafından) enjekte ediliyor.

```csharp
[Fact]
public async Task Delete_NoToken_ReturnsUnauthorized()
{
    var client = _factory.CreateClient();

    var response = await client.DeleteAsync("/api/products/999999");

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}
```

**Buradaki fark, önceki tüm testlerden:** `_factory.CreateClient()` — gerçek bir `HttpClient` veriyor. `client.DeleteAsync("/api/products/999999")` — bu, `controller.Delete(999999)` gibi metodu doğrudan çağırmak **değil**, gerçek bir HTTP isteği gönderiyor. Bu istek, gerçek `Program.cs`'teki **tüm** middleware zincirinden geçiyor: routing, `UseAuthentication`, `UseAuthorization`, sonunda `Delete` action'ı. Token olmadığı için `UseAuthentication` `HttpContext.User`'ı boş bırakıyor, `UseAuthorization` `[Authorize(Policy="CanManageProducts")]`'ı karşılayamadığını görüyor, gerçek bir `401` HTTP cevabı dönüyor — bu, `ProductsControllerTests.cs`'teki hiçbir testin asla kanıtlayamadığı bir şey.

```csharp
private static async Task<string> LoginAsync(HttpClient client, string username, string password)
{
    var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
    response.EnsureSuccessStatusCode();
    var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
    return login!.Token;
}
```

Gerçek bir `POST /api/auth/login` isteği atıp, gerçek bir JWT alıyor — tıpkı curl ile yaptığımız gibi, ama artık **otomatik bir test içinde**.

```csharp
var employeeToken = await LoginAsync(client, "employee", "Employee123!");
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", employeeToken);

var response = await client.DeleteAsync("/api/products/999999");

Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
```

`client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", employeeToken)` — curl'de elle yazdığımız `-H "Authorization: Bearer ..."` satırının kod karşılığı. Bundan sonraki her istek bu header'ı otomatik taşıyor.

---

## Canlı olarak yakalanan gerçek bir hata

İlk yazdığımızda üçüncü test (`Delete_AdminToken_...`) **gerçekten** başarısız oldu:

```
System.Net.Http.HttpRequestException : Response status code does not indicate success: 400 (Bad Request).
```

**Sebep:** Test, benzersiz bir SKU üretmek için şunu kullanıyordu:
```csharp
var uniqueSku = $"SKU-INTEGRATION-TEST-{Guid.NewGuid():N}";
```
`"SKU-INTEGRATION-TEST-"` (21 karakter) + `Guid.NewGuid():N` (32 karakter, tire olmadan) = **53 karakter** — ama `CreateProductRequest.Sku`'da Day 12'den beri duran `[StringLength(50)]` kısıtlaması var! Bu yüzden `POST /api/products` gerçekten `400 Bad Request` döndü — bu, uydurulmuş bir demo değil, testi ilk yazdığımızda **gerçekten** karşılaştığımız bir hata.

**Düzeltme:**
```csharp
var uniqueSku = $"SKU-IT-{Guid.NewGuid():N}"[..30];
```
Daha kısa bir önek (`"SKU-IT-"`, 7 karakter) + `[..30]` ile toplamın kesinlikle 50'yi aşmaması garanti edildi.

---

## Bağımsız görev — Berkan tarafından yazıldı, ilk denemede doğru

```csharp
[Fact]
public async Task GetAll_NoToken_ReturnsOk()
{
    var client = _factory.CreateClient();

    var response = await client.GetAsync("/api/products");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

Mevcut testlerdeki deseni doğru şekilde uyguladı: `Delete_NoToken_ReturnsUnauthorized`'daki `client.DeleteAsync(...)` yerine `GetAll` için doğru HTTP metodu ve adresi (`client.GetAsync("/api/products")`) seçti, doğru beklentiyi (`HttpStatusCode.OK`, çünkü `GetAll`'da hiç `[Authorize]` yok) yazdı. İlk çalıştırmada geçti.

---

## Doğrulanan davranış

```
dotnet build/test (StockPilot) → 0 Hata, 0 Uyari, 26/26 basarili (3 yeni entegrasyon testi + 1 bagimsiz gorev)
dotnet test (RoadmapOS)        → 8/8 (etkilenmedi)

Yeni testler GERCEK HTTP pipeline'indan geciyor:
  Delete_NoToken_ReturnsUnauthorized              → gercek 401
  Delete_EmployeeToken_ReturnsForbidden            → gercek 403
  Delete_AdminToken_DeletesRealProductThroughThePipeline → gercek 204

Veritabani kontrolu: testler kendi olusturduklari gecici urunu kendileri
siliyor — toplam urun sayisi hala 7, hicbir kalinti yok.
```

**Bugün bilerek yapılmayan:** Bu testler hâlâ **gerçek** `StockPilotDb` veritabanına karşı çalışıyor (aynı `appsettings.Development.json`, aynı `localhost\SQLEXPRESS`). Gerçek, izole bir test veritabanı (in-memory ya da Testcontainers ile ayrı bir SQL Server konteyneri) kurmak — "test-database isolation" — Hafta 6'nın ayrı, ileride ele alınacak bir konusu.
