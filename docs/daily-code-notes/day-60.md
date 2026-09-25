# Day 60 — Kod Notları

Faz 3, Hafta 11, Gün 60 — **Week 11'in son günü**. Konu: **Continuous Integration** — dünkü (`Dockerfile`) ve önceki günkü (`docker-compose.yml`) çalışmanın, CI'da **gerçekten** doğrulanır hale getirilmesi.

---

## 1. Gerçek problem

`Dockerfile` (Day 58) ve `docker-compose.yml` (Day 59), artık bu repodaki **gerçek, çalışan** teslim edilebilirler. Ama `.github/workflows/ci.yml`, sadece `dotnet build`/`dotnet test` çalıştırıyordu — bu ikisine **hiç dokunmuyordu**. Biri yarın yeni bir modül ekleyip `Dockerfile`'daki `COPY` satırlarını güncellemeyi unutsa, CI bunu **hiç fark etmezdi** — sadece bir insan gerçekten `docker build` çalıştırıp elle keşfettiğinde ortaya çıkardı.

---

## 2. `ci.yml`'ye eklenen yeni adımlar — satır satır

```yaml
- name: Build FieldOps.Api Docker image
  run: docker build -t fieldops-api:ci .
```
Dünkü `Dockerfile`'ın **gerçekten** build edilebildiğini her push'ta kanıtlıyor. GitHub Actions'ın `ubuntu-latest` runner'ında Docker zaten hazır (Day 28/48'den beri Testcontainers için zaten kullandığımız aynı gerçek).

```yaml
- name: Install dotnet-ef
  run: dotnet tool install --global dotnet-ef --version 10.0.11
```
`dotnet ef` bu makinede zaten global olarak kurulu, ama CI'ın **kendi**, sıfırdan başlayan runner'ında hiç yok — migration'ları uygulamak için gerekiyor.

```yaml
- name: Create CI-only .env for Docker Compose
  run: echo "SA_PASSWORD=CI_Only_Temp_Pw1" > .env
```
Day 57/59'un "gerçek sır, dosyaya değil ortam değişkenine" dersi burada **CI bağlamında** tekrarlanıyor — CI'a özel, geçici bir `.env` dosyası, workflow'un **kendisi tarafından**, çalışma anında oluşturuluyor (gerçek bir sır deposu — GitHub Secrets — bu kişisel proje için bugün kapsam dışı, ama mekanizma aynı).

```yaml
- name: Start Docker Compose stack
  run: docker compose up -d --build
```
Dünkü `docker-compose.yml`'i, CI'ın kendi ortamında **gerçekten** ayağa kaldırıyor.

```yaml
- name: Apply EF Core migrations to the containerized SQL Server
  run: |
    CONN_BASE="Server=localhost,14330;User Id=sa;Password=CI_Only_Temp_Pw1;TrustServerCertificate=True;"
    for pair in "FieldOps.Modules.Organizations:FieldOpsOrganizations" ...; do
      proj="${pair%%:*}"
      db="${pair##*:}"
      for attempt in $(seq 1 10); do
        if dotnet ef database update --project "src/$proj" --connection "${CONN_BASE}Database=${db};"; then
          break
        fi
        sleep 5
        # 10 denemeden sonra basarisizsa exit 1
      done
    done
```
**Neden bir yeniden deneme döngüsü, tek bir çağrı değil:** Day 59'da `depends_on`'un sadece "container başlatma sırası" garanti ettiğini, "içerideki servis gerçekten hazır" garantisi vermediğini öğrenmiştik. SQL Server container'ı "started" olsa bile, motor gerçekten bağlantı kabul etmeye hazır olana kadar birkaç saniye geçebilir — döngü, bu gecikmeyi (ayrı bir "hazır mı?" kontrolü yazmadan) doğal olarak emiyor.

```yaml
- name: Verify the containerized stack is genuinely healthy
  run: |
    for attempt in $(seq 1 10); do
      RESPONSE=$(curl -s http://localhost:5190/health/ready || true)
      if echo "$RESPONSE" | grep -q '"status":"Healthy"'; then
        exit 0
      fi
      sleep 5
    done
    exit 1
```
Bugünün **asıl doğrulaması** — Day 56'nın `/health/ready`'sini, bugün CI'da **gerçek bir smoke test** olarak kullanıyoruz. `Healthy` dönmezse, bu adım (ve dolayısıyla tüm CI çalışması) **başarısız oluyor** — tam olarak istediğimiz davranış.

```yaml
- name: Tear down Docker Compose stack
  if: always()
  run: docker compose down
```
**`if: always()`** — StockPilot Day 29'daki `upload-artifact` adımıyla aynı desen: önceki adımlardan biri başarısız olsa bile (örn. sağlık kontrolü `Unhealthy` dönüp CI'ı kırsa bile), bu temizlik adımı **yine de çalışsın** — yarım kalmış container'lar CI runner'ında asılı kalmasın.

---

## 3. Canlı kanıt — push etmeden önce yerelde tam simülasyon

Her adımı, CI'daki **birebir aynı komutlarla**, yerelde çalıştırdım (kendi `.env`'imi geçici olarak CI'ın kullandığı basit şifreyle değiştirip, sonra geri yükleyerek):
```
1) docker build -t fieldops-api:ci .           -> basarili
2) echo "SA_PASSWORD=..." > .env
3) docker compose up -d --build                -> basarili
4) 5 modul icin migration dongusu               -> hepsi ILK denemede basarili
5) /health/ready dogrulama dongusu              -> ILK denemede "Healthy"
6) docker compose down                          -> temiz
```
Gerçek CI ortamı (GitHub Actions) burada test edilmedi — ama yerel simülasyon, komutların **doğru** olduğunu, CI'a gönderilmeden önce kanıtlıyor.

---

## 4. Demo basitleştirmesi vs. üretim gereksinimi

- CI'daki `.env` şifresi basit, sabit bir değer (`CI_Only_Temp_Pw1`) — gerçek bir sır deposu (GitHub Secrets) yerine workflow'un kendisi tarafından oluşturuluyor. Bu kişisel proje için makul; gerçek bir üretim CI/CD'sinde bu GitHub Secrets üzerinden gelirdi.
- Migration'lar CI'da da hâlâ elle (bir script adımıyla) uygulanıyor — Day 59'un bağımsız görevinde konuştuğumuz "ayrı bir migrator job'ı" fikri bugün de uygulanmadı, gelecekte eklenebilecek bir iyileştirme olarak kaldı.

---

## Regresyon

```
dotnet test FieldOps.slnx    → 49/49 (yerel kurulumla, degismedi)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## Week 11 tamamen kapandı

Structured logging & correlation ID (Day 55) → health checks (Day 56) → configuration & environment management (Day 57) → Docker (Day 58) → Docker Compose (Day 59) → continuous integration (Day 60). Sıradaki: **Week 12** (Phase 3'ün son haftası — entegrasyon/yetkilendirme testleri, hata senaryoları, AI provider abstraction, dokümantasyon ve demo).
