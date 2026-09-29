# Day 83 — Kod Notları

Faz 4, Hafta 16, Gün 83. Konu: **SOAP client integration**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Gerçek hayattaki problem:** FieldOps bir iş emri yönetim sistemi — muhasebeyle hiç ilgisi yok. Ama gerçek şirketlerde, bir iş emri tamamlanınca fatura kesilir, ve faturalarda tutarlar çoğu zaman hem rakamla hem **yazıyla** yazılır (bir çekin üzerine "İki Yüz Elli Lira" yazmak gibi). Bu tür işlemler genelde şirketlerin **çok eski, yıllardır değişmeden çalışan muhasebe/ERP programlarında** zaten var. Sorun şu: o eski programlar dışarıyla konuşmak için bazen **sadece SOAP** denen, 2000'lerden kalma bir yöntemi biliyor — yeni bir REST/JSON API'leri yok, hiç de olmayacak.

**SOAP nedir, en basit haliyle:** İki programın birbirine "şunu yap, cevabını ver" diye konuşmasının eski bir yöntemi — XML kullanır, REST'ten daha katıdır. Biz FieldOps'ta bugüne kadar hep REST/JSON kullandık (modern yöntem). SOAP hâlâ bazı **bankacılık, muhasebe, devlet sistemlerinde** tek seçenek olarak karşımıza çıkabiliyor.

**Bunu FieldOps'a neden ekliyoruz — gerçek bir ihtiyaç mı?** Hayır. FieldOps'un bugün gerçek bir muhasebe entegrasyonuna ihtiyacı yok. Bunu, "bir .NET geliştiricisi meslekte SOAP'la karşılaşabilir, bunu bilmeli" diyen roadmap maddesi yüzünden, **öğrenmek amacıyla** yaptık. Kendi hayali bir muhasebe sistemimiz olmadığı için, internette **gerçekten çalışan, herkese açık, bedava bir test SOAP servisi** bulup onu "sanki bizim muhasebe sistemimizmiş gibi" kullandık.

**O test servisi ne yapıyor:** Ona bir sayı gönderiyorsun (`250.75`), o da bunu **yazıya döküp** geri veriyor: *"two hundred and fifty dollars and seventy five cents"*. Gerçek hayatta bunu muhasebe sistemi yapardı; biz sadece "böyle eski bir sisteme nasıl bağlanılır" konusunu, gerçek bir örnek üzerinden öğrendik.

**Uygulamada ne yaptık, çok kısaca:**
1. O servisin "sözleşme dosyasını" (**WSDL**) bir araca (`dotnet-svcutil`) verdik.
2. Araç, bizim yerimize, o servisle konuşmayı bilen **hazır C# kodunu otomatik üretti** — tek satır elle yazmadık.
3. Bu üretilmiş kodu FieldOps'un her yerinde kullanmak yerine, kendi basit bir "kapı" yazdık: `IBillingAmountSpeller`.
4. Bu kapının **arkasına**, gerçek SOAP koduyla konuşan **tek bir sınıf** koyduk — SOAP'ın kendisi sadece o bir dosyada yaşıyor.
5. Test edebilmek için basit bir web adresi açtık: `/api/billing/amount-in-words?amount=250.75`.

