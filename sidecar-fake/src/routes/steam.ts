import { Router } from 'express';
import { config } from '../config.js';
import { logger } from '../logger.js';
import { postWebhook } from '../webhookClient.js';
import { fakeOfferId, fakeAssetId } from '../ids.js';

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
  // MA-verified seller, no Steam escrow hold → trades settle instantly.
  res.json({ active: false, escrowEndDurationSeconds: 0 });
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

  // Self-drive acceptance: after the dispatch job commits TRADE_OFFER_SENT_TO_*,
  // emit trade_offer.accepted so the backend advances to ITEM_ESCROWED (escrow
  // leg) or ITEM_DELIVERED (delivery leg). The asset ids populate
  // EscrowBotAssetId / DeliveredBuyerAssetId required by the accept handlers.
  const webhookDirection = direction === 'SELLER_TO_BOT' ? 'escrow' : 'delivery';
  setTimeout(() => {
    const envelope = {
      event: 'trade_offer.accepted',
      timestamp: new Date().toISOString(),
      data: {
        transactionId,
        direction: webhookDirection,
        partnerSteamId,
        botSteamId: config.botSteamId,
        botAccountName,
        offerId,
        status: 'accepted',
        receivedAssetId: fakeAssetId(`${transactionId}:recv`),
        deliveredAssetId: fakeAssetId(`${transactionId}:deliv`),
      },
    };
    postWebhook(
      '/api/v1/webhooks/steam/trade-events',
      config.steamWebhookSecret,
      envelope,
      correlationId,
    ).catch((err: unknown) =>
      logger.error(
        { err: String(err), transactionId, direction: webhookDirection },
        'trade_offer.accepted webhook failed',
      ),
    );
  }, config.tradeAcceptDelayMs);
});
