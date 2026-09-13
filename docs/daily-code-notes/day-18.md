# Day 18 — Kod Notları

Faz 2, Hafta 4, Gün 3 (Çarşamba — persistence/infrastructure). Konu: database constraints, SQL indexes.

Bu doküman, bugün eklenen/değişen her kod parçasını **yazılma/kullanılma sırasına göre** gezer.

---

## 1. Ön kontrol — kısıtlama eklemeden önce

```sql
SELECT Sku, COUNT(*) AS Adet FROM Products GROUP BY Sku HAVING COUNT(*) > 1
```

**Neden:** Yeni bir unique index eklemeden önce, mevcut veride **zaten bir çakışma olup olmadığını** kontrol etmek gerekiyor — eğer olsaydı, migration'ın kendisi (mevcut veriye kısıtlama uygulanamayacağı için) başarısız olurdu. Sonuç: çakışma yok, güvenle devam edildi. Bu, "önce doğrula, sonra uygula" prensibinin bir örneği — RoadmapOS Day 4'te de connection string/instance adını doğrulamadan migration'a girmemiştik.

## 2. `Data/StockPilotDbContext.cs` — unique index

```csharp
entity.HasIndex(p => p.Sku).IsUnique();
```

**Neden:** Aynı SKU'ya sahip iki ürün, gerçek bir iş kuralı ihlali — bu kısıtlamayı **uygulama kodunda bir `if` ile değil, veritabanının kendisinde** garanti altına alıyoruz (RoadmapOS Day 6'daki FK/max-length kısıtlamalarıyla aynı felsefe).

**Nasıl çalışıyor:** `HasIndex(p => p.Sku)` bir index tanımlıyor (arama hızlandırma), `.IsUnique()` bunu ayrıca "bu sütunda tekrar olamaz" kısıtlamasına çeviriyor. Migration, bunu gerçek bir SQL Server `UNIQUE INDEX`'ine çeviriyor.

## 3. Migration + uygulama

```
dotnet ef migrations add AddUniqueSkuIndex
dotnet ef database update
```

`IX_Products_Sku` adında bir unique index oluşturuldu.

---

## 4. Canlı Test 1 — doğrudan SQL, uygulamayı hiç kullanmadan

```sql
INSERT INTO Products (Sku, Name, Price) VALUES ('SKU-001', 'Sahte Kopya', 1.00)
```

**Sonuç:**
```
REDDEDILDI: Cannot insert duplicate key row in object 'dbo.Products' with
unique index 'IX_Products_Sku'. The duplicate key value is (SKU-001).
```

Kısıtlama gerçekten çalışıyor — uygulamanın kodunu hiç çalıştırmadan bile kırılamıyor.

## 5. Canlı Test 2 — gerçek API üzerinden, dürüst gözlem

```
POST /api/products  (body: {"sku":"SKU-001", ...})
→ HTTP 500 Internal Server Error
   {"type":"...", "title":"An error occurred while processing your request.", "status":500, ...}
```

**Ne oldu:** `EfProductStore.AddAsync`, `SaveChangesAsync()` çağırdığında SQL Server unique index ihlalini fırlatıyor, bu bir `DbUpdateException` olarak EF Core tarafından sarılıp yukarı fırlatılıyor. Hiçbir yerde bu exception'ı **yakalamadığımız** için, Day 13'te kurduğumuz `AddProblemDetails()`/`UseExceptionHandler()` bunu **genel bir sunucu hatası (500)** olarak ele alıyor.

**Neden bu yanlış (ama bugün bilerek düzeltmiyoruz):** 500, "sunucuda beklenmedik bir şey ters gitti" anlamına gelir — ama burada aslında **istemci** hatalı bir şey yaptı (zaten var olan bir SKU gönderdi). Doğru durum kodu **409 Conflict** olurdu. Bunu düzeltmek, `Create` action'ında bu spesifik hatayı **yakalayıp** anlamlı bir yanıta çevirmeyi gerektirir — bu, RoadmapOS Day 6'da FK/length kısıtlamalarını kanıtlayıp o günün kapsamını "zarif hata yönetimi" ile genişletmediğimiz gibi, **bilinçli olarak bugüne bırakılan bir sonraki adım**.

**Veritabanı bütünlüğü korundu:** Hem doğrudan SQL denemesi hem API denemesi sonrasında, `SKU-001`'in veritabanında hâlâ **tam olarak 1 kayıt** olduğu doğrulandı — kısıtlama, çirkin hata mesajına rağmen görevini yapıyor.

---

## Doğrulanan davranış

```
dotnet build/test (StockPilot) → 0 Hata, 0 Uyari, 9/9 test yesil (regresyon yok)

Dogrudan SQL ile duplicate SKU     → REDDEDILDI (unique index calisiyor)
POST ile duplicate SKU             → HTTP 500 (calisiyor ama yanlis durum kodu — bilinen, ertelenen bir bosluk)
SKU-001 kayit sayisi (her iki test sonrasi) → 1 (bozulma yok)
```
