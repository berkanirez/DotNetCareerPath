# Day 79 — Elasticsearch: Referans Notu

Faz 4, Hafta 16, Gün 79 — **Week 16'nın ilk günü**. Bu dosya, Day 48'in `day-48-redis-detay.md`'siyle aynı gerekçeyle var: sadece kodu göstermek değil, "Elasticsearch nedir, neden var, biz neden kullanıyoruz, genel olarak nasıl çalışır, ve bunu bizim kodumuza nasıl bağladık" sorularına baştan sona, tek bir yerde cevap vermek.

---

## 1. Elasticsearch nedir — zihinsel model

Elasticsearch, **tam metin arama (full-text search) için tasarlanmış bir arama motoru**. SQL Server gibi "birincil veri kaynağı" (source of truth) olması BEKLENMEZ — biz de kullanmadık: SQL Server hâlâ tek gerçek kaynak, Elasticsearch sadece onun "aranabilir bir kopyası."

**Neden SQL'in `LIKE '%kelime%'`'i yetmiyor?** SQL Server, bir metin sütununda `LIKE '%su%'` çalıştırdığında, gerçekten **her satırın metnini baştan sona tarıyor** — hiçbir indeks bu deseni hızlandıramaz (indeks, "bu değer NEREDE başlıyor" sorusuna hızlı cevap verir, ama "bu değerin İÇİNDE herhangi bir yerde bu kelime var mı" sorusuna değil). Ayrıca `LIKE` sana sadece **evet/hayır** verir — "kelime var mı yok mu." Hangi sonucun daha alakalı olduğunu (relevance) hiç bilemezsin.

**Elasticsearch'in çözümü — ters indeks (inverted index):** Bir kitabın sonundaki "dizin" (index) sayfasını düşün — "su pompası" kelimesini kitapta aramak için sayfa sayfa taramazsın, dizine bakarsın: "pompa → sayfa 12, 45, 90." Elasticsearch, indekslediğin her belge için bunu otomatik yapıyor: her kelimeyi, o kelimenin geçtiği TÜM belgelerin bir listesine önceden eşliyor. Arama yaptığında, kelimeyi tek seferde bu listede buluyor — belgelerin metnini baştan taramıyor. Bu yüzden Elasticsearch'te arama, SQL'in `LIKE`'ından **temelde farklı ve çok daha hızlı** bir işlem.

**Bunun üstüne bir de "ne kadar iyi eşleşti" sorusu var — relevance scoring:** Elasticsearch her sonuca bir **puan** veriyor: kelime ne kadar sık geçiyor, kelime ne kadar "nadir" (yaygın olmayan bir kelime daha çok bilgi taşır), belge ne kadar kısa/uzun gibi faktörlere göre. SQL'in `WHERE` şartı bunu hiç yapmaz — ya eşleşir ya eşleşmez, "ne kadar iyi eşleştiği" diye bir kavram yoktur.

**Alt yapı notu:** Elasticsearch'ün altında **Apache Lucene** adlı, Java'yla yazılmış bir arama kütüphanesi çalışıyor — ters indeksi gerçekten tutan, sorguyu gerçekten çalıştıran motor o. Elasticsearch, Lucene'in üstüne JSON tabanlı bir REST API, dağıtık çalışma (cluster/node/shard) ve yönetim kolaylığı ekliyor. Docker imajının Java bellek ayarları (`ES_JAVA_OPTS`) istemesinin sebebi bu — Elasticsearch aslında bir **JVM (Java Virtual Machine) uygulaması**.

---

## 2. Genel terimler — SQL'deki karşılıklarıyla

