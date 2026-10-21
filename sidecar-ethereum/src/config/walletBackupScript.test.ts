import { spawnSync } from 'node:child_process';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { HDNodeWallet, Mnemonic } from 'ethers';
import { afterAll, describe, expect, it } from 'vitest';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';

/**
 * T163 — the offline backup of the wallet secrets (scripts/wallet-backup/,
 * DEPLOY_RUNBOOK §L). It lives in this suite because the address check runs on
 * this package's `ethers`, and because the addresses it prints must be the
 * ones the services derive: the Ethereum ones are compared with this
 * service's own wallet class, the TRON ones with the T143 known-answer vectors
 * the TRON service pins (sidecar-blockchain HdWalletService.test.ts).
 *
 * The round trip is the restore drill in miniature: back up → delete → restore
 * → same addresses; plus the refusals (wrong passphrase, altered archive,
 * wrong label, overwrite). Nothing a test prints may contain a secret.
 *
 * Needs bash, tar, gpg and shred (Git for Windows has all four; so does the CI
 * runner). Skipped locally without them, FAILS in CI — a gate that skips is
 * not a gate (scripts/compose-exposure.test.mjs rule).
 */
const REPO = resolve(__dirname, '../../..');
const SCRIPT = join(REPO, 'scripts/wallet-backup/wallet-backup.sh');
const MANIFEST_TOOL = join(REPO, 'scripts/wallet-backup/wallet-manifest.mjs');

const MNEMONIC = `${'abandon '.repeat(11)}about`;
const ETH = new EthereumHdWallet(MNEMONIC);
const ETH_HOT = ETH.deriveSigner(30);
const ETH_FUEL = ETH.deriveSigner(31);
const TRON_HOT_KEY = HDNodeWallet.fromSeed(Mnemonic.fromPhrase(MNEMONIC).computeSeed())
  .derivePath("m/44'/195'/0'/0/0")
  .privateKey.slice(2);
/** T143 vectors, m/44'/195'/0'/0/{0..4}. */
const TRON_DEPOSITS = [
  'TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH',
  'TSeJkUh4Qv67VNFwY8LaAxERygNdy6NQZK',
  'TYJPRrdB5APNeRs4R7fYZSwW3TcrTKw2gx',
  'TRhVWK5XEDkQBDevcdCWW7RW51aRncty4W',
  'TT2X2yyubp7qpAWYYNE5JQWBtoZ7ikQFsY',
];
const PASSPHRASE = 'drill-passphrase-not-a-real-one';
const SECRETS = [
  MNEMONIC,
  ETH_HOT.privateKey.slice(2),
  ETH_FUEL.privateKey.slice(2),
  TRON_HOT_KEY,
  PASSPHRASE,
];

function hasTools(): boolean {
  return ['bash', 'tar', 'gpg', 'shred'].every(
    (tool) => spawnSync(tool, ['--version'], { encoding: 'utf8' }).status === 0,
  );
}
const TOOLS = hasTools();
if (!TOOLS && process.env.CI) {
  throw new Error(
    'walletBackupScript: bash/tar/gpg/shred missing in CI — the backup drill cannot run.',
  );
}

const work = mkdtempSync(join(tmpdir(), 'wallet-backup-'));
afterAll(() => rmSync(work, { recursive: true, force: true }));
/** MSYS tools on Windows take forward slashes. */
const posix = (path: string) => path.replace(/\\/g, '/');

function fixture(name: string, files: Record<string, string>): string {
  const dir = join(work, name);
  mkdirSync(dir, { recursive: true });
  for (const [file, content] of Object.entries(files))
    writeFileSync(join(dir, file), content + '\n');
  return dir;
}

const ALL_FIVE = {
  'tron_hd_mnemonic.txt': MNEMONIC,
  'tron_hot_wallet_private_key.txt': TRON_HOT_KEY,
  'ethereum_hd_mnemonic.txt': MNEMONIC,
  'ethereum_hot_wallet_private_key.txt': ETH_HOT.privateKey.slice(2),
  'ethereum_fuel_wallet_private_key.txt': ETH_FUEL.privateKey.slice(2),
};

