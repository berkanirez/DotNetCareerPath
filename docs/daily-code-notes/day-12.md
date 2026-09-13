# Day 12 — Kod Notları

Faz 2, Hafta 3, Gün 2 (Salı — happy-path implementasyon). Konu: `POST` ile kayıt oluşturma, 201/Location, domain ile DTO ayrımı, manuel mapping.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `Models/Product.cs` — domain modeli

```csharp
namespace StockPilot.Api.Models;

public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }

    public Product(string sku, string name, decimal price)
    {
        Sku = sku;
        Name = name;
        Price = price;
    }
}
```

**Neden:** Day 11'de `ProductDto`, hem "bellekte tutulan gerçek veri" hem de "API'nin dışarı gösterdiği şekil" olarak aynı sınıftı. Bugün, `Id`'yi **sunucunun atadığı**, kullanıcının **asla göndermemesi gereken** bir alan olarak ayırmak gerçek bir ihtiyaç oldu — bu da RoadmapOS Day 2'deki `Skill` class'ıyla birebir aynı tasarım deseni: constructor sadece zorunlu alanları alıyor, `Id` dışarıda bırakılıyor.

## 2. `Models/CreateProductRequest.cs` — input DTO

```csharp
namespace StockPilot.Api.Models;

public record CreateProductRequest(string Sku, string Name, decimal Price);
```

**Neden:** `POST` isteğinin body'sinde **sadece** kullanıcının vermesi gereken üç alan var — `Id` yok. Bu, RoadmapOS Day 5'teki `SkillFormModel`'in API karşılığı: domain modelini (`Product`) doğrudan body'ye bağlamak yerine, ayrı bir "giriş sözleşmesi" tanımlıyoruz. Şu an `ProductDto` (çıkış) ile neredeyse aynı görünüyor, ama kavramsal olarak **farklı roller** üstleniyorlar — biri "sunucuya ne gönderebilirsin", diğeri "sunucu sana ne gösterir".

## 3. `ProductsController.cs` — üç action'ın güncellenmesi

```csharp
private static readonly List<Product> Products = new()
{
    new Product("SKU-001", "Wireless Mouse", 19.99m) { Id = 1 },
    ...
};

private static int _nextId = 4;

[HttpGet]
public ActionResult<IReadOnlyList<ProductDto>> GetAll()
{
    var dtos = Products.Select(ToDto).ToList();
    return Ok(dtos);
}
```

**Neden/Nasıl:** Artık liste `Product` (domain) tutuyor, `ProductDto` değil. `GetAll`'un dışarıya vereceği şey hâlâ `ProductDto` olmalı — bu yüzden `Products.Select(ToDto)` ile her `Product`'ı `ToDto` (aşağıda) kullanarak `ProductDto`'ya **elle** çeviriyoruz. `.Select(...)`, RoadmapOS Day 8'den bildiğimiz LINQ metodu — burada "her elemanı dönüştür" için kullanılıyor.

```csharp
[HttpPost]
public ActionResult<ProductDto> Create(CreateProductRequest request)
{
    var product = new Product(request.Sku, request.Name, request.Price)
    {
        Id = _nextId++
    };

    Products.Add(product);

    var dto = ToDto(product);
    return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
}
```

**Neden/Nasıl — asıl bugünkü konu:**
* `Create(CreateProductRequest request)` — model binder, `POST` body'sindeki JSON'ı otomatik olarak `CreateProductRequest`'e bağlıyor (RoadmapOS Day 5'teki `SkillFormModel model` parametresiyle birebir aynı mekanizma).
* `Id = _nextId++` — kendi elle yazdığımız, basit bir "auto-increment" taklidi (RoadmapOS'ta bunu SQL Server'ın `IDENTITY`'si yapıyordu, burada henüz DB yok).
* **`CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto)`** — bugünün en önemli satırı. Üç şey yapıyor:
  1. HTTP durum kodunu **201 Created** yapıyor (sadece `Ok()`'un 200'ü değil).
  2. Response'a bir **`Location`** header'ı ekliyor — değeri, `GetById` action'ına `id = dto.Id` parametresiyle giderek üretilen **gerçek URL** (`nameof(GetById)` kullanmak, `"GetById"` diye elle string yazmaktan daha güvenli — action adı değişirse burası da derleme hatası verir, Day 5'teki `RedirectToAction(nameof(Index))`'le aynı mantık).
  3. Response body'sine, oluşturulan kaynağın (`dto`) kendisini koyuyor.
* Bunun **neden önemli** olduğu: `Ok(dto)` dönseydik, istemci "tamam, oluşturuldu ama bu kaynağa nereden ulaşacağım" bilgisine sahip olmazdı — `Location` header'ı, "işte yeni kaydının kalıcı adresi" diyor. Bunu canlı doğruladık: `POST` sonrası dönen `Location: http://localhost:5402/api/Products/4` adresine `GET` attık, aynı ürünü aldık.

```csharp
private static ProductDto ToDto(Product product) =>
    new(product.Id, product.Sku, product.Name, product.Price);
```

**Neden:** Üç action'ın da tekrar tekrar yazmaması için, `Product` → `ProductDto` dönüşümünü **tek bir yerde** topladık — bu, "manuel mapping" (AutoMapper değil) prensibinin somut hâli. Dönüşüm burada, gözünün önünde, tek satırda — hiçbir "sihir" yok.

## 4. Bilinçli bir basitleştirme — `_nextId++`

```csharp
// TEMPORARY — not thread-safe (a real race is possible under concurrent
// POSTs). Acceptable only because Week 4 replaces this in-memory store
// with EF Core + SQL Server's own IDENTITY column.
private static int _nextId = 4;
```

**Neden bunu bugün düzeltmiyoruz:** `_nextId++` atomik bir işlem değil — iki istek **tam aynı anda** gelirse, ikisi de aynı ID'yi alabilir (RoadmapOS Day 3'teki Singleton+shared-mutable-state riskinin bir başka türü). Bunu bugün `Interlocked.Increment` gibi bir araçla düzeltmek yerine, **bilinçli olarak** işaretleyip bıraktık — çünkü Week 4'te bu bellek-içi liste tamamen ortadan kalkacak, yerini SQL Server'ın kendi `IDENTITY` mekanizmasına (RoadmapOS Day 4'te gördüğümüz gibi, bu sorunu doğal olarak çözen) bırakacak. Şimdi düzeltmek, kısa ömürlü bir koda gereksiz karmaşıklık eklemek olurdu.

---

## Doğrulanan davranış

```
dotnet build → 0 Hata, 0 Uyarı

POST /api/products (body: {"sku":"SKU-004","name":"Webcam","price":45.00})
  → HTTP 201 Created
  → Location: http://localhost:5402/api/Products/4
  → Body: {"id":4,"sku":"SKU-004","name":"Webcam","price":45.00}

GET (Location header'daki adres) → HTTP 200, aynı ürün

GET /api/products → artık 4 ürün listeleniyor
```