| Elasticsearch | SQL Server karşılığı | Not |
|---|---|---|
| **Index** (bugün: `workorders`) | Tablo | Bir tür belgenin toplandığı yer. |
| **Document** | Satır (row) | Ama JSON — şeması SQL kadar katı değil. |
| **Mapping** | Şema (`CREATE TABLE`'daki sütun tipleri) | Hangi alanın hangi tipte olduğu — bugün bunu **biz hiç tanımlamadık**, bkz. bölüm 6.3. |
| **Node** | Bir SQL Server instance'ı | Elasticsearch'ü çalıştıran tek bir sunucu/process. |
| **Cluster** | — (SQL Server'da doğrudan karşılığı yok) | Birden fazla node'un birlikte çalışması. Bugün tek node (`discovery.type: single-node`). |
| **Shard** | — | Bir index'in, büyük veri setlerinde birden fazla node'a bölünmüş parçaları. Bugünkü ölçekte önemsiz, ama "Elasticsearch neden node/cluster/shard diye kelimeler kullanıyor" sorusunun cevabı bu — SQL Server'ın tek makinede çalışmasının aksine, Elasticsearch baştan **dağıtık** çalışmak üzere tasarlandı.

---

## 3. Sorgu tarafı — Query DSL, `bool`, `Filter` vs `Must`

Elasticsearch'e sorgu, düz bir `WHERE` cümlesi gibi değil, **JSON tabanlı bir "sorgu dili"** (Query DSL) ile gidiyor. En sık kullanılan yapı `bool` sorgusu — birden fazla şartı birleştirmenin yolu. `bool` sorgusunun İÇİNDE, şartlar **iki farklı bağlamda** yaşayabilir:

* **Query context (`must`, `should`):** "Bu şart sağlanmalı (veya `should` için: sağlanırsa iyi olur), VE ne kadar iyi sağlandığına göre bir **relevance puanı** hesapla." Burada kullanılan asıl amaç aramanın kendisi.
* **Filter context (`filter`, `must_not`):** "Bu şart sağlanmalı ama SADECE evet/hayır — puanlamaya hiç katkısı yok." Elasticsearch, filter context'teki sonuçları ayrıca **cache'leyebiliyor** da (tekrar eden filtreler için), bu yüzden filter context genelde daha hızlı.

**Kuralımız (bugün kodda uyguladığımız):** *Kesin, iş kuralı niteliğindeki* şartlar (`organizationId == X` gibi "bu senin verin mi değil mi") → `filter`. *Gerçek aranan şey* (kullanıcının yazdığı arama kelimesi) → `must`.

**`Term` vs `Match` — iki farklı sorgu tipi, `filter`/`must`'tan bağımsız bir başka eksen:**
* **`Term`** — **analiz edilmemiş, tam eşleşme.** Değeri olduğu gibi, hiç işlemeden karşılaştırıyor. Sayılar, ID'ler, statü kodları gibi "ya tam eşleşir ya eşleşmez" alanlar için.
* **`Match`** — **analiz edilmiş, tam metin eşleşmesi.** Hem sorgu metnini hem de indekslenen metni önce bir **analyzer**'dan geçiriyor: küçük harfe çeviriyor, kelimelere ayırıyor (tokenization), vs. Bu yüzden `"Su Pompası"` diye indekslenen bir başlık, `match` ile `"su"`, `"pompası"`, hatta `"POMPASI"` aramalarının hepsiyle eşleşebiliyor — `Term` bunu ASLA yapmaz, birebir aynı string'i ister.

Bizim kodumuzda: `OrganizationId` için `Filter` + `Term` (kesin, analiz gerektirmeyen bir kiracı filtresi), `Title` için `Must` + `Match` (gerçek, analiz edilmiş tam metin araması).

---

## 4. Çözülen gerçek problem

`GET /api/workorders` zaten var, ama sadece `OrganizationId`'ye göre **tam listeliyor** — "başlığında 'su' geçen iş emirlerini bul" gibi bir soruya cevap veremiyor. Bunu SQL'de `LIKE '%su%'` ile çözmeye çalışsak: (1) yavaş olurdu (indekslenemez), (2) hangi sonucun daha "alakalı" olduğunu hiç söyleyemezdi, (3) yazım varyasyonlarına (büyük/küçük harf, kelime sırası) toleranslı olmazdı. Elasticsearch tam olarak bunun için var.

---

## 5. Kurulum, adım adım

`docker-compose.yml`'e eklenen servis:
```yaml
elasticsearch:
  image: docker.elastic.co/elasticsearch/elasticsearch:9.1.0
  environment:
    discovery.type: single-node
    xpack.security.enabled: "false"
    ES_JAVA_OPTS: "-Xms512m -Xmx512m"
  ports:
    - "9200:9200"
```

Parça parça:
- `docker.elastic.co/elasticsearch/elasticsearch:9.1.0` — Elastic'in kendi resmi image kaynağı (Docker Hub değil, kendi registry'leri). `9.1.0`, kullandığımız .NET istemci paketinin (`Elastic.Clients.Elasticsearch` 9.5.2) uyumlu olduğu majör sürüm.
- `discovery.type: single-node` — Elasticsearch normalde başka node'larla bir cluster kurmaya çalışır; bu ayar olmadan, hiç gelmeyecek diğer node'ları bekleyip **başlamayı reddeder**. "Tek başınasın, cluster kurmaya çalışma" diyoruz.
- `xpack.security.enabled: "false"` — kimlik doğrulamayı tamamen kapatıyor. **Sadece demo için** — üretimde asla böyle bırakılmaz.
- `ES_JAVA_OPTS: "-Xms512m -Xmx512m"` — Elasticsearch bir JVM uygulaması olduğu için (bkz. bölüm 1), Java'nın kendi bellek ayarları: başlangıç (`-Xms`) ve maksimum (`-Xmx`) heap boyutu, ikisi de 512MB'a sabitlenmiş — demo ölçeğinde gereğinden fazla bellek harcamasın diye.
- `ports: "9200:9200"` — Elasticsearch'ün REST API'sinin varsayılan portu. Redis'in `6379`'u, RabbitMQ'nun `5672`'si gibi.

Doğrulama:
```bash
curl http://localhost:9200/_cluster/health
# -> {"cluster_name":"docker-cluster","status":"green","number_of_nodes":1,...}
```
`status: green` — tek node'lu bir cluster için "her şey yolunda" anlamına gelir (gerçek çok-node'lu bir cluster'da `yellow`/`red` de anlamlı olur, tek node'da hep ya `green` ya da index hiç yoksa boş).

