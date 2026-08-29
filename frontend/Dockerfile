# Stage 1: Dependencies
FROM node:20-slim AS deps
WORKDIR /app
COPY package.json ./
RUN npm install

# Stage 2: Build
FROM node:20-slim AS builder
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .

# NEXT_PUBLIC_* values are inlined into the client bundle at build time, so a
# runtime environment variable on the container never reaches the browser. The
# explorer host has to be a build arg or every non-mainnet deployment links
# transaction hashes to tronscan.org while the chain is a testnet. Empty falls
# back to the mainnet default in src/lib/utils/blockchain.ts.
ARG NEXT_PUBLIC_TRONSCAN_TX_BASE_URL
ENV NEXT_PUBLIC_TRONSCAN_TX_BASE_URL=${NEXT_PUBLIC_TRONSCAN_TX_BASE_URL}

RUN npm run build

# Stage 3: Production
FROM node:20-slim AS runner
WORKDIR /app

ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1

RUN addgroup --system --gid 1001 nodejs
RUN adduser --system --uid 1001 nextjs

COPY --from=builder /app/public ./public
COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static

USER nextjs

EXPOSE 3000
ENV PORT=3000
ENV HOSTNAME="0.0.0.0"

CMD ["node", "server.js"]
