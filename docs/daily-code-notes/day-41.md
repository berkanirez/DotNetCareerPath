# Day 41 — Kod Notları

Faz 3, Hafta 9, Gün 41. Konu: **iş emri atama (assignment)** ve ilk gerçek **durum geçişi**: `Open → Assigned`.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. `WorkOrderStatus` — yeni değer

```csharp
public enum WorkOrderStatus { Open, Assigned }
```

---

## 2. `WorkOrder` / `WorkOrderSummary` — `AssignedEmployeeId`

```csharp
public int? AssignedEmployeeId { get; set; }   // nullable — atanmamışsa null
```

---

## 3. `IWorkOrderDirectory.Assign` — modülün kendi kuralı, host'un kuralı ayrı

```csharp
WorkOrderSummary? Assign(int workOrderId, int employeeId);
```

`InMemoryWorkOrderDirectory`:
```csharp
public WorkOrderSummary? Assign(int workOrderId, int employeeId)
{
    var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
    if (workOrder is null || workOrder.Status != WorkOrderStatus.Open)
    {
        return null;
    }
    workOrder.Status = WorkOrderStatus.Assigned;
    workOrder.AssignedEmployeeId = employeeId;
    return ToSummary(workOrder);
}
```

**Önemli tasarım ayrımı:** "Sadece `Open` olan bir iş emri atanabilir" kuralı, **modülün kendi içinde** kontrol ediliyor — çünkü bu, tamamen `WorkOrder`'ın kendi durumuyla ilgili bir kural, başka hiçbir modülün bilgisine ihtiyaç duymuyor. Ama "atanan çalışan gerçekten var mı ve doğru organizasyondan mı" kontrolü **host'ta** (çünkü `IEmployeeDirectory`'ye ihtiyaç var, ve `WorkOrders` modülünün `Employees`'e hiç referansı yok — ADR 0002). Bu, "hangi kural nereye ait" sorusunun somut bir örneği: **tek-modüllü bir durum makinesi kuralı** modülün kendisine, **çapraz-modül bir gerçek** host'a.

---

## 4. `WorkOrderAssignmentResult` + `WorkOrderAssignmentService` — Day 34'ün aynı gerekçesi

```csharp
public WorkOrderAssignmentResult AssignWorkOrder(int workOrderId, int employeeId, int callerOrganizationId)
{
    var workOrder = _workOrderDirectory.GetById(workOrderId);

    if (workOrder is null || workOrder.OrganizationId != callerOrganizationId)
    {
        return WorkOrderAssignmentResult.Failure($"Work order {workOrderId} does not exist.");
    }

    var employee = _employeeDirectory.GetById(employeeId);
    if (employee is null) { return WorkOrderAssignmentResult.Failure($"Employee {employeeId} does not exist."); }

    if (employee.OrganizationId != workOrder.OrganizationId)
    {
        return WorkOrderAssignmentResult.Failure($"Employee {employeeId} is not part of this organization.");
    }

    if (workOrder.Status != WorkOrderStatus.Open)
    {
        return WorkOrderAssignmentResult.Failure($"Work order {workOrderId} is not open for assignment.");
    }

    var assigned = _workOrderDirectory.Assign(workOrderId, employeeId);
    return WorkOrderAssignmentResult.Success(assigned!);
}
```

**Bu servisin var olma sebebi, Day 34'ün `EmployeeApplicationService`'iyle birebir aynı:** hem çapraz-modül koordinasyon (`IWorkOrderDirectory` + `IEmployeeDirectory`) hem de gerçek bir iş kuralı (atanan çalışan doğru organizasyondan olmalı). Mekanik bir katman değil.

**Day 37/38'in dersini bu sefer baştan uygulama:** `workOrder is null || workOrder.OrganizationId != callerOrganizationId` satırına dikkat — iş emri **hiç yoksa** ile **var ama başka bir organizasyona aitse**, **aynı** "does not exist" mesajını döndürüyoruz. Bu, Day 37/38'de canlı bir açık olarak keşfettiğimiz "yetkisiz birine varlık bilgisi sızdırma" dersinin, bu sefer **hiç açık oluşturmadan** baştan uygulanmış hali.

