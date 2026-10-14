using UnityEngine;

namespace NeonProtocol
{
    // Player-facing settings, persisted in PlayerPrefs (IndexedDB on the web).
    public static class Settings
    {
        public static float CameraShake = 1f;    // 0 = reduced camera effects
        public static float MasterVolume = 0.8f;
        public static int Quality = 1;           // 0 Low, 1 Medium, 2 High

        public static void Load()
        {
            NPInput.Sensitivity = PlayerPrefs.GetFloat("np.sens", 1f);
            NPInput.InvertY = PlayerPrefs.GetInt("np.invertY", 0) == 1;
            CameraShake = PlayerPrefs.GetFloat("np.shake", 1f);
            MasterVolume = PlayerPrefs.GetFloat("np.volume", 0.8f);
            Quality = PlayerPrefs.GetInt("np.quality", DefaultQuality());
            Apply();
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat("np.sens", NPInput.Sensitivity);
            PlayerPrefs.SetInt("np.invertY", NPInput.InvertY ? 1 : 0);
            PlayerPrefs.SetFloat("np.shake", CameraShake);
            PlayerPrefs.SetFloat("np.volume", MasterVolume);
            PlayerPrefs.SetInt("np.quality", Quality);
            PlayerPrefs.Save();
            Apply();
        }

        public static void Apply()
        {
            AudioListener.volume = MasterVolume;
            QualityPresets.Apply(Quality);
        }

        static int DefaultQuality()
        {
            // Conservative default for browsers: Medium unless the GPU looks weak.
            if (SystemInfo.graphicsMemorySize > 0 && SystemInfo.graphicsMemorySize < 2048) return 0;
            return 1;
        }
    }
}
