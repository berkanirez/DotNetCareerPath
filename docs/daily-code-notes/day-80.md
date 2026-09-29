# Day 80 — Kod Notları

Faz 4, Hafta 16, Gün 80. Konu: **index senkronizasyonu** — Day 79'da bilerek kabul edilen riski (Elasticsearch indekslemesinin `Create`/`Complete`'in kendi HTTP isteği içinde, senkron olması) kapatmak. Yeni bir desen İCAT ETMİYORUZ — Day 71'in Outbox pattern'ini **aynen tekrar kullanıyoruz**.

---

## 1. `OutboxEntry.cs` (yeni) — bir mutasyonun birden fazla outbox satırı yazabilmesi

```csharp
public record OutboxEntry(string EventType, string Payload);
// Day 71'e kadar, Complete tek bir (EventType, Payload) ciftini kabul
// ediyordu -- bugun Complete'in HEM RabbitMQ'ya bir event HEM Elasticsearch'e
// bir indeksleme istegi yazmasi gerekiyor. Bu kucuk record, "birden fazla
// outbox satirini AYNI SaveChanges cagrisinda yaz" ihtiyacini karsiliyor.
```

---

## 2. `IWorkOrderDirectory.cs` — `Create` neden bir `Func` (callback) alıyor

```csharp
WorkOrderSummary Create(string title, int organizationId, int? customerId, Func<int, IReadOnlyList<OutboxEntry>> buildOutboxEntries);
```

**Neden bu şekilde yazıldı — gerçek bir sorun çözüyor, süs değil:** `Complete` için outbox payload'ını kolayca ÖNCEDEN kurabiliyorduk (Day 71), çünkü tamamlanan iş emrinin `Id`'si zaten biliniyordu. Ama `Create` için YENİ bir iş emri oluşturuyoruz — ve o iş emrinin `Id`'si (SQL Server'ın `IDENTITY` sütunundan gelen, otomatik artan bir sayı) **veritabanına gerçekten yazılana kadar bilinmiyor.** Elasticsearch'e giden arama belgesi ise TAM OLARAK bu `Id`'yi içermek zorunda (aynı iş emri tekrar indekslendiğinde AYNI belgenin üzerine yazılsın diye). Yani: controller, outbox payload'ını Create çağrılmadan ÖNCE kuramaz — henüz var olmayan bir `Id`'yi JSON'a koyamaz.

**Çözüm:** Controller, JSON'u kendisi kurmak yerine, "bana gerçek Id'yi ver, ben JSON'u O ZAMAN kurarım" diyen bir **fonksiyon** (`Func<int, IReadOnlyList<OutboxEntry>>`) veriyor. `EfWorkOrderDirectory.Create`, iş emrini veritabanına yazıp gerçek `Id`'yi öğrendiği AN bu fonksiyonu çağırıyor, dönen outbox satırlarını da AYNI transaction içinde kaydediyor.

---

## 3. `EfWorkOrderDirectory.Create` — iki `SaveChanges`, tek transaction

