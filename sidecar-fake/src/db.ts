import sql from 'mssql';
import { config } from './config.js';

// StablecoinType enum order (backend Skinora.Shared.Enums.StablecoinType):
// 0 = USDT, 1 = USDC. Stored as int; mapped back to the symbol the backend's
// TryParseToken expects on the inbound payment webhook.
const TOKEN_SYMBOLS = ['USDT', 'USDC'];

let pool: sql.ConnectionPool | null = null;

async function getPool(): Promise<sql.ConnectionPool> {
  if (pool && pool.connected) {
    return pool;
  }
  pool = await new sql.ConnectionPool({
    server: config.db.server,
    port: config.db.port,
    user: config.db.user,
    password: config.db.password,
    database: config.db.database,
    options: { encrypt: false, trustServerCertificate: true },
    pool: { max: 4, min: 0, idleTimeoutMillis: 30_000 },
  }).connect();
  return pool;
}

export interface DepositPaymentAddress {
  paymentAddressId: string;
  transactionId: string;
  address: string;
  /** Exact expected amount as a 6-fraction-digit decimal string. */
  expectedAmount: string;
  tokenSymbol: string;
}

/**
 * Resolve a transaction's deposit PaymentAddress so the /__e2e payment control
 * endpoint can post a backend-valid inbound webhook (the handler loads the
 * PaymentAddress by id and validates the amount against ExpectedAmount).
 */
export async function lookupPaymentAddress(
  transactionId: string,
): Promise<DepositPaymentAddress | null> {
  const p = await getPool();
  const result = await p
    .request()
    .input('txId', sql.UniqueIdentifier, transactionId)
    .query(
      'SELECT TOP 1 Id, TransactionId, Address, ExpectedAmount, ExpectedToken ' +
        'FROM PaymentAddresses WHERE TransactionId = @txId AND IsDeleted = 0 ' +
        'ORDER BY CreatedAt DESC',
    );

  if (result.recordset.length === 0) {
    return null;
  }
  const row = result.recordset[0];
  const tokenIndex = Number(row.ExpectedToken);
  return {
    paymentAddressId: String(row.Id),
    transactionId: String(row.TransactionId),
    address: String(row.Address),
    expectedAmount: Number(row.ExpectedAmount).toFixed(6),
    tokenSymbol: TOKEN_SYMBOLS[tokenIndex] ?? 'USDT',
  };
}

export async function closePool(): Promise<void> {
  if (pool) {
    await pool.close();
    pool = null;
  }
}
