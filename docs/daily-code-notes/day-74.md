# Day 74 — Kod Notları

Faz 4, Hafta 14, Gün 74 — **Week 14'ün son günü**. Konu: **dead-letter queue** — kalıcı olarak işlenemeyen mesajlar için sonsuza kadar yeniden denemek yerine, ayrı bir "son durak" kuyruğu.

---

## 1. `EventQueueNaming.cs` — dead-letter kuyruğunun adı

```csharp
public static string DeadLetterQueueNameFor<TEvent>(string consumerName) =>
    $"{QueueNameFor<TEvent>(consumerName)}.dead-letter";
// ornek: "fieldops.events.WorkOrderCompletedEvent.notifications.dead-letter"
// -- normal kuyruk adinin sonuna ".dead-letter" ekleniyor, her consumer'in KENDI
// dead-letter kuyrugu olacak sekilde
```

---

## 2. `FailedMessageAttempt.cs` (yeni) — başarısızlık sayacı

```csharp
internal class FailedMessageAttempt
{
    public int Id { get; set; }
    public string ConsumerName { get; set; }
    public string MessageId { get; set; }
    public int AttemptCount { get; set; }
// Day 73'un ProcessedMessage'inin (basari) tam tersi -- bu sefer "kac kez BASARISIZ oldu"
}
```

`WorkOrdersDbContext`'e, `ProcessedMessages`'inkiyle **aynı** `(ConsumerName, MessageId)` üzerinde bir `unique index` ile eklendi.

---

## 3. `EfWorkOrderDirectory.RecordFailedAttempt` — sayacı artırıp yeni değeri döndürüyor

```csharp
public int RecordFailedAttempt(string consumerName, string messageId)
{
    var attempt = _dbContext.FailedMessageAttempts
        .FirstOrDefault(m => m.ConsumerName == consumerName && m.MessageId == messageId);

    if (attempt is null)
    {
        attempt = new FailedMessageAttempt(consumerName, messageId);
        _dbContext.FailedMessageAttempts.Add(attempt);
// bu mesaj icin ilk basarisizlik -- yeni bir satir olusturuluyor
    }

    attempt.AttemptCount++;
// ya yeni olusturulan satirin (0'dan 1'e) ya da var olan satirin sayaci artiyor
    _dbContext.SaveChanges();
    return attempt.AttemptCount;
// caginan tarafa GUNCEL toplam basarisizlik sayisi donduruluyor -- "kac kez denedik" sorusunun cevabi
}
```

---

## 4. `IInboxStore.cs` / `WorkOrderInboxStore.cs` — genişletildi

```csharp
int RecordFailedAttempt(string consumerName, string messageId);
// EventConsumerBase, IWorkOrderDirectory'yi hala hic bilmiyor -- sadece bu soyutlamayi
// kullanmaya devam ediyor, Day 73'teki ayni tasarim gerekcesiyle
```

---

## 5. `EventConsumerBase.cs` — `ReceivedAsync`'in yeniden yapılandırılması

**Önce, `ack` çağrısı artık `finally`'de değil, `try`'ın SONUNDA (başarı yolunda):**
```csharp
consumer.ReceivedAsync += async (_, ea) =>
{
    var messageId = ea.BasicProperties.MessageId;
// artik try disinda -- catch bloğu da buna erisebilsin diye
    try
    {
        // ... Inbox kontrolu, HandleAsync, MarkProcessed (Day 73'teki gibi) ...
        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
// SADECE basari durumunda buraya ulasiliyor -- eskiden finally'deydi, artik burada
    }
    catch (Exception ex)
    {
        Logger.LogWarning(ex, "{ConsumerName} failed to process a {EventType} message", _consumerName, typeof(TEvent).Name);

        try
        {
            await HandleDeliveryFailureAsync(channel, ea, messageId, stoppingToken);
// basarisizlik durumunda NE yapilacagina (tekrar dene mi, dead-letter'a mi tasi) burada karar veriliyor
        }
        catch (Exception handlingEx)
        {
            Logger.LogWarning(handlingEx, "...");
// bu ic ic gecmis try/catch bile kendi icinde korunuyor -- HICBIR SEY ReceivedAsync'ten
// disariya, yakalanmadan sizmamali (Day 50/52'nin dersi)
        }
    }
};
```

