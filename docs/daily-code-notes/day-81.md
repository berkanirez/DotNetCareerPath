# Day 81 — Kod Notları

Faz 4, Hafta 16, Gün 81. Konu: **rebuild stratejisi** — Elasticsearch'ün "SQL'den her zaman yeniden inşa edilebilir, atılabilir bir kopya" olduğu iddiasını gerçek bir işlemle kanıtlamak.

---

## 1. `IWorkOrderSearchIndex.cs` — iki yeni metot

```csharp
Task EnsureIndexExistsAsync(CancellationToken cancellationToken);
// Idempotent -- index zaten varsa hicbir sey yapmiyor. SQL Server migrasyonlarimizin
// aksine (hep ELLE uygulaniyor), bu metot her uygulama baslangicinda OTOMATIK
// cagrilabiliyor -- cunku "zaten varsa dokunma" garantisi, migrasyonlarin
// SAHIP OLMADIGI bir guvenlik.

Task RebuildOrganizationIndexAsync(int organizationId, IReadOnlyList<WorkOrderSearchDocument> documents, CancellationToken cancellationToken);
// Bilerek TEK BIR organizasyona ozel -- "tum index'i sil, sifirdan kur" YAPMIYORUZ,
// cunku bu BASKA organizasyonlarin da arama verisini silerdi (asagida 3. bolumde
// detayli).
```

---

## 2. `ElasticsearchWorkOrderSearchIndex.cs` — `EnsureIndexExistsAsync`, satır satır

**Bu metodun tamamının yaptığı şey (tek cümlede):** "workorders" index'i Elasticsearch'te zaten varsa hiçbir şey yapma; yoksa, alan tiplerini ELLE belirttiğimiz bir mapping ile SIFIRDAN oluştur.

```csharp
public async Task EnsureIndexExistsAsync(CancellationToken cancellationToken)
```
* `public` — bu metot, `IWorkOrderSearchIndex` arayüzünün bir üyesi olduğu için dışarıdan (controller'dan, `Program.cs`'ten) çağrılabilmesi gerekiyor.
* `async` — içinde `await` kullanacağımızı derleyiciye bildiren anahtar kelime. `async` OLMASAYDI, metodun gövdesinde `await` yazmak derleme hatası olurdu.
* `Task` — dönüş tipi. Metodun geriye somut bir DEĞER döndürmediğini (`void`'in async karşılığı gibi düşünülebilir, ama `void` değil — `async void` başka bir şeydir ve genelde kaçınılır), sadece "bu iş bir noktada BİTECEK" sözü verdiğini belirtiyor. Çağıran taraf bu `Task`'i `await` ederek "iş bitene kadar bekle" diyebiliyor.
* `EnsureIndexExistsAsync(CancellationToken cancellationToken)` — metot adı ve tek parametresi. `CancellationToken`, çağıranın "bu işlemi yarıda kesmek istersem sana haber verebileyim" demesini sağlayan standart .NET tipi; biz bunu doğrudan Elastic client'ın kendi metotlarına AKTARIYORUZ (kendimiz kontrol etmiyoruz).

```csharp
{
    var existsResponse = await _client.Indices.ExistsAsync(IndexName, cancellationToken);
```
* `_client` — sınıfın constructor'da aldığı `ElasticsearchClient` alanı.
* `.Indices` — `ElasticsearchClient`'ın bir **özelliği** (property), index YÖNETİMİYLE (oluşturma, silme, var mı kontrolü) ilgili tüm metotları bir arada tutan ayrı bir "alt istemci" nesnesi döndürüyor. Bunu `_client.IndexAsync(...)` (belge yazma) ile KARIŞTIRMAYIN — `_client.Indices.ExistsAsync(...)` TAMAMEN farklı bir amaç için (index'in kendisiyle ilgili), farklı bir nesne üzerinden çağrılıyor.
* `.ExistsAsync(IndexName, cancellationToken)` — "bu isimde bir index var mı?" sorusunu Elasticsearch'e soran metot. `IndexName`, sınıfın en üstünde tanımlı `private const string IndexName = "workorders";` sabiti.
* `await` — bu metot bir `Task<ExistsResponse>` (ya da benzeri bir tip) döndürüyor; `await`, bu asenkron işlemin GERÇEKTEN bitmesini bekleyip, içindeki GERÇEK sonucu (`ExistsResponse` nesnesini) `existsResponse` değişkenine atıyor. `await` olmasaydı, `existsResponse`'un tipi `Task<ExistsResponse>` olurdu — henüz bitmemiş bir "iş sözü", sonucun kendisi değil.
* `var` — C#'ın tip çıkarımı (type inference): `existsResponse`'un GERÇEK tipini (`BooleanResponse` ya da benzeri) siz yazmak zorunda değilsiniz, derleyici `await` ifadesinin sonucundan kendisi çıkarıyor.

