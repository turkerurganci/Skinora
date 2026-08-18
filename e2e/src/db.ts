import sql from 'mssql';
import { setFakeInventory } from './api';
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
  // T119a — POST /accept requires a Steam trade URL whose `partner` resolves to
  // the accepting buyer's own SteamID64 (partner = SteamID64 - 76561197960265728).
  // 76561198000000061 - 76561197960265728 = 39734333.
  buyerTradeUrl: 'https://steamcommunity.com/tradeoffer/new/?partner=39734333&token=E2ETOKEN',
  priceCacheId: '44444444-4444-4444-4444-444444444444',
  // Admin actor for the admin-cancel scenarios (T108). AuditLog.ActorId is an
  // FK to Users, so the admin must exist as a row; the CANCEL_TRANSACTIONS
  // permission itself comes from the super_admin role claim on the JWT.
  adminId: '55555555-5555-5555-5555-555555555555',
  adminSteamId: '76561198000000099',
  // Fixed id for the account-level FraudFlag the T111 fund-flow-block scenario
  // inserts for the seller (so the test can reject it by id to unblock).
  accountFlagId: '66666666-6666-6666-6666-666666666666',
  // The listed item. These two constants DRIVE the seed rather than mirror a
  // fixture: seedHappyPath() writes them into the seller's fake inventory and
  // the ItemPriceCaches row below is keyed by the same market hash name. T137
  // retired the fake's constant inventory — an undriven steamId now reads
  // EMPTY, so there is no default item left to 'match'; the fake's
  // AK47_REDLINE catalog template supplies the remaining fields (class,
  // instance, type, exterior, tradable/marketable).
  itemAssetId: '11111111001',
  itemMarketHashName: 'AK-47 | Redline (Field-Tested)',
  price: '100.00',
};

// The buyer's on-chain wallet the fake sidecar pays FROM = fakeTronAddress(999_001)
// (sidecar-fake/src/routes/control.ts BUYER_WALLET). Every payment-edge-case
// refund returns to the payment *source* address (08 §562), so the refund row's
// ToAddress equals this — distinct from seed.buyerRefundAddress (the trade-side
// refund wallet used only by the item-timeout BUYER_REFUND).
export const fakeBuyerWallet = 'TGDcTRVZVvKBUE7h5fRCVUjRGj6K52AFWg';

