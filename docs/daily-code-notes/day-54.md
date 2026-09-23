# Day 54 — Kod Notları

Faz 3, Hafta 10, Gün 54. Konu: **Idempotency** — Week 10'un son konusu. `POST /api/workorders`'a, aynı isteğin tekrar gönderilmesi durumunda (örn. ağ zaman aşımı sonrası retry) **duplicate iş emri oluşturmayı önleyen** bir mekanizma.

---

## 1. Idempotency nedir — zihinsel model, en baştan

**"Idempotent"** kelimesi, matematikten ödünç alınmış: bir işlemi **bir kez** uygulamakla **N kez** uygulamak **aynı sonucu** veriyorsa, o işlem idempotent'tir. Örnek: `x = 5` (bir değişkene sabit bir değer atamak) idempotent — kaç kere çalıştırırsan çalıştır, sonuç hep `x=5`. Ama `x = x + 1` idempotent **değil** — her çalıştırmada sonuç değişir.

HTTP dünyasında bu kavram, metotların "tekrar denenebilir mi" sorusuna karşılık geliyor:
- **`GET`** doğası gereği idempotent — aynı `GET /api/workorders`'ı 10 kez çağırmak, veriyi 10 kez **değiştirmez**, sadece okur.
- **`PUT`**/`DELETE` genelde idempotent — "bu kaydı sil" ya da "bu kaydı şu değere ayarla" işlemini 2 kez yapmak, 1 kez yapmakla aynı sonucu verir (kayıt zaten silinmiş/zaten o değerde olur).
- **`POST`** (bizim `Create`'imiz) **idempotent değil** — "yeni bir kayıt oluştur" işlemini 2 kez çağırmak, **2 farklı kayıt** oluşturur. Bu, bugünün çözdüğü tam problem.

**Neden bu önemli — gerçek senaryo:** İstemci `POST /api/workorders` gönderir. Sunucu isteği alır, iş emrini **veritabanına gerçekten yazar**, ama yanıtı istemciye geri gönderirken ağ bağlantısı kopar (timeout). İstemci hiçbir yanıt almadı — "acaba işlem gerçekleşti mi, gerçekleşmedi mi?" bilmiyor. Güvenli tarafta kalmak için **aynı isteği tekrar gönderir**. Sunucu bunun bir "retry" olduğunu bilmiyor, normal bir yeni istek gibi işler — sonuç: **iki adet aynı iş emri**, veritabanında. Bu senaryo senin muhtemelen Node.js'te webhook/ödeme entegrasyonlarından (Stripe, PayPal) bildiğin, tam olarak `Idempotency-Key` header'ının çözdüğü problem: istemci, tekrar gönderdiği isteğe **aynı anahtarı** ekler; sunucu "bu anahtarı daha önce işledim" der ve işlemi tekrarlamadan, ilk seferki sonucu aynen geri döndürür.

---

## 2. `IdempotencyService` — satır satır, ne oluyor

Önce genel resim: bu servisin işi, bir key-değer deposu (Redis) üzerinde **"bu anahtarı daha önce gördüm mü?"** sorusunu cevaplamak ve cevabı hatırlamak. İki metodu var: biri **okuma** (`TryGetCachedResponse`), biri **yazma** (`StoreResponse`) — cache-aside'ın (Day 48) aynı iki hareketi, farklı bir amaç için.

```csharp
public class IdempotencyService
{
    private static readonly TimeSpan RecordDuration = TimeSpan.FromSeconds(60);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<IdempotencyService> _logger;

    public IdempotencyService(IConnectionMultiplexer redis, ILogger<IdempotencyService> logger)
    {
        _redis = redis;
        _logger = logger;
    }
```

- **`RecordDuration = TimeSpan.FromSeconds(60)`** — bir idempotency kaydının Redis'te ne kadar yaşayacağı. 60 saniye demo için kısa/gözle görülür seçildi; gerçek üretimde genelde çok daha uzun (Stripe 24 saat kullanıyor) çünkü bir istemcinin retry denemesi dakikalar sonra bile olabilir.
- **`IConnectionMultiplexer _redis`** — Day 48'den beri kurulu, uygulamanın tek Redis bağlantısı (Singleton). Bu servis kendi bağlantısını açmıyor, var olanı paylaşıyor.
- **`ILogger<IdempotencyService> _logger`** — hata olduğunda (aşağıda göreceğiz) sessizce yutmak yerine loglamak için.

```csharp
    public WorkOrderDto? TryGetCachedResponse(string idempotencyKey)
    {
        try
        {
            var cached = _redis.GetDatabase().StringGet($"idempotency:{idempotencyKey}");
            return cached.HasValue ? JsonSerializer.Deserialize<WorkOrderDto>((string)cached!) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check idempotency cache for key {IdempotencyKey}", idempotencyKey);
            return null;
        }
    }
```

- **`WorkOrderDto?` dönüş tipi** — nullable. `null` dönmesi iki farklı durumu temsil edebilir: (a) bu anahtar hiç kullanılmamış, (b) Redis'e erişilemedi. İkisi de, çağıran kod açısından **aynı davranışı** gerektiriyor: "cache'te bir şey yok, normal akışa devam et" — bu yüzden ikisini ayırt etmeye bile gerek yok, aynı `null` dönüş değeri her ikisini de kapsıyor.
- **`_redis.GetDatabase().StringGet($"idempotency:{idempotencyKey}")`** — Day 48'deki `WorkOrderReportService.GetStatusReport`'la birebir aynı çağrı şekli, sadece anahtar formatı farklı (`idempotency:{key}` vs `workorders:report:{orgId}`). Redis'in `GET <key>` komutuna karşılık geliyor.
- **`cached.HasValue ? JsonSerializer.Deserialize<WorkOrderDto>((string)cached!) : null`** — anahtar bulunduysa, saklanan JSON metnini geri `WorkOrderDto`'ya çeviriyor (`(string)cached!` cast'i, Day 48'de gördüğümüz aynı `RedisValue` belirsizliği yüzünden gerekli); bulunamadıysa `null`.
- **`catch (Exception ex) { ...; return null; }`** — Redis'e hiç ulaşılamazsa (bağlantı hatası vb.), exception'ı **yutuyoruz**, sadece loglayıp `null` döndürüyoruz. Yani çağıran kod açısından "Redis çöktü" ile "bu anahtar hiç yok" **ayırt edilemez** — ikisi de "normal akışa devam et" anlamına geliyor. Bu, aşağıda ayrıca açıklanan bilinçli bir "fail open" tercihi.

```csharp
    public void StoreResponse(string idempotencyKey, WorkOrderDto response)
    {
        try
        {
            _redis.GetDatabase().StringSet($"idempotency:{idempotencyKey}", JsonSerializer.Serialize(response), RecordDuration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store idempotency record for key {IdempotencyKey}", idempotencyKey);
        }
    }
}
```

- **`void` dönüş tipi** — bu metodun çağırana hiçbir şey raporlamasına gerek yok; "kaydetmeye çalış, olursa olur" mantığı (aşağıda neden açıklanıyor).
- **`_redis.GetDatabase().StringSet(key, JsonSerializer.Serialize(response), RecordDuration)`** — `response`'u (bir `WorkOrderDto`) JSON'a çevirip, Redis'in `SET <key> <value> EX 60` komutuyla, 60 saniyelik bir son kullanma tarihiyle kaydediyor. Day 48'deki `db.StringSet(cacheKey, JsonSerializer.Serialize(report), CacheDuration)` ile birebir aynı satır şekli.
- **`catch` bloğu burada da var, exception hiçbir yere fırlatılmıyor** — kayıt başarısız olsa bile (Redis erişilemezse), `Create`'in kendisi zaten başarıyla tamamlanmış durumda; bu kaydın başarısız olması, o başarıyı geri almayı gerektirmiyor.

**Neden Redis, yeni bir tablo/veritabanı değil:** Bir idempotency kaydının **kalıcı** olması gerekmiyor — sadece "yakın zamanda tekrar denenirse" senaryosunu yakalaması yeterli. Bu, tam olarak Redis'in `SET ... EX <saniye>` (TTL) özelliğinin çözdüğü problem — Day 48'deki cache-aside ile birebir aynı mekanik (`StringGet`/`StringSet`, `IConnectionMultiplexer`), sadece amaç farklı: orada "pahalı bir hesaplamayı tekrarlamamak", burada "bir işlemi tekrar yapmamak."

**Her iki metotta da `try`/`catch`'in exception yutup `null`/`void` dönmesi neden bilinçli:** Day 51/52'nin hata izolasyonu dersi burada **üçüncü kez** uygulanıyor. Redis erişilemezse, en kötü ihtimalle bir retry isteği **yanlışlıkla tekrar işlenir** (duplicate oluşabilir), ama sunucu asla çökmez/hata döndürmez. Bu bilinçli bir "fail open" (güvenli tarafta hata ver, hizmeti durdurma) tercihi: idempotency korumasının geçici olarak kaybolması, sunucunun tamamen çalışmaz hale gelmesinden daha az kötü bir sonuç.

---

## 3. `WorkOrdersController.Create` — bağlanması, satır satır

```csharp
public class IdempotencyService
{
    private static readonly TimeSpan RecordDuration = TimeSpan.FromSeconds(60);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<IdempotencyService> _logger;

    public WorkOrderDto? TryGetCachedResponse(string idempotencyKey)
    {
        try
        {
            var cached = _redis.GetDatabase().StringGet($"idempotency:{idempotencyKey}");
            return cached.HasValue ? JsonSerializer.Deserialize<WorkOrderDto>((string)cached!) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check idempotency cache for key {IdempotencyKey}", idempotencyKey);
            return null;
        }
    }

    public void StoreResponse(string idempotencyKey, WorkOrderDto response)
    {
        try
        {
            _redis.GetDatabase().StringSet($"idempotency:{idempotencyKey}", JsonSerializer.Serialize(response), RecordDuration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store idempotency record for key {IdempotencyKey}", idempotencyKey);
        }
    }
}
```

**Neden Redis, yeni bir tablo/veritabanı değil:** Bir idempotency kaydının **kalıcı** olması gerekmiyor — sadece "yakın zamanda tekrar denenirse" senaryosunu yakalaması yeterli. Bu, tam olarak Redis'in `SET ... EX <saniye>` (TTL) özelliğinin çözdüğü problem — Day 48'deki cache-aside ile birebir aynı mekanik (`StringGet`/`StringSet`, `IConnectionMultiplexer`), sadece amaç farklı: orada "pahalı bir hesaplamayı tekrarlamamak", burada "bir işlemi tekrar yapmamak."

**`TryGetCachedResponse` başarısız olursa `null` dönüyor, exception fırlatmıyor:** Day 51/52'nin hata izolasyonu dersi burada **üçüncü kez** uygulanıyor. Redis erişilemezse, bu "cache'te yok" ile aynı şekilde ele alınıyor — yani en kötü ihtimalle bir retry isteği **yanlışlıkla tekrar işlenir** (duplicate oluşabilir), ama sunucu asla çökmez/hata döndürmez. Bu bilinçli bir "fail open" (güvenli tarafta hata ver) tercihi: idempotency koruması kaybolması, sunucunun çalışmaz hale gelmesinden daha az kötü bir sonuç.

---

## 3. `WorkOrdersController.Create` — bağlanması

Tam action imzası ve akış:

```csharp
[HttpPost]
[EnableRateLimiting("PerOrganization")]
public ActionResult<WorkOrderDto> Create(
    CreateWorkOrderRequest request,
    [FromHeader(Name = "X-Organization-Id")] int? organizationId,
    [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId,
    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
{
    var membershipError = ValidateMembership(organizationId, actingEmployeeId);
    if (membershipError is not null)
    {
        return membershipError;
    }

    if (idempotencyKey is not null)
    {
        var cachedResponse = _idempotencyService.TryGetCachedResponse(idempotencyKey);
        if (cachedResponse is not null)
        {
            return StatusCode(StatusCodes.Status201Created, cachedResponse);
        }
    }

    if (request.CustomerId is not null)
    {
        var customer = _customerDirectory.GetById(request.CustomerId.Value);
        if (customer is null || customer.OrganizationId != organizationId)
        {
            return BadRequest($"Customer {request.CustomerId} does not exist.");
        }
    }

    var workOrder = _workOrderDirectory.Create(request.Title, organizationId!.Value, request.CustomerId);
    _workOrderReportService.InvalidateCache(organizationId.Value);
    var dto = ToDto(workOrder);

    if (idempotencyKey is not null)
    {
        _idempotencyService.StoreResponse(idempotencyKey, dto);
    }

    return StatusCode(StatusCodes.Status201Created, dto);
}
```

Satır satır:

- **`[FromHeader(Name = "Idempotency-Key")] string? idempotencyKey`** — Day 35'ten beri tanıdık header-okuma deseni (`[FromHeader]`), `X-Organization-Id`/`X-Employee-Id` ile aynı mekanizma. `string?` (nullable) olması kritik: header gönderilmezse, ASP.NET Core bu parametreyi otomatik olarak `null` yapıyor — `int?` ile `X-Organization-Id`'nin `0`'a değil `null`'a düşmesiyle aynı Day 35 dersi (`int?` "gerçekten yok" ile "gerçek bir değer" arasındaki farkı ifade edebiliyor, `string`'in kendisi zaten nullable olduğu için burada ekstra bir numaraya gerek yok).

- **1. adım — membership kontrolü (`ValidateMembership`)** — hiç değişmedi. Kimliksiz/yetkisiz bir istek, idempotency mantığına hiç ulaşmadan `400`/`403` alır.

- **2. adım — idempotency kontrolü:**
  ```csharp
  if (idempotencyKey is not null)
  {
      var cachedResponse = _idempotencyService.TryGetCachedResponse(idempotencyKey);
      if (cachedResponse is not null)
      {
          return StatusCode(StatusCodes.Status201Created, cachedResponse);
      }
  }
  ```
  `idempotencyKey is not null` — istemci header'ı hiç göndermediyse, bu blok tamamen atlanır, kod aşağıya (normal `Create` akışına) düşer. Header gönderildiyse, `TryGetCachedResponse` çağrılır. Cevap `null` değilse (yani bu anahtar daha önce başarıyla kullanılmış), fonksiyon **burada, hemen** `return` eder — aşağıdaki müşteri kontrolü, veritabanı yazması, cache invalidation, hiçbiri **çalışmaz**. İstemciye, ilk seferki ile **birebir aynı** `201` + `WorkOrderDto` döner.

  **Neden kontrol, membership'ten SONRA ama müşteri doğrulamasından ÖNCE konumlandırıldı:** Kimliksiz bir isteğin idempotency cache'ine bakmanın anlamı yok (zaten `400` alacak, `organizationId`'ye bile ihtiyaç duymadan erken çıkacak); ama gerçek mutasyonu (veritabanına yazma) denemeden **önce** cache kontrolü yapılmalı ki, tekrar eden bir istek **hiçbir yan etki** (ikinci bir satır, ikinci bir cache invalidation, ikinci bir audit log) yaratmasın.

- **3. adım — normal `Create` akışı** (müşteri doğrulaması, `_workOrderDirectory.Create`, `InvalidateCache`) — **hiç değişmedi**, aynı Day 47-49'daki kod.

- **4. adım — başarılı sonucu kaydetmek:**
  ```csharp
  if (idempotencyKey is not null)
  {
      _idempotencyService.StoreResponse(idempotencyKey, dto);
  }
  ```
  `dto` (yeni oluşturulan iş emrinin `WorkOrderDto`'su) hazır olduktan **sonra**, eğer istemci bir key göndermişse, bu sonucu Redis'e kaydediyoruz — böylece **bir sonraki** aynı key'li istek, 2. adımda bunu bulup direkt geri döndürecek.

  **Neden sadece başarı yolu (`return StatusCode(201, dto)`'ya giden yol) cache'leniyor, `BadRequest` dönen satırlar değil:** `request.CustomerId` geçersizse dönen `BadRequest`, hiçbir zaman `StoreResponse`'a uğramıyor (kod akışı o satıra hiç ulaşmıyor, çünkü `BadRequest` zaten `return` ediyor). Bu bilinçli: istemci geçersiz bir `customerId` gönderip hata aldıysa, muhtemelen **isteği düzeltip** (doğru `customerId` ile) gerçekten tekrar denemek isteyecektir — eğer o hatayı da cache'leseydik, düzeltilmiş istek bile hep aynı eski hatayı "replay" ederdi, asla gerçekten işlenemezdi. Idempotency'nin amacı "başarılı bir işlemi tekrarlamamak," "her yanıtı sonsuza kadar dondurmak" değil.

**Özetle bağlantı noktası tek bir string:** Controller ile servis arasındaki tüm bağlantı, `idempotencyKey` değişkeninin kendisi — `IdempotencyService`'in `Create`'in içinde neyi temsil ettiğinden (bir iş emri oluşturma isteği) hiçbir haberi yok, sadece "bu string'e karşılık gelen bir `WorkOrderDto` var mı" sorusuna cevap veriyor. Bugünkü haliyle `IdempotencyService` **`WorkOrderDto`'ya özel** — başka bir action (örn. gelecekte `Approve`) aynı korumayı isteseydi, bu servisi olduğu gibi kullanamazdı, çünkü dönüş tipi sabit kodlanmış. Bu bilinçli bir sınırlama: bugün tek bir kullanım noktası (`Create`) olduğu için genel bir `TryGetCachedResponse<T>`/`StoreResponse<T>` yazmak, henüz ihtiyacı olmayan bir soyutlama olurdu.

---

## 4. Canlı kanıt

```
1) Ayni Idempotency-Key ile IKI KEZ POST /api/workorders:
   1. cagri -> {"id":15, "title":"Day54 idempotency test", ...}
   2. cagri -> {"id":15, "title":"Day54 idempotency test", ...}   <- AYNI id!

2) sqlcmd ile WorkOrders tablosu kontrol edildi:
   -> Id=15 icin SADECE BIR satir var (iki POST'a ragmen).

3) Hic key olmadan (ya da farkli bir key ile) POST:
   -> {"id":16, ...}   <- normal sekilde YENI bir is emri olustu.

4) IdempotencyService.TryGetCachedResponse gecici olarak "throw" edecek
   sekilde degistirildi, uygulama yeniden baslatildi:
   -> Idempotency-Key ile POST -> HTTP 201 Created (!)
   -> log'da: "Failed to check idempotency cache for key failure-test-key"
   -> Idempotency kontrolu patladi ama Create yine basariyla gerceklesti.
   -> Kod geri alindi, tekrar dogrulandi.
```

---

## 5. Demo basitleştirmesi vs. üretim gereksinimi

- Demo TTL kısa (60 saniye) — üretimde tipik olarak 24 saat (Stripe'ın kullandığı standart).
- Sadece `Create` için — diğer action'lar zaten doğal olarak idempotent (aynı iş emrini iki kez `Complete` etmek zaten kendi state kontrolüyle `BadRequest` veriyor, ek bir mekanizma gerekmiyor).
- "Fail open" tercihi bilinçli: Redis erişilemezse idempotency koruması kaybolur (duplicate oluşabilir) ama sunucu asla çökmez. Gerçek üretimde, işlemin türüne göre ("fail closed" — Redis yoksa isteği tamamen reddet) tam tersi bir tercih de savunulabilir; bugünkü seçim FieldOps'un demo ölçeği için makul.

---

## Regresyon

```
dotnet test FieldOps.slnx    → 49/49 (degismedi, hicbir test Idempotency-Key gondermiyor)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## Week 10 tamamen kapandı

Redis + cache-aside (Day 48) → cache invalidation (Day 49) → background services/cache warming (Day 50) → notification abstraction (Day 51) → audit logs (Day 52) → rate limiting (Day 53) → idempotency (Day 54). Sıradaki: Week 11 (structured logging, correlation ID, health checks, configuration, Docker, CI).
