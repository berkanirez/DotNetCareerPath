# Day 36 — Kod Notları

Faz 3, Hafta 8, Gün 36. Konu: FieldOps'un ilk gerçek HTTP entegrasyon testleri — Day 35'te elle kanıtladığımız tenant izolasyonunu (ve `X-Organization-Id` zorunluluğunu) otomatik testlerle kalıcı hale getirmek. StockPilot Day 27'nin `WebApplicationFactory` yaklaşımının FieldOps'taki karşılığı.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. `public partial class Program { }` sorgulaması — beklenmedik bir yan araştırma

StockPilot Day 27'de, `WebApplicationFactory<Program>`'ın ayrı bir test projesinden `Program` sınıfına erişebilmesi için `Program.cs`'in sonuna şunu eklemiştik:

```csharp
public partial class Program { }
```

O günkü gerekçe: top-level statements (`var builder = ...` şeklinde, sınıfsız yazılan `Program.cs`) derleyici tarafından **`internal` bir `Program` sınıfına** dönüştürülüyor; ayrı bir test assembly'sinden `internal` bir tipe generic parametre olarak (`WebApplicationFactory<Program>`) erişilemez (`CS0122` tarzı bir hata verir) — bu satır, o sınıfı `public` yapan "parça"yı (partial) tamamlıyor.

**Bugün bu satırı FieldOps.Api/Program.cs'e eklerken**, VS Code/IDE canlı olarak şu uyarıyı gösterdi:

> `ASP0027: Using public partial class Program { } to make the generated Program class public is no longer required in ASP.NET Core apps.`

Bunu **hemen doğru kabul etmedim** — CLAUDE.md'nin "çalıştığını kanıtlamadan iddia etme" kuralı sadece benim StockPilot'ta öğrettiğim bir varsayım için de geçerli. Canlı test ettim:

1. Satırı ekledim → `dotnet build FieldOps.slnx` → 0 hata/uyarı (IDE'nin gösterdiği hint, `dotnet build`'in varsayılan çıktısında görünmüyor, bu ayrı bir bulgu).
2. Satırı **tamamen kaldırdım**.
3. `tests/FieldOps.Api.Tests/EmployeesAuthorizationIntegrationTests.cs`'i, `IClassFixture<WebApplicationFactory<Program>>` kullanacak şekilde oluşturdum (aşağıda tam hali var).
4. `dotnet build FieldOps.slnx` → yine **0 hata/uyarı**.

