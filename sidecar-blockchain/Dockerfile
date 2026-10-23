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

# T172b — the deposit refund ledger (24-hour cap). Compose mounts the
# skinora-tron-deposit-refunds volume here; without a writable ledger every
# deposit refund waits (the rest of the service runs).
RUN mkdir -p /var/lib/skinora-tron/deposit-refunds

EXPOSE 5200
CMD ["node", "dist/index.js"]
