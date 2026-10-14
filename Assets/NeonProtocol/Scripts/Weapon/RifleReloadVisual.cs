using UnityEngine;

namespace NeonProtocol
{
    // The mechanical reload, driven by PaintRifle's phase timeline (never the other way round):
    //   RELEASE   support hand leaves the handguard and grabs the canister under the receiver, latch vents
    //   EJECT     canister pulled out and dropped (physical spent canister, despawns)
    //   FETCH     hand goes to the belt and comes back with a fresh canister
    //   INSERT    canister pushed up into the well -> SEATED (ammo committed by PaintRifle at this instant)
    //   PRESSURIZE hand returns to the handguard, conduits charge from dark to full glow, READY ping
    // The rifle cants toward the support hand during the cycle. Tag-out/cancel restores the canister instantly.
    [DefaultExecutionOrder(200)]   // after CharacterAnimator (100): reads final hand poses
    public class RifleReloadVisual : MonoBehaviour
    {
        public PaintRifle rifle;
        public CharacterAnimator body;
        public Transform canister;
        public Renderer bodyRenderer;        // the rifle body (has the Glow material slot)
        public Renderer canisterRenderer;
        public AudioSource audioSource;
        public AudioClip releaseClip, dropClip, insertClip, pressurizeClip;
        public Color glowColor = new Color(0.15f, 2.6f, 2.3f), canisterColor = new Color(1.3f, 0.25f, 2.6f);

        Transform homeParent; Vector3 homePos; Quaternion homeRot;
        Transform lHand, lMiddle, hips;
        int glowIndex = -1;
        MaterialPropertyBlock _mpb; MaterialPropertyBlock mpb => _mpb ??= new MaterialPropertyBlock();
        static readonly int idEmission = Shader.PropertyToID("_EmissionColor");
        PaintRifle.Phase lastPhase = PaintRifle.Phase.Ready;
        bool dropped, fresh;