```csharp
public WorkOrderSummary Create(string title, int organizationId, int? customerId, Func<int, IReadOnlyList<OutboxEntry>> buildOutboxEntries)
{
    using var transaction = _dbContext.Database.BeginTransaction();
    // Day 71'e kadar HER outbox yazimi TEK bir SaveChanges'e sigiyordu.
    // Bugun ilk defa BUNU YAPAMIYORUZ -- Id, ilk SaveChanges CALISMADAN
    // bilinmiyor. Elle acilan bir transaction, iki AYRI SaveChanges'i
    // yine de TEK bir atomik birim haline getiriyor: ikisi de basarili
    // olur ya da IKISI DE geri alinir (rollback).

    var workOrder = new WorkOrder(title, organizationId, WorkOrderStatus.Open) { CustomerId = customerId };
    _dbContext.WorkOrders.Add(workOrder);
    _dbContext.SaveChanges();
    // BU satirdan SONRA, workOrder.Id artik GERCEK, veritabaninin
    // urettigi degere sahip -- SaveChanges cagrisi, INSERT'i calistirip
    // IDENTITY sutununun urettigi degeri EF Core'a geri okutuyor ve
    // izlenen (tracked) workOrder nesnesine yaziyor.

    foreach (var entry in buildOutboxEntries(workOrder.Id))
    {
        _dbContext.OutboxMessages.Add(new OutboxMessage(entry.EventType, entry.Payload));
    }
    _dbContext.SaveChanges();
    // Simdi callback cagriliyor, GERCEK Id ile -- controller'in kurdugu
    // JSON artik dogru Id'yi iceriyor. Bu ikinci SaveChanges, outbox
    // satirlarini yaziyor.

    transaction.Commit();
    // Iki SaveChanges de basarili oldu -- transaction'i KALICI hale
    // getiriyoruz. Ikisinden biri BASARISIZ olsaydi (orn. crash), commit
    // hic calismazdi ve SQL Server transaction'i kendiliginden geri alirdi
    // -- yani WorkOrder'in KENDISI bile hic var olmamis gibi kalirdi,
    // "var ama indekslenmeyecek" gibi yarim bir durum OLUSAMAZ.
    return ToSummary(workOrder);
}
```

**Bunu neden varsaymadık, düşünerek bulduk:** İlk yaklaşım "tek SaveChanges'e sığdır" olacaktı (Day 71'in deseni), ama `Id`'nin ne zaman var olduğunu düşününce bunun mümkün olmadığı ortaya çıktı — bu, "framework'ün ne yaptığını varsayma, düşün" ilkesinin bu sefer kod ÇALIŞTIRILMADAN, sadece tasarım aşamasında uygulanmış hali.

---

## 4. `EfWorkOrderDirectory.Complete` — artık bir liste, `foreach` ile yazılıyor

```csharp
public WorkOrderSummary? Complete(int workOrderId, IReadOnlyList<OutboxEntry> outboxEntries)
{
    // ... (mevcut durum kontrolu ayni) ...
    workOrder.Status = WorkOrderStatus.Completed;

    foreach (var entry in outboxEntries)
    {
        _dbContext.OutboxMessages.Add(new OutboxMessage(entry.EventType, entry.Payload));
    }
    // Complete'in Id'si zaten BILINIYOR (parametre olarak geldi) -- bu
    // yuzden Create'teki gibi iki SaveChanges'e/transaction'a GEREK YOK,
    // Day 71'deki TEK SaveChanges deseni aynen devam ediyor, sadece
    // TEK satir yerine BIRDEN FAZLA satir eklenebiliyor.

    _dbContext.SaveChanges();
    return ToSummary(workOrder);
}
```

---

## 5. `WorkOrdersController.cs` — `Create`/`Complete` tekrar SENKRON

```csharp
// Create icinde:
var workOrder = _workOrderDirectory.Create(request.Title, organizationId!.Value, request.CustomerId, newId =>
    [new OutboxEntry(
        nameof(WorkOrderSearchDocument),
        JsonSerializer.Serialize(new WorkOrderSearchDocument(newId, organizationId!.Value, request.Title, WorkOrderStatus.Open)))]);
// "newId =>" -- bu lambda, EfWorkOrderDirectory.Create'in bize GERI
// CAGIRDIGI (callback) fonksiyon. Icinde, artik bilinen newId ile
// WorkOrderSearchDocument'i kuruyoruz, JSON'a ceviriyoruz, tek elemanli
// bir dizi (OutboxEntry[]) donduruyoruz.
```
```csharp
// Complete icinde:
var indexPayload = JsonSerializer.Serialize(
    new WorkOrderSearchDocument(id, organizationId!.Value, workOrderBeforeCompletion.Title, WorkOrderStatus.Completed));
// Id zaten biliniyor (parametre "id"), Status'u da ELLE "Completed" yaziyoruz --
// _workOrderDirectory.Complete HENUZ CALISMADI, ama Complete'in NE YAPACAGINI
// zaten biliyoruz (InProgress -> Completed), bu yuzden sonucu BEKLEMEYE gerek yok.

var updated = _workOrderDirectory.Complete(id, [
    new OutboxEntry(nameof(WorkOrderCompletedEvent), eventPayload),
    new OutboxEntry(nameof(WorkOrderSearchDocument), indexPayload)
]);
// IKI outbox satiri BIRDEN, tek bir listede -- biri RabbitMQ icin, biri
// Elasticsearch icin. Ikisi de AYNI SaveChanges'te, Complete'in kendi
// Status degisikligiyle birlikte yaziliyor.
```

**Sonuç olarak `Create` ve `Complete`, artık `async` DEĞİL** — `IWorkOrderSearchIndex`'e (Elasticsearch'e) hiç doğrudan gitmiyorlar, o yüzden `await` edecek bir şey kalmadı. Day 79'da "arama indekslemesi için await eklemek zorunda kaldık" dediğimiz şey, bugün tamamen geri alındı — ama Elasticsearch'e indeksleme YİNE oluyor, sadece istekten SONRA, arka planda.

