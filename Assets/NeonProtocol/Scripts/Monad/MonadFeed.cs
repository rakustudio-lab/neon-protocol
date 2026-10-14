using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace NeonProtocol
{
    // OBSERVED Monad network data (read-only). One batched JSON-RPC call per second to a public RPC
    // (eth_getBlockByNumber latest), exponential backoff on errors, paused while the app is unfocused/hidden.
    // Nothing in gameplay waits on it, and nothing here is fabricated: if there is no data the state says so.
    public class MonadFeed : MonoBehaviour
    {
        public static MonadFeed Instance { get; private set; }

        public enum Status { Connecting, Live, Stale, Offline }
        [System.Serializable] public struct Block { public long number; public long timestamp; public int txCount; public long gasUsed; public string hash; public float receivedAt; }

        public string networkName = "MONAD MAINNET";
        public long chainId = 143;
        public string rpcUrl = "https://rpc.monad.xyz";
        public string explorerBlockUrl = "https://monadvision.com/block/";
        public float interval = 1f, staleAfter = 8f;

        public Status State { get; private set; } = Status.Connecting;
        public Block Latest { get; private set; }
        public int BlocksSeen { get; private set; }
        public float BlocksPerSecond { get; private set; }       // observed: block-number advance per wall-clock second between polls (smoothed)
        public string SourceLabel => rpcUrl.Replace("https://", "");
        public event System.Action<Block> NewBlock;

        float backoff, lastOk = -100f;
        bool focused = true;

        void Awake() { Instance = this; }
        void OnEnable() { Instance = this; StartCoroutine(Poll()); }
        void OnApplicationFocus(bool f) => focused = f;
        void OnApplicationPause(bool p) => focused = !p;

        public float AgeSeconds => Latest.number == 0 ? -1f : Mathf.Max(0f, (float)(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - Latest.timestamp));

        IEnumerator Poll()
        {
            const string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"eth_getBlockByNumber\",\"params\":[\"latest\",false]}";
            while (true)
            {
                if (!focused && Application.platform == RuntimePlatform.WebGLPlayer) { yield return new WaitForSecondsRealtime(1f); continue; }
                using (var req = new UnityWebRequest(rpcUrl, "POST"))
                {
                    req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.timeout = 6;
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success && Parse(req.downloadHandler.text)) { backoff = 0f; lastOk = Time.realtimeSinceStartup; }
                    else backoff = Mathf.Min(30f, backoff <= 0f ? 2f : backoff * 2f);
                }
                float sinceOk = Time.realtimeSinceStartup - lastOk;
                State = lastOk < 0f ? (backoff > 0f ? Status.Offline : Status.Connecting) : sinceOk > 20f ? Status.Offline : (AgeSeconds > staleAfter || sinceOk > staleAfter) ? Status.Stale : Status.Live;
                yield return new WaitForSecondsRealtime(interval + backoff);
            }
        }

        bool Parse(string json)
        {
            // minimal field extraction (no JSON lib dependency in the web build)
            string num = Field(json, "number"), ts = Field(json, "timestamp"), gas = Field(json, "gasUsed"), hash = Field(json, "hash");
            if (num == null || ts == null) return false;
            var b = new Block { number = Hex(num), timestamp = Hex(ts), gasUsed = gas != null ? Hex(gas) : 0, hash = hash ?? "", receivedAt = Time.realtimeSinceStartup };
            int ti = json.IndexOf("\"transactions\":[", System.StringComparison.Ordinal);
            if (ti >= 0)
            {
                int end = json.IndexOf(']', ti); int count = 0;
                for (int i = ti; i < end; i++) if (json[i] == '"') count++;
                b.txCount = Mathf.Max(0, (count - 2) / 2);
            }
            if (b.number > Latest.number)
            {
                if (Latest.number > 0)
                {
                    float dt = b.receivedAt - Latest.receivedAt;
                    if (dt > 0.2f) { float r = (b.number - Latest.number) / dt; BlocksPerSecond = BlocksPerSecond <= 0f ? r : Mathf.Lerp(BlocksPerSecond, r, 0.3f); }
                }
                Latest = b; BlocksSeen++;
                NewBlock?.Invoke(b);
            }
            return true;
        }

        static string Field(string json, string name)
        {
            string key = "\"" + name + "\":\"";
            int i = json.IndexOf(key, System.StringComparison.Ordinal); if (i < 0) return null;
            i += key.Length; int j = json.IndexOf('"', i); return j > i ? json.Substring(i, j - i) : null;
        }
        static long Hex(string h) { try { return System.Convert.ToInt64(h.StartsWith("0x") ? h.Substring(2) : h, 16); } catch { return 0; } }
    }
}
