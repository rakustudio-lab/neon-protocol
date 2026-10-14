using System.Collections.Generic;
using UnityEngine;

namespace NeonProtocol
{
    // The heist state machine. One run = DEPLOY -> EXPLORE -> ACTIVATE RELAYS -> ACQUIRE CORE -> PURSUIT -> EXTRACT.
    // Owns the timer, penalties and the result; tells the page (WebBridge) so the HTML result screen can render the
    // Neon Signature and offer registration/sharing.
    [DefaultExecutionOrder(-40)]
    public class HeistDirector : MonoBehaviour
    {
        public enum Phase { Briefing, Infiltrate, CoreUnlocked, Pursuit, FinalityRun, Extracted, Failed, Ascension }

        public static HeistDirector Instance { get; private set; }
        public const byte RulesVersion = 1;          // the full adventure (3 relays, long escape)
        public const byte FeaturedRulesVersion = 3;  // the featured judge route: Relay A (Iris) -> Core -> short escape
        // Featured is the default route. Different routes never compare: the rules version travels with every record.
        public static bool FullRoute;
        public static byte Rules => FullRoute ? RulesVersion : FeaturedRulesVersion;
        public static bool Required(Relay r) => r && (FullRoute || r.name.EndsWith("A"));

        public PlayerMotor player;
        public Transform[] checkpoints;        // respawn points after a tag-out (nearest reached)
        public Relay[] relays;
        public CoreObjective core;
        public ExtractionGate gate;
        public SignatureRecorder recorder;
        public float timeLimit = 420f;
        public float tagPenalty = 8f;
        public string challengeToken;          // set by the page for server sessions
        public int challengeOf;                 // trace id being challenged (0 = open run)

        public Phase Current { get; private set; } = Phase.Briefing;
        public float Elapsed { get; private set; }
        public float Penalty { get; private set; }
        public int RelaysOnline { get; private set; }
        public int PlayerTags { get; private set; }
        public int RivalsTagged { get; private set; }
        public string Objective { get; private set; } = "";
        public SignatureRecorder Recorder => recorder;
        public byte[] LastSignature { get; private set; }
        public event System.Action<Phase> PhaseChanged;
        public event System.Action<string> Announce;

        int lastCheckpoint;

        void Awake() { Instance = this; }
        void OnEnable() { Instance = this; }

        void Start()
        {
            foreach (var r in relays) if (r) r.WentOnline += OnRelay;
            if (core) { core.PickedUp += OnCorePicked; core.Dropped += OnCoreDropped; }
            if (gate) gate.Extracted += OnExtracted;
            SetObjective("FIND AND PAINT THE 3 SECURITY RELAYS");
        }

        public void BeginRun()
        {
            Elapsed = 0; Penalty = 0; RelaysOnline = 0; PlayerTags = 0; RivalsTagged = 0; lastCheckpoint = 0; FinalityShards = 0; FinalityDrones = 0; AscentBlocks = 0;
            foreach (var r in relays) if (r) r.ResetRelay();
            FinalityElevator.Instance?.ResetElevator();
            FinalityRun.Instance?.Abort();
            core?.ResetCore(); gate?.ResetGate();
            if (checkpoints != null && checkpoints.Length > 0 && player) player.Teleport(checkpoints[0].position, checkpoints[0].eulerAngles.y);
            PaintSystem.Instance?.ClearAll();
            if (recorder) recorder.rulesVersion = Rules;   // the artwork carries the same rules version as the record
            recorder?.Begin();
            SetPhase(Phase.Infiltrate);
            SetObjective(FullRoute ? "FIND AND PAINT THE 3 SECURITY RELAYS" : "PAINT THE LIT PAD");
            WebBridge.Send("{\"type\":\"run_start\",\"rules\":" + Rules + ",\"route\":\"" + (FullRoute ? "full" : "featured") + "\"}");
        }