---

## 6. `OutboxPublisher.cs` — ikinci bir `EventType` dalı

```csharp
var workOrderSearchIndex = scope.ServiceProvider.GetRequiredService<IWorkOrderSearchIndex>();
// Day 71'den beri sadece IWorkOrderDirectory + IEventPublisher cozuluyordu --
// bugun bu ucuncusu eklendi, cunku artik BU sinif Elasticsearch'e de yaziyor.

// mevcut "if (message.EventType == nameof(WorkOrderCompletedEvent))" dalinin YANINA:
else if (message.EventType == nameof(WorkOrderSearchDocument))
{
    var document = JsonSerializer.Deserialize<WorkOrderSearchDocument>(message.Payload);
    if (document is not null)
    {
        await workOrderSearchIndex.IndexAsync(document, cancellationToken);
        workOrderDirectory.MarkOutboxMessagePublished(message.Id);
    }
}
// Day 71/73'un AYNI toleransi, simdi Elasticsearch icin: IndexAsync patlarsa
// (Elasticsearch o an kapaliysa), bu satir hic calismaz, MarkOutboxMessagePublished
// CAGRILMAZ, satir "yayinlanmadi" olarak kalir -- bir SONRAKI tikte AYNI dongude
// tekrar denenir. Nothing lost.
```

---

## 7. Canlı testte yakalanan gerçek bir hata — `ElasticsearchClient` istisna FIRLATMIYOR

Elasticsearch kapalıyken bir iş emri oluşturup tamamladık — HTTP istekleri beklendiği gibi başarılı döndü. Ama outbox satırına bakınca **beklenmedik** bir şey görüldü: satır `PublishedAtUtc` dolu, yani **"başarıyla yayınlandı" olarak işaretlenmiş** — Elasticsearch o an KAPALI olmasına rağmen!

