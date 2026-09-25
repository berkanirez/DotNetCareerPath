# Day 61 — Kod Notları

Faz 3, Hafta 12, Gün 61 — **Week 12'nin (Phase 3'ün son haftasının) ilk günü**. Konu: **entegrasyon/yetkilendirme test kapsamı denetimi**. Yeni bir özellik (AI provider abstraction) eklemeden önce, mevcut temelin gerçekten sağlam olduğunu **doğrulamak**.

---

## 1. Gerçek problem

47 günden fazla süredir FieldOps'a kod eklerken, yetkilendirme testleri genelde **canlı bulunan** açıklardan sonra eklendi (Day 38, 39, 45 gibi gerçek güvenlik açıkları). Hiçbir zaman sistematik bir "her action'ın her kontrolü test edilmiş mi?" denetimi yapılmadı. Week 12'nin roadmap listesi bunu **AI çalışmasından önce** sıralıyor — bilinçli bir sıra.

---

## 2. Denetim yöntemi — varsaymadan, gerçekten karşılaştırarak

`grep` ile testlerin tam listesini çıkardım (37 test, `WorkOrdersAuthorizationIntegrationTests.cs`'de), sonra `WorkOrdersController`'ın her action'ının **kod içindeki** kontrollerini tek tek okuyup, her birine karşılık gelen bir test olup olmadığını kontrol ettim. İki gerçek boşluk buldum:

**Boşluk 1 — `Create`:** `ValidateMembership`'i çağırıyor (aynı `GetAll`'un çağırdığı metot), ama `GetAll`'un bu kontroller için **kendi özel testleri** varken (`GetAll_NoOrganizationHeader_ReturnsBadRequest`, `GetAll_ByEmployeeFromAnotherOrganization_ReturnsForbidden`), `Create`'in hiç yoktu.

**Boşluk 2 — `Approve`:** Eksik `X-Customer-Id` header'ı için açık kod (`if (actingCustomerId is null) return BadRequest(...)`) var, ama bunu kanıtlayan hiç test yoktu.

---

## 3. Eklenen üç test

```csharp
[Fact]
public async Task Create_NoOrganizationHeader_ReturnsBadRequest()
{
    var client = _factory.CreateClient();
    var response = await client.PostAsJsonAsync("/api/workorders", new { Title = "Should-Never-Be-Created" });
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task Create_ByEmployeeFromAnotherOrganization_ReturnsForbidden()
{
    var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Add("X-Organization-Id", "2");
    client.DefaultRequestHeaders.Add("X-Employee-Id", "1"); // seeded Org1 Admin, claiming Org 2's header
    var response = await client.PostAsJsonAsync("/api/workorders", new { Title = "Should-Never-Be-Created" });
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```
İkincisi, Day 35'in klasik tenant-izolasyon senaryosunun `Create`'e özel hâli: Org 1'in gerçek bir çalışanı, `X-Organization-Id: 2` header'ıyla "ben Org 2'deymişim gibi davran" diyor — `ValidateMembership`'in `actingEmployee.OrganizationId != organizationId` kontrolü bunu yakalamalı.

```csharp
[Fact]
public async Task Approve_NoCustomerHeader_ReturnsBadRequest()
{
    // ... tam bir yasam dongusu (Completed'a kadar) ...
    var noHeaderClient = _factory.CreateClient();
    noHeaderClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
    // X-Customer-Id deliberately omitted.
    var approveResponse = await noHeaderClient.PostAsync($"/api/workorders/{completed.Id}/approve", null);
    Assert.Equal(HttpStatusCode.BadRequest, approveResponse.StatusCode);
}
```

---

## 4. Canlı Red→Green kanıtı — her üçü için

**`Create`'in membership kontrolü geçici olarak yorum satırına alındı:**
```
Create_NoOrganizationHeader_ReturnsBadRequest       -> FAIL (Expected: BadRequest, Actual: InternalServerError)
Create_ByEmployeeFromAnotherOrganization_ReturnsForbidden -> FAIL (Expected: Forbidden, Actual: Created)
```
**İkinci satır özellikle çarpıcı bir kanıt:** kontrol devre dışıyken, Org 1'in bir çalışanı **gerçekten** Org 2 adına bir iş emri oluşturabildi (`201 Created`) — tam bir çapraz-tenant güvenlik açığının canlı, kontrollü bir tekrarı. Kontrol geri getirilince ikisi de yeşile döndü.

**`Approve`'un müşteri header kontrolü geçici olarak yorum satırına alındı:**
```
Approve_NoCustomerHeader_ReturnsBadRequest -> FAIL (Expected: BadRequest, Actual: InternalServerError)
```
Geri getirilince yeşile döndü.

---

