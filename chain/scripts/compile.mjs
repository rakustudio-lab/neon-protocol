// Compile every contracts/*.sol -> build/<Contract>.json (abi + bytecode). NeonRegistry output is unchanged in shape.
import fs from "node:fs"; import path from "node:path"; import solc from "solc";
const root = path.resolve(import.meta.dirname, "..");
const sources = {};
for (const f of fs.readdirSync(path.join(root, "contracts")).filter(f => f.endsWith(".sol"))) sources[f] = { content: fs.readFileSync(path.join(root, "contracts", f), "utf8") };
const input = { language: "Solidity", sources,
  settings: { optimizer: { enabled: true, runs: 200 }, viaIR: true, outputSelection: { "*": { "*": ["abi", "evm.bytecode.object", "evm.deployedBytecode.object"] } } } };
const out = JSON.parse(solc.compile(JSON.stringify(input)));
const errs = (out.errors || []).filter(e => e.severity === "error");
(out.errors || []).forEach(e => console.log(e.severity, e.formattedMessage.split("\n").slice(0, 3).join(" | ")));
if (errs.length) process.exit(1);
fs.mkdirSync(path.join(root, "build"), { recursive: true });
for (const [file, cs] of Object.entries(out.contracts))
  for (const [name, c] of Object.entries(cs)) {
    if (!c.evm.bytecode.object || file.replace(".sol", "") !== name) continue;   // main contract of each file only
    fs.writeFileSync(path.join(root, `build/${name}.json`), JSON.stringify({ abi: c.abi, bytecode: "0x" + c.evm.bytecode.object, deployedBytecode: "0x" + c.evm.deployedBytecode.object }, null, 1));
    console.log("ok", name, "deployed bytes:", c.evm.deployedBytecode.object.length / 2);
  }
