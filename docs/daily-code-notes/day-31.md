# Day 31 — Kod Notları

Faz 2, Hafta 6, Gün 31 — Hafta 6'nın (ve Phase 2'nin) son günü. Konu: API dokümantasyonu ve portfolio cilası.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `src/StockPilot.Api/StockPilot.Api.csproj` — yeni paket

```
Scalar.AspNetCore
```

**Neden:** Day 11'den beri `AddOpenApi()`/`MapOpenApi()` sadece **ham bir JSON şeması** üretiyordu (`/openapi/v1.json`) — okunması zor, denemesi imkansız bir metin. Scalar, bu şemayı okuyup **tıklanabilir, deneyimlenebilir** bir arayüze çeviren bir kütüphane.

---

## 2. `src/StockPilot.Api/Program.cs` — tek satırlık ekleme

```csharp
using Scalar.AspNetCore;
```
```csharp
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
```

**Neden bu kadar az kod yeterli:** `MapScalarApiReference()`, **zaten var olan** `/openapi/v1.json` şemasını okuyor — hiçbir endpoint'in kendi kodunu değiştirmemize gerek yok. Bu, Day 11'de `[ApiController]`'ın otomatik olarak bir OpenAPI şeması üretmesinin (hiçbir ekstra kod yazmadan) bugünkü doğal devamı.

**Canlı kanıt:** Uygulama çalıştırılıp `/openapi/v1.json`'un tüm 5 route'u (8 HTTP metodu: `Auth/login`, `Auth/refresh`, `Products` GET/POST, `Products/bulk`, `Products/{id}` GET/PUT/DELETE) doğru şekilde listelediği, `/scalar/v1`'in gerçek bir HTML sayfası (200, `text/html`) döndürdüğü doğrulandı.

---

## 3. `README.md` — StockPilot bölümü eklenmesi

RoadmapOS'un Day 10'da aldığı aynı muameleyi StockPilot da bugün alıyor: Prerequisites, çalıştırma talimatları, demo hesapları (tablo halinde), test çalıştırma talimatı, ve dürüst bir "Known simplifications" listesi.

**Neden dürüst bir liste önemli:** Bir portfolyo projesinin gerçek değeri, "her şey mükemmel" demekte değil — neyin bilinçli bir basitleştirme olduğunu, neyin gerçek bir üretim gereksinimi olduğunu **ayırt edebilmekte**. Bu liste, StockPilot'un tüm günler boyunca (Day 23'ten beri) tekrar tekrar not düşülen gerçek sınırlarını (iki sabit demo kullanıcı, dev-only JWT anahtarı, `InMemoryRefreshTokenStore`'un kalıcı olmaması, henüz bir `Order` domain'inin olmaması) tek bir yerde topluyor.

**"Current status" güncellemesi:** `README.md`'nin en üstündeki durum özeti 20 gündür güncellenmemişti (hâlâ "Phase 1, Day 11, ~9%" yazıyordu) — bugün gerçek duruma (Phase 2 kapanıyor, Day 31, ~27%) güncellendi.

---

## Doğrulanan davranış

```
dotnet build/test (StockPilot) → 0 Hata, 0 Uyari, 27/27 basarili (degisiklik yok)
dotnet test (RoadmapOS)        → 8/8 (etkilenmedi)

/openapi/v1.json → 200, tum 5 route/8 metot dogru listelendi
/scalar/v1       → 200, gercek bir HTML sayfasi (Scalar arayuzu)
```

---

## Phase 2 kapanış özeti (resmi bir "gate" olmadan, dürüst bir envanter)

`ROADMAP.md`'de Phase 2'ye özel bir "completion gate" tanımlı değil (sadece Phase 1 ve Phase 3'te var) — bu yüzden bugün, Week 3-6 boyunca gerçekten neyin tamamlandığının dürüst bir listesini çıkarıyoruz:

* **Week 3:** Controller-based REST API, HTTP status kodları, DTO'lar, manuel mapping, validation, pagination/filtering/sorting.
* **Week 4:** EF Core + SQL Server, unique constraint + iki katmanlı 409 savunması, optimistic concurrency (`RowVersion`), transactions (`AddRangeAsync`'in explicit transaction'ı), query analysis (`AsNoTracking`).
* **Week 5:** JWT authentication (access + refresh token, rotation), role-based authorization, policy-based authorization, 401/403 ayrımı.
* **Week 6:** xUnit, mocking (Moq), gerçek entegrasyon testleri (`WebApplicationFactory`), test-database isolation (Testcontainers), CI (GitHub Actions), API dokümantasyonu (Scalar), portfolio cilası (bugün).

**Dürüstçe eksik/kapsam dışı bırakılan (StockPilot'un "Order API" yarısı):** Warehouses, inventory movements, orders, stock reservations, order cancellation — bunlar `ROADMAP.md`'nin Phase 2 domain tanımında var ama hiç ele alınmadı. Bu, Phase 2'nin StockPilot'un **authentication/testing/CI mekaniklerini** öğretme amacına hizmet etmesinden kaynaklanıyor — "Order API" kısmı, gerçek bir ihtiyaç doğduğunda (belki daha sonraki bir fazda) ele alınabilir, ama bugün bunu gizlemek yerine açıkça söylüyoruz.
