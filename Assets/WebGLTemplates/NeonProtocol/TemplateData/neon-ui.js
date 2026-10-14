// NEON PROTOCOL web layer: menu, HUD, pause, result screen, Monad registration, wallet, sharing.
// Unity talks to this file through window.onNeonGameEvent / window.onNeonCrosshair (see NeonBridge.jslib).
// Rule: every number shown here is bound to real game, relay or chain state. Empty/unavailable states say so.
import { renderSignature, hexToBytes } from "./signature.js";
import { localTraceUrl, registeredTraceUrl, xIntent } from "./social.js";

const CFG = Object.assign({ relay: "", explorer: "https://testnet.monadvision.com", network: "Monad Testnet", site: location.origin + location.pathname,
  observeRpc: "https://rpc.monad.xyz", observeName: "Monad Mainnet", observeChain: 143,
  testnetRpc: "https://testnet-rpc.monad.xyz", testnetChain: 10143 }, window.NEON_CONFIG || {});
const $ = (s, r = document) => r.querySelector(s);
const qs = new URLSearchParams(location.search);
const state = { running: false, ended: false, session: null, lastRun: null, registered: null, challengeOf: Math.max(0, parseInt(qs.get("c") || "0", 10) || 0),
  name: localStorage.getItem("np.name") || "", wallet: null, walletChain: null, walletBal: null, linkWallet: localStorage.getItem("np.linkWallet") === "1",
  block: null, blockSeen: 0, feed: "CONNECTING", registry: null, hud: null };
const ICON = (n) => `TemplateData/icons/${n}.svg`;
const fmt = (ms) => { const m = Math.floor(ms / 60000), s = Math.floor(ms / 1000) % 60, t = Math.floor(ms % 1000 / 100); return `${m}:${String(s).padStart(2, "0")}.${t}`; };
const fmtFull = (ms) => { const m = Math.floor(ms / 60000), s = Math.floor(ms / 1000) % 60, x = ms % 1000; return `${m}:${String(s).padStart(2, "0")}.${String(x).padStart(3, "0")}`; };
const esc = (s) => String(s ?? "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const short = (a) => a ? a.slice(0, 6) + "…" + a.slice(-4) : "";
const send = (m, a = "") => window.unityInstance?.SendMessage("_Systems", m, a);

// R4: pointer lock can be refused (embedded previews raise WrongDocumentError; Chrome returns a rejected promise).
// That must never surface as an uncaught error / Unity alert: catch it, explain it, keep the menu recoverable.
// Unity's own Cursor.lockState also calls canvas.requestPointerLock, so the prototype is wrapped once here.
let everLocked = false, lockFails = 0;
function lockRefused(err) {
  const n = err?.name || "pointerlockerror";
  console.warn("[NP] pointer lock refused:", n, err?.message || "");
  lockFails++;
  const hard = /WrongDocumentError|NotSupportedError/.test(n) || (!everLocked && lockFails >= 2);
  if (state.scripted || !hard || !state.running || state.ended || document.pointerLockElement) return;
  screen(`<div class="glass lockhelp"><div class="kicker">MOUSE CAPTURE BLOCKED</div><h2>This view can't capture the mouse.</h2>
    <p>NEON PROTOCOL needs pointer lock for mouse look. Embedded previews and some in-app browsers block it. Open the game in a normal Chrome, Edge or Firefox tab, then press PLAY.</p>
    <div class="row-btns"><button class="btn primary" id="lkRetry">TRY AGAIN</button><button class="btn" id="lkOpen">OPEN IN A NEW TAB</button><button class="btn" id="lkMenu">BACK TO MENU</button></div>
    <div class="status mono">${esc(n)}</div></div>`);
  $("#lkRetry").onclick = () => { hideScreen(); lockCanvas(); };
  $("#lkOpen").onclick = () => window.open(location.href, "_blank", "noopener");
  $("#lkMenu").onclick = () => { state.running = false; send("QuitToMenuFromWeb"); showMenu(); };
}
function lockCanvas() { const c = document.getElementById("c"); c.focus(); c.requestPointerLock?.(); }
{
  const orig = HTMLCanvasElement.prototype.requestPointerLock;
  if (orig) HTMLCanvasElement.prototype.requestPointerLock = function (...a) {
    try { const r = orig.apply(this, a); if (r && typeof r.catch === "function") r.catch(lockRefused); return r; }
    catch (e) { lockRefused(e); }
  };
}
window.addEventListener("unhandledrejection", (ev) => { const n = ev.reason?.name || ""; if (/WrongDocumentError/.test(n) || /pointer ?lock/i.test(String(ev.reason?.message || ""))) { ev.preventDefault(); lockRefused(ev.reason); } });

// ======================================================================= observed Monad block feed (real, read-only)
async function rpc(url, method, params = []) {
  const r = await fetch(url, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ jsonrpc: "2.0", id: 1, method, params }) });
  const j = await r.json(); if (j.error) throw new Error(j.error.message); return j.result;
}
let feedFails = 0;
async function pollBlocks() {
  try {
    const b = await rpc(CFG.observeRpc, "eth_getBlockByNumber", ["latest", false]);
    const n = parseInt(b.number, 16);
    if (!state.block || n !== state.block.n) { state.block = { n, ts: parseInt(b.timestamp, 16), tx: b.transactions.length, hash: b.hash }; state.blockSeen = Date.now(); }
    feedFails = 0;
  } catch (e) { feedFails++; }
  const age = state.blockSeen ? (Date.now() - state.blockSeen) / 1000 : 999;
  state.feed = feedFails >= 3 ? "OFFLINE" : age > 10 ? "STALE" : state.block ? "LIVE" : "CONNECTING";
  paintFeed();
  setTimeout(pollBlocks, document.hidden ? 8000 : feedFails ? Math.min(15000, 2000 * 2 ** feedFails) : 2000);   // bounded rate, backoff on errors
}
function paintFeed() {
  const cls = { LIVE: "live", STALE: "stale", OFFLINE: "off" }[state.feed] || "";
  const b = state.block, age = state.blockSeen ? Math.max(0, Math.round((Date.now() - state.blockSeen) / 1000)) : null;
  document.querySelectorAll("[data-feed]").forEach(el => {
    const k = el.dataset.feed;
    if (k === "dot") el.className = "dot " + cls;
    else if (k === "state") el.textContent = state.feed;
    else if (k === "block") el.textContent = b ? "#" + b.n.toLocaleString("en-US") : "—";
    else if (k === "tx") el.textContent = b ? b.tx + " tx" : "—";
    else if (k === "age") el.textContent = age == null ? "—" : `seen ${age}s ago`;
    else if (k === "hash") el.textContent = b ? short(b.hash) : "—";
  });
}

// ======================================================================= registry (relay) status — only real records
async function loadRegistry() {
  if (!CFG.relay) { state.registry = { ok: false, reason: "Monad collection is not live yet. Play and save your artwork on this device." }; return state.registry; }
  try {
    const g = await (await fetch(`${CFG.relay}/gallery?n=24`)).json();
    state.registry = { ok: true, total: g.total, items: g.items || [] };
  } catch (e) { state.registry = { ok: false, reason: "Registry relay unreachable: " + e.message }; }
  return state.registry;
}

// ======================================================================= root: HUD
const root = document.createElement("div");
root.className = "np-ui";
root.innerHTML = `
<div id="hud">
  <div class="h-objbar glass"><div class="ob-step" id="obStep">1/5</div><div class="ob-main"><div class="ob-ch" id="chapter">CHAPTER I — THE FRACTURE</div><div class="ob-t" id="nextT">—</div></div><div class="ob-dist mono" id="obDist"></div><div class="ob-time mono" id="time">0:00.0</div></div>
  <div class="h-net"><span class="dot" data-feed="dot" style="display:inline-block;vertical-align:middle"></span> ${esc(CFG.observeName.toUpperCase())} <span data-feed="block">—</span></div>
  <div class="h-lock np-hidden" id="lockdown"><b>VAULT LOCKDOWN</b><span id="lockTxt">SHUTTERS CLOSED · CREWS CALLED TO YOUR POSITION · LOCK PROGRESS RESET</span></div>
  <div class="h-detect np-hidden" id="detect">SPOTTED</div>
  <div class="hud-announce" id="announce"></div>
  <div class="xh" id="xh"><i class="l"></i><i class="r"></i><i class="t"></i><i class="b"></i><i class="d"></i></div>
  <div class="hm" id="hm"><i class="a"></i><i class="b"></i><i class="c"></i><i class="e"></i></div>
  <div class="hitarc" id="hitarc"></div>
  <div id="markers"></div><div id="bots"></div>
  <div class="h-hint" id="hint"></div>
  <div class="h-tags"><div class="lbl">PAINT HITS</div><div class="pips" id="pips"></div><span class="mono" id="hitsV" style="display:none"></span></div>
  <div class="hud-ammo" id="wcard"><div class="wt">CHARGED PAINT RIFLE</div><div class="wprompt" id="wprompt">R RELOAD</div>
    <div class="wrow"><span class="n" id="ammo">28</span><span class="m" id="mag">/ 28</span>
      <div class="wside"><div class="segs" id="segs"></div><div class="reload" id="reload"><i></i></div><div class="wstate" id="wstate">READY</div></div></div>
    <div class="press"><span>PRESSURE</span><div class="pbar"><i id="pfill"></i></div></div></div>
  <div id="bikeHud" class="np-hidden">
    <div class="b-ch glass"><div class="ch">CHAPTER II — FINALITY RUN</div><div class="ob" id="bObj">REACH THE EXIT</div><div class="bt" id="bBeat">LAUNCH TUNNEL</div></div>
    <div class="b-speed glass"><div class="lbl">FINALITY SPEEDWAY</div><div><span class="v" id="bKmh">0</span><span class="u">KM/H</span></div><div class="tm" id="bTime">0:00.0 · <span id="bRemain">0</span> m to the gate</div></div>
    <div class="b-shards glass"><div><div class="lbl">ENERGY SHARDS</div><div class="v" id="bShards">0 / 0</div><div class="req" id="bReq"></div></div>
      <div style="flex:1"><div class="lbl">BOOST <kbd>SHIFT</kbd></div><div class="bar"><i id="bBoost"></i></div><div class="lbl" style="margin-top:10px">DODGE <kbd>SPACE</kbd></div><div class="bar thin"><i id="bDodge"></i></div></div></div>
    <div class="b-gun glass"><div class="lbl" style="color:var(--white)">BIKE PAINT LAUNCHER</div><div class="row"><span class="v" id="bAmmo">20</span><span class="m" id="bMag">/ 20</span>
      <div class="shields"><div class="lbl">SHIELDS</div><div id="bShields"></div></div></div>
      <div class="bar thin"><i id="bReload"></i></div><div class="hint" id="bHint"><kbd>LMB</kbd> PAINT · <kbd>R</kbd> RELOAD · <kbd>A</kbd><kbd>D</kbd> STEER · <kbd>S</kbd> BRAKE</div></div>
  </div>
  <div class="ascend np-hidden" id="ascend"><div class="k">ESCAPE COMPLETE</div><div class="t">YOUR RUN BECOMES ART</div><div class="bar"><i id="ascBar"></i></div><div class="b">SAVE YOUR ARTWORK · CHALLENGE A FRIEND · COLLECT ON MONAD<span class="mono" id="ascBlocks" style="display:none">0</span></div></div>
  <div class="tagged np-hidden" id="tagged"><div>TAGGED OUT<small>RESPAWNING AT CHECKPOINT · TIME PENALTY APPLIES</small></div></div>
</div>`;
document.body.appendChild(root);

