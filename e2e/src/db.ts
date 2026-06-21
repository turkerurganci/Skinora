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
  // Must match the fake's inventory item (assetId + marketHashName).
  itemAssetId: '11111111001',
  itemMarketHashName: 'AK-47 | Redline (Field-Tested)',
  price: '100.00',
};

/** Seed seller, buyer, ACTIVE bot, and a matching price-cache row (0% deviation
 *  → CREATED, not FLAGGED). Idempotent: clears prior e2e rows first. */
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

  // Buyer — exists + not suspended; refund address supplied per-transaction at accept.
  await r()
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

/** Notification types produced for the seeded parties (WP19 assertion). */
export async function getNotificationTypes(): Promise<string[]> {
  const p = await getPool();
  const result = await p
    .request()
    .input('s', sql.UniqueIdentifier, seed.sellerId)
    .input('b', sql.UniqueIdentifier, seed.buyerId)
    .query('SELECT Type FROM Notifications WHERE UserId IN (@s,@b)');
  return result.recordset.map((row) => String(row.Type));
}

export async function closePool(): Promise<void> {
  if (pool) {
    await pool.close();
    pool = null;
  }
}
