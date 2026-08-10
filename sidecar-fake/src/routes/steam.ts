import { Router } from 'express';
import { config } from '../config.js';
import { logger } from '../logger.js';
import { postWebhook } from '../webhookClient.js';
import { fakeOfferId, fakeAssetId } from '../ids.js';
import { isAcceptSuppressed } from '../tradeControl.js';

export const steamRouter = Router();

// Deterministic seller inventory. The happy-path test picks an item from here
// to create the transaction; both are tradable + marketable so the create
// flow's eligibility checks pass.
const INVENTORY_ITEMS = [
  {
    assetId: '11111111001',
    classId: '310776767',
    instanceId: '302028390',
    name: 'AK-47 | Redline (Field-Tested)',
    marketHashName: 'AK-47 | Redline (Field-Tested)',
    type: 'Rifle',
    exterior: 'Field-Tested',
    iconUrl: '',
    tradable: true,
    marketable: true,
  },
  {
    assetId: '11111111002',
    classId: '310777458',
    instanceId: '302028390',
    name: 'AWP | Asiimov (Field-Tested)',
    marketHashName: 'AWP | Asiimov (Field-Tested)',
    type: 'Sniper Rifle',
    exterior: 'Field-Tested',
    iconUrl: '',
    tradable: true,
    marketable: true,
  },
];

steamRouter.get('/api/inventory/:steamId', (_req, res) => {
  res.json({
    items: INVENTORY_ITEMS,
    totalCount: INVENTORY_ITEMS.length,
    tradeableCount: INVENTORY_ITEMS.filter((i) => i.tradable).length,
  });
});

steamRouter.delete('/api/inventory/:steamId/cache', (_req, res) => {
  res.status(204).end();
});

steamRouter.get('/api/trade-hold/:steamId', (_req, res) => {
  // MA-verified account, no Steam escrow hold → trades settle instantly.
  //
  // `active` is the MOBILE AUTHENTICATOR flag, not the hold flag: the backend
  // maps active=true → SteamTradeHoldProbeResult.Active ("MA on, hold is 0
  // seconds"). This used to answer `false`, which contradicted the comment
  // above it and meant "MA off". Nothing observed it until T119a made the
  // accept endpoint probe live — with the old value every accept would have
  // failed 403 MOBILE_AUTHENTICATOR_REQUIRED across all e2e suites.
  res.json({ active: true, escrowEndDurationSeconds: 0 });
});

interface SendTradeOfferBody {
  transactionId?: string;
  direction?: string;
  partnerSteamId?: string;
  botAccountName?: string;
}

steamRouter.post('/api/trade-offers/send', (req, res) => {
  const body = (req.body ?? {}) as SendTradeOfferBody;
  const transactionId = body.transactionId ?? '';
  const direction = body.direction ?? 'SELLER_TO_BOT';
  const partnerSteamId = body.partnerSteamId ?? '';
  const botAccountName = body.botAccountName ?? 'E2E-Bot';
  const offerId = fakeOfferId(`${transactionId}:${direction}`);
  const correlationId = req.correlationId ?? offerId;

  res.json({ status: 'sent', offerId, attempts: 1 });

  // Self-drive the trade. After the dispatch job commits TRADE_OFFER_SENT_TO_*,
  // emit two webhooks in order:
  //   1. trade_offer.sent — HandleSentAsync persists the TradeOffer row (bot
  //      resolved by DisplayName == botAccountName, keyed by offerId). REQUIRED
  //      first: HandleAcceptedAsync looks the row up by offerId.
  //   2. trade_offer.accepted — advances the state machine (escrow leg →
  //      ITEM_ESCROWED, delivery leg → ITEM_DELIVERED). receivedAssetId /
  //      deliveredAssetId populate EscrowBotAssetId / DeliveredBuyerAssetId.
  // The inbound webhook's direction must be the SAME token the dispatch sent
  // (SELLER_TO_BOT / BOT_TO_BUYER / BOT_TO_SELLER_REFUND) — the backend's
  // ParseDirection maps those to TO_SELLER / TO_BUYER / RETURN_TO_SELLER.
  const base = {
    transactionId,
    direction,
    partnerSteamId,
    botSteamId: config.botSteamId,
    botAccountName,
    offerId,
  };

  const driveTrade = async (): Promise<void> => {
    await postWebhook(
      '/api/v1/webhooks/steam/trade-events',
      config.steamWebhookSecret,
      {
        event: 'trade_offer.sent',
        timestamp: new Date().toISOString(),
        data: { ...base, status: 'sent', attempts: 1 },
      },
      correlationId,
    );
    // T109 — when this direction's accept leg is suppressed, stop after `sent`.
    // The offer row is persisted but never accepted, so the transaction parks
    // in TRADE_OFFER_SENT_TO_* until the backend deadline scanner times it out.
    if (isAcceptSuppressed(direction)) {
      logger.info({ transactionId, direction }, 'trade accept suppressed (T109) — holding at SENT');
      return;
    }
    await postWebhook(
      '/api/v1/webhooks/steam/trade-events',
      config.steamWebhookSecret,
      {
        event: 'trade_offer.accepted',
        timestamp: new Date().toISOString(),
        data: {
          ...base,
          status: 'accepted',
          receivedAssetId: fakeAssetId(`${transactionId}:recv`),
          deliveredAssetId: fakeAssetId(`${transactionId}:deliv`),
        },
      },
      correlationId,
    );
  };

  setTimeout(() => {
    driveTrade().catch((err: unknown) =>
      logger.error(
        { err: String(err), transactionId, direction },
        'trade self-drive (sent→accepted) failed',
      ),
    );
  }, config.tradeAcceptDelayMs);
});
