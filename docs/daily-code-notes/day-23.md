# Day 23 — Kod Notları

Faz 2, Hafta 5, Gün 23 — Hafta 5'in ilk günü. Konu: Authentication vs Authorization, ilk JWT (JSON Web Token).

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. Yeni paket: `Microsoft.AspNetCore.Authentication.JwtBearer`

**Neden:** ASP.NET Core'un JWT'leri okuyup doğrulayabilmesi için gereken hazır middleware/altyapı bu pakette geliyor — Day 4'te EF Core için `Microsoft.EntityFrameworkCore.SqlServer` paketini eklememizle aynı mantık: framework'ün kendisi bu özelliği bilmiyor, ayrı bir paketle geliyor.

---

## 2. `appsettings.Development.json` — JWT ayarları

```json
"Jwt": {
    "Issuer": "StockPilot.Api",
    "Audience": "StockPilot.Api.Clients",
    "Key": "dev-only-signing-key-not-for-production-use-1234567890",
    "ExpiryMinutes": 60
}
```

**Neden burada, açıkça "dev-only" yazıyor:** `Key`, JWT'lerin imzalanmasında kullanılan gizli anahtar — bunu bilen herkes sahte token üretebilir. Gerçek bir üretim sisteminde bu asla `appsettings.json`'da düz metin olarak durmaz (user-secrets, Azure Key Vault, ortam değişkeni gibi güvenli bir yerde tutulur). Bugün bu, Day 4'ün bağlantı dizesi gibi bilinçli bir basitleştirme — isim bile bunu itiraf ediyor.

---

## 3. `Models/LoginRequest.cs` ve `Models/LoginResponse.cs`

```csharp
public record LoginRequest([Required] string Username, [Required] string Password);
public record LoginResponse(string Token, DateTime ExpiresAtUtc);
```

Şimdiye kadarki tüm DTO'larla (`CreateProductRequest`, `ProductDto` vb.) aynı desen — giriş ve çıkış için ayrı, açık DTO'lar.

---

## 4. `Controllers/AuthController.cs` — asıl kimlik doğrulama mantığı

```csharp
private const string DemoUsername = "admin";
private static readonly PasswordHasher<object> PasswordHasher = new();
private static readonly string DemoPasswordHash = PasswordHasher.HashPassword(null!, "Passw0rd!");
```

**Neden tek, sabit bir kullanıcı:** Bugünün konusu "authentication mekanizmasının kendisi" — gerçek bir kullanıcı yönetimi (kayıt, birden fazla kullanıcı, kendi tablosu) StockPilot'un roadmap'inde ayrı bir iş değil, sadece bu mekanizmayı öğretebilmek için minimum bir "biri var" gerekiyordu. Bu, Day 5'in `SkillFormModel`'i gibi — mekanizmayı kanıtlamak için en küçük gerçek örnek.