        void Awake()
        {
            if (!rifle) rifle = GetComponent<PaintRifle>();
            if (canister) { homeParent = canister.parent; homePos = canister.localPosition; homeRot = canister.localRotation; }
            if (bodyRenderer)
            {
                var mats = bodyRenderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] && mats[i].name.Contains("Glow")) glowIndex = i;
            }
            if (rifle) { rifle.ReloadCancelled += Restore; rifle.ReloadStarted += () => { dropped = false; fresh = false; }; }
        }

        void Start()
        {
            if (!body) body = GetComponentInParent<Transform>().root.GetComponentInChildren<CharacterAnimator>();
            var anim = body ? body.GetComponent<Animator>() : null;
            if (anim)
            {
                lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                lMiddle = anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
                hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            }
        }

        void Restore()
        {
            SeatCanister();
            if (body) { body.leftOverrideW = 0f; body.reloadPose = 0f; }
        }

        void SeatCanister()
        {
            if (!canister) return;
            canister.SetParent(homeParent, false); canister.localPosition = homePos; canister.localRotation = homeRot;
            canister.gameObject.SetActive(true);
        }

        Vector3 Palm => lHand ? (lMiddle ? Vector3.Lerp(lHand.position, lMiddle.position, 0.5f) : lHand.position) : transform.position;
        Vector3 HomeWorld => homeParent ? homeParent.TransformPoint(homePos) : transform.position;
        static float S(float a, float b, float k) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, k));
        static Vector3 Bezier(Vector3 a, Vector3 c, Vector3 b, float t) { float u = 1f - t; return u * u * a + 2f * u * t * c + t * t * b; }

        void LateUpdate()
        {
            if (!rifle || !canister) return;
            Glow();
            if (!rifle.Reloading)
            {
                if (body) { body.leftOverrideW = Mathf.MoveTowards(body.leftOverrideW, 0f, Time.deltaTime * 6f); body.reloadPose = Mathf.MoveTowards(body.reloadPose, 0f, Time.deltaTime * 5f); }
                return;
            }
            float k = rifle.ReloadProgress;
            Transform root = body ? (body.transform.parent ? body.transform.parent : body.transform) : transform;
            Vector3 up = transform.up;
            Vector3 under = HomeWorld - up * 0.11f;                         // palm just below the seated canister
            Vector3 pulled = under - up * 0.22f - root.right * 0.08f;       // canister pulled out
            // pouch on the LEFT side of the belt, slightly forward; the hand travels out and around, never across the torso
            Vector3 belt = (hips ? hips.position : root.position + Vector3.up) - root.right * 0.3f + root.forward * 0.14f;
            Vector3 swing = Vector3.Lerp(pulled, belt, 0.5f) - root.right * 0.16f + root.forward * 0.12f;   // arc waypoint, outside the body

            // support-hand path (palm target) and rifle cant
            Vector3 target;
            if (k < 0.16f) target = under;
            else if (k < 0.36f) target = Vector3.Lerp(under, pulled, S(0.16f, 0.3f, k));
            else if (k < 0.56f) { float u = S(0.36f, 0.5f, k); target = Bezier(pulled, swing, belt, u); }
            else if (k < PaintRifle.SeatAt) { float u = S(0.56f, 0.7f, k); target = Bezier(belt, swing, under, u); }
            else target = under;
            float w = k < 0.16f ? S(0f, 0.14f, k) : k < PaintRifle.SeatAt ? 1f : 1f - S(PaintRifle.SeatAt, 0.92f, k);
            if (body) { body.leftOverridePos = target; body.leftOverrideW = w; body.reloadPose = k < 0.9f ? Mathf.Min(1f, body.reloadPose + Time.deltaTime * 6f) : 1f - S(0.9f, 1f, k); }

            // canister: seated -> in hand -> dropped -> fresh in hand -> seated
            var ph = rifle.ReloadPhase;
            if (ph != lastPhase)
            {
                if (audioSource)
                {
                    if (ph == PaintRifle.Phase.Release && releaseClip) audioSource.PlayOneShot(releaseClip, 0.8f);
                    if (ph == PaintRifle.Phase.Insert && insertClip) audioSource.PlayOneShot(insertClip, 0.9f);
                    if (ph == PaintRifle.Phase.Pressurize && pressurizeClip) audioSource.PlayOneShot(pressurizeClip, 0.75f);
                }
                lastPhase = ph;
            }
            if (k >= 0.16f && k < 0.3f) { canister.gameObject.SetActive(true); InHand(); }
            else if (k >= 0.3f && !dropped) { dropped = true; DropSpent(); canister.gameObject.SetActive(false); }
            else if (k >= 0.46f && k < 0.66f) { canister.gameObject.SetActive(true); fresh = true; InHand(); }
            else if (k >= 0.66f && k < PaintRifle.SeatAt)
            {
                // slide up into the well
                canister.gameObject.SetActive(true);
                if (canister.parent != homeParent) canister.SetParent(homeParent, true);
                float u = S(0.66f, PaintRifle.SeatAt, k);
                canister.localPosition = Vector3.Lerp(homePos + Quaternion.Inverse(homeParent.rotation) * (-up) * 0.12f, homePos, u);
                canister.localRotation = homeRot;
            }
            else if (k >= PaintRifle.SeatAt) SeatCanister();
            if (k >= 0.3f && k < 0.46f) canister.gameObject.SetActive(false);
        }

        void InHand()
        {
            canister.SetParent(null, true);
            canister.SetPositionAndRotation(Palm + transform.up * 0.05f, homeParent ? homeParent.rotation * homeRot : transform.rotation);
        }

        void DropSpent()
        {
            var spent = Instantiate(canister.gameObject, canister.position, canister.rotation);
            spent.name = "SpentCanister"; spent.layer = 2;
            foreach (var t in spent.GetComponentsInChildren<Transform>()) t.gameObject.layer = 2;
            var r = spent.GetComponentInChildren<Renderer>();
            if (r) { r.GetPropertyBlock(mpb, 0); mpb.SetColor(idEmission, canisterColor * 0.05f); for (int i = 0; i < r.sharedMaterials.Length; i++) r.SetPropertyBlock(mpb, i); }
            var col = spent.AddComponent<CapsuleCollider>(); col.radius = 0.025f; col.height = 0.13f; col.center = new Vector3(0, -0.065f, 0);
            var rb = spent.AddComponent<Rigidbody>(); rb.mass = 0.3f;
            Transform root = body ? body.transform : transform;
            rb.linearVelocity = -transform.up * 1.2f - root.right * 0.6f;
            rb.angularVelocity = Random.insideUnitSphere * 8f;
            Destroy(spent, 4f);
            if (audioSource && dropClip) audioSource.PlayOneShot(dropClip, 0.5f);
        }

        void Glow()
        {
            float p = rifle.Pressure;
            if (bodyRenderer && glowIndex >= 0)
            {
                float flicker = p < 1f && p > 0f ? 0.85f + 0.15f * Mathf.Sin(Time.time * 60f) : 1f;
                bool low = !rifle.Reloading && rifle.Ammo <= rifle.lowAmmo;
                Color c = low ? Color.Lerp(glowColor, new Color(2.8f, 0.6f, 0.2f), 0.5f + 0.5f * Mathf.Sin(Time.time * 8f)) : glowColor;
                bodyRenderer.GetPropertyBlock(mpb, glowIndex); mpb.SetColor(idEmission, c * Mathf.Lerp(0.04f, 1f, p) * flicker); bodyRenderer.SetPropertyBlock(mpb, glowIndex);
            }
            if (canisterRenderer)
            {
                float fill = rifle.Reloading ? (fresh ? 1f : 0.15f) : Mathf.Lerp(0.15f, 1f, rifle.Ammo / (float)Mathf.Max(1, rifle.magazine));
                for (int i = 0; i < canisterRenderer.sharedMaterials.Length; i++)
                {
                    var m = canisterRenderer.sharedMaterials[i]; if (!m || !m.name.Contains("Canister")) continue;
                    canisterRenderer.GetPropertyBlock(mpb, i); mpb.SetColor(idEmission, canisterColor * fill); canisterRenderer.SetPropertyBlock(mpb, i);
                }
            }
        }
    }
}
