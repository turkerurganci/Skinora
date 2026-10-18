import { BlockchainNetwork } from "@/types/enums";

/**
 * Block explorers (WP13 — "Tronscan URL sabit"; T156 — per network).
 *
 * Centralised here so each explorer host is defined once and can be pointed at
 * a testnet explorer via a build-time env in non-production environments
 * (`NEXT_PUBLIC_*` is inlined into the bundle — DEPLOY_RUNBOOK §B).
 *
 * `||`, not `??`: docker-compose passes the build arg as `${VAR:-}`, so an
 * unset value reaches the bundle as the EMPTY string, and `??` kept that empty
 * string — every link would have been just the hash. Empty means "mainnet"
 * (DEPLOY_RUNBOOK §B), so empty must fall back too.
 */
export const TRONSCAN_TX_BASE_URL =
  process.env.NEXT_PUBLIC_TRONSCAN_TX_BASE_URL || "https://tronscan.org/#/transaction/";

/** Ethereum mainnet explorer; Sepolia: `https://sepolia.etherscan.io/tx/`. */
export const ETHERSCAN_TX_BASE_URL =
  process.env.NEXT_PUBLIC_ETHERSCAN_TX_BASE_URL || "https://etherscan.io/tx/";

export function tronscanTxUrl(txHash: string): string {
  return `${TRONSCAN_TX_BASE_URL}${txHash}`;
}

/**
 * Display metadata per network (07 §1.3: the label and the explorer link are
 * produced on the client from the machine value). Names stay English in every
 * locale (04 §10.4).
 */
export interface NetworkMeta {
  /** Network name as shown in copy ("TRON", "Ethereum"). */
  name: string;
  /** Token standard of USDT on this network ("TRC-20", "ERC-20"). */
  standard: string;
  /** Explorer the transaction links open. */
  explorerName: string;
  /** Placeholder hinting the address shape (TRON base58 `T…`, EVM `0x…`). */
  addressPlaceholder: string;
}

const META: Record<BlockchainNetwork, NetworkMeta> = {
  [BlockchainNetwork.TRON]: {
    name: "TRON",
    standard: "TRC-20",
    explorerName: "Tronscan",
    addressPlaceholder: "T…",
  },
  [BlockchainNetwork.ETHEREUM]: {
    name: "Ethereum",
    standard: "ERC-20",
    explorerName: "Etherscan",
    addressPlaceholder: "0x…",
  },
};

/** Every network, in the normative enum order (TRON first — 06 §2.25). */
export const ALL_NETWORKS: readonly BlockchainNetwork[] = Object.values(BlockchainNetwork);

export function networkMeta(network: BlockchainNetwork): NetworkMeta {
  return META[network];
}

/** "TRON (TRC-20)" / "Ethereum (ERC-20)" — the explicit network label (04 §7.3). */
export function networkLabel(network: BlockchainNetwork): string {
  const meta = META[network];
  return `${meta.name} (${meta.standard})`;
}

/**
 * Reads a wire value into the enum. Strict and case-sensitive like the API
 * (07 §1.3): anything else is `null`, never a guessed default — a wrong network
 * label next to a deposit address is the one mistake this screen exists to
 * prevent, so an unknown value must show as unknown rather than as TRON.
 */
export function parseNetwork(value: string | null | undefined): BlockchainNetwork | null {
  if (!value) return null;
  return (ALL_NETWORKS as readonly string[]).includes(value) ? (value as BlockchainNetwork) : null;
}

/** Explorer link for a transaction hash on the network it lives on. */
export function explorerTxUrl(network: BlockchainNetwork, txHash: string): string {
  const base =
    network === BlockchainNetwork.ETHEREUM ? ETHERSCAN_TX_BASE_URL : TRONSCAN_TX_BASE_URL;
  return `${base}${txHash}`;
}

// ---------------------------------------------------------------------------
// Address shape (02 §12.3, 07 §5.3)
// ---------------------------------------------------------------------------

const TRON_SHAPE = /^T[1-9A-HJ-NP-Za-km-z]{33}$/;
const EVM_SHAPE = /^0x[0-9a-fA-F]{40}$/;

/**
 * Client-side pre-check of a wallet address for a network. The server is the
 * authority (TRON base58check, EIP-55 checksum — `IWalletAddressValidator`);
 * this only catches the wrong SHAPE early and tells the form when an Ethereum
 * address carries no checksum, so it can ask for the second confirmation
 * 02 §12.3 requires before anything is sent.
 *
 * - `INVALID_FORMAT` — not this network's shape (a TRON address on Ethereum,
 *   a typo in length, a non-hex character).
 * - `NO_CHECKSUM` — Ethereum, all-lowercase or all-uppercase hex: well formed,
 *   but a typo cannot be detected (EIP-55 lives in the letter case).
 * - `OK` — the shape is right; a mixed-case EIP-55 or base58check mismatch is
 *   still the server's to reject (no Keccak / SHA-256 in the bundle).
 */
export type AddressShape = "OK" | "NO_CHECKSUM" | "INVALID_FORMAT";

export function checkAddressShape(network: BlockchainNetwork, address: string): AddressShape {
  if (network === BlockchainNetwork.TRON) {
    return TRON_SHAPE.test(address) ? "OK" : "INVALID_FORMAT";
  }
  if (!EVM_SHAPE.test(address)) return "INVALID_FORMAT";
  const hex = address.slice(2);
  // Digits have no case; the checksum is carried by the letters only, so an
  // address whose letters are all one case carries none (07 §5.3).
  const letters = hex.replace(/[0-9]/g, "");
  if (letters === "" || letters === letters.toLowerCase() || letters === letters.toUpperCase()) {
    return "NO_CHECKSUM";
  }
  return "OK";
}
