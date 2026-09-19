# Day 43 — Kod Notları

Faz 3, Hafta 9, Gün 43. Konu: **yeniden atama (reassignment)** — Day 42'nin bağımsız görevinde kod okuyarak bulduğumuz gerçek boşluğun (bir iş emri `Open` olmadan asla yeniden atanamıyordu) kapatılması.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. `IWorkOrderDirectory.Reassign` — `Assign`'dan farkı

```csharp
public WorkOrderSummary? Reassign(int workOrderId, int newEmployeeId)
{
    var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
    if (workOrder is null || (workOrder.Status != WorkOrderStatus.Assigned && workOrder.Status != WorkOrderStatus.InProgress))
    {
        return null;
    }
    workOrder.AssignedEmployeeId = newEmployeeId;
    return ToSummary(workOrder);   // Status'a hiç dokunulmuyor
}
```

`Assign`, `Open → Assigned` durum değişikliği **yapıyordu**. `Reassign` ise **durumu hiç değiştirmiyor** — sadece kimin sorumlu olduğunu değiştiriyor. Bir iş `InProgress` iken yeniden atansa bile, hâlâ `InProgress` kalıyor (yeni kişi kaldığı yerden devam ediyormuş gibi).

---

## 2. `WorkOrderAssignmentService` — ortak doğrulamanın çıkarılması

`AssignWorkOrder` ve `ReassignWorkOrder`, "iş emri var mı ve doğru organizasyona mı ait, atanacak çalışan var mı ve doğru organizasyondan mı" kontrollerini **birebir aynı** şekilde yapıyordu — sadece durum ön koşulu (`Open` vs `Assigned`/`InProgress`) ve son çağrı (`Assign` vs `Reassign`) farklıydı. Bu sefer bunu ortak bir `ValidateWorkOrderAndEmployee` metoduna çıkardık:

```csharp
public WorkOrderAssignmentResult AssignWorkOrder(int workOrderId, int employeeId, int callerOrganizationId)
{
    var validationError = ValidateWorkOrderAndEmployee(workOrderId, employeeId, callerOrganizationId, out var workOrder);
    if (validationError is not null) return validationError;

    if (workOrder!.Status != WorkOrderStatus.Open) { ... }
    ...
}

public WorkOrderAssignmentResult ReassignWorkOrder(int workOrderId, int newEmployeeId, int callerOrganizationId)
{
    var validationError = ValidateWorkOrderAndEmployee(workOrderId, newEmployeeId, callerOrganizationId, out var workOrder);
    if (validationError is not null) return validationError;

    if (workOrder!.Status != WorkOrderStatus.Assigned && workOrder.Status != WorkOrderStatus.InProgress) { ... }
    ...
}
```

**Day 39'daki kararla tezat:** `EmployeesController.GetAll`/`Create`'i **bilerek** ayrı bıraktık, çünkü ikisi tam aynı değildi (`Create`'in ekstra bir rol kontrolü vardı). Burada ise ikisi **gerçekten birebir aynı** — bu yüzden hemen çıkardık, üçüncü bir tekrarı beklemeden (Day 40'ın `WorkOrdersController.ValidateMembership` kararıyla aynı mantık).

---

## 3. Canlı bir kanıt sırasında **gerçek bir çökme** bulundu

Red→Green kanıtı için `ReassignWorkOrder`'daki durum kontrolünü yorum satırına aldığımda, beklediğim gibi test kırmızıya düştü ama **beklemediğim bir şekilde**:

```
Expected: BadRequest
Actual:   InternalServerError    ← 500!
```

**Neden:** `WorkOrderAssignmentService`'in kendi kontrolünü kaldırınca, akış doğrudan `_workOrderDirectory.Reassign(...)`'a gidiyor. Ama `InMemoryWorkOrderDirectory.Reassign`'ın **kendi** durum kontrolü hâlâ var — `Open` bir iş emri için `null` döndürüyor. Servis kodu ise `WorkOrderAssignmentResult.Success(reassigned!)` satırında `!` (null-forgiving operatörü) kullanarak "bu kesinlikle null değil" diyordu — ama **gerçekten null'dı**. Bu, controller'ın `ToDto(result.WorkOrder!)` satırına kadar ilerleyip orada gerçek bir `NullReferenceException` fırlatmasına, yani `500`'e sebep oldu.

**Ders:** Modülün kendi durum makinesi kuralı (Day 41/42'den beri kurulu "modül kendi invaryantını korur" ilkesi) bir güvenlik ağı gibi görünüyordu, ama servis katmanındaki `!` operatörleri bu ağın varlığını **hesaba katmıyordu** — servis, modülün `null` dönebileceğini unutup "Success" varsayıyordu. İki katmanın da doğru davranması, aralarındaki **arayüzün varsayımlarının** da doğru kurulmuş olmasını gerektiriyor. (Bu, kontrolü tekrar ekleyerek zaten çözülmüş durumda — sadece canlı olarak neyin kırılabileceğini gördük.)

---

## 4. `WorkOrdersController` — üçüncü bir tekrar da çıkarıldı

`Assign` ve `Reassign`, "isteği yapan Admin mi" kontrolünü de birebir aynı şekilde yapıyordu. Bunu da `ValidateIsAdmin(actingEmployeeId, action)` diye ortak bir metoda çıkardık — Day 40'ın "iki çağrı yeri gerçekten aynıysa hemen çıkar" kararının bir tekrarı.

---

## 5. Canlı kanıt — tam senaryo

```
1) Admin olusturur + Org1 Member'a (id=2) atar        → Assigned
2) Yeni bir Org1 calisani olusturulur (id=6)
3) Admin yeniden atar (hala Assigned)                  → 200, Status hala Assigned, AssignedEmployeeId=6
4) Open bir ise yeniden atama denemesi                 → 400
5) Member yeniden atamaya calisir                      → 403
```

---

## 6. Otomatik testler + Red→Green

6 yeni test: `Assigned` iken yeniden atama (durum değişmiyor), `InProgress` iken yeniden atama (durum değişmiyor), `Open` iken deneme (`400`), `Completed` iken deneme (`400`), Member'ın denemesi (`403`), başka organizasyondan çalışana atama denemesi (`400`).

```
dotnet test FieldOps.slnx → 31/31 (25 eski + 6 yeni)
```

---

## 7. Regresyon

```
dotnet test FieldOps.slnx    → 31/31
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## Bugünün bilinçli sınırı

"Unassign" (bir işi tekrar `Open`'a döndürme) bugünün kapsamı dışında — sadece bir çalışandan diğerine devir var, tamamen atamayı iptal etme yok.
