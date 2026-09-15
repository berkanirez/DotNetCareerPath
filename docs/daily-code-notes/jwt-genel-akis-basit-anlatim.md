# JWT Mantığı — Baştan Sona, Çok Basit Anlatım

Bu doküman, teknolojiyle hiç ilgisi olmayan birine anlatır gibi, StockPilot'taki JWT sistemini **baştan sona** anlatıyor: nerede üretiliyor, nerede kontrol ediliyor, hangi dosya ne iş yapıyor. Day 23, 24 ve 25'in hepsini tek bir bütün halinde topluyor.

---

## 1. Önce JWT nedir — bir bilezik gibi düşün

Bir konsere/festivale gittiğini hayal et. Girişte biletini gösteriyorsun, görevli seni kontrol ediyor, sonra koluna bir **bilezik** takıyor. Artık içeride her büfeye, her alana giderken biletini tekrar göstermene gerek yok — sadece bileziğini gösteriyorsun.

Bu bileziğin birkaç özelliği var:
- Sadece **girişte, bir kere** kontrol yapıldı (kullanıcı adı/şifre).
- Bileziğin üzerinde bazı bilgiler var (mesela hangi gün geçerli, VIP mi normal mi).
- Bilezik **taklit edilemez** — festivalin kendine has, özel bir baskısı/deseni var, sahte bir bilezik bunu taklit edemez.
- Bileziğin bir **süresi** var — festival bitince ya da ertesi gün artık geçersiz.

**JWT tam olarak bu bileziğin dijital hali.** Kullanıcı adı/şifreni bir kere, sadece login olurken gösteriyorsun. Sunucu doğruysa sana bu "dijital bileziği" (bir sürü rastgele harf/rakamdan oluşan uzun bir metin, `eyJhbGc...` diye başlıyor) veriyor. Bundan sonra her istekte, şifreni tekrar yazmak yerine, sadece bu bileziği gösteriyorsun.

---

## 2. Genel resim — hiç koda bakmadan, sadece akış

1. Kullanıcı, kullanıcı adı ve şifresini gönderiyor ("login" isteği).
2. Sunucu bunun doğru olup olmadığına bakıyor.
3. Doğruysa: sunucu bir "dijital bilezik" (JWT) üretip kullanıcıya veriyor.
4. Kullanıcı, bundan sonraki her istekte bu bileziği gösteriyor (şifresini bir daha yazmıyor).
5. Bazı "kapılar" (bazı işlemler), sadece belirli bir bilezik türünü kabul ediyor — mesela "ürün silme" kapısı sadece "Admin" yazan bilezikleri kabul ediyor, "Employee" yazanları içeri almıyor.
6. Bileziğin bir süresi var; süresi dolunca yeniden login olman (ya da "yenileme bileziğini" göstermen — refresh token) gerekiyor.

Şimdi bunun kodda **hangi dosyada, tam olarak nasıl** yapıldığına bakalım.

---

## 3. Bilezik NEREDE üretiliyor — dosya dosya

### Dosya 1: `appsettings.Development.json` — "bilezik tarifi"

```json
"Jwt": {
    "Issuer": "StockPilot.Api",
    "Audience": "StockPilot.Api.Clients",
    "Key": "dev-only-signing-key-not-for-production-use-1234567890",
    "ExpiryMinutes": 60
}
```

Bunu, festivalin bileziği nasıl basacağının **tarifi** gibi düşün — henüz bir bilezik değil, bileziği üretirken kullanılacak ayarlar:
- `Key` — festivalin sadece kendisinin bildiği **gizli mühür deseni**. Bu, bileziği taklit edilemez yapan şey. Kimse bu deseni bilmeden sahte bir bilezik basamaz.
- `Issuer` — "bu bileziği kim bastı" (bizim uygulamamız).
- `Audience` — "bu bilezik hangi festival için geçerli" (başka bir uygulamanın bileziği burada işe yaramaz).
- `ExpiryMinutes` — bileziğin kaç dakika geçerli olacağı (60 dakika).

### Dosya 2: `Controllers/AuthController.cs` — "asıl bilezik basma makinesi"

Kullanıcı `POST /api/auth/login` isteğini (kullanıcı adı + şifre) gönderdiğinde, **buradaki kod** çalışıyor. Üç önemli parça var:

**a) Önce kimlik kontrolü:**

```csharp
private static readonly Dictionary<string, (string PasswordHash, string Role)> DemoUsers = new()
{
    ["admin"] = (PasswordHasher.HashPassword(null!, "Passw0rd!"), "Admin"),
    ["employee"] = (PasswordHasher.HashPassword(null!, "Employee123!"), "Employee")
};
```

