/**
 * Minimal ambient declaration for `steam-tradeoffer-manager@^2.13.x`.
 *
 * DefinitelyTyped does not publish `@types/steam-tradeoffer-manager`. We only
 * declare the surface we actually use in T65/T66 (send, cancel, addItems,
 * setCookies, ETradeOfferState). When T66 extends polling support, additional
 * methods (pollData, EOfferFilter, events) can be added here.
 */
declare module 'steam-tradeoffer-manager' {
  import { EventEmitter } from 'events';

  export type ItemDescriptor = {
    assetid: string;
    appid: number;
    contextid: string;
    amount?: number;
  };

  /** Subset of Steam's EResult enum surfaced by trade offer manager errors. */
  export interface TradeOfferError extends Error {
    eresult?: number;
    cause?: string;
  }

  export class TradeOffer {
    id?: string;
    state: number;
    partner: { getSteamID64(): string };
    itemsToGive: ItemDescriptor[];
    itemsToReceive: ItemDescriptor[];
    message: string;

    addMyItem(item: ItemDescriptor): boolean;
    addTheirItem(item: ItemDescriptor): boolean;
    setMessage(message: string): void;
    send(callback: (err: TradeOfferError | null, status: 'pending' | 'sent') => void): void;
    cancel(callback: (err: TradeOfferError | null) => void): void;
  }

  /** ETradeOfferState — 08 §2.4 status table. */
  export const ETradeOfferState: {
    Invalid: 1;
    Active: 2;
    Accepted: 3;
    Countered: 4;
    Expired: 5;
    Canceled: 6;
    Declined: 7;
    InvalidItems: 8;
    CreatedNeedsConfirmation: 9;
    CanceledBySecondFactor: 10;
    InEscrow: 11;
  };

  export interface TradeOfferManagerOptions {
    steam?: unknown;
    community?: unknown;
    domain?: string;
    language?: string;
    pollInterval?: number;
    cancelTime?: number;
    pendingCancelTime?: number;
  }

  export default class TradeOfferManager extends EventEmitter {
    constructor(options: TradeOfferManagerOptions);
    static readonly ETradeOfferState: typeof ETradeOfferState;

    setCookies(cookies: string[], callback?: (err: Error | null) => void): void;
    createOffer(partner: string): TradeOffer;
    shutdown(): void;

    /** T66: emitted by built-in polling when a tracked sent offer changes state. */
    on(
      event: 'sentOfferChanged',
      listener: (offer: TradeOffer, oldState: number) => void,
    ): this;
    /** T66: emitted when the polling cycle itself fails (network/HTTP/auth). */
    on(event: 'pollFailure', listener: (err: Error) => void): this;
    /** T66: emitted after a successful polling cycle (mostly for diagnostics). */
    on(event: 'pollSuccess', listener: () => void): this;
    // Fallback overload — keep the EventEmitter contract for events we do not
    // explicitly model (newOffer, receivedOfferChanged, debug, etc.).
    on(event: string | symbol, listener: (...args: unknown[]) => void): this;
  }
}
