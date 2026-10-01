# Day 92 — Kod Notları

Faz 5, Hafta 18, Gün 92. Konu: **Reactive Forms — yeni iş emri oluşturma**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Dünden hatırlarsan:** Şu ana kadar hep OKUDUK (`GET`) — hiç veri GÖNDERMEDİK. Backend'de zaten çalışan, gerçek bir `POST /api/workorders` var (bunu Day 40'larda yazmıştık). Bugün Angular tarafında ilk kez bir FORM yazıp, kullanıcıdan aldığımız veriyi bu GERÇEK endpoint'e gönderiyoruz.

**"Reactive Forms" ne demek, neden bu isimde:** Angular'da formları yönetmenin iki yolu var — "template-driven" (HTML'de `ngModel` ile, daha basit ama test etmesi zor) ve "reactive" (form'un TÜM durumunu TypeScript tarafında, `FormGroup` adlı TEK bir nesnede tutmak). Bugün reactive'i seçtik çünkü form durumunu KOD tarafında okuyup kontrol edebiliyoruz — "başlık doluysa buton aktif olsun" gibi kararları HTML'de değil, TypeScript'te yazıyoruz.

---

## 1. `WorkOrderService.create` — gerçek POST isteği

```typescript
create(title: string, customerId: number | null): Observable<WorkOrder> {
  return this.http.post<WorkOrder>(
    this.apiBaseUrl,
    { title, customerId },      // bu nesne, backend'in CreateWorkOrderRequest'ine JSON olarak gönderiliyor
    { headers: this.demoHeaders },
  );
}
```
`http.post<T>(url, body, options)` — `http.get`'in POST karşılığı. İkinci parametre (`body`), gövde olarak GERÇEKTEN gönderilen JSON. Backend'deki `CreateWorkOrderRequest(string Title, int? CustomerId)` kaydı, bu JSON'u (büyük/küçük harf duyarsız şekilde) otomatik olarak kendi alanlarına eşliyor.

---

## 2. `work-order-create.ts` — `FormGroup`/`FormControl`

```typescript
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

@Component({
  imports: [ReactiveFormsModule],   // formGroup/formControlName direktiflerini kullanabilmek için
  ...
})
export class WorkOrderCreate {
  protected readonly form = new FormGroup({
    title: new FormControl('', {
      nonNullable: true,                                          // değeri ASLA null/undefined olmasın, hep string
      validators: [Validators.required, Validators.maxLength(200)],  // backend'deki [Required, StringLength(200)] ile AYNI kural
    }),
    customerId: new FormControl<number | null>(null),   // opsiyonel -- validator yok
  });

  constructor(
    private readonly workOrderService: WorkOrderService,
    private readonly router: Router,
  ) {}

  submit(): void {
    if (this.form.invalid) {
      return;    // zaten buton devre dışı olurdu, ama GÜVENLİK için burada da kontrol
    }
    const { title, customerId } = this.form.getRawValue();
    // .getRawValue() -- form'un O ANKİ gerçek değerlerini düz bir nesne
    // olarak okur (disabled alanlar dahil -- bugün yok ama genel kullanım bu).
    this.workOrderService.create(title, customerId).subscribe(created => {
      this.router.navigate(['/work-orders', created.id]);
      // Router.navigate -- routerLink'in KOD tarafındaki karşılığı; bir
      // tıklama olmadan, PROGRAMATİK olarak sayfa değiştirmek için kullanılır.
      // Dünkü routing, bugün burada GERÇEKTEN işe yarıyor.
    });
  }
}
```

---

## 3. `work-order-create.html` — formun HTML'i

```html
<form [formGroup]="form" (ngSubmit)="submit()">
  <!-- [formGroup]="form" -- bu HTML form'unu, YUKARIDAKİ TypeScript FormGroup'una BAĞLIYOR -->
  <!-- (ngSubmit)="submit()" -- forma Enter'a basılınca VEYA submit butonuna
       tıklanınca, TARAYICININ KENDİ sayfa-yenileme davranışı yerine BİZİM
       submit() metodumuz çalışsın diye -->

  <input id="title" type="text" formControlName="title" />
  <!-- formControlName="title" -- bu input'u, FormGroup içindeki "title"
       FormControl'üne bağlıyor. Kullanıcı yazdıkça, form.controls.title.value
       OTOMATİK güncelleniyor -- biz hiçbir (change) event'i dinlemiyoruz. -->

  @if (form.controls.title.invalid && form.controls.title.touched) {
    <p>Title is required (max 200 characters).</p>
  }
  <!-- .touched -- kullanıcı bu alana En Az Bir Kere dokunup (focus olup)
       sonra çıktı mı diye -- hata mesajını SAYFA AÇILIR AÇILMAZ değil,
       kullanıcı gerçekten o alanla etkileşime geçtikten sonra göstermek için. -->

  <button type="submit" [disabled]="form.invalid">Create</button>
  <!-- [disabled]="form.invalid" -- form.invalid, FormGroup'un İÇİNDEKİ TÜM
       FormControl'lerin validator'larına göre otomatik hesaplanan bir alan --
       biz hiç elle "title boş mu" diye kontrol etmiyoruz. -->
</form>
```

---

## 4. `app.routes.ts` — route SIRASININ önemi

```typescript
export const routes: Routes = [
  { path: '', component: WorkOrderList },
  { path: 'work-orders/new', component: WorkOrderCreate },   // ÖNCE
  { path: 'work-orders/:id', component: WorkOrderDetail },   // SONRA
];
```
Router, listeyi YUKARIDAN AŞAĞIYA, İLK eşleşeni kullanarak kontrol eder. `work-orders/new` ÖNCE gelmezse, `:id` deseni "new" kelimesini bir ID gibi YUTAR (çünkü `:id` herhangi bir metinle eşleşir), ve `WorkOrderCreate` asla çalışmaz, yerine `WorkOrderDetail` "new" diye bir ID arar ve "bulunamadı" gösterirdi.

---

## Regresyon (Day 92)

```
npx ng build               -> Başarılı, 285.99 kB (dün 232.31 kB -- ReactiveFormsModule eklenince büyüdü)
npx ng test --watch=false  -> 16/16 geçti (5 test dosyası)
dotnet build FieldOps.slnx -> 0 Hata, 0 Uyarı (backend'e hiç dokunulmadı)
```

## Canlı doğrulama (HTTP seviyesinde)

```
curl -X OPTIONS ... (POST için preflight) -> 204, Access-Control-Allow-Methods: POST
curl -X POST .../api/workorders -d '{"title":"Day 92 reactive forms test","customerId":null}'
  -> 201 Created, {"id":41,"title":"Day 92 reactive forms test",...}
```
Gerçek bir iş emri (id=41) GERÇEKTEN SQL Server'da oluştu — `WorkOrderCreate`'in göndereceği GERÇEK isteğin backend tarafında sorunsuz işlendiğinin kanıtı.

**Dürüst sınırlama:** Formun kendisinin (tıklama, validasyon mesajının görünmesi, yönlendirme) gerçek tarayıcıda çalıştığı yine benim tarafımdan gözlemlenemedi — bu curl testi sadece backend'in isteği doğru işlediğini kanıtlıyor, Angular form'unun GERÇEKTEN bu isteği doğru ürettiğini değil (o kısım `ng test`'teki `createSpy`/`navigateSpy` ile birim test seviyesinde doğrulandı).

---

## 5. Gerçek bir ortam sorunu, Berkan'ın "çok bekliyor" demesiyle yakalandı

**Ne oldu:** Berkan gerçek tarayıcıda formu denedi, çalıştı AMA "Create ederken çok bekliyor" dedi.

**Kök neden (Day 92'nin kodunda bir hata DEĞİL):** `WorkOrdersController.Create`, Day 49'dan beri her başarılı oluşturmadan sonra `WorkOrderReportService.InvalidateCache(...)` çağırıyor — bu, Redis'e bir `KeyDelete` komutu gönderiyor. Berkan'ın gerçek dev Redis container'ı (`fieldops-redis`, Week 11'den beri kullanılan) **4 gündür kapalıydı**. Redis'e ulaşılamayınca, bu çağrı (hata `catch` ile yakalanıp sessizce loglanmadan ÖNCE) StackExchange.Redis'in senkron komut zaman aşımı kadar (**~5 saniye**) bekliyor.

**Kanıt:**
```
Redis KAPALIYKEN: port 6379 erişilemez
docker start fieldops-redis
Redis AÇIKKEN:    aynı POST isteği -> 0.106 saniye (neredeyse anlık)
```

**Neden bugün fark edildi, önceki günlerde değil:** Bu davranış `Create`'in HER ZAMAN bir parçasıydı — bugün ilk kez GERÇEK UI üzerinden, beklerken hissederek test edildiği için ortaya çıktı. curl ile yapılan önceki testlerde bu gecikme fark edilmemiş olabilir ya da o testler sırasında Redis açıktı.

**Bugün kod DEĞİŞTİRİLMEDİ** — bu bir uygulama hatası değil, eksik bir ortam bağımlılığıydı. Çözüm: `docker start fieldops-redis`, API'yi çalıştırmadan önce. İleride (ayrı bir gün olarak) `InvalidateCache`'in Redis kapalıyken isteği HİÇ bekletmeyecek şekilde (fire-and-forget) yazılması, gerçek bir iyileştirme olurdu.

## Demo basitleştirmesi vs. üretim gereksinimi

* `Idempotency-Key` header'ı (Day 54'te backend'e eklenmişti) bugün Angular'dan GÖNDERİLMİYOR — bir form çift gönderiminin (örn. kullanıcı "Oluştur"a iki kez tıklarsa) iki ayrı iş emri yaratması mümkün, bilerek bugünün kapsamı dışında bırakıldı.
* Backend hatası (örn. geçersiz `CustomerId`) bugün hiç ele alınmıyor — `subscribe`'ın `error` callback'i yazılmadı, bir hata durumunda kullanıcı hiçbir şey görmez. Gerçek bir uygulamada bu KESİNLİKLE eklenmesi gereken bir şey.
