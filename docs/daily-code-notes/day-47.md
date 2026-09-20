# Day 47 — Kod Notları

Faz 3, Hafta 9, Gün 47. Konu: **müşteri onayı (customer approval)** — Week 9'un roadmap'te kalan son konusu, ve **dördüncü bir yetkilendirme aktörü türü**.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. Yeni modül — `FieldOps.Modules.Customers`

`Organizations`'ın (Day 32) **birebir aynı deseni**: `Customer` (internal), `CustomerSummary` (public), `ICustomerDirectory` (`GetAll`, `GetById`), `InMemoryCustomerDirectory` (her organizasyon için 1 seed müşteri: `Id=1` → Org1, `Id=2` → Org2), `CustomersModule.AddCustomersModule()`.

**Bilinçli sınırlama:** Bugün bir `Create` uç noktası yok — müşteriler sadece seed veride var. Yeni müşteri ekleme akışı, ileriki bir günün konusu.

---

## 2. `WorkOrder` — `CustomerId` ve `CustomerApproved`

```csharp
public int? CustomerId { get; set; }      // hangi musteriye ait (opsiyonel)
public bool CustomerApproved { get; set; }
```

`CustomerId`, tıpkı `OrganizationId`/`AssignedEmployeeId` gibi **düz bir `int`** — `Customer` tipine referans değil (ADR 0002'nin aynı ilkesi, `WorkOrders` modülünün `Customers`'a hiç referansı yok).

---

## 3. `IWorkOrderDirectory.Approve` — modülün kendi kuralı

```csharp
public WorkOrderSummary? Approve(int workOrderId)
{
    var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
    if (workOrder is null || workOrder.Status != WorkOrderStatus.Completed) { return null; }
    workOrder.CustomerApproved = true;
    return ToSummary(workOrder);
}
```

Diğer tüm mutasyonlarla aynı desen: modül sadece **kendi durum kuralını** koruyor (`Completed` olmalı), "isteği yapan gerçekten bu işin müşterisi mi" kontrolü host'ta.

---

## 4. `WorkOrdersController.Approve` — dördüncü aktör türü

```csharp
[HttpPost("{id}/approve")]
public ActionResult<WorkOrderDto> Approve(int id, [FromHeader] organizationId, [FromHeader(Name = "X-Customer-Id")] actingCustomerId)
{
    // header kontrolleri...
    var actingCustomer = _customerDirectory.GetById(actingCustomerId.Value);
    if (actingCustomer is null || actingCustomer.OrganizationId != organizationId) { return BadRequest(...); }

    var workOrder = _workOrderDirectory.GetById(id);
    if (workOrder is null || workOrder.OrganizationId != organizationId) { return BadRequest(...); }

    if (workOrder.CustomerId != actingCustomerId)
    {
        return StatusCode(403, "Only this work order's own customer can approve it.");
    }

    var updated = _workOrderDirectory.Approve(id);
    ...
}
```

**Bugüne kadarki dört yetkilendirme türü:**
1. **Day 35** — Tenant üyeliği ("hangi organizasyondansın")
2. **Day 37** — Rol ("hangi roldesin")
3. **Day 42** — Sahiplik ("bu kayıtla ilişkin misin" — ama hep bir **Employee** için)
4. **Day 47 (bugün)** — **Employee bile olmayan** bir aktör türü: `Customer`. "Bu spesifik iş emrinin müşterisi sen misin" kontrolü, Day 42'nin sahiplik mantığıyla **aynı şekil**de ama tamamen farklı bir kimlik sınıfı için.

`X-Customer-Id`, `X-Employee-Id`'yle (Day 35) aynı sınıftan bilinçli bir basitleştirme — gerçek bir müşteri portalı/kimlik doğrulaması yok.

---

## 5. Canlı kanıt — tam senaryo

```
1) Musteriye baglanmis (customerId=1) bir is olusturulur, atanir, baslatilir, tamamlanir
2) Baglanan musteri (id=1) onayliyor              → 200, customerApproved: true
3) Org2'nin musterisi (id=2), Org1 header'iyla    → 400 "Customer 2 does not exist."
```

---

## 6. Otomatik testler + Red→Green

4 yeni test: doğru müşteri onaylıyor (başarılı), başka organizasyonun müşterisi deniyor (`400`), müşterisi hiç bağlanmamış bir işe onay denemesi (`403`), henüz `Completed` olmamış (ama doğru müşteriye bağlı) bir işe onay denemesi (`400`, izole edilmiş — Day 41'in aynı disiplini: doğru aktörü kullan, sadece test edilen kontrol başarısız olsun).

Kanıt için "bu işin müşterisi mi" kontrolünü yorum satırına aldım:
```
dotnet test --filter Approve_OnWorkOrderWithNoLinkedCustomer_ReturnsForbidden
→ BAŞARISIZ (Expected: Forbidden, Actual: OK)
```
Geri getirdim → `dotnet test FieldOps.slnx` → **49/49 başarılı** (45 eski + 4 yeni).

---

## 7. Regresyon

```
dotnet test FieldOps.slnx    → 49/49
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## Bugünün dersi ve Week 9'un kapanışı

Bununla **Week 9'un roadmap'teki tüm konuları** (lifecycle, assignment, status transitions, file evidence, customer approval) gerçek, canlı kanıtlanmış kodla kapsanmış oldu. `Customers` modülü, `Organizations`'ın kurduğu desenin **beşinci kez** doğrulanması — mimari desenin gerçekten genellenebilir olduğunun bir kez daha kanıtı.