**Sebebi bulmak için küçük, tek amaçlı bir deneme projesi yazıldı** (`dotnet new console`, sadece `_client.IndexAsync(...)`'i çağırıp sonucu yazdıran birkaç satır). Sonuç:
```
No exception thrown.
IsValidResponse: False
HasSuccessfulStatusCode: False
```
**Gerçek sebep:** `RabbitMQ.Client` (Day 66'dan beri kullandığımız), bağlantı başarısız olduğunda **istisna fırlatıyor** — `EventConsumerBase`'in `catch (Exception ex)` blokları bu yüzden çalışıyor. `Elastic.Clients.Elasticsearch` ise **FARKLI bir felsefeyle** yazılmış: bağlantı başarısız olsa bile istisna fırlatmıyor, bunun yerine `IsValidResponse: false` olan bir **cevap nesnesi** döndürüyor. Bizim kodumuz bu cevabı hiç kontrol etmiyordu — `await` bitince "bir şey patlamadı, demek ki başarılı" diye düşünüp devam ediyordu. `OutboxPublisher`'ın tüm yeniden deneme mantığı ise **istisna yakalamaya** dayanıyor (`catch (Exception ex)` → satırı işaretleme). İstisna hiç fırlamayınca, `OutboxPublisher` da "başarılı" sanıp satırı işaretliyordu — **Elasticsearch'e hiçbir şey yazılmamış olsa bile.**

**Düzeltme:**
```csharp
var response = await _client.IndexAsync(document, request => request.Index(IndexName).Id(document.Id), cancellationToken);
if (!response.IsValidResponse)
{
    throw new InvalidOperationException($"Failed to index work order {document.Id}: {response.DebugInformation}");
}
```
Artık cevabı elle kontrol edip, başarısızsa **kendimiz** bir istisna fırlatıyoruz — böylece `OutboxPublisher`'ın zaten var olan, RabbitMQ için yazılmış istisna-yakalama mantığı, Elasticsearch için de doğru çalışıyor. Aynı düzeltme `SearchAsync`'e de eklendi (aksi halde Elasticsearch kapalıyken arama, "sonuç bulunamadı" ile "arama motoru çalışmıyor" durumlarını birbirinden AYIRT EDEMEZDİ).

**Canlı olarak yeniden doğrulandı:** Düzeltmeden sonra, Elasticsearch kapalıyken oluşturulan yeni bir iş emrinin outbox satırı artık doğru şekilde `PublishedAtUtc: NULL` kalıyor, loglarda `"Failed to publish outbox message 6"` uyarısı her 5 saniyede tekrarlanıyor; Elasticsearch açıldıktan birkaç saniye sonra satır gerçekten `PublishedAtUtc` ile işaretleniyor VE belge Elasticsearch'te gerçekten bulunuyor (`"found": true`).

**Bu neden önemli — genel ders:** Bir kütüphanenin hata bildirme şeklini **varsaymak** (RabbitMQ'nun yaptığı gibi istisna fırlatacağını düşünmek) yanlış olabilir. Day 66'dan beri tekrarlanan "framework'ün ne yaptığını varsayma, gözlemle" ilkesinin bu seferki hâli — ve bu sefer gözlemleme, gerçek bir üretim hatasını (sessizce kaybolan indeksleme istekleri) canlı demoda yakalayıp önledi.

---

## Regresyon (Day 80)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65, 24dk36sn
```
Bu ikinci test çalıştırması (bölüm 7'deki düzeltmeden sonra) her zamankinden (~8-9dk) belirgin şekilde daha uzun sürdü — ama bunun sebebi bilinmiyor değil: bu çalıştırma sırasında AYNI makinede paralel olarak canlı demo için Elasticsearch/RabbitMQ konteynerleri, bir `dotnet new console` deneme projesi ve elle SQL sorguları da çalıştırılıyordu. Bu, Day 69'un ayrıca teşhis ettiği bilinmeyen bir ortam yavaşlığı değil, doğrudan gözlemlenebilir bir kaynak çakışması (Docker + dotnet build + test'in aynı anda CPU/disk paylaşması) — yeniden araştırma gerektirmiyor.

## Demo basitleştirmesi vs. üretim gereksinimi

`Create`'teki elle açılan transaction, bugünün tek "yeni" karmaşıklığı — ama gerçek bir problemi çözüyor, süs değil. Aynı `OutboxMessages` tablosu artık İKİ farklı `EventType` taşıyor (`WorkOrderCompletedEvent`, `WorkOrderSearchDocument`) — üretimde muhtemelen bu ikisi ayrı tablolarda tutulurdu, ama bugünkü ölçekte paylaşmak gerçek bir sorun yaratmıyor (Day 79'un planında da böyle öngörülmüştü).

Bölüm 7'de anlatılan hatanın DÜZELTİLMEDEN önce oluşturduğu iki outbox satırı (Id 3 ve 5), veritabanında hâlâ "yayınlandı" olarak işaretli duruyor — ama Elasticsearch'e hiçbir zaman gerçekten yazılmadılar. Bu, düzeltmeden ÖNCEKİ, artık geride kalmış bir veri hatası; bugünün kapsamı bu iki satırı geriye dönük düzeltmek değildi (gerçek üretimde böyle bir durum fark edilirse, elle bir "reindex" ile telafi edilirdi).
