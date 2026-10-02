# Day 97 — Kod Notları

Faz 5, **Hafta 19'un ilk günü**. Konu: **Angular için multi-stage Dockerfile**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Hatırlarsan:** `FieldOps.Api`'nin ZATEN bir Dockerfile'ı vardı (Day 58). Ama `fieldops-web` (Angular) hiç container'a alınmamıştı — çünkü Faz 5'e kadar hiç yoktu.

**Bugün neden farklı bir mantık gerekiyor:** `FieldOps.Api`'nin Dockerfile'ı, container İÇİNDE SÜREKLİ ÇALIŞAN bir .NET process üretiyor (`dotnet FieldOps.Api.dll`). Angular'ın "çalışması" dediğimiz şey ise aslında HİÇ process DEĞİL — sadece HTML/CSS/JS dosyalarının bir web sunucusu tarafından SERVİS EDİLMESİ. Yani bugünkü Dockerfile'ın son hâli, .NET/Node DEĞİL, sadece "dosyaları servis eden" hafif bir web sunucusu (**nginx**) içeriyor.

---

## 1. `Dockerfile` (yeni) — iki aşama

```dockerfile
FROM node:24-alpine AS build
# FROM ... AS build -- bu, İLK aşamanın adı "build". node:24-alpine --
# Node.js'in GERÇEKTEN kurulu olduğu, ama "alpine" (çok küçük bir Linux
# dağıtımı) tabanlı, hafif bir imaj. Bu imaj, SADECE Angular'ı DERLEMEK için
# var -- SON ürüne HİÇ girmeyecek (aşağıda görülecek).
WORKDIR /app
# Container İÇİNDEKİ, bundan sonraki tüm komutların çalışacağı klasör.

COPY package.json package-lock.json ./
# SADECE bu iki dosyayı kopyalıyoruz -- henüz GERÇEK kaynak kodu DEĞİL.
# Bunun sebebi: Docker, her satırı bir "katman" (layer) olarak CACHE'liyor.
# Eğer kaynak kodu DEĞİŞİRSE ama package.json DEĞİŞMEZSE, aşağıdaki
# "npm ci" adımı YENİDEN ÇALIŞTIRILMAZ -- önceki build'den HATIRLANIR. Bu,
# FieldOps.Api'nin Dockerfile'ındaki "önce .csproj kopyala, dotnet restore
# yap" deseninin BİREBİR AYNISI.
RUN npm ci
# npm ci -- node_modules'ı package-lock.json'daki TAM sürümlerle, SIFIRDAN
# kurar (npm install'dan farkı: daha HIZLI ve daha KESİN, CI/CD ortamları
# için önerilen komut).

COPY . .
# ŞİMDİ geri kalan TÜM kaynak kodu kopyalıyoruz (.dockerignore'daki
# node_modules/dist/.angular HARİÇ -- aşağıda açıklanıyor).
RUN npm run build
# package.json'daki "build" script'i (`ng build`) ÇALIŞIYOR -- GERÇEK
# Angular derlemesi, dün/önceki günlerde kendi bilgisayarımda yaptığım
# AYNI işlem, ama şimdi container İÇİNDE oluyor. Sonuç:
# /app/dist/fieldops-web/browser klasöründe GERÇEK HTML/CSS/JS dosyaları.

FROM nginx:alpine AS final
# İKİNCİ aşama, YEPYENİ, KÜÇÜK bir imajla BAŞLIYOR -- içinde Node YOK, npm
# YOK, kaynak kodu YOK. nginx:alpine -- statik dosya servis etmekte
# kullanılan, çok hafif bir web sunucusu.
COPY --from=build /app/dist/fieldops-web/browser /usr/share/nginx/html
# --from=build -- ÖNCEKİ aşamadan (build) dosya kopyalıyoruz, YENİDEN
# DERLEMEDEN. SADECE derlenmiş SONUÇ (dist/fieldops-web/browser) bu son
# imaja giriyor -- Node'un kendisi, node_modules, kaynak kodu HİÇBİRİ bu
# imajda YOK. /usr/share/nginx/html -- nginx'in VARSAYILAN olarak statik
# dosya aradığı klasör.
```

---

## 2. `.dockerignore` (yeni)

```
node_modules
dist
.angular
```
`.gitignore`'a BENZER bir dosya ama Docker için — bu klasörler, `COPY . .` komutuna HİÇ dahil EDİLMİYOR. `node_modules`'ı HARİÇ TUTMAK özellikle önemli: kendi bilgisayarımdaki `node_modules` (Windows için derlenmiş bazı paketler İÇEREBİLİR) container'ın İÇİNE (Linux) sızarsa, uyumsuzluk hatalarına yol açabilir -- container'ın KENDİ `npm ci`'siyle TEMİZ bir `node_modules` kurması gerekiyor.

---

## Regresyon (Day 97)

```
npx ng test --watch=false  -> 30/30 geçti (Angular kaynak kodu hiç değişmedi, sadece Dockerfile eklendi)
dotnet build FieldOps.slnx -> 0 Hata, 0 Uyarı (backend'e hiç dokunulmadı)
```

## Canlı doğrulama — GERÇEK container, GERÇEK sonuç

```
docker build -t fieldops-web:day97 .   -> başarılı, imaj boyutu 94MB
docker run -d -p 8080:80 ...            -> container ayağa kalktı
curl http://localhost:8080/             -> 200, GERÇEK Angular index.html'i (<app-root> içeriyor)
curl http://localhost:8080/main-....js  -> 200, GERÇEK derlenmiş JS dosyası
curl http://localhost:8080/work-orders/28 -> 404  <-- BEKLENEN, aşağıda açıklanıyor
```

**Bulunan, CANLI doğrulanmış bir sınırlama (plandaki tahminim doğru çıktı):** Angular'ın client-side routing'i (Day 91'den hatırlarsan, `/work-orders/28` gibi bir adrese DOĞRUDAN gidince bile doğru ekranın açılması gerekiyordu) bu container'da ÇALIŞMIYOR — nginx, `/work-orders/28` diye GERÇEK bir DOSYA/klasör ARIYOR, bulamayınca 404 veriyor. Sebep: nginx'e "bulamadığın her adres için, onun yerine index.html'i gönder, Angular'ın KENDİ Router'ı GERÇEK adrese göre doğru ekranı açsın" diye bir KURAL (genelde `try_files` ile) henüz eklemedik. Bu, YARININ (ya da bu haftanın ileriki bir gününün) konusu.

## Demo basitleştirmesi vs. üretim gereksinimi

* Bugün SADECE `fieldops-web`'i KENDİ BAŞINA container'a aldık — `docker-compose.yml`'e hiç eklemedik (backend'le birlikte orkestrasyon ayrı bir gün).
* nginx, varsayılan ayarlarıyla — client-side routing fallback'i (`try_files`) YOK, yukarıda canlı kanıtlandığı gibi.
* Backend'in GERÇEK adresi (`http://localhost:5138`) Angular kodunun İÇİNE hâlâ sabit yazılı — container'lar birbirini bu adresle BULAMAZ (gerçek bir docker-compose kurulumunda servis adları kullanılırdı, `FieldOps.Api`'nin kendi Dockerfile'ında `sqlserver`/`redis` gibi).
