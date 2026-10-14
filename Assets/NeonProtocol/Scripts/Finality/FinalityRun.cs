using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NeonProtocol
{
    // ACT II — THE FINALITY RUN. Starts when the Core carrier reaches the Finality Gate: CRYPTO mounts the lightcycle
    // and rides the authored track (tunnel -> energy bridge with a lane choice -> drone chase) to the Finality Elevator.
    // Collect energy shards to stabilise the Core (the exit needs `shardsRequired`). The heist timer keeps running.
    public class FinalityRun : MonoBehaviour
    {
        public static FinalityRun Instance { get; private set; }
        public Lightcycle bike;
        public GameObject stageRoot;                 // track art, enabled only during the ride
        public int shardsRequired = 4;               // full route only
        [Tooltip("Featured route: start just before the tunnel exit; shards are an optional reward line, never a lock.")]
        public float featuredStartS = 120f;
        int Required => HeistDirector.FullRoute ? shardsRequired : 0;
        public float crashPenalty = 3f;
        public AudioSource music;
        public string[] beatNames = { "LAUNCH TUNNEL", "ENERGY BRIDGE", "DRONE CHASE", "FINALITY GATE" };
        public float[] beatStarts = { 0f, 470f, 980f, 1500f };

        public bool Running { get; private set; }
        public int Shards { get; private set; }
        public int ShardsTotal { get; private set; }
        public int DronesDown { get; private set; }
        public int Crashes { get; private set; }
        public readonly List<Transform> AimTargets = new List<Transform>();

        readonly List<TrackElement> elements = new List<TrackElement>();
        public IReadOnlyList<TrackElement> Elements => elements;
        BikeDrone[] drones;
        public BikeDrone[] Drones => drones;
        float checkpointS, sendT;
        string lastAnnounce = "";
        GameObject[] hiddenForRide;

        void Awake()
        {
            Instance = this;
            GetComponentsInChildren(true, elements);
            if (stageRoot) { stageRoot.GetComponentsInChildren(true, elements); }
            drones = stageRoot ? stageRoot.GetComponentsInChildren<BikeDrone>(true) : new BikeDrone[0];
            elements.Sort((a, b) => a.s.CompareTo(b.s));
            ShardsTotal = 0; foreach (var e in elements) if (e.kind == TrackElement.Kind.Shard) ShardsTotal++;
            foreach (var e in elements) if (e.kind == TrackElement.Kind.Switch) AimTargets.Add(e.lamp ? e.lamp.transform : e.transform);   // R4: the paint target sphere sits 5.8 m up its arm; the element root is at road level (assist used to aim at the road)
            foreach (var d in drones) AimTargets.Add(d.transform);
            if (stageRoot) stageRoot.SetActive(false);
            if (bike) bike.gameObject.SetActive(false);
        }

        public void Begin()
        {
            var dir = HeistDirector.Instance;
            Running = true; Shards = 0; DronesDown = 0; Crashes = 0; checkpointS = 0f;
            HeistDirector.Instance?.Recorder?.Mark(SignatureRecorder.Milestone.FinalityStart);
            if (stageRoot) stageRoot.SetActive(true);
            foreach (var e in elements) e.ResetElement();
            foreach (var d in drones) d.ResetDrone();
            // the on-foot rig hands over: hide CRYPTO-on-foot + shoulder camera, CRYPTO-the-rider appears on the bike
            var rig = FindFirstObjectByType<ShoulderRig>(); if (rig) rig.enabled = false;
            if (dir && dir.player) dir.player.gameObject.SetActive(false);
            checkpointS = HeistDirector.FullRoute ? 0f : featuredStartS;
            foreach (var e in elements) if (e.s < checkpointS && e.kind == TrackElement.Kind.Shard) e.done = true;   // not reachable on this route
            bike.gameObject.SetActive(true); bike.Place(checkpointS + 2f, 0f); bike.Active = true;
            if (music) music.Play();
            Say("CHAPTER II — THE FINALITY RUN");
            dir?.SetObjective("RIDE THE FINALITY TUNNEL — DODGE, PAINT, SURVIVE");
        }

        public void Abort()
        {
            // R4: always hand control back to the on-foot rig. After a COMPLETED ride (not running, bike hidden by the
            // elevator) this used to return early, so VISIT THE VAULT / RETRY left the player and camera rig disabled.
            var rig0 = FindFirstObjectByType<ShoulderRig>(FindObjectsInactive.Include); if (rig0) rig0.enabled = true;
            var dir0 = HeistDirector.Instance; if (dir0 && dir0.player) dir0.player.gameObject.SetActive(true);
            if (!Running && (!bike || !bike.gameObject.activeSelf)) return;
            Running = false;
            if (bike) { bike.Active = false; bike.gameObject.SetActive(false); }
            if (stageRoot) stageRoot.SetActive(false);
            if (music) music.Stop();
            var rig = FindFirstObjectByType<ShoulderRig>(FindObjectsInactive.Include); if (rig) rig.enabled = true;
            var dir = HeistDirector.Instance; if (dir && dir.player) dir.player.gameObject.SetActive(true);
        }

        // Called by the bike each frame with the distance it covered: exact pass-through tests in track space.
        public void Sweep(Lightcycle b, float s0, float s1, float x)
        {
            if (!Running) return;
            foreach (var e in elements)
            {
                if (e.s <= s0 || e.s > s1) continue;
                bool inside = x + 0.45f > e.x0 && x - 0.45f < e.x1;
                switch (e.kind)
                {
                    case TrackElement.Kind.Shard:
                        if (inside && !e.done) { e.done = true; if (e.moving) e.moving.gameObject.SetActive(false); Shards++; HeistDirector.Instance?.Recorder?.Mark(SignatureRecorder.Milestone.Shard); Say($"ENERGY SHARD {Shards}/{ShardsTotal}"); if (b.sfx && b.shardClip) b.sfx.PlayOneShot(b.shardClip); }
                        break;
                    case TrackElement.Kind.Barrier:
                    case TrackElement.Kind.Shutter:
                        if (inside && e.Blocks) { b.Damage(e.kind == TrackElement.Kind.Shutter ? "SHUTTER CLOSED — PAINT ITS SWITCH" : "BARRIER", true); if (b.Shields > 0) Bounce(b, e); }
                        break;
                    case TrackElement.Kind.Checkpoint:
                        if (e.s > checkpointS) { checkpointS = e.s; Say("CHECKPOINT"); }
                        break;
                    case TrackElement.Kind.Gate:
                        if (Shards >= Required) { b.BeginFinale(e.s); Complete(); return; }
                        else { Say($"CORE UNSTABLE — {Required - Shards} MORE SHARD{(Required - Shards == 1 ? "" : "S")} NEEDED · GO BACK"); RewindForShards(); Crash(b, false); }
                        break;
                }
            }
        }

        void Bounce(Lightcycle b, TrackElement e)
        {
            // stop just before the obstacle and nudge to the nearest open side, so the player can recover and steer past
            float half = b.track.HalfWidth(e.s) - 0.8f;
            float left = e.x0 - 0.9f, right = e.x1 + 0.9f;
            float nx = (Mathf.Abs(b.X - left) < Mathf.Abs(b.X - right) && left > -half) || right > half ? left : right;
            nx = Mathf.Clamp(nx, -half, half);
            // R4: on the bridge the divider splits the road. The right-lane shutters span x 0.8..9, so "nearest open side"
            // was x=-0.1: the bike was warped INTO the divider and came out in the other lane (the reported pass-through).
            // Keep the bike on its own side; a closed shutter that fills the lane just stops it (paint the switch).
            var t = b.track;
            if (t.dividerS1 > t.dividerS0 && e.s >= t.dividerS0 - 2f && e.s <= t.dividerS1)
            {
                float clear = t.dividerHalf + 0.55f, side = b.X >= 0f ? 1f : -1f;
                if (nx * side < clear) nx = side * Mathf.Clamp(Mathf.Abs(b.X), clear, half);
            }
            b.Warp(e.s - 3f, nx);
        }

        public void OnDamage(string why, int shieldsLeft)
        {
            Say(shieldsLeft > 0 ? $"{why} · SHIELD {shieldsLeft}" : "CRASHED");
            if (shieldsLeft <= 0) Crash(bike, true);
        }

        // Softlock fix (rescue 2026-10-11): missed shards before the last checkpoint never come back, and few lie after it.
        // Pick the latest checkpoint from which (shards kept before it) + (all shards after it) reaches the requirement.
        void RewindForShards()
        {
            var cps = new List<float> { 0f };
            foreach (var e in elements) if (e.kind == TrackElement.Kind.Checkpoint) cps.Add(e.s);
            cps.Sort();
            for (int i = cps.Count - 1; i >= 0; i--)
            {
                float cp = cps[i]; if (cp > checkpointS) continue;
                int kept = 0, after = 0;
                foreach (var e in elements) if (e.kind == TrackElement.Kind.Shard) { if (e.s <= cp) { if (e.done) kept++; } else after++; }
                if (kept + after >= Required || i == 0) { checkpointS = cp; return; }
            }
        }

        void Crash(Lightcycle b, bool counts)
        {
            if (counts) { Crashes++; HeistDirector.Instance?.AddPenalty(crashPenalty); }
            // respawn at the last checkpoint; shards collected after it must be collected again
            foreach (var e in elements)
                if (e.s > checkpointS && (e.kind == TrackElement.Kind.Shard || e.kind == TrackElement.Kind.Switch || e.kind == TrackElement.Kind.Shutter))
                {
                    if (e.kind == TrackElement.Kind.Shard && e.done) Shards--;
                    e.ResetElement();
                }
            b.Place(checkpointS + 2f, 0f); b.Active = true;
        }

        public void OnSwitch(TrackElement s) { Say("SHUTTER UNLOCKED"); }
        public void OnDroneDown(BikeDrone d) { DronesDown++; HeistDirector.Instance?.Recorder?.Mark(SignatureRecorder.Milestone.DroneDown); Say("DRONE DISABLED"); HeistDirector.Instance?.OnRivalTagged(d.transform.position); }

        void Complete()
        {
            Running = false;
            Say("FINALITY REACHED — CORE STABILIZED");
            HeistDirector.Instance?.CompleteFinality(Shards, DronesDown);
        }

        void Say(string s) { lastAnnounce = s; HeistDirector.Instance?.AnnounceText(s); }

        string Beat()
        {
            string b = beatNames[0];
            for (int i = 0; i < beatStarts.Length && i < beatNames.Length; i++) if (bike.S >= beatStarts[i]) b = beatNames[i];
            return b;
        }

        void LateUpdate()
        {
            if (!Running || !bike) return;
            sendT -= Time.unscaledDeltaTime; if (sendT > 0f) return; sendT = 1f / 15f;
            var inv = CultureInfo.InvariantCulture;
            var dir = HeistDirector.Instance;
            string json = "{\"type\":\"bike\",\"kmh\":" + Mathf.RoundToInt(bike.Speed * 3.6f) + ",\"shards\":" + Shards + ",\"shardsTotal\":" + ShardsTotal + ",\"required\":" + Required
                + ",\"boost\":" + bike.Boost.ToString("F2", inv) + ",\"ammo\":" + bike.Ammo + ",\"mag\":" + bike.magazine + ",\"reload\":" + (bike.Reloading ? bike.ReloadProgress.ToString("F2", inv) : "-1")
                + ",\"shields\":" + bike.Shields + ",\"maxShields\":" + bike.maxShields + ",\"dodge\":" + bike.DodgeReady.ToString("F2", inv)
                + ",\"beat\":\"" + Beat() + "\",\"remain\":" + Mathf.Max(0, Mathf.RoundToInt(bike.track.Length - bike.S)) + ",\"drones\":" + DronesDown
                + ",\"timeMs\":" + (dir ? Mathf.RoundToInt((dir.Elapsed + dir.Penalty) * 1000f) : 0) + "}";
            WebBridge.Send(json);
        }
    }
}
