# Day 24 — Refresh Token Akışı: Satır Satır, Baştan Sona

Bu doküman, Day 24'te eklenen refresh token akışını **hiçbir satırı atlamadan**, gerçek koddan alınmış tam parçalarla anlatıyor. Yeni başlayan biri için yazıldı — her satırın ne işe yaradığı, yanında açıklanıyor.

## Önce birkaç temel kavram

- **Metot (fonksiyon):** Bir isim altında toplanmış, çağrıldığında çalışan kod parçası. `Login(...)` bir metot.
- **Parametre:** Metoda dışarıdan verilen bilgi. `Login(LoginRequest request)`'te `request`, dışarıdan gelen veri.
- **Dönüş değeri (`return`):** Metot işini bitirince "sonuç bu" diye geri verdiği şey.
- **Değişken (variable):** Bir veriye takılan etiket. `var accessToken = ...` → `accessToken` artık bir değeri tutan bir kutu.
- **`if` / `!`:** "Eğer şu doğruysa şunu yap." `!` ise "değilse/tersini al" demek — `!isValid` = "geçerli değilse".
- **Nesne / class:** Birden fazla bilgiyi bir arada tutan bir paket. `LoginRequest`, içinde `Username` ve `Password`'ü birlikte taşıyan bir class.
- **HTTP isteği/cevabı:** Client'ın (curl, tarayıcı) sunucuya gönderdiği bir mesaj (istek) ve sunucunun buna verdiği karşılık (cevap).

---

## ADIM 0 — İstek nereye gidiyor? (Routing)

```csharp
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
```

`[Route("api/[controller]")]` bu class'ın temel adresini belirliyor. `[controller]` yazan yere, class isminden "Controller" çıkarılıp otomatik yerleştiriliyor: `AuthController` → `Auth`. Temel adres: `api/auth`.

```csharp
[HttpPost("login")]
[AllowAnonymous]
public ActionResult<LoginResponse> Login(LoginRequest request)
```

- `[HttpPost("login")]` → "bu metot, `POST` isteğiyle `api/auth/login` adresine gelince çalışır."
- `[AllowAnonymous]` → "buraya girmek için önceden bir token gerekmiyor" (mantıklı, çünkü token almak için zaten buraya geliyoruz).
- `Login(LoginRequest request)` → dışarıdan bir `LoginRequest` alıyor, geriye `ActionResult<LoginResponse>` (ya bir hata ya da başarılıysa bir `LoginResponse`) döndürüyor.

`POST /api/auth/login` isteği `{"username":"admin","password":"Passw0rd!"}` gövdesiyle geldiğinde, ASP.NET Core bu JSON'u otomatik olarak şuna çeviriyor:

```csharp
public record LoginRequest([Required] string Username, [Required] string Password);
```

Artık elimizde `request.Username == "admin"` ve `request.Password == "Passw0rd!"` var.

---

## ADIM 1 — `Login` metodunun içi

```csharp
private const string DemoUsername = "admin";
private static readonly PasswordHasher<object> PasswordHasher = new();
private static readonly string DemoPasswordHash = PasswordHasher.HashPassword(null!, "Passw0rd!");
```

Bunlar metodun DIŞINDA, class'ın en üstünde — bir kere hazırlanıp orada duruyorlar:
- `DemoUsername = "admin"` — sabit bir metin.
- `PasswordHasher = new()` — şifreleri hash'leyen (geri döndürülemez şekilde karıştıran) bir yardımcı nesne.
- `DemoPasswordHash = ...HashPassword(null!, "Passw0rd!")` — `"Passw0rd!"`'ün hash'lenmiş hali burada saklanıyor, düz metin değil.

```csharp
public ActionResult<LoginResponse> Login(LoginRequest request)
{
    var isValidPassword = request.Username == DemoUsername &&
        PasswordHasher.VerifyHashedPassword(null!, DemoPasswordHash, request.Password) == PasswordVerificationResult.Success;
```

