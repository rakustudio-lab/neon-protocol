// LOCAL ONLY (Hardhat node at 127.0.0.1:8545, chain 31337, throwaway dev accounts). Sends nothing to Monad.
// Proves the on-chain half of the social loop and the collectible rules before any deployment request:
//  register (wallet-linked attested, guest attested, self) -> art bytes from the event match artHash -> challenge
//  lineage (same rules ok, other rules rejected) -> NEON MARK: owner mints once, duplicate / other wallet / guest refused,
//  tokenURI deterministic and bound to the run record.
import fs from "node:fs"; import path from "node:path";
import { createPublicClient, createWalletClient, http, defineChain, keccak256, toHex, parseEventLogs, decodeErrorResult } from "viem";
import { privateKeyToAccount } from "viem/accounts";
const root = path.resolve(import.meta.dirname, "..");
const J = n => JSON.parse(fs.readFileSync(path.join(root, `build/${n}.json`), "utf8"));
const REG = J("NeonRegistry"), MARK = J("NeonMark");
const local = defineChain({ id: 31337, name: "local", nativeCurrency: { name: "ETH", symbol: "ETH", decimals: 18 }, rpcUrls: { default: { http: ["http://127.0.0.1:8545"] } } });
// Hardhat's publicly documented default dev keys (no value anywhere)
const K = ["0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80", "0x59c6995e998f97a5a0044966f0945389dc9e86dae88c7a8412f4603b6b78690d", "0x5de4111afa1a4b94908f83103eb1f1706367c2e68ca870fc3fb9a804cdab365a", "0x7c852118294e51e653712a81e05800f419141751be58f605c371e15141b007a6"];
const [deployer, operator, alice, bob] = K.map(k => privateKeyToAccount(k));
const pub = createPublicClient({ chain: local, transport: http() });
const W = a => createWalletClient({ account: a, chain: local, transport: http() });
const results = []; const ok = (n, c, d = "") => { results.push({ test: n, pass: !!c, detail: d }); console.log((c ? "PASS " : "FAIL ") + n + (d ? "  " + d : "")); };
async function send(acct, address, abi, fn, args) { const h = await W(acct).writeContract({ address, abi, functionName: fn, args }); const r = await pub.waitForTransactionReceipt({ hash: h }); return r; }
async function reverts(acct, address, abi, fn, args) {
  try { await pub.simulateContract({ account: acct, address, abi, functionName: fn, args }); return null; }
  catch (e) { return e?.cause?.data?.errorName ?? e?.cause?.reason ?? e.shortMessage ?? "reverted"; }
}
async function deploy(acct, c, args) { const h = await W(acct).deployContract({ abi: c.abi, bytecode: c.bytecode, args }); return (await pub.waitForTransactionReceipt({ hash: h })).contractAddress; }

const reg = await deploy(deployer, REG, [operator.address]);
const mark = await deploy(deployer, MARK, [reg, "https://example.invalid/trace.html?id="]);
ok("deploy registry + mark locally", reg && mark, `${reg} ${mark}`);

