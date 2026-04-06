FROM node:20-alpine

WORKDIR /app
COPY package.json ./
RUN npm install --omit=dev --no-audit --no-fund
COPY logger.js ./
COPY server.js ./

EXPOSE 5200
CMD ["node", "server.js"]
