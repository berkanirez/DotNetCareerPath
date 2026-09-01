# Day 2 — Kod Notları

Faz 1, Hafta 1, Gün 2. Konu: C# tip sistemi (class, record, interface, nullable reference types) ve persistence olmadan ilk domain kavramları.

Bu doküman, bugün eklenen her kod parçasını **yazılma/kullanılma sırasına göre** gezer. Her parça için: kodun kendisi, **neden** böyle yazıldığı (mantık, çözdüğü gerçek problem, ne için kullanılacağı) ve **nasıl** çalıştığı (syntax/teknik açıklama).

---

## 1. `src/RoadmapOS.Web/Domain/SkillLevel.cs`

```csharp
namespace RoadmapOS.Web.Domain;

public enum SkillLevel
{
    NotStudied = 0,
    CanExplainPurpose = 1,
    CanImplementWithGuidance = 2,
    CanImplementIndependently = 3,
    CanExplainProduction = 4
}
```

**Neden:** `docs/REQUIREMENTS_MATRIX.md` zaten 0-4 arası beş seviyelik bir ölçek tanımlıyor ve tüm roadmap boyunca ilerlemeyi bununla takip ediyoruz. Herhangi bir skill'i koda dökmeden önce, bu ölçeğin ham bir `int` yerine somut ve tip-güvenli bir karşılığı olması gerekiyor — `int CurrentLevel` olsaydı, biri `99` veya `-5` atayabilirdi, ki bu anlamsız olurdu. Bu enum, geçersiz bir seviyeyi **temsil etmeyi bile imkansız** hale getiriyor.

