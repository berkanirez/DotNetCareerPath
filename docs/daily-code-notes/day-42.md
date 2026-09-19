# Day 42 — Kod Notları

Faz 3, Hafta 9, Gün 42. Konu: `Assigned → InProgress → Completed` geçişleri ve **üçüncü bir yetkilendirme türü**: sahiplik (ownership) tabanlı yetkilendirme.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. Üç yetkilendirme türü — bugüne kadarki yolculuk

- **Day 35:** Tenant üyeliği — "hangi organizasyondansın?"
- **Day 37:** Rol (RBAC) — "hangi roldesin?" (genel bir grup kuralı)
- **Day 42 (bugün):** Sahiplik (ownership) — "bu **spesifik kayıtla** ilişkin misin?" Bir Admin bir işi birine atayabilir, ama bu Admin'in o işi bizzat yapacağı anlamına gelmiyor.

---

## 2. `WorkOrderStatus` — iki yeni değer

```csharp
public enum WorkOrderStatus { Open, Assigned, InProgress, Completed }
```

---

## 3. `IWorkOrderDirectory.Start`/`Complete` — modülün kendi durum makinesi kuralı

```csharp
public WorkOrderSummary? Start(int workOrderId);      // sadece Assigned -> InProgress
public WorkOrderSummary? Complete(int workOrderId);   // sadece InProgress -> Completed
```

**Dikkat: bu metotlar hiç `employeeId` almıyor.** Day 41'in `Assign`'ı, atanacak çalışanın var olup olmadığını/organizasyonunu kontrol etmek için `employeeId` alıyordu — çünkü bu **çapraz-modül bir gerçek** (Employees modülüne sormak gerekiyordu). Ama "isteği yapan, bu iş emrinin atandığı kişi mi" sorusu, `WorkOrder`'ın **kendi** `AssignedEmployeeId` alanına bakmak yeterli — hiçbir başka modüle ihtiyaç yok. Bu yüzden bu kontrol modülün içine değil, **host'un controller'ına** kondu (aşağıya bakın) — modül sadece "doğru önceki durumda mıyız" kuralını kendi başına koruyor.

---

## 4. `WorkOrdersController.Start`/`Complete` — sahiplik kontrolü burada

```csharp
private ActionResult? ValidateOwnership(int workOrderId, int? organizationId, int? actingEmployeeId, out WorkOrderSummary? workOrder)
{
    workOrder = _workOrderDirectory.GetById(workOrderId);
    if (workOrder is null || workOrder.OrganizationId != organizationId)
    {
        workOrder = null;
        return BadRequest($"Work order {workOrderId} does not exist.");   // Day 41'in ayni gizleme mesaji
    }

    if (workOrder.AssignedEmployeeId != actingEmployeeId)
    {
        return StatusCode(StatusCodes.Status403Forbidden, "Only the assigned employee can act on this work order.");
    }

    return null;
}
```

`Start`/`Complete` action'ları: `ValidateMembership` (tenant) → `ValidateOwnership` (sahiplik) → modülün `Start`/`Complete` metodunu çağırıp `null` dönerse ("yanlış önceki durum") `400`.

**Neden `Start_OnUnassignedWorkOrder` testi `403` döndürüyor, `400` değil:** `AssignedEmployeeId` `null` iken, hiçbir gerçek `actingEmployeeId` buna asla eşit olamaz — yani atanmamış bir iş emri için sahiplik kontrolü **her zaman** önce başarısız olur, "yanlış durum" kontrolüne hiç sıra gelmez. Bunu test yazarken tahmin ettim, canlı çalıştırıp doğruladım — tahminim doğru çıktı.

---

## 5. Canlı kanıt — tam yaşam döngüsü

```
1) Admin olusturur + Org1 Member'a (id=2) atar         → Assigned
2) Admin (atanan degil) baslatmaya calisir             → 403
3) Atanan calisan (id=2) baslatir                       → 200, InProgress
4) Ayni calisan tamamlar                                → 200, Completed
5) Tekrar tamamlamaya calisir                           → 400 "must be InProgress"
```

Beşi de ilk denemede doğru çıktı.

---

## 6. Otomatik testler + Red→Green

5 yeni test: meşru start, Admin'in (atanan olmayan) start denemesi, atanmamış bir işi start denemesi, meşru complete, start'ı atlayıp doğrudan complete denemesi.

Kanıt için sahiplik kontrolünü yorum satırına aldım:
```
dotnet test --filter Start_ByAdminWhoIsNotTheAssignee_ReturnsForbidden
→ BAŞARISIZ (Expected: Forbidden, Actual: OK)
```
Geri getirdim → `dotnet test FieldOps.slnx` → **25/25 başarılı** (20 eski + 5 yeni).

---

## 7. Regresyon

```
dotnet test FieldOps.slnx    → 25/25
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## Bugünün bilinçli sınırı

Bir Admin'in bir işi zorla tamamlaması/geçersiz kılması gibi bir "override" mekanizması yok — bilerek, ileriki bir güne bırakıldı. Dosya kanıtı (file evidence) ve müşteri onayı (customer approval) da haftanın geri kalanına bırakıldı.