        void Update()
        {
            if (Current == Phase.Infiltrate || Current == Phase.CoreUnlocked || Current == Phase.Pursuit || Current == Phase.FinalityRun)
            {
                if (!FullRoute && Current == Phase.Infiltrate && RelaysOnline > 0 && PaintballEncounter.Instance && PaintballEncounter.Instance.Complete)
                {
                    core?.Unlock(); SetPhase(Phase.CoreUnlocked); SetObjective("GO THROUGH THE CORE DOOR");
                    Announce?.Invoke("CORE ACCESS CLEAR · DOOR OPENING");
                }
                Elapsed += Time.deltaTime;
                if (Elapsed + Penalty > timeLimit) Fail("TIME EXPIRED");
                // track the furthest checkpoint reached
                if (checkpoints != null && player)
                    for (int i = lastCheckpoint + 1; i < checkpoints.Length; i++)
                        if (checkpoints[i] && (checkpoints[i].position - player.transform.position).sqrMagnitude < 9f) lastCheckpoint = i;
            }
        }

        void OnRelay(Relay r)
        {
            RelaysOnline = 0; int need = 0; foreach (var x in relays) if (Required(x)) { need++; if (x.Online) RelaysOnline++; }
            Announce?.Invoke(need > 1 ? $"RELAY ONLINE  {RelaysOnline}/{need}" : "RELAY ONLINE");
            NoiseSystem.Emit(r.transform.position, 30f, r);
            if (RelaysOnline >= need && (!PaintballEncounter.Instance || PaintballEncounter.Instance.Complete))
            {
                core?.Unlock();
                SetPhase(Phase.CoreUnlocked);
                SetObjective("TAKE THE CORE");
                Announce?.Invoke("CORE VAULT UNLOCKED");
            }
            else SetObjective(RelaysOnline >= need ? "TAG THE DEFENDERS TO UNLOCK CORE ACCESS" : $"PAINT THE SECURITY RELAYS  {RelaysOnline}/{need}");
        }

        void OnCorePicked()
        {
            recorder?.Mark(SignatureRecorder.Milestone.CoreAcquired);
            gate?.Open();
            SetPhase(Phase.Pursuit);
            SetObjective("REACH THE BLUE GATE");
            PaintballEncounter.Instance?.StartPursuit();
            Announce?.Invoke("CORE SECURED · REACH THE BLUE GATE");
        }

        void OnCoreDropped()
        {
            recorder?.Mark(SignatureRecorder.Milestone.CoreDropped);
            SetPhase(Phase.CoreUnlocked);
            SetObjective("RECOVER THE CORE");
        }

        void OnExtracted()
        {
            if (Current != Phase.Pursuit) return;
            // ACT II: the Finality Gate leads to the lightcycle escape when the stage exists in this scene
            if (FinalityRun.Instance && FinalityRun.Instance.bike)
            {
                SetPhase(Phase.FinalityRun);
                Announce?.Invoke("MOUNT UP — THE FINALITY RUN");
                FinalityRun.Instance.Begin();
                return;
            }
            recorder?.Mark(SignatureRecorder.Milestone.Extracted);
            SetPhase(Phase.Extracted);
            SetObjective("EXTRACTED");
            Finish(true);
        }

        // End of ACT II: the Finality Gate accepted the stabilised Core.
        public void CompleteFinality(int shards, int drones)
        {
            if (Current != Phase.FinalityRun) return;
            FinalityShards = shards; FinalityDrones = drones;
            if (FinalityElevator.Instance)
            {
                // ACT III: the timer stops at the gate; the elevator ride is the payoff, not a time cost
                SetPhase(Phase.Ascension);
                SetObjective("THE FINALITY ELEVATOR");
                FinalityElevator.Instance.Begin();
                return;
            }
            recorder?.Mark(SignatureRecorder.Milestone.Extracted);
            SetPhase(Phase.Extracted);
            SetObjective("EXTRACTED");
            Finish(true);
        }
        // Practice / QA entry: start a run directly at ACT II (the timer and result flow are the real ones).
        public void PracticeFinality()
        {
            BeginRun();
            if (!FinalityRun.Instance) return;
            SetPhase(Phase.FinalityRun);
            FinalityRun.Instance.Begin();
        }
        public int FinalityShards { get; private set; }
        public int AscentBlocks { get; private set; }
        public void CompleteElevator(int blocks)
        {
            if (Current != Phase.Ascension) return;
            AscentBlocks = blocks;
            recorder?.Mark(SignatureRecorder.Milestone.Extracted);
            SetPhase(Phase.Extracted);
            SetObjective("EXTRACTED");
            Finish(true);
        }
        public int FinalityDrones { get; private set; }
        public void AddPenalty(float seconds) { Penalty += seconds; }

