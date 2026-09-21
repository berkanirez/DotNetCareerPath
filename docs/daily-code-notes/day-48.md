# Day 48 — Kod Notları

Faz 3, Hafta 10, Gün 48. Konu: **FieldOps'a gerçek persistence** — senin sorduğun keskin bir soru sayesinde, Redis'i anlamlı kılmadan önce gerçek bir veritabanı eklemek.

Bu doküman, bugün oluşturulan/değişen her parçayı **yazılma sırasına göre** gezer.

---

## 1. Sorunun kendisi — planı sunmadan önce

Redis planını sunduğumda şu soruyu sordun: "neden direkt veritabanına bağlamıyoruz, o zaman Redis'in anlamı olur." Bunu kontrol ettim: `docs/ROADMAP.md`'nin Phase 3'ü (Week 7-12) **hiçbir yerde** FieldOps'a EF Core eklemeyi programlamamış — ama `docs/REQUIREMENTS_MATRIX.md`, "EF Core | Phase 1–4" diye kanıt bekliyor. İki doküman arasında gerçek bir tutarsızlık, senin sorun sayesinde ortaya çıktı.

---

## 2. ADR 0003 — modül başına ayrı veritabanı

`docs/adr/0003-database-per-module.md` — kritik karar: her modül **kendi veritabanını** alacak (`Organizations` → `FieldOpsOrganizations`), paylaşılan tek bir `FieldOpsDb` değil. Gerekçe: ADR 0002'nin "modüller birbirini tanımaz" kuralının, veri katmanına genişletilmiş hali — fiziksel olarak ayrı veritabanları, yanlışlıkla bile çapraz-modül bir `JOIN` yapılmasını **imkansız** kılıyor.