- `request.Username == DemoUsername` → gelen kullanıcı adı `"admin"` ile aynı mı?
- `&&` → "VE" — ikisi de doğru olmalı.
- `PasswordHasher.VerifyHashedPassword(...)` → "gelen düz-metin şifre, sakladığımız hash ile eşleşiyor mu?" diye kontrol ediyor.
- `== PasswordVerificationResult.Success` → sonuç "başarılı" mı?
- Satırın tamamı: "kullanıcı adı VE şifre doğru mu?" → `isValidPassword`'e `true`/`false` yazılıyor.

```csharp
    if (!isValidPassword)
    {
        return Unauthorized("Invalid username or password.");
    }
```

`!isValidPassword` → "doğru değilse". Yanlışsa metot burada duruyor, `401` dönüyor. Bizim örneğimizde şifre doğru, devam ediyoruz.

```csharp
    var (accessToken, expiresAtUtc) = GenerateAccessToken(request.Username);
```

`GenerateAccessToken("admin")` çağrılıyor. Bu metot **iki değer birden** döndürüyor (token + süre dolma zamanı). `var (accessToken, expiresAtUtc) = ...` bu ikiliyi tek satırda iki ayrı değişkene ayırıyor ("tuple deconstruction" — ikili paketi aç, parçalarını ayrı kutulara koy).

### `GenerateAccessToken`'ın içine girelim

```csharp
private (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(string username)
{
    var jwtSection = _configuration.GetSection("Jwt");
```

`(string Token, DateTime ExpiresAtUtc)` → bu metodun döndürdüğü şey tek bir değer değil, isimli iki parçalı bir paket. `_configuration.GetSection("Jwt")` → `appsettings.Development.json`'daki şu bölümü okuyor:

```json
"Jwt": {
    "Issuer": "StockPilot.Api",
    "Audience": "StockPilot.Api.Clients",
    "Key": "dev-only-signing-key-not-for-production-use-1234567890",
    "ExpiryMinutes": 60
}
```

```csharp
    var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
```

- `jwtSection["Key"]` → o uzun anahtar metnini okuyor. `!` → "bu asla null olmayacak, garanti ediyorum" (nullable reference type kuralı).
- `Encoding.UTF8.GetBytes(...)` → metni ham baytlara çeviriyor (imzalama baytlarla çalışır).
- `new SymmetricSecurityKey(...)` → bu baytları "imzalama anahtarı" nesnesine sarıyor.

```csharp
    var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
```

Anahtarı, hangi algoritmayla imzalanacağı bilgisiyle (`HmacSha256`) birleştirip imzalamaya hazır hale getiriyor.

```csharp
    var expiryMinutes = int.Parse(jwtSection["ExpiryMinutes"]!);
    var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiryMinutes);
```

- `jwtSection["ExpiryMinutes"]` metin olarak `"60"` döner — `int.Parse(...)` gerçek sayıya (`60`) çeviriyor.
- `DateTime.UtcNow` → şu anki tarih/saat (UTC).
- `.AddMinutes(60)` → şu ana 60 dakika ekliyor → "bu token 60 dakika sonra geçersiz olacak".

```csharp
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, username),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };
```

- `new[] { ... }` → bir **dizi (array)**: birden fazla şeyi sıralı tutan bir kutu, burada 2 elemanlı.
- `new Claim(Sub, username)` → "bu token kime ait" bilgisi (`"admin"`).
- `new Claim(Jti, Guid.NewGuid().ToString())` → bu token'a özel, rastgele üretilmiş benzersiz bir kimlik numarası. Her token'ın kendine ait bu numarası var, iki token asla aynı `Jti`'ye sahip olmuyor.

```csharp
    var token = new JwtSecurityToken(
        issuer: jwtSection["Issuer"],
        audience: jwtSection["Audience"],
        claims: claims,
        expires: expiresAtUtc,
        signingCredentials: signingCredentials);
```

