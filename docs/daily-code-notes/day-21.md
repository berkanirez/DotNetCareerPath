# Day 21 — Kod Notları

Faz 2, Hafta 4 (uzatılmış), Gün 21. Konu: Update (PUT) endpoint'i ve optimistic concurrency (`RowVersion`).

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `Models/Product.cs` — concurrency token eklendi

```csharp
public byte[] RowVersion { get; set; } = null!;
```

**Neden:** Bu, uygulamanın kendi verisi değil — SQL Server'ın her `INSERT`/`UPDATE`'te **kendiliğinden** değiştirdiği bir "versiyon damgası". Amacı: "bu satırı en son ne zaman okudum, o zamandan beri biri değiştirdi mi?" sorusunu cevaplayabilmek.

**Nasıl çalışıyor:** `byte[] = null!` — `RowVersion`'ı EF Core/SQL Server dolduracağı için burada gerçek bir değer atamıyoruz, ama nullable reference types kuralı yüzünden derleyiciye "bu asla gerçekte null olmayacak, ben garanti ediyorum" demek için `null!` (null-forgiving operatör) kullanıyoruz — Day 2'de gördüğümüz `?`'nin tam tersi bir işaret.

---

## 2. `Data/StockPilotDbContext.cs` — Fluent API ile işaretleme

```csharp
entity.Property(p => p.RowVersion).IsRowVersion();
```

**Neden:** EF Core'a "bu sütunu normal bir veri sütunu gibi değil, concurrency kontrolü için kullan" demek. Bu tek satır iki şeyi tetikliyor: (1) migration'da SQL Server'ın gerçek `rowversion` sütun tipi üretiliyor, (2) EF Core, bu entity'yi her `UPDATE`/`DELETE` ürettiğinde otomatik olarak `WHERE ... AND RowVersion = @eskiDeğer` ekliyor.

**Üretilen migration (`AddProductRowVersion`):**
```csharp
migrationBuilder.AddColumn<byte[]>(
    name: "RowVersion", table: "Products",
    type: "rowversion", rowVersion: true,
    nullable: false, defaultValue: new byte[0]);
```

---

## 3. `Models/ProductDto.cs` ve `Controllers/ProductsController.cs` — `RowVersion`'ın dışarı taşınması

```csharp
public record ProductDto(int Id, string Sku, string Name, decimal Price, byte[] RowVersion);
```

**Neden:** İstemcinin (client) bir ürünü güncelleyebilmesi için, önce onu **okurken** hangi `RowVersion`'da olduğunu bilmesi lazım — bu yüzden her `GetAll`/`GetById`/`Create` yanıtına artık `rowVersion` alanı da ekleniyor (JSON'da otomatik olarak base64 string'e çevriliyor, örn. `"AAAAAAAAB9I="`).

---

## 4. `Models/UpdateProductRequest.cs` — yeni bir giriş DTO'su

```csharp
public record UpdateProductRequest(
    [Required, StringLength(200)] string Name,
    [Range(0.01, double.MaxValue)] decimal Price,
    [Required] byte[] RowVersion);
```

**Neden `Sku` yok:** Bilinçli bir kapsam daraltması — `Sku`'yu güncellenebilir yapmak, Day 18-19'da çözdüğümüz "aynı SKU'dan iki tane olabilir mi" sorununu update tarafında yeniden açardı. Bugünün odağı sadece concurrency; `Sku` güncelleme ihtiyacı gerçek olarak ortaya çıkarsa ayrı bir gün olarak ele alınacak.

---

## 5. `Models/IProductStore.cs` — yeni bir yetenek

```csharp
Task<Product?> UpdateAsync(int id, string name, decimal price, byte[] rowVersion, CancellationToken cancellationToken = default);
```

`null` dönmesi "böyle bir ürün yok" anlamına geliyor (Day 19'daki `GetByIdAsync`'in aynı deseni); concurrency çakışması ise bir dönüş değeri değil, bir **exception** olarak modelleniyor (aşağıya bakınız).

---

## 6. `Data/EfProductStore.cs` — asıl concurrency mekanizması

```csharp
public async Task<Product?> UpdateAsync(int id, string name, decimal price, byte[] rowVersion, CancellationToken cancellationToken = default)
{
    var product = await _context.Products.FindAsync([id], cancellationToken);
    if (product is null)
    {
        return null;
    }

    _context.Entry(product).Property(p => p.RowVersion).OriginalValue = rowVersion;

    product.Name = name;
    product.Price = price;

    await _context.SaveChangesAsync(cancellationToken);
    return product;
}
```

**En kritik satır — neden burada `OriginalValue` set ediliyor:**
`FindAsync`, ürünü **şu an** veritabanında ne haldeyse öyle getirir — yani onun `RowVersion`'ı zaten "güncel" olur, kendi kendiyle asla çakışmaz. Eğer hiçbir şey yapmasaydık, `SaveChangesAsync` her zaman başarılı olurdu — çünkü EF Core, WHERE koşulunu **entity'nin şu an taşıdığı** `RowVersion` değerine göre kurar, bizim istemciden gelen değere göre değil.

