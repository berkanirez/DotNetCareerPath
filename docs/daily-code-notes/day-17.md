# Day 17 — Kod Notları

Faz 2, Hafta 4, Gün 2 (Salı — happy-path implementasyon). Konu: async database operations, `CancellationToken`.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Hem RoadmapOS'ta hem StockPilot'ta **ilk kez** async EF Core kullandığımız gün.

---

## 1. `Models/IProductStore.cs` — async imzalar

```csharp
public interface IProductStore
{
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Product> AddAsync(Product product, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(int id, CancellationToken cancellationToken = default);
}
```

**Neden:** Senkron bir `_context.Products.ToList()` çağrısı, sorgu tamamlanana kadar **o thread'i tamamen bloke ediyor**. `Task<T>` dönüşü + `async`/`await`, Node.js'teki `Promise`/`async`-`await` ile **birebir aynı fikir**: "bu I/O bitene kadar thread'i serbest bırak, başka isteklere hizmet etsin."

**Nasıl çalışıyor:** Her metot artık `Task<T>` dönüyor (senkron hâlde düz `T` dönüyordu). `CancellationToken cancellationToken = default` — varsayılan değeri olduğu için, çağıran taraf token vermek **zorunda değil** (test kodunda hiç vermiyoruz, gerçek uygulamada controller veriyor).

---

## 2. `Data/EfProductStore.cs` — gerçek async EF Core

```csharp
public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
{
    return await _context.Products.ToListAsync(cancellationToken);
}

public async Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
{
    return await _context.Products.FindAsync([id], cancellationToken);
}

public async Task<Product> AddAsync(Product product, CancellationToken cancellationToken = default)
{
    _context.Products.Add(product);
    await _context.SaveChangesAsync(cancellationToken);
    return product;
}
```

**Nasıl çalışıyor:**
* `ToListAsync(cancellationToken)`, `SaveChangesAsync(cancellationToken)` — EF Core'un **her** veritabanı I/O metodunun bir async karşılığı var, hepsi sona `Async` ekleniyor ve bir `CancellationToken` kabul ediyor.
* `FindAsync([id], cancellationToken)` — burada `[id]` bir dizi (array) literal'i. `FindAsync`'in bu overload'ı `object?[] keyValues` (birden fazla anahtarlı — composite key — tablolar için) + `CancellationToken` alıyor; tek bir `int id`'yi bu diziye sarmamız gerekiyor çünkü metot imzası böyle tanımlanmış.
* `await` her çağrının önünde — "bu `Task`'ın sonucunu bekle, ama beklerken thread'i bloklamadan, başka işler yapılabilsin" demek.

---

## 3. `Models/InMemoryProductStore.cs` — "sahte" async

```csharp
public Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
{
    return Task.FromResult<IReadOnlyList<Product>>(_products);
}
```

**Neden/Nasıl:** Burada **gerçek hiçbir I/O yok** — bellek içi bir liste, zaten anında hazır. Ama `IProductStore`'a uyabilmek için (interface, `Task<T>` dönmeyi zorunlu kılıyor) `Task.FromResult(...)` kullanıyoruz — "bu değeri, zaten tamamlanmış bir `Task` içine sar" demenin yolu. Bu, gerçek bir asenkron bekleme **değil**, sadece tip uyumluluğu için. Önemli ayrım: `async`/`await` **her yerde gerçek I/O beklemek zorunda olduğumuz anlamına gelmiyor** — sadece interface tutarlılığı için de kullanılabiliyor.

---

## 4. `Controllers/ProductsController.cs` — action'ların async'e geçişi

```csharp
[HttpGet]
public async Task<ActionResult<PagedResult<ProductDto>>> GetAll(
    [FromQuery] string? search = null,
    [FromQuery] string? sortBy = null,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 10,
    CancellationToken cancellationToken = default)
{
    var allProducts = await _productStore.GetAllAsync(cancellationToken);
    // ... filtre/sirala/sayfala hala bellek icinde, degismedi
}
```

**Dikkat çeken nokta — `CancellationToken` parametresi hiçbir attribute (`[FromQuery]` gibi) taşımıyor.** Bu, ASP.NET Core'un **özel bir davranışı**: bir action metodu parametre listesinde `CancellationToken` tipinde bir parametre görürse, framework bunu **otomatik olarak** `HttpContext.RequestAborted`'dan dolduruyor — bizim elle bir şey yapmamıza gerek yok. `RequestAborted`, istemci bağlantıyı keserse (sayfayı kapatır, isteği iptal eder, timeout olur) "iptal edildi" durumuna geçen bir token.

**Bugün canlı göstermediğimiz şey:** Gerçek bir iptal senaryosunu (örneğin isteği yarıda keserken `OperationCanceledException` fırlatıldığını) bugün **test etmedik** — network seviyesinde bağlantı kesmeyi güvenilir şekilde simüle etmek zor. Bugün sadece token'ın **doğru yerlere doğru şekilde ulaştığını** kurduk; gerçekten uzun süren bir sorgu olduğunda (StockPilot büyüdükçe) bu altyapı zaten hazır olacak.

---

## 5. Testler — `async Task`'a geçiş

```csharp
[Fact]
public async Task GetAll_ReturnsThreeSeededProducts()
{
    var controller = CreateController();
    var result = await controller.GetAll();
    // ...
}
```

**Nasıl çalışıyor:** xUnit, `[Fact]` ile işaretlenmiş bir metodun dönüş tipi `Task`/`async Task` olursa bunu **otomatik olarak destekliyor** — test metodunun kendisi de `async` olabiliyor, içinde `await` kullanılabiliyor. Hiçbir ek kurulum gerekmedi.

---

## Doğrulanan davranış

```
dotnet build (StockPilot.slnx) → 0 Hata, 0 Uyarı
dotnet test  (StockPilot)      → 8/8 (async Task'a cevrilmis testler dahil)

Gercek HTTP regresyon (curl):
  GetAll   → 200
  GetById  → 200
  Create   → 201
  Delete   → 204
  Gecersiz Create → 400
  Sayfalama → dogru totalCount, dogru items

Davranis, senkron versiyonla BIREBIR AYNI kaldi — sadece thread kullanim
sekli degisti, sonuclar degismedi.
```
