# E2E-only fake sidecar (T107). Mirrors the sidecar-steam multi-stage build.
# Serves the backend's outbound Steam + blockchain HTTP surface on BOTH ports
# (5100 + 5200) and drives the escrow happy path via signed inbound webhooks.
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

# 5100 = Steam sidecar surface, 5200 = blockchain sidecar surface.
EXPOSE 5100 5200
CMD ["node", "dist/index.js"]
