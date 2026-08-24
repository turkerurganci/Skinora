import { beforeEach, describe, expect, it, vi } from "vitest";
import { BuyerIdentificationMethod, StablecoinType } from "@/types/enums";
import { clearWizardDraft, readWizardDraft, writeWizardDraft } from "./wizardDraft";

const STORAGE_KEY = "skinora.newTransaction.draft.v1";

const DRAFT = {
  item: { assetId: "12345", name: "AK-47 | Redline", tradeable: true },
  stablecoin: StablecoinType.USDT,
  price: "42.50",
  paymentTimeoutHours: 24,
  method: BuyerIdentificationMethod.STEAM_ID,
  buyerSteamId: "76561198000000000",
  sellerWalletAddress: "TXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  walletConfirmed: true,
} as const;

describe("wizardDraft", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    vi.restoreAllMocks();
  });

  it("round-trips a full draft", () => {
    writeWizardDraft({ ...DRAFT });
    expect(readWizardDraft()).toMatchObject(DRAFT);
  });

  it("returns null when nothing was stored", () => {
    expect(readWizardDraft()).toBeNull();
  });

  it("clears the draft", () => {
    writeWizardDraft({ ...DRAFT });
    clearWizardDraft();
    expect(readWizardDraft()).toBeNull();
  });

  describe("rejects payloads that cannot drive the wizard", () => {
    // The store outlives a deploy and anything running in the tab can write to
    // it, so a stored shape is never trusted — it is validated before it reaches
    // component state.
    it.each([
      ["not JSON", "{{{"],
      ["a JSON scalar", '"just a string"'],
      ["null", "null"],
      ["an object missing every field", "{}"],
      [
        "an unknown stablecoin",
        JSON.stringify({ ...DRAFT, stablecoin: "DOGE" }),
      ],
      [
        "an unknown identification method",
        JSON.stringify({ ...DRAFT, method: "TELEPATHY" }),
      ],
      [
        "a non-numeric timeout",
        JSON.stringify({ ...DRAFT, paymentTimeoutHours: "24" }),
      ],
      [
        "a NaN timeout",
        JSON.stringify({ ...DRAFT, paymentTimeoutHours: null }),
      ],
      [
        "an item without assetId",
        JSON.stringify({ ...DRAFT, item: { tradeable: true } }),
      ],
      [
        "an item without tradeable",
        JSON.stringify({ ...DRAFT, item: { assetId: "1" } }),
      ],
      [
        "a boolean field carrying a string",
        JSON.stringify({ ...DRAFT, walletConfirmed: "true" }),
      ],
    ])("rejects %s", (_label, raw) => {
      window.sessionStorage.setItem(STORAGE_KEY, raw);
      expect(readWizardDraft()).toBeNull();
    });
  });

  it("accepts a draft whose item is null (wizard left at step 1)", () => {
    writeWizardDraft({ ...DRAFT, item: null });
    expect(readWizardDraft()?.item).toBeNull();
  });

  it("survives a storage accessor that throws (private window)", () => {
    // A browser that blocks site data throws on access rather than returning
    // null. Starting fresh is a valid outcome; crashing the page is not.
    vi.spyOn(window.sessionStorage, "getItem").mockImplementation(() => {
      throw new Error("SecurityError");
    });
    expect(() => readWizardDraft()).not.toThrow();
    expect(readWizardDraft()).toBeNull();
  });

  it("does not throw when writing to a full or blocked store", () => {
    vi.spyOn(window.sessionStorage, "setItem").mockImplementation(() => {
      throw new Error("QuotaExceededError");
    });
    expect(() => writeWizardDraft({ ...DRAFT })).not.toThrow();
  });

  it("does not throw when clearing a blocked store", () => {
    vi.spyOn(window.sessionStorage, "removeItem").mockImplementation(() => {
      throw new Error("SecurityError");
    });
    expect(() => clearWizardDraft()).not.toThrow();
  });
});
