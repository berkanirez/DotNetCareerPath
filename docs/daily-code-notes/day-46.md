# Day 46 — Kod Notları

Faz 3, Hafta 9, Gün 46. Konu: **dosya kanıtı (file evidence)** — Week 9'un roadmap'te kalan iki konusundan ilki.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. `WorkOrder`/`WorkOrderSummary` — `EvidenceNotes`

```csharp
// WorkOrder.cs
public List<string> EvidenceNotes { get; } = new();
```

**Bilinçli basitleştirme:** Gerçek bir dosya/fotoğraf yükleme-depolama altyapısı (S3, Azure Blob, disk) henüz yok — bu, roadmap'in Week 10-11'inin konusu (Docker, altyapı). Bugün "kanıt" sadece bir **metin notu** — gerçek bir şeyin yarım bırakılmış hali değil, açıkça işaretlenmiş bir demo basitleştirmesi.

---

## 2. `IWorkOrderDirectory.AddEvidence` — tek kural: `Open` olmasın

```csharp
public WorkOrderSummary? AddEvidence(int workOrderId, string note)
{
    var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
    if (workOrder is null || workOrder.Status == WorkOrderStatus.Open)
    {
        return null;
    }
    workOrder.EvidenceNotes.Add(note);
    return ToSummary(workOrder);
}
```

Diğer mutasyonların aksine (her biri **tam olarak bir** önceki durum istiyordu), bu sadece "**`Open` olmasın**" diyor — `Assigned`, `InProgress`, `Completed` hepsi geçerli, çünkü kanıt eklemek bu üç durumun herhangi birinde mantıklı.

---

## 3. `WorkOrdersController.AddEvidence` — hiç yeni kod tekrarı yok (yine)

```csharp
[HttpPost("{id}/evidence")]
public ActionResult<WorkOrderDto> AddEvidence(int id, AddEvidenceRequest request, ...)
{
    var membershipError = ValidateMembership(organizationId, actingEmployeeId);
    if (membershipError is not null) return membershipError;

    var ownershipError = ValidateOwnership(id, organizationId, actingEmployeeId, out _);
    if (ownershipError is not null) return ownershipError;

    var updated = _workOrderDirectory.AddEvidence(id, request.Note);
    if (updated is null) { return BadRequest(...); }

    return Ok(ToDto(updated));
}
```

**Neden `ValidateOwnership` (Day 42), `ValidateIsAdminOrAssignee` (Day 44) değil:** Kanıt ekleyen kişi, **işi yapan** kişi olmalı — Admin'in kendisi kanıt "uydurabilmemeli". Bu, tam olarak `Start`/`Complete`'in mantığı (sadece sahiplik, rol alternatifi yok), `Reassign`/`Unassign`'in mantığı değil (Admin VEYA sahip).

---

## 4. Beklenen ama önceden tahmin edilen bir sonuç

`AddEvidence_OnUnassignedWorkOrder_ReturnsForbidden` testini yazarken, ismini **baştan** `ReturnsForbidden` koydum (`ReturnsBadRequest` değil) — çünkü Day 42'nin `Start_OnUnassignedWorkOrder_ReturnsForbidden`'ından tanıdık bir desen: `AssignedEmployeeId` `null` iken, sahiplik kontrolü **her zaman** önce başarısız olur, "yanlış durum" kontrolüne (`Open` reddi) hiç sıra gelmez. Test çalıştırıldığında tahmin doğru çıktı — bu, geçen haftaların derslerinin artık **içselleştirildiğinin** bir göstergesi.

---

## 5. Canlı kanıt

```
Atanan calisan (id=2) kanit ekliyor           → 200, evidenceNotes: ["Fixed the leak, photo taken (simulated)."]
Admin (atanan degil) kanit eklemeye calisiyor → 403 "Only the assigned employee can act on this work order."
```

---

## 6. Otomatik testler + Red→Green

3 yeni test: atanan çalışan ekliyor (başarılı), Admin (atanan değilse) deniyor (`403`), atanmamış bir işe deneme (`403`, tahmin edildiği gibi).

Kanıt için modülün asıl mutasyonunu (`EvidenceNotes.Add(note)`) yorum satırına aldım:
```
dotnet test --filter AddEvidence_ByAssignee_Succeeds
→ BAŞARISIZ (Collection: [] içinde not bulunamadı)
```
Geri getirdim → `dotnet test FieldOps.slnx` → **45/45 başarılı** (42 eski + 3 yeni).

---

## 7. Regresyon

```
dotnet test FieldOps.slnx    → 45/45
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## Bugünün bilinçli sınırı

Gerçek dosya/fotoğraf yükleme yok — sadece metin. Kanıt silme/düzenleme yok, kaç kanıt eklenebileceğine sınır yok. Week 9'un son roadmap konusu: müşteri onayı (customer approval).
