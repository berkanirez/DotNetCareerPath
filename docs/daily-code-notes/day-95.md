# Day 95 — Kod Notları

Faz 5, Hafta 18, Gün 95. Konu: **Role-aware screens — role'e göre arayüz**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Dünden hatırlarsan:** Gün 93'te backend, giriş yapan çalışanın rolünü (`Admin`/`Member`) token'ın İÇİNE koyuyordu, ama hiçbir yer bunu OKUMUYORDU. Bugün bu bilgiyi GERÇEKTEN kullanmaya başlıyoruz: SADECE `Admin` rolündeki kullanıcılar "+ New Work Order" linkini görecek.

**ÇOK ÖNEMLİ, açıkça söylüyorum — bu GÜVENLİK DEĞİL:** Bugün yaptığımız şey SADECE bir arayüz (UI) kolaylığı. Bir `Member`, tarayıcının geliştirici araçlarını (F12) kullanıp linki manuel olarak görünür yapabilir, VE YİNE DE `POST /api/workorders`'a istek atabilir — çünkü backend, Day 93'ten beri `[Authorize]` ile HİÇBİR endpoint'i korumuyor. Gerçek güvenlik HER ZAMAN backend'de olmalı; bugünkü iş sadece normal bir kullanıcının gereksiz bir buton görmesini engelliyor, kötü niyetli birini durdurmuyor.

---

## 1. `auth.service.ts` — role'ü de saklamak

```typescript
private readonly role = signal<string | null>(null);
// role -- token (yukarıdaki satırda tanımlı) ile AYNI mantıkla, SADECE
// bellekte tutulan, "Admin" ya da "Member" gibi bir METİN tutan signal.
// Başlangıç değeri null -- henüz giriş yapılmamışken rol de BİLİNMİYOR.

login(employeeId: number): Observable<LoginResponse> {
// Bu metodun GÖVDESİ aynı kaldı, SADECE .pipe(tap(...)) içine bir satır
// daha eklendi (aşağıda).
  return this.http.post<LoginResponse>(`${this.apiBaseUrl}/login`, { employeeId }).pipe(
    tap(response => {
    // tap içindeki fonksiyon artık İKİ iş yapıyor, tek satır değil, bu
    // yüzden { } ile bir BLOK hâline getirildi.
      this.token.set(response.token);
      // Dünkü GİBİ, token'ı kaydediyoruz.
      this.role.set(response.role);
      // YENİ satır: backend'den gelen GERÇEK role bilgisini (response.role,
      // Day 93'ün LoginResponse'undaki Role alanı) role signal'ine yazıyoruz.
    }),
  );
}

isAdmin(): boolean {
// isAdmin -- dışarıdan (component'lerden) "bu kullanıcı Admin mi" diye
// sorulduğunda cevap veren, YENİ bir yardımcı metot.
  return this.role() === 'Admin';
  // role()'ün GERÇEK değeri, TAM OLARAK "Admin" metnine eşitse true döner.
  // (Backend'deki EmployeeRole.Admin, .ToString() ile "Admin" metnine
  // çevrilip token'a/LoginResponse'a GİRİYOR -- Day 93'ten hatırlarsan.)
}
```

---

## 2. `work-order-list.ts` — `AuthService`'i içeri almak

```typescript
constructor(
  private readonly workOrderService: WorkOrderService,
  protected readonly authService: AuthService,
  // DİKKAT: "private" DEĞİL, "protected" yazıldı -- çünkü bu sefer
  // work-order-list.html (component'in ŞABLONU) authService'e DOĞRUDAN
  // erişmek zorunda (aşağıdaki @if bloğunda). "private" olsaydı, şablon
  // buna erişemezdi -- sadece component'in KENDİ TypeScript kodu erişebilirdi.
) {}
```

---

## 3. `work-order-list.html` — `@if` ile role kontrolü

```html
@if (authService.isAdmin()) {
  <a routerLink="/work-orders/new">+ New Work Order</a>
}
```
`authService.isAdmin()` — component'teki `protected readonly authService` sayesinde, şablon DOĞRUDAN `authService`'in metotlarını çağırabiliyor. `@if (...)` — Day 91'den tanıdık, parantez içindeki ifade `true` ise İÇERİDEKİ HTML ekrana BASILIYOR, `false` ise HİÇ basılmıyor (gizlemek DEĞİL, DOM'a hiç EKLENMİYOR).

---

## Regresyon (Day 95)

```
npx ng build               -> Başarılı, 288.52 kB
npx ng test --watch=false  -> 27/27 geçti (8 test dosyası, 4 yeni test)
dotnet build FieldOps.slnx -> 0 Hata, 0 Uyarı (backend'e hiç dokunulmadı)
```

## Canlı doğrulama

```
curl -X POST .../api/auth/login -d '{"employeeId":1}' -> "role":"Admin"   (Org1 Admin, gerçek çalışan)
curl -X POST .../api/auth/login -d '{"employeeId":2}' -> "role":"Member"  (Org1 Member, gerçek çalışan)
```
İki GERÇEK, farklı rollü çalışan için backend'in doğru rolü döndüğü doğrulandı. Angular tarafının bu role'e göre linki gösterip gizlediği ise `work-order-list.spec.ts`'in yeni testleriyle (sahte `AuthService`, gerçek DOM sorgusu `a[href="/work-orders/new"]`) doğrulandı.

## Demo basitleştirmesi vs. üretim gereksinimi

* **En büyük, açıkça söylenen basitleştirme:** Bu bir GÜVENLİK mekanizması DEĞİL, sadece bir UI kolaylığı. Backend hâlâ rol kontrolü yapmıyor.
* Role bilgisi SADECE bellekte — token gibi, sayfa yenilenince kayboluyor.
