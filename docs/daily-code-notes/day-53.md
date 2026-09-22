# Day 53 — Kod Notları

Faz 3, Hafta 10, Gün 53. Konu: **Rate Limiting (organizasyon bazlı)** — Node.js'teki `express-rate-limit`'in .NET karşılığı: `Microsoft.AspNetCore.RateLimiting` (framework'ün kendi yerleşik middleware'i, .NET 7+'tan beri).

---

## 1. Gerçek problem

Şu an hiçbir şey, tek bir organizasyonun `POST /api/workorders`'ı saniyede onlarca kez çağırıp paylaşılan FieldOps.Api sürecini/veritabanını yorup **diğer tüm organizasyonları** etkilemesini engellemiyor. Week 8'de **veri** izolasyonunu sağlamıştık (bir organizasyon başkasının verisini göremez); bugün **kaynak/performans** izolasyonunu ele alıyoruz (bir organizasyon başkasının performansını düşüremez).

---

## 2. `Program.cs` — rate limiter kurulumu, satır satır

Önce üç ayrı parça olduğunu ayırt etmek gerekiyor: (a) **servisi kaydetmek** (`AddRateLimiter`), (b) o servise **bir politika tanımlamak** (`AddPolicy`), (c) o politikayı **middleware pipeline'a bağlamak** (`UseRateLimiter`). Üçü de olmadan hiçbir şey çalışmaz.

### 2.1 — `AddRateLimiter`: servisi DI'a kaydetmek

```csharp
builder.Services.AddRateLimiter(options =>
{
    // ...
});
```

Bu satır, tıpkı `AddControllers()`/`AddDbContext()` gibi, ASP.NET Core'a "rate limiting özelliğini kullanacağım" diyor ve gerekli servisleri DI container'a ekliyor. `options => { ... }` bir **yapılandırma lambda'sı** — içinde ne yazarsak, rate limiting sisteminin genel davranışını ayarlıyoruz. Bu noktada henüz hiçbir istek işlenmiyor, sadece "kurallar" tanımlanıyor.

```csharp
options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
```

Limit aşıldığında dönecek HTTP durum kodunu belirliyoruz. Bunu yazmasaydık varsayılan `503 Service Unavailable` dönerdi — `429 Too Many Requests` (RFC 6585), rate limiting için doğru, standart durum kodu.

### 2.2 — `AddPolicy`: "hangi isteğin hangi kovaya gireceğini" tanımlamak

```csharp
options.AddPolicy("PerOrganization", httpContext =>
{
    var organizationId = httpContext.Request.Headers["X-Organization-Id"].FirstOrDefault() ?? "unknown";
    return RateLimitPartition.GetFixedWindowLimiter(organizationId, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = rateLimitPermitLimit,
        Window = TimeSpan.FromSeconds(rateLimitWindowSeconds),
        QueueLimit = 0
    });
});
```

Bunu adım adım açalım:

- **`"PerOrganization"`** — bu politikaya verdiğimiz **isim**. Bu isim, controller'daki `[EnableRateLimiting("PerOrganization")]` attribute'unun referans verdiği aynı string — ikisi arasındaki bağlantı **sadece bu isimle** kuruluyor, başka hiçbir mekanizma yok. İsim yanlış yazılsa (örn. `"PerOrg"`), derleyici hata vermez ama rate limiting sessizce çalışmazdı — bu, roldan (`RequireRole("SuperAdmin")` Day 26) tanıdığımız aynı "string eşleşmesi, compile-time güvenlik yok" tuzağı.

- **`httpContext => { ... }`** — bu, **her rate-limitli istek geldiğinde** ASP.NET Core tarafından çağrılan bir fonksiyon. Parametre olarak o anki isteğin `HttpContext`'ini alıyor (header'lar, route, her şey burada). Görevi: bu isteğin **hangi kovaya** ait olduğuna karar vermek, ve o kova için bir "limiter" nesnesi döndürmek.

- **`httpContext.Request.Headers["X-Organization-Id"].FirstOrDefault() ?? "unknown"`** — Day 35'ten beri tanıdık header okuma, ama bu sefer `[FromHeader]` model binding üzerinden değil, doğrudan `HttpContext`'ten — çünkü bu kod bir controller action'ı değil, middleware seviyesinde çalışan çıplak bir fonksiyon. `Headers["X-Organization-Id"]` bir `StringValues` döner (header birden fazla değerle gelebilir teorik olarak), `.FirstOrDefault()` ilk değeri alır, header hiç yoksa `null` döner, `?? "unknown"` onu sabit bir string'e çeviriyor.

