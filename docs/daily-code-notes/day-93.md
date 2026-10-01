# Day 93 — Kod Notları

Faz 5, Hafta 18, Gün 93. Konu: **Backend'de JWT üretimi — login endpoint'i**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Dünden hatırlarsan:** "Kimlik" dediğimiz şey, her istekte elle gönderdiğimiz `X-Organization-Id`/`X-Employee-Id` header'larıydı — hiçbir doğrulama, hiçbir "giriş" yoktu, istersen `X-Employee-Id: 999` yazıp başka birinin kimliğine bürünebilirdin (gerçek bir güvenlik açığı, ama bu projede hep bilerek ertelenmişti).

**Bugün ne kuruyoruz:** Gerçek bir "giriş" (login) endpoint'i — kullanıcı bir kere giriş yapıyor, karşılığında İMZALI bir "kimlik kartı" (JWT) alıyor. **ÇOK ÖNEMLİ bir netleştirme:** Bugün bu kartı HİÇBİR YERDE henüz ZORUNLU kılmıyoruz — eski `X-Organization-Id` mekanizması AYNEN duruyor. Bugün sadece kartın ÜRETİMİNİ kuruyoruz; kartı GÖSTERMEK zorunlu hâle gelmesi (Angular'ın bunu otomatik eklemesi, backend'in bunu kontrol etmesi) sonraki günlerin konusu.

