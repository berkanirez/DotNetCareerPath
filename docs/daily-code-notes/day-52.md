# Day 52 — Kod Notları

Faz 3, Hafta 10, Gün 52. Konu: **Audit Logs** — FieldOps'un planlanan on modülünden biri, ilk kez ele alınıyor. Bir iş emri `Assign`/`Complete`/`Approve` edildiğinde, "kim, neyi, ne zaman yaptı" bilgisini kalıcı, silinmez bir kayıt olarak tutmak.

---

## 1. Gerçek problem

`WorkOrders` tablosu sadece **şu anki durumu** gösteriyor — geçmişi yok. Bir anlaşmazlık çıksa ("bu iş emrini kim onayladı, ne zaman?"), cevaplayacak hiçbir şey yok. `AuditLogs`, bu geçmişi tutan, **append-only** (sadece ekleme, hiç güncelleme/silme) bir defter.

---

## 2. Yeni modül — tanıdık 5 dosyalık desen (Day 32'den beri)

`FieldOps.Modules.AuditLogs` — diğer modüllerle **birebir aynı iskelet**, sadece bu sefer okuma değil sadece **yazma** var:

**`Domain/AuditLogEntry.cs`** (`internal`):
```csharp
internal class AuditLogEntry
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int WorkOrderId { get; set; }
    public string Action { get; set; }
    public string ActorType { get; set; }
    public int ActorId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    // ...
}
```
Bilerek minimal: bir "Details" JSON alanı yok, `OrganizationId` ADR 0002 gereği düz bir `int` (diğer modüllere referans yok).

**`IAuditLogWriter.cs`** (public arayüz):
```csharp
public interface IAuditLogWriter
{
    void Record(int organizationId, int workOrderId, string action, string actorType, int actorId);
}
```
Diğer modüllerin arayüzlerinden (`ICustomerDirectory` vb.) farklı olarak **sadece yazma** var — `GetAll`/`GetById` yok, çünkü bugün hiçbir okuma/raporlama özelliği yok (bilinçli, ertelenmiş kapsam).

**`Data/AuditLogsDbContext.cs`** — seed verisi yok (diğer modüllerin aksine, bir audit log gerçekten boş başlar).

**`Data/EfAuditLogWriter.cs`** — asıl implementasyon, aşağıda detaylı.

**`AuditLogsModule.cs`** — `AddAuditLogsModule(connectionString)`, diğerleriyle birebir aynı desen.

---

## 3. `EfAuditLogWriter` — Day 51'in dersi baştan uygulanıyor

```csharp
internal class EfAuditLogWriter : IAuditLogWriter
{
    private readonly AuditLogsDbContext _dbContext;
    private readonly ILogger<EfAuditLogWriter> _logger;

    public void Record(int organizationId, int workOrderId, string action, string actorType, int actorId)
    {
        try
        {
            _dbContext.AuditLogEntries.Add(new AuditLogEntry(organizationId, workOrderId, action, actorType, actorId, DateTime.UtcNow));
            _dbContext.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record audit log entry for work order {WorkOrderId} ({Action})", workOrderId, action);
        }
    }
}
```

**Neden `try`/`catch` en baştan burada, Day 51'deki gibi sonradan keşfedilip düzeltilmek yerine:** `Record` çağrıldığı anda, iş emri **zaten gerçekten** `Assigned`/`Completed`/`Approved` olmuş durumda — bu, o gerçeği kaydetmeye çalışan bir **yan etkiden** başka bir şey değil. Bir veritabanı hatası (bağlantı kopması, geçici bir kilitlenme) bu kaydı engellese bile, asıl işlemi (iş emrinin durumunu değiştirmeyi) geri almanın hiçbir anlamı yok. Day 51'de bu dersi `InvalidateCache`'e sonradan eklemiştik; bugün aynı dersi **en baştan**, modülün ilk satırından itibaren uyguladık.

**Neden `try`/`catch` `WorkOrdersController`'ın üç çağrı noktasında değil de `EfAuditLogWriter`'ın kendi içinde:** Day 51'in `InvalidateCache` düzeltmesiyle aynı gerekçe — koruma, riskli I/O'nun **yanında**, tek bir yerde olmalı; controller'da üç kez tekrarlanmamalı.

---

## 4. `WorkOrdersController`'a bağlanması

```csharp
// Assign, basarili mutasyondan sonra:
_workOrderReportService.InvalidateCache(organizationId!.Value);
_auditLogWriter.Record(organizationId!.Value, id, "Assigned", "Employee", actingEmployeeId!.Value);

// Complete, basarili mutasyondan sonra:
_workOrderReportService.InvalidateCache(organizationId!.Value);
_auditLogWriter.Record(organizationId!.Value, id, "Completed", "Employee", actingEmployeeId!.Value);

// Approve, basarili mutasyondan sonra:
_auditLogWriter.Record(organizationId.Value, id, "Approved", "Customer", actingCustomerId.Value);
```

Dikkat: `Approve`'da `ActorType` **`"Customer"`**, diğer ikisinde **`"Employee"`** — audit log, FieldOps'un farklı aktör tiplerini (Week 8-9'dan beri kurulu: Employee, Customer) tek bir kayıt şeklinde temsil edebiliyor, çünkü `ActorType`/`ActorId` genel (herhangi bir kimlik tipine referans verebilen) bir çift.

**Bugün sadece 3 action kaydediliyor** (`Assign`/`Complete`/`Approve`) — `Create`/`Start`/`Unassign`/`Reopen`/`AddEvidence` bilinçli olarak dışarıda, aynı `Record` çağrısıyla kolayca genişletilebilir.

---

## 5. Canlı kanıt — iki senaryo

```
1) Musterili is emri: Create -> Assign -> Start -> Complete -> Approve
   sqlcmd ile AuditLogEntries sorgulandi:
   Id=1  Assigned  Employee ActorId=1
   Id=2  Completed Employee ActorId=2
   Id=3  Approved  Customer ActorId=1
   -> Uc kayit da doğru veriyle, doğru sırayla olustu.

2) EfAuditLogWriter gecici olarak "throw" edecek sekilde degistirildi:
   -> Assign cagrildi -> HTTP 200 OK (!)
   -> log'da: "Failed to record audit log entry for work order 8 (Assigned)"
   -> Assign basariyla gerceklesti, audit kaydi basarisiz oldu ama bu
      istemciye hic yansimadi.
   -> Kod geri alindi, tekrar dogrulandi.
```

---

## 6. Demo basitleştirmesi vs. üretim gereksinimi

- Bugün **hiçbir okuma/raporlama uç noktası yok** — sadece yazma yolu kuruldu. Coverage yerine derinlik: write path'i doğru ve dayanıklı kurmaya odaklanıldı.
- Sadece 3 action kaydediliyor, tamamı değil — kapsam bilinçli olarak dar tutuldu.
- Audit yazımı senkron/inline — Day 50/51'in aynı sınırı: üretimde bir kuyruğa alınabilirdi.

---

## Regresyon

```
dotnet test FieldOps.slnx    → 49/49 (5. veritabani dahil, Testcontainers'la)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```
