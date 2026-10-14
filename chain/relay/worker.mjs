// NEON PROTOCOL relay (Cloudflare Worker).
// Issues single-use run sessions, applies coarse server checks to submitted runs, and registers accepted runs on
// Monad with the game operator key (Worker secret). Players never sign or pay. Reads are plain eth_calls.
//
//   POST /session            {challengeOf?, rulesVersion}          -> {session, token, issuedAt}
//   POST /submit             {token, name, timeMs, art(hex), wallet?, walletSig?} -> {id, hash, status, ms, attested:true}
//   GET  /trace/:id                                                -> full trace incl. art hex
//   GET  /gallery?from=&n=                                         -> newest-first list
//   GET  /health
//
// Guest identity: a deterministic address derived from a browser-stored random id. Pseudonymous, proves no ownership.
// Trust model (honest): the relay attests "this run used a fresh server session and passed timing/size checks".
// It cannot prove the client wasn't modified. Labels in the UI must say "relay-accepted", never "cheat-proof".
import { createPublicClient, createWalletClient, http, keccak256, toHex, isAddress, getAddress, defineChain, hexToBytes, encodeFunctionData, verifyMessage } from "viem";
import { privateKeyToAccount } from "viem/accounts";

const monadTestnet = defineChain({
  id: 10143, name: "Monad Testnet", nativeCurrency: { name: "MON", symbol: "MON", decimals: 18 },
  rpcUrls: { default: { http: ["https://testnet-rpc.monad.xyz"] } },
});

const ABI = [
  { type: "function", name: "registerAttested", stateMutability: "nonpayable", inputs: [
    { name: "player", type: "address" }, { name: "name", type: "string" }, { name: "timeMs", type: "uint32" }, { name: "challengeOf", type: "uint32" },
    { name: "rulesVersion", type: "uint16" }, { name: "algo", type: "uint8" }, { name: "session", type: "bytes32" }, { name: "art", type: "bytes" }], outputs: [{ type: "uint32" }] },
  { type: "function", name: "count", stateMutability: "view", inputs: [], outputs: [{ type: "uint256" }] },
  { type: "function", name: "getTrace", stateMutability: "view", inputs: [{ type: "uint32" }], outputs: [{ type: "tuple", components: [
    { name: "player", type: "address" }, { name: "timestamp", type: "uint64" }, { name: "timeMs", type: "uint32" }, { name: "challengeOf", type: "uint32" },
    { name: "rulesVersion", type: "uint16" }, { name: "algo", type: "uint8" }, { name: "attested", type: "bool" }, { name: "blockNumber", type: "uint64" },
    { name: "artHash", type: "bytes32" }, { name: "name", type: "string" }] }] },
  { type: "function", name: "page", stateMutability: "view", inputs: [{ type: "uint32" }, { type: "uint32" }], outputs: [
    { name: "ids", type: "uint32[]" }, { name: "players", type: "address[]" }, { name: "times", type: "uint32[]" }, { name: "attested", type: "bool[]" },
    { name: "hashes", type: "bytes32[]" }, { name: "names", type: "string[]" }] },
  { type: "event", name: "TraceRegistered", inputs: [
    { name: "id", type: "uint32", indexed: true }, { name: "player", type: "address", indexed: true }, { name: "challengeOf", type: "uint32", indexed: true },
    { name: "timeMs", type: "uint32" }, { name: "rulesVersion", type: "uint16" }, { name: "algo", type: "uint8" }, { name: "attested", type: "bool" },
    { name: "artHash", type: "bytes32" }, { name: "session", type: "bytes32" }, { name: "name", type: "string" }, { name: "art", type: "bytes" }] },
];

