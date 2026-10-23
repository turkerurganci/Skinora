import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  approveOutboundClosure,
  hotToColdTransfer,
  listAdminTransactions,
  listOutboundClosureCandidates,
  listOutboundClosures,
  proposeOutboundClosure,
  rejectOutboundClosure,
} from "./admin";

/** T157 — wire shape of the new admin calls (paths, query string, bodies). */

const { apiClient } = vi.hoisted(() => ({ apiClient: vi.fn() }));
vi.mock("./client", () => ({ apiClient }));

beforeEach(() => {
  apiClient.mockReset();
  apiClient.mockResolvedValue({});
});

describe("admin API — T157", () => {
  it("sends the network filter on the AD6 list", async () => {
    await listAdminTransactions({ network: "ETHEREUM" as never, page: 1 });
    expect(apiClient).toHaveBeenCalledWith("/admin/transactions?network=ETHEREUM&page=1");
  });

  it("omits the network param when no network is chosen", async () => {
    await listAdminTransactions({ page: 2 });
    expect(apiClient).toHaveBeenCalledWith("/admin/transactions?page=2");
  });

  it("posts the hot→cold transfer with the network in the body", async () => {
    await hotToColdTransfer({ amount: 12.5, token: "USDT", network: "ETHEREUM" as never });
    const [url, init] = apiClient.mock.calls[0];
    expect(url).toBe("/admin/wallets/hot-to-cold-transfer");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body)).toEqual({ amount: 12.5, token: "USDT", network: "ETHEREUM" });
  });

  it("filters closures by status only when one is given", async () => {
    await listOutboundClosures("PROPOSED");
    await listOutboundClosures();
    expect(apiClient.mock.calls[0][0]).toBe("/admin/wallets/outbound-closures?status=PROPOSED");
    expect(apiClient.mock.calls[1][0]).toBe("/admin/wallets/outbound-closures");
  });

  it("T172a — narrows both closure lists to one network when asked", async () => {
    await listOutboundClosures("PROPOSED", "TRON");
    await listOutboundClosures(undefined, "TRON");
    await listOutboundClosureCandidates("TRON");
    await listOutboundClosureCandidates();
    expect(apiClient.mock.calls[0][0]).toBe(
      "/admin/wallets/outbound-closures?status=PROPOSED&network=TRON",
    );
    expect(apiClient.mock.calls[1][0]).toBe("/admin/wallets/outbound-closures?network=TRON");
    expect(apiClient.mock.calls[2][0]).toBe(
      "/admin/wallets/outbound-closures/candidates?network=TRON",
    );
    expect(apiClient.mock.calls[3][0]).toBe("/admin/wallets/outbound-closures/candidates");
  });

  it("posts propose, approve (typed hash) and reject (note) to the right paths", async () => {
    await proposeOutboundClosure({ blockchainTransactionId: "r1", txHash: "0xh", reason: "why" });
    await approveOutboundClosure("c/1", "0xtyped");
    await rejectOutboundClosure("c1", "no");

    expect(apiClient.mock.calls[0][0]).toBe("/admin/wallets/outbound-closures");
    expect(JSON.parse(apiClient.mock.calls[0][1].body)).toEqual({
      blockchainTransactionId: "r1",
      txHash: "0xh",
      reason: "why",
    });
    expect(apiClient.mock.calls[1][0]).toBe("/admin/wallets/outbound-closures/c%2F1/approve");
    expect(JSON.parse(apiClient.mock.calls[1][1].body)).toEqual({ txHash: "0xtyped" });
    expect(apiClient.mock.calls[2][0]).toBe("/admin/wallets/outbound-closures/c1/reject");
    expect(JSON.parse(apiClient.mock.calls[2][1].body)).toEqual({ note: "no" });
  });
});