---

## 5. `WorkOrdersController.Assign` — Admin-only

```csharp
[HttpPost("{id}/assign")]
public ActionResult<WorkOrderDto> Assign(int id, AssignWorkOrderRequest request, ...)
{
    var membershipError = ValidateMembership(organizationId, actingEmployeeId);
    if (membershipError is not null) { return membershipError; }

    var actingEmployee = _employeeDirectory.GetById(actingEmployeeId!.Value)!;
    if (actingEmployee.Role != EmployeeRole.Admin)
    {
        return StatusCode(StatusCodes.Status403Forbidden, "Only an Admin can assign work orders.");
    }

    var result = _workOrderAssignmentService.AssignWorkOrder(id, request.EmployeeId, organizationId!.Value);
    if (!result.Succeeded) { return BadRequest(result.Error); }

    return Ok(ToDto(result.WorkOrder!));
}
```

**Neden `GetAll`'daki gibi "bu ürün kararı, tartışmalı" değil de doğrudan Admin-only:** `GetAll`'da "bir Member kendi organizasyonunun listesini görebilir mi" sorusu gerçekten belirsizdi (Day 39). Ama "herhangi bir Member, herhangi bir iş emrini herhangi birine atayabilsin mi" sorusu o kadar belirsiz değil — bu bir **durum değiştiren** işlem, StockPilot Day 25'in Admin/Employee ayrımına çok benzer bir gerekçeyle Admin'e kilitlendi.

**Küçük, bilinçli bir verimsizlik:** `ValidateMembership` zaten isteği yapan çalışanı bir kez `GetById` ile buluyor, ama bunu dışarıya vermiyor. Rol kontrolü için `Assign` içinde **ikinci bir** `GetById` çağrısı yapıyoruz. Bunu düzeltmek (mesela `ValidateMembership`'in çalışanı da döndürmesi) sadece **tek bir** çağrı yeri için `ValidateMembership`'in imzasını karmaşıklaştırırdı — bellek içi bir liste için bu maliyet önemsiz, ama gerçek bir veritabanı geldiğinde tekrar gözden geçirilmeye değer.

---

## 6. Canlı kanıt ve Red→Green — bir test hatası da dahil

Beş senaryo canlı test edildi (meşru atama, tekrar atama denemesi, çapraz-organizasyon atama denemesi, Member'ın atama denemesi, çapraz-organizasyon iş emrine atama denemesi) — hepsi doğru sonuç verdi.

**Red→Green'de gerçek bir bulgu:** `Assign_WorkOrderFromAnotherOrganization_ReturnsBadRequest` testini ilk yazdığımda, atanacak çalışan olarak `employeeId=4` (Org2 Member) kullanmıştım. Organizasyon-eşleşme kontrolünü devre dışı bırakıp test ettiğimde, **test yine de yeşil çıktı** — çünkü `employee.OrganizationId (2) != workOrder.OrganizationId (1)` kontrolü zaten devrede kalıp aynı `400`'ü başka bir sebepten üretiyordu. Testi, atanacak çalışanı Org1'in **kendi** Member'ı (`employeeId=2`) olacak şekilde değiştirdim — bu sefer kontrolü kaldırınca test gerçekten kırmızıya düştü (`Expected: BadRequest, Actual: OK`). Bu, Day 38/39'da gördüğümüz "bir kontrolün başka bir kontrolle yanlışlıkla aynı sonucu üretmesi" tuzağının bir başka örneği — bir testin gerçekten izole bir şeyi kanıtladığından emin olmanın, sadece "yeşil çıktı" demekten daha fazlasını gerektirdiğini gösteriyor.

```
dotnet test FieldOps.slnx → 20/20 (15 eski + 5 yeni)
```

---

## 7. Regresyon

```
dotnet test FieldOps.slnx    → 20/20
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## Bugünün bilinçli sınırı

Geri alma (unassign), yeniden atama, `InProgress`/`Completed` geçişleri bugünün kapsamı dışında — Week 9'un ilerleyen günlerine bırakıldı.
