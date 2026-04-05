# Checkpoint — Aşama Doğrulama

> **Ne zaman kullanılır:** Herhangi bir aşama içinde veya aşamalar arası geçişte, ilerlemeyi doğrulamak ve eksikleri yakalamak için.
>
> **Tetikleme:** Proje sahibi "checkpoint yap" veya "cross-check" dediğinde bu skill çalıştırılır.

## Kontrol Adımları

1. **Yol haritası kontrolü:** `00_PROJECT_METHODOLOGY.md` dosyasını oku. Şu an hangi aşamadayız? Sıralama doğru mu? Atlanan aşama var mı?

2. **Doküman durumu kontrolü:** `PRODUCT_DISCOVERY_STATUS.md` Bölüm 1'deki doküman durumu tablosunu oku. Tamamlanmış olması gereken ama eksik kalan doküman var mı?

3. **Tutarsızlık taraması:** Tamamlanmış tüm dokümanları oku ve şu soruları sor:
   - İki farklı dokümanda aynı konu farklı anlatılıyor mu?
   - Bir dokümanda karar alınmış ama diğerinde yansıması eksik mi?
   - Sayısal değerler (timeout, oran, limit) tüm dokümanlarda tutarlı mı?

4. **Açık kararlar kontrolü:** `PRODUCT_DISCOVERY_STATUS.md` Bölüm 8'deki "detaylandırılacak konular" listesini kontrol et. Mevcut aşamada netleşmesi gereken ama hala açık kalan karar var mı?

5. **Aşama çıktı kontrolü:** Mevcut aşamanın `00_PROJECT_METHODOLOGY.md`'deki beklenen çıktılarını kontrol et. Üretilmesi gereken ama henüz üretilmemiş çıktı var mı?

6. **Geriye dönük etki kontrolü:** Mevcut aşamada alınan kararlar önceki dokümanları etkiliyor mu? Etkileyen bir karar varsa hangi dokümanın güncellenmesi gerektiğini belirt.

## Çıktı Formatı

```
## Checkpoint Sonucu — [Tarih]
**Aşama:** [Mevcut aşama adı]
**Genel durum:** ✓ Yolunda / ⚠ Dikkat gerektiren noktalar var / ✗ Sorun tespit edildi

### Kontrol Özeti
| # | Kontrol | Sonuç | Detay |
|---|---------|-------|-------|
| 1 | Yol haritası | ✓/⚠/✗ | ... |
| 2 | Doküman durumu | ✓/⚠/✗ | ... |
| 3 | Tutarsızlık | ✓/⚠/✗ | ... |
| 4 | Açık kararlar | ✓/⚠/✗ | ... |
| 5 | Aşama çıktıları | ✓/⚠/✗ | ... |
| 6 | Geriye dönük etki | ✓/⚠/✗ | ... |

### Aksiyon Gerektiren Maddeler
- [ ] ...

### Notlar
...
```

Bu çıktı aynı zamanda `PRODUCT_DISCOVERY_STATUS.md` dosyasının "Checkpoint Log" bölümüne özet olarak eklenir.