`new JwtSecurityToken(...)` asıl JWT nesnesini oluşturuyor. `issuer: ..., audience: ..., ...` şekli **isimlendirilmiş argüman** — parametreleri sırayla değil isimleriyle veriyoruz, hangi değerin nereye gittiği çok net oluyor.

```csharp
    return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
}
```

- `new JwtSecurityTokenHandler()` → JWT okuyup yazabilen bir yardımcı nesne.
- `.WriteToken(token)` → oluşturduğumuz JWT nesnesini gerçek bir **metne** çeviriyor — curl çıktılarında gördüğümüz `eyJhbGc...` string burada ortaya çıkıyor.
- `return (tokenString, expiresAtUtc);` → iki parçalı paketi geri veriyor.

`Login`'e geri dönüyoruz — `accessToken` artık `eyJhbGc...` metnini, `expiresAtUtc` de saatini taşıyor.

---

## ADIM 2 — Refresh token üretimi

```csharp
    var refreshToken = _refreshTokenStore.Issue(request.Username);
```

`_refreshTokenStore`, constructor'da dışarıdan (DI ile) verilmiş bir nesne (`InMemoryRefreshTokenStore`). `.Issue("admin")` çağrılıyor.

### `Issue`'nun içine girelim

```csharp
private readonly ConcurrentDictionary<string, (string Username, DateTime ExpiresAtUtc)> _tokens = new();
private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

public string Issue(string username)
{
    var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    _tokens[token] = (username, DateTime.UtcNow.Add(Lifetime));
    return token;
}
```

- `_tokens` → bir **sözlük (dictionary)**: her "anahtar" bir "değer"e karşılık geliyor. Anahtar burada bir metin (refresh token), değer ise iki parçalı bir paket: `(kullanıcı adı, süre dolma zamanı)`.
- `ConcurrentDictionary` → normal sözlüğün "aynı anda birden fazla istekle güvenle kullanılabilen" özel hali.
- `Lifetime = TimeSpan.FromDays(7)` → "7 gün" anlamına gelen sabit bir süre.

Metodun içi:
- `RandomNumberGenerator.GetBytes(32)` → 32 rastgele, tahmin edilemez bayt üretiyor.
- `Convert.ToBase64String(...)` → bu baytları yazıya çevrilebilir bir metne dönüştürüyor (mesela `"QXd4Ej43..."`).
- `_tokens[token] = (username, ...)` → sözlüğe yeni bir satır ekliyor: `["QXd4Ej43..."] = ("admin", 7 gün sonrası)`.
- `return token;` → ürettiğimiz metni geri veriyor.

`Login`'e geri dönüyoruz — `refreshToken` artık `"QXd4Ej43..."` değerini taşıyor.

---

## ADIM 3 — Cevabın oluşturulup gönderilmesi

```csharp
    return Ok(new LoginResponse(accessToken, expiresAtUtc, refreshToken));
}
```

```csharp
public record LoginResponse(string Token, DateTime ExpiresAtUtc, string RefreshToken);
```

`Ok(...)` → "HTTP 200, gövdesinde bu paket olsun." Client'ın aldığı gerçek JSON:

```json
{
  "token": "eyJhbGc...AT1",
  "expiresAtUtc": "2026-09-15T15:00:00Z",
  "refreshToken": "QXd4Ej43..."
}
```

Client artık iki token'ı da (AT1, RT1) kendi tarafında saklıyor.

---

## ADIM 4 — Normal bir istek: `DELETE` ile access token kullanılıyor

```csharp
[Authorize]
[HttpDelete("{id}")]
public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
```

Client `DELETE /api/products/5` gönderirken header'a ekliyor: `Authorization: Bearer eyJhbGc...AT1`.

`Program.cs`'teki şu satırlar, istek `Delete`'e ulaşmadan ÖNCE devreye giriyor:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

