# Day 49 — Kod Notları

Faz 3, Hafta 10, Gün 49. Konu: **Redis cache invalidation** — Day 48'in bilinçli olarak açık bıraktığı tek eksik: `GET /api/workorders/report`'un, bir iş emrinin durumu değiştiğinde 30 saniyelik TTL'i beklemeden anında güncel veri döndürmesi.

---

## 1. `WorkOrderReportService.InvalidateCache` — yeni metot

```csharp
// Day 49: active invalidation — called by every controller action that
// changes a work order's Status (the only thing this report counts).
// Reassign/Approve never touch Status, so they never call this.
public void InvalidateCache(int organizationId)
{
    var db = _redis.GetDatabase();
    db.KeyDelete($"workorders:report:{organizationId}");
}
```

**Neden yazıldı:** Day 48'de cache-aside'ın sadece "oku" tarafı vardı (`GetStatusReport` — yoksa hesapla, varsa döndür). Bugün "yaz" tarafındaki eksik parça eklendi: veri gerçekten değiştiğinde cache'i **proaktif olarak** temizlemek, TTL'in kendi kendine dolmasını beklemek yerine.

**Nasıl çalışıyor:** `db.KeyDelete(cacheKey)`, alttan alta Redis'in `DEL <key>` komutunu çalıştırır — anahtarı Redis'ten tamamen siler (TTL'i beklemeden). Bir sonraki `GetStatusReport` çağrısı bu yüzden otomatik olarak bir **cache miss** olur, gerçek veriyi SQL Server'dan yeniden okur, doğru sonucu hesaplayıp yeni bir TTL ile tekrar cache'ler. Aynı `GetDatabase()`'i (`_redis.GetDatabase()`) kullanıyor — `WorkOrderReportService`'in kendi içindeki, Day 48'den beri var olan aynı `IDatabase` erişimi.

**Neden `WorkOrdersController`'a `IConnectionMultiplexer` inject edip anahtarı orada silmek yerine, servise bir metot eklendi:** Cache anahtarının formatı (`workorders:report:{organizationId}`) `WorkOrderReportService`'in kendi iç detayı. Bu detayı controller'ın da bilmesi gerekseydi, aynı string formatı iki farklı dosyada tekrarlanmış olurdu — biri değişip diğeri unutulursa (örneğin anahtar formatı `report:workorders:{id}` olarak değiştirilse), cache hiç temizlenmeyen sessiz bir bug doğardı. Formatı tek bir yerde (servisin kendisinde) tutmak, bu riski ortadan kaldırıyor.

---

## 2. Hangi action'lar `InvalidateCache` çağırıyor, hangileri çağırmıyor — ve neden

Rapor sadece `WorkOrder.Status`'a göre sayım yapıyor (Open/Assigned/InProgress/Completed). Bu yüzden kural basit: **sadece `Status`'u değiştiren action'lar invalidation tetikler.**

| Action | Status değişimi | Invalidation eklendi mi? |
|---|---|---|
| `Create` | (yeni) → `Open` | ✅ |
| `Assign` | `Open` → `Assigned` | ✅ |
| `Start` | `Assigned` → `InProgress` | ✅ |
| `Complete` | `InProgress` → `Completed` | ✅ |
| `Unassign` | `Assigned`/`InProgress` → `Open` | ✅ |
| `Reopen` | `Completed` → `InProgress` | ✅ |
| `Reassign` | değişmiyor (sadece `AssignedEmployeeId`) | ❌ kasıtlı olarak eklenmedi |
| `Approve` | değişmiyor (sadece `CustomerApproved`) | ❌ kasıtlı olarak eklenmedi |

Örnek — `Complete`:
```csharp
var updated = _workOrderDirectory.Complete(id);
if (updated is null)
{
    return BadRequest($"Work order {id} must be InProgress before it can be completed.");
}

_workOrderReportService.InvalidateCache(organizationId!.Value);
return Ok(ToDto(updated));
```

**Önemli detay — invalidation her zaman mutasyon `null` dönmeyip GERÇEKTEN başarılı olduktan SONRA çağrılıyor.** Eğer `_workOrderDirectory.Complete(id)` `null` dönerse (yani iş emri `InProgress` değilse), fonksiyon zaten `BadRequest` ile erken dönüyor — `InvalidateCache` hiç çalışmıyor. Bu doğru: başarısız bir mutasyon için cache'i boşuna silmenin hiçbir faydası yok, sadece bir sonraki okuma isteğinin gereksiz yere SQL Server'a gitmesine neden olur.

`Reassign` ve `Approve`'un neden dışarıda bırakıldığı, Day 48'in `EvidenceNotes`/`CustomerApproved` gibi alanların rapora hiç girmediğinin doğal bir sonucu — rapor sadece `Status`'u sayıyor, bu iki action ise `Status`'u hiç değiştirmiyor. Bu yüzden onlara invalidation eklemek, hiçbir zaman gözlemlenemeyecek bir kod yazmak olurdu.

---

## 3. Canlı kanıt — TTL beklenmeden anında güncel veri

Gerçek bir sunucu çalıştırılıp tam bir yaşam döngüsü izlendi, her adımdan **hemen sonra** (30 saniye beklenmeden) rapor tekrar çağrıldı:

```
1) Cache temizlendi, baseline: {open:0, assigned:0, inProgress:0, completed:0}
2) Create (yeni is emri, id=2)          -> rapor HEMEN: {open:1, ...}
3) Assign (employeeId=2)                -> rapor HEMEN: {open:0, assigned:1, ...}
4) Start (assignee=2)                   -> rapor HEMEN: {assigned:0, inProgress:1, ...}
5) Complete (assignee=2)                -> rapor HEMEN: {inProgress:0, completed:1}
6) Reopen (Completed -> InProgress)     -> rapor HEMEN: {inProgress:1, completed:0}
7) Reassign (InProgress -> InProgress)  -> rapor DEGISMEDI (dogru, Status ayni kaldi)
   Redis'te anahtarin TTL'i kontrol edildi: hala 30 saniye dolu
   -> Reassign'in cache'i SILMEDIGI, sadece onceki (Reopen sonrasi) yazilan
      cache girisinin hala canli oldugu dogrulandi.
```

Her adımda gerçek bir SQL Server yazması (`EfWorkOrderDirectory`'nin ilgili metodu) + gerçek bir Redis `DEL` + gerçek bir sonraki `GET`'in cache miss olup yeniden hesaplaması zinciri **canlı** izlendi — hiçbiri varsayılmadı.

**Temizlik:** Test için oluşturulan iş emri (`Id=2`), `sqlcmd` ile `FieldOpsWorkOrders` veritabanından silinerek dev ortamı baseline'a (boş tablo) döndürüldü; Redis anahtarı da elle temizlendi.

---

## 4. Bilinçli olarak eklenmeyen bir şey — otomatik test

`GetStatusReport`/`InvalidateCache`'i egzersiz eden bir integration test **bugün eklenmedi**, kasıtlı olarak: GitHub Actions'ın `ubuntu-latest` çalıştırıcısında gerçek bir Redis sunucusu yok (Day 48'de SQL Server için yaşanan CI-kırılma probleminin aynısı, bu kez Redis için). `FieldOpsApiFactory`, dört SQL Server veritabanını Testcontainers ile sağlıyor ama Redis'i hiç override etmiyor — bir test bu uç noktayı çağırırsa, `appsettings.Development.json`'daki gerçek `localhost:6379`'a bağlanmaya çalışır, ve CI ortamında bu bağlantı başarısız olur. Bu yüzden rapor/invalidation davranışı bugün de (Day 48'deki gibi) sadece canlı, elle yapılan doğrulamayla kanıtlandı — otomatik kapsam, CI'a bir Redis servisi/Testcontainers.Redis eklenmesini gerektiren, ayrı bir gün olarak işaretlendi.

---

## Regresyon

```
dotnet test FieldOps.slnx    → 49/49 (degismedi — hicbiri report/invalidation'i cagirmiyor)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```
