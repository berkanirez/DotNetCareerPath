# Hafta 18 Özeti — Angular Frontend (Gün 89-96)

Bu hafta, FieldOps'un ilk kez bir web arayüzünü (`src/fieldops-web`, Angular) sıfırdan kurduk. Bu dosya, sıfır frontend bilgisiyle "geçen hafta ne yaptık" sorusuna cevap vermek için yazıldı — satır satır kod açıklaması için ilgili `day-89.md`...`day-96.md` dosyalarına bak.

---

## 1. Büyük resim: ne inşa ettik

5 tane EKRAN (Angular'da "component" deniyor) birbirine bağlandı:

| Ekran | Adres (URL) | Ne yapıyor |
|---|---|---|
| İş emri listesi | `/` | Tüm iş emirlerini tablo hâlinde gösterir |
| İş emri detayı | `/work-orders/28` gibi | Tek bir iş emrinin detayını gösterir |
| Yeni iş emri | `/work-orders/new` | Form ile yeni iş emri oluşturur |
| Giriş (login) | `/login` | Çalışan ID'si ile "giriş" yapar, bir token alır |
| Dashboard | `/dashboard` | Open/Assigned/InProgress/Completed SAYILARINI gösterir |

Hepsi, GERÇEK `FieldOps.Api`'ye (backend'e) bağlı — sabit/uydurma veri YOK, hepsi gerçek SQL Server verisi.

---

## 2. Genel akış — her ekran aynı 3 katmanı kullanıyor

```
Ekran (component)  →  Servis (service)  →  Backend (FieldOps.Api)
  "ne zaman,            "GERÇEK HTTP        gerçek veri
   ne isteyeceğim"       isteğini nasıl      /SQL Server
                         atacağım"
```

Ekranlar ASLA doğrudan `http.get(...)` yazmıyor — her zaman bir SERVİS'e sorup, cevabı bekliyor. Bunun sebebi: aynı servis BİRDEN FAZLA ekran tarafından paylaşılıyor, böylece backend adresi/header'ları TEK bir yerde yazılı kalıyor (4-5 ayrı dosyada tekrarlanmıyor).

---

## 3. Dosya dosya — hangisi ne işe yarıyor

**Uygulamanın iskeleti:**
- `app.ts` / `app.html` — en dıştaki "çerçeve", içine o an hangi ekran aktifse ONU yerleştiriyor (`<router-outlet />`).
- `app.routes.ts` — "hangi adres (URL), hangi ekranı açacak" listesi.
- `app.config.ts` — uygulamanın başında bir kere kurulan genel ayarlar (HTTP sistemi, routing sistemi, token ekleyen "interceptor").

**Veri şekilleri (sadece backend'in gönderdiği JSON'un TypeScript karşılığı, hiçbir mantık içermiyor):**
- `work-order.ts`, `work-order-status-report.ts`, `login-response.ts`

**Servisler (gerçek HTTP işini yapan katman):**
- `work-order.service.ts` — iş emirleriyle ilgili HER ŞEY (listele, tekini getir, yeni oluştur, raporu getir) burada.
- `auth.service.ts` — giriş yapma VE giriş bilgisini (token, rol) saklama burada.

**Ekranlar (kullanıcının GÖRDÜĞÜ şeyler):**
- `work-order-list/` — liste ekranı.
- `work-order-detail/` — detay ekranı.
- `work-order-create/` — yeni kayıt formu.
- `work-order-dashboard/` — özet sayılar.
- `login/` — giriş formu.

**Token'ı otomatikleştiren özel parça:**
- `auth.interceptor.ts` — AŞAĞIDA detaylı anlatılıyor.

---

## 4. JWT / Rol sistemi — token'ı NASIL taşıdık (sıfırdan anlatım)

**Önce, token denen şey ne:** Giriş yapınca backend bize uzun, imzalı bir METİN (JWT) veriyor — bu metin, "ben kimim, hangi role sahibim" bilgisini TAŞIYOR. Bu metni her sonraki istekte GERİ göndermemiz gerekiyor, yoksa backend bizi "tanımıyor" (şu anki hâliyle backend bunu henüz hiç ZORUNLU kılmıyor, ama biz yine de gönderiyoruz — ileride zorunlu olacak).

**Adım adım, token'ın yolculuğu:**

1. **Kullanıcı `/login`'de formu doldurur** (`login/login.ts`) → "Login" butonuna basar.
2. **`AuthService.login(employeeId)` çağrılır** (`auth.service.ts`) → GERÇEK bir `POST /api/auth/login` isteği backend'e gider.
3. **Backend cevap verir:** `{ token: "eyJhbG...", role: "Admin", ... }` — GERÇEK, imzalı bir JWT ve kullanıcının rolü.
4. **`AuthService`, bu token'ı VE rolü KENDİ İÇİNDE saklar** (iki ayrı `signal` — Angular'ın "değişince otomatik haber veren kutu" dediği özel değişken türü). Bu, SADECE tarayıcının o anki belleğinde duruyor — sayfa yenilenince (F5) KAYBOLUYOR (bilinçli bir basitleştirme, kalıcı saklama ileride).
5. **Kullanıcı başka bir ekrana gider** (örn. iş emri listesi) → `WorkOrderList`, `WorkOrderService.getAll()`'u çağırır → bu, GERÇEK bir `http.get(...)` isteği başlatır.
6. **TAM BU NOKTADA `auth.interceptor.ts` devreye girer** — bu, HER giden isteği (hangi ekrandan gelirse gelsin) otomatik olarak "yakalayan" özel bir fonksiyon. Şunu yapıyor:
   - `AuthService`'ten saklı token'ı okur.
   - Token VARSA, isteğe `Authorization: Bearer eyJhbG...` diye bir header EKLER (isteği hiçbir ekranın/servisin elle yapması GEREKMİYOR — otomatik).
   - Token YOKSA, isteği olduğu gibi bırakır.
7. **İstek, token'ı taşıyarak backend'e ulaşır.**

**ÇOK ÖNEMLİ, son bir not:** Backend bugünkü hâliyle bu token'ı HİÇ KONTROL ETMİYOR — sadece eski `X-Organization-Id`/`X-Employee-Id` header'larına bakıyor (onlar da hâlâ sabit/elle gönderiliyor). Yani şu an token GERÇEKTEN gidiyor ama hiçbir şeyi DEĞİŞTİRMİYOR — altyapı hazır, ama "zorunlu kılma" kısmı henüz yapılmadı.

**Rol sistemi (Admin/Member) nasıl kullanıldı:** `AuthService`, token'ın yanında gelen rolü de sakladı (`isAdmin()` metodu). `WorkOrderList` ekranı, "+ New Work Order" linkini SADECE `isAdmin()` `true` dönerse gösteriyor (`@if` ile). Bu da SADECE bir arayüz kolaylığı — backend hâlâ rol kontrolü yapmadığı için, bir Member istese GERÇEKTEN de yeni iş emri oluşturabilir, sadece linki GÖRMÜYOR.

---

## 5. Bu hafta canlı yakalanan iki gerçek hata (kısaca)

- **Gün 91:** Veri geliyordu ama ekranda görünmüyordu — sebep, bu projenin "zoneless" Angular kullanması (eski otomatik ekran-yenileme mekanizması yok), çözüm `signal` kullanmaktı.
- **Gün 92:** Yeni iş emri oluşturma çok yavaştı — sebep kod değil, senin bilgisayarındaki Redis container'ının kapalı olmasıydı.
