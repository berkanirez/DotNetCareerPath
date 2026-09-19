# Day 45 — Kod Notları

Faz 3, Hafta 9, Gün 45. Konu: **atamayı tamamen geri alma (unassignment)** — `Assign`/`Reassign` üçgeninin tamamlanması.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. `IWorkOrderDirectory.Unassign` — en basit mutasyon

```csharp
public WorkOrderSummary? Unassign(int workOrderId)
{
    var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
    if (workOrder is null || (workOrder.Status != WorkOrderStatus.Assigned && workOrder.Status != WorkOrderStatus.InProgress))
    {
        return null;
    }
    workOrder.Status = WorkOrderStatus.Open;
    workOrder.AssignedEmployeeId = null;
    return ToSummary(workOrder);
}
```

**Neden dört mutasyonun en basiti:** `Assign`/`Reassign`, atanacak **yeni bir çalışan** hakkında bilgi alıyordu — bu, çapraz-modül bir gerçekti (`IEmployeeDirectory`'ye sormak gerekiyordu), bu yüzden `WorkOrderAssignmentService` gerekiyordu. `Unassign`'de doğrulanacak **hiçbir yeni çalışan yok** — sadece mevcut atamayı temizliyoruz. Hiç çapraz-modül bilgiye ihtiyaç olmadığı için, controller doğrudan `IWorkOrderDirectory`'yi çağırıyor, servise hiç uğramıyor.

---

## 2. `WorkOrdersController.Unassign` — hiç yeni kod tekrarı yok

```csharp
[HttpPost("{id}/unassign")]
public ActionResult<WorkOrderDto> Unassign(int id, ...)
{
    var membershipError = ValidateMembership(organizationId, actingEmployeeId);
    if (membershipError is not null) return membershipError;

    var authError = ValidateIsAdminOrAssignee(id, organizationId, actingEmployeeId, "unassign");
    if (authError is not null) return authError;

    var updated = _workOrderDirectory.Unassign(id);
    if (updated is null) { return BadRequest(...); }

    return Ok(ToDto(updated));
}
```

**Dikkat çeken şey:** `ValidateIsAdminOrAssignee`, Day 44'te `Reassign` için yazılmıştı — bugün **hiç değiştirmeden, aynen** tekrar kullandık. Yeni bir kod tekrarı yok, yeni bir soyutlama kararı da gerekmedi — çünkü ihtiyaç duyduğumuz şey zaten tam olarak dünkü şeydi.

---

## 3. Canlı kanıt — bu sefer çökme yok

Day 43'te, servis katmanındaki bir kontrolü kaldırdığımızda `!` operatörü yüzünden gerçek bir `500` almıştık. Bugün aynı deneyi (`Unassign`'in durum kontrolünü kaldırma) yaptığımda, sonuç **temiz bir yanlış cevaptı** (`200` yerine beklenen `400`), çökme değil:

```
dotnet test --filter Unassign_OnOpenWorkOrder_ReturnsBadRequest
→ BAŞARISIZ (Expected: BadRequest, Actual: OK)   ← 500 DEĞİL
```

**Neden fark var:** `WorkOrdersController.Unassign`, `_workOrderDirectory.Unassign(id)`'in sonucunu `!` ile "kesinlikle var" varsaymıyor — `if (updated is null) { return BadRequest(...); }` diye **gerçekten kontrol ediyor**. Day 43'ün dersi (bir katmanın kontrolünü diğerinin güvenlik ağı sayma) burada, doğru yazılmış null-kontrolü sayesinde, sadece "yanlış sonuç" olarak kaldı, "çökme" olmadı.

---

## 4. Canlı kanıt — tam senaryo

```
1) Admin olusturur + Org1 Member'a (id=2) atar
2) Atanan calisan (id=2, Admin degil) kendi atamasini iptal ediyor -> 200, Open, assignedEmployeeId: null
3) Zaten Open olan ise tekrar unassign denemesi -> 400
```

---

## 5. Otomatik testler + Red→Green

5 yeni test: Admin iptal ediyor, atanan kişi kendi işini iptal ediyor, ilgisiz Member deniyor (`403`), `Open` durumunda deneme (`400`), `Completed` durumunda deneme (`400`).

