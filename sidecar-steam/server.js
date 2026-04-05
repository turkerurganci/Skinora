// Placeholder — will be replaced by Steam sidecar in T14
const http = require("http");

const port = parseInt(process.env.PORT || "5100", 10);

const server = http.createServer((req, res) => {
  if (req.url === "/health") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ status: "ok", service: "skinora-steam-sidecar" }));
    return;
  }
  res.writeHead(200, { "Content-Type": "text/plain" });
  res.end("Skinora Steam Sidecar — placeholder (T14)");
});

server.listen(port, "0.0.0.0", () => {
  console.log(`Steam sidecar placeholder listening on port ${port}`);
});