- `UseAuthentication()` → `Authorization: Bearer ...` header'ını okuyor, imzayı doğruluyor, `exp` (süre dolma) değerine bakıyor. Yolundaysa `HttpContext.User`'ı ("bu isteği kim yaptı" bilgisi) dolduruyor.
- `UseAuthorization()` → `[Authorize]`'ı görüyor, `HttpContext.User` dolu mu diye kontrol ediyor. Doluysa `Delete`'e izin veriyor.

Token'ın süresi henüz dolmadıysa → `Delete` çalışır → ürün silinir → `204 No Content`.

---

## ADIM 5 — Access token'ın süresi doluyor

Aynı `DELETE` isteği, ama saat artık `exp` değerini (15:00:00) geçmiş.

`UseAuthentication()` token'ı okuyor, imza hâlâ doğru AMA `exp` geçmişte kalmış. `ClockSkew = TimeSpan.Zero` ayarımız yüzünden hiçbir tolerans yok — token kesinlikle geçersiz.

- `HttpContext.User` DOLDURULMUYOR.
- `UseAuthorization()`, `[Authorize]`'ın şartının sağlanmadığını görüyor.
- `Delete` metodu **hiç çalışmıyor** — `401 Unauthorized` dönüyor.

---

## ADIM 6 — Client, refresh token ile yenileme istiyor

```csharp
[HttpPost("refresh")]
[AllowAnonymous]
public ActionResult<LoginResponse> Refresh(RefreshTokenRequest request)
{
```

Client `POST /api/auth/refresh` gönderiyor: `{"refreshToken": "QXd4Ej43..."}`. Bu JSON şuna dönüşüyor:

```csharp
public record RefreshTokenRequest([Required] string RefreshToken);
```

Yani `request.RefreshToken == "QXd4Ej43..."`.

```csharp
    if (!_refreshTokenStore.TryConsume(request.RefreshToken, out var username))
    {
        return Unauthorized("Invalid or already-used refresh token.");
    }
```

`out var username` → "eğer başarılıysan gerçek kullanıcı adını da bana ver" — `username` burada, çağrı satırında ilk kez tanımlanıyor.

### `TryConsume`'un içine girelim

```csharp
public bool TryConsume(string refreshToken, out string username)
{
    username = string.Empty;

    if (!_tokens.TryRemove(refreshToken, out var entry))
    {
        return false;
    }

    if (entry.ExpiresAtUtc < DateTime.UtcNow)
    {
        return false;
    }

    username = entry.Username;
    return true;
}
```

- `username = string.Empty;` → C# kuralı: `out` parametreler metot bitmeden önce mutlaka bir değer almalı. Henüz gerçek değeri bilmediğimiz için boş string.
- `_tokens.TryRemove(refreshToken, out var entry)` → sözlükten `"QXd4Ej43..."` anahtarını arıyor VE bulursa **siliyor**, aynı anda. Bulduysa `entry`'ye `("admin", 7 gün sonrası)` koyuyor, `true` döner.
- `if (!_tokens.TryRemove(...))` → BULUNAMADIYSA: `return false;` — metot burada bitiyor.
- Bizim durumumuzda bulunuyor: `entry = ("admin", 7 gün sonrası)`.
- `entry.ExpiresAtUtc < DateTime.UtcNow` → süre dolma tarihi, şu andan ÖNCE mi? Hayır → bu `if` çalışmıyor.
- `username = entry.Username;` → `"admin"`'i `out` parametresine yazıyoruz.
- `return true;` → "başarılı".

**En önemli nokta:** `TryRemove` çağrıldığı an, `"QXd4Ej43..."` artık sözlükte YOK — silindi, bir daha asla bulunamayacak.

`Refresh`'e geri dönüyoruz — `TryConsume` `true` döndürdüğü için `Unauthorized(...)` satırı ÇALIŞMIYOR. `username` artık `"admin"`.

