# Day 48 — Redis Kurulumu: Referans Notu

Bu, `day-48.md`'nin yanında, sadece "Redis nedir, ne işe yarar, nasıl kurduk, kodda neyi neyle değiştirdik/ekledik, neden öyle yaptık ve bize ne kazandırdı" sorularına odaklanan ayrı bir referans notu — Day 24'ün `day-24-refresh-akisi-detay.md`'si ve bugünün `day-48-persistence-detay.md`'siyle aynı gerekçeyle (ana anlatı yeterince net değilse, ayrı ve odaklı bir detay dosyası).

---

## 1. Redis nedir — zihinsel model

Redis, **RAM'de çalışan bir key-value veritabanı**. SQL Server gibi disk üzerinde satır/sütun tutan, `JOIN`/`WHERE` çalıştırabilen bir veritabanı değil — Redis'e sorabileceğin tek soru şu: *"Bu anahtarın (key) değeri (value) ne?"* Karşılığında bu sadeliğin bedelini hızla ödüyor: SQL Server'a giden bir sorgu disk I/O + sorgu planlayıcı + network gerektirirken, Redis'e giden bir okuma sadece bellekten bir string döndürüyor — mikrosaniyeler seviyesinde.

Zihinsel model: Redis'i bir `Dictionary<string, string>` gibi düşün, ama ağ üzerinden erişilebilir, ve her anahtarın bir **TTL** (yaşam süresi) olabilir — süre dolunca anahtar kendiliğinden silinir. Bu yüzden Redis'i **birincil veri kaynağı** değil, SQL Server'ın önünde bir **hızlandırma katmanı (cache)** olarak kullanıyoruz.

---

## 2. Çözülen gerçek problem