**Nasıl çalışıyor:**
* `namespace RoadmapOS.Web.Domain;` — dosya kapsamlı (file-scoped) namespace tanımı, dosyanın tamamını `{ }` bloğuna almaya gerek bırakmıyor (C# 10+ kısayolu). Aşağıdaki her şey `RoadmapOS.Web.Domain`'e ait; başka dosyaların bunu kısa isimle kullanabilmesi için `using RoadmapOS.Web.Domain;` yazması gerekiyor.
* `public enum SkillLevel { ... }` — sadece listelenen isimlendirilmiş üyelerden birini alabilen yeni bir değer tipi tanımlıyor. `public` erişim belirleyicisi, başka bir proje/assembly'nin de buna erişebileceği anlamına geliyor (bugün için bir kısıtlama gerekmiyor).
* Her üyeye açıkça bir tam sayı atanmış (`= 0`, `= 1`, ...). Bu zorunlu değil (C# üyeleri otomatik olarak 0'dan başlatıp numaralandırırdı), ama burada açıkça yazıldı çünkü bu sayılar `REQUIREMENTS_MATRIX.md`'deki seviye tanımlarıyla birebir sözleşme halinde — üyeler yeniden sıralansa bile sayılar sessizce kaymasın diye.
* Arka planda bir `SkillLevel` değeri aslında bir `int` (başka bir alt tip belirtilmediği sürece) — bu yüzden `SkillLevel.CanImplementWithGuidance >= SkillLevel.CanExplainPurpose` derlenir ve `2 >= 1` karşılaştırmasıyla sonuçlanır.

---

## 2. `src/RoadmapOS.Web/Domain/Skill.cs`

```csharp
namespace RoadmapOS.Web.Domain;

public class Skill : IComparable<Skill>
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }
    public SkillLevel CurrentLevel { get; set; }
    public SkillLevel TargetLevel { get; set; }
    public string? Notes { get; set; }

    public Skill(string name, string category, SkillLevel currentLevel, SkillLevel targetLevel)
    {
        Name = name;
        Category = category;
        CurrentLevel = currentLevel;
        TargetLevel = targetLevel;
    }

    public bool IsAtTarget => CurrentLevel >= TargetLevel;

    public int CompareTo(Skill? other)
    {
        if (other is null)
        {
            return 1;
        }

        var levelComparison = CurrentLevel.CompareTo(other.CurrentLevel);
        return levelComparison != 0 ? levelComparison : string.Compare(Name, other.Name, StringComparison.Ordinal);
    }
}
```

**Neden:** Bu, ilk gerçek domain entity'si — `REQUIREMENTS_MATRIX.md`'de elle takip ettiğimiz bir satırın kod karşılığı. `record` değil `class` olarak modellendi, çünkü bir skill'in **değişim boyunca kalıcı olan bir kimliği** var: `Id`'si sabit kalırken `CurrentLevel`'i roadmap boyunca güncellenecek (henüz EF Core yokken bile, ileride SQL Server'daki primary key'li bir satırın habercisi — bkz. Day 4). `IComparable<Skill>` uygulandı çünkü bugün somut bir ihtiyacımız var: skill listesini ilerlemeye göre sıralayabilmek (en az ilerlemiş önce) — tıpkı ileride bir dashboard'un ihtiyaç duyacağı gibi.

**Property ve üye üye nasıl çalıştığı:**

```csharp
public int Id { get; set; }
```
**Auto-implemented property** — `{ get; set; }`, derleyiciye gizli bir private backing field otomatik oluşturmasını söylüyor. Hiç atama yapılmazsa `int` varsayılan olarak `0` olur. EF Core tanıtıldığında bu, primary key olacak.

```csharp
public string Name { get; set; }
public string Category { get; set; }
```
Nullable olmayan (`?` yok) reference type property'ler. Nullable reference types (`.csproj`'daki `<Nullable>enable</Nullable>` ile proje genelinde açık) sayesinde derleyici, constructor bittikten sonra bunların **asla null olmamasını** bekliyor — bu yüzden varsayılana bırakılmıyor, zorunlu constructor parametresi olarak isteniyor.

```csharp
public SkillLevel CurrentLevel { get; set; }
public SkillLevel TargetLevel { get; set; }
```
1. adımdaki enum'u kullanan value-type property'ler. Atama yapılmazsa varsayılan olarak `SkillLevel.NotStudied` (`0`) olur — ki bu zaten doğru anlam ("henüz çalışılmadı").

```csharp
public string? Notes { get; set; }
```
Nullable (`string?`) çünkü her skill'in notu olmak zorunda değil. Bugün nullable reference type güvenliğini göstermek için kullanılan alan bu.

```csharp
public Skill(string name, string category, SkillLevel currentLevel, SkillLevel targetLevel)
{
    Name = name;
    Category = category;
    CurrentLevel = currentLevel;
    TargetLevel = targetLevel;
}
```
Constructor: `new Skill(...)` çağrıldığında bir kez çalışır. Bir skill'in anlamlı olması için *zorunlu* kabul edilen dört property'yi parametre olarak alıp atıyor. `Id` ve `Notes` bilinçli olarak dışarıda bırakıldı — `Id`'yi ileride veritabanı atayacak, `Notes` opsiyonel ve `Program.cs`'te gördüğümüz gibi (`new Skill(...) { Notes = "..." }`) object initializer ile sonradan set ediliyor.

```csharp
public bool IsAtTarget => CurrentLevel >= TargetLevel;
```
Expression-bodied (computed) property — arkasında backing field yok, her okunduğunda `CurrentLevel` ve `TargetLevel`'den yeniden hesaplanıyor. `=>` "değeri bu ifadedir" demek. Setter yok çünkü bu bağımsız bir veri değil — türetilmiş bir değer. `>=` doğrudan çalışıyor çünkü iki taraf da aynı enum, alttaki `int` değerlerine göre karşılaştırılıyor.

```csharp
public int CompareTo(Skill? other)
{
    if (other is null)
    {
        return 1;
    }

    var levelComparison = CurrentLevel.CompareTo(other.CurrentLevel);
    return levelComparison != 0 ? levelComparison : string.Compare(Name, other.Name, StringComparison.Ordinal);
}
```
`IComparable<Skill>`'in zorunlu kıldığı tek metot. .NET'in her yerinde geçerli dönüş kuralı: negatif = "ben ondan önce gelirim", `0` = "sıralama açısından eşitiz", pozitif = "ben ondan sonra gelirim".
* `other is null` — pattern-matching null kontrolü; convention gereği gerçek bir nesne her zaman `null`'dan sonra gelir.
* `CurrentLevel.CompareTo(other.CurrentLevel)` — her enum, alttaki `int` değerlerini karşılaştırarak otomatik olarak `IComparable`'ı uygular.
* Ternary satırı: seviyeler farklıysa sonuç bu farktır; seviyeler eşitse `Name`'e göre alfabetik sırala (tie-breaker). `StringComparison.Ordinal`, kültüre bağlı olmayan ham karakter karşılaştırması — kullanıcıya gösterilecek sıralama değil, iç mantık sıralaması için bilinçli tercih.

`class Skill : IComparable<Skill>` — buradaki `:` **interface uygulamak** anlamına geliyor, kalıtım değil (C#'ta tek bir base class'tan kalıtım alınabilir ama birden fazla interface uygulanabilir). Bu, derleme zamanı bir sözleşme: `Skill`, `IComparable<Skill>`'i uyguladığı için `List<Skill>.Sort()` (parametresiz) her karşılaştırma gerektiğinde `CompareTo`'yu otomatik çağırabiliyor.

---

## 3. `src/RoadmapOS.Web/Domain/SkillSnapshot.cs`

```csharp
namespace RoadmapOS.Web.Domain;

public record SkillSnapshot(string Name, SkillLevel Level, DateOnly RecordedOn);
```

**Neden:** İleriki fazlarda (Day 7+, ilerleme hesaplama) "bu skill, şu tarihte, şu seviyedeydi" gibi tarihsel bir gerçeği temsil etmemiz gerekecek — güncellenen bir şey değil, donmuş bir kayıt. `Skill`'in aksine, bir snapshot'ın takip edilmeye değer bir kimliği yok: `Name`/`Level`/`RecordedOn` değerleri birebir aynı olan iki snapshot, tam olarak aynı gerçeği temsil ediyor ve eşit sayılmalı. `record` bunu bedavaya veriyor.

**Nasıl çalışıyor:**
* `public record SkillSnapshot(string Name, SkillLevel Level, DateOnly RecordedOn);` — bu bir **positional record**: parantez içindeki parametre listesi aynı anda hem constructor parametrelerini hem de `Name`, `Level`, `RecordedOn` adında üç tane public, salt-okunur (`init`-only) property'i tanımlıyor. Bu tek satır; üç `{ get; init; }` property'li, uyumlu bir constructor'lı, üstüne derleyicinin otomatik ürettiği `Equals`, `GetHashCode`, `ToString` ve deconstruction desteğine sahip bir class yazmakla eşdeğer — hiçbirini elle yazmadan.
* `record` olduğu için (`==` operatörü) tüm üç property değerini karşılaştırıyor, bellek referansını değil.
* `DateOnly`, saat bilgisi olmadan sadece takvim tarihini temsil eden bir struct (value type) — "şu tarihte kaydedildi" için doğru tip; saat bilgisi de taşıyan `DateTime`'ın aksine.

---

## 4. `src/RoadmapOS.Web/Program.cs` — geçici doğrulama bloğu

```csharp
using RoadmapOS.Web.Domain;
```
**Neden/Nasıl:** 1-3. adımlardaki namespace'i import ediyor ki `Skill`, `SkillLevel`, `SkillSnapshot` bu dosyada kısa isimleriyle kullanılabilsin.

```csharp
if (app.Environment.IsDevelopment())
{
    // TEMPORARY — Day 2 domain verification only. Will be removed on Day 3
    // once a real controller/view vertical slice replaces this console check.
```
**Neden:** Henüz test framework'ü yok (xUnit Day 7'de geliyor) ve bu domain'e bağlı bir controller/view de yok (Day 3'te geliyor). Bu blok, sadece uygulamayı çalıştırıp konsol çıktısını okuyarak yukarıdaki class/record/interface/nullable davranışının gerçekten tasarlandığı gibi çalıştığını **kanıtlamak** için var — geçici kod için kalıcı bir yer icat etmeden. `Development` ortamına kilitlendi ki gerçek bir deployment'ta asla çalışmasın, ve Day 3'te bu veriye gerçek bir yer verilince silinmek üzere açıkça işaretlendi.

