# Day 94 — Kod Notları

Faz 5, Hafta 18, Gün 94. Konu: **Angular'da login ekranı + JWT interceptor**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Dünden hatırlarsan:** Backend'de token ÜRETMEYİ öğrendik (`POST /api/auth/login`), ama Angular tarafında bunu KULLANAN hiçbir şey yoktu. Bugün: (1) gerçek bir giriş formu, (2) alınan token'ı saklamak, (3) o token'ı GÖNDERDİĞİMİZ HER isteğe otomatik eklemek.

**En önemli netleştirme (dün de söylediğim gibi, bugün de geçerli):** Backend HÂLÂ token'ı kontrol ETMİYOR — sadece `X-Organization-Id`/`X-Employee-Id` header'larını kontrol ediyor. Bugün Angular token'ı EK olarak gönderecek ama bu, backend tarafında HİÇBİR ŞEYİ değiştirmeyecek (henüz). Token'ın GERÇEKTEN işe yaraması (header'ların yerini alması), backend'in bunu okumaya başladığı GELECEK bir günün konusu.

---

## 1. `auth.service.ts` (yeni) — token'ı tutan tek yer

```typescript
import { Injectable, signal } from '@angular/core';
// Injectable -- bu sınıfı Angular'ın DI (dependency injection) sistemine
// kaydedilebilir bir SERVİS olarak işaretleyen decorator.
// signal -- değeri değiştiğinde Angular'a OTOMATİK haber veren özel bir
// "kutu" oluşturan fonksiyon (Day 91'de zoneless sorununu çözmek için
// kullandığımız AYNI mekanizma).

import { HttpClient } from '@angular/common/http';
// HttpClient -- gerçek HTTP istekleri (GET/POST/vs.) atmamızı sağlayan,
// Angular'ın kendi servisi.

import { Observable, tap } from 'rxjs';
// Observable -- "ileride bir sonuç gelecek" sözü veren bir nesne türü
// (Day 90'da WorkOrderService'te de kullanmıştık).
// tap -- bir Observable'ın değerine DOKUNUP, değeri DEĞİŞTİRMEDEN, yan bir
// işlem (side effect) yapmamızı sağlayan bir RxJS fonksiyonu.

import { LoginResponse } from './login-response';
// LoginResponse -- backend'in /api/auth/login'den dönen GERÇEK JSON'ın
// şeklini tanımlayan, dün backend'de yazdığımız record'un TypeScript
// karşılığı (bir interface, Day 90'daki WorkOrder gibi).

@Injectable({ providedIn: 'root' })
// Bu satır, class'ın HEMEN üstüne yazılan bir "decorator" -- "bu sınıf bir
// servistir, ve uygulama genelinde TEK bir örneği (singleton) olsun, SADECE
// birisi gerçekten inject ettiğinde oluşturulsun" diyor.
export class AuthService {
// class AuthService -- bu, token ile ilgili HER ŞEYİ (login isteği atmak,
// token'ı saklamak, token'ı okumak) TEK bir yerde toplayan sınıf.

  private readonly apiBaseUrl = 'http://localhost:5138/api/auth';
  // apiBaseUrl -- backend'in auth endpoint'lerinin bulunduğu adres, sabit
  // (readonly) bir metin olarak tanımlanmış. "private" -- SADECE bu
  // sınıfın kendi içinden erişilebilir, dışarıdan (örn. component'lerden)
  // GÖRÜNMÜYOR bile.

  private readonly token = signal<string | null>(null);
  // Bu, token'ın GERÇEKTEN saklandığı yer. signal<string | null>(null) --
  // başlangıç değeri null (henüz giriş yapılmamış) olan bir signal
  // oluşturuyor. SADECE bellekte (RAM'de) tutuluyor -- localStorage'a
  // YAZILMIYOR. Sayfa yenilenince (F5) token KAYBOLUYOR -- bugünün
  // bilinçli basitleştirmesi. Gerçek uygulamada localStorage'a yazılırdı,
  // ama bu da kendi riskini taşır (XSS saldırısı olursa, sayfadaki
  // herhangi bir JavaScript kodu localStorage'ı okuyabilir) -- bu konu
  // bugünün kapsamı dışı.

  constructor(private readonly http: HttpClient) {}
  // constructor -- bu sınıftan bir örnek oluşturulurken ÇALIŞAN özel metot.
  // "private readonly http: HttpClient" -- Angular'ın DI sistemine "bana
  // bir HttpClient örneği ver, ve onu this.http olarak sakla" demenin kısa
  // yolu (C#'taki bir constructor parametresinin otomatik bir field'a
  // atanması gibi düşünülebilir, ama burada TypeScript'in kendi kısayolu).

  login(employeeId: number): Observable<LoginResponse> {
  // login -- dışarıdan (Login component'inden) çağrılacak, GERÇEK giriş
  // isteğini atan metot. Parametre olarak employeeId (bir sayı) alıyor,
  // geriye bir Observable<LoginResponse> DÖNDÜRÜYOR (henüz SONUÇ değil,
  // "sonuç ileride gelecek" sözü).

    return this.http
      .post<LoginResponse>(`${this.apiBaseUrl}/login`, { employeeId })
      // .post<LoginResponse>(url, body) -- gerçek bir HTTP POST isteği
      // hazırlıyor. `${this.apiBaseUrl}/login` -- iki metni birleştiren
      // TypeScript'in "template literal" söz dizimi (sonuç:
      // "http://localhost:5138/api/auth/login"). { employeeId } -- gönderilen
      // JSON gövdesi, { employeeId: employeeId }'in kısaltması (JavaScript'in
      // "shorthand property" özelliği -- değişken adı ile key adı AYNIYSA
      // tekrar yazmaya gerek yok).

      .pipe(tap(response => this.token.set(response.token)));
      // .pipe(tap(...)) -- Observable'ın değerini DEĞİŞTİRMEDEN, sadece bir
      // YAN ETKİ (token'ı kaydetmek) yapmak için. .NET'teki bir iteratör
      // üzerinde ara bir "gözlemle ama değiştirme" adımına benzer.
      // response => this.token.set(response.token) -- backend'den GERÇEK
      // cevap geldiğinde çalışacak fonksiyon: cevaptaki token alanını alıp,
      // yukarıda tanımladığımız signal'in İÇİNE .set(...) ile yazıyor.
  }

  getToken(): string | null {
  // getToken -- dışarıdan (interceptor'dan) "şu an token var mı, varsa ne"
  // diye sorulduğunda cevap veren metot.
    return this.token();
    // this.token() -- DİKKAT: signal bir DEĞER değil, bir FONKSİYON. İçindeki
    // GERÇEK değeri okumak için ÇAĞIRMAK (parantez koymak) gerekiyor.
  }

  isLoggedIn(): boolean {
  // isLoggedIn -- "kullanıcı giriş yapmış mı" diye basit bir evet/hayır
  // cevabı veren yardımcı metot.
    return this.token() !== null;
    // token'ın GERÇEK değeri null DEĞİLSE (yani bir token varsa), true döner.
  }
}
```

---

## 2. `auth.interceptor.ts` (yeni) — her isteğe otomatik "pul" yapıştırma

```typescript
import { HttpInterceptorFn } from '@angular/common/http';
// HttpInterceptorFn -- bir interceptor fonksiyonunun TAM OLARAK nasıl bir
// şekle (kaç parametre, ne döndürmesi gerektiği) sahip olması gerektiğini
// tanımlayan bir TypeScript tipi.

import { inject } from '@angular/core';
// inject -- constructor'ı OLMAYAN bir yerde (burada olduğu gibi, sade bir
// fonksiyonun İÇİNDE) bile Angular'ın DI sisteminden bir servis almayı
// sağlayan fonksiyon.

import { AuthService } from './auth.service';
// Bir önceki bölümde yazdığımız AuthService'i buraya getiriyoruz.

// Day 94: a functional interceptor — modern Angular's way of writing one
// (vs. the older class-based HttpInterceptor interface). Runs for EVERY
// outgoing HttpClient request in the app, so WorkOrderService never needs
// to know about tokens at all.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
// authInterceptor -- bir SABİT (const) olarak tanımlanmış, ama değeri bir
// FONKSİYON. (req, next) => {...} -- bu fonksiyonun İKİ parametresi var:
// req (giden GERÇEK istek nesnesi) ve next (bu isteği bir sonraki adıma,
// yani GERÇEK ağ çağrısına veya bir sonraki interceptor'a GEÇİREN fonksiyon).
// `HttpInterceptorFn` -- bu interceptor TEK bir fonksiyon, bir sınıf değil
// (eski Angular'daki `HttpInterceptor` interface'inin bir sınıfa implement
// edilmesi gerekiyordu, modern Angular'da artık gerekmiyor).

  const authService = inject(AuthService);
  // inject(AuthService) -- "bana GERÇEK AuthService örneğini ver" diyor.
  // Normal bir component'te bunu constructor'da yapardık, ama bu sade bir
  // fonksiyon olduğu için constructor YOK -- inject() bunun yerine geçiyor.

  const token = authService.getToken();
  // AuthService'ten GERÇEK token'ı (varsa) okuyoruz.

  if (!token) {
  // Token YOKSA (null ise) -- yani kullanıcı HENÜZ giriş yapmamışsa.
    return next(req);
    // İsteği HİÇBİR DEĞİŞİKLİK yapmadan, OLDUĞU GİBİ bir sonraki adıma
    // geçiriyoruz.
  }

  // req is immutable — .clone() is the only way to add a header; it returns
  // a NEW request object rather than mutating the original.
  const authorizedRequest = req.clone({
  // req.clone({...}) -- GERÇEK HttpRequest nesneleri DEĞİŞTİRİLEMEZ
  // (immutable). Bir header EKLEMEK için YENİ bir kopya oluşturmak ZORUNLU
  // -- orijinal nesneyi elle değiştiremeyiz. authorizedRequest, bu YENİ
  // kopyayı tutan değişken.
    setHeaders: { Authorization: `Bearer ${token}` },
    // setHeaders -- klonlanan isteğe EKLENECEK/değiştirilecek header'ları
    // belirtiyoruz. Authorization: `Bearer ${token}` -- gerçek token'ı,
    // JWT'lerin standart taşınma şekli olan "Bearer <token>" formatında
    // ekliyoruz.
  });
  return next(authorizedRequest);
  // Artık token'ı TAŞIYAN YENİ isteği, bir sonraki adıma geçiriyoruz.
};
```

---

## 3. `app.config.ts` — interceptor'ı KAYDETMEK

```typescript
provideHttpClient(withInterceptors([authInterceptor])),
// provideHttpClient(...) -- dün (Day 90) HttpClient'ı tanıttığımız aynı
// fonksiyon. withInterceptors([authInterceptor]) -- bu sefer ek bir
// parametre: "HttpClient'ın attığı HER isteğin, bu dizideki fonksiyonlardan
// (şu an sadece authInterceptor) sırayla GEÇMESİNİ sağla" diyor. Bundan
// sonra WorkOrderService'in HİÇBİR metodunun token'dan HABERİ bile olmuyor
// — otomatik ekleniyor.
```

---

## 4. `login.ts`/`login.html` (yeni) — dünkü reactive form bilgisinin tekrar kullanımı

```typescript
import { Component, signal } from '@angular/core';
// Component -- bu sınıfı bir Angular component'i olarak işaretleyen
// decorator (Day 89'dan beri tanıdık).
// signal -- hatalı giriş mesajını tutacağımız, değişince ekranı GÜNCELLEYEN
// özel değişken türü.

import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
// FormControl/FormGroup/Validators -- Day 92'de "yeni iş emri" formunda
// kullandığımız AYNI reactive forms araçları.

import { Router } from '@angular/router';
// Router -- başarılı girişten sonra kullanıcıyı başka bir SAYFAYA (route'a)
// PROGRAMATİK olarak (bir tıklama olmadan) göndermek için.

import { AuthService } from '../auth.service';
// Bir önceki bölümde yazdığımız AuthService.

@Component({
  imports: [ReactiveFormsModule],
  // Bu component'in şablonunda [formGroup]/formControlName gibi reactive
  // forms direktiflerini kullanabilmek için gereken import.
  selector: 'app-login',
  // Bu component'i HTML'de <app-login /> olarak çağırmamızı sağlıyor.
  styleUrl: './login.css',
  templateUrl: './login.html',
})
export class Login {
// Login -- giriş ekranının TÜM mantığını (form, hata mesajı, submit işlemi)
// barındıran sınıf.

  protected readonly form = new FormGroup({
  // form -- bu ekranın TEK formunu temsil eden FormGroup nesnesi.
  // "protected" -- SADECE bu sınıf VE bu sınıfın kendi şablonu (login.html)
  // erişebilsin diye.
    employeeId: new FormControl<number | null>(null, { validators: [Validators.required] }),
    // employeeId adında, başlangıç değeri null olan, SADECE "boş
    // bırakılamaz" (Validators.required) kuralına sahip TEK bir alan.
  });
  protected readonly errorMessage = signal<string | null>(null);
  // errorMessage -- login BAŞARISIZ olursa gösterilecek metni tutan signal.
  // Başlangıçta null (yani hiç hata yok, hiçbir şey gösterilmiyor).

  constructor(
    private readonly authService: AuthService,
    private readonly router: Router,
  ) {}
  // constructor -- bu component oluşturulurken, Angular'ın DI sisteminden
  // GERÇEK bir AuthService VE GERÇEK bir Router örneği alıp, this.authService
  // / this.router olarak saklıyor.

  submit(): void {
  // submit -- kullanıcı "Login" butonuna bastığında (ya da Enter'a basınca)
  // ÇALIŞACAK metot. void -- hiçbir değer DÖNDÜRMÜYOR, sadece bir İŞLEM
  // yapıyor.

    if (this.form.invalid) {
    // form.invalid -- FormGroup'un İÇİNDEKİ TÜM FormControl'lerin
    // validator'larına göre OTOMATİK hesaplanan, "bu form şu an geçersiz
    // mi" diyen bir alan (burada: employeeId boşsa true olur).
      return;
      // Form geçersizse, METODUN BURADA bitmesini sağlıyoruz -- aşağıdaki
      // hiçbir satır ÇALIŞMIYOR. (Bugünkü UI zaten geçersizken butonu
      // devre dışı bırakıyor, ama Day 92'de öğrendiğimiz gibi, kod
      // tarafında da AYRICA kontrol etmek gerekiyor -- butona güvenmemek.)
    }
    const { employeeId } = this.form.getRawValue();
    // this.form.getRawValue() -- formun O ANKİ GERÇEK değerlerini,
    // { employeeId: <girilen sayı> } şeklinde düz bir nesne olarak döndürür.
    // const { employeeId } = ... -- bu nesnenin İÇİNDEKİ employeeId alanını
    // DOĞRUDAN, ayrı bir değişkene "açarak" alıyoruz -- buna "destructuring"
    // (yapı bozma/açma) deniyor; "const employeeId = sonuç.employeeId"
    // yazmanın kısa yolu.

    this.errorMessage.set(null);
    // Yeni bir deneme başlıyor -- varsa ÖNCEKİ hata mesajını temizliyoruz,
    // kullanıcı eski bir hatayı GÖRMEYE devam etmesin diye.

    this.authService.login(employeeId!).subscribe({
    // this.authService.login(employeeId!) -- bir önceki bölümde yazdığımız
    // GERÇEK login isteğini tetikliyoruz. employeeId! -- TypeScript'e
    // "bu değerin null OLMADIĞINDAN eminim" demenin kısa yolu (form
    // geçerliyse, zaten boş olamaz, ama tip sistemi bunu otomatik bilemiyor).
    // .subscribe({ next, error }) -- dün (Day 92) sadece "next" callback'ini
    // kullanmıştık (hata durumunu AÇIKÇA ele almamıştık, Day 92'nin
    // kaydedilmiş bir eksikliğiydi). Bugün o eksiği BURADA, ilk kez,
    // gerçekten kapatıyoruz -- geçersiz bir employeeId girilirse kullanıcı
    // artık gerçek bir hata mesajı görüyor.

      next: () => this.router.navigate(['/']),
      // next -- istek BAŞARILI olursa çalışacak fonksiyon. Kullanıcıyı ana
      // sayfaya (iş emri listesine) yönlendiriyoruz.

      error: () => this.errorMessage.set('Login failed — check the employee id.'),
      // error -- istek BAŞARISIZ olursa (örn. backend 404 dönerse) çalışacak
      // fonksiyon. errorMessage signal'ini GERÇEK bir metinle dolduruyoruz,
      // bu da login.html'deki @if bloğunun görünmesini tetikliyor.
    });
  }
}
```

**`login.html`'deki hata gösterimi (hatırlatma, Day 92'den tanıdık sözdizimi):**
```html
@if (errorMessage()) {
  <p>{{ errorMessage() }}</p>
}
```
`errorMessage()` — yine, signal bir fonksiyon olduğu için ÇAĞIRARAK okunuyor. `@if (errorMessage())` — signal'in GERÇEK değeri `null` DEĞİLSE (yani gerçek bir hata metni varsa) bu blok ekranda görünür.

