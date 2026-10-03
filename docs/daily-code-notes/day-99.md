# Day 99 — Kod Notları

Faz 5, Hafta 19, Gün 99. Konu: **İlk Kubernetes günü — `fieldops-web`'i bir Deployment + Service olarak çalıştırmak**.

---

## 0. Önce: bunu neden yapıyoruz, hiçbir teknik detay olmadan

**Dünden hatırlarsan:** `fieldops-web`'i bir container'da çalıştırıyorduk — ama `docker run` ile, ELLE. Container çökse, kimse onu yeniden başlatmazdı. İki kopya isteseydik, ikisini de ayrı ayrı elle başlatıp takip etmemiz gerekirdi.

**Kubernetes neyi değiştiriyor:** Artık "şu container'ı ŞİMDİ çalıştır" diye EMİR vermiyoruz. Bunun yerine bir DİLEK LİSTESİ yazıyoruz: "bu imajdan HER ZAMAN 2 kopya çalışıyor olsun." Kubernetes bu listeyi SÜREKLİ gerçekle karşılaştırıyor — bir kopya ölürse, kimse bir şey demeden YENİSİNİ kendisi açıyor. Bugün bunu CANLI olarak gördük (aşağıda).

**Neden backend değil de Angular ile başladık:** `fieldops-web` hiçbir veritabanına, Redis'e, RabbitMQ'ya bağlı değil, hiçbir şifre/sır içermiyor. Kubernetes'in temel kavramlarını başka hiçbir karmaşıklık olmadan öğrenmek için ideal. `FieldOps.Api` (4 bağımlılık + connection string'ler) ileriki günlerin (ConfigMap/Secret) konusu.

**Bugünün yeni kavramları, tek cümleyle:**
- **Pod** — Kubernetes'in çalıştırdığı en küçük birim (bizim için: tek bir container'ı saran bir kutu).
- **Deployment** — "şu imajdan şu kadar Pod hep çalışsın" diyen dilek listesi.
- **Service** — Pod'lar ölüp yenisi doğdukça adresleri DEĞİŞİR; Service, onların ÖNÜNDE duran, hiç değişmeyen SABİT bir adres.

---

## 1. `k8s/fieldops-web-deployment.yaml` (yeni)

```yaml
apiVersion: apps/v1
# Bu dosyanın, Kubernetes API'sinin HANGİ sürümüne göre yazıldığı.
# Deployment türü "apps/v1" grubunda tanımlı -- bu satır her Deployment'ta AYNI.
kind: Deployment
# Bu dosyanın NE tarif ettiği: bir Deployment (dilek listesi).
metadata:
  name: fieldops-web
  # Bu Deployment'ın adı -- `kubectl get deploy fieldops-web` diye buna
  # bu adla ulaşıyoruz.
spec:
# spec -- "istenen durum" kısmı. Kubernetes bunu gerçekle SÜREKLİ karşılaştırır.
  replicas: 2
  # HER ZAMAN 2 Pod çalışsın. Biri ölürse, Kubernetes sayıyı 2'ye geri tamamlar.
  selector:
    matchLabels:
      app: fieldops-web
  # selector -- "hangi Pod'lar BENİM sayılır?" sorusunun cevabı: üzerinde
  # app=fieldops-web etiketi olan Pod'lar. Kubernetes, 2'yi sayarken SADECE
  # bu etiketli Pod'lara bakar.
  template:
  # template -- Kubernetes yeni bir Pod açması gerektiğinde KULLANACAĞI kalıp.
    metadata:
      labels:
        app: fieldops-web
        # Bu kalıptan doğan HER Pod'a bu etiket yapıştırılır -- yukarıdaki
        # selector'ın aradığı etiketin TAM AYNISI olmak ZORUNDA, yoksa
        # Deployment kendi açtığı Pod'ları "kendisinin" saymaz.
    spec:
      containers:
        - name: fieldops-web
          # Pod içindeki container'ın adı.
          image: fieldops-web:day98
          # Dün (Day 98) build ettiğimiz, nginx routing düzeltmesini içeren imaj.
          imagePullPolicy: IfNotPresent
          # "İmaj bu makinede ZATEN varsa, internetten çekmeye çalışma."
          # Bu imaj HİÇBİR registry'ye (Docker Hub vb.) gönderilmedi, sadece
          # yerelde var -- "Always" deseydik Kubernetes onu Docker Hub'da
          # arayıp BULAMAZ, Pod hiç açılmazdı. Gerçek bir registry Hafta 20'nin konusu.
          ports:
            - containerPort: 80
            # Container'ın içinde nginx'in dinlediği port (Day 98'deki
            # nginx.conf'taki `listen 80;` ile aynı).
```

---

## 2. `k8s/fieldops-web-service.yaml` (yeni)