// ======================================================================= MENU
const MISSION = [["paint", "Activate the 3 security relays"], ["gem", "Secure the Core and escape through the Finality Gate"], ["chain", "Create your Neon Trace and register it on Monad"]];
function showMenu() {
  state.running = false; hideScreen(); $("#hud").classList.remove("on");
  let m = $("#np-menu"); if (m) m.remove();
  m = document.createElement("div"); m.id = "np-menu";
  // Founder direction: world + title + one PLAY. Network/records diagnostics live behind a disclosure.
  m.innerHTML = `
  <div class="m-hero">
    <img class="wordmark" src="${ICON("wordmark")}" alt="NEON PROTOCOL">
    <h1 class="m-promise">Steal the core.<br>Your run becomes art.</h1>
    <div id="challengeBanner"></div>
    <div class="m-actions">
      <button class="mbtn primary" id="b_play">${state.challengeOf ? "ACCEPT CHALLENGE" : "PLAY"}</button>
      <button class="mbtn secondary" data-d="vault">THE VAULT</button>
    </div>
    <div class="m-quiet"><button class="m-link" data-d="how">HOW TO PLAY</button><span aria-hidden="true">·</span><button class="m-link" data-d="settings">SETTINGS</button><span aria-hidden="true">·</span><button class="m-link" data-d="wallet">PROFILE &amp; WALLET</button></div>
    <div class="m-req">Desktop prototype · keyboard / mouse or controller</div>
    <div class="m-req m-wallet" id="walletNote">${walletNote()}</div>
  </div>
  <button class="m-link m-netbtn" id="b_net" aria-expanded="false" aria-controls="mRight">NETWORK DETAILS</button>
  <div class="m-right np-hidden" id="mRight">
    <div class="card glass"><h3><span class="dot" data-feed="dot"></span>OBSERVED · ${esc(CFG.observeName.toUpperCase())}</h3>
      <div class="row"><span>LATEST BLOCK</span><span class="mono"><span data-feed="block">—</span> · <span data-feed="age">—</span></span></div>
      <div class="row"><span>SOURCE</span><span class="mono" style="font-size:11px">${esc(CFG.observeRpc.replace("https://", ""))} (public RPC)</span></div>
      <div class="row"><span>RECORDS</span><span id="recSummary" style="text-align:right">Loading…</span></div>
      <div class="note spec">This is a read-only view of the network. ${CFG.relay ? `Run records use ${esc(CFG.network)}; receipt time is measured.` : "This prototype has not deployed its run registry or collectibles to Monad yet."}</div></div>
  </div>`;
  document.body.appendChild(m);
  $("#b_play").onclick = () => { if (!state.challengeOf) state.route = "featured"; showBriefing(false); };
  setTimeout(() => $("#b_play")?.focus({ preventScroll: true }), 0);   // keyboard users start on the primary action
  m.querySelectorAll("[data-d]").forEach(b => b.onclick = () => openDrawer(b.dataset.d, b));
  $("#b_net").onclick = () => { const open = $("#mRight").classList.toggle("np-hidden") === false; $("#b_net").setAttribute("aria-expanded", open); };
  paintFeed();
  loadRegistry().then(r => {
    const el = $("#recSummary"); if (!el) return;
    el.textContent = r.ok ? `${r.total} registered on ${CFG.network}` : `unavailable · ${r.reason}`;
    const c = $("#chCount"); if (c && r.ok) c.textContent = r.total + " OPEN";
  });
  if (state.challengeOf) loadChallengeBanner();
}
function hideMenu() { const m = $("#np-menu"); if (m) m.remove(); }

async function loadChallengeBanner() {
  const box = $("#challengeBanner"); if (!box) return;
  if (!CFG.relay) { box.innerHTML = `<div class="status warn">Challenge #${state.challengeOf}: the registry is not reachable in this build.</div>`; return; }
  box.innerHTML = `<div class="status">Loading challenge #${state.challengeOf} from Monad…</div>`;
  try {
    const t = await (await fetch(`${CFG.relay}/trace/${state.challengeOf}`)).json();
    if (t.error) throw new Error(t.error);
    box.innerHTML = `<div class="challenge-box"><canvas id="cart"></canvas><div><div class="kicker">CHALLENGE · TRACE #${state.challengeOf}</div>
      <div class="mono" style="font-size:28px;margin:6px 0">${fmtFull(t.timeMs)}</div><div>${esc(t.name)} · ${t.attested ? "relay-accepted run" : "self-submitted run"}</div>
      <div style="font-size:12px;color:var(--muted);margin-top:6px">Rules v${t.rulesVersion}. Beat this time on the same rules version.</div></div></div>`;
    renderSignature($("#cart"), hexToBytes(t.art), { id: state.challengeOf, name: t.name, status: "REGISTERED ON MONAD", hash: t.artHash });
    state.challengeTime = t.timeMs; state.route = t.rulesVersion === 1 ? "full" : "featured";   // a challenge replays the same rules
  } catch (e) { box.innerHTML = `<div class="status warn">Could not load challenge #${state.challengeOf}: ${esc(e.message)}</div>`; }
}

