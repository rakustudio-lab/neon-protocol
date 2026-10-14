using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Repeatable web build. Called from the live Editor (eval) or batch mode (-executeMethod).
    public static class WebBuild
    {
        public const string OutDir = "Builds/Web";

        [MenuItem("Neon Protocol/Apply Web Player Settings")]
        public static void ApplySettings()
        {
            var t = NamedBuildTarget.WebGL;
            PlayerSettings.companyName = "Raku Studio";
            PlayerSettings.productName = "NEON PROTOCOL";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.initialMemorySize = 256;
            PlayerSettings.WebGL.maximumMemorySize = 2048;
            PlayerSettings.WebGL.showDiagnostics = false;
            PlayerSettings.SetManagedStrippingLevel(t, ManagedStrippingLevel.High);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.WebGL.template = "PROJECT:NeonProtocol";
            EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.DXT;
        }

        [MenuItem("Neon Protocol/Build Web")]
        public static string Build()
        {
            ApplySettings();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) scenes = new[] { UnityEngine.SceneManagement.SceneManager.GetActiveScene().path };
            if (Directory.Exists(OutDir)) Directory.Delete(OutDir, true);
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            var msg = $"result={s.result} errors={s.totalErrors} warnings={s.totalWarnings} " +
                      $"size={s.totalSize / (1024f * 1024f):F1}MB time={s.totalTime.TotalSeconds:F0}s";
            Debug.Log("[WebBuild] " + msg);
            File.WriteAllText(Path.Combine("Builds", "web-build-report.txt"), msg);
            if (Application.isBatchMode && s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
            return msg;
        }
    }
}
