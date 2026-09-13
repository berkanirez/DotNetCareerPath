# Day 13 — Kod Notları

Faz 2, Hafta 3, Gün 3 (Çarşamba — persistence/infrastructure). Konu: validation, `[ApiController]`'ın otomatik davranışı, global error handling.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer — bugün ayrıca gerçek bir varsayımı canlı test edip **kanıtladık**.

---

## 1. `Models/CreateProductRequest.cs` — validation attribute'ları

```csharp
using System.ComponentModel.DataAnnotations;

namespace StockPilot.Api.Models;

public record CreateProductRequest(
    [Required, StringLength(50)] string Sku,
    [Required, StringLength(200)] string Name,
    [Range(0.01, double.MaxValue)] decimal Price);
```

**Neden:** RoadmapOS Day 5'teki `[Required]`/`[StringLength]`'in aynısı — kullanıcıdan gelen veriye sınır koymak.

**Bugünkü asıl soru — record'ların pozisyonel parametrelerinde bu attribute'lar nasıl davranır:** Bazı kaynaklarda, bir record'un pozisyonel parametresine `[Required]` gibi bir attribute yazarken, bunun **hangi hedefe** (constructor parametresine mi, yoksa üretilen property'ye mi) uygulandığının belirsiz olabileceği, ve ASP.NET Core'un model validation'ının **property**'ye bakması gerektiği söylenir — bu yüzden bazen `[property: Required]` gibi açık bir hedef belirtmek "güvenli" tavsiye edilir.

**Biz ne yaptık:** Emin olmadığımız bu konuda **tahmin yürütmek yerine canlı test ettik** — attribute'ları **hedef belirtmeden** (`[property:]` olmadan) yazdık, geçersiz veri gönderdik, ve **gerçekten çalıştığını** gördük (aşağıdaki doğrulama bölümüne bak). Yani bugünkü .NET 10 sürümünde, bu belirsizliğin bizim için pratikte bir sorun olmadığını **kanıtladık**, ezbere bilgiye güvenmedik.

---

## 2. `[ApiController]`'ın otomatik validation davranışı — hiç kod yazmadan

`ProductsController`'da `Create` action'ında **hiçbir `if (!ModelState.IsValid)` kontrolü yok** — RoadmapOS Day 5'teki gibi elle bir kontrol eklemedik. Bunun yerine `[ApiController]` attribute'u (Day 11'den beri controller'ımızın üstünde duruyordu) şunu yapıyor: model geçersizse, **action'ın kodu hiç çalışmadan**, otomatik olarak `400 Bad Request` + hataları içeren bir `ValidationProblemDetails` JSON'u üretip dönüyor.

**Canlı doğrulama (boş `Sku` ile):**
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "Sku": ["The Sku field is required."] },
  "traceId": "..."
}
```
`StringLength` ve `Range` ihlalleri de aynı şekilde, kendi alanlarına özel mesajlarla otomatik döndü — hiçbirinde elle yazılmış bir kontrol yok.

**Beklenmedik bir gözlem:** `Range` hatasının mesajında `"between 0,01 and ..."` — **virgülle**! Bu, Day 8'de gördüğümüz Türkçe (`tr-TR`) kültür sorununun bir başka izi — sunucunun kültürü, hata mesajlarının sayı formatını da etkiliyor. Bugün bunu **düzeltmiyoruz** (kapsam dışı, ayrı bir gün gerektirir — muhtemelen sunucu genelinde `en-US` culture'a sabitlemek), ama fark ettiğimizi not düşüyoruz.

---

## 3. `Program.cs` — global error handling

```csharp
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
```

**Neden:** Şu ana kadar sadece **bizim kodladığımız** hatalar (404, 400) düzgün formatlıydı. Ya **beklenmeyen** bir exception fırlarsa (bir bug, bir null reference)? Bunu hiç test etmemiştik.

**Nasıl çalışıyor:**
* `builder.Services.AddProblemDetails()` — .NET'in yerleşik servisi; "her türlü hata sonucunu (404, 400, 500 fark etmez) tutarlı bir `ProblemDetails` JSON şablonuna dök" diyor.
* `app.UseExceptionHandler()` — parametresiz çağrıldığında (RoadmapOS'taki gibi belirli bir sayfaya yönlendirmek yerine), yakaladığı **her** işlenmemiş exception'ı otomatik olarak `AddProblemDetails()`'in kurduğu formatta, 500 durum koduyla döndürüyor.

**Karşılaştırma için — bunlar olmasaydı gerçekte ne dönerdi:** `AddProblemDetails()`/`UseExceptionHandler()`'ı geçici olarak devre dışı bırakıp aynı hatayı tekrar tetikledik. Gerçek çıktı:

```
HTTP/1.1 500 Internal Server Error
Content-Type: text/plain; charset=utf-8

