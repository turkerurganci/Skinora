import { describe, it, expect } from "vitest";
import { tronscanTxUrl, TRONSCAN_TX_BASE_URL } from "@/lib/utils/blockchain";

describe("tronscanTxUrl", () => {
  it("appends the tx hash to the explorer base", () => {
    expect(tronscanTxUrl("abc123")).toBe(`${TRONSCAN_TX_BASE_URL}abc123`);
  });

  it("defaults to the production Tronscan host when the env override is unset", () => {
    expect(TRONSCAN_TX_BASE_URL).toBe("https://tronscan.org/#/transaction/");
  });
});
