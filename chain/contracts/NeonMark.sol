// SPDX-License-Identifier: MIT
pragma solidity ^0.8.26;

interface INeonRegistry {
    struct Trace {
        address player; uint64 timestamp; uint32 timeMs; uint32 challengeOf; uint16 rulesVersion; uint8 algo;
        bool attested; uint64 blockNumber; bytes32 artHash; string name;
    }
    function getTrace(uint32 id) external view returns (Trace memory);
}

/// @title NEON MARK — optional collectible for a registered NEON PROTOCOL run
/// @notice One token per registered run, minted only by the wallet recorded as that run's player.
///         tokenId == registry trace id, so a run can never be minted twice and the token is permanently bound to
///         the run's id, rules version, renderer version and artHash (all read live from the immutable registry).
///         Guest runs are recorded under a derived address that no wallet controls, so they cannot be claimed.
///         No price, no supply-based rarity, no marketplace logic. Metadata is deterministic and fully on-chain;
///         the full artwork is re-rendered by anyone from the registry's TraceRegistered event (verify keccak256 == artHash).
/// @dev Minimal ERC-721 (+ metadata, ERC-165). Transfers are standard; the registry record never changes.
contract NeonMark {
    string public constant name = "NEON MARK";
    string public constant symbol = "NMARK";
    INeonRegistry public immutable registry;
    string public externalBase;               // e.g. https://<site>/trace.html?id=  (set once at deploy)

    mapping(uint256 => address) internal _owner;
    mapping(address => uint256) internal _balance;
    mapping(uint256 => address) public getApproved;
    mapping(address => mapping(address => bool)) public isApprovedForAll;
    uint256 public totalSupply;

    event Transfer(address indexed from, address indexed to, uint256 indexed tokenId);
    event Approval(address indexed owner, address indexed approved, uint256 indexed tokenId);
    event ApprovalForAll(address indexed owner, address indexed operator, bool approved);

    error NotRunOwner();
    error AlreadyMinted();
    error NoToken();
    error NotAuthorized();
    error BadReceiver();

    constructor(address registry_, string memory externalBase_) { registry = INeonRegistry(registry_); externalBase = externalBase_; }

    // ------------------------------------------------------------------ mint
    /// @notice Mint the collectible for registered run `traceId`. Caller must be the run's recorded player.
    function mint(uint32 traceId) external returns (uint256 tokenId) {
        INeonRegistry.Trace memory t = registry.getTrace(traceId);    // reverts for unknown ids
        if (t.player != msg.sender) revert NotRunOwner();
        tokenId = traceId;
        if (_owner[tokenId] != address(0)) revert AlreadyMinted();
        _owner[tokenId] = msg.sender; _balance[msg.sender]++; totalSupply++;
        emit Transfer(address(0), msg.sender, tokenId);
    }

    // ------------------------------------------------------------------ ERC-721
    function ownerOf(uint256 id) public view returns (address o) { o = _owner[id]; if (o == address(0)) revert NoToken(); }
    function balanceOf(address a) external view returns (uint256) { if (a == address(0)) revert BadReceiver(); return _balance[a]; }
    function approve(address to, uint256 id) external {
        address o = ownerOf(id);
        if (msg.sender != o && !isApprovedForAll[o][msg.sender]) revert NotAuthorized();
        getApproved[id] = to; emit Approval(o, to, id);
    }
    function setApprovalForAll(address op, bool ok) external { isApprovedForAll[msg.sender][op] = ok; emit ApprovalForAll(msg.sender, op, ok); }
    function transferFrom(address from, address to, uint256 id) public {
        address o = ownerOf(id);
        if (o != from) revert NotAuthorized();
        if (to == address(0)) revert BadReceiver();
        if (msg.sender != o && !isApprovedForAll[o][msg.sender] && getApproved[id] != msg.sender) revert NotAuthorized();
        delete getApproved[id];
        unchecked { _balance[from]--; } _balance[to]++; _owner[id] = to;
        emit Transfer(from, to, id);
    }
    function safeTransferFrom(address from, address to, uint256 id) external { safeTransferFrom(from, to, id, ""); }
    function safeTransferFrom(address from, address to, uint256 id, bytes memory data) public {
        transferFrom(from, to, id);
        if (to.code.length > 0) {
            try IERC721Receiver(to).onERC721Received(msg.sender, from, id, data) returns (bytes4 r) { if (r != IERC721Receiver.onERC721Received.selector) revert BadReceiver(); }
            catch { revert BadReceiver(); }
        }
    }
    function supportsInterface(bytes4 i) external pure returns (bool) { return i == 0x01ffc9a7 || i == 0x80ac58cd || i == 0x5b5e139f; }

    // ------------------------------------------------------------------ metadata (deterministic, on-chain)
    function tokenURI(uint256 id) external view returns (string memory) {
        ownerOf(id);
        INeonRegistry.Trace memory t = registry.getTrace(uint32(id));
        string memory h = _hex32(t.artHash);
        string memory json = string.concat(
            '{"name":"NEON MARK #', _u(id), '","description":"Collectible for registered NEON PROTOCOL run #', _u(id),
            '. Bound to the run record (rules v', _u(t.rulesVersion), ', renderer v', _u(t.algo), '). The full artwork is rendered from the registry event; its keccak256 must equal art_hash.",',
            '"external_url":"', externalBase, _u(id), '",',
            '"image":"data:image/svg+xml;base64,', Base64.encode(bytes(_svg(t, id))), '",',
            '"attributes":[{"trait_type":"Run time (ms)","display_type":"number","value":', _u(t.timeMs), '},',
            '{"trait_type":"Rules version","value":', _u(t.rulesVersion), '},',
            '{"trait_type":"Renderer","value":', _u(t.algo), '},',
            '{"trait_type":"Provenance","value":"', t.attested ? "relay-accepted" : "self-submitted", '"},',
            '{"trait_type":"Challenge of","value":', _u(t.challengeOf), '},',
            '{"trait_type":"Art hash","value":"0x', h, '"}]}');
        return string.concat("data:application/json;base64,", Base64.encode(bytes(json)));
    }

    // badge only: colours come from artHash bytes; the real artwork lives in the registry event
    function _svg(INeonRegistry.Trace memory t, uint256 id) internal pure returns (string memory) {
        bytes32 a = t.artHash;
        string memory c1 = string.concat("hsl(", _u(160 + uint8(a[0]) % 40), ",95%,60%)");
        string memory c2 = string.concat("hsl(", _u(285 + uint8(a[1]) % 40), ",90%,62%)");
        string memory dots;
        for (uint256 i = 0; i < 12; i++) {
            uint256 x = 60 + uint8(a[2 + i]) % 280; uint256 y = 70 + uint8(a[14 + i]) % 220;
            dots = string.concat(dots, '<circle cx="', _u(x), '" cy="', _u(y), '" r="', _u(4 + uint8(a[26 + (i % 6)]) % 10), '" fill="', i % 3 == 0 ? c2 : c1, '"/>');
        }
        return string.concat('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 400"><rect width="400" height="400" fill="#0b0e22"/>',
            '<rect x="16" y="16" width="368" height="368" fill="none" stroke="', c1, '" stroke-width="2"/>', dots,
            '<text x="28" y="372" fill="#f4f4ff" font-family="monospace" font-size="18">NEON MARK #', _u(id), ' . ', _u(t.timeMs / 1000), '.', _pad3(t.timeMs % 1000), 's</text></svg>');
    }

    function _u(uint256 v) internal pure returns (string memory) {
        if (v == 0) return "0";
        uint256 n; for (uint256 x = v; x != 0; x /= 10) n++;
        bytes memory b = new bytes(n); for (; v != 0; v /= 10) b[--n] = bytes1(uint8(48 + v % 10));
        return string(b);
    }
    function _pad3(uint256 v) internal pure returns (string memory) { return v >= 100 ? _u(v) : v >= 10 ? string.concat("0", _u(v)) : string.concat("00", _u(v)); }
    function _hex32(bytes32 d) internal pure returns (string memory) {
        bytes memory s = new bytes(64); bytes16 hx = "0123456789abcdef";
        for (uint256 i = 0; i < 32; i++) { s[2 * i] = hx[uint8(d[i]) >> 4]; s[2 * i + 1] = hx[uint8(d[i]) & 15]; }
        return string(s);
    }
}

interface IERC721Receiver { function onERC721Received(address, address, uint256, bytes calldata) external returns (bytes4); }

library Base64 {
    bytes internal constant T = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    function encode(bytes memory data) internal pure returns (string memory) {
        if (data.length == 0) return "";
        bytes memory out = new bytes(4 * ((data.length + 2) / 3));
        uint256 j;
        for (uint256 i = 0; i < data.length; i += 3) {
            uint256 a = uint8(data[i]);
            uint256 b = i + 1 < data.length ? uint8(data[i + 1]) : 0;
            uint256 c = i + 2 < data.length ? uint8(data[i + 2]) : 0;
            uint256 n = (a << 16) | (b << 8) | c;
            out[j++] = T[(n >> 18) & 63]; out[j++] = T[(n >> 12) & 63];
            out[j++] = i + 1 < data.length ? T[(n >> 6) & 63] : bytes1("=");
            out[j++] = i + 2 < data.length ? T[n & 63] : bytes1("=");
        }
        return string(out);
    }
}
