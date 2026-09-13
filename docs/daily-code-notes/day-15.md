# Day 15 — Kod Notları

Faz 2, Hafta 3, Gün 5 (Cuma — refactor, tam doğrulama, dokümantasyon, Week 3 kapanışı). Konu: sayfalama/filtreleme/sıralama, tam regresyon.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `Models/PagedResult.cs`

```csharp
namespace StockPilot.Api.Models;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
```

**Neden:** Sadece ürün listesini dönmek yeterli değil — istemcinin "toplam kaç ürün var, kaçıncı sayfadayım" bilmesi gerekiyor (örneğin "sonraki sayfa" butonunu gösterip göstermeyeceğine karar vermek için). `TotalCount` olmadan, istemci sadece "bu sayfada 2 ürün var" bilirdi, ama "toplamda 50 ürün mü var, yoksa sadece 2 mi var" ayrımını yapamazdı.

**Nasıl çalışıyor:** `PagedResult<T>` — **generic** bir record (`<T>`), yani `PagedResult<ProductDto>`, `PagedResult<Skill>` gibi herhangi bir tip için yeniden kullanılabilir bir "sayfalama zarfı." Bugün sadece `ProductDto` ile kullanıyoruz ama tasarım gereği başka hiçbir listeye özel değil.

---

## 2. `ProductsController.GetAll()` — yeniden yazım

```csharp
[HttpGet]
public ActionResult<PagedResult<ProductDto>> GetAll(
    [FromQuery] string? search = null,
    [FromQuery] string? sortBy = null,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 10)
{
    var products = _productStore.GetAll().AsEnumerable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        products = products.Where(p =>
            p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            p.Sku.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    products = sortBy?.ToLowerInvariant() switch
    {
        "price" => products.OrderBy(p => p.Price),
        "name" => products.OrderBy(p => p.Name),
        _ => products.OrderBy(p => p.Id)
    };

    var totalCount = products.Count();
    var pagedProducts = products.Skip((page - 1) * pageSize).Take(pageSize).ToList();
    var dtos = pagedProducts.Select(ToDto).ToList();

    var result = new PagedResult<ProductDto>(dtos, page, pageSize, totalCount);
    return Ok(result);
}
```

**Neden/Nasıl:**
* `[FromQuery] string? search = null` — `{id}` gibi URL'in **parçası olan** route parametrelerinden farklı olarak, bu parametreler URL'in `?` sonrasından (`?search=mouse`) geliyor. `= null`/`= 1`/`= 10` varsayılan değerleri sayesinde **hepsi opsiyonel** — `?search=` hiç yazılmazsa `search` otomatik `null` olur.
* `.Where(p => p.Name.Contains(search, ...) || p.Sku.Contains(search, ...))` — LINQ'un filtreleme operatörü (RoadmapOS'ta hiç kullanmamıştık, bugün ilk kez). Sadece koşulu (`predicate`) sağlayan elemanları bırakıyor.
* `sortBy?.ToLowerInvariant() switch { ... }` — bir **switch expression** (Day 2'deki ternary'nin genişletilmiş hâli): `sortBy`'ın değerine göre hangi `.OrderBy(...)`'ın uygulanacağına karar veriyor. `_` (alt çizgi), "yukarıdaki hiçbirine uymuyorsa" demek (varsayılan: `Id`'ye göre sırala).
* `.Skip((page - 1) * pageSize).Take(pageSize)` — sayfalamanın kalbi. `page=2, pageSize=10` ise: `Skip(10)` (ilk 10'u atla), `Take(10)` (sonraki 10'u al) = 2. sayfa.
* `products.Count()` — **`Skip`/`Take`'den önce** çağrılıyor (`totalCount` satırı, `pagedProducts` satırından önce) — çünkü `TotalCount`, **filtrelenmiş ama sayfalanmamış** toplam sayıyı temsil etmeli (örn. arama "mouse" ise, sayfalamadan önceki "mouse" eşleşen toplam sayı, sadece o sayfadaki değil).

---

## Doğrulanan davranış

```
dotnet build (StockPilot) → 0 Hata, 0 Uyarı
dotnet test (StockPilot)  → 8/8 (3 test PagedResult'a uyacak sekilde guncellendi)

GET /api/products                        → 3 urun, totalCount=3
GET /api/products?search=mouse           → sadece "Wireless Mouse", totalCount=1
GET /api/products?sortBy=price           → 19.99, 34.50, 79.99 sirasiyla (artan)
GET /api/products?page=2&pageSize=1      → 2. urun (id=2), page=2, pageSize=1, totalCount=3

Tam regresyon (Week 3 kapanisi):
  dotnet build StockPilot.slnx → 0 Hata, 0 Uyari
  dotnet test StockPilot.slnx  → 8/8
  dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyari
  dotnet test RoadmapOS.slnx   → 8/8
  (iki solution da, birbirinden bagimsiz, tamamen saglikli)
```

---

## Week 3 kapanış özeti

* **Day 11:** Proje kurulumu, controller-based Web API, ilk read-only slice (`GetAll`/`GetById`), güvenlik açığı düzeltmesi.
* **Day 12:** `POST`/`Create`, 201+Location, domain/DTO ayrımı, manuel mapping.
* **Day 13:** Validation (`[Required]`/`[StringLength]`/`[Range]`), `[ApiController]`'ın otomatik 400'ü, global error handling (`AddProblemDetails`/`UseExceptionHandler`).
* **Day 14:** Test izolasyonu sorunu canlı keşfedildi, `IProductStore`/DI ile çözüldü, ilk StockPilot testleri.
* **Day 15:** Sayfalama/filtreleme/sıralama, tam regresyon, Week 3 kapanışı.

Week 4'te StockPilot'a EF Core + SQL Server gelecek — `InMemoryProductStore` yerine `EfProductStore`, `ProductsController` yine değişmeden (RoadmapOS Day 3→4 geçişinin StockPilot versiyonu).
