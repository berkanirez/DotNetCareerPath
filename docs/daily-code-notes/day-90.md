# Day 90 — Kod Notları

Faz 5, Hafta 18, Gün 90. Konu: **HttpClient ile gerçek backend entegrasyonu + CORS**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Dünden hatırlarsan:** `WorkOrderList`, kendi içinde sabit 3 iş emri tutuyordu, backend'e hiç dokunmuyordu. Bugün bu sabit veriyi kaldırıp, GERÇEK `FieldOps.Api`'den, GERÇEK SQL Server verisini çekiyoruz.

**Karşımıza çıkan iki GERÇEK problem (ikisi de dün hiç yaşanmadı, çünkü dün backend'e hiç gitmemiştik):**

1. **CORS:** Tarayıcı, güvenlik gereği, bir sayfanın (burada `localhost:4200`) başka bir adrese (`localhost:5138`, yani API) JavaScript üzerinden istek atmasını, sunucu açıkça izin vermedikçe ENGELLER. Bu kural SADECE tarayıcıdan gelen isteklere uygulanıyor — bugüne kadar hep curl kullandık, curl bu kurala hiç tabi değil, o yüzden hiç karşımıza çıkmamıştı. Çözüm: backend'e "localhost:4200'den gelen isteklere izin ver" diye açıkça söylemek.
2. **Kimlik eksikliği:** `WorkOrdersController.GetAll`, Day 40'tan beri `X-Organization-Id`/`X-Employee-Id` header'larını zorunlu kılıyor. Henüz bir login ekranımız yok (roadmap'te ayrı bir gün) — bugünlük bu header'ları Angular tarafında SABİT (1 ve 1) gönderiyoruz. Bu GERÇEK bir kimlik doğrulama DEĞİL, geçici bir yer tutucu.

---

## 1. Backend: CORS politikası (`FieldOps.Api/Program.cs`)

```csharp
var angularDevOrigin = builder.Configuration["Cors:AngularDevOrigin"] ?? "http://localhost:4200";
// Configuration'dan okunuyor, yoksa varsayılan localhost:4200 -- RabbitMq
// hostname'inin okunma şekliyle aynı desen (Day 67).

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDev", policy => policy
        .WithOrigins(angularDevOrigin)   // SADECE bu adrese izin ver -- joker (*) KULLANILMADI, bilerek dar kapsamlı
        .AllowAnyHeader()                  // X-Organization-Id gibi özel header'lara izin ver
        .AllowAnyMethod());                // GET, POST, vs. hepsine izin ver
});
```
```csharp
app.UseCors("AngularDev");
// Pipeline'da UseHttpsRedirection'dan SONRA, UseRateLimiter/UseAuthorization'dan
// ÖNCE -- tarayıcı bazı isteklerden önce görünmez bir "preflight" (OPTIONS)
// isteği gönderir, bu CORS kontrolünden GEÇMEK ZORUNDA, başka hiçbir
// middleware'e uğramadan.
```

---

## 2. Angular: `provideHttpClient()` (`app.config.ts`)

```typescript
import { provideHttpClient } from '@angular/common/http';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(),   // bu olmadan HttpClient hiçbir yerde inject edilemez
  ]
};
```
Bu, .NET tarafındaki `builder.Services.AddHttpClient()`'a çok benzer bir fikir — "bu uygulamada HTTP istekleri atabileceğim bir servis olsun" diye DI container'a kaydetmek.

---

## 3. `work-order.ts` — artık backend'in gerçek şekliyle eşleşiyor

```typescript
export interface WorkOrder {
  id: number;
  title: string;
  status: number;
  // Day 90: FieldOps.Api, WorkOrderStatus (bir C# enum'u) değerini JSON'a
  // SAYI olarak yazıyor (0=Open, 1=Assigned, 2=InProgress, 3=Completed) --
  // System.Text.Json'ın varsayılan davranışı, JsonStringEnumConverter
  // eklenmediği için. Dün elle yazdığımız 'Open'/'Completed' string'leri
  // GERÇEK API'nin döndürdüğü şeyle uyuşmuyordu.
}

export const WORK_ORDER_STATUS_LABELS: readonly string[] = ['Open', 'Assigned', 'InProgress', 'Completed'];
// status SAYISINI okunabilir bir metne çevirmek için basit bir dizi --
// WORK_ORDER_STATUS_LABELS[2] === 'InProgress'. Yeni bir soyutlama değil,
// sadece küçük bir arama (lookup) tablosu.
```

---

## 4. `work-order.service.ts` (yeni) — gerçek HTTP çağrısı

```typescript
import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { WorkOrder } from './work-order';

@Injectable({ providedIn: 'root' })
// @Injectable -- bu sınıfın Angular'ın DI sistemine kaydedilebilir bir
// SERVİS olduğunu belirtiyor. providedIn: 'root' -- tüm uygulamada TEK bir
// örneği (singleton) olsun, ve SADECE gerçekten birisi inject ettiğinde
// oluşturulsun (ASP.NET Core'daki AddSingleton'a yakın bir fikir).
export class WorkOrderService {
  private readonly apiBaseUrl = 'http://localhost:5138/api/workorders';
  private readonly demoHeaders = new HttpHeaders({
    'X-Organization-Id': '1',
    'X-Employee-Id': '1',
  });
  // SABİT, GEÇİCİ kimlik bilgisi -- gerçek login/JWT henüz yok.

  constructor(private readonly http: HttpClient) {}
  // HttpClient, Angular tarafından OTOMATİK enjekte ediliyor -- biz kendimiz
  // bir örnek oluşturmuyoruz, tıpkı bir .NET controller'ının constructor'ında
  // bir interface alması gibi.

  getAll(): Observable<WorkOrder[]> {
    return this.http.get<WorkOrder[]>(this.apiBaseUrl, { headers: this.demoHeaders });
  }
  // Observable -- "ileride bir sonuç gelecek" sözü veren bir nesne, .NET'teki
  // Task<T>'ye benzer bir fikir ama FARKLI: bir Task sadece BİR KERE sonuç
  // üretir, bir Observable zamanla BİRDEN FAZLA değer de üretebilir (bugün bu
  // farkı kullanmıyoruz, sadece tek bir HTTP cevabı için kullanıyoruz).
}
```

---

## 5. `work-order-list.ts` — sabit diziden gerçek çağrıya

```typescript
export class WorkOrderList implements OnInit {
  protected workOrders: WorkOrder[] = [];           // artık BOŞ başlıyor
  protected readonly statusLabels = WORK_ORDER_STATUS_LABELS;

  constructor(private readonly workOrderService: WorkOrderService) {}

  ngOnInit(): void {
    this.workOrderService.getAll().subscribe(workOrders => {
      this.workOrders = workOrders;
    });
  }
  // ngOnInit -- Angular'ın component'i ekrana YERLEŞTİRDİKTEN hemen sonra
  // çağırdığı yaşam döngüsü (lifecycle) metodu. "Veri çekme" gibi işler
  // constructor'da DEĞİL, burada yapılır -- constructor sadece bağımlılıkları
  // almak için.
  // .subscribe(...) -- Observable'ın "bana sonucu ver, geldiğinde bu kodu
  // çalıştır" demesi. Razor'daki `await`'e benzer bir AMAÇ taşıyor ama farklı
  // bir mekanizma (callback tabanlı, Promise/async-await değil).
}
```

---

## ⚠️ DÜZELTME (Day 91'de eklendi): aşağıdaki teşhis EKSİKTİ

Aşağıda "uygulama hatası DEĞİL, sadece test aracı sorunu" diyorum — bu YANLIŞTI. Berkan gerçek Chrome'da denediğinde AYNI sorunu yaşadı: veri gerçekten geliyordu (backend logları 200 OK gösteriyordu) ama tabloda satır görünmüyordu. Gerçek kök neden, Day 91'de bulundu: bu proje **zoneless** (zone.js yok), ve `.subscribe(d => this.workOrders = d)` gibi düz bir alan ataması, zoneless Angular'a "ekranı yeniden çiz" diye HİÇ haber vermiyor. Doğru çözüm, `workOrders`'ı bir `signal` yapmaktı — detaylar `day-91.md`'de.

## Gerçek, canlı yakalanan bir test-altyapısı sorunu (uygulama hatası DEĞİL) — [BU TEŞHİS YANLIŞTI, yukarıdaki düzeltmeye bakın]

İlk testi yazarken şunu denedim: `detectChanges()` → sahte HTTP isteğini `flush(...)` ile cevapla → tekrar `detectChanges()` → tabloda satır var mı diye bak. Component'in kendi `workOrders` alanının GERÇEKTEN doğru veriyle dolduğunu (doğrudan loglayarak) doğruladım, ama tabloda HÂLÂ hiçbir satır görünmüyordu.

Kök neden: Bu proje (Angular 22, varsayılan olarak **zoneless** — `zone.js` hiç yok) + çok yeni bir test altyapısı (`@angular/build:unit-test`, vitest tabanlı) kombinasyonunda, bir `@for` bloğu İLK ÖNCE boş bir diziyle çizildiyse, SONRADAN gelen veriyle güncellenmesi (ikinci bir `detectChanges()` çağrısıyla) güvenilir şekilde ÇALIŞMIYOR — component'in alanı doğru olsa bile. Bunu izole bir testle KANITLADIM: component'e veriyi SENKRON döndüren sahte (stub) bir servis verdiğimde (`of([...])`), TEK bir `detectChanges()` çağrısı düzgün çalıştı ve satır göründü.

**Ne yaptım (dürüst, kaçmadan):** Testleri ikiye ayırdım — (1) gerçek `WorkOrderService` + sahte HTTP ile SADECE "doğru adrese, doğru header'larla istek atılıyor mu" kontrol ediliyor (DOM'a hiç bakmadan); (2) `@for`'un bir diziyi doğru render ettiği, senkron bir stub servisle AYRI test ediliyor. Bu aslında daha iyi bir test tasarımı da sayılır (tek seferde tek şeyi test etmek), ama asıl sebep dürüstçe kaydedilen bu test-altyapısı sınırlaması.

---

## Regresyon (Day 90)

```
npx ng build               -> Başarılı (1.3sn), 132.29 kB (dün 104.90 kB -- HttpClient eklenince büyüdü)
npx ng test --watch=false  -> 6/6 geçti (3 test dosyası)
dotnet build FieldOps.slnx -> 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx  -> (bu notlardan SONRA tamamlanacak, arka planda çalışıyor)
```

## Canlı uçtan uca doğrulama

`FieldOps.Api`'yi native (`dotnet run`, port 5138, gerçek local `SQLEXPRESS` veritabanına karşı) ve `fieldops-web`'i (`ng serve`, port 4200) AYNI ANDA çalıştırarak:

1. `curl -X OPTIONS ... -H "Origin: http://localhost:4200"` → `204`, `Access-Control-Allow-Origin: http://localhost:4200` — tarayıcının preflight kontrolünün GEÇECEĞİNİN kanıtı.
2. `curl -H "Origin: http://localhost:4200" -H "X-Organization-Id: 1" -H "X-Employee-Id: 1" http://localhost:5138/api/workorders` → `200`, GERÇEK 11 iş emri (Organization 1'in SQL Server'daki gerçek satırları, Day 76-88'in test verisi dahil), yanıtta da `Access-Control-Allow-Origin` header'ı mevcut.
3. `http://localhost:4200/main.js` içinde component'in gerçek metinleri (`"FieldOps"`, `"Work Orders"`) bulundu.

**Dürüst sınırlama (dün de belirtilen):** Gerçek bir tarayıcıda görsel doğrulama yine yapılamadı (araç yok) — ama bu sefer kanıt daha güçlü: CORS, TAM OLARAK tarayıcının kontrol ettiği header'lara (`Access-Control-Allow-Origin`, preflight yanıtı) bakılarak doğrulandı, curl'ün kendisinin CORS'u atlamasından BAĞIMSIZ bir kanıt.

**Temizlik (Day 88/89'un dersi uygulanarak):** Doğrulama bitince `Get-CimInstance Win32_Process` ile hem `FieldOps.Api.exe` hem `node`/`esbuild` (fieldops-web) process'leri komut satırına göre bulunup tek tek kapatıldı, ardından port 5138 ve 4200'ün GERÇEKTEN boşaldığı doğrulandı. Bugünkü demo için başlatılan `redis`/`rabbitmq`/`elasticsearch` Docker container'ları da ayrıca durduruldu.

## Demo basitleştirmesi vs. üretim gereksinimi

* `X-Organization-Id`/`X-Employee-Id` Angular kodunda SABİT — gerçek bir login/JWT akışı henüz yok (roadmap'te ayrı bir gün).
* API adresi (`http://localhost:5138`) doğrudan kodun içinde sabit — üretimde bu genelde ortam bazlı bir konfigürasyon dosyasından (`environment.ts`) okunur.
* CORS politikası sadece `localhost:4200`'e açık — gerçek üretimde gerçek frontend domain'ine açılır.
