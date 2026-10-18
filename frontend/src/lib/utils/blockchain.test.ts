import { describe, it, expect, afterEach, vi } from "vitest";
import {
  checkAddressShape,
  ETHERSCAN_TX_BASE_URL,
  explorerTxUrl,
  networkLabel,
  parseNetwork,
  tronscanTxUrl,
  TRONSCAN_TX_BASE_URL,
} from "@/lib/utils/blockchain";
import { BlockchainNetwork } from "@/types/enums";

// Real addresses (checksums verified by the backend tests, T145/T146):
// TRON base58check and the EIP-55 vector from the spec itself.
const TRON = "TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH";
const EVM_CHECKSUMMED = "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed";

describe("tronscanTxUrl", () => {
  it("appends the tx hash to the explorer base", () => {
    expect(tronscanTxUrl("abc123")).toBe(`${TRONSCAN_TX_BASE_URL}abc123`);
  });

  it("defaults to the production Tronscan host when the env override is unset", () => {
    expect(TRONSCAN_TX_BASE_URL).toBe("https://tronscan.org/#/transaction/");
  });
});

describe("explorerTxUrl (T156 — explorer follows the network)", () => {
  it("links a TRON hash to Tronscan and an Ethereum hash to Etherscan", () => {
    expect(explorerTxUrl(BlockchainNetwork.TRON, "abc")).toBe(
      "https://tronscan.org/#/transaction/abc",
    );
    expect(explorerTxUrl(BlockchainNetwork.ETHEREUM, "0xdef")).toBe(
      "https://etherscan.io/tx/0xdef",
    );
    expect(ETHERSCAN_TX_BASE_URL).toBe("https://etherscan.io/tx/");
  });

  describe("build-time override", () => {
    afterEach(() => {
      vi.unstubAllEnvs();
      vi.resetModules();
    });

    it("an EMPTY build arg falls back to mainnet (compose passes ${VAR:-})", async () => {
      vi.stubEnv("NEXT_PUBLIC_TRONSCAN_TX_BASE_URL", "");
      vi.stubEnv("NEXT_PUBLIC_ETHERSCAN_TX_BASE_URL", "");
      vi.resetModules();
      const mod = await import("@/lib/utils/blockchain");
      expect(mod.TRONSCAN_TX_BASE_URL).toBe("https://tronscan.org/#/transaction/");
      expect(mod.ETHERSCAN_TX_BASE_URL).toBe("https://etherscan.io/tx/");
    });

    it("a testnet value is used as given", async () => {
      vi.stubEnv("NEXT_PUBLIC_ETHERSCAN_TX_BASE_URL", "https://sepolia.etherscan.io/tx/");
      vi.resetModules();
      const mod = await import("@/lib/utils/blockchain");
      expect(mod.explorerTxUrl(BlockchainNetwork.ETHEREUM, "0x1")).toBe(
        "https://sepolia.etherscan.io/tx/0x1",
      );
    });
  });
});

describe("networkLabel / parseNetwork", () => {
  it("labels each network with its token standard (04 §7.3 açık ağ etiketi)", () => {
    expect(networkLabel(BlockchainNetwork.TRON)).toBe("TRON (TRC-20)");
    expect(networkLabel(BlockchainNetwork.ETHEREUM)).toBe("Ethereum (ERC-20)");
  });

  it("reads only the exact wire values — never guesses a network", () => {
    expect(parseNetwork("TRON")).toBe(BlockchainNetwork.TRON);
    expect(parseNetwork("ETHEREUM")).toBe(BlockchainNetwork.ETHEREUM);
    for (const bad of ["tron", "Tron (TRC-20)", "TRC-20", "BSC", "", null, undefined]) {
      expect(parseNetwork(bad)).toBeNull();
    }
  });
});

describe("checkAddressShape (02 §12.3 — per-network address shape)", () => {
  it("TRON: base58, T prefix, 34 characters", () => {
    expect(checkAddressShape(BlockchainNetwork.TRON, TRON)).toBe("OK");
    expect(checkAddressShape(BlockchainNetwork.TRON, TRON.slice(0, 33))).toBe("INVALID_FORMAT");
    // 0 / O / I / l are not in the base58 alphabet.
    expect(checkAddressShape(BlockchainNetwork.TRON, `T${"0".repeat(33)}`)).toBe("INVALID_FORMAT");
    expect(checkAddressShape(BlockchainNetwork.TRON, EVM_CHECKSUMMED)).toBe("INVALID_FORMAT");
  });

  it("Ethereum: 0x + 40 hex; mixed case is the EIP-55 checksum", () => {
    expect(checkAddressShape(BlockchainNetwork.ETHEREUM, EVM_CHECKSUMMED)).toBe("OK");
    expect(checkAddressShape(BlockchainNetwork.ETHEREUM, EVM_CHECKSUMMED.slice(0, 41))).toBe(
      "INVALID_FORMAT",
    );
    expect(checkAddressShape(BlockchainNetwork.ETHEREUM, `${EVM_CHECKSUMMED}0`)).toBe(
      "INVALID_FORMAT",
    );
    expect(checkAddressShape(BlockchainNetwork.ETHEREUM, EVM_CHECKSUMMED.slice(2))).toBe(
      "INVALID_FORMAT",
    );
    expect(checkAddressShape(BlockchainNetwork.ETHEREUM, `0x${"g".repeat(40)}`)).toBe(
      "INVALID_FORMAT",
    );
    // A TRON address is never an Ethereum address.
    expect(checkAddressShape(BlockchainNetwork.ETHEREUM, TRON)).toBe("INVALID_FORMAT");
  });

  it("Ethereum all-lowercase / all-uppercase / digits-only carries no checksum → second confirmation", () => {
    expect(checkAddressShape(BlockchainNetwork.ETHEREUM, EVM_CHECKSUMMED.toLowerCase())).toBe(
      "NO_CHECKSUM",
    );
    expect(
      checkAddressShape(BlockchainNetwork.ETHEREUM, `0x${EVM_CHECKSUMMED.slice(2).toUpperCase()}`),
    ).toBe("NO_CHECKSUM");
    expect(checkAddressShape(BlockchainNetwork.ETHEREUM, `0x${"1".repeat(40)}`)).toBe(
      "NO_CHECKSUM",
    );
  });
});
