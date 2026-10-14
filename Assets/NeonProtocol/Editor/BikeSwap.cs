using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // R4: replaces the box lightcycle with the founder's lightcycle model (Assets/ThirdParty/Lightcycle_Guidon/guidon.fbx:
    // guidon = handlebar, Chassis, Front Wheel / Front Wheel.002, BackLoop, Poignets = grips). Placed from measured
    // bounds (wheels on the road, bike forward = +z), wheels re-pivoted so they spin, glow materials from the kit,
    // grip targets for RiderHands. The old box parts are disabled, not deleted. Idempotent.
    public static class BikeSwap
    {
        [MenuItem("Neon Protocol/Finality/Swap In Lightcycle Model")]
        public static string Swap(float length = 2.5f, float riderDrop = 0.12f, float riderZ = 0.84f)
        {
            var lc = Object.FindFirstObjectByType<Lightcycle>(FindObjectsInactive.Include); if (!lc) return "no bike";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/Lightcycle_Guidon/guidon.fbx"); if (!src) return "no fbx";
            var pivot = lc.body;   // LeanPivot (leans into turns)
            var old = pivot.Find("LC_Model"); if (old) Object.DestroyImmediate(old.gameObject);
            foreach (var r in lc.GetComponentsInChildren<Renderer>(true))
                if (!r.transform.IsChildOf(lc.riderRoot) && (r.name is "Bike_Body" or "Wheel_Front" or "Wheel_Rear" or "Launcher" or "Handlebar" or "Grip_L" or "Grip_R" or "Stem")) r.enabled = false;
            var m = (GameObject)PrefabUtility.InstantiatePrefab(src, pivot); m.name = "LC_Model";
            PrefabUtility.UnpackPrefabInstance(m, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            m.transform.localPosition = Vector3.zero;
            m.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * src.transform.localRotation;   // source -x (grips end) -> bike +z
            // scale from the chassis length
            Renderer chassis = null; foreach (var r in m.GetComponentsInChildren<Renderer>()) if (r.name == "Chassis") chassis = r;
            if (!chassis) return "no Chassis";
            float curLen = Vector3.Scale(chassis.bounds.size, new Vector3(0, 0, 1)).magnitude; if (curLen < 1e-4f) curLen = chassis.bounds.size.z;
            m.transform.localScale = src.transform.localScale * (length / Mathf.Max(curLen, 1e-4f));
            // wheels on the road, chassis centred
            Bounds all = chassis.bounds; foreach (var r in m.GetComponentsInChildren<Renderer>()) all.Encapsulate(r.bounds);
            Vector3 cLocal = lc.transform.InverseTransformPoint(chassis.bounds.center);
            float bottom = lc.transform.InverseTransformPoint(all.min).y;
            m.transform.position += lc.transform.TransformVector(new Vector3(-cLocal.x, -bottom, 0.05f - cLocal.z));
            // materials: dark plate body, cyan glow wheels / loop, violet grips
            var plate = AssetDatabase.LoadAssetAtPath<Material>("Assets/NeonProtocol/Art/Materials/Kit/K_Plate.mat");
            var cyan = AssetDatabase.LoadAssetAtPath<Material>("Assets/NeonProtocol/Art/Materials/Kit/G_Cyan.mat");
            var violet = AssetDatabase.LoadAssetAtPath<Material>("Assets/NeonProtocol/Art/Materials/Kit/G_Violet.mat");
            Transform front = null, rear = null, grips = null;
            foreach (var r in m.GetComponentsInChildren<Renderer>())
            {
                bool wheel = r.name.StartsWith("Front Wheel");
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = wheel || r.name == "BackLoop" ? (i == 0 ? plate : cyan) : r.name == "Poignets" ? violet : (i == 0 ? plate : cyan);
                r.sharedMaterials = mats; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (wheel) { float z = lc.transform.InverseTransformPoint(r.bounds.center).z; if (front == null || z > lc.transform.InverseTransformPoint(front.position).z) { rear = front ?? rear; front = r.transform; } else rear = r.transform; }
                if (r.name == "Poignets") grips = r.transform;
            }
            // wheel pivots at the wheel centre with the bike's axes (Lightcycle spins wheelFront / wheelRear about local X)
            Transform Repivot(Transform w, string n)
            {
                if (!w) return null; var c = w.GetComponent<Renderer>().bounds.center;
                var p = new GameObject(n).transform; p.SetParent(pivot, false); p.position = c; p.rotation = lc.transform.rotation; p.SetParent(m.transform, true);
                w.SetParent(p, true); return p;
            }
            var fp = Repivot(front, "WheelPivot_Front"); var rp = Repivot(rear, "WheelPivot_Rear");
            if (fp) lc.wheelFront = fp; if (rp) lc.wheelRear = rp;
            // grip targets at the two ends of the grips mesh (bike frame)
            var gr = grips ? grips.GetComponent<Renderer>().bounds : new Bounds(lc.transform.TransformPoint(0, 1.1f, 0.6f), new Vector3(0.9f, 0.1f, 0.1f));
            Vector3 gc = lc.transform.InverseTransformPoint(gr.center); float half = Mathf.Abs(lc.transform.InverseTransformVector(gr.extents).x) * 0.82f;
            half = Mathf.Clamp(half * 0.85f, 0.22f, 0.55f);
            float topY = lc.transform.InverseTransformPoint(gr.center + Vector3.up * gr.extents.y).y - 0.05f;   // the bar you hold is the TOP of the L-shaped grips
            Transform Grip(string n, float side) { var t = pivot.Find(n); if (!t) { t = new GameObject(n).transform; t.SetParent(pivot, false); } t.localPosition = new Vector3(side * half, topY, gc.z); return t; }
            var gl = Grip("GripTarget_L", -1f); var grt = Grip("GripTarget_R", 1f);
            // rider sits on the new seat, hands on the grips
            lc.riderRoot.localPosition = new Vector3(0f, 0.06f - riderDrop, -0.42f + riderZ);
            var hands = lc.riderRoot.GetComponent<RiderHands>() ?? lc.riderRoot.gameObject.AddComponent<RiderHands>();
            hands.bike = lc; hands.gripL = gl; hands.gripR = grt;
            // the launcher muzzle sits just in front of the bars
            if (lc.muzzle) lc.muzzle.localPosition = lc.muzzle.parent.InverseTransformPoint(lc.transform.TransformPoint(gc + new Vector3(0f, -0.15f, 0.35f)));
            EditorUtility.SetDirty(lc); EditorUtility.SetDirty(hands);
            EditorSceneManager.MarkSceneDirty(lc.gameObject.scene); EditorSceneManager.SaveScene(lc.gameObject.scene);
            Bounds nb = chassis.bounds; foreach (var r in m.GetComponentsInChildren<Renderer>()) nb.Encapsulate(r.bounds);
            return $"lightcycle swapped: size {lc.transform.InverseTransformVector(nb.size)} grips local {gc} half {half:F2} front {(fp ? lc.transform.InverseTransformPoint(fp.position).ToString("F2") : "none")} rear {(rp ? lc.transform.InverseTransformPoint(rp.position).ToString("F2") : "none")}";
        }
    }
}
