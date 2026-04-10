# Skinora — Direct Push Bypass Log

T11 discipline-only branch protection rejiminde `SKINORA_ALLOW_DIRECT_PUSH=1` ile yapılan tüm direct push'lar burada kayıt altına alınır. Pre-push hook her bypass'ta bu dosyaya otomatik satır ekler.

**Kural:** Bypass kullanıldığında hook bu dosyaya yazar. Kullanıcı bypass commit'inden sonraki **ilk normal commit'te** bu dosyadaki değişikliği commit'lemelidir.

---

## Log

| Tarih | Kullanıcı | Branch | Commit | Sebep |
|---|---|---|---|---|
| *(T11 close-out, 2026-04-08)* | *turkerurganci* | *main* | *0327315, e44e3d2, 8d7c3b1, 7255c33* | *T11 close-out: hook kurulmadan önceki direct push'lar — tek seferlik istisna* |
| 2026-04-10 16:56 UTC | Türker urgancı | main | `529f4a2` | T15+T16 push eksigi + F0 gate check PR#10 ile birlikte esitleme |