const art = n => "0x01" + Array.from({ length: n }, (_, i) => ((i * 37 + n) & 255).toString(16).padStart(2, "0")).join("");
const guest = "0x" + keccak256(toHex("guest:test-browser-id")).slice(26);
// 1) Alice: wallet-linked attested run (relay verified her signature), rules v2
let r = await send(operator, reg, REG.abi, "registerAttested", [alice.address, "ALICE", 131000, 0, 2, 1, keccak256(toHex("s1")), art(400)]);
const ev1 = parseEventLogs({ abi: REG.abi, logs: r.logs, eventName: "TraceRegistered" })[0].args;
ok("attested wallet-linked run registered", ev1.id === 1 && ev1.player === alice.address, `id ${ev1.id}`);
ok("art bytes from the event match artHash", keccak256(ev1.art) === ev1.artHash);
const onchain = await pub.readContract({ address: reg, abi: REG.abi, functionName: "getTrace", args: [1] });
ok("stored artHash/blockNumber point at that event", onchain.artHash === ev1.artHash && onchain.blockNumber === r.blockNumber);
ok("session reuse rejected", (await reverts(operator, reg, REG.abi, "registerAttested", [alice.address, "ALICE", 1, 0, 2, 1, keccak256(toHex("s1")), art(10)])) === "SessionReused");
// 2) guest run (derived address nobody controls)
r = await send(operator, reg, REG.abi, "registerAttested", [guest, "GUEST", 140000, 0, 2, 1, keccak256(toHex("s2")), art(300)]);
// 3) Bob answers Alice's challenge from his own wallet (self-submitted), same rules
r = await send(bob, reg, REG.abi, "registerSelf", ["BOB", 128500, 1, 2, 1, art(380)]);
const ev3 = parseEventLogs({ abi: REG.abi, logs: r.logs, eventName: "TraceRegistered" })[0].args;
ok("challenge response keeps lineage", ev3.challengeOf === 1 && ev3.attested === false, `id ${ev3.id} answers #${ev3.challengeOf}`);
ok("challenge across rules versions rejected", (await reverts(bob, reg, REG.abi, "registerSelf", ["BOB", 120000, 1, 1, 1, art(50)])) === "BadInput");
// 4) NEON MARK rules
r = await send(alice, mark, MARK.abi, "mint", [1]);
ok("run owner mints her run", (await pub.readContract({ address: mark, abi: MARK.abi, functionName: "ownerOf", args: [1n] })) === alice.address);
ok("duplicate mint refused", (await reverts(alice, mark, MARK.abi, "mint", [1])) === "AlreadyMinted");
ok("other wallet cannot claim someone's run", (await reverts(bob, mark, MARK.abi, "mint", [1])) === "NotRunOwner");
ok("guest run cannot be claimed by any wallet", (await reverts(bob, mark, MARK.abi, "mint", [2])) === "NotRunOwner" && (await reverts(alice, mark, MARK.abi, "mint", [2])) === "NotRunOwner");
ok("self-submitted run mintable by its wallet", (await send(bob, mark, MARK.abi, "mint", [3])).status === "success");
const u1 = await pub.readContract({ address: mark, abi: MARK.abi, functionName: "tokenURI", args: [1n] });
const u1b = await pub.readContract({ address: mark, abi: MARK.abi, functionName: "tokenURI", args: [1n] });
const meta = JSON.parse(Buffer.from(u1.split(",")[1], "base64").toString("utf8"));
const attr = Object.fromEntries(meta.attributes.map(a => [a.trait_type, a.value]));
ok("metadata deterministic", u1 === u1b);
ok("metadata bound to the run record", attr["Art hash"] === ev1.artHash && attr["Rules version"] === 2 && attr["Run time (ms)"] === 131000 && attr["Provenance"] === "relay-accepted", JSON.stringify(attr));
ok("metadata image is valid svg", Buffer.from(meta.image.split(",")[1], "base64").toString("utf8").startsWith("<svg"));
ok("ERC-721 + metadata interfaces", await pub.readContract({ address: mark, abi: MARK.abi, functionName: "supportsInterface", args: ["0x80ac58cd"] }) && await pub.readContract({ address: mark, abi: MARK.abi, functionName: "supportsInterface", args: ["0x5b5e139f"] }));
await send(alice, mark, MARK.abi, "transferFrom", [alice.address, bob.address, 1n]);
ok("transfer moves the token, record unchanged", (await pub.readContract({ address: mark, abi: MARK.abi, functionName: "ownerOf", args: [1n] })) === bob.address && (await pub.readContract({ address: reg, abi: REG.abi, functionName: "getTrace", args: [1] })).player === alice.address);
fs.mkdirSync(path.join(root, "evidence"), { recursive: true });
fs.writeFileSync(path.join(root, "evidence/localtest_results.json"), JSON.stringify({ chain: "local hardhat 31337 (not Monad)", registry: reg, mark, results, sampleMetadata: meta }, null, 1));
console.log(results.every(x => x.pass) ? "ALL PASS" : "SOME FAILED");
