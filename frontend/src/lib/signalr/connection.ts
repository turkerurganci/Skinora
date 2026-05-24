import {
  HubConnectionBuilder,
  HubConnection,
  LogLevel,
  HttpTransportType,
} from "@microsoft/signalr";

const SIGNALR_URL = process.env.NEXT_PUBLIC_SIGNALR_URL ?? "/hubs";

/**
 * Creates a SignalR hub connection with automatic reconnect.
 *
 * `tokenFactory` is invoked by the SignalR client on every connect / reconnect
 * attempt so a rotated access token (refresh flow — T32) is picked up without
 * tearing down the underlying connection wrapper. Pass `undefined` for hubs
 * that don't require authentication.
 *
 * The reconnect schedule (0s / 2s / 5s / 10s / 30s) matches the 07 §11.1–§11.2
 * resilience expectation. After the final attempt the connection enters
 * `Disconnected` state; consumers should listen for `onclose` and restart the
 * client when their gating signal (login / re-auth) returns.
 */
export function createHubConnection(
  hubName: string,
  tokenFactory?: () => string | null | Promise<string | null>,
): HubConnection {
  const builder = new HubConnectionBuilder()
    .withUrl(`${SIGNALR_URL}/${hubName}`, {
      transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
      accessTokenFactory: tokenFactory ? async () => (await tokenFactory()) ?? "" : undefined,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(
      process.env.NODE_ENV === "development" ? LogLevel.Information : LogLevel.Warning,
    );

  return builder.build();
}
