# Day 80 — Elasticsearch İndeksleme Akışı (Outbox Üzerinden): Bir İsteğin Üç Ayrı Zaman Çizelgesine Yayılması

Bu doküman, `day-71-rabbitmq-akis-detay.md` ile **birebir aynı formatta** — her arayüz çağrısında "bu metot gerçekte hangi sınıfa gidiyor" sorusu `Program.cs`/`WorkOrdersModule.cs`'teki DI kaydına bakılarak **adım adım** takip ediliyor, hiçbir yer atlanmıyor. Bugünkü fark: RabbitMQ değil, **Elasticsearch** hedefi — ama mekanizma (Outbox pattern) birebir aynı.

**Senaryo 1 — YENİ bir iş emri oluşturuluyor** (bu, Day 80'in en çok kafa karıştıran kısmını, `Create`'in callback'ini, gösteriyor):
```
POST /api/workorders
X-Organization-Id: 1
X-Employee-Id: 1
{"title":"Fix the pump","customerId":7}
```

**Üç ayrı zaman çizelgesi var:**
1. HTTP isteğinin kendisi (saniyeler içinde biter)
2. `OutboxPublisher` — veritabanını periyodik olarak yoklayan arka plan döngüsü (Day 71'de RabbitMQ için kurulmuştu, Day 80'de Elasticsearch için de kullanılıyor)
3. Elasticsearch'ün kendisi — bir consumer DEĞİL, `OutboxPublisher`'ın doğrudan HTTP ile konuştuğu dış bir servis

1. numaralı çizelge, 2. numaralıyı **doğrudan çağırmıyor** — sadece veritabanına bir iz bırakıyor. 2. numaralı çizelge, RabbitMQ'daki gibi bir "consumer"a değil, doğrudan Elasticsearch'ün REST API'sine HTTP isteği atıyor — aradaki fark, RabbitMQ'nun kendi kuyruğunda mesajı TUTMASI, Elasticsearch'ün ise böyle bir "bekleme" kavramı OLMAMASI (`OutboxPublisher` başarısız olursa, mesaj sadece veritabanında bekliyor, Elasticsearch'ün kendisinde değil).

---

## 0. İstek `WorkOrdersController.Create`'e ulaşıyor

`id` henüz yok — bu, `Create`'i `Complete`'ten (Day 71'in akışı) yapısal olarak FARKLI kılan şey.

---

## 1. `WorkOrdersController.Create` — callback'in kurulduğu yer

`src/FieldOps.Api/Controllers/WorkOrdersController.cs`:
```csharp
var workOrder = _workOrderDirectory.Create(request.Title, organizationId!.Value, request.CustomerId, newId =>
    [new OutboxEntry(
        nameof(WorkOrderSearchDocument),
        JsonSerializer.Serialize(new WorkOrderSearchDocument(newId, organizationId!.Value, request.Title, WorkOrderStatus.Open)))]);
// request.Title = "Fix the pump", organizationId = 1, request.CustomerId = 7
//
// DIKKAT: "newId =>" ile baslayan kisim, HENUZ CALISMIYOR. Bu bir lambda --
// yani "eger/ne zaman GERCEK Id'yi ogrenirsen, JSON'u O ZAMAN boyle kur" diyen
// bir TALIMAT. Su an tek yapilan, bu talimati _workOrderDirectory.Create'e
// PARAMETRE olarak GECIRMEK.
```

---

## 2. `_workOrderDirectory.Create(...)` — bu satır gerçekte ne çalıştırıyor?

### 2a. DI konteyneri hangi sınıfı verdiğine nasıl karar veriyor

`src/FieldOps.Modules.WorkOrders/WorkOrdersModule.cs`:
```csharp
return services.AddScoped<IWorkOrderDirectory, EfWorkOrderDirectory>();
```
→ `_workOrderDirectory.Create(...)` gerçekte **`EfWorkOrderDirectory.Create`**'e gidiyor (Day 71'deki `Complete` ile aynı DI kaydı).

### 2b. `EfWorkOrderDirectory.Create` — callback'in GERÇEKTEN çağrıldığı yer