```csharp
    var skills = new List<Skill>
    {
        new("C#", "Language", SkillLevel.CanImplementWithGuidance, SkillLevel.CanExplainProduction),
        new("ASP.NET Core", "Framework", SkillLevel.CanExplainPurpose, SkillLevel.CanImplementIndependently),
        new("EF Core", "Framework", SkillLevel.NotStudied, SkillLevel.CanImplementIndependently)
        {
            Notes = "Not started yet"
        }
    };
```
**Neden:** `Sort()`'a anlamlı bir şey sıralatmak için bilerek üç farklı `CurrentLevel` değeri verilmiş üç örnek skill; aşağıdaki null-safe okumayı test edebilmek için biri `Notes` değeriyle verilmiş.

**Nasıl çalışıyor:** Tip adını tekrar yazmadan `new(...)` kullanmak **target-typed new** — derleyici, içinde bulunduğu `List<Skill>` koleksiyonundan hedefin `Skill` olduğunu zaten biliyor, bu yüzden tip adı atlanabiliyor. Üçüncü elemanda, constructor çağrısından hemen sonra gelen `{ Notes = "Not started yet" }` bir **object initializer**: constructor çalıştıktan sonra, nesne tam olarak "kurulmuş" sayılmadan önce ek property değerleri atıyor.