`GET /api/workorders/report` çağrıldığında, `_workOrderDirectory.GetAll()` artık (Day 48'in ilk yarısından beri, persistence eklendikten sonra) gerçek bir SQL sorgusu — `EfWorkOrderDirectory` içinde `_dbContext.WorkOrders.ToList()` çalıştırıp **tüm** `WorkOrders` tablosunu okuyor, sonra bellekte filtreliyor. Bir organizasyonun binlerce iş emri olduğunu, ve bu raporu bir dashboard'un saniyede birkaç kez çağırdığını düşün: aynı soruyu (org X'in durum sayıları nedir?) kısa aralıklarla tekrar tekrar SQL Server'a sormak, sunucuyu gereksiz yere yoruyor — oysa bu rapor verisi birkaç saniye bayat olsa bile kimse fark etmez.

**Redis'in çözdüğü problem tam olarak bu:** aynı cevabı bir süreliğine RAM'de tutup SQL Server'ı bu tekrarlanan yükten kurtarmak. Bunu, uydurma bir "yapay olarak yavaş" örnekle değil, gerçek bir sorguyla devreye soktuk — bu yüzden Redis'i, gerçek persistence tamamlanana kadar beklettik.

---

## 3. Kurulum, adım adım

```bash
docker run -d --name fieldops-redis -p 6379:6379 redis:7-alpine
```

Parça parça:
- `docker run` → yeni bir konteyner (izole, kendi dosya sistemine/process alanına sahip mini bir ortam) başlat.
- `redis:7-alpine` → Docker Hub'daki resmi Redis image'ının 7. sürümü, `alpine` (çok küçük bir Linux dağıtımı) tabanlı hafif versiyonu. İçinde zaten derlenmiş Redis sunucusu var — kurulum yapmıyoruz, hazır paketi indirip çalıştırıyoruz.
- `-d` → "detached", konteyner arka planda çalışsın, terminali bloklamasın.
- `--name fieldops-redis` → ileride `docker exec fieldops-redis ...` ile ona komut gönderebilmek için bir isim.
- `-p 6379:6379` → **port mapping**. Redis, konteynerin içinde kendi izole ağında `6379`'u (Redis'in varsayılan portu) dinliyor. Bu satır, host makinenin `6379` portunu konteynerin `6379` portuna bağlıyor — yani `localhost:6379`'a bağlanan `FieldOps.Api`, aslında konteynerin içindeki Redis'e konuşuyor.

**Bunun Testcontainers'la kurduğumuz SQL Server'dan farkı önemli:** Testcontainers'daki SQL Server her test çalıştırmasında doğup testler bitince otomatik silinen, **geçici** bir konteyner. `fieldops-redis` ise `docker run` ile elle başlattığımız, **kalıcı** bir konteyner — bilgisayarı kapatana ya da elle durdurana kadar arka planda çalışmaya devam eder, tıpkı senin `SQLEXPRESS` kurulumun gibi. Gerçek uygulamanın dev ortamında her çalıştığında kullanacağı kalıcı bir altyapı parçası olarak kurduk (Testcontainers değil, çünkü Testcontainers zaten "testler bitince yok ol" davranışı için tasarlanmış).

Doğrulama:
```bash
docker exec fieldops-redis redis-cli PING   # -> PONG
```
`redis-cli`, Redis'in kendi komut satırı istemcisi (konteynerin içinde hazır gelir). `PING`, sunucunun ayakta ve komut kabul ettiğini sınayan en temel komut.

---

## 4. Kodda ne değişti, adım adım, neden

### 4.1. `appsettings.Development.json` — bağlantı bilgisi

```json
"Redis": { "ConnectionString": "localhost:6379" }
```

SQL Server bağlantı dizeleriyle aynı mantık: "Redis nerede?" bilgisini kod içine gömmek yerine konfigürasyondan okuyoruz.

### 4.2. `StackExchange.Redis` paketi

.NET dünyasında Redis'e bağlanmanın fiili standardı. Redis kendisi bir network protokolü (RESP) konuşan bir sunucu; bu paket, protokolü senin yerine konuşup C# nesneleri (`IConnectionMultiplexer`, `IDatabase`) sunan client kütüphanesi. EF Core'un SQL Server'a karşı gördüğü işi, bu paket Redis'e karşı görüyor.

### 4.3. `Program.cs` — bağlantıyı kaydetme

```csharp
var redisConnectionString = builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Missing configuration: Redis:ConnectionString");
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddScoped<WorkOrderReportService>();
```

- `IConnectionMultiplexer` — `StackExchange.Redis`'in ana bağlantı nesnesi. Adı ("çoklayıcı") şuradan geliyor: tek bir `IConnectionMultiplexer`, Redis'e açtığı **tek bir TCP bağlantısını**, aynı anda gelen onlarca isteği arasında paylaştırıp verimli kullanıyor — bu yüzden onu her HTTP isteğinde yeniden oluşturmuyoruz.
- **Neden `AddSingleton` (Scoped değil)?** `AddScoped` olsaydı, her HTTP isteği için yeni bir bağlantı (TCP handshake) açılıp kapanacaktı — pahalı bir iş, tam da Redis'i hızlı kılan şeyin tersi. `AddSingleton`, uygulamanın tüm ömrü boyunca **tek bir bağlantı nesnesi** kullanılmasını sağlıyor; `StackExchange.Redis` bu tek bağlantıyı thread-safe paylaşmak üzere zaten tasarlanmış.
- **Neden lambda (`_ => ConnectionMultiplexer.Connect(...)`) — "tembel" (lazy) kayıt?** Bu satır `ConnectionMultiplexer.Connect`'i **hemen** çalıştırmıyor; DI container'a "birisi `IConnectionMultiplexer` istediğinde bu kodu çalıştır, bir daha da çalıştırma" diyor. Yani uygulama `dotnet run` ile ayağa kalktığı anda Redis'e bağlanmıyor — ilk kez `WorkOrderReportService` (yani ilk kez `/report` çağrıldığında) devreye girdiğinde bağlanıyor.
- **Bunu varsaymadık, canlı doğruladık:** mevcut 49 testin hiçbiri `/report`'u çağırmıyor, bu yüzden Redis konteyneri hiç ayakta olmasa bile o 49 test yine 49/49 geçiyor — testler Redis'in varlığına hiç bağımlı değil.

### 4.4. `WorkOrderReportService` — cache-aside mantığının kendisi

```csharp
public class WorkOrderReportService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private readonly IWorkOrderDirectory _workOrderDirectory;
    private readonly IConnectionMultiplexer _redis;

    public WorkOrderReportService(IWorkOrderDirectory workOrderDirectory, IConnectionMultiplexer redis)
    {
        _workOrderDirectory = workOrderDirectory;
        _redis = redis;
    }

    public WorkOrderStatusReport GetStatusReport(int organizationId)
    {
        var db = _redis.GetDatabase();
        var cacheKey = $"workorders:report:{organizationId}";

        var cached = db.StringGet(cacheKey);
        if (cached.HasValue)
        {
            return JsonSerializer.Deserialize<WorkOrderStatusReport>((string)cached!)!;
        }

        var workOrders = _workOrderDirectory.GetAll()
            .Where(w => w.OrganizationId == organizationId)
            .ToList();

        var report = new WorkOrderStatusReport(
            organizationId,
            Open: workOrders.Count(w => w.Status == WorkOrderStatus.Open),
            Assigned: workOrders.Count(w => w.Status == WorkOrderStatus.Assigned),
            InProgress: workOrders.Count(w => w.Status == WorkOrderStatus.InProgress),
            Completed: workOrders.Count(w => w.Status == WorkOrderStatus.Completed));

        db.StringSet(cacheKey, JsonSerializer.Serialize(report), CacheDuration);
        return report;
    }
}
```

Adım adım ne oluyor:
1. `_redis.GetDatabase()` → `IConnectionMultiplexer`'dan komut gönderebileceğimiz bir `IDatabase` alıyoruz. (Redis'te "database" SQL Server'daki gibi ayrı bir şema değil — Redis'in içinde numaralı, çok basit mantıksal bölmeler var, varsayılanı (0) kullanıyoruz.)
2. `db.StringGet(cacheKey)` → alttan alta Redis'in `GET <key>` komutu. "Bu anahtarın değeri var mı, varsa ne?"
3. `cached.HasValue` → anahtar Redis'te yoksa (hiç yazılmamış ya da TTL'i dolup silinmişse) `false` döner — bu bir **cache miss**.
4. Miss durumunda gerçek işi yapıyoruz: `IWorkOrderDirectory.GetAll()` ile SQL Server'a gidip sayıyoruz, `WorkOrderStatusReport` üretiyoruz. Dikkat: `WorkOrderReportService`, `IWorkOrderDirectory`'yi (modülün genel arayüzü) kullanıyor — `EfWorkOrderDirectory`'yi (somut EF Core implementasyonu) hiç adıyla görmüyor, tıpkı controller'ların hiç görmediği gibi.
5. `db.StringSet(cacheKey, JsonSerializer.Serialize(report), CacheDuration)` → alttan alta Redis'in `SET <key> <value> EX 30` komutu. Üç şey oluyor:
   - `JsonSerializer.Serialize(report)` — Redis C# nesnesi bilmiyor, sadece string/byte saklıyor, bu yüzden nesneyi JSON metnine çeviriyoruz.
   - Bu JSON'ı `workorders:report:1` anahtarıyla Redis'e yazıyoruz.
   - `CacheDuration` (30 saniye) — **TTL** parametresi, Redis'e "bu anahtarı 30 saniye sonra kendiliğinden sil" diyor. Bu, Redis'in kendi yerleşik özelliği (`EXPIRE`) — biz elle bir silme tetiklemiyoruz.
