import sql from 'mssql';
import { e2eConfig } from './config';

let pool: sql.ConnectionPool | null = null;

async function getPool(): Promise<sql.ConnectionPool> {
  if (pool && pool.connected) return pool;
  pool = await new sql.ConnectionPool({
    server: e2eConfig.db.server,
    port: e2eConfig.db.port,
    user: e2eConfig.db.user,
    password: e2eConfig.db.password,
    database: e2eConfig.db.database,
    options: { encrypt: false, trustServerCertificate: true },
    pool: { max: 4, min: 0, idleTimeoutMillis: 30_000 },
  }).connect();
  return pool;
}

// Fixed seed identities — the e2e stack runs against a fresh DB, so these never
// collide. Two valid TRC-20 addresses (format-only validation: 'T' + 34 chars,
// base58, no 0/O/I/l).
export const seed = {
  sellerId: '11111111-1111-1111-1111-111111111111',
  sellerSteamId: '76561198000000060',
  sellerPayoutAddress: 'TKzxdSv2FZKQrEqkKVgp5DcwEXBEKMg2Ax',
  buyerId: '22222222-2222-2222-2222-222222222222',
  buyerSteamId: '76561198000000061',
  buyerRefundAddress: 'TJRyWwFs9wTFGZg3JbrVriFbSfCByEEkEN',
  botId: '33333333-3333-3333-3333-333333333333',
  botDisplayName: 'E2E-Bot',
  priceCacheId: '44444444-4444-4444-4444-444444444444',
  // Admin actor for the admin-cancel scenarios (T108). AuditLog.ActorId is an
  // FK to Users, so the admin must exist as a row; the CANCEL_TRANSACTIONS
  // permission itself comes from the super_admin role claim on the JWT.
  adminId: '55555555-5555-5555-5555-555555555555',
  adminSteamId: '76561198000000099',
  // Must match the fake's inventory item (assetId + marketHashName).
  itemAssetId: '11111111001',
  itemMarketHashName: 'AK-47 | Redline (Field-Tested)',
  price: '100.00',
};

/** Seed seller, buyer, ACTIVE bot, and a matching price-cache row (0% deviation
 *  → CREATED, not FLAGGED). Idempotent: clears prior e2e rows first.
 *
 *  The buyer is a pre-registered STEAM_ID user seeded up-front, so create sets
 *  the transaction's BuyerId — the mainline shape exercised by both the API and
 *  UI smokes. Post-WP20 the detail service's canAccept gate
 *  (`role==buyer && CREATED`) enables the accept form for a registered STEAM_ID
 *  buyer, not only a BuyerId-null prospect. */
export async function seedHappyPath(): Promise<typeof seed> {
  const p = await getPool();
  const r = () => p.request();

  // Cleanup (best-effort, FK-safe) so a re-run on a non-fresh DB is clean.
  await r()
    .input('s', sql.UniqueIdentifier, seed.sellerId)
    .input('b', sql.UniqueIdentifier, seed.buyerId)
    .batch(
      `DELETE FROM Notifications WHERE UserId IN (@s,@b);
       DELETE bt FROM BlockchainTransactions bt JOIN Transactions t ON bt.TransactionId=t.Id WHERE t.SellerId=@s;
       DELETE pa FROM PaymentAddresses pa JOIN Transactions t ON pa.TransactionId=t.Id WHERE t.SellerId=@s;
       DELETE tof FROM TradeOffers tof JOIN Transactions t ON tof.TransactionId=t.Id WHERE t.SellerId=@s;
       DELETE FROM TransactionHistory WHERE TransactionId IN (SELECT Id FROM Transactions WHERE SellerId=@s);
       DELETE FROM Transactions WHERE SellerId=@s;
       DELETE FROM Users WHERE Id IN (@s,@b);
       DELETE FROM PlatformSteamBots WHERE Id=@id_unused;`.replace('@id_unused', `'${seed.botId}'`),
    )
    .catch(() => undefined);
  await r()
    .batch(
      `DELETE FROM PlatformSteamBots WHERE SteamId='${e2eConfig.botSteamId}';
       DELETE FROM ItemPriceCaches WHERE MarketHashName='${seed.itemMarketHashName.replace(/'/g, "''")}';`,
    )
    .catch(() => undefined);

  // Seller — MA verified + payout address + backdated account (dodges new-account limit).
  await r()
    .input('id', sql.UniqueIdentifier, seed.sellerId)
    .input('steamId', sql.NVarChar(20), seed.sellerSteamId)
    .input('name', sql.NVarChar(100), 'E2E Seller')
    .input('payout', sql.NVarChar(50), seed.sellerPayoutAddress)
    .query(
      `INSERT INTO Users (Id, SteamId, SteamDisplayName, PreferredLanguage, DefaultPayoutAddress,
         MobileAuthenticatorVerified, CompletedTransactionCount, IsDeactivated, IsSuspended, IsDeleted,
         CreatedAt, UpdatedAt)
       VALUES (@id, @steamId, @name, 'en', @payout, 1, 0, 0, 0, 0,
         DATEADD(DAY,-60,SYSUTCDATETIME()), SYSUTCDATETIME());`,
    );

  // Buyer — registered STEAM_ID user, seeded up-front so create sets BuyerId.
  await insertBuyer();

  // Bot — ACTIVE (0), zero load → always selected first.
  await r()
    .input('id', sql.UniqueIdentifier, seed.botId)
    .input('steamId', sql.NVarChar(20), e2eConfig.botSteamId)
    .input('name', sql.NVarChar(100), seed.botDisplayName)
    .query(
      // Status is an nvarchar enum column → store the name 'ACTIVE', not 0.
      `INSERT INTO PlatformSteamBots (Id, SteamId, DisplayName, Status, ActiveEscrowCount,
         DailyTradeOfferCount, LastHealthCheckAt, IsDeleted, CreatedAt, UpdatedAt)
       VALUES (@id, @steamId, @name, 'ACTIVE', 0, 0, SYSUTCDATETIME(), 0, SYSUTCDATETIME(), SYSUTCDATETIME());`,
    );

  // Price cache — fresh row equal to the listing price ⇒ 0% deviation ⇒ no flag,
  // independent of Steam Market reachability.
  await r()
    .input('id', sql.UniqueIdentifier, seed.priceCacheId)
    .input('name', sql.NVarChar(200), seed.itemMarketHashName)
    .input('price', sql.Decimal(18, 2), Number(seed.price))
    .query(
      `INSERT INTO ItemPriceCaches (Id, MarketHashName, MedianPrice, LowestPrice, FetchedAt, Source,
         CreatedAt, UpdatedAt)
       VALUES (@id, @name, @price, @price, SYSUTCDATETIME(), 'STEAM_MARKET',
         SYSUTCDATETIME(), SYSUTCDATETIME());`,
    );

  return seed;
}

