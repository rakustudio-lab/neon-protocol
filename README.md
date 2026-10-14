# NEON PROTOCOL — Raku Studio

A browser paintball heist on Monad. Fight through defenders, steal the core, escape on a lightcycle. Your route and paint hits become artwork. Share your run as a challenge: a friend's response is recorded and linked to yours on Monad. Link a wallet before a run to mint an optional NEON MARK; the walkable Vault reads NFT ownership to open its collectors' wing.

- **Play:** https://rakustudio.io/neon-protocol/play/
- **Project page:** https://rakustudio.io/neon-protocol/
- **Network:** Monad Testnet (chain 10143)
  - Registry: `0x439a3d6c2b2df600c7afab4bfd4eaf6d43617bcf`
  - NEON MARK (ERC-721): `0x3ad6d7a4c25d6a4a55507019967c3345d23cb3d6`
  - Example run: https://testnet.monadvision.com/tx/0xa15063880ec4460100778dfd4355bab5a0a855f2a465e9a2d4b45c34aab14adb
  - Challenge response linked to it: https://testnet.monadvision.com/tx/0x66b45ef0f5f0d2aa3f6434b406be9d6d2261924e240a3382f1ff066c3b31aae4

## What is in this repository
- `Assets/NeonProtocol/Scripts` — game code (Unity 6, C#): heist director, AI, lightcycle, Vault, web bridge
- `Assets/NeonProtocol/Editor`, `QA` — level builders and automated QA routines
- `Assets/WebGLTemplates/NeonProtocol` — web UI (menu, result, registration, challenge, Vault mint / ownership gate)
- `chain/contracts` — `NeonRegistry.sol` (run records + challenge lineage), `NeonMark.sol` (one optional collectible per wallet-linked run)
- `chain/relay` — Cloudflare Worker relay (single-use sessions, daily cap)
- `COMMIT_HISTORY.md` — dated development history of the full project

## Honest status
Prototype. Gameplay is offchain; run records are relay-attested, not trustless anti-cheat. Art and animation are early. Testnet only.
