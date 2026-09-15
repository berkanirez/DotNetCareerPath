# Day 25 — Kod Notları

Faz 2, Hafta 5, Gün 25. Konu: Role-Based Authorization (RBAC) — `401` vs `403` farkı.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `Controllers/AuthController.cs` — tek kullanıcıdan iki role

```csharp
private static readonly PasswordHasher<object> PasswordHasher = new();
private static readonly Dictionary<string, (string PasswordHash, string Role)> DemoUsers = new()
{
    ["admin"] = (PasswordHasher.HashPassword(null!, "Passw0rd!"), "Admin"),
    ["employee"] = (PasswordHasher.HashPassword(null!, "Employee123!"), "Employee")
};
```

**Neden iki kullanıcı:** RBAC'ı gerçekten kanıtlayabilmek için en az iki **farklı** role ihtiyaç var — tek kullanıcıyla "yetkisi olmayan biri engelleniyor mu" hiç gösterilemez. `Dictionary<string, (string PasswordHash, string Role)>` — anahtar kullanıcı adı, değer ise (şifre hash'i, rol) ikilisi. `DemoUsers` sözlüğü, `PasswordHasher` alanından **sonra** tanımlanmalı, çünkü içindeki `HashPassword(...)` çağrıları `PasswordHasher`'ın zaten var olmasını gerektiriyor (C#'ta static alanlar yazıldıkları sırayla ilklendirilir).

```csharp
public ActionResult<LoginResponse> Login(LoginRequest request)
{
    var isValidLogin = DemoUsers.TryGetValue(request.Username, out var user) &&
        PasswordHasher.VerifyHashedPassword(null!, user.PasswordHash, request.Password) == PasswordVerificationResult.Success;

    if (!isValidLogin)
    {
        return Unauthorized("Invalid username or password.");
    }

    var (accessToken, expiresAtUtc) = GenerateAccessToken(request.Username, user.Role);
    ...
```

`DemoUsers.TryGetValue(request.Username, out var user)` — Day 24'te gördüğümüz aynı "Try" deseni: kullanıcı adı sözlükte varsa `true` döner ve `user` değişkenine `(PasswordHash, Role)` ikilisini yazar. `&&` sayesinde, kullanıcı hiç yoksa `PasswordHasher.VerifyHashedPassword(...)` çağrısı **hiç çalışmıyor** (kısa devre değerlendirme — C#, `&&`'in solu `false` ise sağını hiç değerlendirmez) — bu da olmayan bir kullanıcı için gereksiz bir hesaplama yapmamızı önlüyor.

```csharp
[HttpPost("refresh")]
public ActionResult<LoginResponse> Refresh(RefreshTokenRequest request)
{
    if (!_refreshTokenStore.TryConsume(request.RefreshToken, out var username))
    {
        return Unauthorized("Invalid or already-used refresh token.");
    }

    var role = DemoUsers[username].Role;
    var (accessToken, expiresAtUtc) = GenerateAccessToken(username, role);
    ...
```

**Neden `role` burada yeniden aranıyor:** `IRefreshTokenStore`, sadece `(kullanıcı adı, süre dolma zamanı)` saklıyor — rolü hiç bilmiyor (Day 24'te böyle tasarlanmıştı). Bu yüzden `Refresh` sırasında yeni bir access token üretirken, rolü **yeniden** `DemoUsers` sözlüğünden okumamız gerekiyor. `DemoUsers[username]` (köşeli parantez, `TryGetValue` değil) — burada güvenle kullanılabilir çünkü `username`, `TryConsume`'un başarıyla döndürdüğü, zaten var olduğunu bildiğimiz bir kullanıcı adı.

```csharp
private (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(string username, string role)
{
    ...
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, username),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, role)
    };
    ...
```

**Neden `ClaimTypes.Role` (özel bir sabit) ve rastgele bir string ("role") değil:** ASP.NET Core'un `[Authorize(Roles = "...")]` mekanizması, bir isteğin rolünü kontrol ederken **tam olarak bu claim tipine** bakıyor (`System.Security.Claims.ClaimTypes.Role`, aslında uzun bir URI string'i: `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`). Bunun yerine kendi uydurduğumuz `"role"` gibi bir isim kullansaydık, `[Authorize(Roles=...)]` bunu **asla bulamazdı** — framework'ün beklediği tam isimle eşleşmesi gerekiyor.

---

## 2. `Controllers/ProductsController.cs` — rol kısıtlaması

```csharp
[Authorize(Roles = "Admin")]
[HttpDelete("{id}")]
public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
```