/** Idempotently ensure the admin User exists (T108 admin-cancel scenarios).
 *  Insert-if-absent rather than delete+recreate: admin cancel writes AuditLog
 *  rows referencing this id under a NO ACTION FK, so the row must persist across
 *  tests. The super_admin role on the JWT — not a DB role assignment — grants
 *  the CANCEL_TRANSACTIONS permission. */
export async function ensureAdmin(): Promise<void> {
  const p = await getPool();
  await p
    .request()
    .input('id', sql.UniqueIdentifier, seed.adminId)
    .input('steamId', sql.NVarChar(20), seed.adminSteamId)
    .input('name', sql.NVarChar(100), 'E2E Admin')
    .query(
      `IF NOT EXISTS (SELECT 1 FROM Users WHERE Id = @id)
         INSERT INTO Users (Id, SteamId, SteamDisplayName, PreferredLanguage,
           MobileAuthenticatorVerified, CompletedTransactionCount, IsDeactivated, IsSuspended, IsDeleted,
           CreatedAt, UpdatedAt)
         VALUES (@id, @steamId, @name, 'en', 1, 0, 0, 0, 0,
           DATEADD(DAY,-60,SYSUTCDATETIME()), SYSUTCDATETIME());`,
    );
}

/** Insert the buyer User (registered STEAM_ID party). */
async function insertBuyer(): Promise<void> {
  const p = await getPool();
  await p
    .request()
    .input('id', sql.UniqueIdentifier, seed.buyerId)
    .input('steamId', sql.NVarChar(20), seed.buyerSteamId)
    .input('name', sql.NVarChar(100), 'E2E Buyer')
    .query(
      `INSERT INTO Users (Id, SteamId, SteamDisplayName, PreferredLanguage,
         MobileAuthenticatorVerified, CompletedTransactionCount, IsDeactivated, IsSuspended, IsDeleted,
         CreatedAt, UpdatedAt)
       VALUES (@id, @steamId, @name, 'en', 1, 0, 0, 0, 0,
         DATEADD(DAY,-60,SYSUTCDATETIME()), SYSUTCDATETIME());`,
    );
}

/** Notification types produced for the seeded parties (WP19 assertion).
 *  Includes duplicates (no DISTINCT) so callers can assert per-party fan-out
 *  (e.g. TRANSACTION_COMPLETED is written for seller + buyer). */
export async function getNotificationTypes(): Promise<string[]> {
  const p = await getPool();
  const result = await p
    .request()
    .input('s', sql.UniqueIdentifier, seed.sellerId)
    .input('b', sql.UniqueIdentifier, seed.buyerId)
    .query('SELECT Type FROM Notifications WHERE UserId IN (@s,@b)');
  return result.recordset.map((row) => String(row.Type));
}

/** Poll notifications until every `expected` type is present (or timeout). The
 *  COMPLETED status flip (PayoutCompletedConsumer's own SaveChanges) can commit
 *  a few ms before the notification rows (deferred to the outbox unit-of-work),
 *  so a single read right after COMPLETED could race. Returns the last-seen set. */
