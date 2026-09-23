# Day 58 — Kod Notları

Faz 3, Hafta 11, Gün 58. Konu: **Docker** — `FieldOps.Api`'yi bir container'a almak. Bugünün en önemli dersi, planladığımdan **farklı** çıktı — gerçek bir keşif, tahmin edilenden daha öğretici oldu.

---

## 1. Docker nedir — en baştan

**Sorun, Docker'sız dünyada:** `FieldOps.Api`'yi çalıştırmak istersen, senin makinende **tam olarak şunlar** olmalı: .NET 10 SDK kurulu, doğru sürüm; proje dosyaları doğru yerde; `dotnet run` komutunu bilmen. Bunu bir arkadaşının makinesinde, bir CI sunucusunda, ya da bulutta bir sunucuda çalıştırmak istersen, **her birinde aynı kurulumu tekrar yapman** gerekir — "benim makinemde çalışıyordu" probleminin klasik kaynağı.

**Docker'ın çözümü:** Uygulamanı, çalışması için gereken **her şeyle birlikte** (kod + .NET runtime + gerekli her şey, işletim sistemi kadar değil ama "senin uygulamanın ihtiyaç duyduğu her şey") tek, taşınabilir bir pakete koyuyorsun. Bu pakete **image** deniyor. Bu image'ı **çalıştırdığında** ortaya çıkan, gerçekten çalışan örneğe **container** deniyor.

**Zihinsel model — iki farklı kavram, sık karıştırılıyor:**
- **Image** = bir CD/DVD'ye yakılmış, değişmez bir kurulum paketi. "Bu, FieldOps.Api'nin şu anki hâli" diye donmuş bir anlık görüntü.
- **Container** = o CD'den kurulup **çalışmakta olan** bir kopya. Aynı image'dan istediğin kadar container başlatabilirsin, her biri birbirinden **izole** (kendi dosya sistemi, kendi ağı gibi davranıyor — bugünkü "localhost kendini işaret ediyor" keşfinin sebebi tam olarak bu izolasyon).

**Sanal makineden farkı (kısaca, senin muhtemelen bildiğin bir karşılaştırma):** Bir sanal makine kendi işletim sistemi çekirdeğini taşır (ağır, yavaş başlar). Bir Docker container, **host'un** işletim sistemi çekirdeğini paylaşır, sadece kendi dosya sistemi/süreç alanı izole edilmiştir — bu yüzden çok daha hafif ve saniyeler içinde başlar.

---

## 2. Gerçek problem (bugüne özel)

FieldOps.Api'yi çalıştırmak için .NET SDK kurulu olması, `dotnet run` bilmesi gerekiyordu — taşınabilir, her yerde aynı şekilde çalışan bir paket yoktu. Docker bunu çözüyor: uygulamayı + çalıştırmak için gereken her şeyi tek bir taşınabilir "kutuya" paketliyor.

---

## 3. Çok aşamalı `Dockerfile` — satır satır

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/FieldOps.Api/FieldOps.Api.csproj src/FieldOps.Api/
COPY src/FieldOps.Modules.Organizations/FieldOps.Modules.Organizations.csproj src/FieldOps.Modules.Organizations/
# ... diger 4 modul icin ayni
RUN dotnet restore src/FieldOps.Api/FieldOps.Api.csproj