**Sonuç: `public partial class Program { }` satırı olmadan da derleniyor ve testler çalışıyor.** Bunun kesin mekanizmasını (SDK'nın hangi versiyondan itibaren `Program`'ı otomatik `public` ürettiğini, ya da otomatik bir `InternalsVisibleTo` eklediğini) tam olarak doğrulayamadım — `src/FieldOps.Api/obj/` içinde `InternalsVisibleTo` için arama yaptım, boş sonuç döndü. **Dürüst not:** bu, StockPilot Day 27'de hiç test etmeden "gerekli" diye öğrettiğim bir satırın, aslında bu .NET 10 SDK'sında (muhtemelen top-level statements code-gen'inin güncellenmesiyle) artık gereksiz olabileceğini gösteriyor. Mekanizmayı değil, ampirik sonucu doğruladım — `Program.cs`'te bu satır **yok**, ve her şey çalışıyor.

---

## 2. `tests/FieldOps.Api.Tests/FieldOps.Api.Tests.csproj` — yeni paket

```
dotnet add package Microsoft.AspNetCore.Mvc.Testing
```

**Neden:** `EmployeeApplicationServiceTests` (Day 34), servis katmanını doğrudan çağırıyordu — HTTP'nin, middleware'in, model binding'in hiçbiri devrede değildi. Ama Day 35'in bulduğu asıl hata (`[FromHeader] int` vs `int?`) tam olarak **model binding**'de yaşanmıştı — bunu servis-katmanı testleriyle kanıtlamak yapısal olarak imkansız. `Microsoft.AspNetCore.Mvc.Testing`, `WebApplicationFactory<T>`'yi sağlıyor: gerçek bir HTTP sunucusunu (in-memory) ayağa kaldırıp gerçek bir `HttpClient` ile istek atmamızı sağlıyor.

---

## 3. `tests/FieldOps.Api.Tests/EmployeesAuthorizationIntegrationTests.cs` — yeni dosya

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FieldOps.Api.Tests;

public class EmployeesAuthorizationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EmployeesAuthorizationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAll_NoOrganizationHeader_ReturnsBadRequest() { ... }

    [Fact]
    public async Task Create_NoOrganizationHeader_ReturnsBadRequest() { ... }

    [Fact]
    public async Task Create_NonExistentOrganization_ReturnsBadRequest() { ... }

    [Fact]
    public async Task GetAll_ScopedToOrganization_NeverReturnsAnotherOrganizationsEmployees() { ... }

    private record EmployeeDto(int Id, string Name, int OrganizationId);
}
```

**Nasıl çalışıyor — parça parça:**

- `IClassFixture<WebApplicationFactory<Program>>`: xUnit'e "bu sınıftaki bütün testler, aynı `WebApplicationFactory` örneğini paylaşsın" diyor. StockPilot Day 27'den tanıdık bir desen — her test için sıfırdan bir uygulama ayağa kaldırmak yerine (yavaş), bir kere kaldırılıp tüm testler arasında paylaşılıyor (hızlı).
- `_factory.CreateClient()`: gerçek bir `HttpClient` döndürüyor, ama bu client ağ üzerinden değil, **in-memory** olarak doğrudan uygulamanın pipeline'ına bağlanıyor — gerçek bir port açmadan gerçek bir istek-yanıt döngüsü.
- `client.DefaultRequestHeaders.Add("X-Organization-Id", "1")`: her istekte otomatik olarak bu header'ı ekliyor — Day 35'te `curl -H` ile elle yaptığımızın kod karşılığı.
- `PostAsJsonAsync(url, new { Name = "..." })`: anonymous object'i otomatik olarak JSON'a çeviriyor, `CreateEmployeeRequest`'in beklediği `{"name": "..."}` gövdesini üretiyor.
- `GetFromJsonAsync<List<EmployeeDto>>(url)`: yanıt gövdesini doğrudan bir C# listesine deserialize ediyor.

**Testlerin ne kanıtladığı — sırayla:**

1. `GetAll_NoOrganizationHeader_ReturnsBadRequest`: header hiç yokken `GetAll` gerçekten `400` dönüyor mu — Day 35'in düzeltmesinin GetAll tarafını kanıtlıyor.
2. `Create_NoOrganizationHeader_ReturnsBadRequest`: aynısı `Create` için.
3. `Create_NonExistentOrganization_ReturnsBadRequest`: var olmayan bir `organizationId` (999) ile çalışan oluşturmaya çalışınca, `EmployeeApplicationService`'in (Day 34) organizasyon kontrolü gerçekten devreye giriyor mu.
4. `GetAll_ScopedToOrganization_NeverReturnsAnotherOrganizationsEmployees`: **asıl kritik test** — org 1'in header'ıyla bir çalışan oluşturup, org 2'nin header'ıyla listelendiğinde bu çalışanın **kesinlikle görünmediğini**, org 1'in header'ıyla listelendiğinde **göründüğünü** kanıtlıyor. Bu, Day 35'in çözdüğü güvenlik açığının bir daha asla geri gelmemesini garanti eden test.

**Neden benzersiz isimler (`Guid.NewGuid()`):** `IOrganizationDirectory`/`IEmployeeDirectory` `Singleton` olarak kayıtlı — yani tüm `WebApplicationFactory` ömrü boyunca (bu sınıftaki tüm testler boyunca) **aynı bellek** paylaşılıyor. Sabit bir isim kullansaydım, testler hangi sırayla çalışırsa çalışsın birbirinin verisini görebilir/karıştırabilirdi. StockPilot Day 27'nin "her testte benzersiz SKU" kuralıyla aynı disiplin, ama farklı bir sebepten: orada gerçek bir veritabanı paylaşılıyordu, burada paylaşılan şey bellekteki bir liste.

---

## 4. Canlı Red → Green kanıtı

Testlerin gerçekten bir şey kanıtladığından emin olmak için (yanlışlıkla her zaman yeşil çıkan, hiçbir şeyi test etmeyen bir test yazmamak için), `EmployeesController.GetAll`'daki `null` kontrolünü geçici olarak yorum satırına aldım:

```
dotnet test FieldOps.slnx --filter "GetAll_NoOrganizationHeader_ReturnsBadRequest"
→ BAŞARISIZ (Expected: BadRequest, Actual: OK)
```

Sonra kontrolü geri getirdim:

```
dotnet test FieldOps.slnx
→ Başarılı: 6, Toplam: 6
```

**Bu, testin gerçekten kontrolü kanıtladığının kanıtı** — kontrol olmadan kırmızı, kontrolle yeşil.

---

## 5. Regresyon kontrolü

```
dotnet test FieldOps.slnx    → 6/6 (2 eski servis testi + 4 yeni entegrasyon testi)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## 6. Bağımsız görev — başarı senaryosunu kapatan test (ve iki gerçek hata)

