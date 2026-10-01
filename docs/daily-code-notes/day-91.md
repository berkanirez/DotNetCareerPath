# Day 91 — Kod Notları

Faz 5, Hafta 18, Gün 91. Konu: **Angular Routing**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Dünden hatırlarsan:** Tek bir ekranımız vardı — iş emri listesi. Gerçek bir uygulamada onlarca ekran olur: liste, detay, yeni kayıt formu, vs. Kullanıcı bunlar arasında adres çubuğundaki URL'i değiştirerek gezinir — tıpkı `localhost:4200/Skills/Edit/1` gibi bir MVC URL'inin farklı bir sayfa açması gibi. Fark şu: MVC'de her tıklamada sunucuya yeni bir istek gider, sayfa baştan yüklenir. Angular'da (ve genel olarak "SPA" — Single Page Application dediğimiz yapılarda) sayfa HİÇ yeniden yüklenmiyor, sadece ekranın İÇERİĞİ JavaScript tarafından değiştiriliyor — bu yüzden geçişler anlık, yanıp sönme yok.

**Bugün ne kuruyoruz:** İki URL — `/` (liste) ve `/work-orders/28` gibi bir detay sayfası. Listedeki bir satıra tıklayınca, sayfa yenilenmeden detay ekranına geçilecek.

**Küçük bir basitleştirme, dürüstçe:** Backend'de tek bir iş emri döndüren bir endpoint (`GET /api/workorders/28` gibi) henüz yok — bunu eklemek bugünün konusunun (routing) dışına çıkardı. Bunun yerine `getById`, zaten var olan `GET /api/workorders`'ı çağırıp, gelen listenin içinden doğru `id`'yi istemci tarafında (tarayıcıda) arıyor. Çalışıyor ama verimsiz (tek bir kayıt için TÜM liste çekiliyor) — gerçek çözüm backend'e özel bir endpoint eklemek olurdu, bugünlük bilerek ertelendi.

---

## 1. `app.routes.ts` (yeni) — hangi URL, hangi component

```typescript
import { Routes } from '@angular/router';
import { WorkOrderList } from './work-order-list/work-order-list';
import { WorkOrderDetail } from './work-order-detail/work-order-detail';

export const routes: Routes = [
  { path: '', component: WorkOrderList },           // localhost:4200/
  { path: 'work-orders/:id', component: WorkOrderDetail },  // localhost:4200/work-orders/28
];
// :id -- URL'in İÇİNE gömülü, değişken bir parça (route parametresi).
// "28" yerine herhangi bir sayı/metin gelebilir, component bunu KENDİSİ okuyacak.
```

---

## 2. `app.config.ts` — router'ı uygulamaya tanıtmak

```typescript
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(),
    provideRouter(routes),   // "bu route tanımlarını kullanarak URL'leri eşleştir" diye Angular'a söylüyor
  ]
};
```

---

## 3. `app.html`/`app.ts` — `<router-outlet />`, aktif component'in yerleştiği yer

```typescript
@Component({
  imports: [RouterOutlet],   // şablon içinde <router-outlet /> kullanabilmek için
  ...
})
export class App {}
```
```html
<main>
  <h1>FieldOps</h1>
  <router-outlet />
</main>
```
`<router-outlet />` — boş bir ÇERÇEVE, "şu anki URL'e göre hangi component eşleşiyorsa, onu TAM OLARAK buraya koy" diyor. Dün `app.html` doğrudan `<app-work-order-list />` yazıyordu (SABİT); bugün bu satır kalktı, çünkü artık HANGİ component'in görüneceğine router karar veriyor, biz değil.

---

## 4. `work-order.service.ts` — `getById` eklendi

```typescript
getById(id: number): Observable<WorkOrder | undefined> {
  return this.getAll().pipe(map(workOrders => workOrders.find(w => w.id === id)));
}
// .pipe(map(...)) -- bir Observable'ın SONUCUNU, geldiği anda dönüştürmek
// için kullanılan RxJS operatörü. Burada: "tüm listeyi al, içinden SADECE
// doğru id'ye sahip olanı bul." .NET'teki LINQ'nun .Select()'ine YAKIN bir
// fikir ama Observable (zamanla gelen tek bir sonuç) üzerinde çalışıyor.
```

---

## 5. `work-order-detail.ts` (yeni) — URL'den `id`'yi okuyup veriyi çekmek

