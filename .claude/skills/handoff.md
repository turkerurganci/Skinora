# Handoff — Chat Geçişi

> **Ne zaman kullanılır:** Yeni bir chat'e geçilmeden önce, mevcut oturumun temiz şekilde kapatılması ve sonraki oturumun sorunsuz başlayabilmesi için.
>
> **Tetikleme:** Proje sahibi "yeni chate geçiyorum", "oturumu kapat", "handoff" dediğinde bu skill çalıştırılır.

## Kontrol Adımları

1. **Bekleyen iş kontrolü:** Mevcut oturumda başlanmış ama tamamlanmamış iş var mı?
   - Varsa → tamamla veya proje sahibine bildir
   - Todo listesinde kalan maddeler varsa → tamamla veya temizle

2. **MEMORY.md güncelleme:** `memory/MEMORY.md` dosyasını oku ve güncelle:
   - `Current Status` bölümü güncel mi? (completed, next step, not started)
   - Bu oturumda alınan kararlar veya öğrenimler memory'ye eklenmeli mi?
   - Yeni feedback kaydedilmeli mi?

3. **PRODUCT_DISCOVERY_STATUS.md güncelleme:** Doküman durumu tablosu güncel mi?
   - Tamamlanan dokümanların durumu işaretli mi?
   - Sonraki adımlar bölümü güncel mi?
   - Header versiyonu ve footer versiyonu eşleşiyor mu?
   - Son güncelleme tarihi doğru mu?

4. **Tutarsızlık taraması:** Bu oturumda değiştirilen tüm dosyalarda cross-reference kontrolü yap:
   - Dokümanlar arası referanslar doğru mu? (ekran numaraları, bölüm referansları)
   - Versiyon numaraları tüm yerlerde tutarlı mı?
   - Metodoloji dosyasında (00) öğrenimler güncellendi mi?

5. **CONTEXT.md kontrolü:** Bu oturumda yeni dosya oluşturulduysa `.claude/CONTEXT.md` dosya haritası güncel mi?

6. **Sıradaki adımı onayla:** Proje sahibine sonraki oturumda ne yapılacağını bildir.

## Çıktı Formatı

```
## Handoff Sonucu — [Tarih]

### Oturum Özeti
- **Yapılan iş:** [kısa özet]
- **Değiştirilen dosyalar:** [liste]

### Kontrol Özeti
| # | Kontrol | Sonuç |
|---|---------|-------|
| 1 | Bekleyen iş | ✓ Yok / ⚠ [detay] |
| 2 | MEMORY.md | ✓ Güncel / ⚠ Güncellendi |
| 3 | STATUS.md | ✓ Güncel / ⚠ Güncellendi |
| 4 | Tutarsızlık | ✓ Yok / ⚠ [düzeltilen] |
| 5 | CONTEXT.md | ✓ Güncel / ⚠ Güncellendi |

### Sıradaki Adım
[Sonraki oturumda yapılacak iş]
```