Day 23'teki `[Authorize]` (sadece "kimliklenmiş mi") artık `[Authorize(Roles = "Admin")]` (hem kimliklenmiş HEM rolü `Admin` mi). `UseAuthorization()` middleware'i, `HttpContext.User`'ın (Day 24'te authentication tarafından doldurulan) `ClaimTypes.Role` claim'ine bakıyor — değeri `"Admin"` değilse, isteği reddediyor. **Reddetme şekli önemli:** kullanıcı zaten kimliklendiği (token geçerli, kim olduğu biliniyor) için bu bir `401` değil, bir **`403 Forbidden`** — "seni tanıyorum ama buna iznin yok" anlamında.

---

## 3. `tests/StockPilot.Api.Tests/AuthControllerTests.cs` — yeni bir test dosyası

```csharp
private static AuthController CreateController()
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "TestIssuer",
            ...
        })
        .Build();

    return new AuthController(configuration, new InMemoryRefreshTokenStore());
}
```

`AuthController`, gerçek bir `IConfiguration` istiyor (constructor injection) — testte gerçek `appsettings.json`'ı okumak yerine, `ConfigurationBuilder().AddInMemoryCollection(...)` ile **hafızada, sadece test için** küçük bir konfigürasyon nesnesi kuruyoruz. Bu, EF Core'u mock'lamamaya benzer bir prensip: gerçek `IConfiguration` API'sini kullanıyoruz, sadece verisi gerçek dosyadan değil bellekten geliyor.

```csharp
[Theory]
[InlineData("admin", "Passw0rd!", "Admin")]
[InlineData("employee", "Employee123!", "Employee")]
public void Login_ValidCredentials_IssuesTokenWithCorrectRoleClaim(string username, string password, string expectedRole)
{
    var controller = CreateController();
    var request = new LoginRequest(username, password);

    var result = controller.Login(request);

    var response = Assert.IsType<LoginResponse>(((OkObjectResult)result.Result!).Value);
    var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);
    var roleClaim = Assert.Single(jwt.Claims, c => c.Type == ClaimTypes.Role);
    Assert.Equal(expectedRole, roleClaim.Value);
}
```

**Yeni bir şey — `[Theory]`/`[InlineData]`:** Şimdiye kadar hep `[Fact]` kullanmıştık (tek bir senaryo). `[Theory]`, **aynı test mantığını farklı girdilerle birden fazla kez** çalıştırmamızı sağlıyor — her `[InlineData(...)]` satırı, testin ayrı bir çalıştırılışı. Burada aynı testi hem `admin` hem `employee` için, tek kod tekrarı olmadan çalıştırıyoruz.

**`new JwtSecurityTokenHandler().ReadJwtToken(response.Token)`:** `Login`'in ürettiği gerçek JWT string'ini geri **açıyor** (decode ediyor) — imza doğrulamadan, sadece içeriği okumak için (`ReadJwtToken`, `ValidateToken`'ın aksine imzayı kontrol etmiyor, bu yüzden test için daha basit ve yeterli — biz zaten kendi ürettiğimiz token'ı okuyoruz, güvenmemiz gereken bir "dışarıdan gelen" token değil).

`Assert.Single(jwt.Claims, c => c.Type == ClaimTypes.Role)` — `jwt.Claims` listesinde `Type`'ı `ClaimTypes.Role` olan **tam olarak bir tane** eleman olduğunu doğruluyor (birden fazla ya da hiç olsaydı test başarısız olurdu) ve o elemanı geri döndürüyor. `Assert.Equal(expectedRole, roleClaim.Value)` — o claim'in değerinin, beklediğimiz role (`"Admin"` ya da `"Employee"`) eşit olduğunu kanıtlıyor.

---

## Canlı kanıt

```
POST /api/products (gecici SKU-RBAC-TEST urunu)         → 201, id=21

Login (employee/Employee123!)                            → 200, role=Employee tasiyan JWT
DELETE /api/products/21  (employee token)                 → HTTP 403  (kimliklendi, ama yetkisiz)
GET    /api/products/21  (employee token)                 → HTTP 200  (kisitlama yok, urun hala duruyor)

Login (admin/Passw0rd!)                                   → 200, role=Admin tasiyan JWT
DELETE /api/products/21  (admin token)                     → HTTP 204  (basarili silme)

dotnet test (StockPilot) → 22/22 (4 yeni test: AuthControllerTests)
dotnet test (RoadmapOS)  → 8/8 (etkilenmedi)
Veritabani demo sonrasi temiz (7 orijinal urun).
```