COPY src/FieldOps.Api/ src/FieldOps.Api/
COPY src/FieldOps.Modules.Organizations/ src/FieldOps.Modules.Organizations/
# ... diger 4 modul icin ayni
RUN dotnet publish src/FieldOps.Api/FieldOps.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FieldOps.Api.dll"]
```

Her satırın gerçekte ne yaptığı:

- **`FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build`** — bir Docker image, **başka bir image'ın üzerine** inşa edilir (kendi işletim sistemi çekirdeğini sıfırdan yazmıyoruz). `mcr.microsoft.com/dotnet/sdk:10.0`, Microsoft'un resmi, .NET 10 SDK'sının (derleyiciler dahil) zaten kurulu olduğu bir Linux tabanlı temel image'ı. `AS build` — bu aşamaya "build" adını veriyoruz, aşağıda bu isimle referans vereceğiz.
- **`WORKDIR /src`** — bu aşamadan sonraki tüm komutların (COPY, RUN) çalışacağı "geçerli dizin"i container **içinde** `/src` olarak ayarlıyor (dizin yoksa otomatik oluşturulur). Tıpkı bir terminalde `cd /src` yazmak gibi, ama container'ın kendi dosya sistemi içinde.
- **`COPY src/FieldOps.Api/FieldOps.Api.csproj src/FieldOps.Api/`** — senin makinendeki (build context'teki) `src/FieldOps.Api/FieldOps.Api.csproj` dosyasını, container'ın içine, **aynı göreli yol** (`src/FieldOps.Api/`) altına kopyalıyor. Aynı göreli yapı korunuyor çünkü `.csproj` dosyalarındaki `<ProjectReference Include="..\FieldOps.Modules.Organizations\...">` gibi **göreli** referansların, container içinde de doğru şekilde çözülmesi gerekiyor.
- **Bu satır, her modül için tekrarlanıyor** (5 modül + API = 6 `COPY` satırı) — henüz sadece `.csproj` dosyaları, gerçek `.cs` kaynak kodu değil.
- **`RUN dotnet restore src/FieldOps.Api/FieldOps.Api.csproj`** — `dotnet restore`, bir `.csproj`'daki tüm `<PackageReference>`'ları (NuGet paketleri) indirir. `FieldOps.Api.csproj`'u restore etmek, onun `<ProjectReference>` ile bağlı olduğu 5 modülü de **otomatik olarak** restore ediyor (MSBuild referans grafiğini takip ediyor) — bu yüzden tüm çözümü (`.slnx`) değil, sadece bu tek dosyayı restore etmek yeterli.
- **`COPY src/FieldOps.Api/ src/FieldOps.Api/`** (ve diğer 5 modül için aynısı) — şimdi **gerçek kaynak kodun tamamını** kopyalıyoruz (klasördeki son `/` olmadan yazsaydık farklı davranırdı — sondaki `/`, "bu klasörün içeriğini oraya kopyala" anlamına geliyor).
- **`RUN dotnet publish src/FieldOps.Api/FieldOps.Api.csproj -c Release -o /app/publish --no-restore`** — `publish`, `build`'den farklı olarak, uygulamayı **çalıştırılabilir, dağıtıma hazır** bir klasöre (`-o /app/publish`) çıkarıyor (DLL'ler + gerekli bağımlılıklar, gereksiz ara dosyalar olmadan). `-c Release` — Release yapılandırması (optimize edilmiş, hata ayıklama sembolleri minimum). `--no-restore` — restore zaten yukarıda yapıldığı için tekrarlanmıyor (hem gereksiz hem yavaş olurdu).
- **`FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final`** — **yeni bir aşama başlıyor**, sıfırdan. Bu, `sdk` değil `aspnet` image'ı — sadece ASP.NET Core'u **çalıştırmak** için gereken runtime var, derleyici yok. Bu satırdan önceki her şey (SDK, kaynak kod, ara `obj/` dosyaları) bu yeni aşamaya **hiç taşınmıyor** — sanki temiz bir sayfa.
- **`COPY --from=build /app/publish .`** — işte köprü burada: önceki aşamanın (`build` — isimlendirdiğimiz için referans verebiliyoruz) `/app/publish` klasöründeki **sonuç dosyaları**, bu yeni, temiz aşamaya kopyalanıyor. SDK'nın kendisi, kaynak kodun kendisi — hiçbiri buraya gelmiyor, sadece derlenmiş çıktı.
- **`ENTRYPOINT ["dotnet", "FieldOps.Api.dll"]`** — bu image'dan bir container başlatıldığında **çalıştırılacak komut**. Tıpkı senin terminalde `dotnet FieldOps.Api.dll` yazman gibi, ama bu artık container'ın "varsayılan davranışı."

**`.dockerignore`** — `bin/`, `obj/`, `.git/`, ve bu repodaki **ilgisiz** projeler (RoadmapOS, StockPilot, testler) build context'ine (yani `COPY` komutlarının okuyabileceği dosya kümesine) hiç dahil edilmiyor; hem daha hızlı hem daha temiz bir build (özellikle senin zaten yerelde derlenmiş `bin`/`obj` klasörlerini container'a taşımak hem gereksiz hem de yanlış — container kendi Linux ortamı için yeniden derliyor, Windows'ta derlenmiş dosyalar işine yaramaz).

---

## 4. `docker build` ve `docker run` — komutları satır satır

**Image'ı inşa etmek:**
```
docker build -t fieldops-api:day58 .
```
- **`docker build`** — bir `Dockerfile`'ı okuyup bir image inşa et.
- **`-t fieldops-api:day58`** — image'a bir **etiket** (tag) veriyor: `isim:versiyon` formatında. Etiketlemezsen, image'a rastgele bir ID ile referans vermek zorunda kalırsın — `-t` ona akılda kalıcı bir isim veriyor.
- **`.`** — build **context**'i: "Dockerfile'daki `COPY` komutlarının okuyacağı dosyalar nereden gelsin?" sorusunun cevabı. `.` = "şu an bulunduğum dizin" (repo kökü) — bu yüzden `.dockerignore`, bu dizinden **nelerin** context'e dahil edileceğini kontrol ediyor.

**Container'ı çalıştırmak (ilk deneme, düzeltmeden önce):**
```
docker run -d --name fieldops-api-test -p 5180:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e ConnectionStrings__FieldOpsOrganizationsDb="Server=localhost\SQLEXPRESS;..." \
  -e Redis__ConnectionString="localhost:6379" \
  fieldops-api:day58