## 5. Demo basitleştirmesi vs. üretim gereksinimi

Bugün **bilinçli olarak** dokunulmayan, bilinen boşluklar:
- `/report` uç noktası (Day 48) — otomatik test yok, Redis'in CI'da güvenilir olmaması yüzünden.
- Idempotency (Day 54) ve rate limiting (Day 53) — otomatik eşik testi yok, aynı sınıf bilinçli sınırlamalar.

Bunlar bugünkü denetimde **"gerçek boşluk" değil, "dokümante edilmiş, kabul edilmiş sınır"** olarak işaretlendi — Create/Approve'daki boşluklardan farklı olarak, bunların NEDEN test edilmediği zaten geçmiş günlerde açıkça yazılmıştı.

---

## Regresyon (Day 61'in kendi kapsamı)

```
dotnet test FieldOps.slnx    → 52/52 (49 -> 52, 3 yeni test)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## Day 62 — Denetimi tamamlamak: kalan 7 action

Day 61'in bağımsız görevinde şüphelenilen şey doğru çıktı: `Assign`, `Start`, `Complete`, `Reassign`, `Unassign`, `Reopen`, `AddEvidence` — hepsi `ValidateMembership`'i çağırıyor, ama **hiçbirinin** kendi "header yok" testi yoktu (sadece `GetAll` ve `Create`'de vardı).

**Eklenen 7 test** (hepsi aynı desen): `<Action>_NoOrganizationHeader_ReturnsBadRequest`.

**Önemli bir tasarım kararı — sadece "header yok" testi, "başka organizasyondan çalışan" testi değil:** `GetAll`/`Create` zaten çapraz-organizasyon mekanizmasının **kendisinin** çalıştığını kanıtladı — bu, tüm 9 action'da birebir aynı kod. Asıl kanıtlanmamış risk, "bu action'ın kodu hâlâ bu kontrolü çağırıyor mu" (bir refactor sırasında biri yanlışlıkla silerse) — bunu her action için ayrı kanıtlamak değerli, ama mekanizmayı 7 kere daha kanıtlamak (Day 39/44'ün "gerçekten aynıysa tekrar test etme" dersi) gereksiz tekrar olurdu.

**Body gerektiren action'lar için ince bir detay:** `Assign`/`Reassign`/`AddEvidence` bir `[FromBody]` bekliyor. Boş bir body gönderseydik, `[ApiController]`'ın **kendi otomatik model doğrulaması**, `ValidateMembership`'e hiç ulaşmadan `400` dönebilirdi — bu, testin **yanlış şeyi** kanıtlamasına (asıl kontrolümüzü değil, alakasız bir model hatasını) yol açardı. Çözüm: bu üç action için **geçerli, düzgün bir body** gönderip sadece header'ları atlamak — böylece dönen her `400`, kesinlikle `ValidateMembership`'ten geliyor.

```csharp
// Body gerektiren (Assign/Reassign/AddEvidence) ornek:
var response = await client.PostAsJsonAsync("/api/workorders/999999/assign", new { EmployeeId = 1 });

// Body gerektirmeyen (Start/Complete/Unassign/Reopen) ornek:
var response = await client.PostAsync("/api/workorders/999999/start", null);
```

**`999999` gibi var olmayan bir ID kullanılabilmesinin sebebi:** `ValidateMembership`, route'taki `id`'ye **hiç bakmadan önce** çalışıyor — yani bu testler için gerçek bir iş emri oluşturmaya bile gerek yok.

**Canlı Red→Green kanıtı — sadece bir tanesi (`Assign`), diğer 6'sını temsilen:**
```
Assign'in ValidateMembership cagrisi gecici olarak yorum satirina alindi:
Assign_NoOrganizationHeader_ReturnsBadRequest -> FAIL (Expected: BadRequest, Actual: InternalServerError)
Geri getirilince yesile dondu.
```
Diğer 6 action **birebir aynı** `ValidateMembership` mekanizmasını kullandığı için, bu tek kanıt hepsine genelliyor — 7 kez tekrarlamak (Day 61 Q2'nin "gerçek denetim = matris, ama gereksiz tekrar da değil" dengesi) gereksiz olurdu.

## Regresyon (Day 62 dahil, toplam)

```
dotnet test FieldOps.slnx    → 59/59 (52 -> 59, 7 yeni test)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

**Denetim artık tamamlandı:** 9 action'ın (`GetAll`, `Create`, `Assign`, `Start`, `Complete`, `Reassign`, `Unassign`, `Reopen`, `AddEvidence`) hepsi kendi "header yok" testine sahip; `Approve`'un kendi (`X-Customer-Id`) testi de var. `/report`, idempotency, rate limiting hâlâ bilinçli olarak otomatik testsiz.