System.InvalidOperationException: TEMPORARY test without global exception handling
   at StockPilot.Api.Controllers.ProductsController.GetAll() in C:\...\ProductsController.cs:line 25
   at lambda_method2(Closure, Object, Object[])
   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.SyncObjectResultExecutor.Execute(...)
   ... (ASP.NET Core'un iç çağrı zincirinin tamamı, ~10 satır daha)
```

Bu **ham bir C# stack trace** — `text/plain`, JSON değil; dosya yolunu, satır numarasını, hatta ASP.NET Core'un kendi iç sınıflarının adlarını bile içeriyor. Gerçek bir production API'sinde bu **güvenlik riski** de taşır (sunucunun iç yapısını dışarıya sızdırır). Ardından her iki satırı da geri açtık, `throw`'u kaldırdık, tekrar build alıp tüm endpoint'leri (`GetAll`/`GetById`/`Create`/`Delete`/geçersiz `Create`) regresyon için tekrar test ettik.

**Asıl (`AddProblemDetails`/`UseExceptionHandler` açıkken) canlı doğrulama:** `GetAll()`'a geçici olarak `throw new InvalidOperationException(...)` koyduk, çalıştırdık:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title": "An error occurred while processing your request.",
  "status": 500,
  "traceId": "..."
}
```
Ham bir stack trace **değil**, düzgün, makine-okunabilir bir JSON. Sonra bu geçici `throw`'u kaldırdık (`CS0162: Ulaşılamayan kod` uyarısı da onunla birlikte gitti) ve tüm endpoint'leri (`GetAll`, `GetById`, `Create`, `Delete`, geçersiz `Create`) tekrar test ederek regresyon olmadığını doğruladık.

---

## Doğrulanan davranış

```
dotnet build → 0 Hata, 0 Uyarı

Gecersiz POST (bos Sku)        → HTTP 400, ValidationProblemDetails (otomatik)
Gecersiz POST (uzun Sku)       → HTTP 400, StringLength mesaji (otomatik)
Gecersiz POST (negatif Price)  → HTTP 400, Range mesaji (otomatik, "0,01" — kultur izi)
Gecerli POST                   → HTTP 201

Gecici throw (GetAll icinde)   → HTTP 500, duzgun ProblemDetails JSON'u
throw kaldirildiktan sonra:
  GetAll  → 200
  GetById → 200
  Create  → 201
  Delete  → 204
  (regresyon yok)
```

## Bağımsız görev

Berkan, `Sku`'ya `[MinLength(2)]` ekledi:
```csharp
[Required, MinLength(2), StringLength(50)] string Sku,
```
Canlı doğrulandı: tek karakterli `Sku` (`"A"`) → HTTP 400, `"The field Sku must be a string or array type with a minimum length of '2'."`; iki karakterli (`"AB"`) → HTTP 201. Aynı otomatik `[ApiController]` validation mekanizması, eklenen yeni kural için de sorunsuz çalıştı.