/** Seed seller, buyer, and a matching price-cache row (0% deviation → CREATED,
 *  not FLAGGED). Idempotent: clears prior e2e rows first.
 *
 *  T137a — no bot row is seeded any more. T117's P2P pivot dropped
 *  PlatformSteamBots, TradeOffers and BotRecoveryItems (migration
 *  20260809162642_T117_P2P_Pivot), so the platform never holds the item and
 *  there is no escrow slot to reserve: the seller sends the item directly to
 *  the buyer (02 §2.1).
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
  //
  // Drain un-PROCESSED OutboxMessages FIRST. The previous test's transactions are
  // about to be deleted below; any still-PENDING/FAILED outbox row that, on
  // dispatch, inserts a Notification referencing one of those (now-gone)
  // transactions trips FK_Notifications_Transactions_TransactionId. Because the
  // dispatcher batches rows into one SaveChanges, that single FK failure rolls
  // back the whole batch and the row is retried forever — permanently blocking
  // every later notification (e.g. a subsequent suite's EMERGENCY_HOLD_APPLIED)
  // from committing. Deleting Notifications without draining the outbox that
  // re-creates them left this gap; clearing both closes it (the e2e backend has
  // no other producer, so a global non-PROCESSED purge is safe between tests).
  //
  // T137a — two corrections to this batch:
  //
  // 1. Table set. Every child with a NO-ACTION TransactionId FK must be purged
  //    before Transactions. TradeOffers and PlatformSteamBots left the model
  //    (T117 dropped them); DeliveryEvidenceCaptures (T125), Disputes and
  //    SellerPayoutIssues joined it after this batch was written.
  // 2. Silence. SQL Server resolves object names for an ad-hoc batch at COMPILE
  //    time, so ONE unknown table turns the whole purge into a no-op — no
  //    statement runs at all. That is how the retired-table references survived
  //    four tasks: the cleanup silently stopped deleting anything while
  //    `.catch(() => undefined)` kept the harness quiet, and the only visible
  //    symptom was a duplicate-key error two tests later. The failure is logged
  //    now; it stays non-fatal (a fresh DB has nothing to clean).
  await r()
    .input('s', sql.UniqueIdentifier, seed.sellerId)
    .input('b', sql.UniqueIdentifier, seed.buyerId)
    .input('item', sql.NVarChar(200), seed.itemMarketHashName)
    .batch(
      `DELETE FROM OutboxMessages WHERE Status <> 'PROCESSED';
       DELETE FROM Notifications WHERE UserId IN (@s,@b);
       DELETE FROM FraudFlags WHERE UserId IN (@s,@b);
       DELETE FROM AuditLogs WHERE UserId IN (@s,@b) OR ActorId IN (@s,@b);
       DELETE evc FROM DeliveryEvidenceCaptures evc JOIN Transactions t ON evc.TransactionId=t.Id WHERE t.SellerId=@s;
       DELETE d FROM Disputes d JOIN Transactions t ON d.TransactionId=t.Id WHERE t.SellerId=@s;
       DELETE spi FROM SellerPayoutIssues spi JOIN Transactions t ON spi.TransactionId=t.Id WHERE t.SellerId=@s;
       DELETE bt FROM BlockchainTransactions bt JOIN Transactions t ON bt.TransactionId=t.Id WHERE t.SellerId=@s;
       DELETE pa FROM PaymentAddresses pa JOIN Transactions t ON pa.TransactionId=t.Id WHERE t.SellerId=@s;
       DELETE FROM TransactionHistory WHERE TransactionId IN (SELECT Id FROM Transactions WHERE SellerId=@s);
       DELETE FROM Transactions WHERE SellerId=@s;
       DELETE FROM AdminUserRoles WHERE UserId IN (@s,@b);
       DELETE FROM Users WHERE Id IN (@s,@b);
       DELETE FROM ItemPriceCaches WHERE MarketHashName=@item;`,
    )
    .catch((err: unknown) => {
      console.warn(`[e2e:db] seed cleanup batch failed — stale rows may remain: ${String(err)}`);
    });

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

  // Fake Steam side — the SELLER holds the listed item (T137 fix round, B1).
  // T137 made an undriven steamId read PUBLIC + EMPTY, so create's Stage 5
  // seller-inventory check rejected every scenario with ITEM_NOT_IN_INVENTORY:
  // no spec or harness ever drove the fake. Seeding it here — the one function
  // all nine specs call, and which runs AFTER their beforeEach
  // resetFakeSteamState() — restores create suite-wide without touching a
  // single scenario (the P2P rewrite stays T138's scope).
  //
  // ONLY the seller is seeded. The buyer's ZERO baseline is what the delivery
  // check counts its class delta against (06 §3.5) and is the whole point of
  // the empty default — handing the buyer a copy would destroy it.
  //
  // Loud on failure: a silent no-op here is exactly the T137a failure mode (a
  // setup step that quietly stopped working and surfaced two tests later).
  const inventory = await setFakeInventory(seed.sellerSteamId, {
    items: [
      {
        catalog: 'AK47_REDLINE',
        assetId: seed.itemAssetId,
        name: seed.itemMarketHashName,
        marketHashName: seed.itemMarketHashName,
      },
    ],
  });
  if (!inventory.ok) {
    throw new Error(
      `[e2e:db] seeding the seller's fake inventory failed (${inventory.status}): ` +
        JSON.stringify(inventory.body),
    );
  }

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

// T137a — two custody-era helpers were removed here, and they have NO P2P
// successor (T138 must not look for one):
//
//   pollRefundOfferAccepted(txId)  read TradeOffers WHERE Direction =
//     'RETURN_TO_SELLER'. It proved the escrowed item went back to the seller
//     after a cancel. In P2P the item never leaves the seller before
//     PAYMENT_RECEIVED, so a cancel has no return leg to observe — the
//     `itemReturned` field of the cancel response is the whole story.
//
//   getBotEscrowCount()           read PlatformSteamBots.ActiveEscrowCount
//     (06 §3.10) to prove the platform's escrow slot was taken/released. There
//     is no platform inventory in P2P; the equivalent question ("is the item
//     still with the seller?") is answered by the Steam inventory read the
//     delivery-verification round already performs.

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

/** The exact amount the buyer is expected to pay into the deposit address
 *  (PaymentAddress.ExpectedAmount) — the listing price plus the buyer-side
 *  commission (≈102 for a 100 listing; 02 §4.6). Used by the §5.2 overpayment
 *  test so the asserted excess (received − expected) does not hard-code the fee.
 *  Returns -1 when no PaymentAddress exists yet. */
