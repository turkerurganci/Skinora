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

/**
 * The node RAN the exact TRC-20 transfer in a `triggerconstantcontract`
 * simulation and it did not succeed (`result.result: true` with a REVERT
 * message or a failed `ret`). An answer about the transfer — not a probe that
 * failed to answer, which is a plain <see cref="SidecarError"/> with the same
 * code. Keeps that code and stays retryable, so the fee estimate endpoint
 * behaves as before; the deposit flow tells the two apart by type.
 */
export class SimulationRevertedError extends SidecarError {
  constructor(
    message: string,
    public readonly reason: string,
  ) {
    super(message, 'FEE_ESTIMATE_SIMULATION_FAILED', true);
    this.name = 'SimulationRevertedError';
  }
}

export class InsufficientGasError extends SidecarError {
  constructor(message = 'Insufficient TRX for Energy/Bandwidth') {
    super(message, 'INSUFFICIENT_GAS', false);
    this.name = 'InsufficientGasError';
  }
}

export class TransactionFailedError extends SidecarError {
  constructor(
    message: string,
    public readonly txId?: string,
  ) {
    super(message, 'TRANSACTION_FAILED', true);
    this.name = 'TransactionFailedError';
  }
}

/**
 * T172b — the signer refused <i>for now</i> and nothing was signed or spent.
 * The transfer handlers answer 503 with <c>deferred: true</c> (the Ethereum
 * service's shape, T152), so the backend keeps the row PENDING without
 * spending a retry and asks again (HttpBlockchainTransferClient →
 * <c>TransferBroadcastStatus.Deferred</c>): the refund waits, it is never
 * FAILED and never re-opened under a new id.
 */
export class TransferDeferredError extends SidecarError {
  readonly deferred = true;

  constructor(message: string, code: string) {
    super(message, code, true);
    this.name = 'TransferDeferredError';
  }
}