        public void OnPlayerTagged(Vector3 at)
        {
            PlayerTags++;
            lastTagPos = at;
            Penalty += tagPenalty;
            recorder?.Mark(SignatureRecorder.Kind.Tag, at);
            recorder?.Mark(SignatureRecorder.Milestone.PlayerTagged);
            // the Core stays with the player: a tag-out costs time and position, not the objective
            Announce?.Invoke($"TAGGED OUT  +{tagPenalty:F0}s");
        }

        Vector3 lastTagPos;
        // Respawn at the checkpoint nearest to where the player was tagged: small setback, never a long walk back.
        public Transform RespawnPoint()
        {
            if (checkpoints == null || checkpoints.Length == 0) return null;
            Transform best = checkpoints[0]; float bd = float.MaxValue;
            foreach (var c in checkpoints) { if (!c) continue; float d = (c.position - lastTagPos).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
            return best;
        }

        public void OnRivalTagged(Vector3 at)
        {
            RivalsTagged++;
            recorder?.Mark(SignatureRecorder.Kind.Tag, at);
        }

        void Fail(string why)
        {
            SetPhase(Phase.Failed);
            SetObjective(why);
            Finish(false);
        }

        void Finish(bool success)
        {
            recorder?.Stop();
            int ms = Mathf.RoundToInt((Elapsed + Penalty) * 1000f);
            LastSignature = recorder ? recorder.Encode(Mathf.Max(1, ms), (byte)((success ? 1 : 0) | (PlayerTags > 0 ? 2 : 0))) : null;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string json = "{\"type\":\"run_end\",\"success\":" + (success ? "true" : "false") + ",\"timeMs\":" + ms
                + ",\"elapsedMs\":" + Mathf.RoundToInt(Elapsed * 1000f) + ",\"penaltyMs\":" + Mathf.RoundToInt(Penalty * 1000f)
                + ",\"tags\":" + PlayerTags + ",\"rivalsTagged\":" + RivalsTagged + ",\"rules\":" + Rules + ",\"route\":\"" + (FullRoute ? "full" : "featured") + "\""
                + ",\"shards\":" + FinalityShards + ",\"drones\":" + FinalityDrones + ",\"ascentBlocks\":" + AscentBlocks
                + ",\"challengeOf\":" + challengeOf + ",\"art\":\"" + (LastSignature != null ? SignatureRecorder.ToHex(LastSignature) : "") + "\"}";
            WebBridge.Send(json);
            Debug.Log("[NP] " + json);
            GameBoot.Unlock();
        }

        public void ToBriefing()
        {
            recorder?.Stop();
            FinalityElevator.Instance?.ResetElevator();
            FinalityRun.Instance?.Abort();
            SetPhase(Phase.Briefing);
            if (checkpoints != null && checkpoints.Length > 0 && player) player.Teleport(checkpoints[0].position, checkpoints[0].eulerAngles.y);
        }

        void SetPhase(Phase p) { Current = p; PhaseChanged?.Invoke(p); }
        public void SetObjective(string o) { Objective = o; }

        // --- page hooks (SendMessage on "_Systems" is forwarded by GameBoot) ---
        public void AnnounceText(string text) => Announce?.Invoke(text);
        public void SetChallenge(int traceId) => challengeOf = traceId;
    }
}