```typescript
@Component({
  imports: [RouterLink],     // "Back to list" linki için
  ...
})
export class WorkOrderDetail implements OnInit {
  protected workOrder: WorkOrder | undefined;
  protected notFound = false;

  constructor(
    private readonly route: ActivatedRoute,     // şu anki URL hakkında bilgi veren Angular servisi
    private readonly workOrderService: WorkOrderService,
  ) {}

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    // route.snapshot.paramMap.get('id') -- URL'deki ":id" yerine GERÇEKTE ne
    // yazıldıysa onu (bir string olarak) okur. "28" yazıldıysa "28" döner,
    // Number(...) ile sayıya çeviriyoruz.
    this.workOrderService.getById(id).subscribe(workOrder => {
      this.workOrder = workOrder;
      this.notFound = workOrder === undefined;
    });
  }
}
```

```html
@if (workOrder) {
  <h2>Work Order #{{ workOrder.id }}</h2>
  <p><strong>Title:</strong> {{ workOrder.title }}</p>
  <p><strong>Status:</strong> {{ statusLabels[workOrder.status] }}</p>
} @else if (notFound) {
  <p>No work order found with that id.</p>
}
<a routerLink="/">Back to list</a>
```
`@if`/`@else if` — Razor'daki `@if`/`@else`'in birebir karşılığı, modern Angular'ın yerleşik koşul sözdizimi (eski `*ngIf` directive'inin yerini aldı, `@for` gibi).

---

## 6. `work-order-list.html` — satırlar artık tıklanabilir

```html
<td><a [routerLink]="['/work-orders', workOrder.id]">{{ workOrder.title }}</a></td>
```
`[routerLink]="['/work-orders', workOrder.id]"` — normal bir `<a href="...">` KULLANMIYORUZ bilerek: `href` kullansaydık, tarayıcı sayfayı BAŞTAN yüklerdi (tam olarak kaçınmaya çalıştığımız şey). `routerLink`, Angular'a "bu linke tıklanınca, sayfayı yeniden yüklemeden, router'ın kendi iç mekanizmasıyla git" diyor. Dizi şeklinde yazılması (`['/work-orders', workOrder.id]`), parçaların (`/work-orders` + gerçek id) birleştirilip `/work-orders/28` gibi bir URL oluşturacağı anlamına geliyor.

---

## Regresyon (Day 91)

```
npx ng build               -> Başarılı, 232.28 kB (dün 132.29 kB -- Router eklenince büyüdü)
npx ng test --watch=false  -> önce 11/11, signal düzeltmesinden SONRA 12/12 (yeni regresyon testi dahil)
dotnet build FieldOps.slnx -> 0 Hata, 0 Uyarı (backend'e hiç dokunulmadı)
```
`dotnet test` bugün YENİDEN çalıştırılmadı — backend'de TEK satır bile değişmedi (bugünkü iş tamamen Angular tarafında), ve dünün 65/65'lik sonucu zaten geçerliliğini koruyor. Sadece `dotnet build`'in temiz olduğu doğrulandı.

## Canlı uçtan uca doğrulama

Her iki uygulama (API port 5138, Angular port 4200) birlikte çalışırken:
```
curl http://localhost:4200/                 -> 200
curl http://localhost:4200/work-orders/28   -> 200  (DOĞRUDAN bu URL'e gidildiğinde bile
                                                       404 DEĞİL -- Angular dev server'ın
                                                       "deep link" desteği doğru çalışıyor)
curl .../main.js içinde "No work order found", "Back to list", "router-outlet" bulundu
```
**Dürüst sınırlama (Day 89-90'da da belirtilen):** Gerçek tıklama/navigasyon davranışı (listedeki bir satıra tıklayıp URL'in GERÇEKTEN değişmesi) yine tarayıcı olmadan gözlemlenemedi — ama `/work-orders/28`'e DOĞRUDAN gidildiğinde sayfanın 404 vermemesi, route tanımının GERÇEKTEN doğru kurulduğunun güçlü bir kanıtı.

**Temizlik (Day 88-90'ın dersi):** Doğrulama sonrası `FieldOps.Api.exe` ve Angular'ın `node`/`esbuild` process'leri komut satırına göre bulunup kapatıldı, her iki port da boşaldığı doğrulandı.

## Demo basitleştirmesi vs. üretim gereksinimi

* `getById`, tüm listeyi çekip istemci tarafında filtreliyor — verimsiz, gerçek bir `GET /api/workorders/{id}` endpoint'i daha doğru olurdu, bugün bilerek eklenmedi (routing'in konusu dışı).
* Geçersiz bir `id` için sadece basit bir metin gösteriliyor — gerçek bir 404 sayfası/yönlendirmesi yok.

---

