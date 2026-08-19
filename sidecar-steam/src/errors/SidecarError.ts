export class SidecarError extends Error {
  constructor(
    message: string,
    public readonly code: string,
    public readonly retryable: boolean,
  ) {
    super(message);
    this.name = 'SidecarError';
  }
}

export class SteamApiError extends SidecarError {
  constructor(
    message: string,
    public readonly statusCode?: number,
  ) {
    super(message, 'STEAM_API_ERROR', true);
    this.name = 'SteamApiError';
  }
}