// per rules version: plausible completion window. v1 = full adventure, v2 = featured route (Relay A -> Core -> escape)
const RULES = { 1: { minMs: 45000, maxMs: 15 * 60000 }, 2: { minMs: 40000, maxMs: 15 * 60000 }, 3: { minMs: 40000, maxMs: 15 * 60000 } };
// Monad testnet by default; CHAIN_ID/RPC_URL only exist so the identical code can be tested against a LOCAL dev chain.
const chainFor = env => !env.CHAIN_ID || Number(env.CHAIN_ID) === 10143 ? monadTestnet
  : defineChain({ id: Number(env.CHAIN_ID), name: "local-dev", nativeCurrency: { name: "ETH", symbol: "ETH", decimals: 18 }, rpcUrls: { default: { http: [env.RPC_URL] } } });
const MAX_ART = 1024, ALGO = 1;
const CORS = { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Methods": "GET,POST,OPTIONS", "Access-Control-Allow-Headers": "Content-Type" };
const json = (b, s = 200, extra = {}) => new Response(JSON.stringify(b, (k, v) => typeof v === "bigint" ? Number(v) : v), { status: s, headers: { "Content-Type": "application/json", ...CORS, ...extra } });
const clean = (s, max) => String(s ?? "").normalize("NFKC").replace(/[^\p{L}\p{N} _.\-]/gu, "").trim().slice(0, max) || "Runner";

// --- HMAC session tokens (stateless issue, single use enforced on-chain by sessionUsed + KV fast-path) ---
async function hmac(secret, msg) {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const sig = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(msg));
  return [...new Uint8Array(sig)].map(b => b.toString(16).padStart(2, "0")).join("");
}
async function makeToken(env, payload) {
  const body = btoa(JSON.stringify(payload));
  return body + "." + (await hmac(env.SESSION_SECRET, body));
}
async function readToken(env, token) {
  const [body, sig] = String(token || "").split(".");
  if (!body || !sig || sig !== (await hmac(env.SESSION_SECRET, body))) throw new Error("bad token");
  return JSON.parse(atob(body));
}

export const linkMessage = (session, wallet) => `NEON PROTOCOL
Link this run to my wallet.
Session: ${session}
Wallet: ${String(wallet).toLowerCase()}
No transaction, no gas.`;

// Guest identity = the last 20 bytes of a hash: NO private key exists for it, so no wallet (not the relay, not the
// browser) can ever sign as a guest or claim a guest run as a collectible.
function guestAddress(id) { return getAddress("0x" + keccak256(toHex(`neon-guest-v2-${id}`)).slice(-40)); }

async function rateLimit(env, ip, bucket, max, windowS) {
  if (!env.KV) return true;
  const k = `rl:${bucket}:${ip}:${Math.floor(Date.now() / 1000 / windowS)}`;
  const n = Number((await env.KV.get(k)) || 0);
  if (n >= max) return false;
  await env.KV.put(k, String(n + 1), { expirationTtl: windowS * 2 });
  return true;
}

let nonce = null;

