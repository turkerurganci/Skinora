"use client";

import { useEffect, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { updateSteamTradeUrl } from "@/lib/api/users";
import { useMyProfile } from "@/lib/hooks/useMyProfile";
import { useAuthStore } from "@/lib/stores/auth-store";
import { cn } from "@/lib/utils/cn";

type Outcome =
  | { kind: "idle" }
  | { kind: "active" }
  | { kind: "inactive"; setupGuideUrl: string | null }
  | { kind: "pending" }
  | { kind: "invalid" }
  | { kind: "error" };

/**
 * 07 §5.16a Steam Trade URL — U17'nin tek UI çağıranı.
 *
 * Neden var: `User.MobileAuthenticatorVerified` bayrağını yazan **tek** uç
 * U17'dir ve F1'e kadar onu çağıran hiçbir frontend kodu yoktu. Sonuç, UI
 * turunda ölçülen kapalı döngüydü — `/transactions/new` MA istiyor,
 * `/auth/mobile-authenticator` yalnız `/auth/me`'yi yeniden okuyor, bayrağı
 * kimse yazmıyor (`UITour-NoUiPathToVerifyMobileAuthenticator`). Bu bölüm
 * hem Ayarlar'da hem de kullanıcının engellendiğinde yönlendirildiği MA
 * sayfasında mount edilir; ikinci yerleşim olmadan çözüm, sorunun yaşandığı
 * ekranda görünmezdi.
 *
 * Üç sonuç ayrı ayrı gösterilir. "Pending" (Steam erişilemedi) **sessiz
 * başarı olarak gösterilmez**: URL kaydedilir ama MA doğrulanamamıştır ve
 * kullanıcı bunu bilmeli, yoksa neden hâlâ işlem başlatamadığını anlamaz.
 */
export function SteamTradeUrlSection({ className }: { className?: string }) {
  const t = useTranslations("settings.steamTradeUrl");
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const profile = useMyProfile(isAuthenticated);
  const queryClient = useQueryClient();

  const [value, setValue] = useState("");
  const [saving, setSaving] = useState(false);
  const [outcome, setOutcome] = useState<Outcome>({ kind: "idle" });

  const saved = profile.data?.steamTradeUrl ?? null;
  const maActive = profile.data?.mobileAuthenticatorActive ?? false;

  // Kaydedilmiş URL geldiğinde alanı bir kez doldur; kullanıcı yazmaya
  // başladıysa üstüne yazma.
  useEffect(() => {
    if (saved) setValue((v) => (v === "" ? saved : v));
  }, [saved]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!value.trim() || saving) return;
    setSaving(true);
    setOutcome({ kind: "idle" });
    try {
      const res = await updateSteamTradeUrl(value.trim());
      // `active === false && setupGuideUrl === null` → Steam'e ulaşılamadı
      // (503 pending). Ayrım gövdeden türetilebilir; sözleşme users.ts'te.
      if (res.mobileAuthenticatorActive) {
        setOutcome({ kind: "active" });
      } else if (res.setupGuideUrl === null) {
        setOutcome({ kind: "pending" });
      } else {
        setOutcome({ kind: "inactive", setupGuideUrl: res.setupGuideUrl });
      }
      setValue(res.tradeUrl);
      // MA bayrağı değişmiş olabilir → uygunluk kapısını besleyen sorguları
      // tazele, yoksa kullanıcı sayfayı elle yenilemek zorunda kalır.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["users", "me"] }),
        queryClient.invalidateQueries({ queryKey: ["auth", "me"] }),
        queryClient.invalidateQueries({ queryKey: ["transactions", "eligibility"] }),
      ]);
    } catch (err) {
      setOutcome(
        err instanceof ApiError && err.code === "INVALID_TRADE_URL"
          ? { kind: "invalid" }
          : { kind: "error" },
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className={cn("rounded-lg border border-gray-200 bg-white p-6", className)}>
      <h2 className="mb-2 text-lg font-semibold text-gray-900">{t("title")}</h2>
      <p className="mb-4 text-sm text-gray-600">{t("description")}</p>

      <div
        className={cn(
          "mb-4 inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-xs font-medium",
          maActive ? "bg-green-50 text-green-700" : "bg-amber-50 text-amber-700",
        )}
        data-testid="ma-status"
        data-active={maActive}
      >
        <span aria-hidden="true">{maActive ? "✓" : "!"}</span>
        {maActive ? t("statusActive") : t("statusInactive")}
      </div>

      <form onSubmit={(e) => void handleSubmit(e)} className="space-y-3">
        <label className="block">
          <span className="mb-1 block text-sm font-medium text-gray-700">{t("label")}</span>
          <input
            type="url"
            inputMode="url"
            value={value}
            onChange={(e) => setValue(e.target.value)}
            placeholder="https://steamcommunity.com/tradeoffer/new/?partner=...&token=..."
            disabled={saving}
            data-testid="trade-url-input"
            aria-invalid={outcome.kind === "invalid" || undefined}
            className={cn(
              "w-full rounded-md border px-3 py-2 text-sm",
              "focus:outline-none focus:ring-1",
              outcome.kind === "invalid"
                ? "border-red-400 focus:border-red-500 focus:ring-red-500"
                : "border-gray-300 focus:border-blue-500 focus:ring-blue-500",
              saving && "opacity-50",
            )}
          />
        </label>

        <p className="text-xs text-gray-500">
          {t("whereToFind")}{" "}
          <a
            href="https://steamcommunity.com/id/me/tradeoffers/privacy"
            target="_blank"
            rel="noopener noreferrer"
            className="font-medium text-blue-600 hover:underline"
          >
            {t("whereToFindLink")}
            <span aria-hidden="true"> ↗</span>
          </a>
        </p>

        <button
          type="submit"
          disabled={saving || !value.trim()}
          aria-busy={saving || undefined}
          data-testid="trade-url-save"
          className={cn(
            "inline-flex items-center justify-center rounded-md bg-blue-600 px-4 py-2",
            "text-sm font-semibold text-white shadow-sm hover:bg-blue-700",
            "focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2",
            "disabled:cursor-not-allowed disabled:bg-blue-300",
          )}
        >
          {saving ? t("saving") : t("save")}
        </button>
      </form>

      {outcome.kind === "active" && (
        <p className="mt-3 text-sm text-green-700" role="status" aria-live="polite">
          {t("resultActive")}
        </p>
      )}

      {outcome.kind === "inactive" && (
        <p className="mt-3 text-sm text-amber-700" role="status" aria-live="polite">
          {t("resultInactive")}{" "}
          {outcome.setupGuideUrl && (
            <a
              href={outcome.setupGuideUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="font-medium underline"
            >
              {t("setupGuide")}
              <span aria-hidden="true"> ↗</span>
            </a>
          )}
        </p>
      )}

      {outcome.kind === "pending" && (
        <p className="mt-3 text-sm text-amber-700" role="status" aria-live="polite">
          {t("resultPending")}
        </p>
      )}

      {outcome.kind === "invalid" && (
        <p className="mt-3 text-sm text-red-600" role="alert">
          {t("resultInvalid")}
        </p>
      )}

      {outcome.kind === "error" && (
        <p className="mt-3 text-sm text-red-600" role="alert">
          {t("resultError")}
        </p>
      )}
    </section>
  );
}