---

## 6. Kodda ne yaptık, adım adım, neden

### 6.1. `WorkOrderSearchDocument.cs` — Elasticsearch'e giden "belge"nin şekli

```csharp
public record WorkOrderSearchDocument(int Id, int OrganizationId, string Title, WorkOrderStatus Status);
```
`WorkOrderSummary`'nin (SQL'e bakan tip) küçük ve düz bir kopyası — arama sonucunun göstereceği alanlardan fazlasını taşımıyor. Elasticsearch'e "kaynak veri" gibi davranmıyoruz, sadece arama için gerekeni kopyalıyoruz.

### 6.2. `IWorkOrderSearchIndex.cs` — Day 51/63'ün aynı soyutlama deseni

```csharp
public interface IWorkOrderSearchIndex
{
    Task IndexAsync(WorkOrderSearchDocument document, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkOrderSearchDocument>> SearchAsync(int organizationId, string query, CancellationToken cancellationToken);
}
```
`WorkOrdersController`, `Elastic.Clients.Elasticsearch`'ün TEK bir tipini bile görmüyor — `INotificationSender` (Day 51), `IAiProvider` (Day 63) ile birebir aynı seam.

### 6.3. `ElasticsearchWorkOrderSearchIndex.cs` — istemci burada, ve "mapping'i hiç tanımlamadık"

```csharp
public async Task IndexAsync(WorkOrderSearchDocument document, CancellationToken cancellationToken)
{
    await _client.IndexAsync(document, request => request.Index(IndexName).Id(document.Id), cancellationToken);
}
```
Dikkat: hiçbir yerde "Elasticsearch'e önce şu şemayı kur" demedik — hangi alan `int`, hangi alan `text` olacak diye hiç açıklama yapmadık. Bu, Elasticsearch'ün **dynamic mapping** özelliği: ilk belge geldiğinde, alan tiplerini **kendisi tahmin ederek** index'i otomatik oluşturuyor. Bunu varsaymadan, canlı sorguladık:
```bash
curl http://localhost:9200/workorders/_mapping
```
Gerçek sonuç:
```json
{
  "id": { "type": "long" },
  "organizationId": { "type": "long" },
  "title": { "type": "text", "fields": { "keyword": { "type": "keyword", "ignore_above": 256 } } },
  "status": { "type": "text", "fields": { "keyword": { "type": "keyword", "ignore_above": 256 } } }
}
```
Gözlemlenen, öğretici iki şey:
1. `int` alanlarımız (`Id`, `OrganizationId`) → Elasticsearch'te `long` oldu (sayısal tipler için varsayılan).
2. String alanlarımız (`Title`, `Status`) → hem `text` (analiz edilmiş, `Match` için) HEM DE bir `.keyword` alt alanı (analiz edilMEmiş, tam eşleşme için) aynı anda aldı — Elasticsearch'ün string'ler için varsayılan davranışı bu **çift alan** (multi-field) şeklinde.

