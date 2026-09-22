# Day 50 — Kod Notları

Faz 3, Hafta 10, Gün 50. Konu: **Background services & scheduled jobs** — Node.js'teki `node-cron`/PM2 cron job'ların .NET karşılığı: `BackgroundService`. Amaç, `GET /api/workorders/report`'un Redis cache'ini, hiçbir HTTP isteğine bağlı olmadan, düzenli aralıklarla kendiliğinden tazelemek ("cache warming").

---

## 1. Gerçek problem

Day 48-49'daki cache-aside tamamen **reaktif**: cache boşalınca (TTL doldu ya da bir mutasyon onu sildi), o anda gelen **ilk** isteğin sahibi gerçek SQL sorgusunun tüm maliyetini üstleniyor. Bir dashboard sürekli otomatik yenileniyorsa, bu "ilk şanssız kullanıcı yavaş yaşar" davranışı gerçek kullanıcı deneyimini etkiler. Çözüm: kimse istemeden, arka planda, cache'i düzenli aralıklarla tazeleyen bir iş.

---

## 2. İlk (bilinçli olarak yanlış) versiyon — gerçek DI hatasını canlı görmek için

```csharp
public class WorkOrderReportCacheWarmer : BackgroundService
{
    private readonly IOrganizationDirectory _organizationDirectory;
    private readonly WorkOrderReportService _workOrderReportService;

    public WorkOrderReportCacheWarmer(IOrganizationDirectory organizationDirectory, WorkOrderReportService workOrderReportService)
    {
        _organizationDirectory = organizationDirectory;
        _workOrderReportService = workOrderReportService;
    }
    // ...
}
```

`builder.Services.AddHostedService<WorkOrderReportCacheWarmer>()` ile kaydedilip `dotnet run` çalıştırıldığında, uygulama **hiç başlamadan** şu hatayı verdi:

```
Cannot consume scoped service 'FieldOps.Modules.Organizations.IOrganizationDirectory'
from singleton 'Microsoft.Extensions.Hosting.IHostedService'.
```

**Neden bu hata oluyor:** `AddHostedService<T>`, `T`'yi her zaman **Singleton** olarak kaydeder (uygulamanın tüm ömrü boyunca tek bir örnek yaşar — `dotnet run` başladığında bir kere oluşturulur, kapanana kadar aynı nesne kalır). Ama `IOrganizationDirectory` ve `WorkOrderReportService` **Scoped** (her HTTP isteği için ayrı bir örnek). ASP.NET Core'un DI container'ı, `Development` ortamında uygulama başlarken bu tür kayıtları **doğrular** (`ServiceProviderOptions.ValidateScopes`) ve bir Singleton'ın bir Scoped servisi doğrudan tutmasına izin vermez — çünkü bu, Scoped servisin "her istek için taze" garantisini kırar (bir DbContext gibi, tüm uygulama ömrü boyunca tek bir örnek paylaşılırsa, eşzamanlı isteklerde veri karışması/thread-safety sorunları çıkar).

---

## 3. Doğru versiyon — `IServiceScopeFactory`

```csharp
public class WorkOrderReportCacheWarmer : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkOrderReportCacheWarmer> _logger;

    public WorkOrderReportCacheWarmer(IServiceScopeFactory scopeFactory, ILogger<WorkOrderReportCacheWarmer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            WarmAllOrganizations();
        }
    }

    private void WarmAllOrganizations()
    {
        using var scope = _scopeFactory.CreateScope();
        var organizationDirectory = scope.ServiceProvider.GetRequiredService<IOrganizationDirectory>();
        var workOrderReportService = scope.ServiceProvider.GetRequiredService<WorkOrderReportService>();

        foreach (var organization in organizationDirectory.GetAll())
        {
            try
            {
                workOrderReportService.GetStatusReport(organization.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warm work order report cache for organization {OrganizationId}", organization.Id);
            }
        }
    }
}
```

