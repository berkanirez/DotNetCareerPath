# Day 22 — Kod Notları

Faz 2, Hafta 4 (uzatılmış), Gün 22 — Hafta 4'ün kapanış günü. Konu: transactions (explicit vs implicit) ve query analysis (`AsNoTracking()`).

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `IProductStore.cs` — yeni bir yetenek

```csharp
Task<IReadOnlyList<Product>> AddRangeAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken = default);
```

**Neden:** Toplu ürün ekleme ("bulk create") için — tek tek `AddAsync` çağırmak yerine, "hepsi ya da hiçbiri" davranışı gerektiren ayrı bir yetenek.

---

## 2. `Data/EfProductStore.cs` — asıl transaction mekanizması

```csharp
public async Task<IReadOnlyList<Product>> AddRangeAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken = default)
{
    await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

    foreach (var product in products)
    {
        await AddAsync(product, cancellationToken);
    }

    await transaction.CommitAsync(cancellationToken);
    return products;
}
```

**Neden bu şekilde yazıldı:** Her `AddAsync` çağrısı kendi `SaveChangesAsync()`'ini yapıyor — yani kendi başına, ayrı bir işlem (implicit transaction). Bunları bir döngüde art arda çağırırsak ve 3.'sü başarısız olursa, 1. ve 2.'si zaten kalıcı olarak kaydedilmiş olur. `BeginTransactionAsync()` ile açtığımız bir transaction, bu ayrı `SaveChangesAsync()` çağrılarını **tek bir bölünmez birim** haline getiriyor.

**Nasıl çalışıyor — en kritik nokta:** `await using var transaction = ...` satırı, transaction nesnesini bir `using` bloğuna alıyor. Kod bloğun sonuna **`CommitAsync()` çağrılmadan** ulaşırsa (örneğin bir exception fırlarsa, `foreach` döngüsü ortasında kesilirse), `await using` bloktan çıkarken transaction'ı **otomatik olarak geri alır (rollback)** — elle bir `catch`/`RollbackAsync()` yazmamıza bile gerek yok. Bu, .NET'in `IAsyncDisposable` mekanizmasının (`Dispose`/`DisposeAsync` metodu blok sonunda otomatik çağrılır) transaction nesnesine özel davranışı: "commit edilmeden dispose edilen bir transaction, kendini geri alır" EF Core'un transaction API'sinin tasarım kuralı.

---

## 3. `Models/InMemoryProductStore.cs` — dürüst bir sınır

```csharp
public Task<IReadOnlyList<Product>> AddRangeAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken = default)
{
    foreach (var product in products)
    {
        product.Id = _nextId++;
        _products.Add(product);
    }

    return Task.FromResult(products);
}
```

Burada gerçek bir "transaction" kavramı yok — sadece bir `List<T>`. Bu yüzden ne duplicate-SKU kontrolü var, ne de gerçek bir rollback senaryosu test edilebiliyor. Day 19/21'deki aynı dürüst sınır: gerçek atomiklik sadece `EfProductStore` + gerçek SQL Server ile mümkün ve sadece canlı olarak kanıtlanabiliyor.

---

## 4. `Controllers/ProductsController.cs` — `bulk` endpoint'i

```csharp
[HttpPost("bulk")]
public async Task<ActionResult<IReadOnlyList<ProductDto>>> BulkCreate(List<CreateProductRequest> requests, CancellationToken cancellationToken = default)
{
    var products = requests.Select(r => new Product(r.Sku, r.Name, r.Price)).ToList();

    try
    {
        await _productStore.AddRangeAsync(products, cancellationToken);
    }
    catch (DbUpdateException)
    {
        return Conflict("One or more products in this batch could not be added (e.g. a duplicate SKU) — the entire batch was rolled back, nothing was saved.");
    }

    var dtos = products.Select(ToDto).ToList();
    return StatusCode(StatusCodes.Status201Created, dtos);
}
```

**Neden `CreatedAtAction` değil:** `Create`'te tek bir kaynak oluştuğu için `CreatedAtAction` ile tek bir `Location` header'ı anlamlıydı. Burada birden fazla kaynak oluşuyor — hangi URL'e "Location: ..." yazacağımız belirsiz. Bu yüzden `StatusCode(201, dtos)` ile doğrudan 201 dönüp, oluşan tüm kaynakların listesini gövdede veriyoruz.

