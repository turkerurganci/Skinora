/**
 * Individual bot session stub — will be implemented in T64.
 * Wraps steamcommunity + steam-user for a single bot account.
 */
export interface BotSessionConfig {
  accountName: string;
  password: string;
  sharedSecret: string;
  identitySecret: string;
}

export class BotSession {
  constructor(public readonly config: BotSessionConfig) {}
}
