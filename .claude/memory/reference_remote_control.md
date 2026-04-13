---
name: remote_control
description: VS Code Claude Code extension'ında /remote-control (/rc) komutu — mobil cihazdan session izleme ve kontrol
type: reference
---

## Aktif etme

- **Tek seferlik:** VS Code prompt'una `/rc` veya `/remote-control` yaz, ya da CLI'da `claude --remote-control`
- **Kalıcı (tüm session'lar):**
  ```bash
  claude config set --global remoteControl true
  ```

## Kullanım

1. Session başladığında URL + QR kod üretilir
2. Telefondan Claude mobile app ile QR'ı tara veya tarayıcıda URL'yi aç
3. `claude.ai/code` adresinden de session listesine erişilebilir
4. Mobil üzerinden session'ı izleyebilir, mesaj yazabilir, approval verebilirsin

Uzun süren task'larda masabaşında olmadan session'ı takip etmek için kullanışlı.