export async function getExpectedAmount(transactionId: string): Promise<number> {
  const p = await getPool();
  const result = await p
    .request()
    .input('tx', sql.UniqueIdentifier, transactionId)
    .query(
      `SELECT TOP 1 ExpectedAmount FROM PaymentAddresses
       WHERE TransactionId = @tx AND IsDeleted = 0
       ORDER BY CreatedAt DESC`,
    );
  return result.recordset.length ? Number(result.recordset[0].ExpectedAmount) : -1;
}

/** Allow-list of BlockchainTransaction.Type values an e2e test polls for. The
 *  value is a bound parameter (never interpolated), but the union keeps callers
 *  honest about which rows the harness expects. */
export type BlockchainTxType =
  | 'BUYER_PAYMENT'
  | 'BUYER_REFUND'
  | 'EXCESS_REFUND'
  | 'INCORRECT_AMOUNT_REFUND'
  | 'WRONG_TOKEN_REFUND'
  | 'LATE_PAYMENT_REFUND'
  | 'SPAM_TOKEN_INCOMING';

/** Poll until a BlockchainTransaction of `type` for `transactionId` reaches
 *  CONFIRMED, then return its row. Generalises pollBuyerRefundConfirmed for the
 *  T110 payment-edge-case refund rows (INCORRECT_AMOUNT_REFUND / EXCESS_REFUND /
 *  WRONG_TOKEN_REFUND / LATE_PAYMENT_REFUND) and the §5.3a SPAM_TOKEN_INCOMING
 *  audit row (which the backend writes directly at terminal CONFIRMED). The fake
 *  blockchain sidecar auto-confirms outgoing transfers, so refund rows reach
 *  CONFIRMED on the per-minute dispatch + confirmation cadence with no extra
 *  control call. Returns the last-seen row (or null if none was ever written). */