const relay = {
  async fetch(request, env) {
    if (request.method === "OPTIONS") return new Response(null, { headers: CORS });
    const url = new URL(request.url);
    const ip = request.headers.get("CF-Connecting-IP") || "local";
    const pub = createPublicClient({ chain: chainFor(env), transport: http(env.RPC_URL || undefined) });
    const read = (functionName, args = []) => pub.readContract({ address: env.CONTRACT_ADDRESS, abi: ABI, functionName, args });

    try {
      if (url.pathname === "/health") return json({ ok: true, contract: env.CONTRACT_ADDRESS, chainId: chainFor(env).id, kv: !!env.KV, registrationAvailable: !!(env.OPERATOR_KEY && env.SESSION_SECRET && env.REGISTRATION_QUEUE), dailyAttempts: Number(env.DAILY_CAP || 10) });

      // Writes spend the operator's MON: never serve them without the KV store that enforces rate limits.
      if ((url.pathname === "/session" || url.pathname === "/submit") && request.method === "POST" && !env.KV) return json({ error: "relay misconfigured: no KV" }, 503);

      if (url.pathname === "/session" && request.method === "POST") {
        if (!(await rateLimit(env, ip, "session", 30, 3600))) return json({ error: "rate limited" }, 429);
        const b = await request.json().catch(() => ({}));
        const rulesVersion = Number(b.rulesVersion || 1);
        if (!RULES[rulesVersion]) return json({ error: "unknown rules version" }, 400);
        const challengeOf = Math.max(0, Math.floor(Number(b.challengeOf || 0)));
        const session = keccak256(toHex(crypto.randomUUID() + Date.now()));
        const issuedAt = Date.now();
        const token = await makeToken(env, { session, issuedAt, rulesVersion, challengeOf });
        return json({ session, token, issuedAt, rulesVersion, challengeOf });
      }

      if (url.pathname === "/submit" && request.method === "POST") {
        if (!(await rateLimit(env, ip, "submit", 12, 3600))) return json({ error: "rate limited" }, 429);
        const b = await request.json();
        const t = await readToken(env, b.token);
        const rules = RULES[t.rulesVersion];
        const timeMs = Math.floor(Number(b.timeMs));
        const elapsed = Date.now() - t.issuedAt;
        // Coarse plausibility: claimed time inside the window and not longer than real wall-clock time since the session started.
        if (!(timeMs >= rules.minMs && timeMs <= rules.maxMs)) return json({ error: "time outside accepted window" }, 422);
        if (timeMs > elapsed + 5000) return json({ error: "claimed time exceeds session duration" }, 422);
        if (elapsed > 60 * 60000) return json({ error: "session expired" }, 422);
        const art = String(b.art || "");
        if (!/^0x[0-9a-fA-F]+$/.test(art) || (art.length - 2) / 2 > MAX_ART || art.length < 12) return json({ error: "bad art record" }, 400);
        if (hexToBytes(art)[0] !== ALGO) return json({ error: "unsupported art version" }, 400);
        if (hexToBytes(art)[1] !== t.rulesVersion) return json({ error: "art rules do not match session" }, 422);   // no cross-route comparisons
        // global spend cap: at most DAILY_CAP registrations per UTC day across all players (bounds the operator's MON spend)
        const dayKey = "day:" + new Date().toISOString().slice(0, 10), cap = Number(env.DAILY_CAP || 40);
        const today = Number((await env.KV.get(dayKey)) || 0);
        if (today >= cap) return json({ error: `daily registration limit reached (${cap}); try again tomorrow` }, 429);
        // fast-path duplicate check; the authoritative single-use check is on-chain (sessionUsed)
        if (env.KV && (await env.KV.get("used:" + t.session))) return json({ error: "session already used" }, 409);
        // A wallet is only linked with the owner's signature over this exact session (opt-in, no gas for the player).
        let player;
        if (b.wallet) {
          if (!isAddress(b.wallet) || !b.walletSig) return json({ error: "wallet link needs a signature" }, 400);
          const ok = await verifyMessage({ address: b.wallet, message: linkMessage(t.session, b.wallet), signature: b.walletSig }).catch(() => false);
          if (!ok) return json({ error: "wallet signature does not match" }, 401);
          player = b.wallet;
        } else player = guestAddress(String(b.guestId || t.session).slice(0, 80));
        const account = privateKeyToAccount("0x" + String(env.OPERATOR_KEY).replace(/[^0-9a-fA-F]/g, "").slice(-64));
        const wallet = createWalletClient({ account, chain: chainFor(env), transport: http(env.RPC_URL || undefined) });
        const t0 = Date.now();
        const args = [player, clean(b.name, 24), timeMs, t.challengeOf, t.rulesVersion, ALGO, t.session, art];
        const est = await pub.estimateGas({ account: account.address, to: env.CONTRACT_ADDRESS, data: encodeFunctionData({ abi: ABI, functionName: "registerAttested", args }) });
        const gas = est * 115n / 100n;
        if (gas > 400_000n) throw new Error("gas estimate too high: " + est);
        const gasPrice = await pub.getGasPrice();
        if (chainFor(env).id === 10143 && gasPrice > 110_000_000_000n) return json({ error: "testnet fees above the demo budget; retry later" }, 503);
        // Reserve before broadcast. A timeout must never allow another spend for the same session.
        await env.KV.put(dayKey, String(today + 1), { expirationTtl: 2 * 86400 });
        await env.KV.put("used:" + t.session, "reserved", { expirationTtl: 7 * 86400 });
        nonce = await pub.getTransactionCount({ address: account.address, blockTag: "pending" });
        const hash = await wallet.writeContract({ address: env.CONTRACT_ADDRESS, abi: ABI, functionName: "registerAttested", args, type: "legacy", nonce, gas, gasPrice });
        await env.KV.put("tx:" + t.session, hash, { expirationTtl: 7 * 86400 });
        const receipt = await pub.waitForTransactionReceipt({ hash, pollingInterval: 150, timeout: 15000 });
        if (receipt.status === "success" && env.KV) await env.KV.put("used:" + t.session, "1", { expirationTtl: 7 * 86400 });   // only burn on success
        let id = null;
        for (const log of receipt.logs) if (log.address.toLowerCase() === env.CONTRACT_ADDRESS.toLowerCase() && log.topics.length >= 2) { id = Number(BigInt(log.topics[1])); break; }
        return json({ id, hash, status: receipt.status, block: Number(receipt.blockNumber), ms: Date.now() - t0, player, attested: true });
      }

      const m = url.pathname.match(/^\/trace\/(\d+)$/);
      if (m && request.method === "GET") {
        const id = Number(m[1]);
        const tr = await read("getTrace", [id]);
        // The art bytes live in the TraceRegistered log of tr.blockNumber; verify them against the stored hash.
        const logs = await pub.getContractEvents({ address: env.CONTRACT_ADDRESS, abi: ABI, eventName: "TraceRegistered", args: { id }, fromBlock: tr.blockNumber, toBlock: tr.blockNumber });
        const ev = logs[0];
        if (!ev || keccak256(ev.args.art) !== tr.artHash) return json({ error: "art log not found or hash mismatch" }, 502);
        return json({ ...tr, id, art: ev.args.art, session: ev.args.session, txHash: ev.transactionHash }, 200, { "Cache-Control": "public, max-age=60" });
      }

      if (url.pathname === "/gallery" && request.method === "GET") {
        const from = Number(url.searchParams.get("from") || 0), n = Math.min(48, Number(url.searchParams.get("n") || 24));
        const total = Number(await read("count"));
        if (total === 0) return json({ total, items: [] }, 200, { "Cache-Control": "public, max-age=15" });
        const [ids, players, times, attested, hashes, names] = await read("page", [from, n]);
        return json({ total, items: ids.map((id, i) => ({ id, player: players[i], timeMs: times[i], attested: attested[i], artHash: hashes[i], name: names[i] })) }, 200, { "Cache-Control": "public, max-age=15" });
      }

      return json({ error: "not found" }, 404);
    } catch (e) {
      return json({ error: String(e.shortMessage || e.message || e) }, 500);
    }
  },
};


