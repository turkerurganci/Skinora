// Placeholder — will be replaced by Next.js app in T13
const http = require("http");

const port = parseInt(process.env.PORT || "3000", 10);

const server = http.createServer((req, res) => {
  if (req.url === "/health") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ status: "ok", service: "skinora-frontend" }));
    return;
  }
  res.writeHead(200, { "Content-Type": "text/plain" });
  res.end("Skinora Frontend — placeholder (T13)");
});

server.listen(port, "0.0.0.0", () => {
  console.log(`Frontend placeholder listening on port ${port}`);
});