export async function pollBlockchainTxConfirmed(
  transactionId: string,
  type: BlockchainTxType,
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
      .input('type', sql.NVarChar(40), type)
      .query(
        `SELECT TOP 1 Status, Amount, ToAddress FROM BlockchainTransactions
         WHERE TransactionId = @tx AND Type = @type
         ORDER BY CreatedAt DESC`,
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

/** Poll until every `expected` recipient has an inbox row of `type`, then
 *  return the distinct recipient UserIds (lower-cased, within the seeded seller
 *  + buyer). Generalises pollCancelledNoticeRecipients for the T110
 *  payment-edge-case notifications (INSUFFICIENT_PAYMENT / OVERPAYMENT_REFUNDED /
 *  WRONG_TOKEN_REFUND / LATE_PAYMENT_REFUNDED — all buyer-targeted). */
export async function pollNotificationRecipients(
  type: string,
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
      .input('type', sql.NVarChar(64), type)
      .query(
        `SELECT DISTINCT UserId FROM Notifications
         WHERE Type = @type AND UserId IN (@s, @b)`,
      );
    recipients = result.recordset.map((row) => String(row.UserId).toLowerCase());
    if (want.every((e) => recipients.includes(e))) return recipients;
    await new Promise((res) => setTimeout(res, interval));
  }
  return recipients;
}

/** Phase-deadline columns on the Transactions table (06 §3.5). Fixed allow-list
 *  — the value is interpolated into SQL, so it must never come from free input.
 *
 *  T137a: the two custody-era names were renamed by T117's phase-preserving
 *  rename (migration 20260809162642_T117_P2P_Pivot):
 *    TradeOfferToSellerDeadline → SellerConfirmDeadline
 *    TradeOfferToBuyerDeadline  → DeliveryDeadline
 *  The allow-list carried the dead names, so any backdate/set call using them
 *  would have hit "Invalid column name" — the next wall behind the dropped
 *  tables. Call sites were swapped along the migration's own mapping; whether a
 *  P2P flow should still lean on that phase at all is T138's question. */
export type DeadlineColumn =
  | 'AcceptDeadline'
  | 'SellerConfirmDeadline'
  | 'PaymentDeadline'
  | 'DeliveryDeadline';

const DEADLINE_COLUMNS: ReadonlySet<DeadlineColumn> = new Set([
  'AcceptDeadline',
  'SellerConfirmDeadline',
  'PaymentDeadline',
  'DeliveryDeadline',
]);

/** Push a phase deadline into the past so the DeadlineScannerJob (05 §4.4) fires
 *  Timeout on its next sweep. This is the e2e lever for 03 §4 — all e2e timeouts
 *  are 60 minutes, so a real-clock wait is impossible; backdating the column and
 *  letting the production scanner do the rest keeps the timeout path itself
 *  unmocked. `column` is validated against the allow-list before interpolation;
 *  the offset is a bound int parameter. */
export async function backdateDeadline(
  transactionId: string,
  column: DeadlineColumn,
  minutesInPast = 5,
): Promise<void> {
  if (!DEADLINE_COLUMNS.has(column)) {
    throw new Error(`backdateDeadline: unknown deadline column ${column}`);
  }
  const p = await getPool();
  await p
    .request()
    .input('tx', sql.UniqueIdentifier, transactionId)
    .input('mins', sql.Int, minutesInPast)
    .query(
      `UPDATE Transactions
       SET ${column} = DATEADD(MINUTE, -@mins, SYSUTCDATETIME())
       WHERE Id = @tx`,
    );
}

/** Set a phase deadline to a fixed point in the future — the forward mirror of
 *  backdateDeadline. Used to give a parked TRADE_OFFER_SENT_TO_SELLER transaction
 *  a live seller-trade window before a STEAM_OUTAGE freeze: the e2e fast-path
 *  leaves SellerConfirmDeadline null (the fake's trade leg never goes through
 *  the production deadline stamp), whereas a real outage freezes transactions
 *  whose deadline is live — exactly the state the WP7 integration test seeds at
 *  +12h. Without it the freeze would capture a zero remainder. `column` is
 *  validated against the allow-list before interpolation; the offset is a bound
 *  int parameter. */
export async function setDeadlineFromNow(
  transactionId: string,
  column: DeadlineColumn,
  minutesFromNow: number,
): Promise<void> {
  if (!DEADLINE_COLUMNS.has(column)) {
    throw new Error(`setDeadlineFromNow: unknown deadline column ${column}`);
  }
  const p = await getPool();
  await p
    .request()
    .input('tx', sql.UniqueIdentifier, transactionId)
    .input('mins', sql.Int, minutesFromNow)
    .query(
      `UPDATE Transactions
       SET ${column} = DATEADD(MINUTE, @mins, SYSUTCDATETIME())
       WHERE Id = @tx`,
    );
}

export interface MonitoringRow {
  status: string;
  expiresAt: Date | null;
}

/** Poll the deposit PaymentAddress until its MonitoringStatus enters a
 *  POST_CANCEL_* window — the observable proof that late-payment monitoring was
 *  (re)started after a payment timeout (03 §4.3 step 4, 08 §3.4). The starter
 *  stamps POST_CANCEL_24H + MonitoringExpiresAt = cancelledAt + 24h. Returns the
 *  last-seen row (or null when no PaymentAddress exists). */
export async function pollPostCancelMonitoring(
  transactionId: string,
  opts?: { timeoutMs?: number; intervalMs?: number },
): Promise<MonitoringRow | null> {
  const deadline = Date.now() + (opts?.timeoutMs ?? 60_000);
  const interval = opts?.intervalMs ?? 2_000;
  let last: MonitoringRow | null = null;
  while (Date.now() < deadline) {
    const p = await getPool();
    const result = await p
      .request()
      .input('tx', sql.UniqueIdentifier, transactionId)
      .query(
        `SELECT TOP 1 MonitoringStatus, MonitoringExpiresAt FROM PaymentAddresses
         WHERE TransactionId = @tx AND IsDeleted = 0`,
      );
    if (result.recordset.length) {
      const row = result.recordset[0];
      last = {
        status: String(row.MonitoringStatus),
        expiresAt: row.MonitoringExpiresAt ? new Date(row.MonitoringExpiresAt) : null,
      };
      if (last.status.startsWith('POST_CANCEL')) return last;
    }
    await new Promise((res) => setTimeout(res, interval));
  }
  return last;
}

export interface FraudFlagRow {
  id: string;
  scope: string;
  type: string;
  status: string;
}

/** Read the most recent FraudFlag attached to `transactionId` (06 §3.12) — the
 *  pre-create flag the fraud engine stages when a price-deviation / high-volume
 *  rule trips during create (03 §7.1–§7.2). Returns null when none exists, so a
 *  test can assert both "flagged" and "not flagged". */
export async function getFlagForTransaction(transactionId: string): Promise<FraudFlagRow | null> {
  const p = await getPool();
  const result = await p
    .request()
    .input('tx', sql.UniqueIdentifier, transactionId)
    .query(
      `SELECT TOP 1 Id, Scope, Type, Status FROM FraudFlags
       WHERE TransactionId = @tx
       ORDER BY CreatedAt DESC`,
    );
  if (!result.recordset.length) return null;
  const row = result.recordset[0];
  return {
    id: String(row.Id),
    scope: String(row.Scope),
    type: String(row.Type),
    status: String(row.Status),
  };
}

/** Insert an ACCOUNT_LEVEL FraudFlag (TransactionId NULL, PENDING) for `userId`
 *  — the e2e lever for 03 §7.3/§7.4 "hesap flag'i". An active account flag
 *  (Status != REJECTED) makes the user fail the transaction-creation eligibility
 *  gate (AccountFlagChecker → ACCOUNT_FLAGGED), i.e. the fund-flow block. Uses the
 *  fixed seed.accountFlagId so the test can reject it by id to unblock. Enum
 *  columns are stored as their string names (06 §4 convention; FraudFlag CHECK
 *  constraints compare against 'ACCOUNT_LEVEL'/'PENDING'). RowVersion is a
 *  SQL Server rowversion — omitted so the server generates it. */
export async function insertAccountFlag(
  userId: string,
  opts?: { flagId?: string; type?: string },
): Promise<string> {
  const flagId = opts?.flagId ?? seed.accountFlagId;
  const p = await getPool();
  await p
    .request()
    .input('id', sql.UniqueIdentifier, flagId)
    .input('uid', sql.UniqueIdentifier, userId)
    .input('type', sql.NVarChar(40), opts?.type ?? 'ABNORMAL_BEHAVIOR')
    .input(
      'details',
      sql.NVarChar(sql.MAX),
      '{"pattern":"E2E_ACCOUNT_FLAG","description":"T111 fund-flow block"}',
    )
    .query(
      `INSERT INTO FraudFlags (Id, UserId, TransactionId, ReviewedByAdminId, Scope, Type,
         Status, Details, AdminNote, ReviewedAt, IsDeleted, DeletedAt, CreatedAt, UpdatedAt)
       VALUES (@id, @uid, NULL, NULL, 'ACCOUNT_LEVEL', @type,
         'PENDING', @details, NULL, NULL, 0, NULL, SYSUTCDATETIME(), SYSUTCDATETIME());`,
    );
  return flagId;
}

/** Admin-tunable SystemSetting keys (06 §3.17) an e2e test may rewrite to drive
 *  a fraud rule deterministically. The value is a bound parameter; the union
 *  keeps callers honest about which knobs the harness touches. */
export type FraudSettingKey =
  | 'high_volume_amount_threshold'
  | 'high_volume_count_threshold'
  | 'price_deviation_threshold';

/** Read a SystemSetting value by key ([Key] is a reserved word → bracketed).
 *  Returns null when the row is unconfigured/absent — lets a test capture the
 *  e2e default before overriding it, then restore the exact prior value. */
export async function getSystemSetting(key: FraudSettingKey): Promise<string | null> {
  const p = await getPool();
  const result = await p
    .request()
    .input('k', sql.NVarChar(100), key)
    .query(`SELECT Value FROM SystemSettings WHERE [Key] = @k`);
  if (!result.recordset.length) return null;
  const v = result.recordset[0].Value;
  return v === null || v === undefined ? null : String(v);
}

/** Set a SystemSetting value by key (IsConfigured=1). The fraud pre-check reads
 *  SystemSetting rows directly (no cache), so the change is visible to the very
 *  next create. Used by the high-volume scenario to drop the threshold low
 *  enough that a single prior transaction trips the rule; the test restores the
 *  prior value in a finally block. */
export async function setSystemSetting(key: FraudSettingKey, value: string): Promise<void> {
  const p = await getPool();
  await p
    .request()
    .input('k', sql.NVarChar(100), key)
    .input('v', sql.NVarChar(200), value)
    .query(
      `UPDATE SystemSettings
       SET Value = @v, IsConfigured = 1, UpdatedAt = SYSUTCDATETIME()
       WHERE [Key] = @k`,
    );
}

export interface HoldStateRow {
  status: string;
  isOnHold: boolean;
  timeoutFreezeReason: string | null;
  timeoutFrozenAt: Date | null;
  timeoutRemainingSeconds: number | null;
  previousStatusBeforeHold: number | null;
}

/** Read the emergency-hold + timeout-freeze columns of a transaction (06 §3.5).
 *  Backs the T112 assertions that an apply-hold stamps IsOnHold +
 *  TimeoutFreezeReason=EMERGENCY_HOLD + the freeze trio (TimeoutFrozenAt +
 *  TimeoutRemainingSeconds), and that a release clears them (or, for a rejected
 *  ITEM_DELIVERED cancel, that the hold survives). Status + TimeoutFreezeReason
 *  are stored as their string names (06 §4; the CK_Transactions_* constraints
 *  compare against 'EMERGENCY_HOLD'). Returns null when the row is missing. */
export async function getTransactionHoldState(transactionId: string): Promise<HoldStateRow | null> {
  const p = await getPool();
  const result = await p
    .request()
    .input('tx', sql.UniqueIdentifier, transactionId)
    .query(
      `SELECT TOP 1 Status, IsOnHold, TimeoutFreezeReason, TimeoutFrozenAt,
              TimeoutRemainingSeconds, PreviousStatusBeforeHold
       FROM Transactions WHERE Id = @tx`,
    );
  if (!result.recordset.length) return null;
  const row = result.recordset[0];
  return {
    status: String(row.Status),
    isOnHold: Boolean(row.IsOnHold),
    timeoutFreezeReason: row.TimeoutFreezeReason === null ? null : String(row.TimeoutFreezeReason),
    timeoutFrozenAt: row.TimeoutFrozenAt ? new Date(row.TimeoutFrozenAt) : null,
    timeoutRemainingSeconds:
      row.TimeoutRemainingSeconds === null ? null : Number(row.TimeoutRemainingSeconds),
    previousStatusBeforeHold:
      row.PreviousStatusBeforeHold === null ? null : Number(row.PreviousStatusBeforeHold),
  };
}

export async function closePool(): Promise<void> {
  if (pool) {
    await pool.close();
    pool = null;
  }
}
