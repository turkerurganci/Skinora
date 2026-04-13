---
name: project_gpt_review_workflow
description: GPT cross-review sureci, etki yansitma akisi ve tamamlanan/bekleyen dokuman listesi
type: project
---

GPT cross-review süreci (02-08 dokümanları için):

1. Dokümanı GPT'ye gönder (manuel — ChatGPT)
2. GPT bulgularını Claude'a getir → bağımsız değerlendirme (Faz 2)
3. Onaylanan düzeltmeleri uygula → versiyon güncelle → rapor dosyası oluştur
4. GPT "TEMİZ" deyene kadar tekrarla
5. TEMİZ sonrası: **etki yansıtma** — değişikliklerin diğer dokümanlarla uyumluluğunu kontrol et, uyumsuzlukları düzelt

**Why:** GPT her dokümanı izole review eder, cross-document uyumsuzlukları yakalamaz. Etki yansıtma adımı bu boşluğu kapatır.

**How to apply:** Her TEMİZ sonrası explore agent'larla downstream/upstream analiz, sonra hedefli düzeltmeler.

## Tamamlanan Review'lar
- 02 Product Requirements: 6 round, v1.5→v2.3, TEMİZ
- 03 User Flows: 5 round, v1.5→v2.1, TEMİZ
- 04 UI Specs: 14 round, v1.4→v3.0, TEMİZ
- 05 Technical Architecture: 8 round, v1.6→v2.3, TEMİZ
- 09 Coding Guidelines: 7 round, v0.3→v0.9, TEMİZ
- 08 Integration Spec: 12 round, v1.3→v2.5, TEMİZ. Etki yansıtma tamamlandı (03,06,07).

## Devam Eden Review'lar
- 06 Data Model: 24 round tamamlandı, v2.1→v4.7, R25 bekliyor. Etki yansıtma tamamlandı (02,03,05,07,08). Kritik düzeltme: rounding AwayFromZero→ToZero (09 ile hizalandı). Yeni entity'ler: SellerPayoutIssue (#24), NotificationDelivery (#25). Asset lineage, 30+ state/type constraint, arşivleme stratejisi, idempotency concurrency/lease eklendi.

## Bekleyen Review'lar
- 07 API Design (v1.5)
