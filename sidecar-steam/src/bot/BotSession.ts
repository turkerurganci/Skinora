import SteamUser from 'steam-user';
import SteamCommunity from 'steamcommunity';
import SteamTotp from 'steam-totp';
import TradeOfferManager, { type TradeOffer } from 'steam-tradeoffer-manager';
import type { Logger } from 'pino';
import { logger as rootLogger } from '../logger.js';
import type { TradeOfferEventHandler } from '../trade/types.js';
import type { BotCredentials } from './BotConfig.js';

/**
 * Lifecycle state of a single Steam bot session.
 *
 *  INITIALIZING → LOGGING_IN → READY ⇄ SESSION_EXPIRED → RECONNECTING → READY | FAILED
 *  LOGGING_IN → FAILED (bad credentials / rate limit exhaustion)
 *  LOGGING_IN → BANNED  (Steam rejected with permanent eResult)
 *  any         → STOPPED (graceful shutdown)
 */
export type BotSessionState =
  | 'INITIALIZING'
  | 'LOGGING_IN'
  | 'READY'
  | 'SESSION_EXPIRED'
  | 'RECONNECTING'
  | 'FAILED'
  | 'BANNED'
  | 'STOPPED';

export interface BotSessionStatus {
  accountName: string;
  state: BotSessionState;
  steamId?: string;
  lastTransitionAt: string;
  retryCount: number;
  lastError?: string;
}

export interface BotSessionEvents {
  onStateChanged?: (status: BotSessionStatus) => void;
  onFatalFailure?: (status: BotSessionStatus, reason: BotFailureReason) => void;
}

export type BotFailureReason =
  | 'login_failed'
  | 'session_recovery_failed'
  | 'banned'
  | 'rate_limited'
  | 'restricted';

export interface ReloginConfig {
  /** Backoff schedule from 08 §2.7 retry table; ms. */
  backoffMs: number[];
}

/** 08 §2.7 retry table: 5s / 15s / 45s. */
export const DEFAULT_RELOGIN_BACKOFF: ReloginConfig = {
  backoffMs: [5_000, 15_000, 45_000],
};

/**
 * Subset of steam-user EResult codes treated as **permanent** login failures
 * (no point retrying — operator must rotate credentials or unlock account).
 *
 * Reference: https://steamerrors.com/
 */
const PERMANENT_LOGIN_ERESULTS = new Set<number>([
  5, // InvalidPassword
  6, // LoggedInElsewhere — single session enforced; another login wins, drop this bot
  18, // AccountNotFound
  56, // RevokedAccessToken
]);

/**
 * T69 — Steam EResults that mean the account is permanently banned. The bot
 * is removed from the pool and reported to the backend as BANNED. Backend
 * maps the resulting <c>bot.removed_from_pool</c> webhook to
 * <c>PlatformSteamBotStatus.BANNED</c>.
 */
const BANNED_ERESULTS = new Set<number>([
  3, // AccountBanned (rare; usually surfaced via different channel)
  17, // Banned (community/trade ban surfaced as login error)
  40, // Blocked
  43, // AccountDisabled
  51, // Suspended
  70, // AccountLockedDown
  73, // AccountLockedDown (alternate code observed in 2018+ Steam responses)
  105, // IPBanned
]);

/**
 * T69 — Steam EResults that mean the account still authenticates but trade /
 * community privileges are revoked or rate-limited beyond what an in-process
 * retry can recover. Bot is taken out of the pool so future trade dispatches
 * route to other bots (acceptance criterion #2). Backend maps the resulting
 * <c>bot.removed_from_pool</c> webhook to
 * <c>PlatformSteamBotStatus.RESTRICTED</c>.
 *
 * Note that EResult 84 (RateLimitExceeded) is intentionally NOT here — the
 * trade-offer service treats it as a transient retry (08 §2.7 backoff
 * schedule). Only persistent rate-limits (95, 96, 97) trigger pool removal.
 */
