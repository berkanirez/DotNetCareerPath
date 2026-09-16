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

## Canlı kanıt

Bu dosya, `git push` yapılmadan hiçbir şey ifade etmiyor — canlı kanıt, Berkan'ın push'undan sonra GitHub Actions sekmesinde gerçekleşecek. Sonraki bölüme bakınız.