**Neden şifre düz metin karşılaştırılmıyor:** `PasswordHasher<T>` (ASP.NET Core Identity'nin bağımsız kullanılabilen bir parçası), şifreyi geri döndürülemez bir şekilde "hash"liyor. `HashPassword(null!, "Passw0rd!")` çağrısı, sınıf ilk kullanıldığında **bir kere** çalışıp `DemoPasswordHash`'i dolduruyor — `static readonly` olduğu için bu hesaplama uygulama ömrü boyunca bir kez yapılıyor.

```csharp
[HttpPost("login")]
[AllowAnonymous]
public ActionResult<LoginResponse> Login(LoginRequest request)
{
    var isValidPassword = request.Username == DemoUsername &&
        PasswordHasher.VerifyHashedPassword(null!, DemoPasswordHash, request.Password) == PasswordVerificationResult.Success;

    if (!isValidPassword)
    {
        return Unauthorized("Invalid username or password.");
    }
    ...
```

**`[AllowAnonymous]` neden var:** Şu an hiçbir global `[Authorize]` kuralı yok, yani teknik olarak gerekli değil — ama açıkça "bu endpoint bilerek herkese açık, unutulmadı" demek için yazıldı. İleride (Day 24+) bir global kısıtlama eklenirse, bu satır login'in yanlışlıkla kilitlenmesini önler.

```csharp
var claims = new[]
{
    new Claim(JwtRegisteredClaimNames.Sub, request.Username),
    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
};

var token = new JwtSecurityToken(
    issuer: jwtSection["Issuer"],
    audience: jwtSection["Audience"],
    claims: claims,
    expires: expiresAtUtc,
    signingCredentials: signingCredentials);
```

**JWT'nin üç parçası (`header.payload.signature`):** `JwtSecurityToken` nesnesi bunları bizim için birleştiriyor:
- **Header** — hangi algoritmayla imzalandığı (`HmacSha256`).
- **Payload** — `claims` (kullanıcı hakkındaki iddialar: kim olduğu — `Sub` — ve bu token'a özel benzersiz kimlik — `Jti`) + `issuer`/`audience`/`expires`.
- **Signature** — header+payload'ın, `signingCredentials`'taki gizli anahtarla imzalanmış hali. Bu imza sayesinde, token'ı alan taraf (bizim API'miz) içeriğin **hiç değişmediğini** matematiksel olarak doğrulayabiliyor — veritabanına gitmeden.

`new JwtSecurityTokenHandler().WriteToken(token)` bu üç parçayı base64-encoded, nokta ile ayrılmış tek bir string'e çeviriyor (`eyJhbGc...`ile başlayan, curl çıktısında gördüğümüz şey).

---

## 5. `Program.cs` — authentication şemasının kayıt edilmesi

```csharp
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!)),
            ValidateLifetime = true
        };
    });
builder.Services.AddAuthorization();
```

**Neden bu ayarlar tek tek `true`:** Her biri, gelen bir token için ayrı bir kontrol açıyor — "imza gerçekten bizim anahtarımızla mı atılmış" (`ValidateIssuerSigningKey`), "bu token gerçekten bizim API'miz için mi üretilmiş" (`ValidateAudience`/`ValidateIssuer` — kötü niyetli birinin BAŞKA bir sistem için üretilmiş geçerli bir token'ı bize karşı kullanmasını engelliyor), "süresi dolmuş mu" (`ValidateLifetime`). Bunları `false` bırakmak, o kontrolü tamamen atlamak demek — güvenlik açısından tehlikeli.

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

**Sıra neden önemli:** `UseAuthentication()`, `Authorization: Bearer <token>` header'ını okuyup token'ı doğrulayan ve `HttpContext.User`'ı dolduran şey. `UseAuthorization()` ise sadece "`HttpContext.User` zaten dolu mu, `[Authorize]` gereksinimini karşılıyor mu" diye **kontrol ediyor** — kendisi hiçbir token doğrulaması yapmıyor. `UseAuthorization()` önce gelseydi, henüz doldurulmamış boş bir `User`'a bakıp her zaman "kimliksiz" derdi — token ne olursa olsun her `[Authorize]`'lı istek 401 olurdu.

---

## 6. `Controllers/ProductsController.cs` — ilk korunan endpoint

```csharp
[Authorize]
[HttpDelete("{id}")]
public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
```

Kodda tek satırlık bir değişiklik ama anlamı büyük: artık `DELETE` çağıran biri, geçerli bir JWT göstermek zorunda. `GetAll`/`GetById`/`Create`/`Update`/`BulkCreate` bugün **bilerek** açık bırakıldı — hepsini birden kilitlemek yerine tek bir örnek üzerinden mekanizmayı kanıtlamak bugünün kapsamıydı.

**Fark edilmesi gereken dürüst bir sınır:** `ProductsControllerTests.cs`'teki `Delete_ExistingId_RemovesProductAndReturnsNoContent` testi, controller metodunu **doğrudan** çağırıyor (`await controller.Delete(2)`) — hiçbir HTTP isteği, hiçbir middleware pipeline'ı devreye girmiyor. `[Authorize]`, ASP.NET Core'un **middleware pipeline'ında** (`UseAuthorization()`) uygulanan bir kontrol — bu yüzden bu testler `[Authorize]` eklendikten sonra bile **hiçbir değişiklik olmadan geçmeye devam ediyor**. Gerçek bir "401 dönüyor mu" testi, ancak `WebApplicationFactory` gibi tüm pipeline'ı ayağa kaldıran bir entegrasyon testiyle mümkün — bu, Week 6'nın konusu (`WebApplicationFactory`, integration testing), bugünün kapsamı dışında.

---

## Canlı kanıt

```
DELETE /api/products/999  (token YOK)                    → HTTP 401
POST   /api/auth/login    (yanlis sifre)                 → HTTP 401
POST   /api/auth/login    (dogru: admin/Passw0rd!)       → HTTP 200 + gercek bir JWT
DELETE /api/products/999  (GECERLI token)                → HTTP 404 (401 DEGIL — auth'u gecti,
                                                             ama boyle bir urun yok, normal is mantigi calisti)
POST   /api/products      (gecici SKU-AUTH-TEST urunu)   → HTTP 201, id=18
DELETE /api/products/18   (GECERLI token)                → HTTP 204 (gercek, basarili silme)

dotnet test (StockPilot) → 14/14 (degisiklik yok — [Authorize] middleware
                             pipeline'inda calisiyor, doğrudan controller
                             cagrisi yapan unit testleri etkilemiyor)
dotnet test (RoadmapOS)  → 8/8 (etkilenmedi)
```
