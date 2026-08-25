---
name: reference_local_stack_runbook_g
description: Yerel Docker stack'e dokunmadan önce Docs/DEPLOY_RUNBOOK.md §G oku — çıplak "docker compose up" ve nginx bayat IP tuzakları orada kayıtlı
type: reference
---

Yerel stack'i ayağa kaldırmadan/yeniden kurmadan önce `Docs/DEPLOY_RUNBOOK.md` **§G** okunmalı. İki tuzak orada yazılı ve ikisine de 2026-08-24'te runbook okunmadığı için yeniden düşüldü:

1. **Çıplak `docker compose up` YANLIŞ.** `docker-compose.override.yml` bir **dev şablonu** ve otomatik katmanlanıyor; frontend ile iki sidecar'a host kaynağını bind-mount ediyor (`./frontend:/app`). İmajlar production build (Next.js `.next/standalone`, sidecar `dist/`), dolayısıyla mount entrypoint'i gizliyor ve container `Cannot find module '/app/server.js'` ile **crash-loop**'a giriyor. Doğrusu: **`docker compose -f docker-compose.yml up -d`** (runbook hep böyle yazıyor).
2. **Container yeniden yaratılınca nginx'i yeniden başlat.** `nginx/nginx.conf`'ta `resolver` direktifi yok; `upstream` hostname'leri config yüklenirken **bir kez** çözülüyor. Backend/frontend yeniden yaratılıp yeni IP alınca reverse proxy eski IP'yi tutuyor → **502**. `docker restart skinora-reverse-proxy`.

Ayrıca ölçüme başlamadan önce **çalışan image'ın tarihine bak** (`docker inspect --format '{{.Created}}' escrow-skinora-frontend`): Docker Desktop açılışta stack'i eski image'larla ayağa kaldırıyor ve o image günün kodunu içermeyebilir — [[feedback_verify_probe_subject]].

Admin/kullanıcı arayüzünü gerçek oturumla ölçmek gerektiğinde Steam OAuth scriptlenemez; e2e harness'ının JWT-enjeksiyon yolu kullanılır (`e2e/src/jwt.ts` HS256 mint + `e2e/src/browser.ts` `injectLogin`, secret `.env`'deki `JWT_SECRET`). **Dil/kayıt akışı bu yolla ölçülemez** — enjeksiyon tam da OpenID callback'i atlar.