- **`RateLimitPartition.GetFixedWindowLimiter(organizationId, factory)`** — asıl işi yapan çağrı. İlk parametre (`organizationId`) **partition key** — "1", "2" gibi bir string. Framework, bu key'e göre **ayrı ayrı sayaçlar** tutuyor: `"1"` anahtarlı kova ile `"2"` anahtarlı kova birbirinden tamamen bağımsız, framework bunu bizim yerimize, dahili bir `Dictionary<string, ...>` benzeri yapıda yönetiyor. İkinci parametre (`_ => new FixedWindowRateLimiterOptions { ... }`) bir **factory fonksiyonu** — "bu key için İLK KEZ bir kova oluşturman gerekirse, onu şu ayarlarla oluştur" diyor. `_` kullanılması (parametre adı yok) çünkü bu factory'nin aldığı parametreye (partition key'in kendisi) burada ihtiyacımız yok, her kova aynı ayarlarla kuruluyor.

- **`FixedWindowRateLimiterOptions`** — "Fixed Window" (sabit pencere) algoritmasının ayarları:
  - **`PermitLimit`** — pencere başına izin verilen maksimum istek sayısı (bizim durumumuzda `5`).
  - **`Window`** — pencerenin uzunluğu (`10` saniye). Pencere, o kovaya **ilk isteğin geldiği anda** başlar, `Window` süresi dolunca sıfırlanır (yani "son 10 saniye" kayan bir pencere değil, sabit bir pencere — algoritmanın adının "fixed" olmasının sebebi bu).
  - **`QueueLimit = 0`** — limit dolan istekler bir kuyruğa alınıp beklemeye bırakılabilir (`QueueLimit > 0` olsaydı), ama biz `0` verdik: dolu bir kovaya gelen istek **anında ve kesin olarak** reddedilir, beklemez.

**`rateLimitPermitLimit`/`rateLimitWindowSeconds` neden sabit sayı değil, değişken:**
```csharp
var rateLimitPermitLimit = builder.Configuration.GetValue("RateLimiting:PerOrganization:PermitLimit", 5);
var rateLimitWindowSeconds = builder.Configuration.GetValue("RateLimiting:PerOrganization:WindowSeconds", 10);
```
`builder.Configuration.GetValue<T>(key, defaultValue)` — `appsettings.Development.json`'dan (ya da test ortamında `FieldOpsApiFactory`'nin override ettiği değerden) o anahtarı okuyor; bulamazsa `5`/`10`'u varsayılan olarak kullanıyor. Bu sayede aynı kod, gerçek uygulamada `5` ile, test ortamında `100000` ile çalışabiliyor — **kod hiç değişmeden**, sadece konfigürasyon değeri değişerek (bölüm 4'te detaylı).

### 2.3 — `UseRateLimiter`: middleware pipeline'a eklemek

```csharp
app.UseRateLimiter();
```

`AddRateLimiter`/`AddPolicy` sadece **kuralları tanımlar** — bu satır olmadan, tanımlanan kurallar hiçbir isteğe **uygulanmaz**. `UseRateLimiter()`, rate limiting'i gerçek middleware zincirine ekliyor; artık her istek, controller'a ulaşmadan önce bu middleware'den geçiyor. Middleware, isteğin route'una bakıp "bu action'da `[EnableRateLimiting]` var mı?" diye kontrol ediyor — varsa ilgili politikayı (yukarıdaki fonksiyonu) çalıştırıp karar veriyor, yoksa hiç müdahale etmeden isteği geçiriyor.

---

## 3. `WorkOrdersController.Create` — politikanın bir action'a bağlanması

```csharp
[HttpPost]
[EnableRateLimiting("PerOrganization")]
public ActionResult<WorkOrderDto> Create(...)
```

`[EnableRateLimiting("PerOrganization")]` bir **attribute** — action'ın üzerine konan, derleme zamanında bir "etiket" gibi çalışan bir işaretleyici (`[HttpPost]`, `[Authorize]` ile aynı mekanizma). Bu attribute'un tek işi: "bu action, `UseRateLimiter` middleware'i çalışırken, `'PerOrganization'` adlı politikaya göre değerlendirilsin" demek. `Assign`/`Complete`/`GetAll` gibi diğer action'ların üzerinde bu attribute **yok** — bu yüzden onlara hiç rate limit uygulanmıyor, middleware onları olduğu gibi geçiriyor.

**Neden sadece `Create`:** Diğer action'lar zaten var olan bir iş emri üzerinde çalışıyor, doğal bir üst sınırları var (bir iş emrini sonsuz kez `Assign` edemezsin, `Open` olmalı); ama `Create` her çağrıda **yeni** bir satır oluşturuyor, doğal bir sınırı yok.

---

