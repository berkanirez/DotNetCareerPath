# Day 35 — Kod Notları

Faz 3, Hafta 8, Gün 35 — Hafta 8'in ilk günü. Konu: multi-tenancy, tenant isolation, gerçek bir güvenlik açığının kapatılması.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. Canlı kanıt — önce açığın kendisi

API çalıştırılıp, iki farklı organizasyonda (`organizationId=1` ve `2`) birer çalışan oluşturuldu, sonra:

```
GET /api/employees   (hic filtre yok)
→ [{"id":1,...,"organizationId":1}, {"id":2,...,"organizationId":2}]

GET /api/employees?organizationId=2   (org 1 icin calisan bir istemci, org 2'yi soruyor)
→ [{"id":2,...,"organizationId":2}]
```

**Bu, gerçek bir tenant'lar arası veri sızıntısı.** Day 33'ün bağımsız görevinde eklediğimiz `?organizationId=` filtresi, "isteğe bağlı bir kolaylık" gibi görünüyordu ama aslında **hiçbir zorunluluk taşımıyordu** — istemci filtreyi hiç göndermeyebilir (herkesi görür) ya da başka birinin ID'sini gönderebilir (onun verisini görür).

---

## 2. `src/FieldOps.Api/Models/CreateEmployeeRequest.cs` — `OrganizationId` çıkarıldı

```csharp
public record CreateEmployeeRequest([Required, StringLength(200)] string Name);
```

**Neden:** Hangi organizasyona ait olduğu, artık **istemcinin gövdede söylediği bir şey değil** — header'dan (aşağıya bakınız) geliyor. İstemciye "sen hangi organizasyonsun" diye sormak, tam da bugünkü açığın kaynağıydı.

---

## 3. `EmployeesController.cs` — ilk deneme (ve neden yanlış çıktığı)

**İlk yazdığım hali:**
```csharp
[HttpGet]
public ActionResult<IReadOnlyList<EmployeeDto>> GetAll([FromHeader(Name = "X-Organization-Id")] int organizationId)
```

Buradaki varsayımım: "`int` (nullable değil, varsayılan değeri de yok) olduğu için, header hiç gelmezse ASP.NET Core bunu otomatik olarak `400` ile reddeder." **Bunu test etmeden yazdım — ve yanlış çıktı.**

**Canlı kanıt (gerçek sonuç):**
```
GET /api/employees   (X-Organization-Id header'i HIC yok)
→ HTTP 200, []   (400 DEGIL!)
```

**Gerçek sebep:** ASP.NET Core, `[FromHeader]`/`[FromQuery]` ile bir `int` (referans olmayan, "nullable olmayan" bir değer tipi) bağlarken, header/query hiç yoksa bunu **hata saymıyor** — sessizce `default(int)` yani `0` değerini veriyor. `organizationId=0` olunca, `.Where(e => e.OrganizationId == 0)` da hiçbir şeyle eşleşmediği için boş liste dönüyor — **reddetme değil, sadece hiçbir şeyle eşleşmeyen bir sorgu**.

**Doğru düzeltme:**
```csharp
[HttpGet]
public ActionResult<IReadOnlyList<EmployeeDto>> GetAll([FromHeader(Name = "X-Organization-Id")] int? organizationId)
{
    if (organizationId is null)
    {
        return BadRequest("X-Organization-Id header is required.");
    }
    ...
}
```

`int?` (nullable) kullanmak, "header hiç yoktu" ile "header 0 değeriyle geldi" durumlarını **ayırt edilebilir** hale getiriyor — `null` sadece ilkinde oluşuyor. Bu sayede **elle** bir kontrol yazıp gerçek bir `400` üretebiliyoruz.

**Bu, bugünün en değerli dersi:** ASP.NET Core'un "otomatik doğrulayacak" varsayımı, **her zaman canlı olarak kontrol edilmeli** — StockPilot'ta `[Required]` gibi DataAnnotation'larla otomatik doğrulama görmüştük (Day 13), ama o mekanizma `[FromHeader]`/`[FromQuery]` ile bağlanan **nullable olmayan basit tipler** için aynı şekilde çalışmıyor.

---

## 4. Düzeltilmiş hali — canlı kanıt

```
GET /api/employees                          (header yok)     → HTTP 400 "X-Organization-Id header is required."
GET /api/employees  -H "X-Organization-Id: 1"                → sadece org 1'in calisanlari
GET /api/employees  -H "X-Organization-Id: 2"                → sadece org 2'nin calisanlari
POST /api/employees (header yok)                             → HTTP 400 (Create de ayni sekilde korunuyor)

dotnet test FieldOps.slnx    → 2/2 (etkilenmedi, EmployeeApplicationService dogrudan test ediliyor, HTTP katmanina dokunmuyor)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyari (etkilenmedi)
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyari (etkilenmedi)
```

---

## Bugünün bilinçli sınırı

`X-Organization-Id` **hâlâ istemcinin gönderdiği düz bir header** — yani istemci, "ben org 1'im" yerine "ben org 2'yim" diye yalan söyleyebilir, çünkü bu bilgiyi doğrulayan bir kimlik doğrulama mekanizması yok. Bu, StockPilot'un Day 23 öncesi hiç authentication'ı olmaması gibi bir durum — önce **mekanizmayı** (ambient tenant context, sorgunun her zaman filtrelenmesi) kurduk, gerçek kimlik doğrulamayla (muhtemelen bu haftanın ilerleyen günlerinde, bir JWT claim'i olarak) güçlendirmek ileriki bir gün.