```csharp
    skills.Sort();
```
**Neden/Nasıl:** Listeyi yerinde (in place) sıralıyor, en az ilerlemiş önce gelecek şekilde, `Skill.cs`'te yazılan `CompareTo` mantığını kullanarak. Bu, `IComparable<Skill>` uygulamanın doğrudan karşılığı.

```csharp
    Console.WriteLine("--- Day 2 domain verification ---");
    foreach (var skill in skills)
    {
        Console.WriteLine($"{skill.Name} ({skill.Category}): {skill.CurrentLevel} -> {skill.TargetLevel}, AtTarget={skill.IsAtTarget}");
    }
```
**Neden/Nasıl:** Sıralanmış listeyi ve hesaplanan `IsAtTarget` property'sini görsel olarak doğrulamak için konsola yazdırıyor. `$"..."` **string interpolation** — içindeki `{ifade}` değerlendirilip string'e ekleniyor, JS/TS'teki template literal'in birebir karşılığı.

```csharp
    Console.WriteLine($"Notes length (null-safe): {skills[0].Notes?.Length ?? 0}");
```
**Neden:** Nullable bir alana güvenli erişimi gösteriyor. Sıralamadan sonra `skills[0]`, `Notes`'u null olmayan `EF Core` oluyor — bu satır önce güvenlik operatörleri **olmadan** yazılıp `CS8602` ("olası null başvuru") uyarısını bilerek tetiklemek için kullanıldı, sonra burada görülen güvenli haline düzeltildi.