// ----------------------------------------------------------------------- drawers
function openDrawer(kind, btn) {
  document.querySelectorAll(".mbtn.sel").forEach(b => b.classList.remove("sel")); btn?.classList.add("sel");
  const right = $("#mRight"), wasOpen = right && !right.classList.contains("np-hidden"); if (right) right.classList.add("np-hidden");
  let d = $("#drawer"); if (d) d.remove();
  d = document.createElement("div"); d.id = "drawer"; d.className = "drawer glass";
  $("#np-menu").appendChild(d);
  const close = `<button class="close" id="dclose">CLOSE ✕</button>`;
  const done = () => { $("#dclose").onclick = () => { d.remove(); if (wasOpen) right?.classList.remove("np-hidden"); btn?.classList.remove("sel"); btn?.focus(); }; setTimeout(() => $("#dclose")?.focus(), 0); };
  if (kind === "vault") {
    d.innerHTML = `${close}<h2>THE VAULT</h2><div class="sub">REGISTERED RUNS · CHALLENGES · YOUR PROFILE</div>
      <div id="vaultWalk"></div>
      <h4>REGISTERED RUNS</h4><div id="dlist"><div class="empty">Loading…</div></div>
      <a class="m-link" style="display:inline-block;margin-top:10px" href="halloffame.html" target="_blank">PUBLIC RECORDS PAGE ↗</a>
      <h4>PROFILE</h4><input class="name" id="name" maxlength="24" placeholder="YOUR NAME (SHOWN ON YOUR ARTWORK)" value="${esc(state.name)}">
      <h4>WALLET (OPTIONAL)</h4><div id="wbox"></div>
      <p style="color:var(--muted);font-size:12px">Collectibles: only a run linked to your wallet <b>before</b> you register it can be minted later (you sign a free message; minting costs network gas from your wallet). Guest runs can be registered and shared, but never minted.</p>`;
    $("#name").oninput = (e) => { state.name = e.target.value.trim(); localStorage.setItem("np.name", state.name); };
    renderWallet();
    if (window.onVaultDrawer) window.onVaultDrawer($("#vaultWalk"));
    loadRegistry().then(r => {
      const box = $("#dlist"); if (!box) return;
      if (!r.ok) { box.innerHTML = `<div class="empty">Records unavailable.<small>${esc(r.reason)}</small></div>`; return; }
      if (!r.items.length) { box.innerHTML = `<div class="empty">No registered runs yet.<small>Records appear only after a confirmed transaction.</small></div>`; return; }
      box.innerHTML = `<div class="list">${r.items.slice(0, 12).map(x => `<div class="it"><div class="mono" style="font-size:18px;text-align:center;color:var(--gold)">#${x.id}</div>
        <div><b>${esc(x.name)}</b><small>${fmtFull(x.timeMs)} · ${x.attested ? "relay-accepted" : "self-submitted"} · ${short(x.player)}</small></div>
        <button class="btn cyan" data-c="${x.id}">CHALLENGE</button></div>`).join("")}</div>`;
      box.querySelectorAll("[data-c]").forEach(b => b.onclick = async () => {
        state.challengeOf = +b.dataset.c;
        try { const t = await (await fetch(`${CFG.relay}/trace/${state.challengeOf}`)).json(); state.route = t.rulesVersion === 1 ? "full" : "featured"; state.challengeTime = t.timeMs; } catch { }
        startRun();
      });
    });
  } else if (kind === "hof" || kind === "challenges") {
    const hof = kind === "hof";
    d.innerHTML = `${close}<h2>${hof ? "HALL OF FAME" : "COMMUNITY CHALLENGES"}</h2><div class="sub">${hof ? "VERIFIABLE ACHIEVEMENTS ONLY · FASTEST REGISTERED EXTRACTIONS" : "EVERY REGISTERED TRACE IS A CHALLENGE ANYONE CAN ACCEPT"}</div><div id="dlist"><div class="empty">Loading…</div></div>
      ${hof ? `<a class="btn cyan" style="margin-top:14px;display:inline-block" href="halloffame.html" target="_blank">OPEN THE PUBLIC HALL OF FAME PAGE ↗</a>` : `<h4>HOW CHALLENGES WORK</h4><p>Extract, register your Neon Trace on Monad, then share its link. Whoever opens it plays the same rules version and sees your time to beat.</p>`}`;
    loadRegistry().then(r => {
      const box = $("#dlist"); if (!box) return;
      if (!r.ok) { box.innerHTML = `<div class="empty">No confirmed records loaded.<small>${esc(r.reason)}</small></div>`; return; }
      if (!r.items.length) { box.innerHTML = `<div class="empty">No registered Traces yet.<small>Records appear here only after a real confirmed transaction.</small></div>`; return; }
      const items = hof ? [...r.items].filter(x => x.attested).sort((a, b) => a.timeMs - b.timeMs).slice(0, 10) : r.items;
      box.innerHTML = `<div class="list">${items.map((x, i) => `<div class="it"><div class="mono" style="font-size:20px;text-align:center;color:var(--gold)">${hof ? "#" + (i + 1) : "#" + x.id}</div>
        <div><b>${esc(x.name)}</b><small>${fmtFull(x.timeMs)} · ${x.attested ? "relay-accepted" : "self-submitted"} · ${short(x.player)}</small></div>
        ${hof ? `<small class="mono">TRACE #${x.id}</small>` : `<button class="btn cyan" data-c="${x.id}">PLAY</button>`}</div>`).join("")}</div>`;
      box.querySelectorAll("[data-c]").forEach(b => b.onclick = () => { state.challengeOf = +b.dataset.c; startRun(); });
    });
  } else if (kind === "wallet") {
    d.innerHTML = `${close}<h2>WALLET + PROFILE</h2><div class="sub">GUESTS PLAY FREE · NO WALLET, NO GAS NEEDED</div>
      <h4>PUBLIC PSEUDONYM</h4><input class="name" id="name" maxlength="24" placeholder="YOUR NAME (SHOWN ON YOUR TRACE)" value="${esc(state.name)}">
      <p style="color:var(--muted);font-size:12px">Shown publicly only if you register a run. Letters, numbers, space, _ . - only.</p>
      <h4>WALLET (OPTIONAL)</h4><div id="wbox"></div>
      <h4>YOUR RECENT RUNS</h4><div class="profile-runs">${loadRuns().length ? loadRuns().slice(0,4).map(r=>`<div class="row"><span>${fmtFull(r.timeMs)}</span><span>${r.registeredId ? `<a href="trace.html?id=${Number(r.registeredId)}" target="_blank" rel="noopener">RUN #${Number(r.registeredId)} ↗</a>` : "SAVED ON THIS DEVICE"}</span></div>`).join("") : `<p>No runs yet. Play the heist to create your first artwork.</p>`}</div>
      <p style="color:var(--muted);font-size:12px">Your name and recent runs are saved in this browser. Registered runs and NFT ownership live on Monad testnet. This is a prototype profile, not an email account.</p>
      <h4>PRIVACY</h4><p style="color:var(--muted);font-size:12px">Without a wallet, registered runs use a pseudonymous guest address derived from a random browser id; it proves no ownership and the record is studio-attested. Linking a wallet requires your signature (free, no transaction).</p>`;
    $("#name").oninput = (e) => { state.name = e.target.value.trim(); localStorage.setItem("np.name", state.name); const p = $("#profSmall"); if (p) p.textContent = state.name || "GUEST"; };
    renderWallet();
  } else if (kind === "settings") {
    d.innerHTML = `${close}<h2>SETTINGS</h2><div class="sub">SAVED IN THIS BROWSER</div><div id="settings"></div>`;
    renderSettings($("#settings"));
  } else if (kind === "how") {
    d.innerHTML = `${close}<h2>HOW TO PLAY</h2><div class="sub">ONE HEIST · ABOUT TWO MINUTES</div>
      <div class="how">
        <div><b>01 · POWER THE IRIS</b>Past the guard, paint the lit pad: 1, 2, 3, then its core. One shot each. Crouch behind low cover when he warns.</div>
        <div><b>02 · CORE</b>The vault opens. Take the Core from the dais. Every crew now hunts you.</div>
        <div><b>03 · ESCAPE</b>Reach the Finality Gate and ride the lightcycle out. Shards are a bonus line.</div>
        <div><b>YOUR MARK</b>Your route and paint become artwork. Register it on Monad and send it as a challenge.</div>
      </div>
      <h4>CONTROLS</h4><div class="keys"><kbd>WASD</kbd>Move<kbd>SHIFT</kbd>Sprint<kbd>C</kbd>Crouch<kbd>SPACE</kbd>Jump<kbd>LMB</kbd>Paint<kbd>RMB</kbd>Aim<kbd>R</kbd>Reload<kbd>Q</kbd>Shoulder<kbd>ESC</kbd>Pause</div>
      <p style="color:var(--muted);font-size:12px">4 rival hits tag you out: +8 s and a nearby respawn. Rival paint is magenta, yours is cyan.</p>
      <div class="row-btns"><button class="btn" id="howIntro">SHOW THE INTRO AGAIN</button><button class="btn" id="howFull">PLAY THE FULL ADVENTURE</button></div>
      <p style="color:var(--muted);font-size:12px">The full adventure (three relays, longer escape) uses separate rules and records.</p>`;
    $("#howIntro").onclick = () => { state.route = "featured"; showBriefing(true); };
    $("#howFull").onclick = () => { state.route = "full"; showBriefing(true); };
  }
  done();
}

// ----------------------------------------------------------------------- wallet (EIP-1193, opt-in)
// The decision doc: explain BEFORE play that only a wallet-linked run can be minted later (guest runs can't be claimed).
function walletNote() {
  if (!CFG.relay) return `Play without a wallet. Save your run artwork. <a class="m-link" href="../#monad" target="_blank" rel="noopener">Monad collecting: coming next ↗</a>`;
  return state.wallet && state.linkWallet
    ? `Wallet ${esc(short(state.wallet))} linked · runs you register can be minted later as a collectible.`
    : `Want to mint this run later? <button class="m-link" data-d="wallet">Link your wallet</button> before starting. Guest runs can be saved and shared, not minted.`;
}
function walletLabel() { return state.wallet ? `<span class="dot live"></span>${short(state.wallet)}` : `<span class="dot"></span>GUEST · NO WALLET`; }
async function renderWallet() {
  const box = $("#wbox"); if (!box) return;
  const eth = window.ethereum;
  if (!eth) { box.innerHTML = `<div class="empty">No browser wallet detected.<small>Install a wallet extension to link runs to your address. You can still play and register as a guest.</small></div>`; return; }
  if (!state.wallet) { box.innerHTML = `<button class="btn primary" id="wconn">CONNECT WALLET</button>`; $("#wconn").onclick = connectWallet; return; }
  const ok = state.walletChain === CFG.testnetChain;
  box.innerHTML = `<div class="card glass" style="padding:14px 16px"><div class="row"><span>ADDRESS</span><span class="mono">${short(state.wallet)}</span></div>
    <div class="row"><span>NETWORK</span><span>${ok ? `${esc(CFG.network)} · ${CFG.testnetChain}` : `<span style="color:var(--gold)">chain ${state.walletChain} — wrong network</span>`}</span></div>
    <div class="row"><span>BALANCE</span><span class="mono">${state.walletBal != null ? state.walletBal + " MON" : "—"}</span></div></div>
    <div class="row-btns">${ok ? "" : `<button class="btn cyan" id="wswitch">SWITCH TO ${esc(CFG.network.toUpperCase())}</button>`}
    <label style="display:flex;gap:8px;align-items:center;font-size:13px"><input type="checkbox" id="wlink" ${state.linkWallet ? "checked" : ""}> Link my registered runs to this wallet (signature, no gas)</label></div>`;
  if (!ok) $("#wswitch").onclick = switchChain;
  $("#wlink").onchange = (e) => { state.linkWallet = e.target.checked; localStorage.setItem("np.linkWallet", state.linkWallet ? "1" : "0"); const wn = $("#walletNote"); if (wn) wn.innerHTML = walletNote(); };
}
async function connectWallet() {
  try {
    const [a] = await window.ethereum.request({ method: "eth_requestAccounts" });
    state.wallet = a; await refreshWallet();
    window.ethereum.on?.("chainChanged", refreshWallet); window.ethereum.on?.("accountsChanged", (x) => { state.wallet = x[0] || null; refreshWallet(); });
  } catch (e) { const b = $("#wbox"); if (b) b.insertAdjacentHTML("beforeend", `<div class="status warn">Wallet connection cancelled or failed: ${esc(e.message)}</div>`); }
}
async function refreshWallet() {
  if (!state.wallet) { renderWallet(); return; }
  try {
    state.walletChain = parseInt(await window.ethereum.request({ method: "eth_chainId" }), 16);
    const bal = state.walletChain === CFG.testnetChain ? await window.ethereum.request({ method: "eth_getBalance", params: [state.wallet, "latest"] }) : null;
    state.walletBal = bal ? (Number(BigInt(bal) / 10n ** 14n) / 10000).toFixed(4) : null;
  } catch (e) { state.walletBal = null; }
  const p = $("#walletPill"); if (p) p.innerHTML = walletLabel();
  const wn = $("#walletNote"); if (wn) { wn.innerHTML = walletNote(); wn.querySelectorAll("[data-d]").forEach(b => b.onclick = () => openDrawer(b.dataset.d, b)); }
  renderWallet();
}
async function switchChain() {
  const hex = "0x" + CFG.testnetChain.toString(16);
  try { await window.ethereum.request({ method: "wallet_switchEthereumChain", params: [{ chainId: hex }] }); }
  catch (e) {
    if (e.code === 4902) await window.ethereum.request({ method: "wallet_addEthereumChain", params: [{ chainId: hex, chainName: CFG.network, nativeCurrency: { name: "MON", symbol: "MON", decimals: 18 }, rpcUrls: [CFG.testnetRpc], blockExplorerUrls: [CFG.explorer] }] });
  }
  refreshWallet();
}

function renderSettings(el) {
  const get = (k, d) => parseFloat(localStorage.getItem(k) ?? d);
  el.innerHTML = `<div class="settings">
    <span>MOUSE SENSITIVITY</span><input type="range" id="s_sens" min="0.2" max="3" step="0.05" value="${get("np.web.sens", 1)}"><span class="mono" id="v_sens"></span>
    <span>VOLUME</span><input type="range" id="s_vol" min="0" max="1" step="0.05" value="${get("np.web.vol", 0.8)}"><span class="mono" id="v_vol"></span>
    <span>CAMERA SHAKE</span><input type="range" id="s_shake" min="0" max="1" step="0.1" value="${get("np.web.shake", 1)}"><span class="mono" id="v_shake"></span>
    <span>GRAPHICS</span><select id="s_q"><option value="low">LOW</option><option value="medium">MEDIUM</option><option value="high">HIGH</option></select><span></span>
    <span>CAMERA ON FOOT</span><select id="s_cam"><option value="fp">FIRST PERSON</option><option value="tp">THIRD PERSON</option></select><span></span></div>`;
  $("#s_q").value = localStorage.getItem("np.web.q") || "medium";
  $("#s_cam").value = localStorage.getItem("np.web.cam2") || "fp";
  const push = () => {
    const v = { sens: +$("#s_sens").value, vol: +$("#s_vol").value, shake: +$("#s_shake").value, quality: $("#s_q").value };
    $("#v_sens").textContent = v.sens.toFixed(2); $("#v_vol").textContent = Math.round(v.vol * 100); $("#v_shake").textContent = Math.round(v.shake * 100);
    localStorage.setItem("np.web.sens", v.sens); localStorage.setItem("np.web.vol", v.vol); localStorage.setItem("np.web.shake", v.shake); localStorage.setItem("np.web.q", v.quality);
    localStorage.setItem("np.web.cam2", $("#s_cam").value);
    send("ApplyWebSettings", JSON.stringify(v)); send("SetCameraMode", $("#s_cam").value);
  };
  el.querySelectorAll("input,select").forEach(i => i.oninput = push);
  push();
}