```
- **`docker run`** — bir image'dan **yeni bir container başlat** (henüz çalışmıyorsa).
- **`-d`** — "detached": container arka planda çalışsın, terminali bloklamasın (tıpkı Day 50'de gördüğümüz `docker run` bayraklarına benzer).
- **`--name fieldops-api-test`** — container'a bir isim veriyor, ileride `docker stop fieldops-api-test`/`docker logs fieldops-api-test` gibi komutlarla referans vermek için.
- **`-p 5180:8080`** — **port eşleme**: host makinenin `5180` portunu, container'ın **içindeki** `8080` portuna bağlıyor. `8080`, ASP.NET Core'un resmi runtime image'ının **varsayılan olarak** Kestrel'i dinlettiği port (image'ın kendi içine gömülü bir ayar) — biz bunu değiştirmedik, sadece dışarıdan hangi portla erişeceğimizi seçtik.
- **`-e ANAHTAR=DEĞER`** — container'ın **içine bir ortam değişkeni enjekte ediyor**. Bu, tam olarak Day 57'de öğrendiğimiz mekanizma — `ConnectionStrings__FieldOpsOrganizationsDb` gibi bir `-e` bayrağı, uygulamanın `IConfiguration`'ında `ConnectionStrings:FieldOpsOrganizationsDb` anahtarını **appsettings dosyasındaki değerin üzerine yazarak** ayarlıyor. Container'a "hangi veritabanına/Redis'e bağlanacağını" söylemenin **tek** yolu bu — image'ın içine bu bilgiler hiç gömülmedi (gömülmemeli de, Day 57'nin dersi).
- **`fieldops-api:day58`** — hangi image'dan container başlatılacağı (build sırasında verdiğimiz etiket).

**Düzeltilmiş versiyon:**
```
docker run -d --name fieldops-api-test -p 5180:8080 \
  --add-host=host.docker.internal:host-gateway \
  -e Redis__ConnectionString="host.docker.internal:6379" \
  ...
```
- **`--add-host=host.docker.internal:host-gateway`** — container'ın kendi iç DNS'ine, `host.docker.internal` adının **host makineye** karşılık geldiğini **elle** öğretiyor (bazı Docker kurulumlarında bu otomatik gelir, bazılarında — bu makinede olduğu gibi — açıkça eklemek gerekiyor). `host-gateway`, Docker'ın kendi ayırdığı özel bir değer: "container'ı çalıştıran host'un gerçek IP'si neyse, onu kullan."
- **`-e Redis__ConnectionString="host.docker.internal:6379"`** — artık Redis'e "localhost" değil, "host makine" adresi üzerinden bağlanmasını söylüyoruz.

---

## 5. `/health/ready`'ye JSON detay eklemek

```csharp
static Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var payload = JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), description = e.Value.Description })
    });
    return context.Response.WriteAsync(payload);
}
```

Framework'ün varsayılan yanıtı sadece düz "Healthy"/"Unhealthy" metni — **hangi** kontrolün başarısız olduğunu göstermiyor. `HealthCheckOptions.ResponseWriter`'a bu fonksiyonu vererek, her kontrolü (`redis`, `workorders-db`) ayrı ayrı, JSON içinde görünür yaptık — bugünkü teşhis hikayesinin **gözle görülür** olmasını sağlayan tam olarak bu ekleme.

---

## 6. Canlı keşif — planlanandan farklı, ama gerçek bir sonuç

**Adım 1 — varsayılan (`localhost`) değerlerle container çalıştırıldı:**
```json
{"status":"Unhealthy","checks":[
  {"name":"redis","status":"Unhealthy","description":"Redis is not reachable."},
  {"name":"workorders-db","status":"Unhealthy","description":"SQL Server is not reachable."}
]}
```
Beklenen: container içinde "localhost", container'ın **kendisini** işaret ediyor, host makineyi değil.

**Adım 2 — `host.docker.internal` ile düzeltildi (Day 57'nin ortam değişkeni mekanizmasıyla):**
```
docker run ... --add-host=host.docker.internal:host-gateway \
  -e Redis__ConnectionString="host.docker.internal:6379" \
  -e ConnectionStrings__FieldOpsOrganizationsDb="Server=host.docker.internal\SQLEXPRESS;..." \
  ...
