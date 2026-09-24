# Day 59 — Kod Notları

Faz 3, Hafta 11, Gün 59. Konu: **Docker Compose** — FieldOps.Api + SQL Server + Redis'in **hepsini** container'da, aynı ağda birlikte ayağa kaldırmak. Dünkü (Day 58) çözülmemiş sorunu (adlandırılmış SQL örneği + UDP keşif protokolünün container ağında çalışmaması) **gerçekten** çözdük — etrafından dolanmadık.

---

## 1. Gerçek problem

FieldOps'u tam olarak çalıştırmak, üç ayrı, birbirinden habersiz parçayı elle yönetmeyi gerektiriyordu: Windows servisi olarak `SQLEXPRESS`, ayrı bir `docker run` ile Redis, ve (dünden beri) ayrı bir container'da API. Dün tam olarak bu parçalanmışlığın bedelini gördük — API container'ı, host'taki `SQLEXPRESS`'e hiç ulaşamadı.

---

## 2. `docker-compose.yml` — satır satır

```yaml
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: ${SA_PASSWORD}
    ports:
      - "14330:1433"
    networks:
      - fieldops

  redis:
    image: redis:7-alpine
    ports:
      - "63790:6379"
    networks:
      - fieldops

  fieldops-api:
    build:
      context: .
      dockerfile: Dockerfile
    depends_on:
      - sqlserver
      - redis
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__FieldOpsOrganizationsDb: "Server=sqlserver;Database=FieldOpsOrganizations;User Id=sa;Password=${SA_PASSWORD};TrustServerCertificate=True;"
      # ... diger 4 veritabani icin ayni sekilde
      Redis__ConnectionString: "redis:6379"
    ports:
      - "5190:8080"
    networks:
      - fieldops

networks:
  fieldops:
```

- **`services:`** — tanımladığımız her container'a bir isim (`sqlserver`, `redis`, `fieldops-api`) veriyoruz. **Bu isimler, aynı zamanda o container'ın ağdaki DNS adı** — `fieldops-api`, `Server=sqlserver` yazdığında, Docker'ın kendi iç DNS'i bunu otomatik olarak `sqlserver` container'ının gerçek IP'sine çeviriyor. Dünkü `host.docker.internal` gibi bir köprüye hiç gerek yok, çünkü artık her iki taraf da **aynı** Docker ağının içinde.
- **`image: mcr.microsoft.com/mssql/server:2022-latest`** — SQL Server'ın resmi **Linux** container image'ı. Önemli bir gerçek: bu image **sadece SQL Authentication** destekliyor — Windows Authentication (Trusted_Connection) Linux'ta hiç yok. Bu yüzden bir kullanıcı adı/şifre **zorunlu**.
- **`environment: ACCEPT_EULA: "Y"`** — Microsoft'un lisans sözleşmesini otomatik kabul ediyoruz (bu image'ın kendi gereksinimi, "Y" olmadan başlamaz).
- **`MSSQL_SA_PASSWORD: ${SA_PASSWORD}`** — `sa` (system administrator) hesabının şifresi. **`${SA_PASSWORD}`** — bu dosyada **hiç yazılı değil**, bir `.env` dosyasından (Docker Compose'un otomatik okuduğu, git'e **hiç commit edilmeyen** bir dosya) geliyor. Bu, Day 57'nin "gerçek bir sır asla dosyaya yazılmaz, ortam değişkeniyle enjekte edilir" dersinin **ilk kez gerçek bir sırla** uygulanması — bugüne kadar FieldOps'ta gerçek bir sır yoktu (Windows Auth), şimdi var.
- **`ports: - "14330:1433"`** — host'un `14330` portunu container'ın `1433`'üne (SQL Server'ın standart portu) bağlıyor. Farklı bir host portu seçtik (`14330`) ki, senin zaten çalışan yerel `SQLEXPRESS`'inle **çakışmasın**.
- **`networks: - fieldops`** — her üç servisi de aynı, kendi tanımladığımız `fieldops` ağına koyuyoruz (dosyanın sonundaki `networks: fieldops:` bu ağı tanımlıyor). Aynı ağdaki servisler birbirini isimle bulabiliyor.
- **`fieldops-api.build: context: . / dockerfile: Dockerfile`** — bu servis, hazır bir image çekmek yerine, **dünkü `Dockerfile`'ımızı kullanarak inşa ediliyor**. Yani Day 58'in tüm çalışması, bugünün temeli.
- **`depends_on: [sqlserver, redis]`** — Compose'a "önce `sqlserver` ve `redis`'i başlat, sonra `fieldops-api`'yi başlat" diyor. **Dikkat:** bu sadece **başlatma sırasını** garanti ediyor, "SQL Server tamamen hazır" garantisini **vermiyor** — SQL Server'ın konteyneri "started" olsa da, içindeki SQL Server motorunun gerçekten istek kabul etmeye hazır olması birkaç saniye daha sürebilir (bugün canlı olarak gördük).
- **`ConnectionStrings__FieldOpsOrganizationsDb: "Server=sqlserver;..."`** — `Server=sqlserver` — artık `localhost` ya da `host.docker.internal` değil, doğrudan **servis adı**. Adlandırılmış örnek yok (`\SQLEXPRESS` eki yok), çünkü bu container'daki SQL Server'ın **tek** bir örneği var, varsayılan portunda (`1433`) dinliyor — dünkü UDP keşif adımına hiç gerek kalmadı.