## 7. GERÇEK bir üretim hatası, Berkan'ın Chrome'da denemesiyle canlı yakalandı — Day 90'ın teşhisi düzeltildi

**Ne oldu:** Berkan gerçek tarayıcısında uygulamayı açtı, backend'i de çalıştırdı — ama tabloda sadece başlıklar (`Id`, `Title`, `Status`) göründü, SATIRLAR hiç gelmedi. Backend loglarına bakınca: istek GERÇEKTEN gidiyordu, GERÇEKTEN `200 OK` dönüyordu, SQL sorgusu GERÇEKTEN çalışıyordu. Yani veri VARDI ama ekrana hiç yansımıyordu.

**Day 90'da yaptığım hata:** Aynı semptomu (veri alan'da var, ekranda yok) test ortamında da görmüştüm ve bunu "Angular'ın çok yeni test altyapısının bir tuhaflığı" diye açıklamıştım. YANLIŞTI — gerçek tarayıcıda da aynı şey olunca, bunun bir test sorunu değil, GERÇEK bir uygulama hatası olduğu ortaya çıktı.

**Gerçek kök neden:**
```json
// package.json -- "zone.js" hiç yok
"dependencies": { "@angular/core": "^22.2.0", ... }
```
Bu proje **zoneless** bir Angular uygulaması (Angular 22'nin yeni varsayılanı). Normal (eski, zone.js'li) Angular'da, HER asenkron işlem (HTTP cevabı, `setTimeout`, vs.) bittiğinde, zone.js bunu ARKADAN YAKALAR ve Angular'a otomatik olarak "kontrol et, bir şey değişmiş olabilir" der — buna hiç ihtiyaç duymadan, "sihirli" bir şekilde çalışırdı. Zoneless modda bu sihir YOK. Angular'a "bir şey değişti" demenin AÇIK, elle yapılan bir yolu gerekiyor.

```typescript
// YANLIŞ (dün yazdığımız kod) -- zoneless modda ASLA ekranı güncellemiyor
ngOnInit(): void {
  this.workOrderService.getAll().subscribe(workOrders => {
    this.workOrders = workOrders;   // Angular'a HİÇ haber verilmiyor
  });
}
```

**Doğru çözüm: `signal`**
```typescript
import { signal } from '@angular/core';

export class WorkOrderList implements OnInit {
  protected readonly workOrders = signal<WorkOrder[]>([]);
  // signal(baslangicDegeri) -- Angular'ın ZONELESS modda da TAKİP ETTİĞİ,
  // özel bir "gözlemlenebilir kutu." İçindeki değer DEĞİŞTİĞİNDE, Angular
  // OTOMATİK olarak "bu signal'i kullanan her yeri yeniden çiz" diyor.

  ngOnInit(): void {
    this.workOrderService.getAll().subscribe(workOrders => {
      this.workOrders.set(workOrders);
      // .set(yeniDeger) -- signal'in İÇİNİ değiştirmenin TEK yolu. Bu çağrı,
      // Angular'a GERÇEKTEN "bir şey değişti" diye haber veriyor -- düz bir
      // "=" atamasının YAPAMADIĞI şey tam olarak bu.
    });
  }
}
```
```html
<!-- Template'te de fark var: signal bir DEĞER değil, bir FONKSİYON -- okumak için ÇAĞIRMAK gerekiyor -->
@for (workOrder of workOrders(); track workOrder.id) {   <!-- workOrders DEĞİL, workOrders() -->
  ...
}
```
Aynı düzeltme `WorkOrderDetail`'e de uygulandı (`workOrder`/`notFound` artık `signal`).

**Kanıt — bu sefer gerçekten düzeldiğinin kanıtı:** `work-order-list.spec.ts`'e, GERÇEK bir `HttpTestingController.flush(...)` + `detectChanges()` kullanan yeni bir test eklendi (dün BUNUN AYNISI başarısız olmuştu). Bugün bu test **GEÇTİ** — hem gerçek tarayıcı hem de test ortamı, AYNI kökten düzeldi. Bu, Day 90'daki "test aracı sorunu" açıklamasının yanlış olduğunun, ve `signal` düzeltmesinin doğru olduğunun somut kanıtı.

**Önemli ders:** Bir hatayı "test aracının tuhaflığı" diye etiketlemeden önce, GERÇEK ortamda (tarayıcıda) da aynı şeyin olup olmadığını kontrol etmek gerekiyordu. Berkan'ın "bana veri gelmiyor" diye bildirmesi olmasaydı, bu gerçek hata fark edilmeden kalabilirdi.