**Neden aynı `DbUpdateException` yakalanıyor:** `AddRangeAsync` içindeki her `AddAsync` çağrısı, tek ürünlük `Create`'in kullandığı **aynı** metot — yani aynı unique index ihlali, aynı exception tipini fırlatıyor. Fark, exception fırladığında transaction'ın **her şeyi** geri almasında.

---

## 5. `Data/EfProductStore.cs` — `AsNoTracking()` (query analysis)

```csharp
public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
{
    return await _context.Products.AsNoTracking().ToListAsync(cancellationToken);
}
```

**Neden:** EF Core, `ToListAsync()` ile okuduğu **her satırı** varsayılan olarak `ChangeTracker`'a kaydeder — "belki biri bunu değiştirir, ben izlemeye devam edeyim" diye. `GetAllAsync` sadece görüntüleme amaçlı çağrılıyor, hiçbir zaman aynı context üzerinden geri yazılmıyor — bu izleme tamamen gereksiz bir hafıza/CPU maliyeti. `AsNoTracking()` bu izlemeyi kapatıyor.

**`GetByIdAsync`'e neden eklenmedi:** `RemoveAsync`, silinecek entity'yi bulmak için `GetByIdAsync`'i çağırıyor — `_context.Products.Remove(product)` çalışabilmesi için EF Core'un o entity'yi **izliyor** olması gerekiyor. `UpdateAsync` de kendi `FindAsync` çağrısını aynı sebeple izlemeli tutuyor (Day 21'in `OriginalValue` numarası, izlenen bir entity üzerinde çalışıyor). Yani burada `AsNoTracking()` eklemek, güncelleme/silme akışlarını **bozardı**.

---

## Canlı kanıt 1: Transaction'sız hâlin gerçek hatası (geçici olarak tetiklendi)

Transaction satırları geçici olarak kaldırılıp sadece `foreach (var p in products) await AddAsync(p, ct);` bırakıldı:

```
POST /api/products/bulk  [SKU-020(geçerli), SKU-001(DUPLICATE), SKU-021(geçerli)]
→ HTTP 409 (hata doğru yakalandı)

AMA: GET /api/products?search=Bulk
→ SKU-020 GERÇEKTEN VERİTABANINA YAZILMIŞ! (id=12)
```

Bu, "kısmi commit" hatasının canlı kanıtı — API 409 dönmesine rağmen, batch'in ilk ürünü kalıcı olarak kaydedilmiş.

## Canlı kanıt 2: Transaction geri konunca (kalıcı düzeltme)

```
Sızan SKU-020 satırı sqlcmd ile temizlendi, transaction kodu geri eklendi, yeniden derlendi.

POST /api/products/bulk  [SKU-020(gecerli), SKU-001(DUPLICATE), SKU-021(gecerli)]
→ HTTP 409

GET /api/products?search=Bulk  → []  (SKU-020 bu sefer YOK)
GET /api/products?search=SKU-021 → []  (o da YOK)

SONUC: Tam rollback calisiyor - batch'ten HICBIR SEY kalici olmadi.

POST /api/products/bulk  [SKU-030(gecerli), SKU-031(gecerli)]  (duplicate yok)
→ HTTP 201, ikisi de eklendi  (normal yol hala calisiyor)
```

## Canlı kanıt 3: `AsNoTracking()` (geçici bir test dosyasıyla, gerçek veritabanına karşı)

Geçici `TempChangeTrackerDemo.cs` (Day 2'nin geçici console bloğu gibi, iş bitince silindi):

```
AsNoTracking() ILE:     context.ChangeTracker.Entries().Count() == 0
AsNoTracking() OLMADAN: context.ChangeTracker.Entries().Count() == satir sayisi

Ikisi de gercek StockPilot veritabanina karsi calistirildi, ikisi de PASSED.
```

---

## Doğrulanan davranış (özet)

```
dotnet build/test (StockPilot) → 0 Hata, 0 Uyari, 14/14 (1 yeni test: BulkCreate)
dotnet test (RoadmapOS)        → 8/8 (etkilenmedi)
Canli kanit: kismi commit hatasi once GOSTERILDI, sonra DUZELTILDI ve tam rollback kanitlandi.
Canli kanit: AsNoTracking()'in ChangeTracker uzerindeki gercek etkisi kanitlandi.
Test verisi (SKU-020/021/030/031) demo sonrasi temizlendi, veritabani orijinal 7 urune donduruldu.
```