`_context.Entry(product).Property(p => p.RowVersion).OriginalValue = rowVersion;` satırı, EF Core'a "hayır, WHERE koşuruna bunu koy: istemcinin **elinde tuttuğu**, GET sırasında okuduğu değeri" diyor. Böylece üretilen SQL şuna benzer hale geliyor:

```sql
UPDATE Products SET Name=@n, Price=@p
WHERE Id=@id AND RowVersion=@istemcininElindekiEskiDeğer;
```

Eğer araya biri girip satırı değiştirdiyse, veritabanındaki gerçek `RowVersion` artık farklıdır → bu `WHERE` koşulunu **hiçbir satır** karşılamaz → 0 satır etkilenir → EF Core bunu `DbUpdateConcurrencyException` olarak fırlatır.

---

## 7. `Models/InMemoryProductStore.cs` — dürüst bir sınır

```csharp
public async Task<Product?> UpdateAsync(int id, string name, decimal price, byte[] rowVersion, CancellationToken cancellationToken = default)
{
    var product = await GetByIdAsync(id, cancellationToken);
    if (product is null) return null;
    product.Name = name;
    product.Price = price;
    return product;
}
```

`rowVersion` parametresi burada **kasıtlı olarak hiç kullanılmıyor** — gerçek bir veritabanı olmadığı için kontrol edilecek gerçek bir "şu an ne durumda" bilgisi yok. Bu, Day 19'un "sadece Katman 1 test edilebilir" sınırının bu günkü karşılığı: `InMemoryProductStore` ile sadece "ürün bulundu/bulunamadı" test edilebiliyor, gerçek concurrency çakışması sadece `EfProductStore` + gerçek SQL Server ile mümkün.

---

## 8. `Controllers/ProductsController.cs` — `Update` action'ı

```csharp
[HttpPut("{id}")]
public async Task<ActionResult<ProductDto>> Update(int id, UpdateProductRequest request, CancellationToken cancellationToken = default)
{
    try
    {
        var product = await _productStore.UpdateAsync(id, request.Name, request.Price, request.RowVersion, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        return Ok(ToDto(product));
    }
    catch (DbUpdateConcurrencyException)
    {
        return Conflict("The product was modified by another request since it was last read. Reload it and try again.");
    }
}
```

**Neden `Conflict` (409), `500` değil:** Day 19'daki aynı mantık — bu, sunucunun beklenmedik bir hatası değil, istemcinin **bayat veriyle** geldiği, tamamen beklenen bir durum. `DbUpdateConcurrencyException`, Day 19'un `DbUpdateException`'ından farklı bir tip — biri "aynı SKU zaten var" (uniqueness ihlali), diğeri "senin okuduğun veriden beri bu satır değişti" (concurrency ihlali); ikisi de veritabanı kaynaklı ama farklı iş kurallarını temsil ediyor.

---

## 9. Testler

```csharp
[Fact]
public async Task Update_ExistingId_ReturnsUpdatedProduct() { ... }

[Fact]
public async Task Update_MissingId_ReturnsNotFound() { ... }
```

İkisi de `InMemoryProductStore` ile — yani sadece "bulundu mu / bulunamadı mı" ve "normal, çakışmasız güncelleme" yolunu kanıtlıyorlar. Gerçek concurrency çakışması (aşağıdaki canlı kanıt) otomatik testle değil, gerçek SQL Server'a karşı canlı bir deneyle kanıtlandı — bu bilinçli bir sınır, gizlenmiyor.

**Bağımsız görev olarak eklenen satır** (bu seferlik Berkan'ın isteğiyle Claude tarafından yazıldı):
```csharp
Assert.Equal("SKU-001", dto.Sku); // UpdateProductRequest carries no Sku, so it must stay untouched.
```
`UpdateProductRequest`'te `Sku` alanı hiç yok — bu satır, `Update`'in gerçekten sadece `Name`/`Price`'a dokunduğunu, `Sku`'yu olduğu gibi bıraktığını testle de kanıtlıyor.

---

## Canlı kanıt (gerçek SQL Server'a karşı)

```
1. GET  /api/products/1        → rowVersion = "AAAAAAAAB9I="
2. sqlcmd ile DOĞRUDAN veritabanında ürün 1 güncellendi (RowVersion otomatik değişti → 0x...07D9)
3. PUT  /api/products/1  (eski rowVersion="AAAAAAAAB9I=") → HTTP 409 ✅ (beklenen çakışma yakalandı)
4. PUT  /api/products/1  (güncel rowVersion) → HTTP 200 ✅ (normal güncelleme çalışıyor)
5. GET  /api/products/999       → HTTP 404 ✅ (regresyon yok)

dotnet test (StockPilot) → 13/13 basarili (2 yeni test)
dotnet test (RoadmapOS)  → 8/8 basarili (etkilenmedi)
```

Bu, Day 19'un "sadece Katman 1 test edilebilir" deneyiminin bir adım ötesi: bugün Katman 2'ye eşdeğer olan gerçek çakışma senaryosunu da **gerçek bir veritabanına karşı elle** kanıtladık — otomatik testle değil ama gerçek, tekrarlanabilir bir kanıt.
