# Day 26 — Kod Notları

Faz 2, Hafta 5, Gün 26 — Hafta 5'in son günü. Konu: Policy-based authorization.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. `Program.cs` — isimli, merkezi bir kural

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanManageProducts", policy => policy.RequireRole("Admin"));
});
```

**Neden:** Day 25'te `[Authorize(Roles = "Admin")]` yazarken, `"Admin"` string'ini doğrudan controller'ın üzerine yazmıştık. Bugün aynı kısıtlamayı **ikinci bir yere** (`BulkCreate`) de eklemek istediğimizde, `"Admin"` string'ini tekrar yazmak yerine, kuralı **tek bir isim** altında, **tek bir yerde** tanımlıyoruz.

**Nasıl çalışıyor:** `AddAuthorization(options => ...)` — uygulama başlarken çalışan bir kurulum kodu. `options.AddPolicy("CanManageProducts", policy => policy.RequireRole("Admin"))` — "CanManageProducts" adında bir kural tanımlıyor, kuralın içeriği ise `policy.RequireRole("Admin")` — "Admin rolü şart" demek. Bu, aslında `[Authorize(Roles="Admin")]`'in arka planda yaptığı **aynı** kontrol — fark, artık bu kontrolün bir **isme** sahip olması ve tek bir yerde tanımlı olması.

---

## 2. `Controllers/ProductsController.cs` — iki endpoint, tek isim

```csharp
[Authorize(Policy = "CanManageProducts")]
[HttpDelete("{id}")]
public async Task<IActionResult> Delete(...)
```

```csharp
[Authorize(Policy = "CanManageProducts")]
[HttpPost("bulk")]
public async Task<ActionResult<IReadOnlyList<ProductDto>>> BulkCreate(...)
```

Day 25'teki `[Authorize(Roles = "Admin")]` yerine `[Authorize(Policy = "CanManageProducts")]` — davranış bugün için **birebir aynı** (ikisi de "Admin rolü şart" diyor), ama artık `"Admin"` string'i hiçbir controller'da yazılı değil, sadece `Program.cs`'teki tek satırda duruyor. `BulkCreate`'e bu kısıtlamanın eklenmesi, Day 25'te bilerek atlanan bağımsız görevin bugün, policy üzerinden tamamlanmış hali.

---

## Canlı kanıt 1 — her iki endpoint de aynı şekilde korunuyor

```
employee login + BulkCreate  → HTTP 403
admin login    + BulkCreate  → HTTP 201 (basarili)
employee login + Delete      → HTTP 403
admin login    + Delete      → HTTP 204 (basarili silme)
```

## Canlı kanıt 2 — "tek yerden değiştir, her yerde uygula" (asıl amaç)

`Program.cs`'teki tek satır **geçici olarak** bozuldu (`"Admin"` → `"SuperAdmin"`), **hiçbir controller koduna dokunulmadan**:

```csharp
options.AddPolicy("CanManageProducts", policy => policy.RequireRole("SuperAdmin")); // "Admin" degil!
```

Sonuç — admin token'ı (hâlâ `Role=Admin` taşıyor, `SuperAdmin` değil) artık **her iki** endpoint'te de reddediliyor:

```
admin ile BulkCreate (bozuk policy) → HTTP 403
admin ile Delete     (ayni token)   → HTTP 403
```

**Bunun önemi:** `ProductsController.cs`'e hiç dokunmadık — sadece `Program.cs`'teki tek bir tanım değişti, ama bu değişiklik **her iki endpoint'e aynı anda** yansıdı. Eğer hâlâ `[Authorize(Roles = "Admin")]` kullansaydık, aynı testi yapmak için **iki ayrı controller satırını** değiştirmemiz gerekirdi. Değişiklik geri alındı, düzeltme doğrulandı (üstteki "Canlı kanıt 1" tekrar geçerli).

---

## Doğrulanan davranış

```
dotnet build/test (StockPilot) → 0 Hata, 0 Uyari, 22/22 basarili (degisiklik yok, mevcut testler etkilenmedi)
dotnet test (RoadmapOS)        → 8/8 (etkilenmedi)
Veritabani demo sonrasi temiz (7 orijinal urun).
```

**Bugün eklenmeyen (bilinçli olarak):** Rol dışı, özel mantık gerektiren bir policy (`RequireAssertion`, custom `IAuthorizationHandler`) — bugünkü gerçek ihtiyaç hâlâ sadece bir rol kontrolü, uydurma bir senaryo icat etmek yerine bu genişleme noktasının var olduğu not düşüldü; gerçek ihtiyaç (örn. Order domain'i geldiğinde "sadece siparişin sahibi VEYA Admin" gibi bir kural) doğduğunda kullanılacak.
