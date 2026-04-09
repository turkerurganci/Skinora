import {
  HubConnectionBuilder,
  HubConnection,
  LogLevel,
  HttpTransportType,
} from "@microsoft/signalr";

const SIGNALR_URL = process.env.NEXT_PUBLIC_SIGNALR_URL ?? "/hubs";

/**
 * Creates a SignalR hub connection with automatic reconnect.
 * Hub endpoints will be implemented in T61/T62.
 */
export function createHubConnection(
  hubName: string,
  accessToken?: string,
): HubConnection {
  const builder = new HubConnectionBuilder()
    .withUrl(`${SIGNALR_URL}/${hubName}`, {
      transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
      accessTokenFactory: accessToken ? () => accessToken : undefined,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(
      process.env.NODE_ENV === "development"
        ? LogLevel.Information
        : LogLevel.Warning,
    );

  return builder.build();
}
