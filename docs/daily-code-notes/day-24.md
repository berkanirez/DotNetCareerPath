# Day 24 — Kod Notları

Faz 2, Hafta 5, Gün 24. Konu: refresh token ve refresh-token rotation.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `Models/IRefreshTokenStore.cs` — yeni bir soyutlama

```csharp
public interface IRefreshTokenStore
{
    string Issue(string username);
    bool TryConsume(string refreshToken, out string username);
}
```

**Neden:** Day 3'teki `ISkillCatalog` ile tamamen aynı motivasyon — refresh token'ların nerede/nasıl saklandığını, onu kullanan `AuthController`'dan soyutlamak. `Issue` yeni bir token üretip saklıyor, `TryConsume` bir token'ı **doğrulayıp aynı anda tüketiyor** (tek kullanımlık).

---

## 2. `Models/InMemoryRefreshTokenStore.cs` — asıl mekanizma

```csharp
private readonly ConcurrentDictionary<string, (string Username, DateTime ExpiresAtUtc)> _tokens = new();

public string Issue(string username)
{
    var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    _tokens[token] = (username, DateTime.UtcNow.Add(Lifetime));
    return token;
}

public bool TryConsume(string refreshToken, out string username)
{
    username = string.Empty;
    if (!_tokens.TryRemove(refreshToken, out var entry)) return false;
    if (entry.ExpiresAtUtc < DateTime.UtcNow) return false;
    username = entry.Username;
    return true;
}
```

**Neden `RandomNumberGenerator.GetBytes(32)`:** JWT'nin aksine refresh token'ın içinde **hiçbir okunabilir bilgi yok** — sadece rastgele, tahmin edilemez bir bayt dizisi (base64'e çevrilmiş hali curl çıktılarında gördüğümüz `QXd4Ej43...` gibi string'ler). Buna "opaque token" deniyor: anlamı token'ın kendisinde değil, sunucunun bu token'ı neyle eşlediğinde saklı.

**Neden `TryRemove` (ve `TryGetValue` değil) — rotation'ın asıl sırrı burada:** `TryRemove`, token'ı sözlükten **okurken aynı anda siliyor**. Yani bir refresh token bir kere `TryConsume` ile kullanıldığında, sözlükten tamamen kayboluyor — aynı token'la ikinci bir deneme, orada hiçbir şey bulamıyor (`TryRemove` `false` döner). Bu, "her kullanımda eskisi imha edilip yenisi verilir" (rotation) kuralını **tek bir metot çağrısıyla** garanti ediyor; ayrı bir "şimdi bunu geçersiz kıl" adımına gerek yok.

**Neden `ConcurrentDictionary`, düz `Dictionary` değil:** Bu depo `Singleton` olarak kayıtlı (aşağıda), yani **aynı anda birden fazla istek** aynı sözlüğe erişebilir. Düz `Dictionary`, eşzamanlı okuma/yazmalarda bozulabilir (thread-safe değil); `ConcurrentDictionary` bunun için özel olarak tasarlanmış.

---

## 3. `Models/RefreshTokenRequest.cs`, `Models/LoginResponse.cs`

```csharp
public record RefreshTokenRequest([Required] string RefreshToken);
public record LoginResponse(string Token, DateTime ExpiresAtUtc, string RefreshToken);
```

`LoginResponse`'a üçüncü bir alan eklendi — hem `Login` hem yeni `Refresh` action'ı artık bu genişletilmiş DTO'yu döndürüyor.

---

## 4. `Controllers/AuthController.cs` — refactor + `Refresh` action'ı

```csharp
private (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(string username)
{
    // ... Day 23'teki JWT üretim kodu, aynen ...
}
```