// All live write requests use one SQLite Durable Object. It serializes nonce selection,
// single-use sessions and daily reservations across Worker instances and regions.
export class RegistrationQueue {
  constructor(ctx, env) { this.ctx = ctx; this.env = env; }
  async fetch(request) {
    return this.ctx.blockConcurrencyWhile(async () => {
      const storage = this.ctx.storage;
      const KV = {
        get: async key => { const entry = await storage.get(key); return entry && (!entry.until || entry.until > Date.now()) ? entry.value : null; },
        put: async (key, value, opts = {}) => storage.put(key, { value, until: opts.expirationTtl ? Date.now() + opts.expirationTtl * 1000 : 0 })
      };
      return relay.fetch(request, { ...this.env, KV });
    });
  }
}
export default {
  async fetch(request, env) {
    const path = new URL(request.url).pathname;
    if (request.method === "POST" && (path === "/session" || path === "/submit")) {
      if (env.REGISTRATION_QUEUE) return env.REGISTRATION_QUEUE.get(env.REGISTRATION_QUEUE.idFromName("operator-v1")).fetch(request);
      if (!env.CHAIN_ID || Number(env.CHAIN_ID) === 10143) return json({ error: "registration temporarily unavailable" }, 503);
    }
    return relay.fetch(request, env);
  }
};
