using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Import rules for the Mixamo competitor and its clips (lessons carried over from the GEM GATE prototype):
    //  * every clip retargets through the character's own avatar (separate avatars per clip bend the knees);
    //  * height is baked into the pose (crouch/tag-out go down), horizontal root motion is discarded by the controller;
    //  * web budget: character textures are capped at 1024 px.
    public class CharacterImportRules : AssetPostprocessor
    {
        public const string Root = "Assets/NeonProtocol/Art/Characters/Swat";
        public const string CryptoRoot = "Assets/NeonProtocol/Art/Characters/Crypto";   // CRYPTO (Mixamo Ch45_nonPBR), the player
        static readonly string[] LoopKeywords = { "Idle", "Walk", "Run", "Sprint", "JumpLoop" };

        public override uint GetVersion() => 3;

        void OnPreprocessModel()
        {
            if (assetPath.StartsWith(CryptoRoot) && assetPath.EndsWith(".fbx"))
            {
                // own avatar; plays the same humanoid clips (retargeted through Mecanim). Textures are embedded in the FBX.
                var ci = (ModelImporter)assetImporter;
                ci.animationType = ModelImporterAnimationType.Human;
                ci.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                ci.importAnimation = false;
                ci.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                ci.importCameras = false; ci.importLights = false; ci.importBlendShapes = false;
                ci.isReadable = false; ci.meshCompression = ModelImporterMeshCompression.Medium;
                return;
            }
            if (!assetPath.StartsWith(Root)) return;
            var im = (ModelImporter)assetImporter;
            im.animationType = ModelImporterAnimationType.Human;
            im.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            if (assetPath.Contains("/Anims/"))
            {
                var avatar = AssetDatabase.LoadAllAssetsAtPath(Root + "/Swat.fbx").OfType<Avatar>().FirstOrDefault();
                if (avatar) { im.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; im.sourceAvatar = avatar; }
                im.importAnimation = true;
            }
            im.materialImportMode = ModelImporterMaterialImportMode.None;
            im.importCameras = false; im.importLights = false; im.importBlendShapes = false;
            im.isReadable = false;
            im.meshCompression = ModelImporterMeshCompression.Medium;
            im.animationCompression = ModelImporterAnimationCompression.Optimal;
            im.resampleCurves = true;
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Root + "/Anims/")) return;
            var im = (ModelImporter)assetImporter;
            string clipName = System.IO.Path.GetFileNameWithoutExtension(assetPath).Split('@').Last();
            bool loop = LoopKeywords.Any(k => clipName.Contains(k)) && !clipName.StartsWith("Death");
            var clips = im.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = clipName;
                bool turnClip = clipName.StartsWith("Turn");
                c.loopTime = loop || turnClip;   // turn-in-place repeats its 90-degree step for bigger turns
                // turn clips keep their root rotation as root motion: the body yaw is driven by the authored turn, feet stay planted
                c.lockRootRotation = !turnClip; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                c.lockRootPositionXZ = false; c.keepOriginalPositionXZ = false;
            }
            im.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root) && !assetPath.StartsWith(CryptoRoot)) return;
            var ti = (TextureImporter)assetImporter;
            ti.maxTextureSize = 1024;
            ti.mipmapEnabled = true;
            if (assetPath.Contains("_Normal")) ti.textureType = TextureImporterType.NormalMap;
            if (assetPath.Contains("_Glossiness")) ti.sRGBTexture = false;
            ti.textureCompression = TextureImporterCompression.Compressed;
        }
    }
}