6. Sonraki çağrı (30 saniye içinde, aynı organizasyon için) artık `StringGet` ile **cache hit** alıyor, SQL Server'a hiç gitmeden JSON'ı deserialize edip dönüyor.

### 4.5. Karşılaşılan gerçek derleme hatası (CS0121)

`JsonSerializer.Deserialize<WorkOrderStatusReport>(cached!)` yazınca derleyici **belirsiz çağrı** hatası verdi, çünkü `RedisValue` (Redis'ten dönen tip) hem `string`'e hem `ReadOnlySpan<byte>`'a örtük dönüşebiliyor, ve `Deserialize`'ın her ikisini de alan aşırı yüklemesi (overload) var — derleyici hangisini kastettiğimi çıkaramadı. Çözüm: `(string)cached!` ile açık cast.

### 4.6. `WorkOrdersController` — yeni uç nokta

```csharp
[HttpGet("report")]
public ActionResult<WorkOrderStatusReport> GetStatusReport(
    [FromHeader(Name = "X-Organization-Id")] int? organizationId,
    [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId)
{
    var membershipError = ValidateMembership(organizationId, actingEmployeeId);
    if (membershipError is not null) { return membershipError; }
    var report = _workOrderReportService.GetStatusReport(organizationId!.Value);
    return Ok(report);
}
```

Diğer tüm action'larla aynı membership-kontrolü deseni (Day 33'ten beri) — cache'in kendisi bu kontrolü baypas etmiyor, her istekte önce "bu çalışan gerçekten bu organizasyonda mı?" sorusu yanıtlanıyor, ondan sonra rapora (cache'ten ya da DB'den) bakılıyor.

---

## 5. Bu uç noktada Redis'e özel kullandığımız şeyler

