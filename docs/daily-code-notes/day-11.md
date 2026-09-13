# Day 11 — Kod Notları

Faz 2, Hafta 3, Gün 1. Konu: StockPilot Inventory and Order API'ye başlangıç — proje kurulumu, controller-based Web API, HTTP durum kodları, ilk vertical slice.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. Proje yapısı kararı

```
dotnet new sln -n StockPilot
dotnet new webapi -n StockPilot.Api -o src/StockPilot.Api --use-controllers
dotnet sln StockPilot.slnx add src/StockPilot.Api/StockPilot.Api.csproj
```

**Neden:** StockPilot, RoadmapOS'tan **tamamen ayrı** bir ürün — aynı öğrenme deposunda yaşasa da, aynı solution'a eklemek onları yapay olarak birbirine bağlardı (biri diğerini derlerken beklemek zorunda kalır, ikisi arasında yanlışlıkla bir referans eklenebilir). Bu yüzden ayrı bir `StockPilot.slnx` açtık — gerçek şirketlerde farklı ürünlerin ayrı repo/solution'lara sahip olması gibi.

**`--use-controllers` neden gerekli:** .NET'in son sürümlerinde `dotnet new webapi` **varsayılan olarak Minimal API** şablonunu üretiyor (tek dosyada `app.MapGet(...)` gibi çağrılarla). `CLAUDE.md`'nin "controller-based API'ler" kuralı gereği, bu bayrakla **RoadmapOS'takine benzer**, `[ApiController]` sınıflarına dayalı klasik yapıyı seçtik.

## 2. Bir güvenlik uyarısı — canlı yakalandı ve düzeltildi

Proje oluşturulduğunda şu uyarı çıktı:
```
warning NU1903: 'Microsoft.OpenApi' 2.0.0 paketinde önem derecesi yüksek olan
bilinen bir güvenlik açığı var
```

**Ne yaptık:** `dotnet add package Microsoft.AspNetCore.OpenApi --version 10.0.11` ile, bu güvenlik açıklı sürümü otomatik olarak çeken paketi güncelledik — bu da beraberinde düzeltilmiş `Microsoft.OpenApi 2.7.5`'i getirdi. Build sonrası uyarı tamamen kayboldu.

**Neden önemli:** `CLAUDE.md`'nin "güvenlik açığı oluşturmamaya dikkat et" prensibi sadece elle yazdığımız kod için değil, **otomatik oluşturulan proje şablonları için de** geçerli — bir proje sihirbazının ürettiği kod bile kör kör güvenilecek bir şey değil, `dotnet build`'in verdiği uyarılar ciddiye alınmalı.

## 3. `Program.cs` — RoadmapOS'unkinden farkı

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

**Fark 1:** `AddControllersWithViews()` (RoadmapOS) yerine sadece `AddControllers()` — çünkü burada **hiç view yok**, sadece JSON dönen action'lar var.

**Fark 2:** `AddOpenApi()` / `MapOpenApi()` — .NET'in **yerleşik** (üçüncü parti Swashbuckle olmadan) OpenAPI desteği. Bu, `/openapi/v1.json` adresinde **ham bir OpenAPI şeması** (JSON) üretiyor — API'nin hangi endpoint'leri, hangi parametreleri olduğunu makine-okunabilir şekilde tarif eden bir belge.

**Önemli bir düzeltme (plan ile gerçek arasında fark):** Planımda "Swagger UI'da canlı deneme" demiştim, ama bu şablon **interaktif bir Swagger UI sayfası içermiyor** — sadece ham JSON şemasını veriyor. Klasik, tıklanabilir Swagger UI için ayrı bir paket (örn. Swashbuckle'ın UI katmanı veya Scalar) eklemek gerekirdi. Bugün bunu **eklemedik** — gereksiz bir paket olurdu, bugünün asıl amacı (ilk endpoint'i çalıştırmak) için `curl` ile JSON çıktısını doğrulamak yeterliydi. Bu, .NET ekosisteminde gerçek, güncel bir değişikliğin (Microsoft'un kendi OpenAPI desteğine geçişi) doğrudan planımıza yansıması — kitaplardaki/eski tutorial'lardaki "Swagger UI otomatik gelir" bilgisi artık güncel değil.

## 4. `Models/ProductDto.cs`

```csharp
namespace StockPilot.Api.Models;

public record ProductDto(int Id, string Sku, string Name, decimal Price);
```

**Neden:** Day 5'te öğrendiğimiz DTO prensibinin API dünyasındaki hâli — henüz bir domain entity'miz bile yok, doğrudan DTO ile başlıyoruz. `record` seçildi çünkü bu, dışarıya **sadece okunacak, değişmeyecek** bir veri parçası (Day 2'deki `SkillSnapshot` mantığı).

## 5. `Controllers/ProductsController.cs`

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private static readonly List<ProductDto> Products = new() { ... };

    [HttpGet]
    public ActionResult<IReadOnlyList<ProductDto>> GetAll()
    {
        return Ok(Products);
    }
}
```

**Neden/Nasıl:**
* `: ControllerBase` — RoadmapOS'taki `: Controller`'ın **view desteği olmayan** hâli. API controller'ları view render etmeyeceği için, daha hafif olan `ControllerBase`'den türüyor.
* `[ApiController]` — API'ye özgü davranışları açıyor: örneğin model binding hataları otomatik olarak `400 Bad Request` + `ProblemDetails` formatına dönüşüyor (bunu ileri günlerde göreceğiz).
* `[Route("api/[controller]")]` — RoadmapOS'taki convention routing'in (`{controller=Home}/{action=Index}`) aksine, API'ler genelde **açık (explicit) route** tanımlar. `[controller]`, class adından "Controller" kısmını atıp yerine koyuyor → `api/Products`.
* `[HttpGet]` — Day 5'te gördüğümüz `[HttpGet]`/`[HttpPost]` ayrımının aynısı.
* `ActionResult<IReadOnlyList<ProductDto>>` — dönüş tipi, hem "başarılıysa şu tip veri dönebilirim" (`IReadOnlyList<ProductDto>`) hem de "veya bir HTTP sonucu (404 gibi) dönebilirim" ihtimalini aynı anda taşıyor.
* `return Ok(Products);` — RoadmapOS'taki `return View(...)`'ün API karşılığı: `Ok(...)`, HTTP 200 durum kodunu **açıkça** belirtip, verilen nesneyi JSON'a çevirip response body'sine yazıyor.

---

## Doğrulanan davranış

```
dotnet build (StockPilot.Api) → 0 Hata, 0 Uyarı (güvenlik uyarısı dahil düzeltildi)

GET /api/products → HTTP 200
[{"id":1,"sku":"SKU-001","name":"Wireless Mouse","price":19.99}, ...]

GET /openapi/v1.json → HTTP 200 (ham OpenAPI şeması, interaktif UI değil)
```