`src/FieldOps.Modules.WorkOrders/Data/EfWorkOrderDirectory.cs`:
```csharp
public WorkOrderSummary Create(string title, int organizationId, int? customerId, Func<int, IReadOnlyList<OutboxEntry>> buildOutboxEntries)
{
    using var transaction = _dbContext.Database.BeginTransaction();
    // Day 71'in TEK SaveChanges'i BURADA yetmiyor -- asagida neden aciklaniyor.

    var workOrder = new WorkOrder(title, organizationId, WorkOrderStatus.Open) { CustomerId = customerId };
    _dbContext.WorkOrders.Add(workOrder);
    _dbContext.SaveChanges();
    // BU satirdan ONCE: workOrder.Id = 0 (henuz atanmamis, C#'in int varsayilan degeri)
    // BU satirdan SONRA: workOrder.Id = 34 (SQL Server'in IDENTITY sutunundan GERCEKTEN geldi)
    // -- iste TAM BURADA, adim 1'deki "newId =>" lambda'sinin ihtiyac duydugu deger DOGUYOR.

    foreach (var entry in buildOutboxEntries(workOrder.Id))
    // ISTE BURADA CAGRILIYOR: buildOutboxEntries(34)
    // -> adim 1'deki lambda CALISIYOR: newId=34 ile
    //    new WorkOrderSearchDocument(34, 1, "Fix the pump", WorkOrderStatus.Open)
    //    JsonSerializer.Serialize(...) -> "{\"Id\":34,\"OrganizationId\":1,\"Title\":\"Fix the pump\",\"Status\":0}"
    //    (WorkOrderStatus.Open = 0, System.Text.Json enum'lari SAYI olarak yazar)
    // -> geriye TEK elemanli bir OutboxEntry dizisi donuyor
    {
        _dbContext.OutboxMessages.Add(new OutboxMessage(entry.EventType, entry.Payload));
        // entry.EventType = "WorkOrderSearchDocument", entry.Payload = yukaridaki JSON
    }
    _dbContext.SaveChanges();
    // IKINCI SaveChanges -- outbox satiri simdi veritabaninda.

    transaction.Commit();
    // Iki SaveChanges de basarili oldu -- KALICI hale getiriliyor. Biri BASARISIZ
    // olsaydi (ornegin ikinci SaveChanges bir hata firlatsaydi), COMMIT hic
    // CALISMAZDI ve SQL Server WorkOrder satirini da GERI ALIRDI -- "var ama
    // hicbir zaman indekslenmeyecek bir is emri" durumu OLUSAMAZ.
    return ToSummary(workOrder);
}
```

**Bu noktada veritabanının içinde:** `WorkOrders` tablosunda `Id=34` satırı VE `OutboxMessages` tablosunda `{ Id: Y, EventType: "WorkOrderSearchDocument", Payload: "...", PublishedAtUtc: NULL }` satırı **ikisi de gerçekten var** — ama Elasticsearch bundan **henüz hiç haberdar değil**.

---

## 3. `Create`, Elasticsearch'ü hiç beklemeden yanıt dönüyor

```csharp
_workOrderReportService.InvalidateCache(organizationId.Value);
var dto = ToDto(workOrder);
// ...
return StatusCode(StatusCodes.Status201Created, dto);
// istemciye 201 Created -- TAM BURADA. Elasticsearch'e hic baglanilmadi.
```

---

## 4. Bambaşka bir zaman çizelgesinde: `OutboxPublisher` zaten çalışıyordu

`src/FieldOps.Api/Program.cs`:
```csharp
builder.Services.AddHostedService<OutboxPublisher>();
```

