using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Attaches each rifle to its character's right-hand bone, calibrated from the real Mixamo aiming pose:
    // barrel along right palm -> left palm, grip in the right palm, left-hand target where the clip's left hand is.
    public static class WeaponRig
    {
        const string AimClip = "Assets/NeonProtocol/Art/Characters/Swat/Anims/Swat@IdleAiming.fbx";

        [MenuItem("Neon Protocol/Calibrate Weapons On Hands")]
        public static string CalibrateAll()
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(AimClip).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
            int n = 0; var log = "";
            foreach (var ca in Object.FindObjectsByType<CharacterAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var rifle = ca.weapon; if (!rifle) continue;
                var anim = ca.GetComponent<Animator>();
                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(ca.gameObject, clip, 0.2f); AnimationMode.EndSampling();
                var rh = anim.GetBoneTransform(HumanBodyBones.RightHand); var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                // Mixamo rigs use "mixamorig:" or "mixamorig1:" prefixes: match by suffix
                Transform Child(Transform t, string suffix) { foreach (Transform c in t) if (c.name.EndsWith(suffix)) return c; return t; }
                Vector3 rPalm = (rh.position + Child(rh, "RightHandMiddle1").position) * 0.5f;
                Vector3 lPalm = (lh.position + Child(lh, "LeftHandMiddle1").position) * 0.5f;
                // Mixamo aim clips aim along the character's forward: the authored rifle barrel follows that axis.
                Vector3 f = ca.transform.forward;
                Quaternion rot = Quaternion.LookRotation(f, ca.transform.up);
                var rGrip = rifle.Find("RightGrip"); var lGrip = rifle.Find("LeftGrip");
                Vector3 gripLocal = rGrip ? rGrip.localPosition : Vector3.zero;
                rifle.SetParent(rh, true);
                rifle.SetPositionAndRotation(rPalm - rot * gripLocal, rot);
                if (lGrip)
                {
                    // foregrip point on the gun, 30 cm ahead of the grip; target the WRIST so the palm lands on it
                    Vector3 fore = rifle.TransformPoint(new Vector3(0f, -0.02f, gripLocal.z + 0.30f));
                    lGrip.position = fore - (lPalm - lh.position);
                    lGrip.rotation = lh.rotation;
                }
                AnimationMode.StopAnimationMode();
                ca.rightGrip = rGrip; ca.leftGrip = lGrip; ca.muzzle = rifle.Find("Muzzle");
                EditorUtility.SetDirty(ca); EditorUtility.SetDirty(rifle);
                log += $"{ca.transform.parent?.name}: palms {Vector3.Distance(rPalm, lPalm):F2}m; ";
                n++;
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            return $"calibrated {n} rifles | {log}";
        }
    }
}
