FROM node:20-alpine AS builder

WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY tsconfig.json ./
COPY src/ ./src/
RUN npm run build

FROM node:20-alpine

WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev --no-audit --no-fund
COPY --from=builder /app/dist ./dist

# The seed and the signing keys arrive as Docker secrets under /run/secrets.
# Plain compose mounts them as bind mounts with the host file's owner and mode,
# so the process user must be able to read what the operator created there —
# the image runs as root like the other sidecars (DEPLOY_RUNBOOK §B).

# T152 — the durable journal of signed transactions (08 §3.7.6). Compose mounts
# the ethereum_tx_journal volume here; the service refuses to start if it
# cannot write it.
RUN mkdir -p /var/lib/skinora-ethereum/tx-journal

EXPOSE 5300
CMD ["node", "dist/index.js"]