const passFile = join(work, 'passphrase');
writeFileSync(passFile, PASSPHRASE + '\n');

function run(args: string[]): { status: number | null; out: string } {
  const result = spawnSync('bash', [posix(SCRIPT), ...args], {
    encoding: 'utf8',
    timeout: 120_000,
    env: { ...process.env, TMPDIR: posix(work) },
  });
  const out = `${result.stdout ?? ''}${result.stderr ?? ''}`;
  for (const secret of SECRETS) expect(out).not.toContain(secret);
  expect(out).not.toContain('abandon');
  return { status: result.status, out };
}

function addresses(dir: string): Record<string, Record<string, unknown>> {
  const result = spawnSync(process.execPath, [MANIFEST_TOOL, 'addresses', dir], {
    encoding: 'utf8',
  });
  expect(result.status, result.stderr).toBe(0);
  for (const secret of SECRETS) expect(result.stdout).not.toContain(secret);
  return JSON.parse(result.stdout);
}

function backup(src: string, out: string): string {
  const result = run([
    'backup',
    posix(src),
    posix(out),
    '--label',
    'testnet',
    '--passphrase-file',
    posix(passFile),
  ]);
  expect(result.status, result.out).toBe(0);
  const archive = readdirSync(out).find((f) => f.endsWith('.tar.gpg'));
  expect(archive, result.out).toBeDefined();
  return join(out, archive as string);
}

describe("wallet-manifest — the addresses the backup prints are the services' own", () => {
  it('derives the TRON and Ethereum deposit, hot and fuel addresses', () => {
    const derived = addresses(fixture('vectors', ALL_FIVE));

    expect(derived.tron).toEqual({
      seed: true,
      deposits: TRON_DEPOSITS.map((address, index) => ({ index, address })),
      hotWallet: TRON_DEPOSITS[0],
    });
    expect(derived.ethereum).toEqual({
      seed: true,
      deposits: [0, 1, 2, 3, 4].map((index) => ({ index, address: ETH.derive(index).address })),
      hotWallet: ETH_HOT.address,
      fuelWallet: ETH_FUEL.address,
    });
    expect((derived.ethereum.deposits as { address: string }[])[0].address).toBe(
      '0x9858EfFD232B4033E47d90003D41EC34EcaEda94',
    );
  });

  it('names a broken file without echoing it', () => {
    const result = spawnSync(
      process.execPath,
      [
        MANIFEST_TOOL,
        'addresses',
        fixture('broken', { 'tron_hd_mnemonic.txt': `${'abandon '.repeat(11)}abandon` }),
      ],
      { encoding: 'utf8' },
    );
    expect(result.status).toBe(1);
    expect(result.stderr).toContain('tron_hd_mnemonic.txt: not a valid BIP-39 phrase');
    expect(result.stderr + result.stdout).not.toContain('abandon');
  });
});

