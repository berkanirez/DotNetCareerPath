# Day 98 — Kod Notları

Faz 5, Hafta 19, Gün 98. Konu: **nginx'e client-side routing fallback'i eklemek**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Dünden hatırlarsan:** Container çalışıyordu, ana sayfa (`/`) doğru geliyordu, ama `/work-orders/28` gibi bir adrese DOĞRUDAN gidince `404` alıyorduk. Sebep: nginx, `/work-orders/28`'i GERÇEK bir dosya/klasör sanıp ARIYORDU, bulamayınca pes ediyordu — Angular'ın KENDİ Router'ı (Day 91) bu noktada hiç DEVREYE GİREMİYORDU, çünkü sayfa hiç yüklenmedi ki.

**Bugün ne yapıyoruz:** nginx'e "emin olamadığın HER adres için, en azından `index.html`'i gönder" diyoruz. Bu HTML yüklenince, İÇİNDEKİ Angular JavaScript'i çalışır, O da tarayıcının adres çubuğuna bakıp "ah, `/work-orders/28`'deyiz" diye doğru component'i KENDİSİ açar.

---

## 1. `nginx.conf` (yeni)

```nginx
server {
    listen 80;
    # Bu sunucu bloğu, container'ın 80 numaralı portunu DİNLİYOR.

    root /usr/share/nginx/html;
    # Statik dosyaların (index.html, main.js, vs.) bulunduğu GERÇEK klasör --
    # Day 97'de bu klasöre kopyaladığımız AYNI yer.
    index index.html;
    # "Bir klasör istenirse (örn. sadece '/'), VARSAYILAN olarak index.html'i
    # servis et" demenin GERÇEK karşılığı.

    location / {
    # location / -- GELEN HER istek (hangi adres olursa olsun) bu blok
    # tarafından ele alınıyor.
        try_files $uri $uri/ /index.html;
        # try_files -- nginx'e SIRAYLA üç şeyi DENEMESİNİ söylüyor:
        # 1. $uri -- istenen adrese TAM uyan bir DOSYA var mı (örn.
        #    main-....js diye GERÇEK bir dosya varsa, onu GÖNDER).
        # 2. $uri/ -- istenen adrese uyan bir KLASÖR var mı.
        # 3. İKİSİ DE yoksa (örn. /work-orders/28 -- böyle bir dosya/klasör
        #    HİÇ YOK), SON çare olarak /index.html'i GÖNDER -- 404 verme.
        # Bu SON adım, bugünkü asıl düzeltme -- dün bu satır YOKTU, nginx
        # üçüncü seçeneği hiç DENEMEDEN direkt 404 veriyordu.
    }
}
```

---

## 2. `Dockerfile` — bu config'i GERÇEKTEN kullanmak

```dockerfile
COPY nginx.conf /etc/nginx/conf.d/default.conf
# Bu satır, Day 97'nin Dockerfile'ına EKLENDİ -- nginx imajının KENDİ
# varsayılan config dosyasının (nginx:alpine imajında zaten var olan,
# try_files SATIRI OLMAYAN basit bir config) YERİNE, bizim yazdığımız
# nginx.conf'u KOYUYOR. /etc/nginx/conf.d/default.conf -- nginx'in kendi
# içinde, "site ayarlarını" aradığı GERÇEK, standart yol.
```

---

## Regresyon (Day 98)

```
(Angular/backend kaynak kodu HİÇ değişmedi -- sadece nginx config'i eklendi,
bu yüzden ng test/dotnet test bugün GEREKMİYOR, Docker-seviyesinde bir
değişiklik. Yine de hiçbir şeyin bozulmadığını canlı container ile doğruladım --
aşağıda.)
```

## Canlı doğrulama — dünkü 404'ün bugün GERÇEKTEN düzeldiği

```
docker build -t fieldops-web:day98 .   -> başarılı

curl http://localhost:8081/                  -> 200 (ana sayfa, hâlâ doğru)
curl http://localhost:8081/work-orders/28    -> 200  <-- DÜN 404'tü, BUGÜN düzeldi!
  (içerik GERÇEKTEN index.html -- "<app-root" bulundu)
curl http://localhost:8081/main-....js       -> 200 (GERÇEK statik dosya,
                                                       try_files bunu BOZMADI)
curl http://localhost:8081/login             -> 200
curl http://localhost:8081/dashboard         -> 200
curl http://localhost:8081/work-orders/new   -> 200
```
Tüm Angular route'ları (dün tanımladığımız 5 ekranın hepsi) artık container İÇİNDE, DOĞRUDAN URL ile ziyaret edildiğinde doğru çalışıyor — dünkü eksik, bugün TAM olarak kapatıldı.

## Demo basitleştirmesi vs. üretim gereksinimi

* `nginx.conf` SADECE routing fallback'ini çözüyor — gzip sıkıştırma, cache header'ları (`Cache-Control`), güvenlik header'ları (`X-Frame-Options`, vs.) gibi gerçek bir üretim config'inde olması beklenen başka birçok ayar bugün YOK, bilerek.
