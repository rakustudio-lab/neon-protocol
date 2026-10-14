using System;
using UnityEngine;

namespace NeonProtocol
{
    // THE VAULT — a walkable, unarmed exhibition room (rescue order §6). Separate from the heist: no timer, no rivals.
    //  * slot 0 = centrepiece (the player's own latest run), slots 1..6 = registered community runs (or honest empties)
    //  * walk up to a frame + E -> the page opens the inspect panel (record, REGISTERED / MINTED, challenge link)
    //  * the collectors' wing door opens only when the page reports a fresh, verified NEON MARK ownership read
    // The page owns all chain reads; Unity only displays what it is told and reports proximity/interaction.
    public class VaultRoom : MonoBehaviour
    {
        public static VaultRoom Instance { get; private set; }
        public Transform spawn;
        public Renderer[] slots = new Renderer[7];
        public TMPro.TextMeshPro[] slotLabels = new TMPro.TextMeshPro[7];
        public Transform gateDoor;
        public TMPro.TextMeshPro gateLabel;
        public Transform gateFront;               // where the player stands to be "at the gate"
        public Light[] slotLights;

        public bool Active { get; private set; }
        public string GateState { get; private set; } = "disconnected";

        readonly Texture2D[] tex = new Texture2D[7];
        Vector3 doorHome; float doorT; int near = -1; bool atGate; float sendT;
        MaterialPropertyBlock mpb;
        static readonly int idBase = Shader.PropertyToID("_BaseMap"), idColor = Shader.PropertyToID("_BaseColor");
        [Serializable] class Exhibit { public int slot; public string png; public string label; }
        [Serializable] class Gate { public string state; public string label; }

        void Awake() { Instance = this; mpb = new MaterialPropertyBlock(); if (gateDoor) doorHome = gateDoor.localPosition; }

        public void Enter()
        {
            var dir = HeistDirector.Instance;
            if (dir && dir.player)
            {
                dir.ToBriefing();                                   // no run, no timer
                dir.player.Teleport(spawn.position, spawn.eulerAngles.y);
                SetArmed(dir.player, false);
            }
            Active = true; near = -1; atGate = false;
            WebBridge.Send("{\"type\":\"vault_enter\"}");
        }

        public void Leave()
        {
            if (!Active) return;
            Active = false;
            var dir = HeistDirector.Instance;
            if (dir && dir.player) SetArmed(dir.player, true);
        }

        static void SetArmed(PlayerMotor p, bool armed)
        {
            // no combat in the Vault: the rifle cannot fire (it stays slung in the carry pose the locomotion clips expect)
            var rifle = p.GetComponentInChildren<PaintRifle>(true); if (rifle) rifle.enabled = armed;
            if (!armed) p.SetAimMode(0);
        }

        // page -> Unity: {slot, png (data URL or base64), label}
        public void SetExhibit(string json)
        {
            var e = JsonUtility.FromJson<Exhibit>(json);
            if (e == null || e.slot < 0 || e.slot >= slots.Length || !slots[e.slot]) return;
            if (slotLabels != null && e.slot < slotLabels.Length && slotLabels[e.slot]) slotLabels[e.slot].text = e.label ?? "";
            var r = slots[e.slot];
            if (string.IsNullOrEmpty(e.png)) { r.GetPropertyBlock(mpb); mpb.SetTexture(idBase, Texture2D.blackTexture); mpb.SetColor(idColor, new Color(0.08f, 0.09f, 0.16f)); r.SetPropertyBlock(mpb); return; }
            var b64 = e.png.Contains(",") ? e.png.Substring(e.png.IndexOf(',') + 1) : e.png;
            byte[] bytes; try { bytes = Convert.FromBase64String(b64); } catch { return; }
            if (bytes.Length > 3_000_000) return;
            if (!tex[e.slot]) tex[e.slot] = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            if (!tex[e.slot].LoadImage(bytes, true)) return;
            r.GetPropertyBlock(mpb); mpb.SetTexture(idBase, tex[e.slot]); mpb.SetColor(idColor, new Color(1.25f, 1.25f, 1.25f)); r.SetPropertyBlock(mpb);
        }

        // page -> Unity: verified gate state. Only "owner" opens the door; every other state keeps it shut.
        public void SetGate(string json)
        {
            var g = JsonUtility.FromJson<Gate>(json); if (g == null) return;
            GateState = g.state ?? "disconnected";
            if (gateLabel) gateLabel.text = g.label ?? "";
        }

        void Update()
        {
            if (!Active) return;
            var dir = HeistDirector.Instance; if (!dir || !dir.player) return;
            Vector3 p = dir.player.transform.position;
            // door: slides up only for a verified owner; closes again if the state changes (account switch, wrong chain)
            doorT = Mathf.MoveTowards(doorT, GateState == "owner" ? 1f : 0f, Time.deltaTime * 0.8f);
            if (gateDoor) gateDoor.localPosition = doorHome + Vector3.up * 3.6f * Mathf.SmoothStep(0, 1, doorT);
            // nearest frame the player is facing, within 3.2 m
            int best = -1; float bd = 3.2f;
            var fwd = dir.player.rig ? Quaternion.Euler(0, dir.player.rig.Yaw, 0) * Vector3.forward : dir.player.transform.forward;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i]) continue;
                Vector3 to = slots[i].bounds.center - p; to.y = 0; float d = to.magnitude;
                if (d < bd && Vector3.Dot(fwd, to.normalized) > 0.45f) { bd = d; best = i; }
            }
            bool gate = gateFront && (gateFront.position - p).sqrMagnitude < 9f;
            var input = NPInput.Read();
            if (input.interactPressed && best >= 0) WebBridge.Send("{\"type\":\"vault_inspect\",\"slot\":" + best + "}");
            sendT -= Time.unscaledDeltaTime;
            if (best != near || gate != atGate || sendT <= 0f)
            {
                near = best; atGate = gate; sendT = 0.5f;
                WebBridge.Send("{\"type\":\"vault\",\"near\":" + near + ",\"gate\":" + (atGate ? "true" : "false") + ",\"wing\":" + (doorT > 0.9f && p.z > (gateDoor ? gateDoor.position.z : 0f) ? "true" : "false") + "}");
            }
        }
    }
}