// ======================================================================= run flow
function screen(html) {
  let el = $("#np-screen");
  if (!el) { el = document.createElement("div"); el.id = "np-screen"; el.className = "screen"; document.body.appendChild(el); }
  el.innerHTML = `<div class="wrap">${html}</div>`;
  el.classList.remove("np-hidden");
  return el;
}
function hideScreen() { const el = $("#np-screen"); if (el) el.classList.add("np-hidden"); }

// Mission briefing (first-time players must know where they are, what to do, why, and what happens next)
// Short controls intro (about 15 s to read). Shown on the first PLAY in this browser; replayable from HOW TO PLAY.
function showBriefing(force) {
  hideMenu();
  if (!force && localStorage.getItem("np.introSeen") === "1") { startRun(); return; }
  const ch = state.challengeOf ? `<div class="kick" style="color:var(--gold)">CHALLENGE · BEAT TRACE #${state.challengeOf}${state.challengeTime ? " · " + fmtFull(state.challengeTime) : ""}</div>` : "";
  const full = state.route === "full";
  screen(`<div class="intro glass">
      ${ch}<div class="kick">${full ? "FULL ADVENTURE · SEPARATE RECORDS" : "THE FRACTURE"}</div>
      <h2>${full ? "Three relays. One Core. Get out." : "Steal the Core. Get out."}</h2>
      ${full ? "" : `<p><b>First:</b> power the Iris. A guard watches it, so use the cover.</p>`}
      <p>Your route and paint hits create an artwork. Finish, save the image, and share your run.</p>
      <p class="wallet-note">${walletNote()}</p>
      <div class="keys2"><span><kbd>W</kbd><kbd>A</kbd><kbd>S</kbd><kbd>D</kbd></span><span>move · <kbd>SHIFT</kbd> sprint</span>
        <span><kbd>MOUSE</kbd></span><span>look · <kbd>RMB</kbd> aim · <kbd>LMB</kbd> paint</span>
        <span><kbd>R</kbd></span><span>reload · <kbd>C</kbd> crouch · <kbd>SPACE</kbd> jump · <kbd>ESC</kbd> pause</span></div>
      <details class="pad-help"><summary>Xbox / PlayStation controller</summary><p>Move: left stick · Look: right stick · Fire: RT / R2 · Aim: LT / L2 · Jump: A / Cross · Crouch: B / Circle · Reload: X / Square · Use: Y / Triangle · Sprint: hold L3 · Pause: Menu / Options.</p><p>Bike: RT / R2 accelerate · LT / L2 brake · Left stick steer · A / Cross boost · B / Circle dodge · RB / R1 paint · X / Square reload.</p><p>Menus: D-pad to move, A / Cross to choose. Connect before PLAY. Wallet dialogs may still need the mouse. Controller support is experimental.</p></details>
      <div class="row-btns"><button class="btn primary go" id="go">START</button><button class="btn" id="back">BACK</button></div>
    </div>`);
  $("#go").onclick = () => { localStorage.setItem("np.introSeen", "1"); startRun(); };
  document.querySelectorAll("#np-screen .wallet-note [data-d]").forEach(b => b.onclick = () => { hideScreen(); showMenu(); openDrawer("wallet"); });
  document.querySelectorAll(".intro [data-d]").forEach(b => b.onclick = () => { hideScreen(); showMenu(); openDrawer(b.dataset.d, b); });
  $("#back").onclick = () => { hideScreen(); showMenu(); };
  setTimeout(() => $("#go")?.focus({ preventScroll: true }), 0);
}

async function startRun() {
  const canvas = document.getElementById("c");
  state.running = true; state.ended = false; state.registered = null; state.session = null;
  hideMenu(); hideScreen(); $("#hud").classList.add("on");
  canvas.focus(); send("SetControllerMode", state.controller ? "1" : "0"); if (!state.controller) canvas.requestPointerLock?.();   // the game reads keys from the canvas only (menus keep Tab/Enter)
  send("SetCameraMode", localStorage.getItem("np.web.cam2") || "fp");   // R4: new key, first person unless chosen again in SETTINGS
  send("StartRunFromWeb", JSON.stringify({ challengeOf: state.challengeOf, route: state.route === "full" ? "full" : "featured" }));
  if (CFG.relay) {
    const rulesVersion = state.route === "full" ? 1 : 3;
    const requestId = state.sessionRequest = (state.sessionRequest || 0) + 1;   // a later START wins over a slow earlier request
    try {
      const response = await fetch(`${CFG.relay}/session`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ rulesVersion, challengeOf: state.challengeOf }) });
      const session = await response.json();
      if (!response.ok || !session.token || session.rulesVersion !== rulesVersion) throw new Error("no compatible registration session");
      if (state.sessionRequest === requestId) state.session = session;
    } catch (e) { if (state.sessionRequest === requestId) state.session = null; }
  }
}

document.addEventListener("pointerlockerror", () => {
  const r = $("#resume"); if (r) r.textContent = "CLICK AGAIN TO RESUME";   // browsers refuse a re-lock for ~1 s after ESC
  else lockRefused({ name: "pointerlockerror" });
});
document.addEventListener("pointerlockchange", () => {
  if (state.controller || state.scripted) return;
  if (document.pointerLockElement) { everLocked = true; lockFails = 0; hideScreen(); return; }
  if (state.running && !state.ended) showPause();
});

function showPause() {
  send("PauseFromWeb");
  screen(`<div class="glass" style="padding:34px 40px;max-width:640px;margin:auto"><div class="brand">PAUSED</div><div class="tagline">THE TIMER IS STOPPED</div>
    <div class="row-btns" style="margin-top:22px"><button class="btn primary" id="resume">RESUME</button><button class="btn" id="restart">RESTART RUN</button><button class="btn" id="quit">QUIT TO MENU</button></div>
    <div class="keys" style="margin-top:22px"><kbd>WASD</kbd>Move<kbd>RMB</kbd>Aim · <kbd>V</kbd> zoom<kbd>R</kbd>Reload<kbd>C</kbd>Crouch under scanners</div></div>`);
  $("#resume").onclick = () => { const c = document.getElementById("c"); c.focus(); send("SetControllerMode", state.controller ? "1" : "0"); if (!state.controller) c.requestPointerLock?.(); else hideScreen(); send("ResumeFromWeb"); };
  setTimeout(() => $("#resume")?.focus({ preventScroll: true }), 0);   // the screen hides on pointerlockchange
  $("#restart").onclick = startRun;
  $("#quit").onclick = () => { send("QuitToMenuFromWeb"); showMenu(); };
}

// ======================================================================= result + registration
const REG_STEPS = ["Neon Trace generated from your run", "Await your authorization", "Submitted to Monad", "Confirmed from the actual receipt", "Visible in the Hall of Fame"];
function regSteps(done, active, bad = -1, notes = {}) {
  return REG_STEPS.map((t, i) => `<div class="s ${i < done ? "ok" : i === bad ? "bad" : i === active ? "on" : ""}"><i></i>0${i + 1} ${t}<em>${esc(notes[i] || "")}</em></div>`).join("");
}
// Local run history (newest first, 12 max): the result exists even if registration never happens.
const loadRuns = () => { try { return JSON.parse(localStorage.getItem("np.runs") || "[]"); } catch { return []; } };
const saveRuns = (r) => { try { localStorage.setItem("np.runs", JSON.stringify(r.slice(0, 12))); } catch { } };
function rememberRun(e) {
  if (!e.art) return;
  const runs = loadRuns(); e.localAt = Date.now();
  runs.unshift({ at: e.localAt, timeMs: e.timeMs, rules: e.rules, route: e.route, art: e.art, success: !!e.success, challengeOf: e.challengeOf || 0, tags: e.tags, rivalsTagged: e.rivalsTagged, name: state.name || "Runner", registeredId: 0 });
  saveRuns(runs);
}
function markRegistered(localAt, id) { const runs = loadRuns(); const r = runs.find(x => x.at === localAt); if (r) { r.registeredId = id; saveRuns(runs); } }

function runStates() {
  const e = state.lastRun || {}, r = state.registered, saved = !!(e.art && e.localAt);
  const reg = r ? `REGISTERED · ${esc(CFG.network.toUpperCase())} #${r.id}` : state.registering ? "REGISTERING…" : "NOT REGISTERED";
  const mint = state.minted ? "COLLECTIBLE MINTED" : r && r.wallet ? "COLLECTIBLE · MINT IN THE VAULT" : "NO COLLECTIBLE";
  return `<span class="st ${saved ? "ok" : ""}">${saved ? "SAVED ON THIS DEVICE" : "NOT SAVED"}</span><span class="st ${r ? "ok" : state.registering ? "on" : ""}">${reg}</span><span class="st ${state.minted ? "ok" : ""}">${mint}</span>`;
}
function paintStates() { const el = $("#runStates"); if (el) el.innerHTML = runStates(); }