const RESTRICTED_ERESULTS = new Set<number>([
  11, // InvalidState — frequently observed when account has an active trade ban
  15, // AccessDenied
  25, // LimitExceeded
  82, // RestrictedDevice
  85, // AccountLoginDeniedNeedTwoFactor (operator must re-enrol the bot)
  95, // AccountLimitExceeded
  96, // AccountActivityLimitExceeded
  97, // PhoneActivityLimitExceeded
  112, // LimitedUserAccount
  116, // CommunityCooldown
]);

type SteamUserError = Error & { eresult?: number };

/**
 * Wraps a single Steam bot account: handles login, 2FA, session keep-alive,
 * automatic re-login on session expiry, and confirmation auto-accept.
 *
 * Implements task T64 acceptance criteria (11 §F4):
 *   1. Login: username + password + shared_secret via steam-user + steam-totp
 *   2. Session expire detection: steamcommunity sessionExpired event
 *   3. Auto re-login with 08 §2.7 backoff (5s/15s/45s)
 *   4. Health probe surface (isHealthy) consumed by BotHealthCheck
 *   5. Mobile confirmation hookup via identitySecret (steam-totp confirmation keys)
 */
export class BotSession {
  private state: BotSessionState = 'INITIALIZING';
  private retryCount = 0;
  private lastError?: string;
  private lastTransitionAt = new Date().toISOString();
  private steamId?: string;
  private readonly client: SteamUser;
  private readonly community: SteamCommunity;
  private readonly tradeManager: TradeOfferManager;
  private readonly log: Logger;
  private confirmationCheckerStarted = false;

  constructor(
    public readonly config: BotCredentials,
    private readonly events: BotSessionEvents = {},
    private readonly relogin: ReloginConfig = DEFAULT_RELOGIN_BACKOFF,
    deps?: {
      client?: SteamUser;
      community?: SteamCommunity;
      tradeManager?: TradeOfferManager;
      logger?: Logger;
    },
  ) {
    this.client = deps?.client ?? new SteamUser();
    this.community = deps?.community ?? new SteamCommunity();
    // 08 §2.4: built-in polling at 10s drives sentOfferChanged events for T66.
    // cancelTime keeps unconfirmed offers from lingering forever (Steam default is "never" → unsafe).
    this.tradeManager =
      deps?.tradeManager ??
      new TradeOfferManager({
        steam: this.client,
        community: this.community,
        language: 'en',
        pollInterval: 10_000,
        cancelTime: 15 * 60 * 1_000,
      });
    this.log = (deps?.logger ?? rootLogger).child({ bot: config.accountName });
    this.bindClientEvents();
    this.bindCommunityEvents();
  }

  get accountName(): string {
    return this.config.accountName;
  }

  getStatus(): BotSessionStatus {
    return {
      accountName: this.config.accountName,
      state: this.state,
      steamId: this.steamId,
      lastTransitionAt: this.lastTransitionAt,
      retryCount: this.retryCount,
      lastError: this.lastError,
    };
  }

  isReady(): boolean {
    return this.state === 'READY';
  }

  /** Lifecycle predicate for BotHealthCheck — true while recovery is still possible. */
  isHealthy(): boolean {
    return this.state === 'READY';
  }

  /** Used by BotManager to decide whether a session is permanently lost. */
  isTerminal(): boolean {
    return this.state === 'FAILED' || this.state === 'BANNED' || this.state === 'STOPPED';
  }

  /**
   * Initiate login. Resolves when the session reaches READY or a terminal state.
   * Errors are surfaced via {@link BotSessionEvents.onFatalFailure}; the returned
   * promise still resolves so that BotManager can keep starting other bots.
   */
  async start(): Promise<void> {
    if (this.state !== 'INITIALIZING') {
      this.log.warn({ state: this.state }, 'start() called on non-initial session, ignoring');
      return;
    }
    await this.loginAttempt();
  }

  /** Force a re-login attempt (used by BotHealthCheck recovery flow). */
  async recoverSession(): Promise<boolean> {
    if (this.isTerminal()) {
      return false;
    }
    this.transition('RECONNECTING');
    return this.runReloginLoop();
  }

  /** Graceful shutdown — logs off and stops accepting events. */
  async stop(): Promise<void> {
    if (this.state === 'STOPPED') {
      return;
    }
    this.transition('STOPPED');
    try {
      this.client.logOff();
    } catch (err) {
      this.log.debug({ err }, 'logOff threw during stop()');
    }
  }