## 4. Bir isteğin tam akışı — baştan sona

```
1. Client: POST /api/workorders, header X-Organization-Id: 1
2. UseRateLimiter middleware'i devreye girer
3. Middleware, hedef action'in [EnableRateLimiting("PerOrganization")]
   tasidigini gorur
4. "PerOrganization" politikasinin httpContext => {...} fonksiyonu calisir:
   - organizationId = "1" okunur
   - RateLimitPartition.GetFixedWindowLimiter("1", factory) cagirilir
   - "1" anahtarli kova daha once yoksa, factory ile olusturulur (limit=5, pencere=10sn)
   - "1" anahtarli kova zaten varsa, mevcut sayaci kullanilir
5. Kovada yer varsa: sayac 1 artar, istek WorkOrdersController.Create'e ulasir,
   normal akis (membership -> mutasyon -> invalidation -> audit -> notification) devam eder
6. Kovada yer yoksa: istek HICBIR controller kodu calismadan 429 doner
```

Bu akışta kritik nokta: adım 4-6 arasındaki her şey, **`Create` metodunun içindeki tek bir satır bile çalışmadan** oluyor — rate limiting tamamen middleware seviyesinde, controller'ın hiç haberi olmadan işleniyor.

---

## 5. Canlı olarak bulunan gerçek bir sorun — mevcut testler kırılıyordu

İlk yazdığım haliyle (`PermitLimit = 5` sabit kodlu), `dotnet test` çalıştırınca ne olacağını düşünmeden önce fark ettim: `WorkOrdersAuthorizationIntegrationTests` dosyasında, org 1 için **15'ten fazla** `POST /api/workorders` çağrısı var, hepsi aynı test sınıfının (`IClassFixture`, tüm test metotları arasında paylaşılan tek bir host) çalışması sırasında, 10 saniyeden çok daha kısa sürede. Sabit bir 5/10sn limiti, bu testlerin çoğunu **beklenmedik şekilde** `429` ile kırardı — rate limiting'in kendisiyle hiç ilgisi olmayan testler.

**Çözüm — Day 48'in "config değerini override et, iş kodunu değil" deseni tekrar:**
```csharp
// FieldOpsApiFactory.ConfigureWebHost içine eklendi:
builder.UseSetting("RateLimiting:PerOrganization:PermitLimit", "100000");
```
Gerçek demo/üretim değeri (`appsettings.Development.json`'daki `5`) küçük ve gözle görülür kalıyor; test ortamı ise pratikte sınırsız bir limitle çalışıyor, böylece rate limiting'le ilgisi olmayan testler asla yanlışlıkla `429` almıyor. Bu, tam olarak Day 48'in connection string override deseninin bir tekrarı — iş kodu (`WorkOrdersController`) hiç değişmedi, sadece test ortamının **konfigürasyon değeri** değişti.

---

## 6. Canlı kanıt

```
1) Org 1 icin art arda 6 hizli POST /api/workorders:
   istek 1 -> 201
   istek 2 -> 201
   istek 3 -> 201
   istek 4 -> 201
   istek 5 -> 201
   istek 6 -> 429   <- limit tam 5'te devreye girdi

2) Org 2 icin AYNI ANDA bir POST /api/workorders:
   -> 201   <- Org 1'in limiti dolmasina ragmen Org 2 hic etkilenmedi
```

Bu, izolasyonun **gerçekten** organizasyon bazlı olduğunun, global bir limit olmadığının kanıtı.

---

## 7. Demo basitleştirmesi vs. üretim gereksinimi

- Demo limiti küçük (5/10sn) — gözle görülür olsun diye. Üretimde gerçek trafik desenlerine göre ayarlanır.
- Sadece `Create` korunuyor bugün.
- **En önemli sınırlama:** .NET'in yerleşik rate limiter'ı **bellek-içi** — tek bir `FieldOps.Api` sürecine özel. Yatay ölçeklenen (birden fazla instance) bir dağıtımda, her instance kendi ayrı sayacını tutar, gerçek bir paylaşılan limit olmaz. Gerçek bir dağıtık rate limiter, Redis gibi paylaşılan bir sayaç deposu gerektirirdi — Day 48-52'de kurduğumuz Redis altyapısı tam olarak bunun için kullanılabilirdi, ama bugün bilinçli olarak native/in-memory ile sınırlı kaldık.
- 429 eşiğini test eden otomatik bir test **eklenmedi** — bunun yerine test ortamı limiti pratikte sınırsız yapıldı, ve eşik davranışı sadece canlı curl ile kanıtlandı.

---

## Regresyon

```
dotnet test FieldOps.slnx    → 49/49 (rate limit test ortaminda etkisiz)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```