```
dotnet test FieldOps.slnx → 38/38 (33 eski + 5 yeni)
```

---

## 6. Regresyon

```
dotnet test FieldOps.slnx    → 38/38
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## 7. Bağımsız görev sonrası ek özellik — `Reopen`, ve içinde bulduğum gerçek bir açık

Bağımsız görevde "Completed bir işi yeniden açabilmeli miyiz" sorusuna "evet, olmalı" dediğimde, bunu ekledim:

```csharp
// IWorkOrderDirectory
public WorkOrderSummary? Reopen(int workOrderId)
{
    var workOrder = _workOrders.FirstOrDefault(w => w.Id == workOrderId);
    if (workOrder is null || workOrder.Status != WorkOrderStatus.Completed) { return null; }
    workOrder.Status = WorkOrderStatus.InProgress;   // Open/Assigned degil — atanan kisi hala kayitli
    return ToSummary(workOrder);
}
```

`Reopen`'i **bilerek Admin-only** yaptım (`Reassign`/`Unassign`'in Admin-veya-sahip kuralı değil) — bir işi "tamamlandı" damgasını geri almak, atanan kişinin kendi kendine yapabileceği bir şey değil, daha yüksek riskli bir düzeltme.

**Kodu yazarken kendim bir açık buldum (canlı doğrulanmış):** İlk versiyonda `Reopen` action'ı sadece `ValidateIsAdmin`'i çağırıyordu — bu metot **sadece isteği yapan çalışanın rolüne bakıyor, iş emrinin hangi organizasyona ait olduğunu hiç kontrol etmiyor**. Canlı test ettim:

```
Org1'in Admin'i (id=1), Org2'nin tamamlanmış işini (id=1, X-Organization-Id: 1 diyerek) yeniden açmaya çalışıyor
→ 200!  organizationId: 2  ← Org1'in hiç ilgisi olmayan bir organizasyonun verisini değiştirdi
```

Bu, Day 38'in tam olarak kapattığı türden bir açık — bugün, aynı hatayı ben kendim, yeni kod yazarken yeniden yarattım. `Assign` bu hatayı yapmıyor çünkü `WorkOrderAssignmentService`'in `ValidateWorkOrderAndEmployee`'si organizasyon kontrolünü zaten yapıyor; ama `Reopen` hiçbir servise uğramadan doğrudan modülü çağırıyor, o kontrol hiçbir yerde yoktu.

**Düzeltme:** Yeni bir `ValidateIsAdminForWorkOrder` metodu — `ValidateIsAdminOrAssignee`'ye çok benziyor (iş emrini çeker, organizasyon eşleşmesini kontrol eder) ama sahiplik `OR` dalı yok, çünkü `Reopen` gerçekten sadece Admin'e özel. İkisini birleştirmedim çünkü gerçekten aynı değiller (Day 39/40'ın "sadece gerçekten aynıysa birleştir" kuralı).

**Red→Green:** Organizasyon kontrolünü yorum satırına aldım → `Reopen_ByAdminFromAnotherOrganization_ReturnsBadRequest` testi gerçekten kırmızıya düştü → geri getirdim → `42/42` yeşil.

**Canlı yeniden doğrulama:** Aynı saldırı tekrar denendi → artık `400 "Work order 1 does not exist."`; meşru senaryo (Org2 Admin'i kendi işini açıyor) → hâlâ `200`.

---

## Bugünün dersi ve durum

`Assign` → `Reassign` → `Unassign` üçgeni tam, artık `Reopen` (Admin-only) de eklendi — `Completed` artık kalıcı bir çıkmaz sokak değil. Ayrıca bugün, yeni kod yazarken **kendi kendime** gerçek bir çapraz-organizasyon açığı yaratıp aynı gün içinde yakalayıp kapattık — bu haftanın "her yeni action'da tenant izolasyonunu unutma" dersinin, tecrübeli olsak bile hâlâ dikkat gerektirdiğinin somut kanıtı. Week 9'un geriye kalan roadmap konuları: dosya kanıtı (file evidence), müşteri onayı (customer approval).
