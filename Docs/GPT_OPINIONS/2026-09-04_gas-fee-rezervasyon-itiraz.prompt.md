## Soru

Bir önceki turda "kesin tutarı peşin kesme, üst sınır rezerve et → gerçek yakımı oku → farkı serbest bırak" önerdin. Bu öneriyi ve iki itirazını koda karşı ölçtük; sonuçlar aşağıda. Değerlendirmemiz nerede yanlış?

## Bağlam — bir önceki turda vermediğimiz üç gerçek

1. **Fark iade edilemiyor, çünkü iade bacağının kendisi farktan pahalı.** TRON'da TRC-20 ücreti gönderenin TRX'inden yakılır, transfer edilen USDT'den kesilmez. Farkı geri göndermek **ikinci bir transfer** demek: mainnet'te ~6,4 TRX (~2,2 USD). Kalıntı fark ise sent mertebesinde (yukarı yuvarlama tavanı işlem başına 0,01 USDT). Yani düzeltme, düzelttiği hatadan ~200 kat pahalı.

2. **Kullanıcı bakiyesi yok.** İşlem bazlı escrow: alıcı işleme özel bir deposit adresine yatırır, işlem kapanınca hesap kapanır. "Sonraki bakiyeye alacak kaydet" diye bir yer mimaride mevcut değil.

3. **"Gerçekleşmeyen ücret tahsili" endişen bu kodda karşılıksız.** Hesap, gereken enerjiyi değil hot wallet'ın karşılayamadığı **açığı** fiyatlıyor: `energyShortfall = max(0, energyRequired − energyAvailable)`; açık yoksa kesinti tam olarak `0.00` ve backend o sıfırı sabite yükseltmiyor. Senin tarif ettiğin durum (provada 10.20'den 2.00 sabit kesilmesi, hiç yanmadığı hâlde) bu değişikliğin **kapattığı** durumdu.

## Kabul ettiğimiz eleştirin

Gerekçemiz yanlıştı. "Gönderim sonrası kesecek bakiye kalmaz" demiştik; para iade anında zaten platformun elinde. Doğru gerekçe "imkânsız" değil, "**düzeltme maliyeti hatadan büyük**".

## Ölçümde çıkan ve senin listende olmayan iki kusur

- **Enerji kredisi yanlış havuzdan.** Tahmin hot wallet'ın **tüm** enerjisini düşüyor; oysa iade yolunda deposit adresine sabit **200 TRX** karşılığı delege ediliyor (mainnet'te ~1.914 Energy, bir transfer ~64.285 istiyor). Stake'li bir cüzdanda tahmin `0.00` der, gerçekte çoğu yanar. Yön: **eksik kesme**, farkı platform yer — senin uyardığının tersi.
- **Yanlış-token iadesinde simüle edilen kontrat ile gönderilen kontrat farklı.** Tahmin beklenen token'la alınıyor, gönderim deposit'te duran yanlış token'la yapılıyor.

## Elenen alternatifler

| Alternatif | Neden elendi |
|---|---|
| Gerçekleşeni kes | Ücret gönderim sonrası kesinleşir; tek transferde kesilecek an yok |
| Rezerve et → mahsuplaştır → farkı iade et | İade bacağı farktan ~200 kat pahalı, kullanıcı bakiyesi yok |
| Sabit tutarda kal | Provada ölçüldü: gerçeğin 2–3 katı, bazen tamamen gereksiz |

## Kabul edilen bedeller

- Tahmin ile gerçekleşen arasındaki kalıntı fark platformda kalıyor; bugün hiç ölçülmüyor.
- Tahmin kuyruğa alma anında alınıp dondurulyor; yayına kadar normalde ≤60 sn, geçici hatada retry zinciri ~24 dk sonra **yeniden tahmin almadan** gönderiyor.
- Tahmin yolunda üst sınır yok: kur iki kat yanlış olsa kesinti sessizce iki katına çıkar.

## Senden istediğimiz

Yukarıdaki üç gerçek ışığında: rezervasyon modelini hâlâ savunuyor musun, yoksa maliyet argümanı onu bu mimaride gerçekten eliyor mu? Eğer eliyorsa, kabul ettiğimiz üç bedelden hangisi en tehlikeli ve en ucuz nasıl kapatılır? Ayrıca bulduğumuz iki kusurun ağırlığını nasıl sıralarsın?
