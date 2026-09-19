# Day 39 — Kod Notları

Faz 3, Hafta 8, Gün 39. Konu: `GetAll`'daki **kimliksiz sızıntının** kapatılması — Day 38'in bağımsız görevinde bulunan, canlı doğrulanmış açık. Bu, Week 8'in roadmap'teki son maddesini de kapatıyor.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. Dünkü bulgunun hatırlatması

Day 38'in bağımsız görevinde, `GetAll`'da hiçbir `X-Employee-Id` göndermeden, sadece `X-Organization-Id` ile istediğimiz organizasyonun tüm çalışan listesini çekebildiğimizi canlı kanıtlamıştık:
```
GET /api/employees, X-Organization-Id: 2   (hiçbir kimlik iddiası yok)
→ 200, Org2'nin tüm çalışanları
```
Bu, `Create`'in Day 38'de kapattığı açıktan bile daha temel: orada en azından **sahte de olsa bir kimlik** gerekiyordu, burada hiç gerekmiyordu.

---

## 2. `EmployeesController.GetAll` — asıl düzeltme

```csharp
[HttpGet]
public ActionResult<IReadOnlyList<EmployeeDto>> GetAll(
    [FromHeader(Name = "X-Organization-Id")] int? organizationId,
    [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId)
{
    if (organizationId is null) { return BadRequest("X-Organization-Id header is required."); }
    if (actingEmployeeId is null) { return BadRequest("X-Employee-Id header is required."); }

    var actingEmployee = _employeeDirectory.GetById(actingEmployeeId.Value);
    if (actingEmployee is null) { return BadRequest($"Employee {actingEmployeeId} does not exist."); }

    if (actingEmployee.OrganizationId != organizationId)
    {
        return StatusCode(StatusCodes.Status403Forbidden, "You can only view employees within your own organization.");
    }

    var employees = _employeeDirectory.GetAll()
        .Where(e => e.OrganizationId == organizationId)
        .Select(e => new EmployeeDto(e.Id, e.Name, e.OrganizationId, e.Role))
        .ToList();

    return Ok(employees);
}
```

**Bilinçli olarak eklemediğimiz şey:** Rol kontrolü (Admin mi, Member mi). Day 37'nin bağımsız görevinde "bu bir ürün kararı, teknik değil" diye açık bıraktığımız soruyu bugün de açık bırakıyoruz. Bugünün kapsamı sadece: **hiç kimlik olmaması** ve **yanlış organizasyondan bir kimlik olması** açığını kapatmak. Aynı organizasyondan hem Admin hem Member, kendi organizasyonlarının listesini görebiliyor — bu, bilinçli, dokümante edilmiş bir sınır, unutkanlık değil.

---

## 3. Neden bir yardımcı metoda çıkarmadık (kasıtlı bir tasarım kararı)

`Create` ve `GetAll` artık neredeyse birebir aynı üç kontrolü tekrarlıyor: header yok → 400, çalışan yok → 400, organizasyon eşleşmiyor → 403. Bunu ortak bir özel metoda çıkarmayı düşünebiliriz, ama **bilerek yapmadık**:

- Sadece **iki** çağrı noktası var (`Create`, `GetAll`) — "rule of three" (üçüncü tekrarda soyutla) prensibine göre, ikinci tekrar henüz bir soyutlamayı haklı çıkarmaz.
- İkisi zaten tam aynı değil: `Create`'in ek bir rol kontrolü (`Role != Admin`) var, `GetAll`'ın yok. Şimdi bir soyutlama yaparsak, ya bu farkı da parametreleştirmemiz gerekir (karmaşıklaşır) ya da yarım bir soyutlama oluruz.
- CLAUDE.md'nin "gerçek bir sorun olmadan soyutlama yaratma" kuralı burada da geçerli — üç satırlık bir tekrar, üçüncü bir çağrı noktası gelene kadar bir soyutlamadan daha ucuz.

**Sonuç:** Tekrar bilerek bırakıldı, gelecekte üçüncü bir yer (mesela `Update`/`Delete` eklenirse) bu deseni tekrar ederse, o zaman çıkarılması değerlendirilecek.

---

## 4. Yeni testler

```csharp
[Fact]
public async Task GetAll_NoEmployeeHeader_ReturnsBadRequest()   // dünkü orijinal açığın kendisi

[Fact]
public async Task GetAll_ByEmployeeFromAnotherOrganization_ReturnsForbidden()   // yanlış organizasyondan görüntüleme denemesi
```

Mevcut `GetAll_ScopedToOrganization_NeverReturnsAnotherOrganizationsEmployees` testi de, `org2Client`'e `X-Employee-Id: 3` (seed edilen Org2 Admin'i) eklenerek güncellendi — artık yeni zorunlu header olmadan bu test de kırmızıya düşerdi.

---

## 5. Canlı Red → Green kanıtı

Yeni organizasyon-eşleşme kontrolünü yorum satırına aldım:
```
dotnet test --filter GetAll_ByEmployeeFromAnotherOrganization_ReturnsForbidden
→ BAŞARISIZ (Expected: Forbidden, Actual: OK)
```
Geri getirdim:
```
dotnet test FieldOps.slnx → 11/11 başarılı
```

Gerçek uygulamayı tekrar çalıştırıp üç senaryoyu canlı doğruladım:
```
Hiç X-Employee-Id yok, X-Organization-Id: 2        → 400 (dünkü açık artık kapalı)
Org1 Admin'i (id=1), Org2'nin listesini istiyor     → 403 (rol Admin olsa da fark etmiyor, yanlış tenant)
Org2 Member'i (id=4), kendi organizasyonunu görüyor → 200 (meşru senaryo hâlâ çalışıyor)
```

---

## 6. Regresyon

```
dotnet test FieldOps.slnx    → 11/11
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## Bugünün dersi ve Week 8'in kapanışı

`Create` ve `GetAll` artık simetrik bir üyelik kontrolüne sahip: ikisi de "gerçek bir kimlik" ve "doğru tenant" istiyor. Bununla roadmap'in Week 8 için istediği tüm maddeler (multi-tenancy, tenant identification, tenant isolation, membership, granular RBAC, authorization tests, cross-tenant attack scenarios) gerçek, canlı kanıtlanmış kod ve testlerle kapanmış oldu — üçü de (Day 35, 38, 39) kurgulanmış senaryolar değil, gerçekten yazılan koddan çıkan, gerçekten bulunan açıklardı.
