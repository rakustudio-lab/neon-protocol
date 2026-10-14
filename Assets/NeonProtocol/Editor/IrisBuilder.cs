using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Mounts Astra's Iris relay (NEON_NEXT/encounter/art/relay-iris.fbx) on Relay A as a VISUAL CHILD of the existing
    // relay (the gameplay root, its Relay component and mission wiring are untouched). Re-runnable.
    //  * FBX: Blender face +Z exported with up=Y -> the disc faces Unity +Y; the visual is rotated so +Y faces the catwalk.
    //  * URP materials assigned explicitly (rough dark housing, brushed trim, emissive pads/core driven per renderer).
    //  * Simple colliders only: one thin box for the housing, a box per pad, a capsule for the core (off until exposed).
    //  * The old relay's renderers/colliders are disabled so there is no double charging.
    public static class IrisBuilder
    {
        const string Fbx = "Assets/NeonProtocol/Art/Models/RelayIris/relay-iris.fbx", MatDir = "Assets/NeonProtocol/Art/Materials/Iris";

        static Material Mat(string n, Color baseC, float metal, float smooth, bool emissive)
        {
            System.IO.Directory.CreateDirectory(MatDir);
            string p = $"{MatDir}/{n}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", baseC); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            if (emissive) { m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; m.SetColor("_EmissionColor", Color.black); }
            EditorUtility.SetDirty(m); return m;
        }

        [MenuItem("Neon Protocol/Build Iris Relay A")]
        public static string Build()
        {
            var relayGo = GameObject.Find("Relay_A"); if (!relayGo) return "Relay_A not found";
            var relay = relayGo.GetComponent<Relay>();
            var old = relayGo.transform.Find("IrisVisual"); if (old) Object.DestroyImmediate(old.gameObject);
            // 1) hide the old relay visuals and colliders (gameplay root + Relay component stay)
            int hidden = 0;
            foreach (var r in relayGo.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; hidden++; }
            foreach (var c in relayGo.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var l in relayGo.GetComponentsInChildren<Light>(true)) l.intensity = Mathf.Min(l.intensity, 1.2f);
            relay.acceptsPaint = false;

            // 2) the model, oriented so its face points toward the catwalk (-X, the corridor side)
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var vis = (GameObject)PrefabUtility.InstantiatePrefab(src); PrefabUtility.UnpackPrefabInstance(vis, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            vis.name = "IrisVisual"; vis.transform.SetParent(relayGo.transform, false);
            vis.transform.position = relayGo.transform.position;
            Vector3 face = Vector3.left;
            vis.transform.rotation = Quaternion.LookRotation(Vector3.up, face) ;   // model +Y (its face) -> world -X; model +Z -> world up
            vis.transform.localScale = Vector3.one * 1.35f;
            foreach (var t in vis.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = relayGo.layer;

            var dark = Mat("M_Iris_Housing", new Color(0.07f, 0.075f, 0.09f), 0.65f, 0.42f, false);
            var trim = Mat("M_Iris_Trim", new Color(0.42f, 0.44f, 0.5f), 0.9f, 0.58f, false);
            var pad = Mat("M_Iris_Pad", new Color(0.08f, 0.09f, 0.12f), 0.2f, 0.55f, true);
            var coreM = Mat("M_Iris_Core", new Color(0.05f, 0.06f, 0.08f), 0.1f, 0.8f, true);
            var accent = Mat("M_Iris_Accent", new Color(0.1f, 0.1f, 0.14f), 0.2f, 0.5f, true);
            accent.SetColor("_EmissionColor", new Color(0.4f, 0.9f, 1.4f));
            var iris = relayGo.GetComponent<IrisRelay>() ?? relayGo.AddComponent<IrisRelay>();
            iris.relay = relay;
            var pads = new Renderer[3]; var petals = new Transform[3]; var nums = new TextMeshPro[3];
            foreach (var r in vis.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.name;
                if (n.StartsWith("Target_")) { int k = n[7] - '0'; r.sharedMaterial = pad; pads[k] = r; Collide(r, true, iris, k); }
                else if (n == "Core_target") { r.sharedMaterial = coreM; iris.core = r.transform; iris.coreRenderer = r; var cc = Collide(r, false, iris, 3); iris.coreCollider = cc; }
                else if (n.StartsWith("Petal_")) { int k = n[6] - '0'; r.sharedMaterial = trim; petals[k] = r.transform; }
                else if (n.StartsWith("Accent_")) r.sharedMaterial = accent;
                else if (n.StartsWith("Outer_bezel") || n.StartsWith("Core_bezel") || n.StartsWith("Lock_fin")) r.sharedMaterial = trim;
                else r.sharedMaterial = dark;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            // housing blocker: one thin box behind the pads (shots between pads splat on the housing, not through it)
            var hb = new GameObject("HousingCollider"); hb.transform.SetParent(vis.transform, false); hb.layer = relayGo.layer;
            var box = hb.AddComponent<BoxCollider>(); box.center = new Vector3(0, -0.12f, 0); box.size = new Vector3(1.66f, 0.24f, 1.66f);
            // numbers on the pads (readable without colour)
            for (int k = 0; k < 3; k++)
            {
                if (!pads[k]) continue;
                var go = new GameObject("PadNumber_" + (k + 1)); go.transform.SetParent(vis.transform, false);
                go.transform.position = pads[k].bounds.center + face * 0.05f; go.transform.rotation = Quaternion.LookRotation(-face, Vector3.up); go.transform.localScale = Vector3.one / vis.transform.localScale.x;
                var tmp = go.AddComponent<TextMeshPro>(); tmp.text = (k + 1).ToString(); tmp.fontSize = 1.6f; tmp.alignment = TextAlignmentOptions.Center;
                tmp.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/NeonProtocol/Art/Fonts/Orbitron-900 SDF.asset");
                tmp.rectTransform.sizeDelta = new Vector2(0.4f, 0.3f); tmp.color = Color.white; go.layer = relayGo.layer;
                nums[k] = tmp;
            }
            iris.pads = pads; iris.petals = petals; iris.padNumbers = nums; iris.visual = vis.transform;
            // audio: reuse the relay's source + existing generated clips
            iris.audioSource = relay.audioSource;
            iris.padClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Generated/hit_confirm_1.wav");
            iris.rejectClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Generated/ui_click.wav");
            iris.openClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Generated/reload_pressurize.wav");
            iris.onlineClip = relay.onlineClip;
            // power pulse: relay -> catwalk -> hall reactor
            var pathRoot = relayGo.transform.Find("PulsePath"); if (pathRoot) Object.DestroyImmediate(pathRoot.gameObject);
            pathRoot = new GameObject("PulsePath").transform; pathRoot.SetParent(relayGo.transform, false);
            var pts = new[] { relayGo.transform.position + face * 0.3f, new Vector3(19.4f, 5.3f, -3f), new Vector3(19.4f, 5.3f, -14f), new Vector3(10f, 4.8f, -9f), new Vector3(0f, 3.6f, -3.5f) };
            iris.pulsePath = pts.Select((p, i) => { var t = new GameObject("P" + i).transform; t.SetParent(pathRoot, true); t.position = p; return t; }).ToArray();
            var pl = new GameObject("PulseLight").AddComponent<Light>(); pl.transform.SetParent(pathRoot, false); pl.type = LightType.Point; pl.color = new Color(0.3f, 1f, 1f); pl.range = 7f; pl.intensity = 0f; pl.enabled = false; pl.shadows = LightShadows.None;
            iris.pulseLight = pl;
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere); orb.name = "PulseOrb"; Object.DestroyImmediate(orb.GetComponent<Collider>());
            orb.transform.SetParent(pathRoot, false); orb.transform.localScale = Vector3.one * 0.35f;
            var orbM = AssetDatabase.LoadAssetAtPath<Material>("Assets/NeonProtocol/Art/Materials/Kit/G_Cyan.mat");
            var or = orb.GetComponent<MeshRenderer>(); or.sharedMaterial = orbM; or.enabled = false; or.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            iris.pulseOrb = or;
            // a soft key light on the relay so the hero prop reads (no runtime light per bolt)
            var key = new GameObject("IrisKey").AddComponent<Light>(); key.transform.SetParent(vis.transform, false); key.transform.position = relayGo.transform.position + face * 2.2f + Vector3.up * 0.8f;
            key.type = LightType.Point; key.color = new Color(0.75f, 0.9f, 1f); key.intensity = 3.5f; key.range = 6f; key.shadows = LightShadows.None;

            EditorUtility.SetDirty(iris); EditorSceneManager.MarkSceneDirty(relayGo.scene); EditorSceneManager.SaveScene(relayGo.scene);
            var b = vis.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(vis.transform.position, Vector3.zero), (acc, r) => { acc.Encapsulate(r.bounds); return acc; });
            return $"iris mounted: hidden {hidden} old renderers; pads {pads.Count(x => x)}/3 petals {petals.Count(x => x)}/3 core {(iris.core ? "ok" : "MISSING")}; bounds {b.size}";
        }

        static Collider Collide(Renderer r, bool pad, NeonProtocol.IrisRelay owner, int index)
        {
            foreach (var c in r.GetComponents<Collider>()) Object.DestroyImmediate(c);
            Collider col;
            if (pad) { var bc = r.gameObject.AddComponent<BoxCollider>(); col = bc; }   // mesh bounds
            else { var bc = r.gameObject.AddComponent<BoxCollider>(); col = bc; }
            var it = r.gameObject.GetComponent<IrisTarget>() ?? r.gameObject.AddComponent<IrisTarget>(); it.owner = owner; it.index = index;
            return col;
        }
    }
}