describe.skipIf(!TOOLS)(
  'wallet-backup.sh — the restore drill (backup → delete → restore → same addresses)',
  () => {
    it('restores all five secrets byte for byte and the restored directory derives the same addresses', () => {
      const src = fixture('drill-src', ALL_FIVE);
      const before = addresses(src);
      const archive = backup(src, join(work, 'drill-out'));
      expect(readFileSync(`${archive}.sha256`, 'utf8')).toMatch(
        /^[0-9a-f]{64} [ *]skinora-wallets-testnet-/,
      );

      const verified = run(['verify', posix(archive), '--passphrase-file', posix(passFile)]);
      expect(verified.status, verified.out).toBe(0);
      expect(verified.out).toContain('Archive OK  [testnet]');
      expect(verified.out).toContain(TRON_DEPOSITS[4]);

      // "Delete the secrets", as if the disk were lost.
      rmSync(src, { recursive: true, force: true });
      const target = join(work, 'drill-restored');
      // What Docker Desktop leaves when compose starts with a secret file missing.
      mkdirSync(join(target, 'tron_hd_mnemonic.txt'), { recursive: true });

      const restored = run([
        'restore',
        posix(archive),
        posix(target),
        '--label',
        'testnet',
        '--passphrase-file',
        posix(passFile),
      ]);
      expect(restored.status, restored.out).toBe(0);
      expect(restored.out).toContain('removed the empty directory Docker left');
      for (const [file, content] of Object.entries(ALL_FIVE)) {
        expect(readFileSync(join(target, file), 'utf8').trim()).toBe(content);
      }
      expect(addresses(target)).toEqual(before);
    }, 180_000);

    it('refuses a wrong passphrase, an altered archive and a backup of the other label', () => {
      const archive = backup(fixture('refuse-src', ALL_FIVE), join(work, 'refuse-out'));

      const wrongPass = join(work, 'wrong-passphrase');
      writeFileSync(wrongPass, 'not-the-passphrase-at-all\n');
      const wrong = run(['verify', posix(archive), '--passphrase-file', posix(wrongPass)]);
      expect(wrong.status).not.toBe(0);
      expect(wrong.out).toContain('decryption failed');

      const bytes = readFileSync(archive);
      bytes[Math.floor(bytes.length / 2)] ^= 0xff;
      const altered = join(work, 'altered.tar.gpg');
      writeFileSync(altered, bytes);
      const tampered = run(['verify', posix(altered), '--passphrase-file', posix(passFile)]);
      expect(tampered.status).not.toBe(0);

      const otherLabel = run([
        'restore',
        posix(archive),
        posix(join(work, 'mainnet-target')),
        '--label',
        'mainnet',
        '--passphrase-file',
        posix(passFile),
      ]);
      expect(otherLabel.status).not.toBe(0);
      expect(otherLabel.out).toContain('testnet backup, not mainnet');
      expect(existsSync(join(work, 'mainnet-target', 'tron_hd_mnemonic.txt'))).toBe(false);
    }, 180_000);

    it('never overwrites a different existing secret without --force, and leaves the directory untouched', () => {
      const archive = backup(fixture('overwrite-src', ALL_FIVE), join(work, 'overwrite-out'));
      const target = fixture('overwrite-target', {
        'ethereum_hd_mnemonic.txt': `${'zoo '.repeat(11)}wrong`,
      });

      const refused = run([
        'restore',
        posix(archive),
        posix(target),
        '--label',
        'testnet',
        '--passphrase-file',
        posix(passFile),
      ]);
      expect(refused.status).not.toBe(0);
      expect(refused.out).toContain('refusing to overwrite');
      expect(readdirSync(target)).toEqual(['ethereum_hd_mnemonic.txt']);

      const forced = run([
        'restore',
        posix(archive),
        posix(target),
        '--label',
        'testnet',
        '--force',
        '--passphrase-file',
        posix(passFile),
      ]);
      expect(forced.status, forced.out).toBe(0);
      expect(readFileSync(join(target, 'ethereum_hd_mnemonic.txt'), 'utf8').trim()).toBe(MNEMONIC);
    }, 180_000);

    it('refuses a short passphrase and a backup without a label', () => {
      const src = fixture('short-src', ALL_FIVE);
      const shortPass = join(work, 'short-passphrase');
      writeFileSync(shortPass, 'too-short\n');
      const short = run([
        'backup',
        posix(src),
        posix(join(work, 'short-out')),
        '--label',
        'testnet',
        '--passphrase-file',
        posix(shortPass),
      ]);
      expect(short.status).not.toBe(0);
      expect(short.out).toContain('shorter than 16');

      const unlabelled = run([
        'backup',
        posix(src),
        posix(join(work, 'short-out')),
        '--passphrase-file',
        posix(passFile),
      ]);
      expect(unlabelled.status).not.toBe(0);
      expect(unlabelled.out).toContain('--label testnet|mainnet is required');
      expect(
        existsSync(join(work, 'short-out')) ? readdirSync(join(work, 'short-out')) : [],
      ).toEqual([]);
    }, 60_000);
  },
);
