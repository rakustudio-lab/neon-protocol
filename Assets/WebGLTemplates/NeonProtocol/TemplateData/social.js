// NEON PROTOCOL social layer (no Unity needed): achievement links, sharing, registry reads.
// Two kinds of achievement link:
//   trace.html?id=N        a run REGISTERED on Monad (read back from the registry through the relay, art hash verified)
//   trace.html#r=<payload> a LOCAL run carried inside the link itself (not verified, clearly labelled as such)
const CFG = Object.assign({ relay: "", network: "Monad Testnet", explorer: "https://testnet.monadvision.com" }, window.NEON_CONFIG || {});
export { CFG };

const b64u = {
  enc: (str) => btoa(unescape(encodeURIComponent(str))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, ""),
  dec: (s) => decodeURIComponent(escape(atob(s.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((s.length + 3) % 4)))),
};

const base = () => location.href.replace(/[^/]*([?#].*)?$/, "");
export const siteBase = base;

// Local (unregistered) run -> self-contained link payload
export function localPayload(run) {
  return b64u.enc(JSON.stringify({ v: 1, n: (run.name || "Runner").slice(0, 24), t: run.timeMs, a: String(run.art || "").replace(/^0x/, ""),
    s: { sh: run.shards || 0, tg: run.tags || 0, rv: run.rivalsTagged || 0, dr: run.drones || 0, pen: run.penaltyMs || 0 }, d: run.date || new Date().toISOString().slice(0, 10) }));
}
export function readLocalPayload(s) { try { const o = JSON.parse(b64u.dec(s)); return o && o.v === 1 && o.a ? o : null; } catch { return null; } }
export function localTraceUrl(run) { return `${base()}trace.html#r=${localPayload(run)}`; }
export function registeredTraceUrl(id) { return `${base()}trace.html?id=${id}`; }
export function challengeUrl(id) { return `${base()}index.html?c=${id}`; }
export function xIntent(text, url) { return `https://x.com/intent/post?text=${encodeURIComponent(text)}&url=${encodeURIComponent(url)}`; }

export async function relayGet(path) {
  if (!CFG.relay) throw new Error("registry not deployed yet");
  const r = await fetch(`${CFG.relay}${path}`); const j = await r.json();
  if (!r.ok || j.error) throw new Error(j.error || `HTTP ${r.status}`);
  return j;
}
export const fmtTime = (ms) => { const m = Math.floor(ms / 60000), s = Math.floor(ms / 1000) % 60, x = ms % 1000; return `${m}:${String(s).padStart(2, "0")}.${String(x).padStart(3, "0")}`; };
export const esc = (s) => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
