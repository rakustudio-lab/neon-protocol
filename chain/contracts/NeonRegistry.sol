// SPDX-License-Identifier: MIT
pragma solidity ^0.8.26;

/// @title NEON PROTOCOL — public registry of Neon Signatures ("traces")
/// @notice A trace is the compact, canonical record of one completed run (paint hits, relays, milestones).
///         The record bytes are emitted in the TraceRegistered event (cheap log data) and their keccak256 hash is
///         stored here, so anyone can fetch the bytes from the logs of `block`, verify them against `artHash`
///         and re-render the exact artwork from Monad alone (renderer algorithm version = `algo`).
/// @dev Two honest provenance levels:
///      - ATTESTED: written by the game relay (operator) after its server-side checks (session token, timing bounds).
///                  This attests "the relay accepted this run", NOT that gameplay is cheat-proof.
///      - SELF:     written directly by a player's wallet. Publicly registered, not server-checked.
///      v2 (2026-10-11): art moved from storage to event data (Monad bills the gas limit, storage was ~0.1 MON/run),
///      owner can pause new registrations (kill switch) and hand over ownership.
contract NeonRegistry {
    struct Trace {
        address player;        // wallet, or a deterministic guest address derived by the relay
        uint64  timestamp;
        uint32  timeMs;        // completion time
        uint32  challengeOf;   // id of the root trace this run answered (0 = open run)
        uint16  rulesVersion;  // map/rules version; only equal versions are comparable
        uint8   algo;          // signature renderer version
        bool    attested;      // true = relay-accepted, false = self-submitted
        uint64  blockNumber;   // block that holds the TraceRegistered log with the art bytes
        bytes32 artHash;       // keccak256(art)
        string  name;          // display name (pseudonym)
    }

    address public operator;
    address public owner;
    bool public paused;
    Trace[] internal traces;   // id = index + 1
    mapping(bytes32 => bool) public sessionUsed;
    mapping(uint32 => uint32) public bestOf;        // challenge root id => best answering attested trace id
    mapping(address => uint32[]) internal byPlayer;

    uint256 public constant MAX_ART = 1024;
    uint256 public constant MAX_NAME = 24;

    event TraceRegistered(uint32 indexed id, address indexed player, uint32 indexed challengeOf, uint32 timeMs, uint16 rulesVersion, uint8 algo, bool attested, bytes32 artHash, bytes32 session, string name, bytes art);
    event OperatorChanged(address operator);
    event OwnerChanged(address owner);
    event Paused(bool paused);

    error NotOperator();
    error NotOwner();
    error BadInput();
    error SessionReused();
    error IsPaused();

    modifier onlyOwner() { if (msg.sender != owner) revert NotOwner(); _; }

    constructor(address _operator) {
        owner = msg.sender;
        operator = _operator;
    }

    function setOperator(address o) external onlyOwner { operator = o; emit OperatorChanged(o); }
    function setOwner(address o) external onlyOwner { if (o == address(0)) revert BadInput(); owner = o; emit OwnerChanged(o); }
    function setPaused(bool p) external onlyOwner { paused = p; emit Paused(p); }

    /// @notice Relay-attested registration (gas paid by the game, players never sign).
    function registerAttested(address player, string calldata name, uint32 timeMs, uint32 challengeOf, uint16 rulesVersion, uint8 algo, bytes32 session, bytes calldata art)
        external returns (uint32 id)
    {
        if (msg.sender != operator) revert NotOperator();
        if (session == bytes32(0) || sessionUsed[session]) revert SessionReused();
        sessionUsed[session] = true;
        id = _store(player, name, timeMs, challengeOf, rulesVersion, algo, true, session, art);
    }

    /// @notice Self-submitted registration from the player's own wallet (labelled as not server-checked).
    function registerSelf(string calldata name, uint32 timeMs, uint32 challengeOf, uint16 rulesVersion, uint8 algo, bytes calldata art)
        external returns (uint32 id)
    {
        id = _store(msg.sender, name, timeMs, challengeOf, rulesVersion, algo, false, bytes32(0), art);
    }

    function _store(address player, string calldata name, uint32 timeMs, uint32 challengeOf, uint16 rulesVersion, uint8 algo, bool attested, bytes32 session, bytes calldata art)
        internal returns (uint32 id)
    {
        if (paused) revert IsPaused();
        if (player == address(0) || art.length == 0 || art.length > MAX_ART || bytes(name).length > MAX_NAME || timeMs == 0) revert BadInput();
        if (challengeOf != 0 && challengeOf > traces.length) revert BadInput();
        uint32 root = challengeOf;
        if (root != 0) {
            Trace storage parent = traces[root - 1];
            if (parent.rulesVersion != rulesVersion) revert BadInput();   // only comparable versions
            if (parent.challengeOf != 0) root = parent.challengeOf;       // challenges chain to the original
        }
        bytes32 h = keccak256(art);
        traces.push(Trace({
            player: player, timestamp: uint64(block.timestamp), timeMs: timeMs, challengeOf: root, rulesVersion: rulesVersion,
            algo: algo, attested: attested, blockNumber: uint64(block.number), artHash: h, name: name
        }));
        id = uint32(traces.length);
        byPlayer[player].push(id);
        if (root != 0 && attested) {
            uint32 best = bestOf[root];
            if (best == 0 || timeMs < traces[best - 1].timeMs) bestOf[root] = id;
        }
        emit TraceRegistered(id, player, root, timeMs, rulesVersion, algo, attested, h, session, name, art);
    }

    // ---------- views (gallery reads these with plain eth_call; art bytes come from the event log in `blockNumber`) ----------

    function count() external view returns (uint256) { return traces.length; }

    function getTrace(uint32 id) external view returns (Trace memory) {
        if (id == 0 || id > traces.length) revert BadInput();
        return traces[id - 1];
    }

    /// @notice Newest-first page of traces (cheap listing).
    function page(uint32 fromId, uint32 n) external view returns (uint32[] memory ids, address[] memory players, uint32[] memory times, bool[] memory attested, bytes32[] memory hashes, string[] memory names) {
        uint256 total = traces.length;
        if (fromId == 0 || fromId > total) fromId = uint32(total);
        uint256 m = n < fromId ? n : fromId;
        ids = new uint32[](m); players = new address[](m); times = new uint32[](m); attested = new bool[](m); hashes = new bytes32[](m); names = new string[](m);
        for (uint256 i = 0; i < m; i++) {
            uint32 id = uint32(fromId - i);
            Trace storage t = traces[id - 1];
            ids[i] = id; players[i] = t.player; times[i] = t.timeMs; attested[i] = t.attested; hashes[i] = t.artHash; names[i] = t.name;
        }
    }

    function tracesOf(address player) external view returns (uint32[] memory) { return byPlayer[player]; }
}
