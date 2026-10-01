# Day 89 — Kod Notları

Faz 5 (Angular, Kubernetes, Cloud), Hafta 18, Gün 89. Konu: **Angular'a giriş — component, template, standalone yapı**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**"Frontend" dediğimiz şey tam olarak ne:** Şu ana kadar FieldOps'ta SADECE şunu yaptık: `FieldOps.Api` (bir sunucu) çalışıyordu, biz ona curl/Postman ile "bana iş emirlerini ver" diye istek atıyorduk, o da JSON döndürüyordu — yani hep **sunucuya doğrudan konuştuk**. Gerçek bir kullanıcı terminale curl yazmaz, tarayıcısına bir adres yazar, güzel bir sayfa (tablo, buton, form) görmek ister. İşte o sayfayı üreten koda **frontend** diyoruz. Bugün onu yazmaya başladık.

**En can alıcı netleştirme:** Bugün yazdığımız kod, `FieldOps.Api`'ye HİÇ bağlanmıyor. Tamamen ayrı, kendi başına duran, sahte/sabit veri gösteren bir sayfa yaptık. Yarın bu sayfayı gerçek API'ye bağlayacağız. Bugün sadece "tarayıcıda bir sayfa nasıl yazılır" öğrendik — henüz hiçbir gerçek işlev eklemedik, sadece ileride kuracağımız şeyin en küçük iskeletini kurduk.

**Nereye yazdık:** `src/fieldops-web`, `src/FieldOps.Api`'den TAMAMEN bağımsız, ayrı bir proje — aynı workspace'in içinde yan yana duruyorlar ama birbirlerini hiç tanımıyorlar (`RoadmapOS.slnx`/`StockPilot.slnx`'in aynı klasörde ama birbirinden habersiz iki ayrı çözüm olması gibi, Day 11).

**MVC'den bildiğin şeylere benzetme:**

| MVC'de (bildiğin) | Angular'da (bugün) |
|---|---|
| `Controller` | yok (henüz) — backend'e hiç bağlanmadık |
| `View` (`.cshtml`) | `.html` dosyası |
| View'a veri taşıyan model | `.ts` dosyasındaki sınıf |

**Tarayıcı sayfayı açtığında gerçek akış:**
```
1. localhost:4200 açılır
2. Angular önce app.html'i (ana çerçeve) çizer
3. app.html içindeki "<app-work-order-list />" görülünce,
   work-order-list.ts'teki 3 sabit iş emri alınır
4. work-order-list.html'deki tablo o 3 iş emriyle doldurulup ekrana basılır
```

**Bugün bilerek YOK olanlar:** backend bağlantısı, birden fazla sayfa (routing), herhangi bir buton/form/etkileşim, hata/yüklenme durumu. Hepsi roadmap'e göre ileride tek tek eklenecek.

**Angular CLI ne işe yarıyor:** `dotnet new`/`dotnet run`'ın karşılığı — proje iskeletini, derleme ayarlarını, geliştirme sunucusunu otomatik kuruyor.

---

## 1. Proje scaffold

```bash
npx @angular/cli@latest new fieldops-web --style=css --routing=false --ssr=false --skip-git
# npx         -> npm paketini kurmadan/global kurmadan direkt çalıştırır (dotnet tool run'a benzer)
# new         -> yeni bir Angular projesi oluştur
# --routing=false -> bugün sayfa yönlendirme (routing) yok, TEK ekran var, erken karmaşıklık eklemiyoruz
# --ssr=false -> Server-Side Rendering KAPALI -- bugün sadece tarayıcıda (client-side) çalışan basit bir uygulama istiyoruz
# --skip-git  -> ayrı bir git repo başlatma, zaten DotNetCareerPath'in kendi reposu var
```
Bu komut `src/fieldops-web/` altında tam bir Angular projesi (paket bağımlılıkları dahil) oluşturdu — tıpkı `dotnet new webapi`'nin `.csproj` ve başlangıç dosyalarını oluşturması gibi.

---

## 2. `work-order.ts` — veri şekli (TypeScript interface)

```typescript
export interface WorkOrder {
  id: number;
  title: string;
  status: string;
}
// interface -- TypeScript'in "bu şekle sahip bir nesne" tanımı. C#'taki bir
// DTO/record'un en yalın hâli gibi düşünülebilir -- ama derleme zamanında
// SADECE tip kontrolü için var, çalışma zamanında (runtime'da) HİÇBİR iz
// bırakmaz (derlenince tamamen silinir) -- C#'taki bir class/record'un
// aksine, gerçek bir obje değil.
```

---

## 3. `work-order-list.ts` — component'in TypeScript sınıfı

```typescript
import { Component } from '@angular/core';
import { WorkOrder } from '../work-order';

@Component({
  imports: [],                                  // bu component'in KENDİ template'inde kullandığı başka component/directive'ler -- bugün yok
  selector: 'app-work-order-list',               // bu component'i HTML'de <app-work-order-list /> olarak çağırabiliriz -- ASP.NET Core'daki bir Tag Helper'a benzer bir fikir
  styleUrl: './work-order-list.css',             // bu component'e ÖZEL, dışarı sızmayan CSS dosyası
  templateUrl: './work-order-list.html',         // bu component'in HTML şablonu, ayrı dosyada
})
export class WorkOrderList {
  protected readonly workOrders: WorkOrder[] = [
    { id: 1, title: 'Replace HVAC filter - Building A', status: 'Open' },
    { id: 2, title: 'Inspect generator - Site 12', status: 'InProgress' },
    { id: 3, title: 'Repair leaking valve - Warehouse 3', status: 'Completed' },
  ];
  // Day 89: veri BİLEREK burada sabit kodlanmış (hardcoded) -- henüz hiçbir
  // HTTP çağrısı yok. "protected readonly" -- SADECE bu sınıfın kendi
  // template'i erişebilsin (protected), ve referans asla değişmesin
  // (readonly) diye -- C#'taki "private readonly + sadece bu sınıfın
  // Razor view'i erişebilir" fikrine benzer bir kısıtlama.
}
```
`@Component({...})` — bir **decorator**: altındaki sınıfı "bu sıradan bir class değil, Angular bunu bir component olarak tanısın" diye işaretliyor. C#'taki `[ApiController]`/`[HttpGet]` attribute'larıyla aynı fikir — ek bilgiyi doğrudan kodun üzerine, ayrı bir konfigürasyon dosyasına gitmeden ekliyor.

**"Standalone component" ne demek:** Eski Angular'da her component'in mutlaka bir `NgModule`'e (bir grup component'i bir araya toplayan, ayrı bir "paket" tanımı) ait olması gerekiyordu. Modern Angular'da (bugün kullandığımız) bir component `imports: [...]` ile ihtiyaç duyduğu şeyleri DOĞRUDAN kendisi bildiriyor, ayrı bir `NgModule` dosyasına gerek kalmıyor — daha az dolaylama, daha kolay takip edilebilir bağımlılıklar.