- **`String` veri tipi** — Redis aslında List, Set, Hash, Sorted Set gibi başka veri yapıları da sunar. Biz en basitini kullandık, çünkü sakladığımız şey tek bir JSON blob'u; ayrı alanlara Redis seviyesinde erişmemiz gerekmiyor.
- **TTL / `EXPIRE`** — cache-aside'ın can damarı. Aktif invalidation yazmadığımız için (bkz. bölüm 7), veri en kötü ihtimalle 30 saniye bayat kalabilir ama sonsuza kadar bayat kalmaz.
- **Anahtar isimlendirme deseni** (`workorders:report:{organizationId}`) — Redis'te "namespace" kavramı yok, bu yüzden çakışmayı önlemek ve okunabilirlik için iki-noktayla ayrılmış hiyerarşik isimlendirme, Redis topluluğunun yaygın bir konvansiyonu. Her organizasyon kendi anahtarına sahip; bir organizasyonun raporunu cache'lemek başka bir organizasyonunkini etkilemiyor.

---

## 6. Asıl kanıt — cache gerçekten okunuyor mu, yoksa tesadüfen aynı sonuç mu geliyor?

İlk çağrıdan sonra Redis'te `workorders:report:1` anahtarının oluştuğunu gördük, ama org 1'de hiç iş emri olmadığı için ikinci çağrının da aynı (sıfır) sonucu vermesi, hem "cache'ten okundu" hem "yeniden hesaplandı ama tesadüfen aynı" ile tutarlıydı — kanıt değildi. Bunu kesinleştirmek için:

```
1) Redis'teki degeri elle, gercek veriyle CELISECEK bir sey yap:
   redis-cli SET "workorders:report:1" '{"Open":999,"Assigned":999,"InProgress":999,"Completed":999}'
2) API'yi tekrar cagir -> 999'lari dondurdu!
   -> Bu, endpoint'in gercekten Redis'ten okudugunu kanitliyor (DB'de asla 999 olamaz).
3) Bozuk anahtari sil (redis-cli DEL "workorders:report:1"), tekrar cagir
   -> Dogru degerler (0'lar) yeniden hesaplandi ve Redis'e doğru şekilde geri yazıldı.
```

Bu, StockPilot/RoadmapOS'ta defalarca uyguladığımız "framework'ün ne yaptığını varsayma, gözlemle" ilkesinin Redis'e uygulanmış hali — ve aynı zamanda gerçek bir SQL Server mutasyonuna/temizliğine gerek kalmadan yapılabilen, daha temiz bir kanıt yöntemi.

---

## 7. Demo basitleştirmesi vs. üretim gereksinimi

Bugün sadece **TTL'e dayalı** (30 saniye) süre sonu var — bir iş emrinin durumu (`Assign`/`Start`/`Complete`/`Approve` gibi mutasyonlarla) değiştiğinde cache'i **aktif olarak** geçersiz kılan (invalidation) bir mekanizma yok. Bu, bilinçli ve dokümante edilmiş bir eksik, unutkanlık değil: gerçek üretimde, bu mutasyonlardan hemen sonra ilgili `workorders:report:{organizationId}` anahtarının Redis'ten silinmesi gerekir — böylece bir sonraki okuma güncel veriyi yeniden hesaplar. Bu, ayrı bir gün olarak planlanıyor.

---

## Kısa özet

**Neyi çözdük:** `/report` uç noktasının, artık gerçek bir SQL sorgusuna dayandığı için tekrarlanan çağrılarda SQL Server'ı gereksiz yormasını.

**Neyi ekledik:** Kalıcı bir Docker Redis konteyneri (`fieldops-redis`), `StackExchange.Redis` paketi, `IConnectionMultiplexer`'ın lazy Singleton kaydı, ve cache-aside mantığını uygulayan `WorkOrderReportService`.

**Nasıl bağladık:** `appsettings.Development.json`'daki `Redis:ConnectionString` → `Program.cs`'te `AddSingleton<IConnectionMultiplexer>` (lazy) → `WorkOrderReportService` → `db.StringGet`/`StringSet` (TTL'li) → `WorkOrdersController`'ın yeni `GET /api/workorders/report` action'ı.

**Neyi kanıtladık:** Cache'in gerçekten okunduğunu (elle bozup okutarak), TTL sonrası doğru yeniden hesaplandığını, ve mevcut testlerin Redis'in varlığına hiç bağımlı olmadığını (lazy connection sayesinde).
