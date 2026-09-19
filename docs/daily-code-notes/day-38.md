# Day 38 — Kod Notları

Faz 3, Hafta 8, Gün 38. Konu: **gerçek bir yatay yetki yükselmesi (horizontal privilege escalation) açığının kapatılması** — Day 37'nin RBAC kuralı "doğru" ama eksikti. Bu, roadmap'in Week 8 için istediği "cross-tenant attack scenario" maddesini kapatıyor.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. Canlı kanıt — önce açığın kendisi (kod yazılmadan önce)

Planı sunmadan önce, dün yazdığımız `EmployeesController.Create` kodunu tekrar okurken şunu fark ettim: `actingEmployee.Role == Admin` kontrol ediliyordu, ama **hangi organizasyon için** Admin olduğu hiç kontrol edilmiyordu. Canlı test ettim:

```
Org1'in Admin'i (id=1), X-Organization-Id: 2 (kendi organizasyonu değil!) göndererek:
POST /api/employees, X-Organization-Id: 2, X-Employee-Id: 1
→ 201 Created, organizationId: 2   ← olmaması gereken bir şey oldu

GET /api/employees, X-Organization-Id: 2
→ Org2'nin listesinde gerçekten göründü
```

**Bunun adı:** yatay yetki yükselmesi — doğru rol (Admin), ama yanlış kapsam (başka bir tenant). Day 35'in tenant isolation'ı ve Day 37'nin RBAC'ı ayrı ayrı doğru kurulmuştu, ama **birbirine hiç bağlanmamıştı**.

---

## 2. `EmployeesController.Create` — asıl düzeltme

```csharp
if (actingEmployee.Role != EmployeeRole.Admin)
{
    return StatusCode(StatusCodes.Status403Forbidden, "Only an Admin can create employees.");
}

// Day 38: Day 37's Admin check alone was not enough...
if (actingEmployee.OrganizationId != organizationId)
{
    return StatusCode(StatusCodes.Status403Forbidden, "An Admin can only create employees within their own organization.");
}
```

**Neden bu sırada (rol kontrolü → sonra organizasyon eşleşmesi → sonra organizasyon var mı):** Day 37'nin Q1'inde konuştuğumuz aynı prensip: yetki kontrolleri, kaynağın var olup olmadığını kontrol etmeden önce yapılır. Burada da: "bu kişi doğru tenant için mi Admin" sorusu, "hedef organizasyon gerçekten var mı" sorusundan önce sorulmalı — yoksa yetkisiz/yanlış-kapsamlı biri bile organizasyon ID'lerinin var olup olmadığını yoklayabilir.

---

## 3. Beklenmedik bir yan etki — mevcut bir testin anlamı değişti

Yeni kontrolü ekledikten sonra `dotnet test` çalıştırdığımda, **hiç dokunmadığım** bir test kırmızıya düştü:

```
Create_NonExistentOrganization_ReturnsBadRequest
Expected: BadRequest
Actual:   Forbidden
```

**Neden:** Bu test, `X-Organization-Id: 999` + `X-Employee-Id: 1` (Org1 Admin, kendi organizasyonu 1) gönderiyordu. Yeni kontrolümüz `actingEmployee.OrganizationId (1) != organizationId (999)` olduğunu görüp **daha organizasyonun var olup olmadığına hiç bakmadan** `403` döndürüyor artık. Yani: **artık hiçbir gerçek Admin, "kendi organizasyonu" 999 olmadığı sürece, 999'un var olup olmadığını test eden bu koda hiç ulaşamıyor.**

Bu aslında iyi bir şey (güvenlik açısından doğru davranış) ama şunu ortaya çıkardı: `EmployeeApplicationService`'in "organizasyon var mı" iş kuralı, artık HTTP seviyesinde **sadece** bir senaryoda tetiklenebilir — **kendi `OrganizationId`'si zaten geçersiz olan bir çalışan** (yani ADR 0002'nin daha Day 33'te işaretlediği, hiç çözülmemiş referans bütünlüğü boşluğu: bir organizasyon silinirse `Employee.OrganizationId`'nin geçersiz kalması).

**Çözüm — bunu gerçek bir senaryo olarak seed'e eklemek:**
```csharp
// "Orphaned Admin" — kendi OrganizationId'si (999) hiçbir gerçek organizasyona karşılık gelmiyor
new Employee("Orphaned Admin", organizationId: 999, EmployeeRole.Admin) { Id = 5 }
```
Test artık `X-Employee-Id: 5` kullanıyor — bu "sahipsiz" Admin'in kendi organizasyonu zaten 999 olduğu için organizasyon-eşleşme kontrolünü geçiyor, ama `EmployeeApplicationService`'in organizasyon-var-mı kontrolü gerçekten tetikleniyor ve `400` dönüyor.

---

## 4. Yeni testler

```csharp
[Fact]
public async Task Create_ByMember_ReturnsForbidden()   // Day 37'nin temel kuralı, ilk kez otomatik teste bağlandı

[Fact]
public async Task Create_ByAdminFromAnotherOrganization_ReturnsForbidden()   // bugünkü asıl saldırı senaryosu
```

`Create_ByAdminFromAnotherOrganization_ReturnsForbidden`, canlı kanıtladığımız saldırıyı birebir kodluyor: Org1 Admin (id=1), `X-Organization-Id: 2` ile deniyor, `403` bekliyor.

---

## 5. Canlı Red → Green kanıtı

Yeni organizasyon-eşleşme kontrolünü yorum satırına aldım:
```
dotnet test --filter Create_ByAdminFromAnotherOrganization_ReturnsForbidden
→ BAŞARISIZ (Expected: Forbidden, Actual: Created)
```
Kontrolü geri getirdim:
```
dotnet test FieldOps.slnx
→ 9/9 başarılı
```

Ayrıca gerçek uygulamayı tekrar çalıştırıp **aynı saldırıyı** tekrar denedim:
```
POST /api/employees, X-Organization-Id: 2, X-Employee-Id: 1 (Org1 Admin)
→ 403 "An Admin can only create employees within their own organization."

GET /api/employees, X-Organization-Id: 2
→ Org2'nin listesi temiz, enjekte edilen çalışan yok

Meşru senaryo (Org2 Admin, kendi organizasyonunda oluşturuyor) → hâlâ 201
```

---

## 6. Regresyon

```
dotnet test FieldOps.slnx    → 9/9
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## Bugünün dersi

İki güvenlik kontrolünü (Day 35'in tenant isolation'ı, Day 37'nin RBAC'ı) **ayrı ayrı doğru** kurmak, onların **birlikte** doğru çalıştığı anlamına gelmiyor. Aradaki bağlantıyı (bir Admin'in "hangi tenant için" Admin olduğu) kurmadan, ikisi de "doğru" görünen kod, gerçek bir açık bırakabiliyordu. Bununla Week 8'in roadmap'teki tüm maddeleri (tenant isolation, membership, RBAC, authorization tests, cross-tenant attack scenarios) kapanmış oldu.
