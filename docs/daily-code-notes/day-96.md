# Day 96 — Kod Notları

Faz 5, Hafta 18, Gün 96. Konu: **Work Order Dashboard — Hafta 18'in SON konusu**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Dünden hatırlarsan:** Liste ekranı TEK TEK iş emirlerini gösteriyor. Ama bazen "toplamda kaç tane AÇIK iş emrim var" gibi bir ÖZET soruya cevap gerekir — bunun için TÜM listeyi okuyup kendin saymak zorunda kalmak istemezsin.

**Bugün ne kuruyoruz:** Backend'de ZATEN var olan (Day 48/49'dan beri çalışan, Redis'te cache'lenen) `GET /api/workorders/report` endpoint'ini Angular'dan İLK KEZ çağırıp, dört sayıyı (Open/Assigned/InProgress/Completed) basit bir ekranda göstermek. **Backend'de HİÇBİR değişiklik yok** — bu endpoint zaten vardı, sadece Angular tarafı bugüne kadar hiç kullanmamıştı.

**Bugün Hafta 18 bitiyor:** Roadmap'in bu haftaki 8 konusu (components, services, routing, reactive forms, HTTP client, JWT interceptor, role-aware screens, work-order dashboard) bugünle TAMAMLANMIŞ oluyor.

---

## 1. `work-order-status-report.ts` (yeni) — backend'in cevabının şekli

```typescript
export interface WorkOrderStatusReport {
// interface -- Day 90'daki WorkOrder gibi, sadece derleme zamanında tip
// kontrolü için var olan, çalışma zamanında İZ BIRAKMAYAN bir şekil tanımı.
  organizationId: number;
  // Hangi organizasyona ait olduğu bilgisi.
  open: number;
  // "Open" durumundaki iş emri SAYISI (tek bir iş emri DEĞİL, bir SAYI).
  assigned: number;
  inProgress: number;
  // Backend'deki "InProgress" (C#'ın PascalCase'i), JSON'a camelCase olarak
  // (System.Text.Json'ın varsayılanı) "inProgress" diye geliyor.
  completed: number;
}
```

---

## 2. `work-order.service.ts` — `getReport()` eklendi

```typescript
getReport(): Observable<WorkOrderStatusReport> {
// getReport -- dışarıdan (WorkOrderDashboard'dan) çağrılacak, rapor
// isteğini atan YENİ metot.
  return this.http.get<WorkOrderStatusReport>(`${this.apiBaseUrl}/report`, { headers: this.demoHeaders });
  // .get<WorkOrderStatusReport>(url, options) -- dün/önceki günlerdeki
  // .get çağrılarıyla AYNI desen, SADECE adres farklı
  // ("http://localhost:5138/api/workorders/report") ve dönen TİP farklı
  // (bir dizi DEĞİL, TEK bir rapor nesnesi).
}
```

---

## 3. `work-order-dashboard.ts` (yeni) — raporu çekip saklamak

```typescript
import { Component, OnInit, signal } from '@angular/core';
// Component/OnInit/signal -- önceki günlerden tanıdık, bugün yeni bir şey
// YOK.
import { WorkOrderService } from '../work-order.service';
import { WorkOrderStatusReport } from '../work-order-status-report';

@Component({
  imports: [],
  // Bu component'in şablonunda başka bir component/direktif KULLANILMIYOR
  // (routerLink bile yok), bu yüzden dizi BOŞ.
  selector: 'app-work-order-dashboard',
  styleUrl: './work-order-dashboard.css',
  templateUrl: './work-order-dashboard.html',
})
export class WorkOrderDashboard implements OnInit {
  protected readonly report = signal<WorkOrderStatusReport | null>(null);
  // report -- raporu tutan signal. Başlangıç değeri null -- rapor HENÜZ
  // backend'den gelmedi demek.

  constructor(private readonly workOrderService: WorkOrderService) {}
  // Dünden/önceki günlerden tanıdık DI deseni.

  ngOnInit(): void {
  // ngOnInit -- component ekrana YERLEŞTİKTEN hemen sonra çalışan, "veri
  // çekmeye BAŞLA" dediğimiz lifecycle metodu (Day 90'dan beri tanıdık).
    this.workOrderService.getReport().subscribe(report => {
    // GERÇEK isteği atıyoruz.
      this.report.set(report);
      // Backend'den GERÇEK cevap geldiğinde, report signal'ine YAZIYORUZ --
      // bu, Day 91'in zoneless dersinden beri bildiğimiz, ekranı GERÇEKTEN
      // güncelleyen TEK doğru yol.
    });
  }
}
```

---

## 4. `work-order-dashboard.html` — raporu ekrana basmak

```html
@if (report(); as r) {
<!-- report() -- signal'i ÇAĞIRARAK okuyoruz. "as r" -- Angular'ın kendi
     şablon söz dizimindeki bir kısayol: "eğer report() GERÇEKTEN null
     DEĞİLSE, onun değerini 'r' adında KISA bir değişkene ata, aşağıda
     r.open/r.assigned diye tekrar tekrar signal'i çağırmak ZORUNDA kalma." -->
  <ul>
    <li>Open: {{ r.open }}</li>
    <li>Assigned: {{ r.assigned }}</li>
    <li>In Progress: {{ r.inProgress }}</li>
    <li>Completed: {{ r.completed }}</li>
  </ul>
} @else {
<!-- @else -- report() HÂLÂ null İSE (yani cevap henüz gelmediyse) bu kısım
     çalışır. -->
  <p>Loading...</p>
  <!-- Kullanıcıya "bir şeyler yükleniyor" diye basit bir geri bildirim --
       Day 90'dan beri ilk kez bir "loading" durumu GÖSTERİYORUZ (önceki
       günlerde boş bir tablo sadece bir an için görünüyordu, hiç
       belirtilmiyordu). -->
}
```

---

## 5. `app.routes.ts` — `/dashboard` route'u

```typescript
{ path: 'dashboard', component: WorkOrderDashboard },
// Basit, parametresiz bir route -- 'work-orders/new' gibi bir sıralama
// sorunu YOK, çünkü hiçbir ":id" deseniyle ÇAKIŞMIYOR.
```

---

## Regresyon (Day 96)

```
npx ng build               -> Başarılı, 290.03 kB
npx ng test --watch=false  -> 30/30 geçti (9 test dosyası, 3 yeni test)
dotnet build FieldOps.slnx -> 0 Hata, 0 Uyarı (backend'e hiç dokunulmadı)
```

## Canlı doğrulama — çapraz kontrol

```
curl .../api/workorders/report -> {"organizationId":1,"open":10,"assigned":0,"inProgress":0,"completed":6}
curl .../api/workorders (ham liste) içindeki status sayıları: 10x status=0 (Open), 6x status=3 (Completed)
```
Rapor endpoint'inin döndüğü sayılar, GERÇEK liste verisinin elle sayılmasıyla BİREBİR eşleşti — rapor'a körü körüne güvenmek yerine, bağımsız bir yoldan (ham listeyi sayarak) çapraz kontrol edildi.

## Demo basitleştirmesi vs. üretim gereksinimi

* Hiç grafik/görsel yok, sadece düz sayılar — gerçek bir dashboard'da genelde bar/pie chart kullanılırdı.
* Sayılar otomatik YENİLENMİYOR — sayfayı manuel yenilemek (F5) gerekiyor, periyodik otomatik yenileme (örn. her 10 saniyede bir) yok.