```
Sonuç:
```json
{"status":"Unhealthy","checks":[
  {"name":"redis","status":"Healthy","description":"Redis is reachable."},
  {"name":"workorders-db","status":"Unhealthy","description":"SQL Server is not reachable."}
]}
```
Redis düzeldi (basit bir TCP bağlantısı, kimlik doğrulama katmanı yok). SQL Server **hâlâ** başarısız — **planımda Windows Authentication'ı suçlamıştım, ama gerçek log şunu gösterdi:**

```
Microsoft.Data.SqlClient.SqlException: A network-related or instance-specific
error occurred while establishing a connection to SQL Server...
(provider: TCP Provider, error: 26 - Error Locating Server/Instance Specified)
 ---> System.Net.Sockets.SocketException (101): Network is unreachable
   at ...SsrpClient.SendUDPRequest(...)
```

**Gerçek kök neden, tahmin ettiğimden daha erken bir aşamada:** `SQLEXPRESS` gibi **adlandırılmış bir örneği** (named instance) çözmek için SQL Server istemcisi, önce SQL Server **Browser** servisine bir **UDP** isteği gönderip "SQLEXPRESS hangi TCP portunu kullanıyor?" diye soruyor (SSRP — SQL Server Resolution Protocol). Bu UDP isteği, container'dan `host.docker.internal`'a giderken **"Network is unreachable"** hatası alıyor — container'ın ağ topolojisi bu UDP yönlendirmesini desteklemiyor. Yani sorun Windows Authentication'a **hiç ulaşamadan**, çok daha önceki bir adımda (hangi porta bağlanacağını bile bulamadan) tıkanıyor.

**Bu neden önemli bir ders:** Bir hipotez kurdum (Windows Auth), ama gerçek log'u okuyunca **farklı, daha temel** bir sorun olduğunu gördüm — tam olarak bu workspace'in "varsayma, gözlemle" ilkesinin kendi planıma uygulanmış hali. Bunu düzeltmeye çalışmak (SQLEXPRESS'in dinamik portunu bulup elle belirtmek, UDP 1434'ü açmak vb.) bugünün kapsamı dışında, gerçek bir tavşan deliği — **Day 59'un (Docker Compose) çözümü zaten çok daha temiz**: SQL Server'ı da container'a alınca, adlandırılmış örnek/UDP Browser protokolüne hiç ihtiyaç kalmıyor — container'lar birbirine doğrudan bir TCP portu ve container adıyla konuşuyor.

---

## 7. Demo basitleştirmesi vs. üretim gereksinimi

- Bugün sadece `FieldOps.Api` container'a alındı — SQL Server/Redis hâlâ host makinede. Redis çalıştı (basit TCP), SQL Server çalışmadı (adlandırılmış örnek + UDP Browser protokolü, container ağında güvenilmez).
- `/health/live` her koşulda `200` kaldı — tasarım gereği, hiçbir bağımlılığa dokunmuyor.

---

## Regresyon

```
dotnet test FieldOps.slnx    → 49/49 (Docker'dan tamamen bagimsiz, etkilenmedi)
dotnet build StockPilot.slnx → 0 Hata, 0 Uyarı
dotnet build RoadmapOS.slnx  → 0 Hata, 0 Uyarı
```