**Demo basitleştirmesi:** Üretimde, mapping genelde **elle, önceden** tanımlanır (hangi alan `keyword`, hangi alan `text` olacak açıkça söylenir) — dynamic mapping'e güvenmek, Elasticsearch'ün "tahmin ettiği" tipin her zaman istediğin tip olacağının garantisi değildir (örn. `Status`'un `text` değil, sadece `keyword` olmasını isteseydik, bunu elle söylememiz gerekirdi).

### 6.4. Arama sorgusu — `Filter`/`Term` ve `Must`/`Match`'in gerçek kod karşılığı

```csharp
public async Task<IReadOnlyList<WorkOrderSearchDocument>> SearchAsync(int organizationId, string query, CancellationToken cancellationToken)
{
    var response = await _client.SearchAsync<WorkOrderSearchDocument>(request => request
        .Indices(IndexName)
        .Query(q => q
            .Bool(b => b
                .Filter(f => f.Term(t => t.Field(d => d.OrganizationId).Value(organizationId)))
                .Must(m => m.Match(mm => mm.Field(d => d.Title).Query(query)))
            )
        ), cancellationToken);

    return response.Documents.ToList();
}
```
Bölüm 3'te anlatılan genel kuralın birebir kod karşılığı: `OrganizationId` → `Filter` + `Term` (Day 40'ın kiracı izolasyonu, puanlamaya katılmıyor, sadece eler). `Title` → `Must` + `Match` (gerçek arama, `.keyword` değil `text` alanı üzerinden, yani analiz edilmiş hâliyle eşleşiyor).

### 6.5. `Program.cs` — kayıt

```csharp
var elasticsearchUri = builder.Configuration["Elasticsearch:Uri"] ?? "http://localhost:9200";
builder.Services.AddSingleton(_ => new ElasticsearchClient(new Uri(elasticsearchUri)));
builder.Services.AddSingleton<IWorkOrderSearchIndex, ElasticsearchWorkOrderSearchIndex>();
```
`ElasticsearchClient`, Day 48'in `IConnectionMultiplexer`'ıyla aynı sebeple Singleton — thread-safe, bağlantı havuzu tutuyor, her istekte yeniden kurulmamalı.

### 6.6. `WorkOrdersController.cs` — `Create`/`Complete` indeksliyor, yeni `Search` sadece Elasticsearch'e gidiyor

```csharp
// Create icinde, SQL'e yazdiktan SONRA:
await _workOrderSearchIndex.IndexAsync(new WorkOrderSearchDocument(workOrder.Id, workOrder.OrganizationId, workOrder.Title, workOrder.Status), cancellationToken);

// Complete icinde, ayni sekilde -- Status artik Completed:
await _workOrderSearchIndex.IndexAsync(new WorkOrderSearchDocument(updated.Id, updated.OrganizationId, updated.Title, updated.Status), cancellationToken);
```
```csharp
[HttpGet("search")]
public async Task<ActionResult<IReadOnlyList<WorkOrderDto>>> Search(
    [FromQuery] string q,
    [FromHeader(Name = "X-Organization-Id")] int? organizationId,
    [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId,
    CancellationToken cancellationToken)
{
    var membershipError = ValidateMembership(organizationId, actingEmployeeId);
    if (membershipError is not null) { return membershipError; }

    var results = await _workOrderSearchIndex.SearchAsync(organizationId!.Value, q, cancellationToken);
    var workOrders = results.Select(d => new WorkOrderDto(d.Id, d.Title, d.OrganizationId, d.Status, null, Array.Empty<string>(), null, false)).ToList();
    return Ok(workOrders);
}
```
`Search`, bu controller'daki **tek** okuma işlemi — `IWorkOrderDirectory`'ye (SQL) değil, `IWorkOrderSearchIndex`'e (Elasticsearch) gidiyor.

### 6.7. `FieldOpsApiFactory.cs` — testler için No-Op

```csharp
private class NoOpWorkOrderSearchIndex : IWorkOrderSearchIndex
{
    public Task IndexAsync(WorkOrderSearchDocument document, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<WorkOrderSearchDocument>> SearchAsync(int organizationId, string query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkOrderSearchDocument>>(Array.Empty<WorkOrderSearchDocument>());
}
```
Day 67'nin `NoOpEventPublisher`'ıyla birebir aynı sebep: test ortamında gerçek Elasticsearch yok, gerçek implementasyon bırakılsaydı her `Create`/`Complete` testi başarısız bağlantı denemesiyle zaman kaybederdi.

