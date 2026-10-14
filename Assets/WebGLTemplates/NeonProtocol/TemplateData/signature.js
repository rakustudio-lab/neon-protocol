// NEON SIGNATURE renderer, algorithm v1.
// Input: the canonical run record (the same bytes stored on Monad). Output: artwork on a 2D canvas.
// Deterministic: every random choice is seeded from the record bytes, so the record alone reproduces the art.

export const PALETTES = [
  ["#16f5d6", "#ff2bd6"],   // 0 cyan -> magenta (default)
  ["#b6ff3b", "#19d9ff"],   // 1 acid -> ice
  ["#ffb020", "#8a5cff"],   // 2 amber -> violet
  ["#ff3b6b", "#ffd23b"],   // 3 coral -> gold
];
const KIND = { SURFACE: 0, FLOOR: 1, TARGET: 2, TAG: 3, RELAY: 4, DRONE: 5, PATH: 6, RIDE: 7 };
const MILESTONE = { 1: "RELAY A", 2: "RELAY B", 3: "RELAY C", 4: "CORE", 5: "EXTRACTED", 6: "TAGGED", 7: "CORE DROPPED", 8: "SHARD", 9: "DRONE DOWN", 10: "FINALITY RUN" };
export function summarize(rec) {
  const k = n => rec.points.filter(p => p.kind === n).length, m = n => rec.milestones.filter(x => x.type === n).length;
  return { paint: rec.points.filter(p => p.kind <= 2).length, rivals: k(3), taggedOut: m(6), relays: Math.min(3, m(1) + m(2) + m(3)) || Math.min(3, k(4)), drones: k(5) + m(9), shards: m(8), rode: k(7) > 0 || m(10) > 0, route: k(6) };
}

export function hexToBytes(hex) {
  const h = hex.startsWith("0x") ? hex.slice(2) : hex;
  const out = new Uint8Array(h.length / 2);
  for (let i = 0; i < out.length; i++) out[i] = parseInt(h.substr(i * 2, 2), 16);
  return out;
}

export function decode(bytes) {
  if (!bytes || bytes.length < 10 || bytes[0] !== 1) throw new Error("unsupported signature record");
  const r = { algo: bytes[0], rulesVersion: bytes[1], palette: bytes[2] % PALETTES.length,
    timeMs: ((bytes[3] << 24) >>> 0) + (bytes[4] << 16) + (bytes[5] << 8) + bytes[6], flags: bytes[7], points: [], milestones: [] };
  const n = bytes[8], m = bytes[9];
  let o = 10;
  for (let i = 0; i < n && o + 4 <= bytes.length; i++, o += 4)
    r.points.push({ x: bytes[o] / 255, z: bytes[o + 1] / 255, level: bytes[o + 2] >> 4, kind: bytes[o + 2] & 15, t: bytes[o + 3] / 255 });
  for (let i = 0; i < m && o + 2 <= bytes.length; i++, o += 2) r.milestones.push({ type: bytes[o], t: bytes[o + 1] / 255 });
  return r;
}

