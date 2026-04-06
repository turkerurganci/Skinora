// Placeholder — will be replaced by Blockchain sidecar in T15.
// T08: structured logging via Pino → Loki, correlationId per request.
const http = require("http");
const { logger, loggerForRequest } = require("./logger");

const port = parseInt(process.env.PORT || "5200", 10);

const server = http.createServer((req, res) => {
  const { logger: reqLogger, correlationId } = loggerForRequest(req);
  res.setHeader("X-Correlation-Id", correlationId);

  reqLogger.info({ method: req.method, url: req.url }, "Incoming request");

  if (req.url === "/health") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(
      JSON.stringify({ status: "ok", service: "skinora-blockchain-sidecar" }),
    );
    return;
  }
  res.writeHead(200, { "Content-Type": "text/plain" });
  res.end("Skinora Blockchain Sidecar — placeholder (T15)");
});

server.listen(port, "0.0.0.0", () => {
  logger.info({ port }, "Blockchain sidecar placeholder listening");
});