**Yeni yardımcı metot — asıl karar burada veriliyor:**
```csharp
private async Task HandleDeliveryFailureAsync(IChannel channel, BasicDeliverEventArgs ea, string? messageId, CancellationToken cancellationToken)
{
    if (string.IsNullOrEmpty(messageId))
    {
        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        return;
// kimligi olmayan bir mesajin denemesi SAYILAMAZ -- bu yuzden ack edilip birakiliyor
// (sonsuz dongude kalmasindan iyidir)
    }

    using var scope = _scopeFactory.CreateScope();
    var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();
    var attemptCount = inboxStore.RecordFailedAttempt(_consumerName, messageId);
// bu basarisizlik KAYDEDILIYOR, ve GUNCEL toplam sayi geri aliniyor

    if (attemptCount < MaxDeliveryAttempts)
    {
        Logger.LogWarning("... will retry ... (attempt {AttemptCount} of {MaxDeliveryAttempts})", /* ... */);
        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
// NACK + requeue:true -- RabbitMQ'ya "bunu ISLEYEMEDIM, ama BASKA BIR SEKILDE (yeniden
// kuyruga koyarak) tekrar dene" deniyor. BasicAckAsync'in tersi -- mesaj kuyruktan
// SILINMIYOR, hemen yeniden teslim ediliyor.
    }
    else
    {
        Logger.LogWarning("... exhausted {MaxDeliveryAttempts} attempts ...; moving it to the dead-letter queue", /* ... */);

        var deadLetterQueueName = EventQueueNaming.DeadLetterQueueNameFor<TEvent>(_consumerName);
        await channel.QueueDeclareAsync(queue: deadLetterQueueName, /* ... */);
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: deadLetterQueueName,
            mandatory: false,
            basicProperties: new BasicProperties { MessageId = messageId },
            body: ea.Body,
// orijinal mesajin BAYTLARI (ea.Body), OLDUGU GIBI, yeni bir kuyruga yayinlaniyor
            cancellationToken: cancellationToken);

        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
// ORIJINAL mesaj simdi ack ediliyor -- normal kuyruktan CIKARILIYOR, artik dongude degil
    }
}
```

**Neden bu şekilde yazıldı:** Day 73'e kadar, `catch` bloğu her zaman **loglayıp mesajı yine de `ack` ediyordu** — yani kalıcı olarak bozuk bir mesaj bile, ilk denemede sessizce siliniyordu. Bugün, başarısızlık sayısına göre **iki farklı yol** açıldı: (1) hâlâ deneme hakkı varsa `BasicNackAsync(..., requeue: true)` — mesaj kuyruğa geri konup **hemen** yeniden teslim ediliyor; (2) hakkı bittiyse, mesajın **bir kopyası** ayrı bir dead-letter kuyruğuna yayınlanıp, orijinali `ack` ediliyor — normal akıştan çıkarılıyor ama **kaybolmuyor**, sadece görülebilir bir yere taşınıyor.

---

## 6. Canlı demonstrasyon — bilerek bozuk bir mesaj gönderme

RabbitMQ'nun yönetim API'si üzerinden, doğrudan exchange'e **geçersiz JSON içeren** bir mesaj gönderildi:
```
POST /api/exchanges/%2F/fieldops.events.WorkOrderCompletedEvent/publish
{"properties":{"message_id":"poison-1"}, "routing_key":"", "payload":"not valid json {{{", ...}
```
Bunun `OutboxPublisher`'ı değil, **doğrudan RabbitMQ'yu** hedeflemesi bilinçli — `OutboxPublisher` zaten payload'ı yayınlamadan önce kendi içinde deserialize ediyor, bozuk bir JSON orada yakalanır ve hiç RabbitMQ'ya ulaşmazdı. Bugün test etmek istediğimiz, **consumer'ın kendi** deserileştirme/işleme hatasına verdiği tepki.

Loglarda, **her iki consumer'ın da bağımsız olarak**, aynı sırayı izlediği görüldü:
```
"audit will retry ... (attempt 1 of 3)"
"notifications will retry ... (attempt 1 of 3)"
"audit will retry ... (attempt 2 of 3)"
"notifications will retry ... (attempt 2 of 3)"
"notifications exhausted 3 attempts ...; moving it to the dead-letter queue"
"audit exhausted 3 attempts ...; moving it to the dead-letter queue"
```
RabbitMQ'nun yönetim API'sinden de doğrulandı:
```
curl .../api/queues → "...notifications.dead-letter" ve "...audit.dead-letter" kuyrukları GERÇEKTEN var
curl .../get (dead-letter'dan) → payload: "not valid json {{{" -- orijinal bozuk mesaj, oldugu gibi, orada duruyor
```

---

## Regresyon (Day 74)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı
dotnet test FieldOps.slnx     → 65/65 (~8.4dk, bilinen ortam yavaşlığı, yeni bir regresyon yok)
dotnet build StockPilot.slnx  → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx   → 0 Hata, 0 Uyarı
```

## Demo basitleştirmesi vs. üretim gereksinimi

Eşik sayısı (3) sabit kodlanmış. Dead-letter kuyruğundaki mesajları incelemek/yeniden oynatmak (replay) için bir araç yok — sadece "kaybolmadan bir yere konması" hedeflendi. `FailedMessageAttempts` tablosu da (Inbox'ın `ProcessedMessages`'ı gibi) süresiz büyüyor, temizleme yok.

---

## Week 14 kapanışı

Week 14'ün roadmap listesindeki tüm maddeler tamamlandı: outbox pattern (Day 71), retry + exponential backoff (Day 72), inbox pattern + idempotent consumer'lar (Day 73), dead-letter queue (Day 74). **Week 14 — Distributed FieldOps'un ikinci haftası — tamamlandı.** Day 75'ten itibaren **Week 15 — mikroservis sınırları, veri sahipliği, servis çıkarma** başlıyor.
