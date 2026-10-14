using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Builds the competitor Animator Controller from the Mixamo clips.
    // Blend-tree positions use each clip's measured root speed, so the feet match the real movement speed.
    public static class AnimatorBuilder
    {
        const string AnimDir = CharacterImportRules.Root + "/Anims";
        public const string ControllerPath = "Assets/NeonProtocol/Animation/Competitor.controller";

        static AnimationClip Clip(string name)
        {
            var c = AssetDatabase.LoadAllAssetsAtPath($"{AnimDir}/Swat@{name}.fbx").OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview"));
            if (!c) Debug.LogWarning("[AnimatorBuilder] missing clip " + name);
            return c;
        }

        static Vector2 Vel(AnimationClip c, Vector2 dir)
        {
            // averageSpeed is the root velocity in model space (x = right, z = forward)
            if (!c) return dir;
            var v = c.averageSpeed;
            var p = new Vector2(v.x, v.z);
            return p.sqrMagnitude > 0.01f ? p : dir * 0.01f;
        }

        [MenuItem("Neon Protocol/Build Competitor Animator")]
        public static string Build()
        {
            System.IO.Directory.CreateDirectory("Assets/NeonProtocol/Animation");
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ac)
            {
                // reset in place so scene references survive
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath)) if (sub != ac && !(sub is AnimatorStateMachine && ac.layers.Length > 0 && sub == ac.layers[0].stateMachine)) Object.DestroyImmediate(sub, true);
                while (ac.parameters.Length > 0) ac.RemoveParameter(0);
                var l0 = new AnimatorControllerLayer { name = "Base Layer", stateMachine = new AnimatorStateMachine { name = "Base Layer" } };
                AssetDatabase.AddObjectToAsset(l0.stateMachine, ac);
                ac.layers = new[] { l0 };
            }
            else ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ac.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ac.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            ac.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
            ac.AddParameter("TaggedOut", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Turn", AnimatorControllerParameterType.Float);
            ac.AddParameter("Turning", AnimatorControllerParameterType.Bool);
            ac.AddParameter("VelY", AnimatorControllerParameterType.Float);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("TurnSpeedMul", AnimatorControllerParameterType.Float);
            var sm = ac.layers[0].stateMachine;
            var log = new List<string>();

            BlendTree Tree(string name, string idle, string[] rings)
            {
                var bt = new BlendTree { name = name, blendType = BlendTreeType.FreeformDirectional2D, blendParameter = "MoveX", blendParameterY = "MoveZ", useAutomaticThresholds = false };
                AssetDatabase.AddObjectToAsset(bt, ac);
                bt.AddChild(Clip(idle), Vector2.zero);
                var dirs = new Dictionary<string, Vector2> {
                    { "Forward", new Vector2(0, 1) }, { "ForwardLeft", new Vector2(-0.7f, 0.7f) }, { "ForwardRight", new Vector2(0.7f, 0.7f) },
                    { "Left", new Vector2(-1, 0) }, { "Right", new Vector2(1, 0) },
                    { "Backward", new Vector2(0, -1) }, { "BackwardLeft", new Vector2(-0.7f, -0.7f) }, { "BackwardRight", new Vector2(0.7f, -0.7f) } };
                foreach (var ring in rings)
                    foreach (var kv in dirs)
                    {
                        var c = Clip(ring + kv.Key);
                        if (!c) continue;
                        var pos = Vel(c, kv.Value);
                        bt.AddChild(c, pos);
                        log.Add($"{c.name}:{pos.magnitude:F2}");
                    }
                return bt;
            }

            var standTree = Tree("Stand", "Idle", new[] { "Walk", "Run" });
            foreach (var s in new[] { "SprintForward", "SprintForwardLeft", "SprintForwardRight" })
            {
                var c = Clip(s); if (!c) continue;
                var d = s.EndsWith("Left") ? new Vector2(-0.7f, 0.7f) : s.EndsWith("Right") ? new Vector2(0.7f, 0.7f) : Vector2.up;
                standTree.AddChild(c, Vel(c, d)); log.Add($"{c.name}:{Vel(c, d).magnitude:F2}");
            }
            var crouchTree = Tree("Crouch", "IdleCrouching", new[] { "WalkCrouching" });

            var stand = sm.AddState("Stand"); stand.motion = standTree; stand.iKOnFeet = true;
            var crouch = sm.AddState("Crouch"); crouch.motion = crouchTree; crouch.iKOnFeet = true;
            var jumpLoop = sm.AddState("Air"); jumpLoop.motion = Clip("JumpLoop");
            var tagged = sm.AddState("TaggedOut"); tagged.motion = Clip("DeathFromTheFront");
            sm.defaultState = stand;

            // Turn in place (rescue 2026-10-11): stepping turn clips blended by signed turn rate; the body yaw itself is code-driven.
            var turnTree = new BlendTree { name = "TurnInPlace", blendType = BlendTreeType.Simple1D, blendParameter = "Turn", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(turnTree, ac);
            turnTree.AddChild(Clip("Turn90Left"), -1f); turnTree.AddChild(Clip("Idle"), 0f); turnTree.AddChild(Clip("Turn90Right"), 1f);
            var turn = sm.AddState("TurnInPlace"); turn.motion = turnTree; turn.iKOnFeet = true; turn.speed = 1f; turn.speedParameterActive = true; turn.speedParameter = "TurnSpeedMul";
            // Jump take-off and landing
            var jumpUp = sm.AddState("JumpUp"); jumpUp.motion = Clip("JumpUp"); jumpUp.speed = 1.3f;
            var land = sm.AddState("Land"); land.motion = Clip("JumpDown"); land.iKOnFeet = true; land.speed = 1.4f;

            AnimatorStateTransition T(AnimatorState a, AnimatorState b, float dur)
            {
                var t = a.AddTransition(b); t.hasExitTime = false; t.duration = dur; t.interruptionSource = TransitionInterruptionSource.Destination; return t;
            }
            T(stand, crouch, 0.18f).AddCondition(AnimatorConditionMode.If, 0, "Crouch");
            T(crouch, stand, 0.18f).AddCondition(AnimatorConditionMode.IfNot, 0, "Crouch");
            foreach (var from in new[] { stand, crouch, turn })
            {
                var up = T(from, jumpUp, 0.08f); up.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded"); up.AddCondition(AnimatorConditionMode.Greater, 1.5f, "VelY");
                var fall = T(from, jumpLoop, 0.15f); fall.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded"); fall.AddCondition(AnimatorConditionMode.Less, 1.5f, "VelY");
            }
            T(stand, turn, 0.15f).AddCondition(AnimatorConditionMode.If, 0, "Turning");
            T(turn, stand, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Turning");
            T(turn, crouch, 0.18f).AddCondition(AnimatorConditionMode.If, 0, "Crouch");
            T(jumpUp, jumpLoop, 0.2f).AddCondition(AnimatorConditionMode.Less, 0.5f, "VelY");
            T(jumpUp, land, 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            T(jumpLoop, land, 0.06f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            var landRun = T(land, stand, 0.12f); landRun.AddCondition(AnimatorConditionMode.Greater, 1.2f, "Speed");
            var landEnd = land.AddTransition(stand); landEnd.hasExitTime = true; landEnd.exitTime = 0.55f; landEnd.duration = 0.2f;
            var anyTag = sm.AddAnyStateTransition(tagged); anyTag.hasExitTime = false; anyTag.duration = 0.12f; anyTag.canTransitionToSelf = false;
            anyTag.AddCondition(AnimatorConditionMode.If, 0, "TaggedOut");
            T(tagged, stand, 0.3f).AddCondition(AnimatorConditionMode.IfNot, 0, "TaggedOut");

            // Upper-body hit reaction (additive feel via override on the upper body mask)
            var mask = new AvatarMask { name = "UpperBody" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers;
                mask.SetHumanoidBodyPartActive(part, upper);
            }
            AssetDatabase.AddObjectToAsset(mask, ac);
            ac.AddLayer(new AnimatorControllerLayer { name = "UpperHit", defaultWeight = 1f, avatarMask = mask, stateMachine = new AnimatorStateMachine { name = "UpperHit" } });
            var layers = ac.layers;
            var hsm = layers[1].stateMachine;
            AssetDatabase.AddObjectToAsset(hsm, ac);
            var empty = hsm.AddState("Empty"); hsm.defaultState = empty;
            var hit = hsm.AddState("Hit"); hit.motion = Clip("HitReaction"); hit.speed = 1.6f;
            var toHit = hsm.AddAnyStateTransition(hit); toHit.hasExitTime = false; toHit.duration = 0.05f; toHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
            var back = hit.AddTransition(empty); back.hasExitTime = true; back.exitTime = 0.55f; back.duration = 0.2f;
            layers[1].defaultWeight = 0.7f;
            layers[0].iKPass = true;
            ac.layers = layers;

            AssetDatabase.SaveAssets();
            return "built " + ControllerPath + " | " + string.Join(" ", log.Take(40));
        }
    }
}