Satır satır:
- **`BackgroundService`** — `Microsoft.Extensions.Hosting`'in soyut temel sınıfı. Tek yapman gereken `ExecuteAsync`'i override etmek; host (uygulama başlarken) bunu otomatik çağırır, uygulama kapanırken de `stoppingToken`'ı tetikleyip düzgün durmasını sağlar. Bu, senin Node.js'teki bir cron job kaydetmenin .NET karşılığı — ek bir kütüphane (Hangfire/Quartz) gerekmeden, framework'ün kendi yerleşik mekanizması.
- **`IServiceScopeFactory`** — kendisi Singleton-güvenli bir servis; tek işi, istendiğinde **yeni bir DI scope** üretmek. ASP.NET Core, her HTTP isteği geldiğinde arka planda tam olarak bunu yapıyor zaten (her isteğe kendi scope'unu açıyor) — biz burada aynı mekanizmayı, isteğe değil, zamanlayıcının her turuna bağlıyoruz.
- **`_scopeFactory.CreateScope()`** — yeni bir scope açar; `using` ile scope, iş bitince (`WarmAllOrganizations` dönünce) atılır — bu, o turda scope içinde çözülen her Scoped servisin de birlikte temizlenmesini sağlar (tıpkı bir HTTP isteği bitince kendi scope'unun temizlenmesi gibi).
- **`scope.ServiceProvider.GetRequiredService<T>()`** — o scope'un kendi DI container'ından `T`'yi çöz. Constructor injection değil, çünkü `WorkOrderReportCacheWarmer`'ın kendisi Singleton — bu servisleri hiçbir zaman kendi alanlarında (field) tutamaz, her turda yeniden, taze bir scope'tan almalı.
- **`PeriodicTimer`** — `.NET 6+`'nın modern zamanlayıcısı; `WaitForNextTickAsync(stoppingToken)` her tur `Interval` kadar bekler ve `true` döner; `stoppingToken` iptal edilirse (uygulama kapanırken) `false` döner ve döngü doğal olarak biter — eski `Timer`/`Task.Delay` döngülerine göre iptal ve arka arkaya tur atlamama garantisi daha temiz.
- **Her organizasyon için ayrı `try`/`catch`** — bir organizasyonu tazelerken bir hata olursa (örn. geçici bir SQL Server/Redis kesintisi), bu hem o turdaki **diğer** organizasyonları hem de **gelecekteki** turları etkilememeli. `catch` olmasaydı, tek bir organizasyondaki hata `ExecuteAsync`'in tüm döngüsünü sonlandırır, bir daha hiç çalışmayan sessiz bir servis bırakırdı.
- **`ILogger<WorkOrderReportCacheWarmer>`** — hata olduğunda sessizce yutmak yerine loglamak; gerçek üretimde bu loglar, warmer'ın gerçekten çalışıp çalışmadığını izlemenin tek yolu (kimse bu servisi HTTP ile "test edemez").

`Program.cs`: `builder.Services.AddHostedService<WorkOrderReportCacheWarmer>();`

---

## 4. Canlı kanıt — hiçbir istek olmadan cache kendiliğinden doluyor

```
1) Redis'te workorders:report:* anahtarlari temizlendi.
2) Uygulama baslatildi (dotnet run) -> DI hatasi YOK, temiz acildi.
3) 12 saniye beklendi (interval=10sn) -> HICBIR /report istegi yapilmadi.
4) redis-cli KEYS "workorders:report:*" -> workorders:report:1 VE workorders:report:2 zaten orada!
5) redis-cli GET ile icerikleri kontrol edildi -> gercek, dogru hesaplanmis JSON raporlar.
6) Loglarda hic warning/error yok (basarili calisma).
7) Normal bir client GET /report cagrisi -> ayni veriyi aninda dondurdu (cache hit).
```

Bu, warmer'ın gerçekten kendi başına, hiçbir client isteği olmadan çalıştığının doğrudan kanıtı — varsayılmadı, gözlemlendi.

---

## 5. Demo basitleştirmesi vs. üretim gereksinimi

- **Interval:** Bugün 10 saniye (gözle görülür olsun diye). Üretimde TTL'in (30sn) hemen altına (örn. 25sn) ayarlanmalı ki cache neredeyse hiç soğumasın.
- **Tek instance varsayımı:** Bugün tek bir `FieldOps.Api` süreci çalışıyor. Gerçek, yatay ölçeklenen bir dağıtımda (birden fazla instance) bu servis her instance'ta ayrı ayrı çalışıp aynı işi tekrarlardı — zararsız (idempotent, ucuz) ama gereksiz. Gerçek çözüm (distributed lock/leader election, "sadece bir instance çalıştırsın") Phase 4'ün konusu; bugün sadece işaretlendi, çözülmedi.
- **`try`/`catch` yolu canlı test edilmedi:** Bir organizasyonu tazelerken gerçekten bir hata fırlatmasını tetikleyip loglamanın çalıştığını görmedik — kod okunarak güvenilir kabul edildi (Day 19'un Layer 2 boşluğuyla aynı sınıf, dürüstçe kaydedilen bir sınır).

---

## Regresyon

```
dotnet test FieldOps.slnx    → 49/49 (degismedi)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

**Ortamsal not:** Bugünün ilk test çalıştırmasında Docker Desktop kapalıydı (47/49 test, Testcontainers Docker'a ulaşamadığı için başarısız) — Day 34'teki aynı sınıf, koddan bağımsız bir durum. Docker Desktop başlatılıp `fieldops-redis` konteyneri yeniden ayağa kaldırıldıktan sonra 49/49'a döndü.
