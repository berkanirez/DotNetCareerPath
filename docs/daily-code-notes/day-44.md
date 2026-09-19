# Day 44 — Kod Notları

Faz 3, Hafta 9, Gün 44. Konu: **ilk birleşik yetkilendirme kuralı** — rol **VEYA** sahiplik. Day 43'ün bağımsız görevinde konuştuğumuz fikrin hayata geçirilmesi.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. `ValidateIsAdminOrAssignee` — yeni, birleşik kontrol

```csharp
private ActionResult? ValidateIsAdminOrAssignee(int workOrderId, int? organizationId, int? actingEmployeeId, string action)
{
    var actingEmployee = _employeeDirectory.GetById(actingEmployeeId!.Value)!;

    var workOrder = _workOrderDirectory.GetById(workOrderId);
    if (workOrder is null || workOrder.OrganizationId != organizationId)
    {
        return BadRequest($"Work order {workOrderId} does not exist.");
    }

    if (actingEmployee.Role != EmployeeRole.Admin && workOrder.AssignedEmployeeId != actingEmployeeId)
    {
        return StatusCode(StatusCodes.Status403Forbidden, $"Only an Admin or the assigned employee can {action} this work order.");
    }

    return null;
}
```

**Bu, şu ana kadarki tüm kontrollerden farklı:** Day 35 (tenant), Day 37 (rol), Day 42 (sahiplik) hep **tek bir kategori** kontrol ediyordu. Buradaki koşul `actingEmployee.Role != Admin && workOrder.AssignedEmployeeId != actingEmployeeId` — yani **her ikisi de** başarısız olmalı ki reddedilsin. De Morgan kuralıyla düşünürsek: "Admin OL, YA DA atanan kişi ol" yeterli.

**`ValidateIsAdmin`'e (Day 43) göre fark:** `ValidateIsAdmin` sadece isteği yapan çalışana bakıyordu, iş emrine hiç bakmıyordu. Bu yeni metot **iş emrini de** çekiyor — çünkü "atanan kişi sen misin" sorusuna cevap vermek için önce "atanan kişi kim" bilgisine ihtiyaç var.

---

## 2. `WorkOrdersController.Reassign` ve `Assign` arasındaki bilinçli fark

- `Assign` (Day 41): **hâlâ sadece Admin.** Yeni bir işi kime dağıtacağına karar vermek, bir dispatcher/yönetici kararı — henüz kimseye atanmamış bir işi rastgele bir Member'ın kendine "alması" mantıklı değil.
- `Reassign` (bugün): Admin **veya** o işin **şu anki sahibi**. "Ben bu işi yapamayacağım, devret" — bu, işin sahibinin kendi kararı olabilir.

Bu ayrım bilerek yapıldı ve `ValidateIsAdmin` hâlâ ayrı bir metot olarak duruyor (artık tek çağrı yeri olsa da) — çünkü "Assign Admin-only'dir" gerçek, adı olan bir iş kuralı, sadece `Assign`'ın içine gömülecek bir detay değil.

---

## 3. Testte gerçek bir varsayım hatası

Yeni testleri eklerken, **var olan** bir test kırıldı: `Reassign_ByMember_ReturnsForbidden`, `employeeId=2`'yi (seed edilen Org1 Member) "herhangi bir Member" örneği olarak kullanıyordu. Ama `employeeId=2`, aynı zamanda test senaryosundaki iş emrinin **atandığı kişiydi** — bugünden itibaren bu, **geçerli bir aktör**. Test artık `200` dönüyordu (doğru davranış), ama testin adı/amacı "reddedilmeli" diyordu.

**Düzeltme:** Testi `Reassign_ByUnrelatedMember_ReturnsForbidden` olarak yeniden adlandırdım, gerçekten **ilgisiz** (ne Admin ne atanan) yeni bir çalışan oluşturup onunla test ettim. Ayrıca asıl yeni özelliği kanıtlayan `Reassign_ByCurrentAssignee_Succeeds` testini ekledim.

---

## 4. Canlı kanıt

```
1) Admin olusturur + Org1 Member'a (id=2) atar
2) Yeni bir calisan olusturulur (id=6)
3) Atanan calisan (id=2, Admin DEGIL) kendi isini id=6'ya devrediyor -> 200
4) Farkli bir ise atanmis olmayan/ilgisiz bir calisan (id=2, ama BU iste atanan degil) devretmeye calisiyor -> 403
```

---

## 5. Otomatik testler + Red→Green

Kanıt için `Role != Admin` kontrolünü geçici olarak "sadece Admin" haline geri döndürüp sahiplik kısmını devre dışı bıraktım:
```
dotnet test --filter Reassign_ByCurrentAssignee_Succeeds
→ BAŞARISIZ (403 yanıtının JSON olmayan gövdesini parse etmeye çalışırken hata — Day 36'da gördüğümüz aynı desen)
```
Geri getirdim → `dotnet test FieldOps.slnx` → **32/32 başarılı**.

---

## 6. Regresyon

```
dotnet test FieldOps.slnx    → 32/32
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## 7. Bağımsız görev sonrası düzeltme — anlamsız bir "yeniden atama"

Bağımsız görevde iki şey sordum. Biri ("bir Member kendisinin olmayan bir işi başkasına atayabilir mi") zaten kapalıydı — `ValidateIsAdminOrAssignee`'nin `workOrder.AssignedEmployeeId != actingEmployeeId` satırı ve `Reassign_ByUnrelatedMember_ReturnsForbidden` testi bunu zaten kanıtlıyordu. Ama diğeri gerçek bir boşluktu: **kod, yeni atanacak çalışanın zaten atanmış olan kişiyle aynı olup olmadığını hiç kontrol etmiyordu.**

**Düzeltme (`WorkOrderAssignmentService.ReassignWorkOrder`):**
```csharp
if (newEmployeeId == workOrder.AssignedEmployeeId)
{
    return WorkOrderAssignmentResult.Failure($"Work order {workOrderId} is already assigned to employee {newEmployeeId}.");
}
```

**Canlı kanıt:**
```
POST /api/workorders/1/reassign, employeeId: 2 (zaten atanan kişi)
→ 400 "Work order 1 is already assigned to employee 2."
```

Yeni test: `Reassign_ToSameEmployeeAlreadyAssigned_ReturnsBadRequest`. `dotnet test FieldOps.slnx` → 33/33.

---

## Bugünün bilinçli sınırı

Devredilecek yeni çalışana herhangi bir onay/bildirim akışı yok. "Unassign" (tamamen iptal) hâlâ yok. Dosya kanıtı ve müşteri onayı haftanın geri kalanına bırakıldı.
