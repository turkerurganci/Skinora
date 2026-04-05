// Placeholder — will be replaced by Blockchain sidecar in T15
const http = require("http");

const port = parseInt(process.env.PORT || "5200", 10);

const server = http.createServer((req, res) => {
  if (req.url === "/health") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ status: "ok", service: "skinora-blockchain-sidecar" }));
    return;
  }
  res.writeHead(200, { "Content-Type": "text/plain" });
  res.end("Skinora Blockchain Sidecar — placeholder (T15)");
});

server.listen(port, "0.0.0.0", () => {
  console.log(`Blockchain sidecar placeholder listening on port ${port}`);
});