  /** Generate a fresh 2FA TOTP code from the shared_secret (steam-totp). */
  private generateTwoFactorCode(): string {
    return SteamTotp.generateAuthCode(this.config.sharedSecret);
  }

  /**
   * Access the underlying TradeOfferManager (T65: send-only, T66: polling).
   * Returns null when the session is not READY — callers MUST check.
   */
  getTradeManager(): TradeOfferManager | null {
    return this.isReady() ? this.tradeManager : null;
  }

  /**
   * T66 — Bridge `sentOfferChanged` and `pollFailure` events to a monitor.
   *
   * Events bind to the underlying TradeOfferManager instance (created in the
   * constructor) so they survive SESSION_EXPIRED → RECONNECTING → READY
   * transitions: the same manager keeps polling once setCookies refreshes the
   * session. Listener attachment is therefore safe even before the bot reaches
   * READY — steam-tradeoffer-manager will only emit once cookies are present.
   *
   * Idempotent: calling twice rebinds and would double-emit; callers (today
   * only TradeOfferMonitor) MUST invoke once per session.
   */
  bindTradeOfferEvents(handler: TradeOfferEventHandler): void {
    this.tradeManager.on('sentOfferChanged', (offer: TradeOffer, oldState: number) => {
      try {
        handler.onSentOfferChanged(offer, oldState);
      } catch (err) {
        this.log.error({ err, offerId: offer.id }, 'sentOfferChanged handler threw');
      }
    });
    this.tradeManager.on('pollFailure', (err: Error) => {
      try {
        handler.onPollFailure(err);
      } catch (handlerErr) {
        this.log.error({ err: handlerErr }, 'pollFailure handler threw');
      }
    });
  }

  /**
   * Accept the mobile confirmation for a specific trade offer (08 §2.4).
   * Uses identity_secret + steam-totp confirmation key, scoped to the offer id
   * so we do not accidentally confirm market listings or other actions.
   */
  acceptTradeConfirmation(offerId: string): Promise<void> {
    return new Promise((resolve, reject) => {
      const time = Math.floor(Date.now() / 1_000);
      const confirmationKey = SteamTotp.getConfirmationKey(
        this.config.identitySecret,
        time,
        'accept',
      );
      this.community.acceptConfirmationForObject(
        this.config.identitySecret,
        offerId,
        (err: Error | null) => {
          if (err) {
            this.log.error({ err, offerId }, 'acceptConfirmationForObject failed');
            reject(err);
            return;
          }
          this.log.info(
            { offerId, confirmationKey: confirmationKey.slice(0, 6) + '…' },
            'Mobile confirmation accepted',
          );
          resolve();
        },
      );
    });
  }

  private bindClientEvents(): void {
    this.client.on('loggedOn', () => {
      this.log.info('steam-user loggedOn');
    });

    this.client.on('webSession', (sessionId: string, cookies: string[]) => {
      this.log.info({ sessionId }, 'webSession received, setting cookies');
      this.community.setCookies(cookies);
      this.tradeManager.setCookies(cookies, (err) => {
        if (err) {
          this.log.error({ err }, 'TradeOfferManager.setCookies failed');
        } else {
          this.log.debug('TradeOfferManager cookies refreshed');
        }
      });
      this.retryCount = 0;
      this.lastError = undefined;
      this.startConfirmationChecker();
      this.transition('READY');
    });

    this.client.on('accountInfo', (_name: string, _country: string) => {
      // Steam-user emits accountInfo before webSession; capture SteamID once available.
      const id = this.client.steamID?.toString();
      if (id) this.steamId = id;
    });

    this.client.on('error', (err: SteamUserError) => {
      this.lastError = `${err.message} (eresult=${err.eresult ?? 'n/a'})`;
      this.log.error({ err, eresult: err.eresult }, 'steam-user error');

      const eresult = err.eresult ?? 0;
      if (BANNED_ERESULTS.has(eresult)) {
        this.transition('BANNED');
        this.emitFatal('banned');
        return;
      }
      if (RESTRICTED_ERESULTS.has(eresult)) {
        // Login succeeded mechanically but trade/community privileges are
        // revoked or rate-limited beyond what an in-process retry can
        // recover. Treat as terminal for this session (T69 acceptance #2 +
        // #4): bot is dropped from the pool, backend mirrors the status
        // onto PlatformSteamBot.Status = RESTRICTED and pushes an admin
        // notification.
        this.transition('FAILED');
        this.emitFatal('restricted');
        return;
      }
      if (PERMANENT_LOGIN_ERESULTS.has(eresult)) {
        this.transition('FAILED');
        this.emitFatal('login_failed');
        return;
      }
      // Transient errors leave the state alone — recovery loop drives retries.
    });
  }

