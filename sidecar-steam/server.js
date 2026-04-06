// Placeholder — will be replaced by Steam sidecar in T14.
// T08: structured logging via Pino → Loki, correlationId per request.
const http = require("http");
const { logger, loggerForRequest } = require("./logger");

const port = parseInt(process.env.PORT || "5100", 10);

const server = http.createServer((req, res) => {
  const { logger: reqLogger, correlationId } = loggerForRequest(req);
  res.setHeader("X-Correlation-Id", correlationId);

  reqLogger.info({ method: req.method, url: req.url }, "Incoming request");

  if (req.url === "/health") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ status: "ok", service: "skinora-steam-sidecar" }));
    return;
  }
  res.writeHead(200, { "Content-Type": "text/plain" });
  res.end("Skinora Steam Sidecar — placeholder (T14)");
});

server.listen(port, "0.0.0.0", () => {
  logger.info({ port }, "Steam sidecar placeholder listening");
});