function showResult(e) {
  state.ended = true; state.running = false; state.lastRun = e; state.minted = false; state.registering = false;
  rememberRun(e);
  hideMenu();
  document.exitPointerLock?.();
  $("#hud").classList.remove("on");
  const ok = e.success && e.art;
  const beat = state.challengeTime ? (e.timeMs < state.challengeTime ? `BEAT TRACE #${state.challengeOf} BY ${fmtFull(state.challengeTime - e.timeMs)}` : `${fmtFull(e.timeMs - state.challengeTime)} SLOWER THAN TRACE #${state.challengeOf}`) : "";
  const linked = !!(state.wallet && state.linkWallet);
  screen(`<div class="result">
    <div class="art glass"><div style="width:100%"><div class="kicker" style="margin-bottom:10px">YOUR MARK · RUN ARTWORK</div><canvas id="art"></canvas>
      <div style="font-size:12px;color:var(--muted);margin-top:8px"><b>Line:</b> where you moved. <b>Splashes:</b> where paint hit. <b>Rings:</b> tags and relay events. <b>Diamond:</b> the core. The lower line is your bike ride. This is a stylised run diagram, not a floor plan.</div></div></div>
    <div class="info glass">
      <div class="kicker">${ok ? "EXTRACTION COMPLETE" : "RUN FAILED"}</div>
      <h1>${ok ? "You escaped." : "The run failed."}</h1>
      ${ok ? `<div class="lede">Your run, turned into a collectible image.</div>` : ""}
      <div class="big">${fmtFull(e.timeMs)}</div>
      ${beat ? `<div style="color:var(--pink);letter-spacing:.16em;font-size:12px;font-weight:800">${beat}</div>` : ""}
      <div class="states" id="runStates">${runStates()}</div>
      <div class="row-btns main"><button class="btn primary" id="chal">CHALLENGE A FRIEND</button><button class="btn" id="dl">SAVE ART</button><button class="btn" id="x">SHARE ON X</button><button class="m-link" id="toVault">VISIT THE VAULT</button></div>
      <div class="status" id="chalStatus"></div>
      <details class="details" id="details"><summary>DETAILS · RECORD · SHARING</summary>
        <div class="kv">
          <span>CREATOR</span><span>${esc(state.name || "Guest runner")}${linked ? ` · ${short(state.wallet)}` : " · guest (not mintable)"}</span>
          <span>RUN TIME</span><span class="mono">${fmtFull(e.elapsedMs)} + ${fmtFull(e.penaltyMs)} penalty (${e.tags} tag-out${e.tags === 1 ? "" : "s"})</span>
          <span>RIVALS + DRONES</span><span class="mono">${e.rivalsTagged} tagged${e.drones ? ` · ${e.drones} drone${e.drones === 1 ? "" : "s"} shut down` : ""}</span>
          <span>RULES</span><span class="mono">v${e.rules ?? 3} · ${e.route === "full" ? "full adventure" : "featured route"} · equal equipment</span>
          <span>RECORD</span><span id="monadStatus">Saved on this device · not registered</span>
          <span>ACHIEVEMENT PAGE</span><span id="chUrl" style="color:var(--muted)">Local link now · verified link after registration</span>
        </div>
        <div class="reg"><div class="kicker">REGISTER THIS RUN · ${esc(CFG.network.toUpperCase())}</div><div class="steps" id="regSteps">${regSteps(ok ? 1 : 0, ok ? 1 : -1)}</div><div class="status" id="regStatus"></div></div>
        <div class="row-btns"><button class="btn" id="reg">REGISTER ONLY</button><button class="btn" id="page">ACHIEVEMENT PAGE</button><button class="btn" id="link">COPY LINK</button></div>
      </details>
      <div class="m-quiet"><button class="m-link" id="retry">RETRY RUN</button><span aria-hidden="true">·</span><button class="m-link" id="menu">MAIN MENU</button></div>
    </div></div>`);
  draw();
  $("#retry").onclick = startRun; $("#menu").onclick = () => { send("QuitToMenuFromWeb"); showMenu(); };
  $("#toVault").onclick = () => { hideScreen(); enterVault(); };
  $("#dl").onclick = download;
  const reg = $("#reg"), link = $("#link"), x = $("#x"), page = $("#page"), st = $("#regStatus"), chal = $("#chal"), cst = $("#chalStatus");
  // shareable achievement page: verified (?id) once registered, otherwise a self-contained local link (labelled unverified)
  const shareUrl = () => state.registered ? registeredTraceUrl(state.registered.id) : localTraceUrl({ name: state.name || "Runner", timeMs: e.timeMs, art: e.art, shards: e.shards, tags: e.tags, rivalsTagged: e.rivalsTagged, drones: e.drones, penaltyMs: e.penaltyMs });
  const shareText = () => state.registered ? `I stole the Core in ${fmtFull(e.timeMs)}. My run #${state.registered.id} is recorded on ${CFG.network}. Can you beat it?` : `I stole the Core in ${fmtFull(e.timeMs)} in NEON PROTOCOL. Can you beat my run?`;
  const copy = (u, el, okText) => navigator.clipboard?.writeText(u).then(() => { el.innerHTML = `${okText}<br><span class="mono">${esc(u)}</span>`; }).catch(() => { el.innerHTML = `Copy this link:<br><span class="mono">${esc(u)}</span>`; }) ?? (el.innerHTML = `Copy this link:<br><span class="mono">${esc(u)}</span>`);
  page.onclick = () => window.open(shareUrl(), "_blank");
  link.onclick = () => copy(shareUrl(), $("#regStatus"), "Link copied.");
  x.onclick = () => window.open(xIntent(shareText(), shareUrl()), "_blank");
  page.disabled = link.disabled = x.disabled = !e.art;
  // CHALLENGE A FRIEND: a challenge points at a registered record (same rules for both players). If the run isn't
  // registered yet, this one action registers it first and says so; on failure the local run is kept and retry offered.
  chal.onclick = async () => {
    if (state.registered) { copy(challengeUrl(), cst, "Challenge link copied. Your friend plays the same route and rules; their answer is recorded against yours."); return; }
    cst.className = "status"; cst.textContent = "Saving your challenge on Monad testnet… You can still save your artwork while it confirms.";
    $("#details").open = true;
    const okReg = await register();
    if (okReg) copy(challengeUrl(), cst, "Registered. Challenge link copied. Your friend plays the same route and rules; their answer is recorded against yours.");
    else { cst.className = "status warn"; cst.textContent = "Not registered, so no challenge link yet. Your run and artwork are still saved on this device. Press CHALLENGE A FRIEND to retry."; }
  };
  if (!ok) { reg.disabled = chal.disabled = true; st.textContent = "Only completed extractions can be registered."; cst.textContent = "Only completed runs can be sent as challenges."; $("#dl").disabled = !e.art; return; }
  if (!CFG.relay) { reg.disabled = chal.disabled = true; st.className = cst.className = "status warn"; st.textContent = "Registration is not enabled in this build (the registry is not deployed yet). Your run and artwork stay saved on this device."; cst.textContent = "Challenges need a registered run; registration isn't enabled in this build yet. SAVE ART works now."; }
  else if (!state.session) { reg.disabled = chal.disabled = true; st.className = cst.className = "status warn"; st.textContent = "No server session for this run (offline?), so it can't be registered. It stays saved on this device."; cst.textContent = st.textContent; }
  else st.textContent = "Relay-attested record: the game relay pays the gas on " + CFG.network + ". Nothing is sent until you press CHALLENGE A FRIEND or REGISTER ONLY." + (linked ? " Linked to your wallet: you can mint it later in the Vault." : " Guest record: it can be shared and challenged, not minted.");
  reg.onclick = register;
}

function draw() {
  const e = state.lastRun; if (!e?.art) return;
  const r = state.registered;
  renderSignature($("#art"), hexToBytes(e.art), { id: r?.id, name: state.name || "RUNNER", status: r ? `REGISTERED · ${CFG.network.toUpperCase()}` : "LOCAL RUN · NOT REGISTERED", hash: r?.artHash });
}
const challengeUrl = () => `${CFG.challengeBase || CFG.site}?c=${state.registered?.id}`;

async function register() {
  if (state.scripted) return; // Automated QA runs never register a public result.
  const btn = $("#reg"), st = $("#regStatus"), steps = $("#regSteps"), e = state.lastRun;
  if (state.registered) return true;
  btn.disabled = true; st.className = "status"; state.registering = true; paintStates();
  const notes = {};
  try {
    let wallet, walletSig;
    if (state.wallet && state.linkWallet) {
      steps.innerHTML = regSteps(1, 1, -1, { 1: "sign in your wallet (no gas)" });
      const msg = `NEON PROTOCOL\nLink this run to my wallet.\nSession: ${state.session.session}\nWallet: ${state.wallet.toLowerCase()}\nNo transaction, no gas.`;
      walletSig = await window.ethereum.request({ method: "personal_sign", params: [msg, state.wallet] }); wallet = state.wallet;
    }
    notes[1] = "authorized"; steps.innerHTML = regSteps(2, 2, -1, notes);
    st.textContent = "Submitting to the relay…";
    const t0 = performance.now();
    const res = await fetch(`${CFG.relay}/submit`, { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ token: state.session.token, name: state.name || "Runner", timeMs: e.timeMs, art: e.art, guestId: localStorage.getItem("np.guest") || newGuest(), wallet, walletSig }) });
    const j = await res.json();
    const wall = Math.round(performance.now() - t0);
    if (!res.ok || j.error) throw new Error(j.error || res.statusText);
    if (j.status !== "success") { steps.innerHTML = regSteps(3, -1, 3, notes); throw new Error("transaction reverted (receipt status: " + j.status + ")"); }
    state.registered = { id: j.id, hash: j.hash, block: j.block, wallet: j.wallet || wallet || null }; state.registering = false; paintStates();
    if (state.lastRun?.localAt) markRegistered(state.lastRun.localAt, j.id);
    notes[2] = short(j.hash); notes[3] = `block ${j.block} · ${j.ms} ms`;
    steps.innerHTML = regSteps(4, 4, -1, notes);
    st.className = "status ok";
    st.innerHTML = `Measured: receipt confirmed <b>${j.ms} ms</b> after the relay sent the transaction; <b>${wall} ms</b> end-to-end from your click. <a href="${CFG.explorer}/tx/${j.hash}" target="_blank" rel="noopener">View on explorer ↗</a>`;
    $("#monadStatus").innerHTML = `<span style="color:var(--green)">Registered · Trace #${j.id}</span> on ${esc(CFG.network)} · ${j.wallet || wallet ? "wallet-linked (mintable in the Vault)" : "relay-attested guest record (not mintable)"}`;
    $("#chUrl").innerHTML = `<a class="mono" style="font-size:12px;color:var(--cyan)" target="_blank" href="${esc(registeredTraceUrl(state.registered.id))}">${esc(registeredTraceUrl(state.registered.id))}</a>`;
    $("#link").disabled = $("#x").disabled = false;
    draw();
    // 05: confirm it is readable back from the public registry (what other players will see)
    for (let i = 0; i < 6; i++) {
      await new Promise(r => setTimeout(r, 1500));
      const g = await loadRegistry();
      if (g.ok && g.items.some(x => x.id === j.id)) { notes[4] = "indexed"; steps.innerHTML = regSteps(5, -1, -1, notes); return true; }
    }
    notes[4] = "not visible yet — retry later"; steps.innerHTML = regSteps(4, 4, -1, notes);
    return true;
  } catch (err) {
    st.className = "status warn"; st.textContent = "NOT REGISTERED · " + err.message + (err.code === 4001 ? " (you cancelled the signature)" : "") + " · your run is still saved on this device.";
    btn.disabled = false; state.registering = false; paintStates();
    return false;
  }
}
function newGuest() { const g = crypto.randomUUID(); localStorage.setItem("np.guest", g); return g; }
function download() {
  const e = state.lastRun; if (!e?.art) return;
  const c = document.createElement("canvas");
  renderSignature(c, hexToBytes(e.art), { id: state.registered?.id, name: state.name || "RUNNER", status: state.registered ? `REGISTERED · ${CFG.network.toUpperCase()}` : "LOCAL RUN · NOT REGISTERED" });
  c.toBlob(b => { const a = document.createElement("a"); a.href = URL.createObjectURL(b); a.download = `neon-trace-${state.registered?.id || "local"}.png`; a.click(); });
}

