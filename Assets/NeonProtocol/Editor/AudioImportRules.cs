using UnityEditor;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Web budget for audio: Vorbis everywhere, mono, 22 kHz for effects; music compressed in memory.
    public class AudioImportRules : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/NeonProtocol/Audio")) return;
            var ai = (AudioImporter)assetImporter;
            bool music = assetPath.Contains("/Music/");
            ai.forceToMono = true;
            ai.loadInBackground = music;
            var s = ai.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = music ? 0.4f : 0.5f;
            s.loadType = music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.sampleRateSetting = music ? AudioSampleRateSetting.PreserveSampleRate : AudioSampleRateSetting.OverrideSampleRate;
            s.sampleRateOverride = 22050;
            ai.defaultSampleSettings = s;
        }
    }
}