```csharp
    var (accessToken, expiresAtUtc) = GenerateAccessToken(username);
    var newRefreshToken = _refreshTokenStore.Issue(username);

    return Ok(new LoginResponse(accessToken, expiresAtUtc, newRefreshToken));
}
```

- `GenerateAccessToken("admin")` → ADIM 1'de incelediğimiz AYNI metot tekrar çalışıyor, yepyeni bir JWT üretiyor (`Jti` her seferinde rastgele olduğu için önceki token ile aynı olmuyor). Diyelim `"eyJhbGc...AT2"`, yeni süre `15:15:00`.
- `_refreshTokenStore.Issue("admin")` → ADIM 2'deki AYNI metot tekrar çalışıyor, yeni rastgele bir refresh token üretip sözlüğe ekliyor: diyelim `"7L6eRA..."`.
- `return Ok(new LoginResponse(...))` → client'a yeni çifti dönüyor: `{ token: "AT2", expiresAtUtc: 15:15:00, refreshToken: "7L6eRA..." }`.

Client eski `"QXd4Ej43..."`'yı atıyor, yeni `"7L6eRA..."`'yı saklıyor.

---

## ADIM 7 (kanıt) — Biri eski refresh token'ı tekrar kullanmaya çalışırsa

`POST /api/auth/refresh` `{"refreshToken": "QXd4Ej43..."}` (ADIM 6'da zaten kullanılmış eski token) tekrar gönderiliyor.

`TryConsume("QXd4Ej43...", out username)` tekrar çalışıyor:
- `_tokens.TryRemove("QXd4Ej43...", ...)` → bu anahtar ADIM 6'da sözlükten SİLİNMİŞTİ. Şimdi bulamıyor → `false`.
- `if (!false)` = `if (true)` → `return false;` çalışıyor.

`Refresh`'e geri dönüyoruz: `if (!false)` = `if (true)` → `Unauthorized("Invalid or already-used refresh token.")` çalışıyor. Client `401` alıyor.

---

## Özet — tek bakışta

| Adım | İstek | `_tokens` sözlüğünün durumu | Sonuç |
|---|---|---|---|
| Login | `POST /auth/login` | `RT1 eklendi` | AT1 + RT1 verildi |
| Normal kullanım | `DELETE` + AT1 | değişmedi | Başarılı (204) |
| Süre doldu | `DELETE` + AT1 | değişmedi | 401 |
| Refresh | `POST /auth/refresh` + RT1 | `RT1 silindi`, `RT2 eklendi` | AT2 + RT2 verildi |
| Devam | `DELETE` + AT2 | değişmedi | Başarılı (204) |
| Eski token tekrar | `POST /auth/refresh` + RT1 | RT1 zaten yok | 401 |

---

## Bonus — Token'a ekstra veri eklemek istersek (nickname, telefon numarası vb.)

Şu an token'ın içinde sadece iki bilgi var (`Sub` = kullanıcı adı, `Jti` = token'ın kendi kimliği). "Nickname" veya "telefon numarası" gibi ekstra bir bilgi de taşımak istersek, tam olarak şurayı değiştiririz — `GenerateAccessToken` metodunun içindeki `claims` dizisi:

```csharp
var claims = new[]
{
    new Claim(JwtRegisteredClaimNames.Sub, username),
    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
};
```

Bu diziye, aynı `new Claim(...)` deseniyle, istediğimiz kadar yeni satır ekleyebiliriz:

```csharp
var claims = new[]
{
    new Claim(JwtRegisteredClaimNames.Sub, username),
    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
    new Claim("nickname", nickname),
    new Claim("phoneNumber", phoneNumber)
};
```