// ======================================================================= HUD
const RELAY_NAMES = ["RELAY A · EAST CATWALK", "RELAY B · STEALTH TRENCH", "RELAY C · VAULT LOCK"];
const MK_LABEL = { relay: "RELAY", core: "CORE", gate: "EXIT" };
let announceT = 0;
function bikeHud(e) {
  $("#bKmh").textContent = e.kmh; $("#bBeat").textContent = e.beat; $("#bTime").firstChild.textContent = fmt(e.timeMs) + " · "; $("#bRemain").textContent = e.remain;
  $("#bShards").textContent = `${e.shards} / ${e.shardsTotal}`;
  const need = Math.max(0, e.required - e.shards);
  $("#bReq").textContent = e.required === 0 ? "BONUS · SHARDS SHAPE YOUR ARTWORK" : need ? `${need} MORE NEEDED TO OPEN THE GATE` : "CORE STABLE — GATE WILL OPEN"; $("#bReq").classList.toggle("ok", !need);
  const bo = $("#bObj"); if (bo) bo.textContent = e.required === 0 ? "REACH THE EXIT" : "STABILIZE THE CORE: COLLECT ENERGY SHARDS";
  $("#bBoost").style.width = Math.round(e.boost * 100) + "%"; $("#bDodge").style.width = Math.round(e.dodge * 100) + "%";
  $("#bAmmo").textContent = e.ammo; $("#bMag").textContent = "/ " + e.mag;
  $("#bReload").style.width = (e.reload >= 0 ? e.reload * 100 : 0) + "%";
  $("#bShields").innerHTML = Array.from({ length: e.maxShields }, (_, i) => `<i class="${i < e.shields ? "on" : ""}"></i>`).join("");
}
function hud(e) {
  state.hud = e;
  const ascending = e.phase === "Ascension";
  $("#ascend").classList.toggle("np-hidden", !ascending);
  if (ascending) {
    document.querySelectorAll(".h-objbar,.h-net,.h-tags,.hud-ammo,#markers,#bots,.xh,.h-hint,#bikeHud").forEach(el => el.style.display = "none");
    $("#ascBar").style.width = Math.round((e.ascend || 0) * 100) + "%"; $("#ascBlocks").textContent = e.ascBlocks || 0; return;
  }
  document.querySelectorAll(".h-objbar,.h-net,.h-tags,.h-hint").forEach(el => el.style.display = "");
  const riding = e.phase === "FinalityRun";
  $("#bikeHud").classList.toggle("np-hidden", !riding);
  // the ride has its own HUD: on-foot objective, hints and detection tags must not linger over it
  document.querySelectorAll(".h-objbar,.h-hint,#hint,#detect,.h-obj,.h-next,.h-tags,.hud-ammo,#markers,#bots,.xh").forEach(el => el.style.display = riding ? "none" : "");
  if (riding) { if (e.announce) { const a = $("#announce"); a.textContent = e.announce; a.classList.add("show"); clearTimeout(announceT); announceT = setTimeout(() => a.classList.remove("show"), 2400); } return; }
  const rs = e.rs || [], pursuit = e.phase === "Pursuit", unlocked = e.phase === "CoreUnlocked";
  const featured = e.route !== "full";
  let steps;
  if (featured) {
    // one current goal with its own progress: PAD n/3 -> CORE (relay) -> TAKE THE CORE -> REACH THE EXIT
    const irisN = e.iris || 0, relayDone = !!rs[0];
    const nearRelay = (e.markers || []).some(m => m.k === "relay" && m.d < 9) || irisN > 0;
    steps = [{ t: relayDone ? "RELAY ONLINE" : irisN >= 3 ? "HIT THE CORE" : nearRelay ? `PAINT THE LIT PAD · ${irisN + 1} / 3` : "POWER THE IRIS", done: relayDone, k: "relay" },
             { t: `TAG THE DEFENDERS · ${e.defenders || 0} / ${e.defenderTotal || 3}`, done: !e.defenderTotal || e.defenders >= e.defenderTotal, k: "combat" },
             { t: "GO THROUGH THE CORE DOOR", done: !!e.core || pursuit, k: "core" },
             { t: "REACH THE BLUE GATE", done: e.phase === "Extracted", k: "gate" }];
  } else {
    steps = RELAY_NAMES.map((n, i) => ({ t: i === 2 && e.lockTotal && !rs[2] ? `${n} — HIT THE MOVING NODES ${e.lock || 0}/${e.lockTotal}` : `PAINT ${n}`, done: !!rs[i], k: "relay" }));
    steps.push({ t: "TAKE THE CORE FROM THE VAULT DAIS", done: !!e.core || pursuit, k: "core" });
    steps.push({ t: "ESCAPE THROUGH THE FINALITY GATE", done: e.phase === "Extracted", k: "gate" });
  }
  const cur = steps.findIndex(s => !s.done);
  $("#chapter").textContent = pursuit ? "CORE SECURED · REACH THE BLUE GATE" : unlocked ? "IRIS ONLINE · REACH THE CORE" : featured ? "STEAL THE CORE. GET OUT." : "CHAPTER I — THE FRACTURE";
  $("#obStep").textContent = cur < 0 ? "✓" : featured ? "" : `${cur + 1}/${steps.length}`;
  $("#obStep").style.display = featured ? "none" : "";
  const curK = cur < 0 ? null : steps[cur].k;
  let near = null;
  if (e.markers) for (const m of e.markers) if (m.k === curK && (!near || m.d < near.d)) near = m;
  $("#nextT").textContent = cur < 0 ? "EXTRACTED" : steps[cur].t;
  $("#obDist").textContent = near ? `${near.d} m` : "";
  if (e.timeMs != null) $("#time").textContent = fmt(e.timeMs);
  // lockdown banner with the real remaining seconds
  $("#lockdown").classList.toggle("np-hidden", !e.lockdown);
  if (e.lockdown) $("#lockTxt").textContent = `SHUTTERS CLOSED ${Math.ceil(e.lockdown)} s · CREWS CALLED TO YOUR POSITION · VAULT LOCK PROGRESS RESET`;
  // one contextual hint, most urgent first
  const hint = e.tagged ? "" : e.lockdown ? "BREAK LINE OF SIGHT — HIDE UNTIL THE SHUTTERS OPEN"
    : e.detected ? "YOU'VE BEEN SPOTTED — <kbd>C</kbd> CROUCH BEHIND COVER OR FIGHT BACK"
    : (e.ammo === 0 && e.reload < 0) ? "<kbd>R</kbd> RELOAD THE PAINT CANISTER"
    : curK === "combat" ? "BREAK SIGHT BEHIND THE REACTOR · AIM, BURST, RELOAD"
    : (!featured && e.inVault && !pursuit && !e.core) ? "RED SCANNERS TRIGGER LOCKDOWN — <kbd>C</kbd> CROUCH UNDER THE HIGH BEAM · <kbd>SPACE</kbd> JUMP THE LOW ONE"
    : curK === "relay" && near && near.d < 18 && featured ? "<kbd>RMB</kbd> AIM · <kbd>LMB</kbd> ONE SHOT ON THE LIT PAD"
    : curK === "relay" && featured ? "FOLLOW THE AMBER ARROWS · <kbd>C</kbd> CROUCH BEHIND COVER"
    : curK === "relay" && near && near.d < 18 ? "<kbd>RMB</kbd> AIM · <kbd>LMB</kbd> PAINT THE RELAY UNTIL IT CHARGES"
    : curK === "core" && near && near.d < 8 ? "WALK ONTO THE DAIS TO TAKE THE CORE"
    : pursuit ? "<kbd>SHIFT</kbd> SPRINT · FOLLOW THE BLUE GATE MARKER · SHOOT OR EVADE THE RESPONSE TEAM" : "";
  const hb = $("#hint"); if (hb.dataset.h !== hint) { hb.dataset.h = hint; hb.innerHTML = hint; hb.classList.toggle("on", !!hint); }
  if (e.ammo != null) {
    $("#ammo").textContent = e.ammo; $("#mag").textContent = "/ " + e.mag;
    const rl = e.reload >= 0, card = $("#wcard");
    $("#reload").firstElementChild.style.width = (rl ? e.reload * 100 : 0) + "%";
    $("#wstate").textContent = rl ? `${e.wstate} ${Math.round(e.reload * 100)}% · NO FIRING` : e.wstate;
    $("#wprompt").textContent = rl ? "RELOADING" : "R RELOAD";
    $("#pfill").style.width = Math.round((e.pressure != null ? e.pressure : 1) * 100) + "%";
    card.classList.toggle("reloading", rl); card.classList.toggle("low", !rl && e.low == 1); card.classList.toggle("empty", !rl && e.ammo == 0);
    const segs = $("#segs"); if (segs.childElementCount != e.mag) { segs.innerHTML = ""; for (let i = 0; i < e.mag; i++) segs.appendChild(document.createElement("i")); }
    for (let i = 0; i < segs.children.length; i++) segs.children[i].classList.toggle("on", i < e.ammo);
  }
  const H = e.hitsToTag || 3, hits = e.hits || 0, pips = $("#pips");
  if (pips.children.length !== H) pips.innerHTML = Array.from({ length: H }, () => `<div class="tag-pip"></div>`).join("");
  [...pips.children].forEach((p, i) => p.classList.toggle("hit", i < hits));
  $("#tagged").classList.toggle("np-hidden", !e.tagged);
  $("#detect").classList.toggle("np-hidden", !e.detected);
  const arc = $("#hitarc");
  if (e.hitDir != null) { arc.style.opacity = Math.max(0, 1 - e.hitAge); arc.style.transform = `rotate(${e.hitDir}deg)`; } else arc.style.opacity = 0;
  if (e.markers) {
    const box = $("#markers"), W = innerWidth, Hh = innerHeight;
    while (box.children.length < e.markers.length) box.insertAdjacentHTML("beforeend", `<div class="mk"><b></b><i></i><span></span></div>`);
    [...box.children].forEach((el, i) => {
      const m = e.markers[i]; if (!m || m !== near || m.d < 7) { el.style.display = "none"; return; }   // close up the target itself is the marker
      let x = m.x, y = 1 - m.y; const behind = m.z < 0;
      if (behind) { x = 1 - x; y = 0.92; }
      const edge = behind || x < 0.04 || x > 0.96 || y < 0.1 || y > 0.9;
      x = Math.min(0.96, Math.max(0.04, x)); y = Math.min(0.9, Math.max(0.1, y));
      const ob = document.querySelector(".h-objbar")?.getBoundingClientRect();   // never slide under the objective bar
      if (ob && ob.height && x * W > ob.left - 40 && x * W < ob.right + 40) y = Math.max(y, (ob.bottom + 34) / Hh);
      el.style.display = ""; el.style.left = (x * W) + "px"; el.style.top = (y * Hh) + "px";
      el.className = "mk " + m.k + (edge ? " edge" : ""); el.querySelector("b").textContent = MK_LABEL[m.k] || ""; el.querySelector("span").textContent = m.d + " m";
    });
  }
  if (e.bots) {
    const box = $("#bots"), W = innerWidth, Hh = innerHeight;
    while (box.children.length < e.bots.length) box.insertAdjacentHTML("beforeend", `<div class="bot"></div>`);
    [...box.children].forEach((el, i) => {
      const b = e.bots[i]; if (!b || b.s !== "!") { el.style.display = "none"; return; }
      el.style.display = ""; el.style.left = (b.x * W) + "px"; el.style.top = ((1 - b.y) * Hh) + "px";
      el.className = "bot" + (b.p ? " p" : ""); el.innerHTML = `<b>AI</b>${esc(b.n)}${b.s ? `<span class="s">${b.s}</span>` : ""}`;
    });
  }
  if (e.announce) { const a = $("#announce"); a.textContent = e.announce; a.classList.add("show"); clearTimeout(announceT); announceT = setTimeout(() => a.classList.remove("show"), 2400); }
}