**Neden doğrudan kullanmadık, "kapı" arkasına koyduk:** Çünkü yarın bu eski muhasebe sistemi değişirse (ya da SOAP yerine modern bir API'ye geçerlerse), **sadece o tek dosyayı** değiştiririz — hiçbir controller'a dokunmayız. Bu, Day 51 (bildirim gönderme), Day 63 (yapay zeka sağlayıcısı), Day 79'da (arama motoru) hep yaptığımız **aynı numara**.

**Sonuç olarak ne kazandık:** Gerçekten çalışan bir internet servisine, gerçek bir SOAP isteği attık, gerçek bir cevap aldık, ve bunu FieldOps'un geri kalanını hiç bozmadan yaptık.

---

## 1. Servis seçimi ve WSDL

`dataaccess.com`'un genel, herkese açık **NumberConversion** servisini kullandık. Sözleşme dosyasını (WSDL) inceledik ve `NumberToDollars` adlı işleminin tam olarak "bir tutarı yazıya dökme" ihtiyacımıza karşılık geldiğini gördük:
```
NumberToWords(ubiNum)    -- bir tam sayıyı kelimelere çeviriyor
NumberToDollars(dNum)    -- bir tutarı, tıpkı bir çekin üzerinde yazılan gibi, "iki yüz elli dolar" şeklinde yazıya döküyor
```

---

## 2. `dotnet-svcutil` ile istemci kodu üretmek

```bash
dotnet tool install --global dotnet-svcutil
cd src/FieldOps.Api
DOTNET_ROLL_FORWARD=LatestMajor dotnet-svcutil "https://www.dataaccess.com/webservicesserver/NumberConversion.wso?WSDL" --outputDir NumberConversionClient --namespace "*,FieldOps.Api.NumberConversionClient"
```
* `dotnet-svcutil` — WSDL'i okuyup bizim için C# kodu yazan resmi .NET aracı.
* `DOTNET_ROLL_FORWARD=LatestMajor` — **karşılaşılan gerçek bir engel**: `dotnet-svcutil`, .NET 9.0 çalışma zamanını hedefliyor, ama bu makinede sadece .NET 10 kurulu. .NET'in varsayılan davranışı, istenen TAM sürüm yoksa çalışmayı reddetmek — "roll forward" (ileri sarma) sadece AYNI major sürüm içinde (9.0.0 iste, 9.0.5 bul) kendiliğinden çalışır, FARKLI bir major sürüme (9'dan 10'a) asla otomatik atlamaz. Bu ortam değişkeni, ".NET'e "istediğin sürüm yoksa, kurulu olan en yeni major sürümü de dene" diyerek bu kısıtlamayı gevşetiyor.
* Sonuç: `src/FieldOps.Api/NumberConversionClient/Reference.cs` — **elle yazılmamış**, tamamen üretilmiş, 318 satırlık bir dosya.

**Üretilen kodun önemli parçaları (biz yazmadık, sadece OKUDUK):**
```csharp
public interface NumberConversionSoapType
{
    Task<NumberToWordsResponse> NumberToWordsAsync(NumberToWordsRequest request);
    Task<NumberToDollarsResponse> NumberToDollarsAsync(NumberToDollarsRequest request);
}

public partial class NumberConversionSoapTypeClient : ClientBase<NumberConversionSoapType>, NumberConversionSoapType
{
    public Task<NumberToDollarsResponse> NumberToDollarsAsync(decimal dNum) { /* ... */ }
    // basitlestirilmis "kolay" bir cagri sekli -- Request/Response nesnelerini kendisi kuruyor
}
```

---

## 3. `IBillingAmountSpeller.cs` — bizim yazdığımız "kapı"

```csharp
public interface IBillingAmountSpeller
{
    Task<string> SpellAmountInWordsAsync(decimal amount, CancellationToken cancellationToken);
}
// Bu arayuzu kullanan HICBIR yer, NumberConversionSoapTypeClient'i,
// FaultException'i, ya da SOAP'in kendisinin var oldugunu HIC bilmiyor --
// sadece "bana bir tutar ver, sana yazisini vereyim" diyen basit bir sozlesme.
```

---

## 4. `DataAccessBillingAmountSpeller.cs` — SOAP'ın yaşadığı TEK dosya

```csharp
public class DataAccessBillingAmountSpeller : IBillingAmountSpeller
{
    public async Task<string> SpellAmountInWordsAsync(decimal amount, CancellationToken cancellationToken)
    {
        using var client = new NumberConversionSoapTypeClient(NumberConversionSoapTypeClient.EndpointConfiguration.NumberConversionSoap12);
        // ClientBase<T>, IDisposable. Cagri bitince (basarili ya da basarisiz),
        // altta acilan HTTP/SOAP kaynaklari duzgunce kapatiliyor.

        try
        {
            var response = await client.NumberToDollarsAsync(amount);
            // ALTTAN ALTA: gercek bir SOAP zarfi, gercek bir HTTP POST ile
            // dataaccess.com'a gidiyor -- <NumberToDollars><dNum>250.75</dNum></NumberToDollars>
            return response.Body.NumberToDollarsResult;
        }
        catch (FaultException ex)
        {
            // SOAP'in KENDI hata bildirme sekli (Fault), BURADA, SADECE BU
            // DOSYADA yakalanip, FieldOps'un her yerde zaten kullandigi
            // sade bir InvalidOperationException'a CEVRILIYOR.
            throw new InvalidOperationException($"The billing amount-in-words service rejected {amount}: {ex.Message}", ex);
        }
    }
}
```

---

## 5. `BillingController.cs` — test edebileceğimiz web adresi

```csharp
[HttpGet("amount-in-words")]
public async Task<ActionResult<string>> GetAmountInWords([FromQuery] decimal amount, CancellationToken cancellationToken)
{
    if (amount <= 0) { return BadRequest("Amount must be a positive number."); }

    try
    {
        var words = await _billingAmountSpeller.SpellAmountInWordsAsync(amount, cancellationToken);
        return Ok(words);
    }
    catch (InvalidOperationException ex)
    {
        return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        // 502 Bad Gateway -- "ben calisiyorum, isteveni dogru aldim, ama
        // isteveni yerine getirmek icin konustugum BASKA bir sunucu (SOAP
        // servisi) bana basarisiz bir cevap verdi" anlamina gelen dogru kod.
        // 400/403 yanlis olurdu -- onlar "SENIN isteğin/yetkin hatali" demek,
        // ama burada hata bizim istegimizde degil, aracilik ettigimiz dis serviste.
    }
}
```
**Bilinçli bir demo sadeleştirmesi:** Bu controller'da `X-Organization-Id`/`X-Employee-Id` kontrolü YOK — çünkü hiçbir kiracı verisine dokunmuyor, sadece herkese açık bir dönüşüm yapıyor. Gerçek bir kullanım (örn. bir faturanın üzerine tutarı yazıya dökmek) muhtemelen zaten kimlik doğrulanmış bir action'ın İÇİNDE yaşardı, kendi ayrı controller'ında değil.

---

## Regresyon (Day 83)

```
dotnet build FieldOps.slnx    → 0 Hata, 0 Uyarı (üretilen SOAP istemci kodu dahil)
dotnet test FieldOps.slnx     → 65/65, 8dk29sn (normal)
```

## Canlı doğrulama

```
GET /api/billing/amount-in-words?amount=250.75
→ "two hundred and fifty dollars and seventy five cents"
```
Bu, `dataaccess.com`'un GERÇEK, uzaktaki SOAP servisinden dönen, gerçek bir cevap — mock edilmedi, taklit edilmedi.

```
GET /api/billing/amount-in-words?amount=-5
→ 400 Bad Request: "Amount must be a positive number."
```
Bu, SOAP'a hiç ulaşmadan, kendi doğrulamamızda durduruldu.

**Dürüst bir not:** `catch (FaultException ex)` bloğu **canlı olarak tetiklenmedi** — aşırı büyük bir tutar (`99999999999999999999999999`) denendiğinde, gerçek servis bunu bir SOAP Fault olarak değil, `"number too large"` diye normal bir metin cevabı olarak döndürdü. Bu kod yolu **derlendi ve gözden geçirildi**, ama gerçek bir Fault ile **canlı doğrulanmadı** — bunu olduğu gibi kaydediyoruz, "çalışıyor" diye iddia etmiyoruz.

## Demo basitleştirmesi vs. üretim gereksinimi

* **Demo bugün:** Gerçek bir muhasebe/ERP sistemi yerine, genel/herkese açık bir demo SOAP servisi kullanıldı. Her çağrıda yeni bir istemci açılıyor — Day 67'nin `RabbitMqEventPublisher`'ının aynı "havuzlanmamış bağlantı" sadeleştirmesi.
* **Üretimde gerekli olurdu:** Gerçek bir kimlik doğrulaması (çoğu gerçek SOAP servisi anonim değildir), yeniden kullanılan/havuzlanan bir istemci, ve muhtemelen bu entegrasyonun gerçek bir iş akışına (örn. `Complete` sonrası faturalama) bağlanması.