// FNV-1a seed + mulberry32 stream
function seedFrom(bytes) { let h = 2166136261 >>> 0; for (const b of bytes) { h ^= b; h = Math.imul(h, 16777619) >>> 0; } return h; }
function rng(seed) { let a = seed >>> 0; return () => { a = (a + 0x6D2B79F5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }

function mix(a, b, t) {
  const pa = [1, 3, 5].map(i => parseInt(a.substr(i, 2), 16)), pb = [1, 3, 5].map(i => parseInt(b.substr(i, 2), 16));
  return `rgb(${pa.map((v, i) => Math.round(v + (pb[i] - v) * t)).join(",")})`;
}
function rgba(color, a) { return color.replace("rgb(", "rgba(").replace(")", `,${a})`); }

function blob(ctx, x, y, r, seed, rand, drip) {
  // Organic splash outline: low harmonics + two thrown "fingers" (same family as the in-game splat shader).
  const s = seed;
  ctx.beginPath();
  for (let i = 0; i <= 64; i++) {
    const a = (i / 64) * Math.PI * 2;
    let w = 1 + 0.1 * Math.sin(3 * a + s * 6.28) + 0.07 * Math.sin(5 * a + s * 19.1) + 0.045 * Math.sin(8 * a + s * 37.7);
    w += 0.3 * Math.pow(Math.max(0, Math.cos(a - s * 41)), 30) + 0.2 * Math.pow(Math.max(0, Math.cos(a - s * 59 - 2.3)), 40);
    const px = x + Math.cos(a) * r * w, py = y + Math.sin(a) * r * w;
    i ? ctx.lineTo(px, py) : ctx.moveTo(px, py);
  }
  ctx.closePath(); ctx.fill();
  const drops = 3 + Math.floor(rand() * 5);
  for (let k = 0; k < drops; k++) {
    const a = rand() * Math.PI * 2, d = r * (1.4 + rand() * 1.4), rr = r * (0.08 + rand() * 0.16);
    ctx.beginPath(); ctx.arc(x + Math.cos(a) * d, y + Math.sin(a) * d, rr, 0, Math.PI * 2); ctx.fill();
  }
  if (drip) {
    const runs = 1 + Math.floor(rand() * 3);
    for (let k = 0; k < runs; k++) {
      const dx = (rand() - 0.5) * r * 1.2, len = r * (0.8 + rand() * 2.2), w = r * 0.12;
      ctx.beginPath(); ctx.moveTo(x + dx - w, y + r * 0.5); ctx.lineTo(x + dx + w, y + r * 0.5);
      ctx.lineTo(x + dx + w * 0.6, y + r * 0.5 + len); ctx.arc(x + dx, y + r * 0.5 + len, w * 0.9, 0, Math.PI); ctx.closePath(); ctx.fill();
    }
  }
}

function hex(ctx, x, y, r) { ctx.beginPath(); for (let i = 0; i <= 6; i++) { const a = Math.PI / 6 + i * Math.PI / 3; i ? ctx.lineTo(x + r * Math.cos(a), y + r * Math.sin(a)) : ctx.moveTo(x + r * Math.cos(a), y + r * Math.sin(a)); } ctx.stroke(); }

function fmtTime(ms) { const m = Math.floor(ms / 60000), s = Math.floor(ms / 1000) % 60, x = ms % 1000; return `${m}:${String(s).padStart(2, "0")}.${String(x).padStart(3, "0")}`; }

/**
 * @param {HTMLCanvasElement} canvas
 * @param {Uint8Array} bytes canonical record
 * @param {{id?:number,name?:string,status?:string,hash?:string,challengeOf?:number}} meta
 * @param {{format?:"square"|"card"}} opt
 */
export function renderSignature(canvas, bytes, meta = {}, opt = {}) {
  const rec = decode(bytes);
  const card = opt.format === "card";
  const W = card ? 1200 : 1200, H = card ? 630 : 1200;
  const scale = opt.scale || 1;   // draw in 1200-unit space, store at W*scale pixels (gallery tiles use ~0.4)
  canvas.width = Math.round(W * scale); canvas.height = Math.round(H * scale);
  const ctx = canvas.getContext("2d");
  ctx.setTransform(scale, 0, 0, scale, 0, 0);
  const rand = rng(seedFrom(bytes));
  const [c0, c1] = PALETTES[rec.palette];

  // art area
  const A = card ? { x: 0, y: 0, w: 630, h: 630 } : { x: 0, y: 0, w: 1200, h: 1200 };
  const bg = ctx.createRadialGradient(A.x + A.w * 0.5, A.y + A.h * 0.45, 40, A.x + A.w * 0.5, A.y + A.h * 0.5, A.w * 0.75);
  bg.addColorStop(0, "#0d0b18"); bg.addColorStop(1, "#020205");
  ctx.fillStyle = "#020205"; ctx.fillRect(0, 0, W, H);
  ctx.fillStyle = bg; ctx.fillRect(A.x, A.y, A.w, A.h);

  // architectural grid
  ctx.save(); ctx.strokeStyle = "rgba(160,140,255,0.06)"; ctx.lineWidth = 1;
  const step = A.w / 16;
  for (let i = 1; i < 16; i++) { ctx.beginPath(); ctx.moveTo(A.x + i * step, A.y); ctx.lineTo(A.x + i * step, A.y + A.h); ctx.stroke(); ctx.beginPath(); ctx.moveTo(A.x, A.y + i * step); ctx.lineTo(A.x + A.w, A.y + i * step); ctx.stroke(); }
  ctx.restore();

  const margin = A.w * 0.11;
  const ridePts0 = rec.points.filter(p => p.kind === KIND.RIDE);
  const hasRide = ridePts0.length > 1;
  // Act I map occupies the upper area; the Act II lightcycle line sweeps across a band underneath
  const mapH = hasRide ? 0.74 : 1;
  const P = p => ({ x: A.x + margin + p.x * (A.w - 2 * margin), y: A.y + margin + (1 - p.z) * (A.h * mapH - 2 * margin) - p.level * A.w * 0.006 });
  const pts = rec.points.filter(p => p.kind !== KIND.RIDE).map(p => ({ ...p, ...P(p) }));
  const route = pts.filter(p => p.kind === KIND.PATH);

  // core glow behind the art (where the vault sits in the map)
  { const g = ctx.createRadialGradient(A.x + A.w * 0.5, A.y + A.h * mapH * 0.32, 10, A.x + A.w * 0.5, A.y + A.h * mapH * 0.32, A.w * 0.42);
    g.addColorStop(0, rgba(mix(c0, c1, 0.5), 0.16)); g.addColorStop(1, "rgba(0,0,0,0)"); ctx.fillStyle = g; ctx.fillRect(A.x, A.y, A.w, A.h); }

  // Act II: lightcycle line (track distance -> x, lateral -> wobble inside the band), speed streaks, shards, drones
  if (hasRide) {
    const by = A.y + A.h * 0.80, bh = A.h * 0.1;
    const R = p => ({ x: A.x + margin * 0.6 + p.x * (A.w - margin * 1.2), y: by + (p.z - 0.5) * bh });
    const rp = ridePts0.map(p => ({ ...p, ...R(p) }));
    ctx.save(); ctx.globalCompositeOperation = "lighter"; ctx.lineCap = "round";
    for (let k = 0; k < 26; k++) { const y = by + (rand() - 0.5) * bh * 1.6, x0 = A.x + rand() * A.w * 0.8, len = A.w * (0.08 + rand() * 0.25); const gr = ctx.createLinearGradient(x0, y, x0 + len, y); gr.addColorStop(0, "rgba(255,255,255,0)"); gr.addColorStop(1, rgba(mix(c1, c0, rand()), 0.35)); ctx.strokeStyle = gr; ctx.lineWidth = 1 + rand() * 2; ctx.beginPath(); ctx.moveTo(x0, y); ctx.lineTo(x0 + len, y); ctx.stroke(); }
    for (const pass of [{ w: A.w * 0.02, a: 0.08, blur: 28 }, { w: A.w * 0.007, a: 0.3, blur: 10 }, { w: A.w * 0.0025, a: 1, blur: 0 }]) {
      ctx.shadowBlur = pass.blur; ctx.lineWidth = pass.w;
      ctx.beginPath(); rp.forEach((p, i) => i ? ctx.lineTo(p.x, p.y) : ctx.moveTo(p.x, p.y));
      const gr = ctx.createLinearGradient(rp[0].x, 0, rp[rp.length - 1].x, 0); gr.addColorStop(0, rgba(mix(c0, c1, 0.6), pass.a)); gr.addColorStop(1, rgba(mix(c0, c1, 1), pass.a));
      ctx.strokeStyle = gr; ctx.shadowColor = c1; ctx.stroke();
    }
    const at = t => { let b = rp[0]; for (const p of rp) if (Math.abs(p.t - t) < Math.abs(b.t - t)) b = p; return b; };
    ctx.shadowBlur = 16;
    for (const m of rec.milestones) {
      if (m.type !== 8 && m.type !== 9) continue;
      const p = at(m.t), r = A.w * 0.012;
      if (m.type === 8) { ctx.fillStyle = "#fff"; ctx.shadowColor = c0; ctx.beginPath(); ctx.moveTo(p.x, p.y - r * 1.6); ctx.lineTo(p.x + r, p.y); ctx.lineTo(p.x, p.y + r * 1.6); ctx.lineTo(p.x - r, p.y); ctx.closePath(); ctx.fill(); }
      else { ctx.strokeStyle = c1; ctx.shadowColor = c1; ctx.lineWidth = 3; ctx.beginPath(); ctx.moveTo(p.x - r, p.y - r); ctx.lineTo(p.x + r, p.y + r); ctx.moveTo(p.x + r, p.y - r); ctx.lineTo(p.x - r, p.y + r); ctx.stroke(); }
    }
    ctx.restore();
    ctx.save(); ctx.font = `600 ${Math.round(A.w * 0.012)}px "Space Grotesk","Segoe UI",sans-serif`; ctx.fillStyle = "rgba(255,255,255,0.55)";
    ctx.fillText("FINALITY RUN", A.x + margin * 0.6, by - bh * 0.95); ctx.restore();
  }

  // route: smoothed luminous trace through the run in time order (the real path when recorded, else the events)
  const line = route.length > 1 ? route : pts;
  if (line.length > 1) {
    ctx.save(); ctx.globalCompositeOperation = "lighter"; ctx.lineCap = "round"; ctx.lineJoin = "round";
    for (const pass of [{ w: A.w * 0.014, a: 0.08, blur: 30 }, { w: A.w * 0.006, a: 0.25, blur: 12 }, { w: A.w * 0.0022, a: 0.9, blur: 0 }]) {
      ctx.shadowBlur = pass.blur;
      for (let i = 0; i < line.length - 1; i++) {
        const p0 = line[Math.max(0, i - 1)], p1 = line[i], p2 = line[i + 1], p3 = line[Math.min(line.length - 1, i + 2)];
        const col = mix(c0, c1, p1.t);
        ctx.strokeStyle = rgba(col, pass.a); ctx.shadowColor = col; ctx.lineWidth = pass.w;
        ctx.beginPath(); ctx.moveTo(p1.x, p1.y);
        ctx.bezierCurveTo(p1.x + (p2.x - p0.x) / 6, p1.y + (p2.y - p0.y) / 6, p2.x - (p3.x - p1.x) / 6, p2.y - (p3.y - p1.y) / 6, p2.x, p2.y);
        ctx.stroke();
      }
    }
    ctx.restore();
  }

  // energy connections between key events
  const keys = pts.filter(p => p.kind >= KIND.TAG && p.kind <= KIND.DRONE);
  ctx.save(); ctx.globalCompositeOperation = "lighter"; ctx.lineWidth = 1;
  for (let i = 0; i < keys.length; i++) for (let j = i + 1; j < keys.length; j++) {
    const d = Math.hypot(keys[i].x - keys[j].x, keys[i].y - keys[j].y);
    if (d < A.w * 0.35) { ctx.strokeStyle = rgba(mix(c0, c1, (keys[i].t + keys[j].t) / 2), 0.18); ctx.beginPath(); ctx.moveTo(keys[i].x, keys[i].y); ctx.lineTo(keys[j].x, keys[j].y); ctx.stroke(); }
  }
  ctx.restore();

  // splashes
  ctx.save(); ctx.globalCompositeOperation = "lighter";
  for (const p of pts) {
    if (p.kind === KIND.PATH) continue;
    const col = mix(c0, c1, p.t);
    const base = A.w * ({ 0: 0.012, 1: 0.014, 2: 0.022, 3: 0.03, 4: 0.026, 5: 0.026 }[p.kind] ?? 0.012);
    const r = base * (0.8 + rand() * 0.5);
    ctx.shadowColor = col; ctx.shadowBlur = r * 1.6;
    ctx.fillStyle = rgba(col, p.kind >= KIND.TARGET ? 0.95 : 0.78);
    if (p.kind === KIND.RELAY) { ctx.strokeStyle = col; ctx.lineWidth = A.w * 0.003; hex(ctx, p.x, p.y, r * 1.3); hex(ctx, p.x, p.y, r * 0.8); }
    else blob(ctx, p.x, p.y, r, rand(), rand, p.kind === KIND.SURFACE);
    if (p.kind === KIND.TAG) { ctx.strokeStyle = rgba(col, 0.9); ctx.lineWidth = A.w * 0.002; ctx.beginPath(); ctx.arc(p.x, p.y, r * 2.1, 0, Math.PI * 2); ctx.stroke(); }
  }
  ctx.restore();

  // milestones along the route
  ctx.save();
  ctx.font = `600 ${Math.round(A.w * 0.013)}px "Space Grotesk", "Segoe UI", sans-serif`; ctx.textAlign = "center";
  for (const m of rec.milestones) {
    if (m.type >= 8) continue;   // Act II marks live on the ride band
    let best = pts[0]; for (const p of pts) if (Math.abs(p.t - m.t) < Math.abs(best.t - m.t)) best = p;
    if (!best) continue;
    const col = mix(c0, c1, m.t);
    ctx.strokeStyle = col; ctx.fillStyle = "rgba(255,255,255,0.85)"; ctx.lineWidth = A.w * 0.0025; ctx.shadowColor = col; ctx.shadowBlur = 14;
    const r = A.w * 0.024;
    ctx.beginPath();
    if (m.type === 4) { ctx.moveTo(best.x, best.y - r * 1.4); ctx.lineTo(best.x + r, best.y); ctx.lineTo(best.x, best.y + r * 1.4); ctx.lineTo(best.x - r, best.y); ctx.closePath(); }
    else if (m.type === 5) { ctx.arc(best.x, best.y, r * 1.5, Math.PI, 0); ctx.lineTo(best.x + r * 1.5, best.y + r); ctx.moveTo(best.x - r * 1.5, best.y); ctx.lineTo(best.x - r * 1.5, best.y + r); }
    else ctx.arc(best.x, best.y, r, 0, Math.PI * 2);
    ctx.stroke();
    ctx.shadowBlur = 0; ctx.fillText(MILESTONE[m.type] || "", best.x, best.y - r * 1.9);
  }
  ctx.restore();

  // typography
  ctx.save(); ctx.fillStyle = "rgba(255,255,255,0.9)"; ctx.textBaseline = "alphabetic";
  const name = (meta.name || "RUNNER").toUpperCase(), id = meta.id ? `#${meta.id}` : "UNREGISTERED";
  if (card) {
    const x = 690;
    ctx.font = '700 22px "Space Grotesk","Segoe UI",sans-serif'; ctx.fillStyle = "rgba(255,255,255,0.55)"; ctx.fillText("NEON PROTOCOL · NEON SIGNATURE", x, 90);
    ctx.font = '700 64px "Space Grotesk","Segoe UI",sans-serif'; ctx.fillStyle = "#fff"; ctx.fillText(id, x, 170);
    ctx.font = '600 34px "Space Grotesk","Segoe UI",sans-serif'; ctx.fillText(name, x, 230);
    ctx.font = '600 52px "JetBrains Mono",Consolas,monospace'; ctx.fillStyle = mix(c0, c1, 0.2); ctx.fillText(fmtTime(rec.timeMs), x, 320);
    ctx.font = '500 20px "Space Grotesk","Segoe UI",sans-serif'; ctx.fillStyle = "rgba(255,255,255,0.6)";
    { const sm = summarize(rec); ctx.fillText(`THE FRACTURE · RULES v${rec.rulesVersion} · ${sm.paint} PAINT HITS · ${sm.shards} SHARDS`, x, 370); }
    ctx.fillText(meta.status || "Local run (not registered)", x, 410);
    if (meta.hash) { ctx.font = '500 16px "JetBrains Mono",Consolas,monospace'; ctx.fillText(`art ${meta.hash.slice(0, 18)}…`, x, 450); }
    ctx.font = '700 26px "Space Grotesk","Segoe UI",sans-serif'; ctx.fillStyle = "#fff"; ctx.fillText(meta.challengeText || "CAN YOU BEAT THIS RUN?", x, 560);
  } else {
    ctx.font = '600 22px "Space Grotesk","Segoe UI",sans-serif'; ctx.fillStyle = "rgba(255,255,255,0.5)";
    ctx.fillText(`NEON PROTOCOL · THE FRACTURE · RULES v${rec.rulesVersion}`, 60, 70);
    ctx.textAlign = "right"; ctx.fillText(meta.status || "LOCAL RUN", W - 60, 70); ctx.textAlign = "left";
    ctx.font = '700 46px "Space Grotesk","Segoe UI",sans-serif'; ctx.fillStyle = "#fff"; ctx.fillText(`NEON SIGNATURE ${id}`, 60, H - 110);
    ctx.font = '600 28px "Space Grotesk","Segoe UI",sans-serif'; ctx.fillStyle = "rgba(255,255,255,0.75)"; ctx.fillText(name, 60, H - 66);
    ctx.textAlign = "right"; ctx.font = '600 44px "JetBrains Mono",Consolas,monospace'; ctx.fillStyle = mix(c0, c1, 0.2); ctx.fillText(fmtTime(rec.timeMs), W - 60, H - 110);
    if (meta.hash) { ctx.font = '500 18px "JetBrains Mono",Consolas,monospace'; ctx.fillStyle = "rgba(255,255,255,0.45)"; ctx.fillText(`art ${meta.hash.slice(0, 22)}…`, W - 60, H - 66); }
  }
  ctx.restore();
  return rec;
}

/** Deterministic demo record for previews/tests (clearly not a real run). */
export function demoRecord(seed = 1) {
  const r = rng(seed); const n = 60; const b = [1, 1, 0, 0, 1, 0xD4, 0xC0, 0, n, 4];
  let x = 40, z = 30;
  for (let i = 0; i < n; i++) { x = Math.max(10, Math.min(245, x + (r() - 0.45) * 40)); z = Math.max(10, Math.min(245, z + (r() - 0.35) * 30)); const kind = i % 17 === 5 ? 3 : i % 23 === 11 ? 4 : r() < 0.4 ? 1 : 0; b.push(x | 0, z | 0, ((r() * 4) | 0) << 4 | kind, Math.round(i / (n - 1) * 255)); }
  b.push(1, 70, 2, 110, 4, 150, 5, 255);
  return new Uint8Array(b);
}
