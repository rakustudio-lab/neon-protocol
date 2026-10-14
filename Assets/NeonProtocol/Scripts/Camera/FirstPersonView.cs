using UnityEngine;

namespace NeonProtocol
{
    // First-person weapon view for the on-foot trial (ShoulderRig.firstPersonTrial). The player's own rifle model is
    // copied onto the camera (the body + third-person rifle are hidden by CharacterFade), framed at hip / ADS,
    // with restrained sway, a recoil kick that returns cleanly, a reload dip timed by the real PaintRifle reload,
    // sprint lowering and muzzle obstruction (pulls in instead of clipping into walls). Shots leave from the visible
    // muzzle: PaintRifle.muzzle is pointed at this copy while first person is active and restored afterwards.
    [DefaultExecutionOrder(1000)]   // after ShoulderRig places the camera
    public class FirstPersonView : MonoBehaviour
    {
        public ShoulderRig rig;
        public PaintRifle rifle;
        public CharacterAnimator body;
        public Vector3 hipPos = new Vector3(0.14f, -0.27f, 0.25f), adsPos = new Vector3(0f, -0.16f, 0.27f);
        public Vector3 euler = new Vector3(0f, 0f, 0f);      // extra framing rotation (degrees)

        Transform view, viewMuzzle, origMuzzle; Renderer viewFlash; Light viewLight;
        Quaternion baseRot; Vector3 recoil, recoilVel; float kickPitch, kickVel, sprintW, reloadW, blockW, aimW;
        float lastYaw, lastPitch; Vector2 sway;
        public bool Active => view && view.gameObject.activeSelf;

        void Start()
        {
            if (!rig) rig = FindFirstObjectByType<ShoulderRig>();
            if (!rifle) rifle = FindFirstObjectByType<PaintRifle>();
            if (!body) body = FindFirstObjectByType<PlayerMotor>()?.GetComponentInChildren<CharacterAnimator>();
            if (rifle) rifle.Fired += OnFired;
        }
        void OnDestroy() { if (rifle) rifle.Fired -= OnFired; }

        void Build()
        {
            if (view || !body || !body.weapon || !rig || !rig.cam) return;
            var src = body.weapon;
            view = Instantiate(src.gameObject, rig.cam.transform).transform; view.name = "FP_Viewmodel";
            foreach (var mb in view.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mb);
            foreach (var c in view.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in view.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
            view.localScale = src.lossyScale;
            // the barrel points from the rifle origin to its muzzle: align that with the camera's forward, keep the top up
            if (body.muzzle)
            {
                Vector3 barrel = (Quaternion.Inverse(src.rotation) * (body.muzzle.position - src.position)).normalized;
                Vector3 up = Quaternion.Inverse(src.rotation) * Vector3.up;   // calibrated aim pose: top of the rifle ~ world up
                up = Vector3.ProjectOnPlane(up, barrel).normalized;
                baseRot = Quaternion.Inverse(Quaternion.LookRotation(barrel, up));
                viewMuzzle = FindByName(view, body.muzzle.name);
            }
            else baseRot = Quaternion.identity;
            if (rifle && rifle.muzzleFlash) { var f = FindByName(view, rifle.muzzleFlash.name); viewFlash = f ? f.GetComponent<Renderer>() : null; }
            if (rifle && rifle.muzzleLight) { var l = FindByName(view, rifle.muzzleLight.name); viewLight = l ? l.GetComponent<Light>() : null; }
        }
        static Transform FindByName(Transform root, string n) { foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }

        void OnFired()
        {
            if (!Active) return;
            recoilVel += new Vector3(0f, 0.02f, -0.9f); kickVel += 55f;
            if (viewFlash) viewFlash.enabled = rifle.muzzleFlash && rifle.muzzleFlash.enabled;
            if (viewLight) { viewLight.enabled = rifle.muzzleLight && rifle.muzzleLight.enabled; if (viewLight.enabled) viewLight.intensity = rifle.muzzleLight.intensity; }
        }

        void LateUpdate()
        {
            bool want = rig && rig.firstPersonTrial && rig.enabled && rifle && rifle.isActiveAndEnabled && !FirstPersonBody.Active;   // fallback viewmodel only
            if (want && !view) Build();
            if (!view) return;
            if (view.gameObject.activeSelf != want) view.gameObject.SetActive(want);
            if (rifle) { if (want && viewMuzzle) { if (rifle.muzzle != viewMuzzle) { origMuzzle = rifle.muzzle; rifle.muzzle = viewMuzzle; } } else if (origMuzzle && rifle.muzzle == viewMuzzle) rifle.muzzle = origMuzzle; }
            if (!want) return;

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var motor = rifle.motor;
            aimW = Springs.Damp(aimW, rig.AimMode > 0 ? 1f : 0f, 16f, dt);
            sprintW = Springs.Damp(sprintW, motor && motor.Sprinting ? 1f : 0f, 9f, dt);
            reloadW = Springs.Damp(reloadW, rifle.Reloading ? 1f : 0f, 10f, dt);
            // obstruction: if a wall is closer than the barrel, pull the rifle in and down instead of clipping
            float block = 0f;
            if (Physics.Raycast(rig.cam.transform.position, rig.cam.transform.forward, out var hit, 0.85f, ~((1 << 2) | (1 << 8) | (1 << 9) | (1 << 11) | (1 << 12)), QueryTriggerInteraction.Ignore))
                block = Mathf.Clamp01((0.85f - hit.distance) / 0.5f);
            blockW = Springs.Damp(blockW, block, 18f, dt);
            // restrained sway from look speed (no bob, no roll)
            float dy = Mathf.DeltaAngle(lastYaw, rig.Yaw), dp = Mathf.DeltaAngle(lastPitch, rig.Pitch); lastYaw = rig.Yaw; lastPitch = rig.Pitch;
            sway = Vector2.Lerp(sway, new Vector2(Mathf.Clamp(-dy * 0.0025f, -0.03f, 0.03f), Mathf.Clamp(dp * 0.0025f, -0.03f, 0.03f)), 1f - Mathf.Exp(-12f * dt)) * (1f - aimW * 0.8f);
            // recoil springs (return cleanly)
            recoil = Springs.Step(recoil, ref recoilVel, Vector3.zero, 18f, dt);
            kickPitch = Springs.Step(kickPitch, ref kickVel, 0f, 18f, dt);

            Vector3 pos = Vector3.Lerp(hipPos, adsPos, aimW);
            pos += new Vector3(sway.x, sway.y, 0f) + recoil;
            pos += new Vector3(0.04f, -0.1f, -0.06f) * sprintW + new Vector3(0f, -0.13f, -0.04f) * reloadW + new Vector3(0f, -0.08f, -0.22f) * blockW;
            Quaternion rot = Quaternion.Euler(euler) * Quaternion.Euler(-kickPitch * (1f - aimW * 0.5f) - 18f * sprintW - 14f * reloadW - 28f * blockW, -22f * sprintW, 24f * reloadW) * baseRot;
            view.localPosition = pos; view.localRotation = rot;
            // reload: the canister visibly leaves and returns (the real seat time drives it)
            if (viewFlash) viewFlash.enabled = rifle.muzzleFlash && rifle.muzzleFlash.enabled;
            if (viewLight) { viewLight.enabled = rifle.muzzleLight && rifle.muzzleLight.enabled; if (viewLight.enabled) viewLight.intensity = rifle.muzzleLight.intensity; }
        }
    }
}
