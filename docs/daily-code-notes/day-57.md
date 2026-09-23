# Day 57 — Kod Notları

Faz 3, Hafta 11, Gün 57. Konu: **Configuration & Environment Management**. Bugün yeni bir "özellik" eklemedik — .NET'in **zaten var olan** konfigürasyon sistemini derinlemesine anlayıp, ilk kez ortama göre gerçekten farklı davranan bir uygulama kurduk.

---

## 1. Gerçek problem

FieldOps'un tüm konfigürasyonu tek bir dosyada (`appsettings.Development.json`) yaşıyordu — "dev nasıl davranır" ile "üretim nasıl davranır" arasında hiçbir ayrım yoktu. Docker/Kubernetes'e (bu haftanın ve gelecek haftanın konusu) girdiğimizde bu **zorunlu** bir gereksinim — bir container image'ını sadece bir ayarı değiştirmek için yeniden derlemek istemezsin.

---

## 2. Zihinsel model — katmanlı konfigürasyon

.NET'in konfigürasyon sistemi bir **yığın** — her katman, altındakini aynı anahtar için **ezebilir**:
```
appsettings.json (temel, her ortamda ortak)
  → appsettings.{ASPNETCORE_ENVIRONMENT}.json (ortama özel)
    → gercek OS ortam degiskenleri
      → komut satiri argumanlari (en yuksek oncelik)
```
Bu, senin Node.js'teki `.env` dosyalarıyla aynı fikir — sadece daha fazla katman ve net bir öncelik sırası. Container dünyasında en kritik katman **ortam değişkenleri**: bir orkestratör, imaja hiç dokunmadan, sadece environment variable enjekte ederek konfigürasyonu değiştirebiliyor.

---

## 3. `appsettings.Production.json` — yeni dosya

```json
{
  "Logging": { "LogLevel": { "Default": "Warning", ... } },
  "ConnectionStrings": { /* ayni, yerel demo altyapisi */ },
  "Redis": { "ConnectionString": "localhost:6379" },
  "RateLimiting": {
    "PerOrganization": { "PermitLimit": 100, "WindowSeconds": 60 }
  }
}
```

İki gerçek fark var `appsettings.Development.json`'a kıyasla:
- **`RateLimiting`**: Development'ta `5/10sn` (gözle görülür olsun diye küçük tutulmuştu — Day 53), Production'da `100/60sn` (gerçekçi bir üretim değeri).
- **`Logging:LogLevel:Default`**: Development'ta `"Information"`, Production'da `"Warning"` — üretimde genelde daha az ayrıntılı loglama tercih edilir (disk/log toplama maliyeti).

**Hiçbir kod değişikliği gerekmedi** — `Program.cs`'teki `builder.Configuration.GetValue("RateLimiting:PerOrganization:PermitLimit", 5)` (Day 53) zaten hangi dosyanın yüklendiğinden habersiz, sadece "bu anahtarın son kazanan değeri ne" diye soruyor. `ASPNETCORE_ENVIRONMENT` değişince, .NET otomatik olarak doğru `appsettings.{Environment}.json`'ı yükleyip üzerine yazıyor.

---

## 4. Canlı kanıt 1 — ortam gerçekten davranışı değiştiriyor

```
ASPNETCORE_ENVIRONMENT=Production ile baslatildi:

1) GET /openapi/v1.json -> 404
   (Program.cs'teki "if (app.Environment.IsDevelopment()) { MapOpenApi(); }"
    kontrolu sayesinde — bu kontrol zaten Day 11'den beri vardi, bugun ilk
    kez GERCEKTEN Production ortaminda calistirilip kanitlandi.)

2) Org 1 icin 6 hizli POST /api/workorders -> HEPSI 201
   (Development'in 5/10sn limiti olsaydi 6. istek 429 alirdi;
    Production'in 100/60sn limiti sayesinde hicbiri tetiklenmedi.)
```

**Önemli bir engel, canlı çözüldü:** `dotnet run`, varsayılan olarak `launchSettings.json`'daki profili kullanıyor — bu profil `ASPNETCORE_ENVIRONMENT=Development`'ı **kendi içinde** zaten tanımlı olduğu için, shell'de elle set ettiğim ortam değişkenini **eziyordu** (ilk denemede "Hosting environment: Development" gördüm, `Production` değil — beklenmedik bir sonuç, hemen düzeltildi). Çözüm: `dotnet run --no-launch-profile` — bu, `launchSettings.json`'ı tamamen devre dışı bırakıp gerçekten benim set ettiğim ortam değişkenini kullanmasını sağladı.

---

## 5. Canlı kanıt 2 — gerçek bir OS ortam değişkeniyle tek bir ayarı ezmek

```
Development ortaminda, ama SU env var ile baslatildi:
  RateLimiting__PerOrganization__PermitLimit=2

Org 2 icin 3 hizli POST /api/workorders:
  istek 1 -> 201
  istek 2 -> 201
  istek 3 -> 429   <- dosyadaki "5" DEGIL, env var'daki "2" kullanildi!
```

**`__` (çift alt çizgi) neden:** Çoğu işletim sisteminde ortam değişkeni adlarında `:` (iki nokta) kullanılamaz/sorunlu. .NET'in konfigürasyon sistemi, `RateLimiting__PerOrganization__PermitLimit` gibi bir env var adını otomatik olarak `RateLimiting:PerOrganization:PermitLimit` anahtarına eşliyor — hiçbir ekstra kod yazmadan.

**Bu, aslında hiç yeni bir mekanizma değil:** `FieldOpsApiFactory.ConfigureWebHost`'un Day 48'den beri yaptığı `builder.UseSetting("ConnectionStrings:FieldOpsOrganizationsDb", ...)` çağrıları, tam olarak **aynı** konfigürasyon sisteminin bir başka giriş noktası — sadece testler için, gerçek bir OS ortam değişkeni yerine kod içinden ayarlanıyor. Bugün, aynı mekanizmayı **gerçek bir ortam değişkeniyle**, test altyapısı olmadan kanıtladık.

---

## 6. Demo basitleştirmesi vs. üretim gereksinimi

- FieldOps'ta şu an gerçek bir "sır" (şifre, gizli anahtar) yok — Windows Authentication kullanılıyor, connection string'lerde hiç parola yok. Gerçek üretimde bir SQL şifresi/Redis şifresi olsaydı, bunlar **asla** `appsettings.json`'a yazılmazdı — tam olarak bugün kanıtladığımız mekanizmayla (ortam değişkenleri) ya da bir sır deposuyla (Azure Key Vault, Kubernetes Secrets) enjekte edilirdi. Bugün uydurma bir sır göstermedik, sadece mekanizmayı gerçek, var olan bir ayarla (rate limit) kanıtladık.
- `appsettings.Production.json`'daki connection string'ler bugün hâlâ aynı yerel `SQLEXPRESS`/Redis'e işaret ediyor — gerçek bir üretim ortamında bunlar tamamen farklı (gerçek, uzak) sunuculara işaret ederdi.

---

## Regresyon

```
dotnet test FieldOps.slnx    → 49/49 (degismedi)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```