window.onNeonCrosshair = (gap, aimMode, hitmark, visible) => {
  const xh = $("#xh");
  xh.style.setProperty("--gap", gap.toFixed(1) + "px");
  xh.classList.toggle("zoom", aimMode === 2);
  xh.style.opacity = visible ? 1 : 0;
  const hm = $("#hm"); hm.style.opacity = hitmark; hm.style.transform = `rotate(45deg) scale(${1 + (1 - hitmark) * 0.4})`;
};

// In-world community art: the latest registered trace (or an honest empty state) goes onto the vault wall.
async function featuredArt() {
  const c = document.createElement("canvas");
  let payload = null;
  if (CFG.relay) {
    try {
      const g = await (await fetch(`${CFG.relay}/gallery?n=1`)).json();
      if (g.items && g.items.length) {
        const t = await (await fetch(`${CFG.relay}/trace/${g.items[0].id}`)).json();
        renderSignature(c, hexToBytes(t.art), { id: g.items[0].id, name: t.name, status: "FEATURED REGISTERED TRACE", hash: t.artHash }, { scale: 0.5 });
        payload = { id: g.items[0].id, name: t.name, label: "FEATURED REGISTERED TRACE" };
      }
    } catch (e) { /* offline: empty state */ }
  }
  if (!payload) {
    c.width = 600; c.height = 600; const x = c.getContext("2d");
    x.fillStyle = "#04030a"; x.fillRect(0, 0, 600, 600); x.strokeStyle = "rgba(160,140,255,.35)"; x.strokeRect(20, 20, 560, 560);
    x.fillStyle = "rgba(232,230,255,.85)"; x.textAlign = "center"; x.font = '600 30px "Space Grotesk", sans-serif';
    x.fillText("NO REGISTERED TRACES YET", 300, 290); x.font = '500 18px "Space Grotesk", sans-serif'; x.fillStyle = "rgba(51,233,241,.9)";
    x.fillText("EXTRACT AND REGISTER TO APPEAR HERE", 300, 330);
    payload = { id: 0, name: "", label: "EMPTY" };
  }
  payload.png = c.toDataURL("image/png");
  send("SetFeaturedArt", JSON.stringify(payload));
}

// ======================================================================= THE VAULT (walkable room)
// Gate states shown to the player, one per real condition. Only "owner" opens the door.
const GATE = { unconfigured: "COLLECTIBLE NOT DEPLOYED YET", disconnected: "CONNECT A WALLET TO CHECK", wrong: "WRONG NETWORK", checking: "CHECKING OWNERSHIP…", nonowner: "NO NEON MARK IN THIS WALLET", owner: "NEON MARK VERIFIED — WELCOME", rpc: "COULD NOT READ THE CHAIN — RETRY" };
const vault = { on: false, slots: [], gate: "disconnected", gateAt: 0, near: -1 };
const MARK = CFG.mark || null;   // { address, chainId, rpc } from config.js once a collectible exists
const sel = { balanceOf: "0x70a08231", ownerOf: "0x6352211e", mint: "0xa71bbebe" };
const pad32 = (h) => h.replace(/^0x/, "").padStart(64, "0");
async function markCall(data) { return rpc(MARK.rpc, "eth_call", [{ to: MARK.address, data }, "latest"]); }

window.onVaultDrawer = (box) => {
  if (!box) return;
  box.innerHTML = `<button class="btn primary" id="enterVault">ENTER THE VAULT</button><p style="color:var(--muted);font-size:12px">Walk the exhibition: your latest run in the centre, registered runs on the walls. No weapons, no timer.</p>`;
  $("#enterVault").onclick = enterVault;
};
function vaultArt(hexArt, meta) {
  const c = document.createElement("canvas");
  if (hexArt) renderSignature(c, hexToBytes(hexArt), meta, { scale: 0.5 });
  return c.toDataURL("image/png");
}
async function enterVault() {
  hideMenu(); hideScreen();
  vault.on = true; vault.near = -1;
  $("#vaultHud").classList.remove("np-hidden");
  const canvas = document.getElementById("c"); canvas.focus(); send("SetControllerMode", state.controller ? "1" : "0"); if (!state.controller) canvas.requestPointerLock?.();
  send("EnterVaultFromWeb");
  // slot 0: the player's own latest run (registered or not); slots 1..6: registered runs, honest empties otherwise
  const mine = loadRuns()[0];
  vault.slots = [mine ? { source: "local", id: mine.registeredId || 0, name: mine.name, timeMs: mine.timeMs, rules: mine.rules, art: mine.art, attested: null } : null];
  if (mine && mine.registeredId && CFG.relay) {   // registered: take owner/provenance/hash from the public record (needed for MINT)
    try { const t = await (await fetch(`${CFG.relay}/trace/${mine.registeredId}`)).json(); if (t && t.player) Object.assign(vault.slots[0], { player: t.player, attested: t.attested, artHash: t.artHash }); } catch { }
  }
  const reg = await loadRegistry();
  const items = reg.ok ? reg.items.slice(0, 6) : [];
  for (const it of items) {
    let t = null; try { t = await (await fetch(`${CFG.relay}/trace/${it.id}`)).json(); } catch { }
    vault.slots.push(t && t.art ? { source: "registry", id: it.id, name: t.name, timeMs: t.timeMs, rules: t.rulesVersion, art: t.art, attested: t.attested, player: t.player, artHash: t.artHash } : null);
  }
  while (vault.slots.length < 7) vault.slots.push(null);
  vault.slots.forEach((r, i) => {
    const label = r ? `${r.name} · ${fmtFull(r.timeMs)}${r.id ? " · #" + r.id : " · NOT REGISTERED"}` : i === 0 ? "PLAY A RUN TO HANG YOUR ART HERE" : reg.ok ? "EMPTY SLOT" : "RECORDS UNAVAILABLE";
    send("SetVaultExhibit", JSON.stringify({ slot: i, png: r ? vaultArt(r.art, { id: r.id, name: r.name, status: r.id ? "REGISTERED" : "LOCAL" }) : "", label }));
  });
  checkGate(true);
}
function leaveVault() {
  vault.on = false; $("#vaultHud").classList.add("np-hidden"); closeInspect();
  send("LeaveVaultFromWeb"); send("QuitToMenuFromWeb"); showMenu();
}
function setGate(st, extra = "") {
  vault.gate = st; vault.gateAt = Date.now();
  send("SetVaultGate", JSON.stringify({ state: st, label: "COLLECTORS WING\n" + GATE[st] }));
  const g = $("#vaultGate"); if (g) g.textContent = GATE[st] + (extra ? " · " + extra : "");
}
// Fresh read on every approach (older than 15 s), and immediately on account / chain change.
async function checkGate(force) {
  if (!force && Date.now() - vault.gateAt < 15000) return;
  if (!MARK || !MARK.address) return setGate("unconfigured");
  const eth = window.ethereum;
  if (!eth) return setGate("disconnected");
  let acct = null, chain = null;
  try { acct = (await eth.request({ method: "eth_accounts" }))[0] || null; chain = parseInt(await eth.request({ method: "eth_chainId" }), 16); } catch { }
  state.wallet = acct; state.walletChain = chain;
  if (!acct) return setGate("disconnected");
  if (chain !== MARK.chainId) return setGate("wrong", `switch to chain ${MARK.chainId}`);
  setGate("checking");
  try { const r = await markCall(sel.balanceOf + pad32(acct)); setGate(BigInt(r) > 0n ? "owner" : "nonowner"); }
  catch (e) { setGate("rpc", e.message.slice(0, 60)); }
}
if (window.ethereum?.on) { window.ethereum.on("accountsChanged", () => vault.on && checkGate(true)); window.ethereum.on("chainChanged", () => vault.on && checkGate(true)); }