---

## 7. Asıl kanıt — canlı doğrulama

Üç iş emri oluşturuldu (curl ile, gerçek çalışan bir Elasticsearch'e karşı):
```
Org 1: "Replace broken water pump"       (id 29)
Org 1: "Inspect HVAC ventilation system" (id 30)
Org 2: "Replace water heater"            (id 31)
```

**Org 1 için "water" araması:**
```json
[{"id":29,"title":"Replace broken water pump", ...}]
```
Sadece kendi iş emrini döndürdü — Org 2'nin "water heater"ı **hiç görünmedi**. Bu, `Filter`'daki `OrganizationId` kontrolünün gerçekten çalıştığının kanıtı; sadece "muhtemelen doğrudur" değil, iki organizasyonda da "water" geçen birer başlık koyup gözlemleyerek doğrulandı.

**Org 1 için "ventilation" araması:**
```json
[{"id":30,"title":"Inspect HVAC ventilation system", ...}]
```

**Statü güncellemesinin indekse yansıması:** İş emri 29 tamamlandıktan sonra, doğrudan Elasticsearch'e sorgu atıldı:
```bash
curl http://localhost:9200/workorders/_doc/29
# -> {"_version":2, "_source": {"id":29, ..., "status":"Completed"}}
```
`_version:2` — aynı belgenin İKİNCİ kez yazıldığını gösteriyor (ilk `Create`'te 1, `Complete`'te 2), ve `status` alanı gerçekten `"Completed"`e güncellenmiş. İlginç, gözlemlenen bir ayrıntı: `WorkOrderStatus` enum'ı C# tarafında bir `int` olsa da, Elasticsearch'e **string olarak** ("Open", "Completed") gitti — `Elastic.Clients.Elasticsearch` paketinin kendi JSON serileştiricisi enum'ları isimleriyle yazıyor, ASP.NET'in HTTP cevaplarında kullandığı sayısal serileştirmeden farklı bir davranış.

---

## 8. Demo basitleştirmesi vs. üretim gereksinimi

* **Demo bugün:** İndeksleme senkron, `Create`/`Complete`'in kendi HTTP isteği içinde — Elasticsearch o an ulaşılamazsa isteğin kendisi de etkilenebilir. Mapping hiç elle tanımlanmadı, dynamic mapping'e güvenildi. Tek node, güvenlik kapalı.
* **Üretimde gerekli olurdu:** İndekslemenin SQL yazma işleminden bağımsız, asenkron olması (Outbox/mesajlaşma desenleriyle — tam olarak Week 16'nın "index synchronization" konusu), elle tanımlanmış bir mapping, çok node'lu bir cluster, gerçek kimlik doğrulama.

---

## Kısa özet

**Neyi çözdük:** İş emirlerinde, SQL'in veremediği türden bir tam metin arama (kelime bazlı, alaka sıralı, yazım toleranslı) ihtiyacını.

**Neyi ekledik:** Tek node'lu bir Docker Elasticsearch konteyneri, `Elastic.Clients.Elasticsearch` paketi, `IWorkOrderSearchIndex` soyutlaması ve onun tek implementasyonu, `Create`/`Complete`'e eklenen indeksleme çağrıları, yeni `GET /api/workorders/search` endpoint'i.

**Nasıl bağladık:** `appsettings.Development.json`'daki `Elasticsearch:Uri` → `Program.cs`'te `AddSingleton<ElasticsearchClient>` ve `AddSingleton<IWorkOrderSearchIndex, ElasticsearchWorkOrderSearchIndex>` → `WorkOrdersController`'ın `Create`/`Complete`'i (yazma) ve yeni `Search`'ü (okuma).

**Neyi kanıtladık:** Kiracı izolasyonunun (`Filter`) arama tarafında da gerçekten çalıştığını (iki organizasyonun aynı kelimeyi içeren başlıkları karışmadan), tam metin eşleşmenin (`Must`/`Match`) doğru sonuçları getirdiğini, statü değişikliğinin indekse gerçekten yansıdığını (`_version` artışıyla), ve mapping'in bizim hiç müdahale etmeden, Elasticsearch tarafından doğru tahmin edildiğini (elle sorgulayarak, varsaymadan).