**Nasıl çalışıyor:** `?.` (null-conditional), `Notes` null ise `.Length`'i hiç değerlendirmeden direkt `null` döner — TypeScript'teki `obj?.prop`'un birebir karşılığı. `?? 0` (null-coalescing) ise sol taraf `null` çıkarsa `0` ile değiştiriyor — TypeScript'teki `??` operatörünün birebir karşılığı (aslında TS bu operatörü C#'tan esinlenerek eklemişti).

```csharp
    var snapshotToday = new SkillSnapshot("C#", SkillLevel.CanImplementWithGuidance, DateOnly.FromDateTime(DateTime.Today));
```
**Neden/Nasıl:** Bugünün tarihiyle bir snapshot record'u oluşturuyor. `DateOnly.FromDateTime(DateTime.Today)`, o anki tarih/saati sadece tarihe indirgiyor.

```csharp
    var snapshotNextWeek = snapshotToday with
    {
        Level = SkillLevel.CanImplementIndependently,
        RecordedOn = snapshotToday.RecordedOn.AddDays(7)
    };
    var sameSnapshotAgain = snapshotToday with { };
```
**Neden:** Record'un en ayırt edici özelliği olan `with` ifadesini gösteriyor — değiştirilmiş bir kopya (farklı `Level`/`RecordedOn`, aynı `Name`) ve değiştirilmemiş bir kopya üretip ikisini de orijinalle karşılaştırmak için.

**Nasıl çalışıyor:** `with { ... }`, derleyici tarafından üretilen bir kısayol: `snapshotToday`'in her property'sini yepyeni bir `SkillSnapshot` nesnesine kopyalıyor, sonra sadece açıkça belirtilen property'lerin üzerine yazıyor. `with { }` (hiçbir property belirtilmeden) her şeyi değiştirmeden, ama yine de yepyeni bir nesneye kopyalıyor — bellekte farklı bir nesne, ama eşit değerlerle.

```csharp
    Console.WriteLine($"Original snapshot: {snapshotToday}");
    Console.WriteLine($"Updated snapshot:  {snapshotNextWeek}");
    Console.WriteLine($"Original == Updated? {snapshotToday == snapshotNextWeek}");
    Console.WriteLine($"Original == re-created copy? {snapshotToday == sameSnapshotAgain}");
    Console.WriteLine("--- end verification ---");
}
```
**Neden/Nasıl:** Interpolation içindeki `{snapshotToday}`, record'un derleyici tarafından otomatik üretilen `ToString()`'ini çağırıyor; bu da tüm property isim ve değerlerini yazdırıyor (`SkillSnapshot { Name = C#, Level = ..., RecordedOn = ... }`) — düz bir `class`'ın bedavaya sahip olmadığı bir başka özellik. Record'larda `==` değere göre karşılaştırıyor: `snapshotToday == snapshotNextWeek` → `False` (farklı `Level`/`RecordedOn`), `snapshotToday == sameSnapshotAgain` → `True` (değerler birebir aynı, bellekte iki ayrı nesne olsalar bile).

---

## Doğrulanan davranış (gerçek `dotnet run` konsol çıktısı)

```
--- Day 2 domain verification ---
EF Core (Framework): NotStudied -> CanImplementIndependently, AtTarget=False
ASP.NET Core (Framework): CanExplainPurpose -> CanImplementIndependently, AtTarget=False
C# (Language): CanImplementWithGuidance -> CanExplainProduction, AtTarget=False
Notes length (null-safe): 15
Original snapshot: SkillSnapshot { Name = C#, Level = CanImplementWithGuidance, RecordedOn = 1.09.2026 }
Updated snapshot:  SkillSnapshot { Name = C#, Level = CanImplementIndependently, RecordedOn = 8.09.2026 }
Original == Updated? False
Original == re-created copy? True
--- end verification ---
```

Bu çıktı sırasıyla şunları doğruluyor: `IComparable<Skill>` ile sıralama (`NotStudied` < `CanExplainPurpose` < `CanImplementWithGuidance`), hesaplanan `IsAtTarget` property'si, `Notes`'a null-safe erişim, record'un otomatik ürettiği `ToString()`, ve record'larda `==`'nin hem "farklı değer" hem "aynı değer" durumunda doğru çalışan value equality davranışı.
