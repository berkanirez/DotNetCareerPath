# Day 19 — Kod Notları

Faz 2, Hafta 4, Gün 4 (Perşembe — tests, failures, production considerations). Konu: Day 18'in bıraktığı 500→409 boşluğunu kapatma, iki katmanlı savunma.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `Models/IProductStore.cs` — yeni bir yetenek

```csharp
Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken = default);
```

**Neden:** `Create`'in duplicate SKU'yu **önceden, nazikçe** sorabilmesi için — veritabanının kendi kısıtlamasına (Day 18) çarpıp çirkin bir exception almadan önce.

## 2. `Data/EfProductStore.cs`

```csharp
public async Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken = default)
{
    return await _context.Products.AnyAsync(p => p.Sku == sku, cancellationToken);
}
```

**Nasıl çalışıyor:** `AnyAsync(...)`, LINQ'un "koşulu sağlayan **en az bir** eleman var mı" sorusu — `Where(...).Count() > 0` yazmaktan çok daha verimli, çünkü SQL Server'a "bul, say" değil "bul, ilk bulduğunda dur" dedirtiyor (`EXISTS` benzeri bir SQL üretir).

## 3. `Models/InMemoryProductStore.cs`

```csharp
public Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken = default)
{
    foreach (var product in _products)
    {
        if (product.Sku == sku)
        {
            return Task.FromResult(true);
        }
    }
    return Task.FromResult(false);
}
```

Aynı mantığın bellek-içi karşılığı — `GetByIdAsync`'teki `foreach` deseninin aynısı.

---

## 4. `Controllers/ProductsController.cs` — iki katmanlı savunma

```csharp
[HttpPost]
public async Task<ActionResult<ProductDto>> Create(CreateProductRequest request, CancellationToken cancellationToken = default)
{
    // Katman 1 (proaktif): yaygın durum — veritabanının yazma yoluna hiç
    // dokunmadan, bariz bir tekrarı önceden reddet.
    if (await _productStore.SkuExistsAsync(request.Sku, cancellationToken))
    {
        return Conflict($"A product with SKU '{request.Sku}' already exists.");
    }

    var product = new Product(request.Sku, request.Name, request.Price);

    try
    {
        await _productStore.AddAsync(product, cancellationToken);
    }
    catch (DbUpdateException)
    {
        // Katman 2 (reaktif güvenlik ağı): nadir bir yarış durumu — iki eşzamanlı
        // istek, ikisi de birbirinden habersiz üstteki kontrolü geçmiş olabilir.
        // Veritabanının kendi unique index'i son karar mercii.
        return Conflict($"A product with SKU '{request.Sku}' already exists.");
    }

    var dto = ToDto(product);
    return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
}
```

**Neden iki katman birden:**
* **Sadece Katman 1 (proaktif kontrol) olsaydı:** Day 18'de konuştuğumuz yarış durumu (iki eşzamanlı istek, ikisi de kontrolü aynı anda geçer) hâlâ mümkün olurdu — ve o durumda `AddAsync` yine `DbUpdateException` fırlatırdı, **hiç yakalanmadan**, tekrar 500'e dönerdik.
* **Sadece Katman 2 (try/catch) olsaydı:** Her normal duplicate denemesi (yaygın, beklenen senaryo) **her seferinde** veritabanına gidip bir exception fırlatıp yakalamak zorunda kalırdı — bu hem gereksiz yavaş hem de exception'ları "normal akış kontrolü" için kullanmak kötü bir pratik.
* **İkisi birlikte:** yaygın durum hızlı ve temiz (Katman 1), nadir yarış durumu da güvenli (Katman 2) — hiçbir durumda 500 görünmüyor.

**`Conflict(...)` nedir:** RoadmapOS'ta hiç görmediğimiz yeni bir `ActionResult` yardımcı metodu — `NotFound()`/`Ok()` gibi, ama HTTP **409**'u ve verilen mesajı response body'sine yazıyor.

---

## 5. Test — sadece 1. katman test edilebiliyor (dürüst sınır)

```csharp
[Fact]
public async Task Create_DuplicateSku_ReturnsConflict()
{
    // Uses InMemoryProductStore, so this only exercises Layer 1 (the
    // proactive SkuExistsAsync check) — it can never reach Layer 2's
    // DbUpdateException catch, since InMemoryProductStore never throws one.
    var controller = CreateController();

    var duplicateRequest = new CreateProductRequest("SKU-001", "Another Mouse", 25.00m);
    var result = await controller.Create(duplicateRequest);

    Assert.IsType<ConflictObjectResult>(result.Result);
}
```

**Dürüst bir sınır:** `InMemoryProductStore`, gerçek bir veritabanı olmadığı için **asla** `DbUpdateException` fırlatmıyor — bu test, sadece **Katman 1**'i (proaktif kontrol) kanıtlıyor. **Katman 2**'yi (gerçek yarış durumunda `DbUpdateException` yakalama) otomatik bir testle kanıtlamak, gerçek bir eşzamanlı yarışı güvenilir şekilde tetiklemeyi gerektirir — bu bugünün kapsamı dışında. Katman 2'ye, EF Core'un davranışını **anladığımız için güveniyoruz**, ama bugün **otomatik testle doğrulamadık** — bu farkı gizlemek yerine açıkça belirtiyoruz.

---

## Doğrulanan davranış

```
dotnet build/test (StockPilot) → 0 Hata, 0 Uyari, 10/10 (yeni test dahil)
dotnet test (RoadmapOS)        → 8/8 (etkilenmedi)

POST duplicate SKU (SKU-001) → HTTP 409, "A product with SKU 'SKU-001' already exists."
POST yeni SKU (SKU-010)      → HTTP 201, Location dogru

Bosluk kapandi: artik 500 yerine dogru anlamli 409 donuyor.
```
