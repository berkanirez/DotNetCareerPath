# Day 14 — Kod Notları

Faz 2, Hafta 3, Gün 4 (Perşembe — testler, hatalar, production considerations). Konu: test izolasyonu, `IProductStore`/DI, ilk StockPilot testleri.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer — bugün de gerçek bir sorunu **önce yaşayıp**, sonra çözdük.

---

## 1. Naif deneme — gerçek sorunun canlı kanıtı (geçici, silindi)

```csharp
[Fact]
public void Create_FirstCall_ResultsInFourProducts()
{
    var controller = new ProductsController();
    controller.Create(new CreateProductRequest("SKU-X", "Test X", 10m));
    // ... 4 ürün bekleniyor
}

[Fact]
public void Create_SecondCall_AlsoResultsInFourProducts()
{
    var controller = new ProductsController();
    controller.Create(new CreateProductRequest("SKU-Y", "Test Y", 20m));
    // ... 4 ürün bekleniyor
}
```

**Ne oldu:** İkinci test **başarısız oldu**: `Expected: 4, Actual: 5`. Sebep: `ProductsController`'daki `private static readonly List<Product> Products` — `static` olduğu için **tüm `ProductsController` örnekleri arasında paylaşılıyor**, dolayısıyla **testler arasında da**. Birinci test listeye bir ürün ekledi, bu ekleme kalıcı kaldı; ikinci test kendi eklediği ürünle birlikte artık 5 ürün buldu ama 4 bekliyordu.

**Neden bu önemli:** RoadmapOS'ta hiç karşılaşmadığımız yeni bir problem — orada veriler gerçek bir `DbContext` üzerinden geliyordu (Day 4'ten beri), `static` bir koleksiyon hiç yoktu. Burada, "her istekte veri kaybolmasın" diye bilerek `static` yapmıştık (Day 11-12), ama bunun **test edilebilirliği kırdığını** bugün canlı gördük.

---

## 2. `Models/IProductStore.cs` ve `InMemoryProductStore.cs`

```csharp
public interface IProductStore
{
    IReadOnlyList<Product> GetAll();
    Product? GetById(int id);
    Product Add(Product product);
    bool Remove(int id);
}
```

**Neden:** RoadmapOS Day 3'teki `ISkillCatalog`'un **birebir aynı çözümü** — veriyi controller'ın kendi `static` alanından çıkarıp, DI ile enjekte edilebilen **ayrı bir sınıfa** taşımak. Bu, hem "gerçek uygulamada veri kalıcı kalsın" hem de "her test kendi izole kopyasını alabilsin" ihtiyaçlarının **ikisini birden** çözüyor.

```csharp
public class InMemoryProductStore : IProductStore
{
    private readonly List<Product> _products = new() { ... 3 seed urun ... };
    private int _nextId = 4;
    // GetAll, GetById, Add, Remove — RoadmapOS'un InMemorySkillCatalog'uyla aynı desen
}
```

**Nasıl çalışıyor — kritik fark:** `_products` ve `_nextId` artık **instance field** (RoadmapOS Day 5'teki `InMemorySkillCatalog` ile aynı), `static` değil. Bu şu anlama geliyor: `new InMemoryProductStore()` her çağrıldığında, **kendi başına, kimseyle paylaşılmayan** bir liste oluşuyor. Gerçek uygulamada bunu **tek bir kez** oluşturup DI'a `Singleton` olarak kaydediyoruz (istekler arasında paylaşılsın diye); testte ise **her test kendi tazesini** oluşturuyor.

## 3. `ProductsController.cs` — DI'a geçiş

```csharp
private readonly IProductStore _productStore;

public ProductsController(IProductStore productStore)
{
    _productStore = productStore;
}
```

Artık `static Products`/`_nextId` yok — RoadmapOS Day 3-4'teki `SkillsController`'ın geçirdiği **aynı dönüşüm**. `GetAll`/`GetById`/`Create`/`Delete`, artık doğrudan liste yerine `_productStore` üzerinden çalışıyor.

## 4. `Program.cs`

```csharp
builder.Services.AddSingleton<IProductStore, InMemoryProductStore>();
```

`Singleton` seçimi: veri, tüm istekler arasında paylaşılmalı (bugüne kadarki "kalıcılık hissi" korunsun) — RoadmapOS Day 3'teki `InMemorySkillCatalog` kaydıyla birebir aynı gerekçe.

---

## 5. `tests/StockPilot.Api.Tests/ProductsControllerTests.cs` — gerçek, izole testler

```csharp
private static ProductsController CreateController() => new(new InMemoryProductStore());
```

**Neden:** Her test bu yardımcı metodu çağırarak **kendi taze `InMemoryProductStore`'unu** alıyor — artık hiçbir test, bir başkasının bıraktığı veriyi görmüyor.

Yazılan 7 test:
* `GetAll_ReturnsThreeSeededProducts` — başlangıç durumu.
* `GetById_ExistingId_ReturnsProduct` / `GetById_MissingId_ReturnsNotFound`.
* `Create_ValidRequest_ReturnsCreatedAtActionWithLocationAndAddsProduct` — `CreatedAtActionResult` tipini ve `ActionName`'i kontrol ediyor.
* `Create_OnASeparateTest_AlsoAssignsIdFour` — az önceki naif denemenin **başarısız olduğu senaryonun**, izolasyon sayesinde artık **doğru çalıştığının** kanıtı.
* `Delete_ExistingId_RemovesProductAndReturnsNoContent` / `Delete_MissingId_ReturnsNotFound`.

**Nasıl çalışıyor — yeni assertion türleri:**
* `Assert.IsType<CreatedAtActionResult>(result.Result)` — action'ın **tam olarak beklenen tipte** bir sonuç döndürdüğünü kontrol ediyor (RoadmapOS Day 7'de sadece `Assert.Equal` görmüştük; burada "doğru **tür**'de sonuç mu" diye de soruyoruz).
* `created.ActionName` — `CreatedAtAction`'ın gerçekten `GetById`'i işaret ettiğini doğruluyor (Day 12'de `Location` header'ını `curl` ile görmüştük, burada aynı bilgiyi **kod içinden** doğruluyoruz).

**Önemli sınırlama (bilerek belirtildi):** Bu unit testler, `controller.Create(request)`'i **doğrudan** çağırıyor — gerçek bir HTTP isteği/MVC pipeline'ı üzerinden geçmiyor. Bu yüzden `[ApiController]`'ın otomatik validation'ı (Day 13) **bu testlerde hiç devreye girmiyor** — `request` içeriği ne olursa olsun, `ModelState` hiç kontrol edilmiyor. Validation'ın gerçek testi, ileride bir **integration test** (`WebApplicationFactory` ile gerçek HTTP isteği simüle eden) günü gerektirecek — bugünün kapsamı dışında, bilerek ertelendi.

---

## Doğrulanan davranış

```
dotnet test (naif deneme) → 1 basarili, 1 basarisiz (Expected: 4, Actual: 5) — sorun kanitlandi
IProductStore/InMemoryProductStore + DI refactoru sonrasi:
dotnet test → 7/7 basarili

Gercek HTTP regresyon (curl):
  GetAll  → 200
  GetById → 200
  Create  → 201, Location: /api/Products/4
  Delete  → 204
  Gecersiz Create → 400
  (hepsi refactor oncesiyle ayni davraniyor)
```
