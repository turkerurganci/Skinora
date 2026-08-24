import { describe, it, expect, vi, beforeEach } from "vitest";

/**
 * F7a — `Session-LogoutDoesNotRevokeRefreshToken`.
 *
 * Buradaki iddia "logout çağrıldı mı" değil, OTURUMUN İKİ TARAFININ DA
 * kapandığı. Önceki hâlde arayüzdeki "Sign out" yalnız localStorage'daki access
 * token'ı siliyordu; HttpOnly refresh cookie'si sunucuda 7 gün geçerli kalıyor
 * ve aynı tarayıcıdan `POST /auth/refresh` yeni bir oturum üretebiliyordu.
 * A8'in üretimde hiçbir çağıranı yoktu.
 *
 * İkinci iddia da en az onun kadar önemli: sunucu çağrısı BAŞARISIZ olsa bile
 * yerel oturum kapanmalı. Aksi hâlde ağ koptuğunda kullanıcı çıkmak istediği
 * oturumun içinde mahsur kalır.
 */

const { logoutRequest } = vi.hoisted(() => ({ logoutRequest: vi.fn() }));
vi.mock("@/lib/api/auth", () => ({ logout: logoutRequest }));

const { storeLogout } = vi.hoisted(() => ({ storeLogout: vi.fn() }));
vi.mock("@/lib/stores/auth-store", () => ({
  useAuthStore: { getState: () => ({ logout: storeLogout }) },
}));

const { signOut } = await import("./signOut");

beforeEach(() => {
  logoutRequest.mockReset();
  storeLogout.mockReset();
});

describe("signOut", () => {
  it("sunucudaki oturumu da kapatır — A8 çağrılır", async () => {
    logoutRequest.mockResolvedValue(undefined);
    await signOut();
    expect(logoutRequest).toHaveBeenCalledTimes(1);
    expect(storeLogout).toHaveBeenCalledTimes(1);
  });

  it("A8 patlasa bile yerel oturum kapanır — kullanıcı mahsur kalmaz", async () => {
    logoutRequest.mockRejectedValue(new Error("network down"));
    await expect(signOut()).resolves.toBeUndefined();
    expect(storeLogout).toHaveBeenCalledTimes(1);
  });

  it("yerel temizlik sunucu cevabından SONRA yapılır — çağrı tokensiz gitmez", async () => {
    const order: string[] = [];
    logoutRequest.mockImplementation(async () => {
      order.push("a8");
    });
    storeLogout.mockImplementation(() => {
      order.push("local");
    });
    await signOut();
    expect(order).toEqual(["a8", "local"]);
  });
});