async function inspect(slot) {
  const r = vault.slots[slot]; if (!r) return;
  // R4: show the panel BEFORE releasing the mouse; the vault leaves on "pointer lock lost with no panel open", and the
  // ownership read below is async, so the old order dropped wallet users out of the Vault when they inspected.
  { const b = $("#inspect"); b.classList.remove("np-hidden"); b.innerHTML = `<div class="glass insp"><div class="insp-info"><div class="kicker">READING THE RECORD…</div></div></div>`; }
  send("PauseFromWeb"); document.exitPointerLock?.();
  let minted = "COLLECTIBLE NOT DEPLOYED", canMint = false, why = "";
  const guest = !r.player || /^0x0{40}$/i.test(r.player);
  if (r.id && MARK && MARK.address) {
    try { const o = await markCall(sel.ownerOf + pad32("0x" + r.id.toString(16))); minted = "MINTED · owner " + short("0x" + o.slice(-40)); }
    catch (e) {
      minted = /revert|execution/i.test(e.message) ? "NOT MINTED" : "MINT STATUS UNAVAILABLE";
      const mine = state.wallet && r.player && state.wallet.toLowerCase() === r.player.toLowerCase();
      canMint = minted === "NOT MINTED" && mine;
      why = minted !== "NOT MINTED" ? "" : guest ? "Guest run: not linked to a wallet, so it can't be minted." : !state.wallet ? "Connect the wallet that registered this run to mint it." : !mine ? "Only the wallet that registered this run can mint it." : "";
    }
  } else if (!r.id) { minted = "NOT MINTED"; why = "Register the run first (CHALLENGE A FRIEND on the result screen)."; }
  const box = $("#inspect"); box.classList.remove("np-hidden");
  box.innerHTML = `<div class="glass insp"><canvas id="inspArt"></canvas><div class="insp-info">
    <div class="kicker">${r.source === "local" ? "YOUR RUN" : "REGISTERED RUN"}</div><h3>${esc(r.name)}</h3><div class="mono big">${fmtFull(r.timeMs)}</div>
    <div class="row"><span>RULES</span><span>v${r.rules} · ${r.rules === 1 ? "full adventure" : "featured route"}</span></div>
    <div class="row"><span>SAVED</span><span>${r.source === "local" ? "YES · on this device" : "—"}</span></div>
    <div class="row"><span>REGISTERED</span><span>${r.id ? `#${r.id} · ${r.attested === false ? "self-submitted" : "relay-attested"}` : "NO · saved in this browser only"}</span></div>
    <div class="row"><span>CREATOR WALLET</span><span class="mono">${r.player && !guest ? esc(short(r.player)) : "guest (none)"}</span></div>
    <div class="row"><span>COLLECTIBLE</span><span>${esc(minted)}</span></div>
    ${why ? `<div class="status">${esc(why)}</div>` : ""}
    ${MARK && MARK.address && r.id ? `<div class="note">The collectible is a NEON MARK token for this registered run. Its token image is a badge derived from the art hash; the full artwork above is rendered from the run record.</div>` : ""}
    <div class="row-btns">${r.id ? `<button class="btn primary" id="iChal">CHALLENGE THIS RUN</button><button class="btn" id="iLink">COPY SHARE LINK</button>` : ""}${canMint ? `<button class="btn cyan" id="iMint">MINT COLLECTIBLE</button>` : ""}<button class="btn" id="iClose">BACK TO THE VAULT</button></div>
    <div class="status" id="iStatus"></div></div></div>`;
  renderSignature($("#inspArt"), hexToBytes(r.art), { id: r.id, name: r.name, status: r.id ? "REGISTERED" : "LOCAL", hash: r.artHash });
  $("#iClose").onclick = closeInspect;
  if (r.id) {
    $("#iChal").onclick = () => { closeInspect(); vault.on = false; $("#vaultHud").classList.add("np-hidden"); send("LeaveVaultFromWeb"); state.challengeOf = r.id; state.challengeTime = r.timeMs; state.route = r.rules === 1 ? "full" : "featured"; startRun(); };
    $("#iLink").onclick = async () => { const u = registeredTraceUrl(r.id); try { await navigator.clipboard.writeText(u); $("#iStatus").textContent = "Copied: " + u; } catch { $("#iStatus").textContent = u; } };
  }
  if (canMint) {
    // state the network fee BEFORE the wallet asks for a signature (estimate from the chain, not a promise)
    const st0 = $("#iStatus"); const data = sel.mint + pad32("0x" + r.id.toString(16));
    Promise.all([rpc(MARK.rpc, "eth_estimateGas", [{ from: state.wallet, to: MARK.address, data }]), rpc(MARK.rpc, "eth_gasPrice", [])])
      .then(([g, p]) => { const fee = Number(BigInt(g) * BigInt(p)) / 1e18; if (st0 && !st0.textContent) st0.textContent = `Optional. Estimated network fee ≈ ${fee < 0.0001 ? fee.toExponential(2) : fee.toFixed(5)} MON (${parseInt(g, 16)} gas), paid by your wallet. Minting gives no gameplay advantage.`; })
      .catch(() => { if (st0 && !st0.textContent) st0.textContent = "Optional. Your wallet pays the network fee (estimate unavailable). Minting gives no gameplay advantage."; });
  }
  if (canMint) $("#iMint").onclick = async () => {
    const st = $("#iStatus"); st.textContent = "Confirm the mint in your wallet (you pay the network fee).";
    try {
      const hash = await window.ethereum.request({ method: "eth_sendTransaction", params: [{ from: state.wallet, to: MARK.address, data: sel.mint + pad32("0x" + r.id.toString(16)) }] });
      st.textContent = "Submitted " + short(hash) + " · waiting for the receipt…";
      const t0 = performance.now(); let rc = null;
      while (!rc && performance.now() - t0 < 120000) { await new Promise(z => setTimeout(z, 1500)); rc = await rpc(MARK.rpc, "eth_getTransactionReceipt", [hash]).catch(() => null); }
      st.textContent = rc ? (rc.status === "0x1" ? `MINTED · receipt after ${((performance.now() - t0) / 1000).toFixed(1)} s on ${CFG.network} (measured)` : "Mint reverted (receipt status 0)") : "No receipt yet — check later";
      if (rc && rc.status === "0x1") { state.minted = true; paintStates(); checkGate(true); }
    } catch (e) { st.textContent = "Mint not sent: " + e.message; }
  };
}
function closeInspect() { const b = $("#inspect"); if (b) { b.classList.add("np-hidden"); b.innerHTML = ""; } if (vault.on) { if (!state.controller) document.getElementById("c").requestPointerLock?.(); send("ResumeFromWeb"); } }
function vaultEvent(e) {
  if (e.type === "vault") {
    vault.near = e.near;
    $("#vaultPrompt").textContent = e.near >= 0 && vault.slots[e.near] ? (state.controller ? "Y / TRIANGLE · INSPECT" : "E · INSPECT") : e.gate ? "" : "";
    $("#vaultPrompt").classList.toggle("np-hidden", !(e.near >= 0 && vault.slots[e.near]));
    if (e.gate) checkGate(false);
  } else if (e.type === "vault_inspect") inspect(e.slot);
}
root.insertAdjacentHTML("beforeend", `<div id="vaultHud" class="np-hidden"><div class="vh-top glass"><b>THE VAULT</b><span id="vaultGate">—</span><button class="m-link" id="vaultLeave">LEAVE · ESC</button></div><div class="vh-prompt np-hidden" id="vaultPrompt"></div></div><div id="inspect" class="np-hidden"></div>`);
$("#vaultLeave").onclick = leaveVault;
document.addEventListener("pointerlockchange", () => { if (!state.controller && vault.on && !document.pointerLockElement && $("#inspect").classList.contains("np-hidden")) leaveVault(); });

window.onNeonGameEvent = (e) => {
  if (e.type === "vault" || e.type === "vault_inspect") { vaultEvent(e); return; }
  if (e.type === "hud") hud(e);
  else if (e.type === "bike") bikeHud(e);
  else if (e.type === "run_end") showResult(e);
  else if (e.type === "warmup" && e.progress >= 1) {
    if (qs.get("test") === "j") {
      // Explicit QA URL runs the existing Unity input script. It has no registration session.
      state.scripted = true; state.running = true; state.session = null;
      hideMenu(); hideScreen(); $("#hud").classList.add("on");
      const label = document.createElement("div");
      label.textContent = "SCRIPTED INPUT · TECHNICAL CHECK · NOT A HUMAN PLAYTEST";
      label.style.cssText = "position:fixed;bottom:8px;left:8px;z-index:9999;padding:6px 10px;background:#080612e8;color:#fff;font:11px monospace;pointer-events:none";
      document.body.appendChild(label);
    } else { setTimeout(showMenu, 300); setTimeout(featuredArt, 1500); }
  }
};
window.__npUI = { state, showMenu, openDrawer, hud, showResult };   // QA hooks (local tests only)
pollBlocks();

// Standard browser Gamepad API: menu navigation and pause. No synthetic score or gameplay events.
let padButtons = [], padNextNav = 0, padHadConnection = false;
function padMenuFrame(now) {
  const pad = Array.from(navigator.getGamepads?.() || []).find(p => p && p.connected && p.mapping === "standard");
  if (!pad) {
    if (padHadConnection && state.controller) { if (vault.on) leaveVault(); else if (state.running && !state.ended) showPause(); }
    padHadConnection = false; padButtons = []; requestAnimationFrame(padMenuFrame); return;
  }
  padHadConnection = true;
  const down = n => !!pad.buttons[n]?.pressed, pressed = n => down(n) && !padButtons[n];
  const overlay = document.querySelector("#np-screen:not(.np-hidden)");
  const menu = document.querySelector("#np-menu:not(.np-hidden)");
  const inspectPanel = document.querySelector("#inspect:not(.np-hidden)");
  const drawer = document.querySelector("#drawer");
  if (pressed(9) && vault.on) {
    if (inspectPanel) closeInspect(); else leaveVault();
  } else if (pressed(9) && state.running && !state.ended) {
    state.controller = true;
    if (document.querySelector("#resume")) document.querySelector("#resume").click(); else showPause();
  } else if (overlay || inspectPanel || drawer || menu) {
    const scope = overlay || inspectPanel || drawer || menu;
    const items = Array.from(scope.querySelectorAll("button:not([disabled]), a[href], input, select, summary"))
      .filter(e => e.getClientRects().length && getComputedStyle(e).visibility !== "hidden" && !e.closest(".np-hidden"));
    const dir = down(13) || down(15) || pad.axes[1] > .6 ? 1 : down(12) || down(14) || pad.axes[1] < -.6 ? -1 : 0;
    if (dir && now >= padNextNav && items.length) {
      state.controller = true; padNextNav = now + 230;
      const i = items.indexOf(document.activeElement); items[(i + dir + items.length) % items.length].focus();
    }
    if (!dir) padNextNav = 0;
    if (pressed(0) && items.length) {
      state.controller = true;
      const chosen = items.includes(document.activeElement) ? document.activeElement : items[0];
      chosen.focus(); chosen.click();
    }
  }
  padButtons = pad.buttons.map(b => b.pressed); requestAnimationFrame(padMenuFrame);
}
requestAnimationFrame(padMenuFrame);
document.addEventListener("keydown", e => {
  if (e.key === "Escape" && state.controller) { if (vault.on) leaveVault(); else if (state.running && !state.ended) showPause(); }
});

// A real pointer action in the menus switches back to mouse mode. Gamepad .click() does not emit pointerdown.
document.addEventListener("pointerdown", () => {
  if (document.querySelector("#np-menu, #np-screen:not(.np-hidden)")) state.controller = false;
});
