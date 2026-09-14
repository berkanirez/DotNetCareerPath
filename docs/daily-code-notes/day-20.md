# Day 20 — Kod Notları

Faz 2, Hafta 4, Gün 5 (Cuma — refactor, tam doğrulama, dokümantasyon, Hafta 4 durum kontrolü). Bugün yeni bir domain konsepti eklenmedi; amaç küçük bir temizlik, sıfırdan tam doğrulama ve dürüst bir haftalık durum değerlendirmesiydi.

---

## 1. Sıfırdan temiz build/test doğrulaması

Her iki solution'ın `bin`/`obj` klasörleri silindi, sonra sıfırdan derlendi:

```
dotnet build StockPilot.slnx   → 0 Hata, 0 Uyari
dotnet test  StockPilot.slnx   → 11/11 basarili
dotnet build RoadmapOS.slnx    → 0 Hata, 0 Uyari
dotnet test  RoadmapOS.slnx    → 8/8 basarili
```

**Neden bu adım gerekli:** "benim makinemde derleniyor" ile "temiz bir checkout'tan derleniyor" iki farklı iddia — sadece `bin`/`obj` silinip yeniden derlemek ikincisini gerçekten test ediyor (Day 10'da RoadmapOS için yapılan aynı doğrulamanın StockPilot tarafındaki karşılığı).

---

## 2. `Controllers/ProductsController.cs` — tekrarlanan mesajın tek yere çıkarılması

Day 19'dan kalan `Create` metodunda aynı string **iki kez** yazılıyordu — biri Katman 1 (proaktif kontrol), diğeri Katman 2 (`catch` bloğu):

```csharp
// ÖNCE (Day 19 sonu):
if (await _productStore.SkuExistsAsync(request.Sku, cancellationToken))
{
    return Conflict($"A product with SKU '{request.Sku}' already exists.");
}
...
catch (DbUpdateException)
{
    return Conflict($"A product with SKU '{request.Sku}' already exists.");
}
```

```csharp
// SONRA (Day 20):
var duplicateSkuMessage = $"A product with SKU '{request.Sku}' already exists.";

if (await _productStore.SkuExistsAsync(request.Sku, cancellationToken))
{
    return Conflict(duplicateSkuMessage);
}
...
catch (DbUpdateException)
{
    return Conflict(duplicateSkuMessage);
}
```

**Neden bu şekilde yazıldı:** İki katman aynı iş kuralını (aynı SKU zaten var) farklı zamanlarda tespit ediyor, ama kullanıcıya söylenen mesaj **aynı olmak zorunda** — iki farklı yerde ayrı ayrı yazılmış olması, birinin değişip diğerinin unutulması riskini taşıyordu (klasik "iki yerde aynı bilgi" bakım sorunu). Metodun en başında bir kere hesaplanan `duplicateSkuMessage` yerel değişkeni, tek doğruluk kaynağı (single source of truth) haline getiriyor.

**Nasıl çalışıyor:** `$"..."` bir string interpolation ifadesi — `request.Sku` metodun parametresinden geldiği için, metot içindeki her yerden erişilebilir. Yerel değişkene atamak sadece bu ifadeyi bir kez değerlendirip sonucu saklamak anlamına geliyor; iki `return Conflict(...)` satırı artık aynı önceden hesaplanmış string'i kullanıyor, yeniden hesaplamıyor.

**Bilinçli olarak yapılmayan:** Ayrı bir private metoda (`BuildDuplicateSkuMessage(string sku)`) çıkarmadım — tek bir metodun içinde, iki komşu kullanım için bir yerel değişken yeterli; ayrı bir metot burada gereksiz bir soyutlama olurdu.

---

## 3. Refactor sonrası regresyon kontrolü

```
dotnet build StockPilot.slnx → 0 Hata, 0 Uyari
dotnet test  StockPilot.slnx → 11/11 basarili (degisiklik yok)
```

Refactor davranışı değiştirmedi — sadece kodun kendi içindeki tekrarı kaldırdı. Bunu kanıtlayan şey, testlerin sayısının ve sonucunun (11/11) refactor öncesiyle birebir aynı kalması.

---

## 4. Hafta 4 durum değerlendirmesi (kod değil, dürüst bir envanter)

Bugün yeni kod yerine, Hafta 4'ün gerçek durumu netleştirildi:

**Hafta 4'te tamamlanan (Day 16-19):**
* Day 16: EF Core + SQL Server'a geçiş (`StockPilotDbContext`, `EfProductStore`, `DbSeeder`).
* Day 17: `IProductStore`/`ProductsController` async'e çevrildi, `CancellationToken` eklendi.
* Day 18: `Sku` üzerinde unique index eklendi, canlı olarak kanıtlandı; 500 yerine 409 dönmesi gerektiği tespit edildi ama o gün bilinçli olarak düzeltilmedi.
* Day 19: İki katmanlı savunma (`SkuExistsAsync` + `try/catch DbUpdateException`) ile 500→409 boşluğu kapatıldı.

**Hafta 4'ün orijinal konu listesinden Day 21'e (ve gerekirse Day 22'ye) taşınanlar:**
* **Update (PUT) endpoint'i + optimistic concurrency** (örn. `Product` üzerinde bir `RowVersion`/concurrency token) — Day 21'e planlanıyor.
* **Transactions ve query analysis** — eğer Day 21 bunlara sığmazsa Day 22'ye taşınacak.
* **"Stock-reservation rules"** — bu, henüz var olmayan bir `Order` domain'ine bağımlı bir konu (bir siparişin bir ürünün stokunu "rezerve etmesi" fikri, ortada henüz sipariş kavramı yokken anlamsız). Bu, Hafta 4'e (hatta Day 21/22'ye) değil, StockPilot'un "Order API" tarafı gerçekten başladığında ele alınacak, daha ileri bir konu olarak işaretlendi — bugün veya yarın çözülecek bir eksik değil.

**Neden bu şekilde karar verildi:** Kalan konuları bugüne sıkıştırmak ya da sessizce atlamak yerine, bu durum açıkça ortaya kondu ve Hafta 4'ün 1-2 gün uzatılmasına karar verildi (Berkan'ın tercihi) — `CLAUDE.md`'nin "bir günün Definition of Done'ı karşılanmadan bir sonraki güne geçilmez" kuralına sadık kalarak.

---

## Doğrulanan davranış

```
Temiz checkout'tan build/test: StockPilot 11/11, RoadmapOS 8/8, ikisi de 0 Hata/0 Uyari.
Refactor sonrasi: davranis degismedi, ayni 11/11.
```