```csharp
    if (existsResponse.Exists)
    {
        return;
        // Index zaten varsa BURADA duruyoruz -- ikinci, ucuncu, yuzuncu
        // uygulama baslangicinda hicbir sey degismiyor.
    }
```
* `existsResponse.Exists` — cevap nesnesinin `bool` bir özelliği: index gerçekten var mı, yok mu.
* `return;` — metodun geri kalanını (index oluşturma kısmını) hiç çalıştırmadan, metottan HEMEN çıkıyor. `Task` dönüş tipli bir metotta, parantezsiz `return;` yazmak "işim bitti, geriye özel bir değer yok" demek (tıpkı `void` bir metotta olduğu gibi — `Task`, `void`'in "beklenebilir" hali).

```csharp
    var createResponse = await _client.Indices.CreateAsync(IndexName, request => request
        .Mappings(m => m
            .Properties<WorkOrderSearchDocument>(p => p
                .LongNumber(d => d.Id)
                .LongNumber(d => d.OrganizationId)
                .Text(d => d.Title)
                .Keyword(d => d.Status)
            )
        ), cancellationToken);
```
* `_client.Indices.CreateAsync(IndexName, request => ..., cancellationToken)` — "bu isimde YENİ bir index oluştur" isteği. İki "normal" parametrenin (`IndexName`, `cancellationToken`) ARASINDA `request => ...` diye bir **lambda ifadesi** var — bu, "isteğin ayrıntılarını SEN (Elastic kütüphanesi) bana bir `request` nesnesi ver, ben de onun üzerinde istediğim metotları çağırarak isteği ŞEKİLLENDİREYİM" demenin C# yolu. Bu, `IndexAsync`'te gördüğümüz `request => request.Index(...).Id(...)` ile AYNI desen — Elastic'in TÜM API'si bu "fluent/lambda ile yapılandırma" üslubunu kullanıyor.
* `.Mappings(m => m...)` — `request` nesnesinin üzerinde `Mappings` diye bir metot çağrılıyor, o da KENDİ İÇİNDE başka bir lambda (`m => m...`) alıyor — "mapping'in ayrıntılarını da BANA bir `m` nesnesi vererek SEN tanımla" deniyor. Bu, iç içe geçmiş (nested) lambda'lar — her seviye, bir öncekinin "alt ayarını" yapılandırıyor.
* `.Properties<WorkOrderSearchDocument>(p => p...)` — `<WorkOrderSearchDocument>`, bir **generic tip parametresi**: "bu mapping'i, `WorkOrderSearchDocument` tipinin ALANLARINA göre kur" diyor. Bu sayede aşağıdaki `d => d.Id`, `d => d.Title` gibi lambda'lar, GERÇEK C# özelliklerine (property) referans verebiliyor (string yazmak yerine) — yanlış yazılan bir alan adı burada DERLEME HATASI olur, çalışma zamanı hatası değil.
* `.LongNumber(d => d.Id)` — "`Id` alanı, Elasticsearch'te `long` (64-bit tam sayı) tipinde olacak" demek. `d => d.Id`, Day 79'da gördüğümüz aynı "lambda ile alan seçme" deseni (`t.Field(d => d.OrganizationId)` gibi) — burada da string ("id") yazmak yerine, gerçek property'ye referans veriliyor.
* `.LongNumber(d => d.OrganizationId)`, `.Text(d => d.Title)`, `.Keyword(d => d.Status)` — aynı desenin, sırasıyla `OrganizationId`, `Title`, `Status` alanları için tekrarı. Her biri, o alanın Elasticsearch'teki TİPİNİ (uzun sayı / tam metin / tam eşleme) açıkça belirliyor.
* Bu üç satırlık zincir (`.Mappings(...).Properties(...).LongNumber/Text/Keyword(...)`) tek bir C# ifadesi — noktalarla (`.`) art arda eklenen her metot çağrısı, bir öncekinin döndürdüğü nesne üzerinde çalışıyor (buna **method chaining** denir); sonunda hepsi birleşip TEK bir JSON isteği (index oluşturma isteği, içinde mapping tanımıyla) haline geliyor.
* `await ... _client.Indices.CreateAsync(...)` — yukarıdaki tüm yapılandırma TAMAMLANDIKTAN sonra, gerçek istek Elasticsearch'e gönderiliyor ve cevap bekleniyor.

```csharp
    if (!createResponse.IsValidResponse)
    {
        throw new InvalidOperationException($"Failed to create the '{IndexName}' index: {createResponse.DebugInformation}");
    }
}
```
* `!createResponse.IsValidResponse` — `!`, mantıksal DEĞİL (not) operatörü: "eğer cevap GEÇERLİ (başarılı) DEĞİLSE." Day 80'de canlı yakaladığımız dersin (Elastic client istisna fırlatmıyor, sadece bu alanı `false` yapıyor) buradaki uygulaması.
* `throw new InvalidOperationException(...)` — `throw`, C#'ın istisna FIRLATMA anahtar kelimesi; `new InvalidOperationException(...)`, .NET'in genel amaçlı, "bu işlem şu an geçerli/mümkün değildi" anlamına gelen hazır istisna sınıfından YENİ bir örnek yaratıyor.
* `$"Failed to create the '{IndexName}' index: {createResponse.DebugInformation}"` — başında `$` olan bir string: **interpolated string** (iç içe değer yerleştirilmiş metin). Süslü parantez içindeki (`{IndexName}`, `{createResponse.DebugInformation}`) her ifade, çalışma zamanında GERÇEK değeriyle metnin içine yerleştiriliyor — `string.Format` yazmanın daha okunaklı hali.

---

## 3. `RebuildOrganizationIndexAsync` — satır satır

**Bu metodun tamamının yaptığı şey:** Sadece BELİRTİLEN organizasyona ait belgeleri Elasticsearch'ten sil, sonra çağıranın verdiği GÜNCEL belge listesini tek tek yeniden yaz.

```csharp
public async Task RebuildOrganizationIndexAsync(int organizationId, IReadOnlyList<WorkOrderSearchDocument> documents, CancellationToken cancellationToken)
```
* `int organizationId` — hangi organizasyonun belgelerinin silineceğini/yeniden yazılacağını belirten, sıradan bir `int` parametre.
* `IReadOnlyList<WorkOrderSearchDocument> documents` — çağıranın (controller'ın) SQL'den okuyup HAZIRLADIĞI belge listesi. `IReadOnlyList<T>`, "bu listeyi SADECE okuyabilirsin, içine ekleme/çıkarma yapamazsın" diyen bir arayüz — bu metodun, kendisine verilen listeyi YANLIŞLIKLA değiştirmeyeceğinin bir garantisi (sözleşmesi).

```csharp
{
    var deleteResponse = await _client.DeleteByQueryAsync<WorkOrderSearchDocument>(IndexName, request => request
        .Query(q => q.Term(t => t.Field(d => d.OrganizationId).Value(organizationId))), cancellationToken);
```
* `_client.DeleteByQueryAsync<WorkOrderSearchDocument>(IndexName, request => ..., cancellationToken)` — "bu index'te, belirli bir SORGUYA uyan TÜM belgeleri sil" isteği. `<WorkOrderSearchDocument>`, yine hangi tipin alanlarına göre sorgu kurulacağını belirten generic parametre.
* `request => request.Query(q => q.Term(...))` — "hangi belgeler silinsin" sorusunun cevabı, Day 79'da `SearchAsync`'te gördüğümüz AYNI `Term` sorgu şekliyle kuruluyor: `t.Field(d => d.OrganizationId).Value(organizationId)` — "`OrganizationId` alanı, TAM OLARAK bana verilen `organizationId` değerine eşit olan belgeler." Fark: Day 79'da bu sorgu bir ARAMA sonucunu filtrelemek için kullanılıyordu, bugün aynı sorgu şekli bir SİLME işlemini hedeflemek için kullanılıyor — Elasticsearch'te "hangi belgeler" sorusu, arasa da silse de HEP aynı Query DSL ile soruluyor.
* Bu satırın SONUCU: `workorders` index'indeki, SADECE `organizationId`'si eşleşen belgeler silinir — diğer organizasyonların belgelerine dokunulmaz.

```csharp
    if (!deleteResponse.IsValidResponse) { throw new InvalidOperationException(...); }
```
* Yukarıdaki `EnsureIndexExistsAsync`'teki AYNI kontrol deseni — silme isteği de başarısız olabilir (Elasticsearch kapalıysa), bu yüzden aynı şekilde kontrol edilip istisna fırlatılıyor.

```csharp
    foreach (var document in documents)
    {
        await IndexAsync(document, cancellationToken);
    }
}
```
* `foreach (var document in documents)` — parametre olarak gelen `documents` listesindeki HER bir `WorkOrderSearchDocument` nesnesi için, sırayla, döngü gövdesini bir kez çalıştır. `var document`, o anki elemanın tipini (`WorkOrderSearchDocument`) otomatik çıkarıyor.
* `await IndexAsync(document, cancellationToken);` — `_client.IndexAsync(...)` DEĞİL, bu sınıfın **kendi** `IndexAsync` metodu (başına `_client.` yok) — yani bölüm 6'da (Day 79/80'de) yazdığımız, `IsValidResponse` kontrolü ZATEN İÇİNDE olan metot. Her belge için bu metot TEKRAR TEKRAR çağrılıyor — yeni bir "toplu yazma" metodu yazılmadı, var olan tek-belge metodunun bir döngü içinde tekrarı yeterli oldu.

**Neden bu şekilde yazıldı — gerçek bir tasarım hatasını düşünerek önledik:** İlk akla gelen, "index'i tamamen sil, mapping'i yeniden kur, her şeyi yeniden yaz" olurdu. Ama `workorders` index'i **tüm organizasyonların** belgelerini aynı anda tutuyor — Org 1'in admin'i "benim index'imi yeniden kur" dediğinde, bu işlem TÜM index'i silerse, **Org 2'nin arama verisi de kaybolur**, sadece Org 1'inki geri yüklenir. Bu, Day 40'tan beri bu controller'ın her aksiyonunda uyguladığımız kiracı izolasyonu kuralının bozulması olurdu. Çözüm: index'in **tamamını** değil, sadece **istenen organizasyona ait belgeleri** silip yeniden yazmak (`DeleteByQueryAsync`, `OrganizationId` filtresiyle).

---

## 4. `Program.cs` — başlangıçta otomatik, ama `try/catch` ile korumalı

```csharp
var app = builder.Build();

try
{
    using var startupScope = app.Services.CreateScope();
    var searchIndex = startupScope.ServiceProvider.GetRequiredService<IWorkOrderSearchIndex>();
    await searchIndex.EnsureIndexExistsAsync(CancellationToken.None);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not ensure the Elasticsearch search index exists at startup; it will fall back to dynamic mapping once reachable");
}
```

**Bunu neden düşünerek eklemek zorunda kaldık:** İlk yazımda `try/catch` YOKTU. Ama sonra fark ettik: Elasticsearch o an kapalıysa, `EnsureIndexExistsAsync` içindeki `_client.Indices.ExistsAsync` de, `CreateAsync` de BAŞARISIZ olur (`IsValidResponse: false`), ve BİZİM kodumuz bunu bir istisnaya çeviriyor (`throw`). Bu istisna `Program.cs`'in ana akışında YAKALANMAZSA, **tüm uygulama BAŞLAMAYI REDDEDER** — tam olarak Day 80'in çözdüğü "Elasticsearch kapalıyken uygulama çalışmaya devam etmeli" garantisinin, bu sefer BAŞLANGIÇTA bozulması olurdu. `try/catch` ile bu riski ortadan kaldırdık: başarısız olursa sadece bir uyarı loglanıyor, uygulama normal şekilde açılmaya devam ediyor — index, Elasticsearch tekrar erişilebilir olduğunda (ilk `IndexAsync` çağrısıyla) dynamic mapping'e geri düşerek zaten oluşacak.

---

## 5. `WorkOrdersController.cs` — yeni `RebuildSearchIndex` action'ı, satır satır

**Bu action'ın tamamının yaptığı şey:** Çağıranın gerçekten bu organizasyonda VE Admin olduğunu doğrula; öyleyse, SQL'deki güncel veriyi oku ve Elasticsearch'teki karşılığını yeniden inşa et.

```csharp
[HttpPost("search/rebuild")]
```
* `[HttpPost("search/rebuild")]` — bir **öznitelik** (attribute), köşeli parantezle yazılır, üzerine yapıştırıldığı metodun (aşağıdaki `RebuildSearchIndex`) HANGİ HTTP metoduna (`POST`) ve HANGİ URL parçasına (`search/rebuild`) cevap vereceğini ASP.NET Core'a bildiriyor. Controller'ın başındaki `[Route("api/[controller]")]` ile birleşerek, tam adres `POST /api/workorders/search/rebuild` oluyor.

```csharp
public async Task<ActionResult> RebuildSearchIndex(
    [FromHeader(Name = "X-Organization-Id")] int? organizationId,
    [FromHeader(Name = "X-Employee-Id")] int? actingEmployeeId,
    CancellationToken cancellationToken)
```
* `Task<ActionResult>` — `Task<WorkOrderDto>` gibi generic bir sonuç DEĞİL, sade `ActionResult` — çünkü bu action'ın başarı durumunda geriye GÖSTERİLECEK bir veri (bir DTO) yok, sadece "işlem tamamlandı" bilgisi dönüyor.
* `[FromHeader(Name = "X-Organization-Id")] int? organizationId` — bu parametrenin değerinin, HTTP isteğinin GÖVDESİNDEN değil, bir HTTP BAŞLIĞINDAN (`X-Organization-Id`) okunacağını söyleyen öznitelik. `int?`, "nullable int" — başlık hiç gönderilmemişse `organizationId` burada `null` olur (bir hata fırlamaz), bu yüzden aşağıda `ValidateMembership` içinde AYRICA kontrol ediliyor.
* `CancellationToken cancellationToken` — ASP.NET Core, bu tipteki bir parametreyi ÖZEL olarak tanır ve OTOMATİK doldurur: istemci bağlantıyı erken keserse (tarayıcıyı kapatmak gibi), bu token "iptal edildi" durumuna geçer, ve biz onu Elastic client'a aktardığımız için, gerçekleşmekte olan Elasticsearch çağrısı da erkenden durdurulabilir.

```csharp
{
    var membershipError = ValidateMembership(organizationId, actingEmployeeId);
    if (membershipError is not null) { return membershipError; }
```
* `ValidateMembership(...)` — bu controller'daki HER action'ın kullandığı, ortak bir yardımcı metot (Day 33'ten beri): çağıran çalışanın GERÇEKTEN o organizasyonda olup olmadığını kontrol ediyor.
* `is not null` — C#'ın `!= null` yazmanın modern, okunması daha net bir yolu (desen eşleştirme/pattern matching sözdizimi).
* `return membershipError;` — hata varsa, action BURADA sona eriyor, aşağıdaki hiçbir satır çalışmıyor.

```csharp
    var adminError = ValidateIsAdmin(actingEmployeeId!.Value, "rebuild the search index for");
    if (adminError is not null) { return adminError; }
```
* `actingEmployeeId!.Value` — `actingEmployeeId`'nin tipi `int?` (nullable). `!` (null-forgiving operatörü), derleyiciye "buraya kadar geldiysen bunun `null` OLMADIĞINDAN eminim, uyarı verme" diyor (çünkü `ValidateMembership` zaten bunu kontrol edip geçti). `.Value`, `int?`'in İÇİNDEKİ gerçek `int` değerini çıkarıyor.
* `ValidateIsAdmin(...)` — Day 41'de `Assign` için yazılmış, bu action'da İLK KEZ tekrar kullanılan yardımcı metot: çağıranın rolünün `Admin` olup olmadığını kontrol ediyor. İkinci parametre (`"rebuild the search index for"`), sadece hata mesajının içine yerleşecek bir metin parçası.

```csharp
    var documents = _workOrderDirectory.GetAll()
        .Where(w => w.OrganizationId == organizationId)
        .Select(w => new WorkOrderSearchDocument(w.Id, w.OrganizationId, w.Title, w.Status))
        .ToList();
```
Bu tek ifade, LINQ (Language Integrated Query) ile üç işlemi ZİNCİRLEME uyguluyor:
* `_workOrderDirectory.GetAll()` — SQL Server'daki TÜM iş emirlerini (tüm organizasyonlardan!) `WorkOrderSummary` nesneleri olarak getiriyor.
* `.Where(w => w.OrganizationId == organizationId)` — `Where`, bir LINQ metodu: kendisine verilen lambda'yı (`w => w.OrganizationId == organizationId`) HER elemana uygulayıp, sonucu `true` olanları SÜZÜYOR. `w`, `Where`'in üzerinde çalıştığı listedeki "şu anki eleman"ı temsil eden, kendi seçtiğimiz bir isim (yerine `x`, `item`, ne istersek yazabilirdik). Bu satırdan sonra elimizde SADECE bu organizasyona ait iş emirleri kalıyor.
* `.Select(w => new WorkOrderSearchDocument(w.Id, w.OrganizationId, w.Title, w.Status))` — `Select`, her elemanı BAŞKA bir şeye DÖNÜŞTÜRÜR (projeksiyon): elimizdeki `WorkOrderSummary`'lerin her birini, sadece arama için gereken dört alanı taşıyan bir `WorkOrderSearchDocument`'e çeviriyor.
* `.ToList()` — LINQ'nun bu ana kadarki adımları GERÇEKTEN henüz çalıştırmamış olabileceği "tembel" (lazy) doğasını sonlandırıp, sonucu somut, bellekte duran bir `List<WorkOrderSearchDocument>`'e çeviriyor — `RebuildOrganizationIndexAsync`'in beklediği `IReadOnlyList<WorkOrderSearchDocument>` parametresine tam uyan bir tip.

```csharp
    await _workOrderSearchIndex.RebuildOrganizationIndexAsync(organizationId!.Value, documents, cancellationToken);

    return NoContent();
}
```
* `await _workOrderSearchIndex.RebuildOrganizationIndexAsync(...)` — bölüm 3'te satır satır incelediğimiz metot, GERÇEKTEN burada çağrılıyor; `await`, bu işlem (silme + yeniden yazma döngüsü) TAMAMEN bitene kadar action'ın devam etmemesini sağlıyor.
* `return NoContent();` — `ControllerBase`'in hazır bir yardımcı metodu; HTTP `204 No Content` durum kodunu döndürüyor — "işlem başarıyla tamamlandı, ama geri gösterecek bir veri yok" anlamına gelen standart HTTP cevabı (bir DTO döndüren `Ok(dto)`'nun aksine).

**Neden outbox'tan geçmiyor:** Outbox pattern, "bir iş kuralının SONUCUNU (yeni bir gerçeği) güvenilir şekilde duyurmak" için var (Day 71). Rebuild ise yeni bir gerçek üretmiyor — zaten var olan, bilinen veriyi SQL'den okuyup Elasticsearch'e **yeniden** yazıyor. Bu bir "duyuru" değil, bir "senkronizasyon" — bu yüzden doğrudan çağrı yeterli ve daha basit.

---

## Regresyon (Day 81)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65, 8dk32sn (normal)
```

## Canlı doğrulama

* Elasticsearch açıkken uygulama başlatıldı → `_mapping` sorgusuyla doğrulandı: `title` sadece `text`, `status` sadece `keyword` — Day 79'un dynamic mapping'inin verdiği `text+keyword` çiftinin YERİNE, gerçekten bizim tanımladığımız açık mapping geldi.
* Org 1 ve Org 2'de birer iş emri oluşturuldu, ikisi de Elasticsearch'te doğrulandı. **Sadece Org 1** için `POST /api/workorders/search/rebuild` çağrıldı (`204 No Content`) — sonrasında Org 2'nin belgesi **hiç dokunulmadan** yerinde kaldı, Org 1'in belgeleri (bugün oluşturulan VE daha önceki günlerden kalan gerçek SQL verisi dahil) doğru şekilde geri geldi.
* Admin olmayan bir çalışan (`X-Employee-Id: 2`, Member) rebuild'i çağırmaya çalıştı → `403 Forbidden`, beklendiği gibi.

## Demo basitleştirmesi vs. üretim gereksinimi

`RebuildSearchIndex`, bugün tüm iş emirlerini **tek bir senkron döngüde, tek istek içinde** işliyor — küçük veri setinde sorun değil, ama gerçek, büyük bir veri setinde (binlerce iş emri) bu hem HTTP isteğini çok uzatırdı hem de zaman aşımına (timeout) uğrayabilirdi. Üretimde bu muhtemelen bir arka plan/batch job'u olurdu (Day 71'in `OutboxPublisher`'ına benzer bir yapı, ama tek seferlik tetiklenen).
