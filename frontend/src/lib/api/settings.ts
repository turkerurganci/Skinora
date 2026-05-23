import { apiClient } from "./client";

// ---------- GET /users/me/settings (07 §5.6 — U6) ----------

export interface EmailChannel {
  enabled: boolean;
  address: string | null;
  verified: boolean;
}

export interface ExternalChannel {
  enabled: boolean;
  connected: boolean;
  username: string | null;
}

export interface PlatformChannel {
  enabled: boolean;
  canDisable: boolean;
}

export interface NotificationSettings {
  email: EmailChannel;
  telegram: ExternalChannel;
  discord: ExternalChannel;
  platform: PlatformChannel;
}

/**
 * Response body for U6 — GET /users/me/settings (07 §5.6). Mirrors the
 * backend `AccountSettingsDto` (T35). Consumed by S10 (04 §7.6).
 *
 * `notifications.platform.canDisable` is always `false` — the in-app
 * channel cannot be turned off (04 §7.6).
 */
export interface AccountSettings {
  language: string;
  notifications: NotificationSettings;
}

export function getAccountSettings(): Promise<AccountSettings> {
  return apiClient<AccountSettings>("/users/me/settings");
}

// ---------- PUT /users/me/settings/language (07 §5.10 — U8) ----------

export type SupportedLanguage = "en" | "zh" | "es" | "tr";

export interface UpdateLanguageRequest {
  language: SupportedLanguage;
}

export interface LanguageResponse {
  language: string;
}

export function updateLanguage(language: SupportedLanguage): Promise<LanguageResponse> {
  return apiClient<LanguageResponse>("/users/me/settings/language", {
    method: "PUT",
    body: JSON.stringify({ language }),
  });
}

// ---------- PUT /users/me/settings/notifications (07 §5.9 — U7) ----------

export interface NotificationChannelUpdate {
  enabled?: boolean;
  address?: string;
}

export interface UpdateNotificationsRequest {
  email?: NotificationChannelUpdate;
  telegram?: NotificationChannelUpdate;
  discord?: NotificationChannelUpdate;
}

/**
 * U7 — only changed channels are sent in the request body. Response shape
 * matches U6 (07 §5.9).
 */
export function updateNotifications(body: UpdateNotificationsRequest): Promise<AccountSettings> {
  return apiClient<AccountSettings>("/users/me/settings/notifications", {
    method: "PUT",
    body: JSON.stringify(body),
  });
}

// ---------- POST /users/me/settings/email/send-verification (07 §5.7 — U15) ----------

export interface EmailVerificationSentResponse {
  sentTo: string;
  expiresIn: number;
}

export function sendEmailVerification(): Promise<EmailVerificationSentResponse> {
  return apiClient<EmailVerificationSentResponse>("/users/me/settings/email/send-verification", {
    method: "POST",
  });
}

// ---------- POST /users/me/settings/email/verify (07 §5.8 — U16) ----------

export interface VerifyEmailRequest {
  code: string;
}

export interface EmailVerifiedResponse {
  verified: boolean;
  verifiedAt: string;
}

export function verifyEmail(code: string): Promise<EmailVerifiedResponse> {
  return apiClient<EmailVerifiedResponse>("/users/me/settings/email/verify", {
    method: "POST",
    body: JSON.stringify({ code }),
  });
}

// ---------- POST /users/me/settings/telegram/connect (07 §5.11 — U9) ----------

export interface TelegramConnectResponse {
  verificationCode: string;
  botUrl: string;
  expiresIn: number;
}

export function connectTelegram(): Promise<TelegramConnectResponse> {
  return apiClient<TelegramConnectResponse>("/users/me/settings/telegram/connect", {
    method: "POST",
  });
}

// ---------- DELETE /users/me/settings/telegram (07 §5.14 — U11) ----------

export function disconnectTelegram(): Promise<null> {
  return apiClient<null>("/users/me/settings/telegram", { method: "DELETE" });
}

// ---------- POST /users/me/settings/discord/connect (07 §5.12 — U10) ----------

export interface DiscordConnectResponse {
  discordAuthUrl: string;
}

export function connectDiscord(): Promise<DiscordConnectResponse> {
  return apiClient<DiscordConnectResponse>("/users/me/settings/discord/connect", {
    method: "POST",
  });
}

// ---------- DELETE /users/me/settings/discord (07 §5.15 — U12) ----------

export function disconnectDiscord(): Promise<null> {
  return apiClient<null>("/users/me/settings/discord", { method: "DELETE" });
}

// ---------- POST /users/me/deactivate (07 §5.17 — U13) ----------

export interface AccountDeactivateResponse {
  deactivatedAt: string;
  message: string;
}

export function deactivateAccount(): Promise<AccountDeactivateResponse> {
  return apiClient<AccountDeactivateResponse>("/users/me/deactivate", {
    method: "POST",
  });
}

// ---------- DELETE /users/me (07 §5.17 — U14) ----------

export interface DeleteAccountResponse {
  deletedAt: string;
  message: string;
}

/**
 * U14 — backend expects the exact verbatim string `"SİL"` as confirmation
 * (UsersController.cs:496 / 04 §7.6). The constant is not localized; the
 * UI displays it as-is across all locales.
 */
export const DELETE_ACCOUNT_CONFIRMATION = "SİL";

export function deleteAccount(confirmation: string): Promise<DeleteAccountResponse> {
  return apiClient<DeleteAccountResponse>("/users/me", {
    method: "DELETE",
    body: JSON.stringify({ confirmation }),
  });
}
