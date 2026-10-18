/**
 * The network this service watches and signs on (T148 — 05 §3.3a "Servisler").
 *
 * Every network has its own blockchain service — its own container, seed,
 * internal key and webhook secret (11 §F8 K7). This codebase is the TRON
 * service; the Ethereum service is a separate one (T150). The value is a
 * constant, not configuration: a TRON service that could be told it is
 * Ethereum would sign TRON transfers for requests meant for another chain.
 *
 * Both directions of the contract carry it:
 *  - every webhook envelope names it (`network`), so the backend can reject a
 *    callback whose network does not match the referenced deposit;
 *  - every request body may name it, and a request naming another network is
 *    refused (`NETWORK_MISMATCH`) instead of acted on — the guard against a
 *    backend whose Ethereum service address points here by mistake.
 */
export const SERVICE_NETWORK = 'TRON' as const;

/** Network names on the wire — the backend's `BlockchainNetwork` enum (06 §2.25). */
export type BlockchainNetworkName = 'TRON' | 'ETHEREUM';