**Neden bu refactor yapıldı:** Day 23'te JWT üretim kodu sadece `Login` içindeydi. Bugün `Refresh` action'ının da **aynı** JWT üretme mantığına ihtiyacı var — kopyalamak yerine, private bir metoda çıkarıp ikisinin de çağırmasını sağladık (tuple dönüş tipi `(string Token, DateTime ExpiresAtUtc)`, C#'ın adlandırılmış tuple özelliği — `.Token`/`.ExpiresAtUtc` ile erişilebiliyor).

```csharp
[HttpPost("refresh")]
[AllowAnonymous]
public ActionResult<LoginResponse> Refresh(RefreshTokenRequest request)
{
    if (!_refreshTokenStore.TryConsume(request.RefreshToken, out var username))
    {
        return Unauthorized("Invalid or already-used refresh token.");
    }

    var (accessToken, expiresAtUtc) = GenerateAccessToken(username);
    var newRefreshToken = _refreshTokenStore.Issue(username);

    return Ok(new LoginResponse(accessToken, expiresAtUtc, newRefreshToken));
}
```

Akış: gelen refresh token tüketiliyor (bulunamazsa/süresi dolmuşsa 401) → başarılıysa **yeni** bir access token VE **yeni** bir refresh token üretilip ikisi birden dönüyor. Kullanıcı, önceki refresh token'ı bir daha asla kullanamaz.

---

## 5. `Program.cs` — DI kaydı ve **beklenmedik bir keşif**

```csharp
builder.Services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
```

**Neden `Singleton`, `Scoped` değil:** Bir refresh token, login isteğiyle **farklı, sonraki** bir istekte (`/refresh`) kullanılıyor — yani iki ayrı HTTP isteği arasında hayatta kalması lazım. `Scoped`, sadece TEK bir istek boyunca yaşar (Day 4'ün `EfSkillCatalog`'unda öğrendiğimiz gibi) — bu işe yaramaz. `Singleton`, Day 3'ün `InMemorySkillCatalog`'u gibi, uygulama ömrü boyunca tek bir örnek, tüm isteklerce paylaşılıyor.

### Canlı demo sırasında bulunan gerçek bir hata: `ClockSkew`

Access token süresini geçici olarak 5 saniyeye düşürüp süre dolumunu kanıtlamaya çalışırken, **7 saniye sonra bile token hâlâ kabul edildi.** Beklenmedik bir sonuç — araştırıp gerçek nedeni bulduk:

```csharp
options.TokenValidationParameters = new TokenValidationParameters
{
    ...
    ValidateLifetime = true,
    ClockSkew = TimeSpan.Zero   // ← bu satır yoktu, varsayılan 5 DAKİKA
};
```

**Neden:** ASP.NET Core'un JWT doğrulaması, `ValidateLifetime = true` olsa bile, varsayılan olarak **5 dakikalık bir tolerans (`ClockSkew`)** uyguluyor — token'ın kendi `exp` alanı dolmuş olsa bile, dolma anından itibaren 5 dakika daha kabul ediliyor. Bunun amacı, farklı sunucuların saatleri arasındaki küçük farkları (clock drift) tolere etmek. Bizim 5 saniyelik demo token'ımız, aslında bu 5 dakikalık pencerenin çok içindeydi — bu yüzden 7 saniye sonra hâlâ çalışıyordu. `ClockSkew = TimeSpan.Zero` ekleyerek bu toleransı kapattık, süre dolumu artık tam token'ın kendi `exp` değerine göre işliyor.

**Bu, kodu "kırıp" göstermek için tasarlanmış bir demo değildi** — gerçekten beklenmeyen bir sonuçtu, araştırıp gerçek sebebini bulup kalıcı olarak düzelttik. Tam da bu workspace'in "iddia etme, kanıtla" ilkesinin bizi gerçek bir .NET/JWT tuzağına götürdüğü bir an.

---

## Canlı kanıt

```
Login                                    → access token + refresh token (RT1)
Refresh (RT1)                            → YENİ access token + YENİ refresh token (RT2)
Refresh (RT1) TEKRAR (eskisi)            → HTTP 401 "Invalid or already-used refresh token." (rotation calisiyor)
Refresh (RT2)                            → basariyla calisti (RT1'in tukenmesi genel bir hata degildi)

ClockSkew keşfi ve düzeltmesi:
Access token süresi gecici 5 saniyeye düşürüldü
Login → 7 saniye bekle → DELETE (süresi gercekten dolmus token)  → HTTP 401 (ClockSkew=Zero SONRASI)
Refresh ile yeni token al → DELETE (aynı komutta, gecikmesiz)    → HTTP 204 (gercek basarili silme)
Sure 60 dakikaya geri alindi (production degeri)

dotnet test (StockPilot) → 14/14 (degisiklik yok)
dotnet test (RoadmapOS)  → 8/8 (etkilenmedi)
Veritabani demo sonrasi temiz (7 orijinal urun, demo urunleri silinmis durumda kaldi zaten).
```

---

## Bağımsız görev — `TryConsume`'un süresi dolmuş token yolunu test edilebilir hale getirmek

**Sorun:** `Lifetime` (`TimeSpan.FromDays(7)`), `InMemoryRefreshTokenStore.cs` içinde sabit bir değerdi — bir testin "süresi dolmuş bir token" senaryosunu kanıtlaması için gerçekten 7 gün beklemesi gerekirdi. Bu, Day 21'in `RowVersion` demo problemine çok benziyor: bir mekanizmanın gerçek davranışını, gerçek zamanı beklemeden kanıtlamak.

**Çözüm — `Lifetime`'ı bir constructor parametresi yapmak:**

```csharp
private readonly TimeSpan _lifetime;

public InMemoryRefreshTokenStore(TimeSpan? lifetime = null)
{
    _lifetime = lifetime ?? TimeSpan.FromDays(7);
}
```

**Neden bu şekilde:** `TimeSpan? lifetime = null` — parametre **opsiyonel** (`= null` varsayılan değeri sayesinde), yani hem `new InMemoryRefreshTokenStore()` (varsayılan 7 gün) hem `new InMemoryRefreshTokenStore(TimeSpan.FromSeconds(-1))` (test için) çalışıyor. `lifetime ?? TimeSpan.FromDays(7)` — `??` (null-coalescing operatörü) "eğer `lifetime` null ise sağdakini kullan" demek. `Program.cs`'teki `AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>()` kaydı hiç değişmedi — ASP.NET Core'un DI container'ı, bu opsiyonel parametreye karşılık gelen bir servis bulamayınca otomatik olarak varsayılan değeri (`null` → 7 gün) kullanıyor; canlı olarak da doğrulandı (login hâlâ normal çalışıyor).

**Eklenen testler (`tests/StockPilot.Api.Tests/InMemoryRefreshTokenStoreTests.cs`):**

```csharp
[Fact]
public void TryConsume_ExpiredToken_ReturnsFalse()
{
    var store = new InMemoryRefreshTokenStore(TimeSpan.FromSeconds(-1));
    var token = store.Issue("admin");

    var result = store.TryConsume(token, out var username);

    Assert.False(result);
    Assert.Equal(string.Empty, username);
}
```

**Neden `TimeSpan.FromSeconds(-1)` işe yarıyor:** `Issue`, `DateTime.UtcNow.Add(_lifetime)` ile süre dolma zamanını hesaplıyor — `_lifetime` **negatif** olduğunda, bu hesaplama "şu andan 1 saniye ÖNCE" bir zaman üretiyor, yani token **doğduğu anda zaten dolmuş** oluyor. Hiçbir gerçek bekleme gerekmiyor, test anında ve deterministik çalışıyor.

Bu arada, aynı dosyaya rotation'ı ve "bilinmeyen token" durumunu kanıtlayan 3 test daha eklendi (`TryConsume_ValidToken_...`, `TryConsume_UnknownToken_...`, `TryConsume_SameTokenTwice_...`) — toplam StockPilot test sayısı 14'ten **18**'e çıktı.

**Doğrulanan davranış:**
```
dotnet build/test (StockPilot) → 0 Hata, 0 Uyari, 18/18 basarili (4 yeni test)
dotnet test (RoadmapOS)        → 8/8 (etkilenmedi)
Canli DI kontrolu: gercek login akisi (7 gunluk production suresiyle) hala calisiyor.
```
