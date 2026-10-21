import { render } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import messages from "@/i18n/messages/en.json";
import type {
  NetworkWallets,
  OutboundClosure,
  OutboundClosureCandidate,
  PlatformWallet,
} from "@/lib/api/admin";
import { BlockchainNetwork } from "@/types/enums";

/** Test-only helpers shared by the T157 wallet screen tests. */

export const TRON_HOT = "TRonHotWalletAddress0000000000000001";
export const TRON_COLD = "TRonColdWalletAddress000000000000002";
export const ETH_HOT = "0xAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
export const ETH_COLD = "0xBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
export const ETH_FUEL = "0xCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
export const HASH_A = `0x${"a".repeat(64)}`;
export const HASH_B = `0x${"b".repeat(64)}`;

export function wallet(overrides: Partial<PlatformWallet> & Pick<PlatformWallet, "role">) {
  const base: PlatformWallet = {
    role: overrides.role,
    address: null,
    configuredAddress: null,
    addressMismatch: false,
    balances: null,
    nativeFloor: null,
    belowNativeFloor: null,
  };
  return { ...base, ...overrides };
}

export function tronWallets(overrides: Partial<NetworkWallets> = {}): NetworkWallets {
  return {
    network: BlockchainNetwork.TRON,
    serviceConfigured: true,
    degraded: false,
    nativeAsset: "TRX",
    balancesStatus: "OK",
    balancesBlock: 4242,
    wallets: [
      wallet({
        role: "HOT",
        address: TRON_HOT,
        configuredAddress: TRON_HOT,
        balances: [
          { symbol: "TRX", amount: "120.5" },
          { symbol: "USDT", amount: "501.25" },
          { symbol: "USDC", amount: "7" },
        ],
        nativeFloor: "50",
        belowNativeFloor: false,
      }),
      wallet({
        role: "COLD",
        address: TRON_COLD,
        configuredAddress: TRON_COLD,
        balances: [{ symbol: "USDT", amount: "9000" }],
      }),
    ],
    outbound: { pendingCount: 2, oldestPendingAt: "2026-10-01T10:00:00Z", inFlightCount: 1 },
    recentColdTransfers: [
      {
        id: 1,
        token: "USDT",
        amount: 100,
        fromAddress: TRON_HOT,
        toAddress: TRON_COLD,
        txHash: "tron-cold-hash-0000000000000000000000000000000000000000000000000000001",
        initiatedBy: "Alice Admin",
        createdAt: "2026-10-02T10:00:00Z",
      },
    ],
    recentOutbound: [
      {
        id: "o1",
        transactionId: "t1",
        type: "SELLER_PAYOUT",
        status: "CONFIRMED",
        token: "USDT",
        amount: 20,
        toAddress: TRON_COLD,
        txHash: "tron-out-hash-00000000000000000000000000000000000000000000000000000001",
        retryCount: 0,
        createdAt: "2026-10-03T10:00:00Z",
      },
    ],
    refundFees: {
      withheld: "7.31",
      collected: "4.06",
      inTransit: "0",
      onDeposits: "3.25",
      refundChainCost: "21.4",
      collectionChainCost: "6.27",
      refundChainCostUsdt: "7.09",
      collectionChainCostUsdt: "2.08",
      netUsdt: "-1.86",
      costUnmeasured: 0,
    },
    ...overrides,
  };
}

export function ethWallets(overrides: Partial<NetworkWallets> = {}): NetworkWallets {
  return {
    network: BlockchainNetwork.ETHEREUM,
    serviceConfigured: true,
    degraded: false,
    nativeAsset: "ETH",
    balancesStatus: "OK",
    balancesBlock: 777,
    wallets: [
      wallet({
        role: "HOT",
        address: ETH_HOT,
        configuredAddress: ETH_HOT,
        balances: [
          { symbol: "ETH", amount: "0.75" },
          { symbol: "USDT", amount: "33.3" },
        ],
        nativeFloor: "0.1",
        belowNativeFloor: false,
      }),
      wallet({
        role: "COLD",
        address: ETH_COLD,
        configuredAddress: ETH_COLD,
        balances: [{ symbol: "USDT", amount: "1234" }],
      }),
      wallet({
        role: "FUEL",
        address: ETH_FUEL,
        configuredAddress: ETH_FUEL,
        balances: [{ symbol: "ETH", amount: "2" }],
        nativeFloor: "0.5",
        belowNativeFloor: false,
      }),
    ],
    outbound: { pendingCount: 0, oldestPendingAt: null, inFlightCount: 0 },
    recentColdTransfers: [],
    recentOutbound: [],
    refundFees: {
      withheld: "0.12",
      collected: "0.03",
      inTransit: "0.02",
      onDeposits: "0.07",
      refundChainCost: "0.00021",
      collectionChainCost: "0.00004",
      refundChainCostUsdt: "0.51",
      collectionChainCostUsdt: "0.1",
      netUsdt: "-0.49",
      costUnmeasured: 0,
    },
    ...overrides,
  };
}

export function candidate(
  overrides: Partial<OutboundClosureCandidate> = {},
): OutboundClosureCandidate {
  return {
    blockchainTransactionId: "row-1",
    transactionId: "tx-1",
    type: "SELLER_PAYOUT",
    token: "USDT",
    chainToken: "USDT",
    amount: 42.5,
    fromAddress: ETH_HOT,
    toAddress: "0xDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD",
    retryCount: 3,
    createdAt: "2026-10-05T10:00:00Z",
    hasLiveClosure: false,
    ...overrides,
  };
}

export function closure(overrides: Partial<OutboundClosure> = {}): OutboundClosure {
  return {
    id: "c1",
    status: "PROPOSED",
    blockchainTransactionId: "row-1",
    transactionId: "tx-1",
    rowType: "SELLER_PAYOUT",
    token: "USDT",
    chainToken: "USDT",
    amount: 42.5,
    fromAddress: ETH_HOT,
    toAddress: "0xDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD",
    rowStatus: "PENDING",
    txHash: HASH_A,
    reason: "Nonce 12 of the hot account, matched amount and recipient on-chain.",
    proposedByAdminId: "admin-other",
    proposedByName: "Bob Other",
    proposedAt: "2026-10-06T10:00:00Z",
    decidedByAdminId: null,
    decidedByName: null,
    decidedAt: null,
    decisionNote: null,
    ...overrides,
  };
}

export function renderWithProviders(node: ReactNode) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <NextIntlClientProvider locale="en" messages={messages}>
        {node}
      </NextIntlClientProvider>
    </QueryClientProvider>,
  );
}
