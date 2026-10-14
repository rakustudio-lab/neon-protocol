using UnityEngine;

namespace NeonProtocol
{
    // Seated riding pose for CRYPTO on the lightcycle, authored as humanoid muscle values (no riding clip exists in
    // our licensed set). Works on any humanoid avatar. `lean` (from the bike) tilts the spine into turns.
    [RequireComponent(typeof(Animator))]
    public class RiderPose : MonoBehaviour
    {
        [System.Serializable] public struct M { public string muscle; public float value; }
        public M[] pose = {
            new M { muscle = "Spine Front-Back", value = -0.75f }, new M { muscle = "Chest Front-Back", value = -0.6f },
            new M { muscle = "Neck Nod Down-Up", value = 0.3f }, new M { muscle = "Head Nod Down-Up", value = 0.6f },
            new M { muscle = "Left Upper Leg Front-Back", value = -0.8f }, new M { muscle = "Right Upper Leg Front-Back", value = -0.8f },
            new M { muscle = "Left Upper Leg In-Out", value = 0.15f }, new M { muscle = "Right Upper Leg In-Out", value = 0.15f },
            new M { muscle = "Left Lower Leg Stretch", value = -0.7f }, new M { muscle = "Right Lower Leg Stretch", value = -0.7f },
            new M { muscle = "Left Foot Up-Down", value = 0.3f }, new M { muscle = "Right Foot Up-Down", value = 0.3f },
            new M { muscle = "Left Arm Down-Up", value = 0.05f }, new M { muscle = "Right Arm Down-Up", value = 0.05f },
            new M { muscle = "Left Arm Front-Back", value = -1f }, new M { muscle = "Right Arm Front-Back", value = -1f },
            new M { muscle = "Left Forearm Stretch", value = 0.85f }, new M { muscle = "Right Forearm Stretch", value = 0.85f },
            new M { muscle = "Left Hand Down-Up", value = 0.1f }, new M { muscle = "Right Hand Down-Up", value = 0.1f },
            // R4: fingers closed around the grips
            new M { muscle = "Left Index 1 Stretched", value = 0.95f }, new M { muscle = "Left Index 2 Stretched", value = 0.95f }, new M { muscle = "Left Index 3 Stretched", value = 0.95f }, new M { muscle = "Left Middle 1 Stretched", value = 0.95f }, new M { muscle = "Left Middle 2 Stretched", value = 0.95f }, new M { muscle = "Left Middle 3 Stretched", value = 0.95f }, new M { muscle = "Left Ring 1 Stretched", value = 0.95f }, new M { muscle = "Left Ring 2 Stretched", value = 0.95f }, new M { muscle = "Left Ring 3 Stretched", value = 0.95f }, new M { muscle = "Left Little 1 Stretched", value = 0.95f }, new M { muscle = "Left Little 2 Stretched", value = 0.95f }, new M { muscle = "Left Little 3 Stretched", value = 0.95f }, new M { muscle = "Left Thumb 2 Stretched", value = 0.6f }, new M { muscle = "Right Index 1 Stretched", value = 0.95f }, new M { muscle = "Right Index 2 Stretched", value = 0.95f }, new M { muscle = "Right Index 3 Stretched", value = 0.95f }, new M { muscle = "Right Middle 1 Stretched", value = 0.95f }, new M { muscle = "Right Middle 2 Stretched", value = 0.95f }, new M { muscle = "Right Middle 3 Stretched", value = 0.95f }, new M { muscle = "Right Ring 1 Stretched", value = 0.95f }, new M { muscle = "Right Ring 2 Stretched", value = 0.95f }, new M { muscle = "Right Ring 3 Stretched", value = 0.95f }, new M { muscle = "Right Little 1 Stretched", value = 0.95f }, new M { muscle = "Right Little 2 Stretched", value = 0.95f }, new M { muscle = "Right Little 3 Stretched", value = 0.95f }, new M { muscle = "Right Thumb 2 Stretched", value = 0.6f },
        };
        public float lean;            // degrees, set by the bike
        public float seatDrop = 0f;   // pelvis lowered onto the seat (local hips units)
        HumanPoseHandler handler; HumanPose hp; int[] idx; int spineLR = -1, chestLR = -1;
        Transform hips; Vector3 hipsLocal; Quaternion hipsRot;

        void OnEnable()
        {
            var an = GetComponent<Animator>();
            if (!an.avatar || !an.avatar.isHuman) { enabled = false; return; }
            an.runtimeAnimatorController = null;
            handler = new HumanPoseHandler(an.avatar, transform);
            idx = new int[pose.Length];
            for (int i = 0; i < pose.Length; i++) idx[i] = System.Array.IndexOf(HumanTrait.MuscleName, pose[i].muscle);
            spineLR = System.Array.IndexOf(HumanTrait.MuscleName, "Spine Left-Right");
            chestLR = System.Array.IndexOf(HumanTrait.MuscleName, "Chest Left-Right");
            hips = an.GetBoneTransform(HumanBodyBones.Hips); if (hips) { hipsLocal = hips.localPosition; hipsRot = hips.localRotation; }
        }

        void OnDisable() { handler?.Dispose(); handler = null; }

        void LateUpdate()
        {
            if (handler == null) return;
            handler.GetHumanPose(ref hp);
            for (int i = 0; i < hp.muscles.Length; i++) hp.muscles[i] = 0f;
            for (int i = 0; i < pose.Length; i++) if (idx[i] >= 0) hp.muscles[idx[i]] = pose[i].value;
            float l = Mathf.Clamp(-lean / 40f, -0.6f, 0.6f);
            if (spineLR >= 0) hp.muscles[spineLR] = l; if (chestLR >= 0) hp.muscles[chestLR] = l * 0.6f;
            handler.SetHumanPose(ref hp);
            // muscles only: the seat holds the pelvis where it was authored (SetHumanPose would otherwise move the body)
            if (hips) { hips.localPosition = hipsLocal + new Vector3(0f, seatDrop, 0f); hips.localRotation = hipsRot; }
        }
    }
}
