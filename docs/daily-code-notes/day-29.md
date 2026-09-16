# Day 29 — Kod Notları

Faz 2, Hafta 6, Gün 29. Konu: Mocking (Moq).

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `tests/StockPilot.Api.Tests/StockPilot.Api.Tests.csproj` — yeni paket

```
Moq
```

**Neden:** Moq, "bu arayüzün bu metodu çağrıldığında tam olarak şunu yap" diyebilmemizi sağlayan bir kütüphane. `IProductStore`'u mock'luyoruz (kendi soyutlamamız), **EF Core'un kendisini değil** — `CLAUDE.md`'nin "EF Core'u sırf test geçsin diye mock'lama" kuralına tam uyumlu.

---

## 2. `tests/StockPilot.Api.Tests/ProductsControllerMockingTests.cs` — yeni dosya

```csharp
[Fact]
public async Task Create_StoreThrowsDbUpdateException_ReturnsConflict()
{
    var mockStore = new Mock<IProductStore>();

    mockStore
        .Setup(s => s.SkuExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);
    mockStore
        .Setup(s => s.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new DbUpdateException("Simulated unique index violation from a concurrent request."));

    var controller = new ProductsController(mockStore.Object);
    var request = new CreateProductRequest("SKU-RACE", "Mocked Race Condition Product", 10.00m);

    var result = await controller.Create(request);

    Assert.IsType<ConflictObjectResult>(result.Result);
}
```

Adım adım:
- `new Mock<IProductStore>()` — `IProductStore` arayüzünü uygulayan, tamamen kontrol edebildiğimiz sahte bir nesne oluşturuyor.
- `.Setup(s => s.SkuExistsAsync(...)).ReturnsAsync(false)` — "`SkuExistsAsync` hangi parametrelerle çağrılırsa çağrılsın (`It.IsAny<T>()`), `false` döndür" demek. Bu, `Create`'in **Katman 1**'ini (proaktif kontrol) geçmesini sağlıyor — geçmeseydi, kod hiç `AddAsync`'e ulaşmadan erken `Conflict` dönerdi, test etmek istediğimiz Katman 2'ye hiç varmazdık.
- `.Setup(s => s.AddAsync(...)).ThrowsAsync(new DbUpdateException(...))` — "`AddAsync` çağrılırsa, gerçekte hiçbir ekleme yapma, doğrudan bu hatayı fırlat" demek. `InMemoryProductStore`'un **asla** yapamadığı şey tam olarak bu.
- `new ProductsController(mockStore.Object)` — controller'ı, gerçek implementasyon yerine bu mock'la kuruyoruz. `mockStore.Object`, mock'un **gerçek `IProductStore` olarak davranan** halini veriyor.
- Geri kalanı (`await controller.Create(request)`, `Assert.IsType<ConflictObjectResult>(...)`) tamamen tanıdık — controller'ın **gerçek** kodu (mock'lanmamış) çalışıyor, sadece bağımlı olduğu şey sahte.

---

## Canlı olarak keşfedilen bir Moq ayrıntısı: `SkuExistsAsync` ayarı gerçekten gerekli mi?

`SkuExistsAsync`'in `.Setup(...)`'ı geçici olarak kaldırılıp test tekrar çalıştırıldı — **test yine geçti**. Sebebi: Moq'un varsayılan davranışı, hiç ayarlanmamış (unconfigured) bir metot `Task<bool>` gibi bir tip döndürüyorsa, otomatik olarak "tamamlanmış, `false` değerli bir Task" veriyor (bu, `await`'lenen bir mock çağrısının `null` dönüp `NullReferenceException` patlatmasını önlemek için Moq'a eklenmiş bir kolaylık). Bizim ihtiyacımız zaten `false` olduğu için, bu tesadüfen işimize yarıyordu — `.Setup(...)` **tam olarak zorunlu değildi**. Yine de kalıcı olarak yazılı bırakıldı: bir kütüphanenin sessiz varsayılanına güvenmek yerine, testin gerçek varsayımını **açıkça** ifade etmek daha sağlam ve okunaklı bir pratik.

---

## Canlı kanıt (Red → Green)

`ProductsController.Create`'teki `try/catch (DbUpdateException)` bloğu **geçici olarak** kaldırıldı:

```csharp
// try/catch olmadan, doğrudan:
await _productStore.AddAsync(product, cancellationToken);
```

Test tekrar çalıştırıldı:

```
[FAIL] Create_StoreThrowsDbUpdateException_ReturnsConflict
Hata: Microsoft.EntityFrameworkCore.DbUpdateException : Simulated unique index violation from a concurrent request.
   at ProductsController.Create(...) line 80
   at Create_StoreThrowsDbUpdateException_ReturnsConflict() line 37
```

**Sonuç:** Mock'un fırlattığı hata, `catch` olmadığı için testin **dışına kadar** yükseldi ve testi gerçekten kırdı. Bu, testin "her zaman yeşil kalan, hiçbir şey kanıtlamayan" bir test olmadığını kanıtlıyor (Day 7'nin Red→Green ilkesi). `catch` bloğu geri kondu, test tekrar yeşile döndü.

```
dotnet build/test (StockPilot) → 0 Hata, 0 Uyari, 27/27 basarili (1 yeni mock testi)
dotnet test (RoadmapOS)        → 8/8 (etkilenmedi)
```

---

## Bugün not düşülen, ama yapılmayan

Aynı desen (`Mock<IProductStore>` ile gerçek bir DB hatasını simüle etmek), Day 21'in `Update`'indeki `DbUpdateConcurrencyException` yakalama bloğuna ve Day 22'nin `AddRangeAsync`'indeki transaction rollback davranışına da uygulanabilir — ikisi de şu an sadece canlı kanıtlanabiliyor, otomatik testle değil. Bugün sadece `Create`'e odaklanıldı; diğerleri aynı desenle ileriki bir günde ele alınabilir.
