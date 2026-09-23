# Day 56 — Kod Notları

Faz 3, Hafta 11, Gün 56. Konu: **Health Checks** — `/health/live` ve `/health/ready`. Uygulamaya "sen sağlıklı mısın?" diye sormanın framework-yerleşik bir yolu.

---

## 1. Gerçek problem

Şu ana kadar FieldOps.Api'ye "sen sağlıklı mısın?" diye sormanın hiçbir yolu yoktu — gerçek bir iş uç noktasına istek atman gerekiyordu, o da alakasız bir sebepten (yanlış header, rate limit) başarısız olabilirdi. Docker/Kubernetes dünyasına girdiğimizde (bu haftanın ve Phase 5'in konusu), orkestratör "bu container'ı yeniden başlatayım mı" ve "bu instance'a trafik göndereyim mi" kararlarını tam olarak böyle bir uç noktaya bakarak veriyor.

---

## 2. Liveness vs. Readiness — iki farklı soru

- **Liveness** ("nabzın atıyor mu?"): sadece süreç ayakta mı. Hiçbir bağımlılık kontrol edilmez. Başarısız olursa: orkestratör container'ı **yeniden başlatır** (süreç muhtemelen tıkanmış/donmuş demektir).
- **Readiness** ("bugün işe yarayacak durumda mısın?"): gerçekten iş yapabilir misin — bağımlılıkların (Redis, SQL Server) çalışıyor mu. Başarısız olursa: orkestratör sadece o instance'a **trafik göndermeyi durdurur**, yeniden başlatmaz (belki geçici bir bağımlılık sorunudur, kendi kendine düzelebilir; container'ı öldürmek bunu çözmez).

---

## 3. `RedisHealthCheck`/`SqlServerHealthCheck` — satır satır

```csharp
public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis is not reachable.", ex);
        }
    }
}
```

- **`IHealthCheck`** — `Microsoft.Extensions.Diagnostics.HealthChecks`'in framework-yerleşik arayüzü, **tek metotlu**: `CheckHealthAsync`. Ek bir NuGet paketi gerekmiyor (Testcontainers gibi üçüncü parti bir şey değil, .NET'in kendi parçası).
- **`PingAsync()`** — Redis'e gerçekten bir `PING` komutu gönderiyor, sadece "bağlantı nesnesi var mı" değil, "gerçekten cevap veriyor mu" diye soruyor.
- **`HealthCheckResult.Healthy(...)`/`Unhealthy(..., ex)`** — framework'ün beklediği dönüş tipi; `Unhealthy` bir `Exception` de taşıyabiliyor, detaylı teşhis için.

```csharp
public class SqlServerHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return HealthCheckResult.Healthy("SQL Server is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQL Server is not reachable.", ex);
        }
    }
}
```

**Neden bir `DbContext` değil, sadece `string _connectionString`:** Host'un (`FieldOps.Api`) hiçbir modülün `internal` `DbContext`'ine proje referansı yok (ADR 0001/0002). Bir health check'in tek ihtiyacı "gerçekten bağlanabiliyor muyum" — bunun için ham bir `SqlConnection` açıp kapatmak yeterli, hiçbir modül sınırını ihlal etmeden.

---

## 4. `Program.cs` — kayıt ve uç noktalar

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"])
    .AddTypeActivatedCheck<SqlServerHealthCheck>(
        "workorders-db",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        args: [RequireConnectionString("FieldOpsWorkOrdersDb")]);
```

- **`AddCheck<RedisHealthCheck>`** — `RedisHealthCheck`'in tek bağımlılığı (`IConnectionMultiplexer`) zaten DI'da kayıtlı olduğu için, framework onu otomatik çözüp örnek oluşturabiliyor.
- **`AddTypeActivatedCheck<SqlServerHealthCheck>(..., args: [connectionString])`** — `SqlServerHealthCheck`'in constructor'ı DI'dan **çözülemeyen** bir `string` parametresi alıyor (bir connection string, DI container'da "kayıtlı bir servis" değil). `AddTypeActivatedCheck`, tam olarak bu durum için var: "bu tipi oluştururken, DI'nın bilmediği şu ekstra parametreleri de ver."
- **`tags: ["ready"]`** — her iki kontrol de `"ready"` etiketiyle işaretleniyor; bu, aşağıdaki uç nokta tanımlarının hangi kontrolleri çalıştıracağını seçmesini sağlıyor.

```csharp
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
```

- **`Predicate = _ => false`** — "hiçbir kayıtlı kontrolü çalıştırma" demek. `/health/live` bu yüzden Redis'e ya da SQL Server'a hiç dokunmuyor — sadece "bu uç noktaya cevap verebiliyorsam, sürecim ayakta" diye `200` dönüyor.
- **`Predicate = check => check.Tags.Contains("ready")`** — sadece `"ready"` etiketli kontrolleri (yani ikisini de) çalıştır, sonuçlarını birleştirip tek bir `200`/`503` üret.

---

## 5. Canlı kanıt — asıl ayrımın gerçekten var olduğu

```
1) Redis ve SQL Server ayaktayken:
   /health/live  -> 200
   /health/ready -> 200

2) docker stop fieldops-redis (Redis GERCEKTEN durduruldu):
   /health/live  -> HALA 200  (hicbir bagimliliga dokunmuyor, dogru davranis)
   /health/ready -> 503 Unhealthy  (Redis kontrolu basarisiz oldu)

3) docker start fieldops-redis:
   /health/ready -> tekrar 200
```

Bu, ikinci adımın en kritik kanıtı: `/health/live` ve `/health/ready`'nin **gerçekten farklı şeyler** kontrol ettiğinin ispatı — biri bağımlılık kesintisinden etkilenmiyor, diğeri anında yansıtıyor.

---

## 6. Demo basitleştirmesi vs. üretim gereksinimi

- Bugün **beş** veritabanından sadece **biri** (WorkOrders) kontrol ediliyor — bilinçli bir kapsam daraltması. Gerçek üretimde ya hepsi kontrol edilir, ya da (bu demoda olduğu gibi) hepsi aynı fiziksel `SQLEXPRESS` örneğinde olduğu için birinin durumu diğerlerini temsil ettiği kabul edilir.
- `Microsoft.Data.SqlClient` paketi `FieldOps.Api.csproj`'a **açıkça** eklendi (daha önce sadece modüller üzerinden dolaylı/transitive olarak erişilebilirdi) — host artık bu tipi doğrudan kullandığı için bağımlılığı açık tutmak, "neden bu paket burada" sorusuna her zaman net bir cevap olması için.

---

## Regresyon

```
dotnet test FieldOps.slnx    → 49/49 (degismedi)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```
