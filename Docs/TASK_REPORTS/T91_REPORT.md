# T91 — Ödeme Bilgileri ve Edge Case UI (T90 ile Subsume Edildi)

**Faz:** F5 | **Durum:** ✓ Tamamlandı (T90 PR #136 ile karşılandı) | **Tarih:** 2026-05-23 | **Doğrulama:** ✓ PASS (kanıtlı subsume haritalama)

---

## Özet

T91 plandaki 4 kabul kriterinin tamamı T90 PR [#136](https://github.com/turkerurganci/Skinora/pull/136) (squash `168d04a`) implementasyonuyla zaten karşılandı. Yeni kod yazılmadı — bu rapor T91 deliverable'larının T90 implementasyonundaki dosya:satır referanslarını belgeler ve task'ı kanıtlı olarak `✓ Tamamlandı` kapatır.

**Kök neden:** T90 ve T91'in plandaki kabul kriterleri tasarım gereği örtüşüyor. T90 ("İşlem detay sayfası — tüm state varyantları") kriterlerinde "Ödeme edge case banner'ları: eksik/fazla/yanlış token/gecikmeli" + "İptal bilgileri (sebep, tür, iade özeti)" + "State × role varyantları" yazıyor; T91 ("Ödeme bilgileri ve edge case UI") kriterleri bu kümenin alt-kümesi (ITEM_ESCROWED buyer view payment info + COMPLETED seller view payout summary + 4 edge case banner + copy button). T90 implementer'ı 4 kriterin tamamını kelimesi kelimesine karşıladı; T91 implementer'ına geriye kod yazımı kalmadı.

Proje sahibi onayı (2026-05-23): "A — Subsume task" — T91 sıfır-kod ✓ Tamamlandı, kanıtlı dosya:satır referansları + rapor + status push, küçük chore PR ile CI watch.

## Subsume Haritalama — T91 Kriterleri × T90 İmplementasyon

| # | T91 Kriter | T90 Karşılığı | Dosya:Satır |
|---|------------|----------------|-------------|
| 1 | Ödeme bilgileri bölümü: adres, tutar, token, ağ, exchange uyarısı | `PaymentInfoBlock` — ITEM_ESCROWED + buyer view'da render edilir; full address (mask'sız) + amount + stablecoin + network + countdown + 4-madde uyarı listesi (onlyTrc20 / fullAmount / noOtherToken / noExchange) | [PaymentInfoBlock.tsx:16-77](frontend/src/components/transactions/detail/PaymentInfoBlock.tsx#L16-L77) |
| 2 | Copy button (adres kopyalama) | `CopyButton` shared common component, 3 yerde wire'lanmış: ödeme adresi (PaymentInfoBlock L26), payout address + tx hash (SellerPayoutSummary L76/L87), refund address + refund tx hash (CancelInfoBlock L77/L88) | [PaymentInfoBlock.tsx:26](frontend/src/components/transactions/detail/PaymentInfoBlock.tsx#L26), [SellerPayoutSummary.tsx:76](frontend/src/components/transactions/detail/SellerPayoutSummary.tsx#L76), [SellerPayoutSummary.tsx:87](frontend/src/components/transactions/detail/SellerPayoutSummary.tsx#L87), [CancelInfoBlock.tsx:77](frontend/src/components/transactions/detail/CancelInfoBlock.tsx#L77), [CancelInfoBlock.tsx:88](frontend/src/components/transactions/detail/CancelInfoBlock.tsx#L88) |
| 3 | Ödeme özeti: fiyat, gas fee, net ödeme, tx hash | `SellerPayoutSummary` — COMPLETED + seller view: grossAmount (item fiyatı) + gasFeeFromSeller (varsa) + netAmount (net ödeme) + gasFeeDetail alt-blok (toplam/komisyondan/satıcıdan) + walletAddress + txHash + sentAt. Buyer-side CANCELLED_* refund özeti `CancelInfoBlock` refund alt-bloğunda: originalAmount + gasFee + netRefundAmount + refundAddress + txHash + refundedAt | [SellerPayoutSummary.tsx:18-97](frontend/src/components/transactions/detail/SellerPayoutSummary.tsx#L18-L97), [CancelInfoBlock.tsx:49-99](frontend/src/components/transactions/detail/CancelInfoBlock.tsx#L49-L99) |
| 4 | Edge case banner'lar: eksik / fazla / yanlış token / gecikmeli ödeme iade bilgisi | `PaymentEventBanners` — 4 enum tipi banner: INCORRECT_AMOUNT (red warning), EXCESS_AMOUNT (blue info), WRONG_TOKEN (red warning), LATE_PAYMENT (blue info, sadece CANCELLED state). Her birinde başlık + body (received/expected/token interpolasyonlu) + opsiyonel refund tx hash satırı | [PaymentEventBanners.tsx:22-55](frontend/src/components/transactions/detail/PaymentEventBanners.tsx#L22-L55) |

**i18n karşılığı (4 locale × parity):**

- `transactionDetail.paymentInfo.*` — 7 leaf (title/addressLabel/amountLabel/tokenLabel/networkLabel/remainingLabel/warnings × 4 madde) — [en.json:552-566](frontend/src/i18n/messages/en.json#L552-L566)
- `transactionDetail.paymentEvents.*` — 11 leaf (4 type × {title,body} + unknown + refundTxHashLabel) — [en.json:567-588](frontend/src/i18n/messages/en.json#L567-L588)
- `transactionDetail.sellerPayout.*` — 10 leaf (title/grossAmount/gasFeeFromSeller/netAmount/gasFeeDetail × 4 + walletAddress/txHash/sentAt) — [en.json:589-603](frontend/src/i18n/messages/en.json#L589-L603)
- `transactionDetail.cancelInfo.*` — 17 leaf (cancelledBy × 4 + cancelledAt/reason/itemReturned/paymentRefunded/yes/no + refund × 6) — [en.json:604-626](frontend/src/i18n/messages/en.json#L604-L626)

Toplam 45 i18n leaf × 4 locale (tr/en/es/zh) = 180 entry parity. T90 i18n parity check raporunda 476×4 olarak doğrulandı.

## Doğrulama Kontrol Listesi (Planda T91 §)

- [x] **Tüm ödeme edge case'leri UI'da gösterilmiş mi?** — Evet. `PaymentEventBanners.tsx` 4 enum tipini ayrı banner olarak render eder:
  - **INCORRECT_AMOUNT** (eksik tutar) → red warning, "We received {received} but expected {expected}. Refunded — send correct amount."
  - **EXCESS_AMOUNT** (fazla tutar) → blue info, "Extra amount above {expected} refunded. Transaction continues."
  - **WRONG_TOKEN** (yanlış token) → red warning, "Unsupported token refunded. Send {token} (TRC-20)."
  - **LATE_PAYMENT** (gecikmeli ödeme) → blue info, **yalnız CANCELLED state'te** render (`event.type !== "LATE_PAYMENT" || cancelled` guard); spec gereği bu event aktif state'lerde olmaz.

  Her banner'da `refundTxHash` varsa monospace alt-satır olarak gösterilir (`PaymentEventBanners.tsx:45-49`).

## Test Sonuçları

**Test beklentisi:** Yok (11_IMPLEMENTATION_PLAN.md T91: "Test beklentisi: Yok").

**Yeni kod yok:** T91 sıfır-kod task. T90 PR #136 squash `168d04a` zaten:

- `npx next build` ✓ Compiled successfully in 3.4s
- `npx tsc --noEmit` ✓ 0 hata
- `npx eslint src --max-warnings=0` ✓ 0 warning
- 4-locale parity 476/476/476/476 ✓
- Task branch CI [`26313228495`](https://github.com/turkerurganci/Skinora/actions/runs/26313228495) 10/10 ✓
- Main merge sonrası CI [`26329500104`](https://github.com/turkerurganci/Skinora/actions/runs/26329500104) ✓ (+chore CI 26329500105 ✓ + T89 CI 26309843043 ✓)

Bu rapor + IMPLEMENTATION_STATUS güncellemesi için açılan chore PR'ında CI ayrıca koşacak (10/10 job ✓ beklenir, kod değişikliği yok = format/lint/build hep yeşil).

## Altyapı Değişiklikleri

- **Migration:** Yok (T91 sıfır-kod).
- **Bağımlılık:** Yok.
- **DI / config / env:** Yok.
- **Docker / runtime:** Yok.

## Mini Güvenlik Kontrolü

T90 PR #136'da yapılmıştı — burada T91-spesifik ek bir yüzey değişikliği yok:

- **Secret sızıntısı:** Yok (yeni kod yok).
- **Auth / authorization:** Etki yok.
- **Input validation:** Etki yok.
- **Yeni dış bağımlılık:** Yok.
- **XSS:** Mevcut React JSX child render guarantee (T90 doğrulandı).

## Dış Varsayımlar (Ön-uçuş)

T91 kod yazımı içermediği için yeni dış varsayım yok. T90'ın dış varsayım denetimi (T90_REPORT §"Dış Varsayımlar") bu task için geçerli kalır; tüm 8 varsayım T90'da ✓ olarak doğrulanmıştı.

## Commit & PR

- Branch: `task/T91-subsume-by-T90`
- Commit: `a8d2f5a` — `T91: Ödeme bilgileri ve edge case UI — T90 PR #136 ile subsume edildi`
- PR: [#138](https://github.com/turkerurganci/Skinora/pull/138)
- CI: run [`26330356414`](https://github.com/turkerurganci/Skinora/actions/runs/26330356414) (izleniyor)

## Known Limitations / Follow-up

T91 sıfır-kod task olarak kapanır. T90'dan devralınan forward-deferred limitler aynen geçerlidir (kapsam değişmedi):

- **K1 (T96 devir) — SignalR real-time:** PaymentInfoBlock countdown + payment event banner'lar şu an React Query `staleTime: 5_000` + window-focus refetch ile yarı-realtime davranır; SignalR client T96'da `/hubs/transactions` `PaymentDetected` + `StateExpiring` event'lerine subscribe edip cache invalidation yapacak.
- **K3 (T-future) — Steam trade offer URL:** Ödeme bölümüyle ilgili değil ama TRADE_OFFER_SENT_TO_BUYER state'inde "Steam'e git" linki için backend DTO field eklenmeli (T90 K3, korunur).
- **K4 (T-future) — Refund address override:** Per-transaction refund address change için `AcceptTransactionRequest` kontratı genişletilmeli (T90 K4, korunur).

## Notlar

- **Working tree:** temiz (Adım -1 ✓; `git status --short` boş).
- **Main CI startup check:** 3/3 success — runs [`26329500104`](https://github.com/turkerurganci/Skinora/actions/runs/26329500104) (chore PR #137), [`26329500105`](https://github.com/turkerurganci/Skinora/actions/runs/26329500105) (chore PR #137 push), [`26313969503`](https://github.com/turkerurganci/Skinora/actions/runs/26313969503) (T90 PR #136) (Adım 0 ✓).
- **Bağımlılık kontrolü:** T90 ✓ Tamamlandı (PR #136 merge). T91 bağımlılığı tek (T90) ve karşılandı.
- **Scope kararı (2026-05-23):** Proje sahibi onayı: "A — Subsume task (Recommended)". Alternatif olarak D (plandan T91 sil) ve C (BLOCKED PLAN_CORRECTION_REQUIRED) sunuldu; subsume seçildi çünkü plan tutarlılığını koruyor (T91 satırı tarihsel kayıt olarak kalır) ve audit trail temiz.
- **Bitiş Kapısı uyum:** Bu chore PR ile rapor + status push edilir → CI watch zorunluluğu karşılanır → repo memory MEMORY.md T91 satırı eklenir → Bitiş Kapısı 8 item ✓.
- **04 §7.3 spec uyum:** T91'in 4 kabul kriteri 04 §7.3'ün 4 alt-paragrafına bire-bir denk: payment info bloğu (L971-993), ödeme özeti (L1029-1063), edge case tablosu (L1101-1106), refund summary (CANCELLED_TIMEOUT satırı L1070). Tümü T90'da implement edildi.
