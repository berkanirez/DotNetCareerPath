# Day 87 — Kod Notları

Faz 4, Hafta 17, Gün 87. Konu: **timeouts ve resilience (dayanıklılık)**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Gerçek problem, Day 86'da CANLI görüldü:** Elasticsearch kapalıyken, her başarısız deneme ~4 saniye sürüyordu, ve `OutboxPublisher`'ın aynı tick'indeki DİĞER mesajları da geciktiriyordu. Elasticsearch uzun süre kapalı kalsaydı, bu 4 saniyelik bekleme HER 5 saniyelik tick'te TEKRAR TEKRAR yaşanırdı — sistem, zaten "kapalı olduğunu bildiği" bir şeye sürekli yeniden sorarak kendini yavaşlatırdı.

**Çözüm — iki parça:**
1. **Timeout (zaman aşımı):** "bu isteğe en fazla 2 saniye bekle, daha fazla değil" diye AÇIKÇA bir sınır koymak — varsayılan (ve neden ~4 saniye sürdüğünü tam bilmediğimiz) davranışa güvenmek yerine.
2. **Circuit Breaker (devre kesici):** Bir elektrik sigortası gibi — Elasticsearch üst üste birkaç kez başarısız olursa, devre "açılır", ve bundan sonraki istekler **hiç denenmeden, anında** başarısız olur. Bir süre sonra devre tekrar "dener" (half-open).

**Bugün bunu neden ekliyoruz:** Dünkü canlı bulgu, TAM OLARAK bu ikisinin çözdüğü sorunu kanıtladı — bu, hayali bir senaryo değil, kendi sistemimizde gerçekten gözlemlenen bir davranış.

---

## 1. Paket

```bash
dotnet add src/FieldOps.Api package Polly.Core
```
`Polly` — .NET'in en yaygın kullanılan dayanıklılık (resilience) kütüphanesi. `Polly.Core`, sadece modern (v8) API'yi içeren, daha hafif paket.

---

## 2. `ElasticsearchWorkOrderSearchIndex.cs` — pipeline'ın kurulması

