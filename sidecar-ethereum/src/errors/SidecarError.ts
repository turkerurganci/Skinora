/**
 * Base error of the service. Same shape as the TRON service's
 * (`sidecar-blockchain/src/errors/SidecarError.ts`): `wallet/hdRoot.ts` is a
 * byte-identical copy shared by both services and constructs it as
 * `new SidecarError(message, code, retryable)`.
 */
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