Bunu, festivalin girişteki elindeki **misafir listesi** gibi düşün — "admin" adında biri var, VIP grubunda ("Admin"); "employee" adında biri var, normal grupta ("Employee"). Şifreler burada düz yazılı değil, "hash'lenmiş" (karıştırılmış) hali duruyor — yani biri bu listeyi görse bile gerçek şifreleri okuyamaz.

Gelen kullanıcı adı/şifre bu listeyle karşılaştırılıyor:

```csharp
var isValidLogin = DemoUsers.TryGetValue(request.Username, out var user) &&
    PasswordHasher.VerifyHashedPassword(null!, user.PasswordHash, request.Password) == PasswordVerificationResult.Success;

if (!isValidLogin)
{
    return Unauthorized("Invalid username or password.");
}
```

Yanlışsa: "seni tanımıyorum" cevabı (`401`) dönüyor, bilezik verilmiyor.

**b) Doğruysa, asıl bilezik burada basılıyor** (`GenerateAccessToken` adlı bölüm):

```csharp
var claims = new[]
{
    new Claim(JwtRegisteredClaimNames.Sub, username),
    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
    new Claim(ClaimTypes.Role, role)
};
```

Bunu, bileziğin üzerine **yazılan bilgiler** gibi düşün — 3 tane bilgi yazılıyor:
- Bu bilezik **kime ait** (kullanıcı adı).
- Bu bileziğin **kendine özel bir seri numarası** (her bilezik farklı, birbirinin aynısı iki bilezik yok).
- Bu kullanıcı **hangi grupta** (Admin mi Employee mi).

```csharp
var token = new JwtSecurityToken(
    issuer: jwtSection["Issuer"],
    audience: jwtSection["Audience"],
    claims: claims,
    expires: expiresAtUtc,
    signingCredentials: signingCredentials);
```

Bu satır, yukarıdaki bilgileri alıp gerçek bileziği **basıyor** — hangi festival, ne zamana kadar geçerli, üzerinde ne yazıyor, hepsi bir arada. `signingCredentials`, `appsettings.json`'daki gizli mühürle bu bileziği **mühürlüyor** — artık kimse üzerindeki yazıyı değiştiremez, değiştirirse mühür bozulur ve sunucu bunu anlar.

```csharp
return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
```

Bu satır, o basılmış bileziği **gerçek, taşınabilir bir metne** çeviriyor — curl çıktılarında gördüğümüz o uzun `eyJhbGc...` yazısı burada ortaya çıkıyor.

**c) Bilezik kullanıcıya gönderiliyor:**

```csharp
return Ok(new LoginResponse(accessToken, expiresAtUtc, refreshToken));
```

Kullanıcı artık cevapta şunu alıyor:
```json
{
  "token": "eyJhbGc...",
  "expiresAtUtc": "2026-09-15T15:00:00Z",
  "refreshToken": "QXd4Ej43..."
}
```

`token` — az önce bahsettiğimiz asıl bilezik. `refreshToken` — ayrı bir konu, "bileziğin süresi dolunca, tekrar şifre yazmadan yeni bilezik almak için kullanılan" ayrı bir anahtar (Day 24'te işlendi, ayrı bir dosyada — `day-24-refresh-akisi-detay.md` — detaylı anlatılmıştı, burada tekrar etmiyoruz).

---

## 4. Kullanıcı bu bileziği nasıl gösteriyor

Kullanıcı, bundan sonraki her istekte, isteğin "başlığına" (header) şunu ekliyor:

```
Authorization: Bearer eyJhbGc...
```

Bunu, "elimde şu bilezik var, bak" demek gibi düşün. Bizim curl örneklerinde bunu elle yazdık; gerçek bir tarayıcı/mobil uygulamada bu genelde otomatik olarak, her istekte eklenir (uygulama bileziği bir yerde saklar, hatırlar).

---

## 5. Bilezik NEREDE kontrol ediliyor — dosya dosya

### Dosya 3: `Program.cs` — "güvenlik görevlisinin eğitimi ve fiilen görev başında olması"

Burada iki farklı şey oluyor, ikisini de ayırmak önemli:

**a) Görevliye eğitim veriliyor (uygulama başlarken, bir kere):**

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ...
            IssuerSigningKey = new SymmetricSecurityKey(...),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
