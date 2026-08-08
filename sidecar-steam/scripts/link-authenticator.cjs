#!/usr/bin/env node
/**
 * TEK SEFERLIK — bot hesabina Steam Mobile Authenticator baglar ve
 * shared_secret / identity_secret degerlerini secrets/steam-bots.json'a yazar.
 *
 * Neden: sidecar bu iki degeri zorunlu tutar (src/bot/BotConfig.ts) —
 *   sharedSecret   -> SteamTotp.generateAuthCode()  (login 2FA)
 *   identitySecret -> startConfirmationChecker()    (trade mobil onayi)
 * Telefondaki Steam uygulamasi bu degerleri disari vermez; authenticator'i
 * kaldirip buradan yeniden baglamak gerekir.
 *
 * ON KOSULLAR
 *   1. Hesapta AKTIF authenticator OLMAMALI (telefondan kaldirilmis olmali).
 *   2. Hesapta dogrulanmis telefon numarasi olmali — Steam SMS ile aktivasyon
 *      kodu gonderir.
 *   3. secrets/steam-bots.json icindeki accountName + password dolu olmali.
 *
 * CALISTIRMA (repo kokunden):
 *   node sidecar-steam/scripts/link-authenticator.cjs
 *
 * CIKTILAR
 *   - secrets/steam-bots.json              -> sharedSecret + identitySecret dolar
 *   - ~/skinora-bot-<accountName>.maFile   -> tam yedek (REPO DISINDA, bilerek)
 *   - konsolda revocation_code (R#####)    -> ayrica guvenli bir yere kaydet
 *
 * UYARI: Authenticator yeniden baglandigi anda Steam'in 7 gunluk sayaci sifirlanir;
 * o 7 gun boyunca bot'un trade'leri 15 gun hold'a girer.
 */

'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const readline = require('readline');
const SteamUser = require('steam-user');

const REPO_ROOT = path.resolve(__dirname, '..', '..');
const BOTS_FILE = path.join(REPO_ROOT, 'secrets', 'steam-bots.json');

/** Sik karsilasilan EResult degerleri icin okunabilir aciklama. */
const ERESULT_HINTS = {
  2: 'Fail — genel hata. Hesapta dogrulanmis telefon numarasi var mi?',
  15: 'AccessDenied — hesap bu islemi yapamiyor.',
  29: 'DuplicateRequest — hesapta ZATEN aktif bir authenticator var. Once telefondan kaldir.',
  84: 'RateLimitExceeded — cok fazla deneme. Bir sure bekleyip tekrar dene.',
};

function ask(question) {
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
  return new Promise((resolve) => {
    rl.question(question, (answer) => {
      rl.close();
      resolve(answer.trim());
    });
  });
}

function die(message) {
  console.error(`\n  HATA: ${message}\n`);
  process.exit(1);
}

function readBots() {
  if (!fs.existsSync(BOTS_FILE)) {
    die(`${BOTS_FILE} bulunamadi. Sablon: secrets/README.md`);
  }
  const parsed = JSON.parse(fs.readFileSync(BOTS_FILE, 'utf8'));
  const bot = parsed.bots && parsed.bots[0];
  if (!bot) die('secrets/steam-bots.json icinde bot kaydi yok.');

  for (const field of ['accountName', 'password']) {
    const value = bot[field];
    if (!value || String(value).startsWith('TODO_')) {
      die(`secrets/steam-bots.json -> "${field}" doldurulmamis.`);
    }
  }
  return { parsed, bot };
}

/**
 * maFile yedegini finalize'DAN ONCE yaz. Aktivasyon yarida kalirsa
 * revocation_code olmadan hesap kurtarilamaz — bu sirayi degistirme.
 */
function writeMaFileBackup(accountName, response) {
  const target = path.join(os.homedir(), `skinora-bot-${accountName}.maFile`);
  fs.writeFileSync(target, JSON.stringify(response, null, 2), { encoding: 'utf8', mode: 0o600 });
  return target;
}

function writeSecretsBack(parsed, bot, response) {
  bot.sharedSecret = response.shared_secret;
  bot.identitySecret = response.identity_secret;
  fs.writeFileSync(BOTS_FILE, `${JSON.stringify(parsed, null, 2)}\n`, 'utf8');
}

async function main() {
  const { parsed, bot } = readBots();

  console.log('\n  Skinora — bot authenticator baglama\n');
  console.log(`  Hesap: ${bot.accountName}`);
  console.log('  Hesapta aktif authenticator OLMAMALI ve telefon numarasi dogrulanmis olmali.\n');

  const go = await ask('  Devam edilsin mi? [e/H]: ');
  if (go.toLowerCase() !== 'e') {
    console.log('  Iptal edildi.');
    process.exit(0);
  }

  const client = new SteamUser();
  client.logOn({ accountName: bot.accountName, password: bot.password });

  client.on('steamGuard', (domain, callback) => {
    const label = domain ? `e-posta: ${domain}` : 'authenticator';
    ask(`\n  Steam Guard kodu (${label}): `).then((code) => callback(code));
  });

  client.on('error', (err) => {
    die(`Steam girisi basarisiz — ${err.message} (eresult ${err.eresult})`);
  });

  client.on('loggedOn', () => {
    console.log(`\n  Giris basarili — SteamID64: ${client.steamID.getSteamID64()}`);
    console.log('  Authenticator baglaniyor...');

    client.enableTwoFactor((err, response) => {
      if (err) die(`enableTwoFactor basarisiz — ${err.message}`);

      if (response.status !== SteamUser.EResult.OK) {
        const hint = ERESULT_HINTS[response.status] || 'bilinmeyen durum';
        die(`Steam authenticator baglamayi reddetti — status ${response.status}: ${hint}`);
      }

      // Once yedek, sonra aktivasyon — bkz. writeMaFileBackup yorumu.
      const backup = writeMaFileBackup(bot.accountName, response);

      console.log(`\n  maFile yedegi yazildi: ${backup}`);
      console.log('\n  ============================================================');
      console.log(`   REVOCATION CODE: ${response.revocation_code}`);
      console.log('   Bunu simdi guvenli bir yere kaydet. Authenticator erisimini');
      console.log('   kaybedersen hesabi kurtarmanin TEK yolu budur.');
      console.log('  ============================================================\n');
      console.log('  Steam telefona SMS ile aktivasyon kodu gonderdi.');

      ask('  SMS aktivasyon kodu: ').then((smsCode) => {
        client.finalizeTwoFactor(response.shared_secret, smsCode, (finalizeErr) => {
          if (finalizeErr) {
            console.error(`\n  HATA: aktivasyon basarisiz — ${finalizeErr.message}`);
            console.error(`  Yedek duruyor: ${backup}`);
            console.error(`  Revocation code: ${response.revocation_code}`);
            process.exit(1);
          }

          writeSecretsBack(parsed, bot, response);

          console.log('\n  Authenticator aktif.');
          console.log('  secrets/steam-bots.json -> sharedSecret + identitySecret yazildi.\n');
          console.log('  SONRAKI ADIM: 7 gunluk sayac SIMDI basladi. O sure dolana kadar');
          console.log('  bot\'un trade\'leri 15 gun hold\'a girer.\n');

          client.logOff();
          process.exit(0);
        });
      });
    });
  });
}

main().catch((err) => die(err.stack || err.message));
