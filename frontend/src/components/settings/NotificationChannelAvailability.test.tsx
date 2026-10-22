import type { ReactNode } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import messages from "@/i18n/messages/en.json";
import { ApiError } from "@/lib/api/client";
import type { AccountSettings } from "@/lib/api/settings";
import { LinkedAccountsSection } from "./LinkedAccountsSection";
import { NotificationPreferencesSection } from "./NotificationPreferencesSection";

/**
 * T165 — channel availability on the settings screen (04 §7.6, 07 §5.6
 * `available`). Production keeps Discord off (owner decision 2026-10-21):
 * no connect option for it; a channel whose provider is not configured yet
 * (Telegram before the bot token arrives) shows disabled with a note. Real
 * `en.json` so a missing key would render its dotted path and fail.
 */

const { connectTelegram, connectDiscord, updateNotifications } = vi.hoisted(() => ({
  connectTelegram: vi.fn(),
  connectDiscord: vi.fn(),
  updateNotifications: vi.fn(),
}));

vi.mock("@/lib/api/settings", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/settings")>()),
  connectTelegram,
  connectDiscord,
  updateNotifications,
}));

const UNAVAILABLE = messages.settings.linkedAccounts.unavailable;

function settings(
  overrides: {
    emailAvailable?: boolean;
    telegramAvailable?: boolean;
    discordAvailable?: boolean;
    discordConnected?: boolean;
  } = {},
): AccountSettings {
  return {
    language: "en",
    notifications: {
      email: {
        enabled: false,
        address: "user@example.com",
        verified: false,
        available: overrides.emailAvailable ?? true,
      },
      telegram: {
        enabled: false,
        connected: false,
        username: null,
        available: overrides.telegramAvailable ?? true,
      },
      discord: {
        enabled: false,
        connected: overrides.discordConnected ?? false,
        username: overrides.discordConnected ? "someone" : null,
        available: overrides.discordAvailable ?? true,
      },
      platform: { enabled: true, canDisable: false },
    },
  };
}

function renderWith(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <NextIntlClientProvider locale="en" messages={messages}>
        {ui}
      </NextIntlClientProvider>
    </QueryClientProvider>,
  );
}

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("LinkedAccountsSection — T165 availability", () => {
  it("shows both connect options when every channel is available", () => {
    renderWith(<LinkedAccountsSection settings={settings()} />);

    expect(screen.getByText("Telegram")).toBeInTheDocument();
    expect(screen.getByText("Discord")).toBeInTheDocument();
    expect(screen.queryByText(UNAVAILABLE)).not.toBeInTheDocument();
  });

  it("hides Discord entirely when it is switched off and not connected (production)", () => {
    renderWith(<LinkedAccountsSection settings={settings({ discordAvailable: false })} />);

    expect(screen.getByText("Telegram")).toBeInTheDocument();
    expect(screen.queryByText("Discord")).not.toBeInTheDocument();
    expect(screen.getAllByRole("button")).toHaveLength(1);
  });

  it("keeps a still-connected Discord visible so the user can disconnect it", () => {
    renderWith(
      <LinkedAccountsSection
        settings={settings({ discordAvailable: false, discordConnected: true })}
      />,
    );

    expect(screen.getByText("Discord")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: messages.settings.linkedAccounts.disconnect }),
    ).toBeEnabled();
  });

  it("disables Telegram connect with a note while the bot is not configured", () => {
    renderWith(<LinkedAccountsSection settings={settings({ telegramAvailable: false })} />);

    const [telegramConnect] = screen.getAllByRole("button", {
      name: messages.settings.linkedAccounts.connect,
    });
    expect(telegramConnect).toBeDisabled();
    expect(screen.getByText(UNAVAILABLE)).toBeInTheDocument();
    fireEvent.click(telegramConnect);
    expect(connectTelegram).not.toHaveBeenCalled();
  });

  it("maps a 409 CHANNEL_UNAVAILABLE from the server to the same note", async () => {
    connectTelegram.mockRejectedValueOnce(
      new ApiError({ code: "CHANNEL_UNAVAILABLE", message: "raw", details: null }, "t", 409),
    );
    renderWith(<LinkedAccountsSection settings={settings()} />);

    fireEvent.click(
      screen.getAllByRole("button", { name: messages.settings.linkedAccounts.connect })[0],
    );

    await waitFor(() => expect(screen.getByText(UNAVAILABLE)).toBeInTheDocument());
  });
});

describe("NotificationPreferencesSection — T165 availability", () => {
  it("has no Discord toggle when Discord is switched off", () => {
    renderWith(<NotificationPreferencesSection settings={settings({ discordAvailable: false })} />);

    const labels = screen.getAllByRole("switch").map((s) => s.getAttribute("aria-label"));
    expect(labels).not.toContain(messages.settings.notifications.channels.discord.label);
    expect(labels).toContain(messages.settings.notifications.channels.telegram.label);
  });

  it("disables the e-mail toggle and hides the verification button when e-mail is unavailable", () => {
    renderWith(<NotificationPreferencesSection settings={settings({ emailAvailable: false })} />);

    expect(
      screen.getByRole("switch", { name: messages.settings.notifications.channels.email.label }),
    ).toBeDisabled();
    expect(
      screen.getByText(messages.settings.notifications.channels.unavailable),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: messages.settings.notifications.verify.sendButton }),
    ).not.toBeInTheDocument();
  });

  it("offers e-mail verification when e-mail is available", () => {
    renderWith(<NotificationPreferencesSection settings={settings()} />);

    expect(
      screen.getByRole("button", { name: messages.settings.notifications.verify.sendButton }),
    ).toBeInTheDocument();
  });
});
