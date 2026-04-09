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