Bir tick sırasında (adım 3'ten sonraki ilk 5 saniyelik pencerede), `src/FieldOps.Api/Application/OutboxPublisher.cs`:
```csharp
var workOrderSearchIndex = scope.ServiceProvider.GetRequiredService<IWorkOrderSearchIndex>();
// Day 80'de yeni eklendi -- asagida 4a'da takip ediliyor.

foreach (var message in workOrderDirectory.GetUnpublishedOutboxMessages())
// adim 2b'de yazilan satiri BULUYOR: { Id=Y, EventType="WorkOrderSearchDocument", Payload="..." }
{
    try
    {
        if (message.EventType == nameof(WorkOrderCompletedEvent)) { /* Day 71'in yolu -- bugun eslesmiyor */ }
        else if (message.EventType == nameof(WorkOrderSearchDocument))
        // "WorkOrderSearchDocument" == "WorkOrderSearchDocument" -> ESLESTI
        {
            var document = JsonSerializer.Deserialize<WorkOrderSearchDocument>(message.Payload);
            // JSON metni -> { Id=34, OrganizationId=1, Title="Fix the pump", Status=Open } nesnesine donuyor

            if (document is not null)
            {
                await workOrderSearchIndex.IndexAsync(document, cancellationToken);
                // -- ASAGIDA 4a/4b'de takip ediliyor --
                workOrderDirectory.MarkOutboxMessagePublished(message.Id);
                // SADECE yukaridaki satir basariyla tamamlandiysa buraya geliniyor
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to publish outbox message {OutboxMessageId}", message.Id);
        // BURAYA dusulurse MarkOutboxMessagePublished HIC CAGRILMAZ -- satir
        // "yayinlanmamis" kalir, bir sonraki tick'te TEKRAR denenir.
    }
}
```

### 4a. `workOrderSearchIndex.IndexAsync(...)` — yine aynı yöntemle geriye gidiyoruz

`Program.cs`:
```csharp
builder.Services.AddSingleton<IWorkOrderSearchIndex, ElasticsearchWorkOrderSearchIndex>();
```
→ `workOrderSearchIndex.IndexAsync(...)` gerçekte **`ElasticsearchWorkOrderSearchIndex.IndexAsync`**'e gidiyor.

### 4b. `ElasticsearchWorkOrderSearchIndex.IndexAsync` — belge Elasticsearch'e gerçekten gidiyor

`src/FieldOps.Api/Application/ElasticsearchWorkOrderSearchIndex.cs`:
```csharp
public async Task IndexAsync(WorkOrderSearchDocument document, CancellationToken cancellationToken)
{
    // document = { Id=34, OrganizationId=1, Title="Fix the pump", Status=Open }
    var response = await _client.IndexAsync(document, request => request.Index(IndexName).Id(document.Id), cancellationToken);
    // ALTTAN ALTA gerceklesen HTTP istegi:
    //   PUT http://localhost:9200/workorders/_doc/34
    //   Govde: {"id":34,"organizationId":1,"title":"Fix the pump","status":"Open"}
    // (document.Id=34, Elasticsearch'teki belgenin KENDI kimligi olarak kullaniliyor)

    if (!response.IsValidResponse)
    {
        throw new InvalidOperationException($"Failed to index work order {document.Id}: {response.DebugInformation}");
        // Day 80'de CANLI YAKALANAN duzeltme: Elasticsearch ulasilamazsa,
        // _client.IndexAsync KENDISI istisna FIRLATMAZ -- response.IsValidResponse
        // false doner. Bu satir olmasaydi, yukaridaki catch (Exception ex) HICBIR
        // ZAMAN calismazdi, ve MarkOutboxMessagePublished HER ZAMAN cagrilirdi --
        // Elasticsearch kapali olsa bile "basarili" sanilirdi.
    }
    // BASARILI ise: MESAJ SIMDI GERCEKTEN Elasticsearch'te -- adim 3'ten bu yana GECEN SURE: 0-5 saniye arasi
}
```

---

## Senaryo 2 — `Complete`: aynı outbox satırının içinden İKİ farklı hedef çıkıyor

`Complete`, `Create`'in aksine `Id`'yi zaten biliyor — bu yüzden callback'e gerek yok, ama bugün **iki** outbox satırı birden yazıyor:

```csharp
var updated = _workOrderDirectory.Complete(id, [
    new OutboxEntry(nameof(WorkOrderCompletedEvent), eventPayload),      // -> RabbitMQ'ya gidecek (Day 71)
    new OutboxEntry(nameof(WorkOrderSearchDocument), indexPayload)       // -> Elasticsearch'e gidecek (bugun)
]);
```

`OutboxPublisher`'ın **aynı** `foreach` döngüsü, **aynı tick'te**, bu iki satırı sırayla işliyor — biri `if (message.EventType == nameof(WorkOrderCompletedEvent))` dalına düşüp `RabbitMqEventPublisher` üzerinden RabbitMQ'ya gidiyor (day-71-rabbitmq-akis-detay.md'nin adım 5-6'sı, değişmedi), diğeri `else if` dalına düşüp yukarıdaki 4a/4b'ye gidiyor. **İki hedef, tek döngü, tek mesaj tipi ayrım noktası** (`message.EventType`).

---

## Senaryo 3 — `Search`: outbox'tan hiç geçmeyen, TEK zaman çizelgeli okuma yolu

```
GET /api/workorders/search?q=pump
X-Organization-Id: 1
```

```csharp
var results = await _workOrderSearchIndex.SearchAsync(organizationId!.Value, q, cancellationToken);
// DOGRUDAN cagriliyor -- outbox YOK, arka plan bekleme YOK. Search bir "yaz"
// islemi degil, "oku" islemi -- Outbox pattern SADECE yazmalari (Create/Complete'in
// SQL'i bozmadan Elasticsearch'e de yansitmasi) korumak icin var, okumalari degil.
```
`ElasticsearchWorkOrderSearchIndex.SearchAsync`, `_client.SearchAsync<WorkOrderSearchDocument>(...)`'i çağırıp `GET http://localhost:9200/workorders/_search` isteğini **hemen, aynı HTTP isteği içinde** atıyor — Elasticsearch o an kapalıysa, `Search` endpoint'i de (aynı `IsValidResponse` kontrolüyle) bir istisna fırlatıyor ve çağıran bunu doğrudan görüyor. Bu bilinçli bir asimetri: **yazma** (indeksleme) outbox ile toleranslı, **okuma** (arama) değil — çünkü aranacak güncel bir şey yoksa, aramanın "sonuç yok" gibi görünüp gerçekte "motor çalışmıyor" olması yanıltıcı olurdu (bkz. `day-80.md`'nin 7. bölümü).

---

## Özet — tek bakışta akış (Create senaryosu, üç zaman çizelgesi)

```
[ZAMAN ÇİZELGESİ 1 — HTTP isteği, saniyeler içinde biter]

İstemci: POST /api/workorders {"title":"Fix the pump","customerId":7}
   ▼
WorkOrdersController.Create
   │  callback = (newId => [OutboxEntry("WorkOrderSearchDocument", JSON-with-newId)])
   ▼
_workOrderDirectory.Create(title, org, customerId, callback)
   ▼
EfWorkOrderDirectory.Create
   │  BEGIN TRANSACTION
   │  WorkOrders.Add(...) + SaveChanges()   -> workOrder.Id = 34 (ARTIK BİLİNİYOR)
   │  callback(34) -> OutboxEntry'ler üretildi
   │  OutboxMessages.Add(...) + SaveChanges()
   │  COMMIT
   ▼
Create -> 201 Created -> İstemciye   <-- BURADA BİTER (Elasticsearch'e hiç dokunmadan)


[ZAMAN ÇİZELGESİ 2 — OutboxPublisher, uygulama başladığından beri, her 5 saniyede bir]

(bekliyordu) ... PeriodicTimer tick ...
   ▼
GetUnpublishedOutboxMessages() -> Zaman Çizelgesi 1'in yazdığı satırı BULUYOR
   ▼
message.EventType == "WorkOrderSearchDocument" -> eşleşti
   ▼
JsonSerializer.Deserialize<WorkOrderSearchDocument>(message.Payload)
   ▼
workOrderSearchIndex.IndexAsync(document) -> ElasticsearchWorkOrderSearchIndex
   │  (canlı denemede: Elasticsearch o an kapalıysa response.IsValidResponse=false,
   │   BİZ istisna fırlatıyoruz, mesaj İŞARETLENMEZ, tekrar denenir)
   ▼
BAŞARILI ise: MarkOutboxMessagePublished(message.Id) -> PublishedAtUtc = now


[ZAMAN ÇİZELGESİ 3 — Elasticsearch'ün kendisi, bir "consumer" değil, dış bir servis]

(dinlemiyordu, sadece HTTP isteği bekliyordu) ...
   ▼
PUT /workorders/_doc/34  Body: {"id":34,"organizationId":1,"title":"Fix the pump","status":"Open"}
   ▼
Elasticsearch, dynamic mapping ile belgeyi kabul ediyor, _version:1 ile kaydediyor
```

**En önemli çıkarım — RabbitMQ akışıyla farkı:** Day 71'in dördüncü zaman çizelgesi (consumer'lar) **sürekli dinleyen, RabbitMQ'nun kendisinin mesajı dağıttığı** bir yapıydı. Bugünkü üçüncü zaman çizelgesi (Elasticsearch) öyle değil — Elasticsearch hiçbir şeyi "dinlemiyor", sadece `OutboxPublisher`'ın attığı HTTP isteğine **o an** cevap veriyor. Bu yüzden Elasticsearch tarafında "kuyruk", "exchange", "binding" gibi kavramlar hiç yok — sadece klasik bir istemci/sunucu HTTP çağrısı, tek farkı bu çağrının **`OutboxPublisher` tarafından, gecikmeli ve tekrar denenebilir şekilde** yapılması.
