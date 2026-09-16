# Day 30 — Kod Notları

Faz 2, Hafta 6, Gün 30. Konu: GitHub Actions (CI).

Bu doküman, bugün eklenen kodu **yazılma sırasına göre** gezer.

---

## 1. `.github/workflows/ci.yml` — yeni dosya

```yaml
name: CI

on:
  push:
    branches: [ master ]
  pull_request:
    branches: [ master ]
```

**Neden:** `on:` bölümü, bu workflow'un **ne zaman** çalışacağını belirliyor — `master` dalına her `push` yapıldığında ve `master`'a açılan her pull request'te. `name: CI`, GitHub'ın arayüzünde bu workflow'u tanıyacağımız isim.

```yaml
jobs:
  build-and-test:
    runs-on: ubuntu-latest
```

**Neden:** `runs-on: ubuntu-latest` — bu işin, GitHub'ın **kendi**, bizim bilgisayarımızdan tamamen bağımsız, her seferinde sıfırdan hazırlanan bir Ubuntu Linux sunucusunda çalışacağını söylüyor. Bu önemli: "benim makinemde çalışıyor" ile "herhangi bir temiz makinede çalışıyor" farklı iddialar (Day 10/22'de "temiz checkout'tan derleme" ile aynı prensip, ama bu sefer **her push'ta otomatik**).

```yaml
    steps:
      - uses: actions/checkout@v4
```

**Neden:** GitHub'ın sunucusu boş başlıyor — hiçbir kod yok. `actions/checkout@v4`, GitHub'ın hazır (bizim yazmadığımız) bir eylemi — repomuzu bu boş sunucuya indiriyor.

```yaml
      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
```

**Neden:** Ubuntu sunucusunda varsayılan olarak .NET SDK'sı kurulu değil. Bu adım, tam olarak bizim kullandığımız (.NET 10) sürümü kuruyor.

```yaml
      - name: Restore StockPilot
        run: dotnet restore StockPilot.slnx

      - name: Build StockPilot
        run: dotnet build StockPilot.slnx --no-restore

      - name: Test StockPilot
        run: dotnet test StockPilot.slnx --no-build
```

**Neden ayrı ayrı üç adım:** Her adım, GitHub'ın arayüzünde **ayrı ayrı** görünüyor — bir şey başarısız olursa, tam olarak hangi aşamada (restore mu, build mi, test mi) olduğunu hemen görebiliyoruz. `--no-restore`/`--no-build`, önceki adımın işini tekrar etmemek için — her komut, kendi bilgisayarımızda olduğu gibi çalışıyor, sadece GitHub'ın sunucusunda.

**Kritik bir nokta — hiçbir SQL Server kurulum adımı yok:** StockPilot'un entegrasyon testleri (Day 27-28), kendi izole SQL Server konteynerlerini **kendileri** Testcontainers ile başlatıyor. GitHub'ın `ubuntu-latest` sunucularında Docker zaten kurulu geliyor — bu yüzden CI için **hiçbir ek veritabanı kurulumuna gerek yok**. Bu, Day 28'in çalışmasının doğrudan bir ödülü: eğer testler hâlâ gerçek `localhost\SQLEXPRESS`'e bağımlı olsaydı, bu CI kesinlikle çalışmazdı (GitHub'ın sunucusunda böyle bir şey yok) — o zaman bu workflow'a ayrıca bir "SQL Server servisini başlat" adımı eklememiz gerekirdi.

Aynı desen `RoadmapOS.slnx` için de tekrarlanıyor (Restore/Build/Test) — o proje zaten hiç veritabanına bağımlı testler içermiyor (`ProgressCalculatorTests`, saf mantık testleri).

---

## Bağımsız görev — test sonuçlarını artifact olarak yükleme (birlikte yapıldı)

```yaml
- name: Test StockPilot
  run: dotnet test StockPilot.slnx --no-build --logger trx --results-directory ./TestResults

- name: Upload StockPilot test results
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: stockpilot-test-results
    path: ./TestResults
```

- `--logger trx --results-directory ./TestResults` — `dotnet test`'e, sonuçları ayrıca `.trx` formatında bir dosyaya da yazmasını söylüyor.
- `if: always()` — önceki adım (testler) başarısız olsa bile bu adımın çalışmasını garanti ediyor; normalde bir adım başarısız olunca sonraki adımlar atlanır.
- `uses: actions/upload-artifact@v4` — bu dosyaları GitHub'a, çalışmanın sonunda indirilebilir bir "artifact" olarak yüklüyor.

Berkan, syntax'ı adım adım (`--logger`/`--results-directory` satırı, sonra `upload-artifact` bloğu) doğru şekilde yazıp iki ayrı commit'te push etti.

**Canlı kanıt:**
```
GET /repos/.../actions/runs/35129881259/artifacts
total_count: 1
name: stockpilot-test-results, size_bytes: 6374, expires_at: 2026-12-15
```

Gerçek, indirilebilir bir artifact üretildi — bağımsız görev tamamen doğrulandı.

---

## Canlı kanıt

**Run #1 — ilk push:**
```
GET /repos/.../actions/runs/35128112643
status: completed, conclusion: success

Adim adim (jobs/steps):
  Restore StockPilot -> success
  Build StockPilot   -> success
  Test StockPilot    -> success   ← Testcontainers'li entegrasyon testleri DAHIL,
                                     hicbir ek SQL Server kurulum adimi olmadan
  Restore RoadmapOS  -> success
  Build RoadmapOS    -> success
  Test RoadmapOS     -> success
```

**Run #2 — CI'ı bilerek kırmızıya döndürme:**

`ProductsControllerMockingTests.cs`'teki assertion geçici olarak bozuldu:
```csharp
Assert.IsType<OkObjectResult>(result.Result); // yanlış, olması gereken ConflictObjectResult
```
Yerel olarak da, GitHub Actions'ta da **gerçekten** başarısız oldu:
```
GET /repos/.../actions/runs/35128432423
status: completed, conclusion: FAILURE
```

**Run #3 — düzeltme geri push edildi:**
```
GET /repos/.../actions/runs (en son)
sha: 075b8ab7
status: completed, conclusion: success
```

**Sonuç:** Üç aşamalı tam bir Red→Green döngüsü, bu sefer **kendi bilgisayarımızda değil, gerçekten GitHub'ın sunucusunda** kanıtlandı: yeşil → bilerek kırmızı → tekrar yeşil. Bundan sonra `master`'a her push'ta bu otomatik olarak, kimse elle bir şey yapmadan çalışacak.

```
dotnet test (StockPilot, yerel) → 27/27 (degisiklik yok)
dotnet test (RoadmapOS, yerel)  → 8/8 (etkilenmedi)
```