**Neden şifre yok, bu güvenli mi:** HAYIR, güvenli değil, ve bunu AÇIKÇA söylüyorum — bugün sadece `employeeId` veriyoruz, "ben buyum" diye hiçbir kanıt istenmiyor. Gerçek bir sistemde bir şifre (hash'lenmiş hâliyle veritabanında saklanan) kontrol edilirdi. Bugünün amacı JWT'nin KENDİSİNİ (üretimi, imzalanması, içeriği) öğrenmek — gerçek kimlik doğrulamayı değil.

---

## 1. Paket ve config

```bash
dotnet add src/FieldOps.Api package Microsoft.AspNetCore.Authentication.JwtBearer
```
```json
// appsettings.Development.json
"Jwt": {
  "Issuer": "FieldOps.Api",
  "SigningKey": "day-93-demo-signing-key-not-a-real-secret-change-me"
}
```
`SigningKey` — token'ı imzalarken VE doğrularken kullanılan gizli bir metin. Burada DÜZ METİN olarak, kaynak kontrolünde duran bir dosyada — bu KASITLI bir demo basitleştirmesi, gerçek üretimde KESİNLİKLE bir "secret manager"da tutulur.

---

## 2. `LoginRequest.cs` / `LoginResponse.cs`

```csharp
public record LoginRequest([Required] int EmployeeId);
// record -- C#'ın değişmez (immutable), değer-eşitliğine sahip veri
// taşıyıcısı (Day 11'den beri tanıdık bir yapı).
// [Required] -- bu alan boş/eksik gelirse, ASP.NET Core'un [ApiController]
// özelliği OTOMATİK olarak 400 Bad Request döner (Day 13'ten beri tanıdık).
// int EmployeeId -- SADECE BİR alan var -- şifre YOK, bilerek.

public record LoginResponse(string Token, int EmployeeId, int OrganizationId, string Role);
// Bu, Angular'a GERİ dönecek cevabın şekli. Token (gerçek JWT metni),
// EmployeeId/OrganizationId/Role (Angular'ın hemen kullanabileceği birkaç
// bilgiyi düz metin olarak dönüyoruz -- Angular'ın token'ı "çözmesine"
// (decode) gerek kalmasın diye, bugünlük).
```

---

## 3. `AuthController.cs` — token'ın GERÇEKTEN üretildiği yer

```csharp
using System.IdentityModel.Tokens.Jwt;
// JwtSecurityToken / JwtSecurityTokenHandler -- JWT'yi GERÇEKTEN oluşturan
// ve metne çeviren sınıflar buradan geliyor.
using System.Security.Claims;
// Claim / ClaimTypes -- token'ın içine koyacağımız "bilgi parçaları" buradan.
using System.Text;
// Encoding.UTF8 -- imzalama anahtarını (bir string) ham byte'lara çevirmek
// için kullanıyoruz, çünkü kriptografik fonksiyonlar string değil byte ister.
using FieldOps.Api.Models;
using FieldOps.Modules.Employees;
// IEmployeeDirectory -- dün yazdığımız AuthController'ın, çalışanı bulmak
// için kullandığı, ZATEN VAR OLAN arayüz (yeni bir şey eklemedik).
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
// SigningCredentials / SymmetricSecurityKey / SecurityAlgorithms -- token'ı
// İMZALAMAK için gereken sınıflar buradan.

namespace FieldOps.Api.Controllers;

[ApiController]
// [ApiController] -- bu sınıfın bir Web API controller'ı olduğunu belirten,
// otomatik model-doğrulama/400 davranışı kazandıran attribute (Day 11'den
// beri tanıdık).
[Route("api/auth")]
// Bu controller'daki TÜM action'ların adresi "api/auth" ile BAŞLAYACAK.
public class AuthController : ControllerBase
{
    private readonly IEmployeeDirectory _employeeDirectory;
    private readonly IConfiguration _configuration;
    // IConfiguration -- appsettings.json/appsettings.Development.json
    // dosyalarını OKUMAMIZI sağlayan, ASP.NET Core'un kendi yerleşik servisi.
    // Buradan Jwt:SigningKey/Jwt:Issuer değerlerini okuyacağız.

    public AuthController(IEmployeeDirectory employeeDirectory, IConfiguration configuration)
    {
        _employeeDirectory = employeeDirectory;
        _configuration = configuration;
    }
    // constructor -- Day 40'tan beri tanıdık desen: ihtiyaç duyulan
    // servisler, Dependency Injection ile constructor üzerinden alınıyor.

    [HttpPost("login")]
    // [HttpPost("login")] -- bu action, SADECE "POST api/auth/login"
    // isteklerine cevap verir.
    public ActionResult<LoginResponse> Login(LoginRequest request)
    {
        // Day 93's one real simplification, stated plainly: no password or
        // any other credential is checked here — knowing a valid EmployeeId
        // is treated as sufficient "proof" for this demo. A real login would
        // verify a hashed password (or delegate to a real identity provider)
        // before ever reaching the point of issuing a token.
        var employee = _employeeDirectory.GetById(request.EmployeeId);
        // GetById -- ZATEN VAR OLAN metot (yeni bir sorgu yazmadık).
        // Çalışan bulunamazsa null döner.
        if (employee is null)
        {
            return NotFound($"Employee {request.EmployeeId} does not exist.");
            // 404 Not Found -- şifre kontrolü YOK, SADECE çalışan VAR MI
            // diye bakılıyor.
        }

        var signingKey = _configuration["Jwt:SigningKey"] ?? "fieldops-dev-only-fallback-signing-key-do-not-use-in-production";
        // _configuration["Jwt:SigningKey"] -- appsettings.Development.json'daki
        // değeri okuyor. ?? ile bir yedek (fallback) değer veriyoruz --
        // config hiç ayarlanmamışsa (örn. test ortamında) uygulama ÇÖKMESİN
        // diye (Day 67'deki RabbitMq:HostName'in AYNI deseni).
        var issuer = _configuration["Jwt:Issuer"] ?? "FieldOps.Api";
        // issuer -- token'ı KİMİN ürettiğini belirten bir kimlik (burada
        // audience olarak da AYNI değeri kullanıyoruz, çünkü token'ı
        // doğrulayacak olan da AYNI uygulama -- FieldOps.Api).

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, employee.Id.ToString()),
            // "Bu token, GERÇEKTEN şu çalışana ait" diyen claim.
            new Claim("organizationId", employee.OrganizationId.ToString()),
            // Kendi SEÇTİĞİMİZ bir isimle (claim türünün string olarak
            // serbestçe yazılabildiğine dikkat) -- "bu çalışan HANGİ
            // organizasyona ait" bilgisi.
            new Claim(ClaimTypes.Role, employee.Role.ToString()),
            // "Bu çalışanın ROLÜ ne" -- ileride "role-aware screens"
            // (role'e göre farklı ekranlar) konusunda KULLANILACAK bir claim.
        };
        // claim -- token'ın İÇİNDEKİ tek bir bilgi parçası. Yukarıdaki üç
        // satır birlikte "bu token, Organization 1'deki, Admin rolündeki,
        // 1 numaralı çalışana ait" diyor.

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            // signingKey (bir string) ham byte'lara çevrilip, bir
            // "SymmetricSecurityKey" nesnesine sarılıyor -- imzalama
            // fonksiyonlarının beklediği GERÇEK format bu.
            SecurityAlgorithms.HmacSha256);
            // Hangi KRİPTOGRAFİK algoritmayla imzalanacağı -- HMAC-SHA256,
            // JWT'lerde en yaygın kullanılan, simetrik anahtarlı algoritma.
        // SymmetricSecurityKey -- aynı anahtar HEM imzalamak HEM doğrulamak için
        // kullanılıyor (asimetrik/public-private key çifti DEĞİL -- o, OAuth gibi
        // üçüncü taraf senaryolarında gerekir, burada tek bir sunucu kendi
        // ürettiğini kendi doğruluyor, simetrik yeterli).

        var token = new JwtSecurityToken(
            issuer: issuer,
            // Token'ın "kim ürettiğini" söyleyen alan (payload'a yazılır).
            audience: issuer,
            // Token'ın "kimin için" üretildiği -- bugün issuer ile AYNI,
            // çünkü aynı uygulama hem üretiyor hem (ileride) doğrulayacak.
            claims: claims,
            // Yukarıda hazırladığımız üç claim, token'ın İÇİNE gömülüyor.
            expires: DateTime.UtcNow.AddHours(1),
            // Token, ŞU ANDAN 1 saat sonra GEÇERSİZ olacak -- bu tarih de
            // token'ın içine (payload'a) yazılıyor.
            signingCredentials: credentials);
            // Token'ı GERÇEKTEN imzalayacak anahtar+algoritma.

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        // JwtSecurityTokenHandler -- JWT'leri okuyup yazabilen yardımcı sınıf.
        // WriteToken -- yukarıda hazırladığımız JwtSecurityToken NESNESİNİ,
        // GERÇEK "header.payload.signature" formatındaki metin hâline (üç
        // nokta ile ayrılmış) çeviriyor -- İŞTE bu, Angular'a dönen GERÇEK
        // token string'i.

        return Ok(new LoginResponse(tokenString, employee.Id, employee.OrganizationId, employee.Role.ToString()));
        // 200 OK ile, token'ı VE birkaç düz-metin bilgiyi birlikte dönüyoruz.
    }
}
```

---

## 4. `Program.cs` — token'ı DOĞRULAYACAK makineyi kaydetmek (henüz kullanılmıyor)

```csharp
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"] ?? "fieldops-dev-only-fallback-signing-key-do-not-use-in-production";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "FieldOps.Api";
// AuthController'daki İLE AYNI mantık -- config'ten okunuyor, yoksa yedek.
// (İleride bu tekrar, ortak bir yere taşınabilir -- bugünlük KASITLI olarak
// iki yerde ayrı ayrı duruyor, erken bir soyutlama eklemedik.)

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    // AddAuthentication(...) -- "bu uygulama kimlik doğrulama (authentication)
    // KULLANACAK" diye DI container'a kaydediyoruz. Parametre, VARSAYILAN
    // şemanın "JWT Bearer" olduğunu belirtiyor (başka şemalar da olabilirdi,
    // örn. Cookie tabanlı -- biz SADECE JWT kullanıyoruz).
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = jwtIssuer,
            // Gelen token'ın "issuer" alanının, BİZİM beklediğimiz değere
            // (jwtIssuer) GERÇEKTEN eşit olup olmadığını kontrol et.
            ValidateAudience = true, ValidAudience = jwtIssuer,
            // Aynı kontrol, "audience" alanı için.
            ValidateLifetime = true,
            // Token'ın "expires" alanına bakıp, SÜRESİ DOLMUŞSA REDDET.
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            // Gelen token'ın İMZASININ, BİZİM anahtarımızla GERÇEKTEN
            // doğrulanıp doğrulanamadığını kontrol et -- biri token'ı
            // DEĞİŞTİRİP imzayı taklit etmeye çalışırsa, bu kontrol
            // BAŞARISIZ olur.
        };
    });
```
```csharp
app.UseAuthentication();   // UseAuthorization'dan HEMEN ÖNCE
// Bu middleware, GELEN her isteğin Authorization header'ına bakıp, "geçerli
// bir Bearer token var mı" diye kontrol ediyor -- varsa, içindeki claim'leri
// HttpContext.User'a yazıyor.
app.UseAuthorization();
// Bu middleware, "bu kullanıcının bu action'a erişim YETKİSİ var mı" diye
// kontrol ediyor -- ama henüz HİÇBİR action [Authorize] ile işaretlenmediği
// için, bugün HİÇBİR ŞEYİ engellemiyor.
```
Bu kayıt, "biri Authorization header'ında bir Bearer token gönderirse, onu doğrula" diyen bir ALTYAPI. Ama bugün HİÇBİR controller/action `[Authorize]` ile işaretlenmedi — yani bu altyapı şu an hiçbir şeyi ENGELLEMİYOR, sadece HAZIR bekliyor. `app.UseAuthentication()`'ın `UseAuthorization()`'dan önce gelmesi önemli: önce "bu kim" (authentication) belirlenmeli, sonra "bunun yetkisi var mı" (authorization) kontrol edilebilir.

---

## Regresyon (Day 93)

```
dotnet build FieldOps.slnx -> 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx  -> 65/65 geçti (20 saniyede -- Docker cache sıcaktı)
```

## Canlı doğrulama

```
curl -X POST .../api/auth/login -d '{"employeeId":1}'
  -> 200, gerçek bir JWT ("eyJhbGc...") + {employeeId:1, organizationId:1, role:"Admin"}

Token'ın orta parçası (payload) elle base64 decode edildi:
  {"...nameidentifier":"1","organizationId":"1","...role":"Admin","exp":...,"iss":"FieldOps.Api","aud":"FieldOps.Api"}
  -> claim'ler GERÇEKTEN doğru çıktı

curl -X POST .../api/auth/login -d '{"employeeId":9999}'
  -> 404 (olmayan çalışan için doğru davranış)
```
**Küçük bir bulgu:** `ClaimTypes.NameIdentifier`/`ClaimTypes.Role`, token içinde KISA isim değil, UZUN bir XML namespace URI'si olarak kodlanıyor (örn. `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`) — .NET'in bilinen bir varsayılanı, bugün zararsız (hiçbir yer bu claim'leri henüz GERİ okumuyor) ama Angular ileride token'ı decode ederse hatırlanması gereken bir detay.

## Demo basitleştirmesi vs. üretim gereksinimi

* **En büyük basitleştirme:** şifre/kimlik doğrulama YOK, sadece `employeeId` yeterli. Gerçek üretimde hash'lenmiş şifre veya üçüncü taraf kimlik sağlayıcı gerekir.
* İmzalama anahtarı kaynak kodunda düz metin — gerçek üretimde bir secret manager'da tutulur.
* Token 1 saat sonra geçersiz oluyor ama "refresh token" (süre dolunca yeniden giriş yapmadan token yenileme) mekanizması yok — bugünün kapsamı dışı.
* Hiçbir endpoint `[Authorize]` ile korunmuyor henüz — token üretiliyor ama HİÇBİR YERDE zorunlu değil.