```

Bunu, güvenlik görevlisine "bir bilezik gördüğünde nasıl kontrol edeceksin" **eğitimini vermek** gibi düşün: hangi mühür deseni geçerli sayılacak (`IssuerSigningKey`), hangi festival için basılmış olması gerektiği (`Issuer`/`Audience`), süresi kontrol edilecek mi (`ValidateLifetime`). Bu, uygulama **açılırken bir kere** ayarlanıyor, her istekte tekrar okunmuyor.

**b) Görevli fiilen kapıda duruyor (her istekte):**

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

- `UseAuthentication()` → gerçek görevlinin işi: gelen her isteğin `Authorization: Bearer ...` başlığına **bakıyor**, bileziği (yukarıdaki eğitime göre) kontrol ediyor — mühür doğru mu, süresi dolmuş mu. Geçerliyse, "bu kişi şu kullanıcı, şu grupta" bilgisini **not ediyor** (bu notun teknik adı `HttpContext.User`, ama şimdilik "görevlinin elindeki not defteri" diye düşünebilirsin).
- `UseAuthorization()` → görevlinin **karar verme** işi: not defterine bakıp "bu kişi bu kapıdan geçebilir mi" diye karar veriyor. Bunun kararı, aşağıdaki 4. dosyadaki "tabela"lara göre değişiyor.

**Bu iki satırın sırası önemli** — önce bileziğe bakılmalı (a), sonra karar verilmeli (b). Tersi olsaydı, görevli elinde hiçbir not olmadan karar vermeye çalışırdı — herkesi "tanımıyorum" diye geri çevirirdi.

### Dosya 4: `Controllers/ProductsController.cs` — "kapılardaki tabelalar"

```csharp
[Authorize(Roles = "Admin")]
[HttpDelete("{id}")]
public async Task<IActionResult> Delete(...)
```

Bu, `Delete` işleminin kapısına asılmış bir **tabela** gibi düşün: "Sadece Admin bileziği olanlar girebilir." `UseAuthorization()` (yukarıdaki görevli), bu tabelayı okuyup, elindeki nota (`HttpContext.User`) bakarak karar veriyor:
- Hiç bilezik yoksa / bilezik geçersizse → görevli kimliği hiç bilmiyor → **401** ("sen kimsin bilmiyorum").
- Bilezik geçerli ama üzerinde "Employee" yazıyorsa (tabela "Admin" istiyor) → görevli kişiyi tanıyor ama izin veremiyor → **403** ("seni tanıyorum ama bu kapı senin için değil").
- Bilezik geçerli ve üzerinde "Admin" yazıyorsa → **içeri alınıyor**, `Delete` metodu gerçekten çalışıyor.

Diğer işlemlerde (`GetAll`, `GetById`, `Create`, `Update`, `BulkCreate`) hiç tabela yok — yani bilezik olsun olmasın herkes girebiliyor (bugüne kadar bilerek böyle bırakıldı).

---

## 6. Baştan sona somut bir örnek — login'den korumalı işleme

```
1. Sen: POST /api/auth/login  {"username":"employee","password":"Employee123!"}
   → AuthController.Login çalışıyor
   → DemoUsers listesinde "employee" bulunuyor, şifre doğru
   → Bilezik basılıyor: üzerinde "employee" ve "Employee" grubu yazıyor, 60 dakika geçerli
   → Sana dönen cevap: { "token": "eyJhbGc...", ... }

2. Sen: DELETE /api/products/5   Header: Authorization: Bearer eyJhbGc...
   → Program.cs'teki güvenlik görevlisi (UseAuthentication) bileziğe bakıyor:
     mühür doğru, süresi dolmamış → "bu kişi employee, grubu Employee" diye not alıyor
   → UseAuthorization, ProductsController.Delete'in tabelasına bakıyor: "Sadece Admin"
   → Nottaki grup ("Employee") tabelayla ("Admin") uyuşmuyor
   → SONUÇ: 403 Forbidden — kapı kapalı, ama SENİ TANIYOR, sadece izin yok

3. Sen: POST /api/auth/login  {"username":"admin","password":"Passw0rd!"}
   → Bu sefer "Admin" grubu yazan bir bilezik alıyorsun

4. Sen: DELETE /api/products/5   Header: Authorization: Bearer <admin bileziği>
   → Görevli notu alıyor: "bu kişi admin, grubu Admin"
   → Tabelayla uyuşuyor ("Admin" == "Admin")
   → SONUÇ: 204 No Content — ürün gerçekten silindi
```

---

## 7. Özet — hangi dosya ne iş yapıyor

| Dosya | Görevi | Benzetme |
|---|---|---|
| `appsettings.Development.json` | Bileziğin nasıl basılacağının ayarları (gizli mühür, süre) | Bilezik basma tarifi |
| `Controllers/AuthController.cs` | Kimlik kontrolü + bileziği gerçekten basıp kullanıcıya vermek | Bilezik basma makinesi |
| `Program.cs` | Bilezik nasıl kontrol edilecek (kuruluşta) + her istekte fiilen kontrol etmek | Görevlinin eğitimi + görevlinin kapıda durması |
| `Controllers/ProductsController.cs` | Hangi işlemin hangi bilezikleri kabul ettiğini belirtmek | Kapılardaki tabelalar |

Bu genel akış oturdu mu? Netleşmeyen bir dosya/adım varsa söyle, o kısmı daha da yavaşlatalım.