---

## 3. `.env` / `.env.example` — sırrı dosyadan uzak tutmak

```
# .env.example (git'e commit edilir, sablon)
SA_PASSWORD=Change_Me_Please1
```
```
# .env (git'e ASLA commit edilmez, .gitignore'da)
SA_PASSWORD=FieldOps_Day59_Local!
```

Docker Compose, `docker-compose.yml` ile **aynı dizindeki** bir `.env` dosyasını **otomatik olarak** okuyup `${SA_PASSWORD}` gibi ifadeleri değiştiriyor — hiçbir ekstra bayrak (`--env-file`) gerekmeden. `.env.example`, gerçek değeri **olmayan**, sadece "bu dosyanın şekli böyle olmalı" diyen bir şablon — yeni bir geliştirici repo'yu klonladığında, `.env.example`'ı `.env`'e kopyalayıp kendi değerini yazması gerektiğini anlıyor.

**Gerçek bir `.gitignore` hatası, canlı bulunup düzeltildi:** `.gitignore`'daki `.env.*` deseni, **`.env.example`'ı da** yanlışlıkla yok sayıyordu (istemeden) — `git status` çalıştırınca `.env.example`'ın hiç görünmediğini fark ettim, `git check-ignore -v .env.example` ile doğruladım. Çözüm: `.gitignore`'a `!.env.example` (bir "istisna" kuralı) eklemek — `.env.*` deseninin **sadece** bu bir dosyayı hariç tutmasını sağlıyor, `.env`'in kendisi hâlâ tamamen yok sayılıyor.

---

## 4. Canlı kanıt — dünkü sorunun gerçekten çözüldüğü

```
1) docker compose up -d --build -> uc servis de basladi (sqlserver, redis, fieldops-api)

2) SQL Server loglarinda: "Login failed... Failed to open the explicitly
   specified database 'FieldOpsOrganizations'"
   -> Bu ONEMLI bir kanit: kimlik dogrulama BASARILI oldu (sa/sifre dogru,
      aga baglanti calisti) — sadece veritabani henuz olusturulmadigi icin
      "USE" basarisiz. Dunku UDP/network sorunuyla HICBIR ilgisi yok.

3) 5 modul icin de "dotnet ef database update --connection Server=localhost,14330;..."
   calistirildi -> hepsi basarili.

4) GET /health/ready:
   {"status":"Healthy","checks":[
     {"name":"redis","status":"Healthy",...},
     {"name":"workorders-db","status":"Healthy",...}
   ]}
   -> Dunku "Unhealthy" tamamen "Healthy"ye donustu.

5) GET /api/organizations -> [{"id":1,"name":"Acme Field Services"},
   {"id":2,"name":"Blue Ridge Maintenance"}] (seed verisi, dogru).

6) Tam bir is emri yasam dongusu + rapor uc noktasi test edildi:
   POST /api/workorders -> basarili (Employees + WorkOrders DB birlikte calisti)
   GET /api/workorders/report -> {"open":1,...} (Redis cache-aside calisti)
```

Bu, sadece "bağlantı kuruldu" değil, **tüm sistemin** (5 veritabanı + Redis + çapraz-modül orkestrasyon + cache) container'lı haliyle uçtan uca çalıştığının kanıtı.

---

## 5. Demo basitleştirmesi vs. üretim gereksinimi

- `SA_PASSWORD` bugün de basit bir yerel demo değeri — ama artık **doğru yerde** (ortam değişkeni, `.env` ile, git'e hiç girmeden), gerçek üretimde olması gerektiği gibi.
- Migration'lar bugün **elle**, host'tan `dotnet ef database update` ile uygulandı. Gerçek bir CI/CD hattında bu adım otomatikleştirilir (bir "migration runner" adımı/container'ı) — bugün bilinçli olarak elle yapıldı, otomasyon Day 60'ın (CI) konusu olabilir.
- Compose yığını, doğrulama bitince **kapatıldı** (`docker compose down`) — bu, senin standart yerel geliştirme kurulumunun (yerel `SQLEXPRESS` + bağımsız `fieldops-redis` container'ı) yerini almıyor, sadece "her şeyi container'da birlikte çalıştırmak mümkün ve doğru" kanıtlandı.

---

## Regresyon

```
dotnet test FieldOps.slnx    → 49/49 (normal yerel kurulumla, Compose'dan bagimsiz)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```