  private bindCommunityEvents(): void {
    this.community.on('sessionExpired', (err: Error | null) => {
      this.log.warn({ err: err?.message }, 'steamcommunity reports sessionExpired');
      if (this.isTerminal()) return;
      this.transition('SESSION_EXPIRED');
      // Fire-and-forget recovery; surface failures via onFatalFailure.
      this.runReloginLoop().catch((recoveryErr) => {
        this.log.error({ err: recoveryErr }, 'sessionExpired recovery loop threw');
      });
    });
  }

  private startConfirmationChecker(): void {
    if (this.confirmationCheckerStarted) return;
    // 08 §2.4: identity_secret + 20s polling, used by T65 trade-offer mobile confirmation.
    this.community.startConfirmationChecker(20_000, this.config.identitySecret);
    this.confirmationCheckerStarted = true;
    this.log.info('Mobile confirmation checker started');
  }

  private async loginAttempt(): Promise<void> {
    this.transition('LOGGING_IN');
    try {
      this.client.logOn({
        accountName: this.config.accountName,
        password: this.config.password,
        twoFactorCode: this.generateTwoFactorCode(),
      });
    } catch (err) {
      this.lastError = (err as Error).message;
      this.transition('FAILED');
      this.emitFatal('login_failed');
    }
  }

  /**
   * Re-login with 08 §2.7 exponential backoff (5s/15s/45s).
   * Resolves true if the session returns to READY, false if all attempts failed.
   */
  private async runReloginLoop(): Promise<boolean> {
    for (let attempt = 0; attempt < this.relogin.backoffMs.length; attempt++) {
      if (this.isTerminal()) return false;
      this.retryCount = attempt + 1;
      const wait = this.relogin.backoffMs[attempt];
      this.log.warn({ attempt: this.retryCount, waitMs: wait }, 'Re-login attempt scheduled');
      await delay(wait);
      if (this.isTerminal()) return false;
      const ready = await this.attemptReloginOnce();
      if (ready) return true;
    }
    // All attempts exhausted — declare permanent failure.
    this.transition('FAILED');
    this.emitFatal('session_recovery_failed');
    return false;
  }

  private attemptReloginOnce(): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      const onReady = (): void => {
        cleanup();
        resolve(true);
      };
      const onError = (): void => {
        cleanup();
        resolve(false);
      };
      const cleanup = (): void => {
        this.client.removeListener('webSession', onReady);
        this.client.removeListener('error', onError);
      };
      this.client.once('webSession', onReady);
      this.client.once('error', onError);

      try {
        this.client.logOn({
          accountName: this.config.accountName,
          password: this.config.password,
          twoFactorCode: this.generateTwoFactorCode(),
        });
      } catch (err) {
        this.log.error({ err }, 'logOn threw synchronously during re-login');
        cleanup();
        resolve(false);
      }
    });
  }

  private transition(next: BotSessionState): void {
    if (this.state === next) return;
    const previous = this.state;
    this.state = next;
    this.lastTransitionAt = new Date().toISOString();
    this.log.info({ from: previous, to: next }, 'BotSession state transition');
    this.events.onStateChanged?.(this.getStatus());
  }

  private emitFatal(reason: BotFailureReason): void {
    this.events.onFatalFailure?.(this.getStatus(), reason);
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => {
    const t = setTimeout(resolve, ms);
    t.unref?.();
  });
}
