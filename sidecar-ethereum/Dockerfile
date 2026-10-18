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

EXPOSE 5300
CMD ["node", "dist/index.js"]