---

## 4. `work-order-list.html` — template (veri ekrana nasıl basılıyor)

```html
<h2>Work Orders</h2>
<table>
  <thead>
    <tr>
      <th>Id</th>
      <th>Title</th>
      <th>Status</th>
    </tr>
  </thead>
  <tbody>
    @for (workOrder of workOrders; track workOrder.id) {
      <tr>
        <td>{{ workOrder.id }}</td>
        <td>{{ workOrder.title }}</td>
        <td>{{ workOrder.status }}</td>
      </tr>
    }
  </tbody>
</table>
```
* `{{ workOrder.id }}` — **interpolation**: TypeScript tarafındaki değeri doğrudan HTML metni içine basar. Razor'daki `@Model.Id` ile aynı fikir.
* `@for (workOrder of workOrders; track workOrder.id) { ... }` — modern Angular'ın (v17+) yerleşik tekrar (loop) sözdizimi; eski `*ngFor` directive'inin yerini aldı, ekstra bir import gerektirmiyor. Razor'daki `@foreach` ile birebir aynı amaç.
* `track workOrder.id` — Angular'a "bu listedeki her satırı HANGİ alana göre takip edeceğini" söylüyor; liste değiştiğinde (örneğin sıralama değişince) Angular tüm DOM'u yeniden çizmek yerine SADECE gerçekten değişen satırları günceller. `id` zaten benzersiz olduğu için doğal seçim.

---

## 5. `app.ts` ve `app.html` — kök component, alt component'i kullanıyor

```typescript
import { Component } from '@angular/core';
import { WorkOrderList } from './work-order-list/work-order-list';

@Component({
  imports: [WorkOrderList],   // bu component'i KULLANABİLMEK için önce buraya import etmek gerekiyor
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {}
```
```html
<main>
  <h1>FieldOps</h1>
  <app-work-order-list />
</main>
```
`<app-work-order-list />` — `work-order-list.ts`'teki `selector: 'app-work-order-list'` sayesinde, bu HTML etiketi Angular tarafından "burada `WorkOrderList` component'ini render et" olarak tanınıyor. Bu, `App` (en dıştaki "kök" component — `index.html`'in içine gömülen tek şey) ile `WorkOrderList` (içerideki, LEGO parçası) arasındaki ilk **parent-child component** ilişkisi.

---

## Regresyon (Day 89)

```
npx ng build   -> Başarılı (12.3sn), tek çıktı: main-*.js (104.90 kB), styles (0 B)
```

## Canlı doğrulama

`npx ng serve --port 4200` ile geliştirme sunucusu başlatıldı:
```
curl http://localhost:4200/          -> HTTP 200 (index.html, Vite dev-server script'i ile)
curl http://localhost:4200/main.js | grep "FieldOps|Work Orders|Replace HVAC filter"
   -> üçü de bulundu -- derlenmiş JS paketinin GERÇEKTEN bizim component'imizin
      metnini içerdiği doğrulandı
```
**Dürüst bir sınırlama:** Bugünkü ortamda gerçek bir tarayıcıda ekran görüntüsü alacak bir araç yoktu — doğrulama, derlenmiş JavaScript paketinin içinde beklenen metinlerin (component'lerin HTML'i derleme sırasında JS'e gömülüyor) gerçekten var olduğunu, ve sunucunun HTTP 200 döndüğünü göstererek yapıldı. Bu, "sayfa tarayıcıda doğru görünüyor" iddiasından daha zayıf bir kanıt — gerçek görsel doğrulama bir sonraki fırsatta (tarayıcı erişimi olduğunda) yapılmalı.

Ayrıca Day 88'in dersi burada da uygulandı: dev server arka planda başlatıldıktan sonra, port 4200'de GERÇEKTEN sadece bu oturumun kendi process zincirinin (node/esbuild, `fieldops-web` komut satırıyla eşleşen) dinlediği doğrulandı, oturum sonunda da aynı şekilde temizlenecek.

## Demo basitleştirmesi vs. üretim gereksinimi

* Veri `WorkOrderList` içinde sabit kodlanmış — yarın gerçek bir `HttpClient` çağrısıyla `FieldOps.Api`'den çekilecek, bugünkü HTML şablonu değişmeden kalacak.
* Routing, state management, hata/yüklenme durumları (loading/error state) bugün bilerek yok — henüz tek, statik bir ekran var.