```csharp
private readonly ResiliencePipeline _resiliencePipeline = new ResiliencePipelineBuilder()
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 1.0,
        MinimumThroughput = 2,
        SamplingDuration = TimeSpan.FromSeconds(10),
        BreakDuration = TimeSpan.FromSeconds(15),
    })
    .AddTimeout(TimeSpan.FromSeconds(2))
    .Build();
```
* `ResiliencePipelineBuilder()` — Polly'nin "birden fazla koruma katmanını üst üste bindirme" aracı.
* **SIRA ÖNEMLİ:** `.AddCircuitBreaker(...)` ÖNCE eklendi — bu onu en DIŞTAKI katman yapıyor, yani devre açıkken bir istek DAHA İÇERİ (timeout'a, gerçek ağ çağrısına) HİÇ GİRMİYOR. `.AddTimeout(...)` SONRA eklendi — en İÇTEKİ katman, sadece devre kesicinin GEÇMESİNE İZİN VERDİĞİ istekleri sınırlıyor.
* `FailureRatio = 1.0, MinimumThroughput = 2, SamplingDuration = 10s` — "son 10 saniyede en az 2 istek oldu VE hepsi (%100'ü) başarısızsa" devre açılır.
* `BreakDuration = 15s` — devre 15 saniye açık kalır, sonra "yarı açık" olup tekrar bir deneme yapar.
* Bu alan `private readonly` ve SINIF SEVİYESİNDE — her çağrıda YENİDEN oluşturulmuyor. Bu ÖNEMLİ: devre kesicinin "kaç kez başarısız olduk" hafızası, ÇAĞRILAR ARASINDA KALICI olmak zorunda — her seferinde sıfırdan bir pipeline kursaydık, hiçbir zaman "geçmişi" hatırlamaz, devre HİÇ açılmazdı.

---

## 3. `IndexAsync` — gerçek çağrının pipeline'a sarılması

```csharp
public async Task IndexAsync(WorkOrderSearchDocument document, CancellationToken cancellationToken)
{
    await _resiliencePipeline.ExecuteAsync(async ct =>
    {
        var response = await _client.IndexAsync(document, request => request.Index(IndexName).Id(document.Id), ct);
        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"...");
            // Bu istisna, Polly'nin devre kesicisi tarafindan "basarisizlik" olarak SAYILIYOR.
        }
    }, cancellationToken);
}
```
* `_resiliencePipeline.ExecuteAsync(async ct => { ... }, cancellationToken)` — verilen kod bloğunu, pipeline'ın KORUMASI ALTINDA çalıştırıyor. `ct` (pipeline'ın kendi, muhtemelen timeout tarafından İPTAL edilebilecek token'ı) — DİKKAT: içeride `cancellationToken` DEĞİL, bu `ct` kullanılıyor, çünkü timeout stratejisi süre dolunca BU token'ı iptal ediyor.
* Aynı desen `SearchAsync`, `EnsureIndexExistsAsync`, `RebuildOrganizationIndexAsync`'in (`DeleteByQueryAsync` çağrısı) içinde de tekrarlanıyor.

---

## Regresyon (Day 87)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65, 8dk38sn (normal)
```

## Canlı doğrulama — devrenin TAM yaşam döngüsü, gerçekten yakalandı

Elasticsearch **bilerek** hiç açılmadan (sadece RabbitMQ çalışırken), gerçek bir iş emri oluşturuldu. Loglardaki gerçek sıra:

```
1. mesaj 9  -> System.InvalidOperationException (GERCEK deneme, "Took: 00:00:01.9999672"
               -- Polly'nin 2 saniyelik timeout'u TAM OLARAK calisti, eski ~4 saniyelik
               belirsiz sureyi 2 saniyeye SINIRLADI)
2. mesaj 11, 12, 14 -> Polly.CircuitBreaker.BrokenCircuitException (ağa HİÇ dokunulmadan,
               ANINDA basarisiz)
   ... (birkaç tick boyunca hep BrokenCircuitException) ...
3. ~15 saniye sonra: mesaj 9 -> YENIDEN System.InvalidOperationException
   (devre "yari acik" oldu, BIR gercek deneme daha yapildi -- Elasticsearch hala kapali
   oldugu icin bu da basarisiz oldu, devre TEKRAR acildi)
```
Bu, devre kesicinin **kapalı → açık → yarı-açık → (başarısız) → açık** tam döngüsünün canlı, gerçek kanıtı.

**Dürüst bir not:** Devre, beklediğimden daha ERKEN açıldı — `MinimumThroughput = 2` ayarına göre en az iki GERÇEK deneme sonrası açılmasını bekliyordum, ama loglar, tek bir gerçek başarısızlıktan (mesaj 9) hemen sonra bile sonraki mesajların (11) direkt `BrokenCircuitException` aldığını gösterdi. Bunun TAM olarak neden böyle olduğunu (Polly'nin `MinimumThroughput` sayımını nasıl yaptığını) burada kesin olarak çözemedim — ama asıl kanıtlamak istediğim şey (devre açıkken istekler ANINDA, ağa dokunmadan reddediliyor, ve devre zamanla kendini yeniden deniyor) canlı ve net şekilde doğrulandı. Bu, "varsaymadan gözlemle" ilkesinin bu seferki hâli — gözlemlenen davranış, ilk tahminimden farklı çıktı, ve bunu olduğu gibi kaydediyorum.

## Demo basitleştirmesi vs. üretim gereksinimi

Bugünkü eşikler (2 başarısızlık, 10 saniyelik pencere, 15 saniyelik açık kalma) sabit kodlandı — üretimde bunlar konfigürasyondan okunur, ve genelde bağımlılık başına farklı ayarlanır (örn. kritik bir ödeme servisi için daha toleranslı, önemsiz bir servis için daha agresif).
