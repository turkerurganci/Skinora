#!/usr/bin/env node
// T163 — manifest and address check of the wallet secrets (DEPLOY_RUNBOOK §L).
//
// Called by wallet-backup.sh; usable on its own:
//   node scripts/wallet-backup/wallet-manifest.mjs manifest <secrets-dir> <label>
//   node scripts/wallet-backup/wallet-manifest.mjs addresses <secrets-dir>
//   node scripts/wallet-backup/wallet-manifest.mjs check <secrets-dir> <MANIFEST.json>
//
// NEVER prints a secret. Output is JSON on stdout: file names, SHA-256
// fingerprints (manifest only — it travels INSIDE the encrypted archive) and
// PUBLIC addresses derived exactly the way the two services derive them:
//   TRON      m/44'/195'/0'/0/{i}  from the BIP-32 master node (08 §3.2, T143)
//   Ethereum  m/44'/60'/0'/0/{i}   from the BIP-32 master node (08 §3.7.2, T150)
//   hot / fuel wallets: the address each private key controls.
// Errors name the file, never its content.
//
// Crypto comes from the sidecars' own pinned `ethers` (6.17.0): run
// `npm ci` in sidecar-ethereum (or sidecar-blockchain) once. No network.

import { createHash } from "node:crypto";
import { existsSync, readFileSync, statSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");

export const FORMAT = "skinora-wallet-backup/1";
export const DEPOSIT_INDEXES = [0, 1, 2, 3, 4];

/** Every file this backup knows. Order is the manifest order. */
export const SECRET_FILES = [
  { name: "tron_hd_mnemonic.txt", network: "tron", kind: "seed" },
  { name: "tron_hot_wallet_private_key.txt", network: "tron", kind: "hotWallet" },
  { name: "ethereum_hd_mnemonic.txt", network: "ethereum", kind: "seed" },
  { name: "ethereum_hot_wallet_private_key.txt", network: "ethereum", kind: "hotWallet" },
  { name: "ethereum_fuel_wallet_private_key.txt", network: "ethereum", kind: "fuelWallet" },
];

export class ManifestError extends Error {}

function loadEthers() {
  for (const pkg of ["sidecar-ethereum", "sidecar-blockchain"]) {
    const manifest = join(ROOT, pkg, "package.json");
    if (!existsSync(join(ROOT, pkg, "node_modules", "ethers"))) continue;
    return createRequire(manifest)("ethers");
  }
  throw new ManifestError(
    "ethers is not installed — run `npm ci` in sidecar-ethereum (or sidecar-blockchain) once.",
  );
}

const ethers = loadEthers();

/** Trimmed content, or null when the file is missing or empty (not provisioned). */
function readSecret(dir, name) {
  const path = join(dir, name);
  if (!existsSync(path)) return null;
  if (!statSync(path).isFile()) {
    throw new ManifestError(`${name}: not a regular file (Docker creates a directory for a missing secret).`);
  }
  const value = readFileSync(path, "utf8").trim();
  return value.length > 0 ? value : null;
}

function masterNode(phrase, name) {
  let mnemonic;
  try {
    mnemonic = ethers.Mnemonic.fromPhrase(phrase);
  } catch {
    throw new ManifestError(`${name}: not a valid BIP-39 phrase.`);
  }
  // fromSeed, never fromPhrase/fromMnemonic: those apply ethers' default
  // Ethereum path and return depth 5 instead of the master node (T143).
  const root = ethers.HDNodeWallet.fromSeed(mnemonic.computeSeed());
  if (root.depth !== 0) throw new ManifestError(`${name}: derivation root is not the master node.`);
  return root;
}

function signingKey(raw, name) {
  const hex = raw.startsWith("0x") || raw.startsWith("0X") ? raw.slice(2) : raw;
  if (!/^[0-9a-fA-F]{64}$/.test(hex)) throw new ManifestError(`${name}: not a 32-byte hex private key.`);
  try {
    return new ethers.SigningKey("0x" + hex.toLowerCase());
  } catch {
    throw new ManifestError(`${name}: not a valid secp256k1 private key.`);
  }
}

/** 0x-address (20 bytes) → TRON base58check address (0x41 prefix). */
export function tronAddress(evmAddress) {
  const payload = ethers.concat(["0x41", evmAddress]);
  const checksum = ethers.dataSlice(ethers.sha256(ethers.sha256(payload)), 0, 4);
  return ethers.encodeBase58(ethers.concat([payload, checksum]));
}

function evmAddressOfKey(key) {
  return ethers.computeAddress(key);
}

/** Public addresses of every secret present in `dir`. */
export function deriveAddresses(dir) {
  const out = {
    tron: { seed: false, deposits: [], hotWallet: null },
    ethereum: { seed: false, deposits: [], hotWallet: null, fuelWallet: null },
  };
  for (const file of SECRET_FILES) {
    const value = readSecret(dir, file.name);
    if (value === null) continue;
    const target = out[file.network];
    if (file.kind === "seed") {
      const root = masterNode(value, file.name);
      const coin = file.network === "tron" ? 195 : 60;
      target.seed = true;
      target.deposits = DEPOSIT_INDEXES.map((index) => {
        const child = root.derivePath(`m/44'/${coin}'/0'/0/${index}`);
        const address = file.network === "tron" ? tronAddress(child.address) : child.address;
        return { index, address };
      });
    } else {
      const evm = evmAddressOfKey(signingKey(value, file.name));
      target[file.kind] = file.network === "tron" ? tronAddress(evm) : evm;
    }
  }
  return out;
}

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

/** The files a backup of `dir` contains: present and non-empty, in manifest order. */
export function presentFiles(dir) {
  return SECRET_FILES.filter((f) => readSecret(dir, f.name) !== null).map((f) => f.name);
}

export function buildManifest(dir, label, now = new Date()) {
  if (!["testnet", "mainnet"].includes(label)) {
    throw new ManifestError(`label must be testnet or mainnet, got "${label}".`);
  }
  const files = presentFiles(dir);
  if (files.length === 0) throw new ManifestError(`no wallet secret found in ${dir}.`);
  return {
    format: FORMAT,
    label,
    createdAt: now.toISOString(),
    files: files.map((name) => ({ name, sha256: sha256(join(dir, name)) })),
    missing: SECRET_FILES.map((f) => f.name).filter((n) => !files.includes(n)),
    addresses: deriveAddresses(dir),
  };
}

/** Throws a ManifestError naming the first difference; returns the addresses on success. */
export function checkAgainstManifest(dir, manifest) {
  if (manifest?.format !== FORMAT) throw new ManifestError(`unknown manifest format ${manifest?.format}.`);
  const present = presentFiles(dir);
  const listed = manifest.files.map((f) => f.name);
  for (const name of listed) {
    if (!present.includes(name)) throw new ManifestError(`${name}: listed in the manifest but missing.`);
  }
  for (const name of present) {
    if (!listed.includes(name)) throw new ManifestError(`${name}: present but not in the manifest.`);
  }
  for (const { name, sha256: expected } of manifest.files) {
    if (sha256(join(dir, name)) !== expected) throw new ManifestError(`${name}: SHA-256 differs from the manifest.`);
  }
  const addresses = deriveAddresses(dir);
  if (JSON.stringify(addresses) !== JSON.stringify(manifest.addresses)) {
    throw new ManifestError("derived addresses differ from the manifest.");
  }
  return addresses;
}

function main(argv) {
  const [command, dir, third] = argv;
  if (command === "manifest" && dir && third) return buildManifest(dir, third);
  if (command === "addresses" && dir) return deriveAddresses(dir);
  if (command === "check" && dir && third) {
    return checkAgainstManifest(dir, JSON.parse(readFileSync(third, "utf8")));
  }
  throw new ManifestError(
    "usage: wallet-manifest.mjs manifest <dir> <testnet|mainnet> | addresses <dir> | check <dir> <MANIFEST.json>",
  );
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    process.stdout.write(JSON.stringify(main(process.argv.slice(2)), null, 2) + "\n");
  } catch (err) {
    // Our own messages name files only; anything else is reduced to its type
    // so a library error can never carry a secret to the terminal.
    const message = err instanceof ManifestError ? err.message : `unexpected ${err?.name ?? "error"}`;
    process.stderr.write(`wallet-manifest: ${message}\n`);
    process.exit(1);
  }
}
