export interface WebhookPayload {
  event: string;
  timestamp: string;
  data: Record<string, unknown>;
}