---

## Regresyon (Day 94)

```
npx ng build               -> Başarılı, 288.32 kB
npx ng test --watch=false  -> 23/23 geçti (8 test dosyası)
dotnet build FieldOps.slnx -> 0 Hata, 0 Uyarı (backend'e hiç dokunulmadı)
```

## Canlı doğrulama

Her iki uygulama birlikte çalışırken:
```
curl http://localhost:5138/health/live  -> 200
curl http://localhost:4200/login        -> 200 (direkt URL, deep-link)
curl .../main.js içinde "Employee Id", "Login failed" bulundu
```
**Dürüst sınırlama (tekrar):** Gerçek tarayıcıda login akışının TAMAMINI (form doldurma, token alma, sonraki isteğe `Authorization` header'ının GERÇEKTEN eklendiğini Network sekmesinde görme) izleyemedim — bu, `authInterceptor`'ın kendi birim testleriyle (sahte `AuthService`, gerçek `HttpTestingController`) doğrulandı, ki bu Day 91'in zoneless dersinden sonra ARTIK güvenilir bir doğrulama yöntemi (Day 91'de signal düzeltmesiyle hem gerçek tarayıcı hem test ortamı aynı anda düzelmişti).

## Demo basitleştirmesi vs. üretim gereksinimi

* Token SADECE bellekte — sayfa yenilenince kayboluyor, her seferinde yeniden giriş gerekiyor.
* Girişten sonra bile `X-Organization-Id`/`X-Employee-Id` sabit kalıyor — backend hâlâ bunları okuyor.
* Hiçbir "route guard" yok — login yapmamış biri de tüm ekranları görebiliyor, sadece isteklere token EKLENMİYOR (backend bunu henüz kontrol etmediği için bunun bugün bir etkisi yok).