- `new Claim(JwtRegisteredClaimNames.Sub, username)`'daki `JwtRegisteredClaimNames.Sub`, JWT standardının **önceden tanımladığı**, herkesin anlaştığı bir isim (`"sub"`). `"nickname"`/`"phoneNumber"` ise bizim **kendi uydurduğumuz** isimler — bir claim'in adı aslında sadece bir metin, JWT standardı bunun ne olacağını zorunlu kılmıyor (bazı yaygın isimler var, `JwtRegisteredClaimNames` sınıfı onları hazır sunuyor, ama zorunlu değil).

### Peki `nickname`/`phoneNumber` değerini nereden alacağız?

Şu anki kodda `GenerateAccessToken` metodu **sadece** `username` parametresi alıyor:

```csharp
private (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(string username)
```

Nickname/telefon gibi ekstra bilgiyi de kullanabilmesi için, bu metodun parametre listesine onları da eklememiz gerekir:

```csharp
private (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(string username, string nickname, string phoneNumber)
```

Ve bu metodu çağıran yer (`Login` metodu), bu değerleri **birinden** bulup vermek zorunda:

```csharp
var (accessToken, expiresAtUtc) = GenerateAccessToken(request.Username, "Berkan", "555-123-4567");
```

**Burada gerçek bir sorun ortaya çıkıyor:** Şu anki `AuthController`'da tek, sabit bir demo kullanıcı var (`admin`/`Passw0rd!`) — hiçbir yerde onun bir "nickname"i ya da "telefon numarası" **saklı değil**. Yani bu bilgiyi `Login` metoduna koyabilmemiz için önce onu **bir yerden okumamız** gerekir:

- **Bugünkü demo düzeyinde:** `Login` metodunun içine, `DemoUsername` gibi, sabit birkaç değer daha eklenebilir (`DemoNickname = "Berkan"`, `DemoPhoneNumber = "555-123-4567"`) — hızlı ama yine "tek kullanıcı" sınırlamasının devamı.
- **Gerçek bir sistemde:** Bir `Users` tablosu olurdu (Day 4'teki `Skills` tablosu gibi), her kullanıcının kendi `Nickname`/`PhoneNumber` sütunu olurdu; `Login` metodu, şifre doğrulandıktan **hemen sonra**, o kullanıcının satırını veritabanından çekip (`GetByUsernameAsync` gibi bir metotla), oradan gelen gerçek `nickname`/`phoneNumber` değerlerini `GenerateAccessToken`'a geçerdi.

### Bu bilgiyi daha sonra nasıl geri okuruz?

Token içine koyduğumuz her claim, `UseAuthentication()` middleware'i tarafından `HttpContext.User` içine dolduruluyor (Day 23/24'te gördüğümüz mekanizmanın ta kendisi). Herhangi bir `[Authorize]`'lı controller action'ının içinden, mevcut `User` özelliği üzerinden okunabilir:

```csharp
var nickname = User.FindFirst("nickname")?.Value;
```

`User.FindFirst("nickname")` → token'ın içindeki claim'ler arasında adı `"nickname"` olanı arıyor, buluyorsa onu (bir `Claim` nesnesi olarak) döndürüyor; `?.Value` → o nesnenin gerçek metin değerini alıyor (`?.`, "eğer null değilse" demek — claim hiç yoksa hata fırlatmak yerine `null` döner).

### Önemli bir güvenlik notu

JWT'nin payload kısmı (claim'lerin durduğu yer) **şifrelenmiş değil**, sadece base64 ile kodlanmış — yani imzayı bozmadan token'ın içeriğini **okumak** için gizli anahtara hiç gerek yok (`jwt.io` gibi bir siteye yapıştırıp direkt okunabilir, denedik zaten curl çıktılarında). Bu yüzden: nickname gibi zararsız bir bilgi koymak sorun değil, ama gerçekten gizli kalması gereken bir şeyi (şifre, TC kimlik no gibi) asla bir claim olarak token'a koymayız — imza sadece "bu içerik değiştirilmedi" garantisi veriyor, "bu içerik gizli" garantisi vermiyor.