**Dürüstçe kabul edilen bedel:** Artık `Employee.OrganizationId`'nin gerçekten var olan bir organizasyona işaret ettiğini garanti eden hiçbir gerçek foreign key yok — bu, ADR 0002'nin zaten "persistence gelince çözülür" diye ertelediği boşluk, ve persistence geldi ama boşluk **kasıtlı olarak** kapanmadı (gerçek dağıtık sistemlerin çözdüğü şekilde, örn. Phase 4'ün outbox/inbox deseni, çözülecek).

---

## 3. `OrganizationsDbContext` — modülün kendi, `internal` DbContext'i

```csharp
internal class OrganizationsDbContext : DbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.Property(o => o.Name).IsRequired().HasMaxLength(200);
            entity.HasData(
                new Organization("Acme Field Services") { Id = 1 },
                new Organization("Blue Ridge Maintenance") { Id = 2 }
            );
        });
    }
}
```

**Neden `HasData` bu sefer güvenli, RoadmapOS Day 9'un kaçındığı gibi değil:** RoadmapOS Day 9'da `HasData`'dan kaçınmıştık çünkü veritabanında **zaten organik veri** vardı, sabit ID'lerle çakışma riski taşıyordu. Burada tamamen **yeni** bir veritabanı — hiçbir çakışma riski yok, ve `Employees`/`WorkOrders` modüllerinin (Day 33/40'tan beri) `OrganizationId=1/2` varsayımını bozmamak için ID'lerin **tam olarak** aynı kalması gerekiyordu.

`dotnet ef migrations add` bir class library'de çalışmak için tasarım-zamanı bir factory'ye ihtiyaç duydu (`OrganizationsDbContextFactory`) — beklenen, StockPilot'ta (tek bir Web API projesi olduğu için) hiç karşılaşmadığımız bir engel.

---

## 4. `OrganizationsModule` — host hâlâ hiçbir EF Core detayı görmüyor

```csharp
public static IServiceCollection AddOrganizationsModule(this IServiceCollection services, string connectionString)
{
    services.AddDbContext<OrganizationsDbContext>(options => options.UseSqlServer(connectionString));
    return services.AddScoped<IOrganizationDirectory, EfOrganizationDirectory>();
}
```

`Program.cs`, sadece bir **connection string** geçiriyor — `OrganizationsDbContext`'i ya da `EfOrganizationDirectory`'yi hiç adıyla görmüyor. `IOrganizationDirectory` sözleşmesi hiç değişmedi, `OrganizationsController` tek satır bile değişmedi — Day 32'nin "modül-özel DI kaydı" deseninin tam olarak neden bu kadar değerli olduğunun kanıtı.

---

## 5. Canlı kanıt — API gerçekten veritabanından okuyor

```
1) API'den organizasyonlari listele                     → Acme, Blue Ridge
2) Veritabaninda DOGRUDAN SQL ile bir ismi degistir      → 1 satir etkilendi
3) API'yi tekrar cagir                                   → degisiklik goruldu!
```

Bu, RoadmapOS Day 4/StockPilot Day 16'nın aynı canlı kanıt deseni — API'nin gerçekten bellekten değil, veritabanından okuduğunu ispatlıyor.

---

## 6. Beklenmedik ama gerçek bir sonuç — testler artık gerçek bir veritabanına muhtaç

Testleri çalıştırınca 49/49 yeşil çıktı ama **neden**: `EmployeesAuthorizationIntegrationTests`'in çoğu, `EmployeeApplicationService.CreateEmployee` üzerinden (Day 34) `IOrganizationDirectory.GetById`'yi çağırıyor — artık bu **gerçek SQL Server'a** gidiyor. `.github/workflows/ci.yml`'deki "FieldOps'un testleri saf bellek-içi sahte nesneler, hiç dış bağımlılık yok" yorumu **artık yanlıştı** — GitHub Actions'ın `ubuntu-latest` çalıştırıcısında FieldOps için hiç SQL Server yok, bu pushlanınca CI kesin kırılacaktı.

**Çözüm:** StockPilot Day 28'in **aynı deseni** — `FieldOpsApiFactory` (Testcontainers ile tek kullanımlık, gerçek bir SQL Server konteyneri). Tek fark: `OrganizationsDbContext` `internal` olduğu için, test projesine dar bir `[InternalsVisibleTo("FieldOps.Api.Tests")]` izni eklendi (sadece migration uygulamak için) — ama `FieldOps.Api`'nin kendisi hâlâ hiçbir internal tipi görmüyor. Ayrıca `ConfigureWebHost`'ta doğrudan `DbContext` servisini değiştirmek yerine (StockPilot'un yaptığı gibi, çünkü `StockPilotDbContext` public), sadece **connection string konfigürasyon değerini** override ettik — sınırı StockPilot'tan bile biraz daha sıkı tuttuk.

**Canlı kanıt:** `docker ps` ile testler çalışırken **gerçek** iki SQL Server konteyneri (her test sınıfı için birer tane) görüldü; testler bitince tamamen temizlendiler (`docker ps -a`'da bile yoklar — Testcontainers'ın "Ryuk" temizlik bekçisi, Day 28'de gördüğümüz aynı davranış).

---

## 7. Regresyon

```
dotnet test FieldOps.slnx    → 49/49 (artık gerçek bir Testcontainers SQL Server'a karşı)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```

---

## 8. İkinci karar — kalan üç modülü de aynı oturumda taşımak

`Organizations`'ı bitirdikten sonra, "her modülü ayrı bir güne yaymayalım, hepsini tek seferde yapıp asıl plana (Redis) dönelim" dedin. Bu, `Employees`, `WorkOrders`, `Customers` modüllerinin de **aynı oturumda** EF Core'a taşınması anlamına geldi — ADR 0003 ve Testcontainers altyapısı zaten kurulu olduğu için bu üçü gerçekten daha hızlı oldu.

**Her modül için tekrarlanan (artık tanıdık) desen:**
- `Data/<Modül>DbContext.cs` (`internal`, `HasData` ile aynı seed ID'ler)
- `Data/Ef<Modül>Directory.cs` (mevcut arayüzü birebir uygulayan, `SaveChanges()` çağıran implementasyon)
- `Data/<Modül>DbContextFactory.cs` (tasarım-zamanı factory)
- `<Modül>Module.cs` güncellemesi (connection string parametresi)
- `AssemblyInfo.cs` (`InternalsVisibleTo("FieldOps.Api.Tests")`)
- Eski `InMemory*Directory.cs` silindi
- Migration oluşturuldu ve gerçek veritabanına uygulandı

**Bir teknik detay — `WorkOrder.EvidenceNotes` (bir `List<string>`) nasıl saklanıyor:** EF Core'un "primitive collection" desteği (`entity.PrimitiveCollection(...)`), bunu otomatik olarak bir JSON sütununa (`nvarchar`) eşliyor — ayrı bir "Evidence" tablosu gerekmedi. Bunu varsaymadım, migration'ı oluşturup gerçek şemayı `INFORMATION_SCHEMA.COLUMNS` ile kontrol ederek doğruladım.

**`Program.cs`'te küçük bir refactor:** Dört modülün de "connection string'i configürasyondan al, yoksa fırlat" mantığı **birebir aynı** olduğu için, bunu tek satırlık bir yerel fonksiyona (`RequireConnectionString`) çıkardım — bu haftanın "gerçekten aynıysa hemen çıkar" kuralının bir devamı.

**`FieldOpsApiFactory`'nin genişlemesi:** Dört ayrı Docker konteyneri yerine **tek bir** SQL Server konteyneri, dört farklı veritabanı adıyla (`FieldOpsOrganizations`, `FieldOpsEmployees`, `FieldOpsWorkOrders`, `FieldOpsCustomers`) — tıpkı yerel geliştirme ortamının zaten yaptığı gibi (aynı SQL Server örneği, farklı veritabanları). `SqlConnectionStringBuilder` ile container'ın bağlantı dizesinin sadece veritabanı adını değiştirdim.

**Canlı uçtan-uca kanıt (gerçek uygulama, gerçek veritabanları):**
```
1) Calisan listesi -> Employees veritabanindan okundu
2) Yeni calisan olustur -> id: 6 (seed edilen 1-5'ten sonra IDENTITY dogru devam etti)
3) Is emri olustur + ata + kanit ekle -> hepsi WorkOrders veritabaninda kalici, JSON kanit notu dogru calisti
```

`dotnet test FieldOps.slnx` → **49/49**, artık dördü de gerçek Testcontainers veritabanlarına karşı, ~20 saniyede (öncesinde <1 saniye).

---

## Bugünün dersi ve sıradaki gün

Senin bir soruyla bulduğun gerçek bir dokümantasyon tutarsızlığı, bugünü tamamen değiştirdi — planlanan "Redis kurulumu" yerine "önce Redis'in anlamlı olacağı gerçek bir temel kur" oldu, ve senin ikinci kararınla bu temel **tek bir oturumda** tüm dört modülü kapsayacak şekilde tamamlandı. Artık FieldOps'un tamamı gerçek, kalıcı, test edilebilir bir persistence katmanına sahip — Redis'e (Week 10) gerçek bir anlamla dönebiliriz.