Görev: "başarı durumu" hiç test edilmemişti (sadece hata durumları) — `X-Organization-Id: 1` header'ı **var** olan bir `Create` isteğinin gerçekten `201 Created` döndüğünü ve gövdedeki `OrganizationId`'nin `1` olduğunu kanıtlayan yeni bir test eklemek.

**İlk yazılan hali** (canlı çalıştırılıp gerçek sonucu görülen):
```csharp
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
```
```
Expected: OK
Actual:   Created
```
Header doğru eklenmişti, controller da doğru çalışıyordu — beklenti yanlıştı. `EmployeesController.Create` bilinçli olarak `StatusCode(StatusCodes.Status201Created, dto)` döndürüyor (Day 34'ten beri değişmedi): bir kaynağın **oluşturulması**, REST'te `200 OK` değil `201 Created` ile ifade edilir.

**Düzeltilmiş hali:**
```csharp
var dto = await response.Content.ReadFromJsonAsync<EmployeeDto>();

Assert.Equal(HttpStatusCode.Created, response.StatusCode);
Assert.Equal(1, dto!.OrganizationId);
```
İlk yazımda ayrıca gövde hiç okunmuyordu — sadece status kodu kontrol ediliyordu, `OrganizationId`'nin gerçekten doğru geldiği hiç kanıtlanmıyordu. `ReadFromJsonAsync<EmployeeDto>()` ile gövde deserialize edilip bu eksik de kapatıldı.

**Son kanıt:** `dotnet test FieldOps.slnx` → 7/7 başarılı (6 önceki + bu yeni test).

**Not:** Test adı hâlâ `Create_OrganizationHeader_ReturnsOk` — artık `201 Created` doğruladığı için isim tam doğru değil (`...ReturnsCreated` daha isabetli olurdu), ama bu küçük bir isimlendirme notu, davranışı etkilemiyor.

---

## Bugünün bilinçli sınırı

Bu testler `WebApplicationFactory`'nin **in-memory** sunucusuna karşı çalışıyor — gerçek bir ağ, gerçek bir port, gerçek bir TLS anlaşması yok. Bu StockPilot Day 27'den beri bilinen bir demo-basitleştirmesi: gerçek üretimde bu testler CI'da gerçek bir HTTP sunucusuna karşı da (örneğin bir smoke test aşamasında) çalıştırılabilir, ama entegrasyon testinin asıl değeri (middleware + model binding + controller + servis + modül zincirinin uçtan uca doğruluğu) zaten in-memory sunucuyla da kanıtlanıyor.