```yaml
apiVersion: v1
# Service, Kubernetes'in "çekirdek" (core) grubunda -- bu yüzden sadece "v1".
kind: Service
metadata:
  name: fieldops-web
  # Service'in adı. Cluster İÇİNDEKİ diğer uygulamalar bu servise SADECE bu
  # adla (fieldops-web) ulaşabilir -- Pod'ların değişen IP'lerini hiç bilmeden.
spec:
  type: ClusterIP
  # ClusterIP (varsayılan tür): SADECE cluster'ın İÇİNDEN ulaşılabilen sabit
  # bir adres. Bugün dışarıdan (kendi bilgisayarımızdan) `kubectl port-forward`
  # ile geçici bir tünel açarak ulaştık; gerçek dış erişim (Ingress) bu
  # haftanın ileriki bir günü.
  selector:
    app: fieldops-web
    # Service, gelen istekleri app=fieldops-web etiketli Pod'lara dağıtır --
    # Deployment'ın Pod'larına koyduğu etiketin AYNISI. Service ile Deployment
    # birbirini DOĞRUDAN tanımıyor; ikisini birbirine bağlayan şey SADECE bu
    # ortak etiket.
  ports:
    - port: 80
      # Service'in kendi dinlediği port.
      targetPort: 80
      # İsteği Pod içindeki container'ın HANGİ portuna ileteceği.
```

---

## 3. Kullanılan komutlar

```bash
kubectl apply -f k8s/
# k8s/ klasöründeki TÜM yaml dosyalarını cluster'a gönder: "bu dilek
# listesini kaydet." Kubernetes hemen işe koyulur ve 2 Pod açar.

kubectl get pods -l app=fieldops-web
# -l app=fieldops-web -- sadece bu etiketli Pod'ları listele.

kubectl port-forward svc/fieldops-web 8082:80
# Kendi bilgisayarımızın 8082 portundan, cluster içindeki Service'in 80
# portuna geçici bir TÜNEL aç. Sadece geliştirme/test için -- gerçek dış
# erişim yolu değil.

kubectl delete pod <pod-adi>
# Bir Pod'u ELLE öldür -- bugünkü "kendini iyileştirme" testinin kendisi.
```

---

## Regresyon (Day 99)

Bugün hiçbir Angular/backend kaynak kodu değişmedi — sadece iki yeni Kubernetes YAML dosyası eklendi. Bu yüzden `ng test`/`dotnet test` bugün gerekli değil (Day 98 ile aynı gerekçe: kod değil, altyapı tanımı değişti).

## Canlı doğrulama

```
kubectl get nodes        -> docker-desktop   Ready   control-plane   v1.34.1

kubectl apply -f k8s/    -> deployment.apps/fieldops-web created
                            service/fieldops-web created
                         -> ~10 saniyede 2/2 Pod Running
                            (yerel imaj sorunsuz bulundu -- imagePullPolicy doğru çalıştı)

port-forward + curl:
  /               -> 200, gerçek Angular index.html (<app-root>)
  /work-orders/28 -> 200  (Day 98'in nginx düzeltmesi Kubernetes İÇİNDE de çalışıyor)
```

**Asıl test — Kubernetes'in kendini iyileştirmesi:**
```
önce:   fieldops-web-...-k9s2h   Running   22s
        fieldops-web-...-x2khn   Running   22s

kubectl delete pod fieldops-web-...-k9s2h

6 sn sonra:
        fieldops-web-...-hjhnm   Running   6s    <-- KİMSE istemeden, Kubernetes AÇTI
        fieldops-web-...-x2khn   Running   28s
deployment: 2/2
```
`k9s2h`'yi biz öldürdük; Kubernetes "dilek listesinde 2 yazıyor ama 1 var" farkını gördü ve 6 saniye içinde yerine YEPYENİ bir Pod (`hjhnm`, farklı bir isim, farklı bir IP) açtı. `docker run` ile bu ASLA kendiliğinden olmazdı.

**Temizlik:** Test bitince `kubectl delete -f k8s/` ile Deployment ve Service kaldırıldı (cluster açık kalıyor, sadece bugünkü kaynaklar silindi). Yarın aynı dosyalar tek komutla yeniden uygulanabilir — dilek listesi dosyalarda duruyor.

## Demo basitleştirmesi vs. üretim gereksinimi

* Tek makineli (tek node) yerel cluster — gerçek üretimde birden çok makine ve bir bulut sağlayıcı (Azure AKS vb.).
* İmaj yerelden geliyor — üretimde bir container registry'den çekilir (Hafta 20).
* Henüz **liveness/readiness probe** yok — Kubernetes şu an bir Pod'un "çalışıyor" olduğunu sadece container process'inin AYAKTA olmasından anlıyor, nginx'in GERÇEKTEN cevap verip vermediğini kontrol etmiyor. Bu, büyük ihtimalle yarının konusu.
* Dış erişim `port-forward` ile — gerçek erişim yolu (Ingress) bu haftanın ileriki bir günü.