export async function pollNotificationTypes(
  expected: string[],
  opts?: { timeoutMs?: number; intervalMs?: number },
): Promise<string[]> {
  const deadline = Date.now() + (opts?.timeoutMs ?? 30_000);
  const interval = opts?.intervalMs ?? 2_000;
  let types: string[] = [];
  while (Date.now() < deadline) {
    types = await getNotificationTypes();
    if (expected.every((t) => types.includes(t))) return types;
    await new Promise((res) => setTimeout(res, interval));
  }
  return types;
}

/** Poll until every `expected` recipient has a TRANSACTION_CANCELLED inbox row,
 *  then return the distinct recipient UserIds (lower-cased, within the seeded
 *  seller + buyer). Lets a test assert the exact fan-out: seller-cancel → buyer
 *  only (03 §2.5), buyer-cancel → seller only (03 §3.3), admin-cancel → both
 *  (03 §8.7). */
export async function pollCancelledNoticeRecipients(
  expected: string[],
  opts?: { timeoutMs?: number; intervalMs?: number },
): Promise<string[]> {
  const deadline = Date.now() + (opts?.timeoutMs ?? 30_000);
  const interval = opts?.intervalMs ?? 2_000;
  const want = expected.map((e) => e.toLowerCase());
  let recipients: string[] = [];
  while (Date.now() < deadline) {
    const p = await getPool();
    const result = await p
      .request()
      .input('s', sql.UniqueIdentifier, seed.sellerId)
      .input('b', sql.UniqueIdentifier, seed.buyerId)
      .query(
        `SELECT DISTINCT UserId FROM Notifications
         WHERE Type = 'TRANSACTION_CANCELLED' AND UserId IN (@s, @b)`,
      );
    recipients = result.recordset.map((row) => String(row.UserId).toLowerCase());
    if (want.every((e) => recipients.includes(e))) return recipients;
    await new Promise((res) => setTimeout(res, interval));
  }
  return recipients;
}

/** Poll until the RETURN_TO_SELLER refund offer for `transactionId` is ACCEPTED
 *  (the fake self-drives the seller's acceptance), proving the escrowed item was
 *  returned to the seller. */
export async function pollRefundOfferAccepted(
  transactionId: string,
  opts?: { timeoutMs?: number; intervalMs?: number },
): Promise<boolean> {
  const deadline = Date.now() + (opts?.timeoutMs ?? 120_000);
  const interval = opts?.intervalMs ?? 3_000;
  while (Date.now() < deadline) {
    const p = await getPool();
    const result = await p
      .request()
      .input('tx', sql.UniqueIdentifier, transactionId)
      .query(
        `SELECT Status FROM TradeOffers
         WHERE TransactionId = @tx AND Direction = 'RETURN_TO_SELLER'`,
      );
    if (result.recordset.some((row) => String(row.Status) === 'ACCEPTED')) return true;
    await new Promise((res) => setTimeout(res, interval));
  }
  return false;
}

/** Read the seeded bot's denormalized ActiveEscrowCount (06 §3.10). Returns -1
 *  when the bot row is missing. */
export async function getBotEscrowCount(): Promise<number> {
  const p = await getPool();
  const result = await p
    .request()
    .input('steamId', sql.NVarChar(20), e2eConfig.botSteamId)
    .query('SELECT ActiveEscrowCount FROM PlatformSteamBots WHERE SteamId = @steamId');
  return result.recordset.length ? Number(result.recordset[0].ActiveEscrowCount) : -1;
}

export interface BuyerRefundRow {
  status: string;
  amount: string;
  toAddress: string;
}

/** Poll until the BUYER_REFUND blockchain transfer for `transactionId` reaches
 *  CONFIRMED. The dispatch + confirmation Hangfire jobs run on a per-minute
 *  cadence, so the timeout is generous. Returns the row (last-seen on timeout),
 *  or null if no BUYER_REFUND row was ever queued. */
export async function pollBuyerRefundConfirmed(
  transactionId: string,
  opts?: { timeoutMs?: number; intervalMs?: number },
): Promise<BuyerRefundRow | null> {
  const deadline = Date.now() + (opts?.timeoutMs ?? 180_000);
  const interval = opts?.intervalMs ?? 3_000;
  let last: BuyerRefundRow | null = null;
  while (Date.now() < deadline) {
    const p = await getPool();
    const result = await p
      .request()
      .input('tx', sql.UniqueIdentifier, transactionId)
      .query(
        `SELECT Status, Amount, ToAddress FROM BlockchainTransactions
         WHERE TransactionId = @tx AND Type = 'BUYER_REFUND'`,
      );
    if (result.recordset.length) {
      const row = result.recordset[0];
      last = {
        status: String(row.Status),
        amount: String(row.Amount),
        toAddress: String(row.ToAddress),
      };
      if (last.status === 'CONFIRMED') return last;
    }
    await new Promise((res) => setTimeout(res, interval));
  }
  return last;
}

export async function closePool(): Promise<void> {
  if (pool) {
    await pool.close();
    pool = null;
  }
}
