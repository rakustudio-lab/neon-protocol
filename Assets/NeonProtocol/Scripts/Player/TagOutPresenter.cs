using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonProtocol
{
    // Makes being tagged readable: desaturate + vignette, camera pulls back from the fall, fade to dark,
    // respawn behind the fade, fade back in with spawn protection.
    [RequireComponent(typeof(TagHealth))]
    public class TagOutPresenter : MonoBehaviour
    {
        public float respawnAt = 3.5f;
        Volume vol; ColorAdjustments ca; Vignette vg; ShoulderRig rig; float baseDistance;

        void Start()
        {
            rig = FindFirstObjectByType<ShoulderRig>(); if (rig) baseDistance = rig.distance;
            var go = new GameObject("_TagOutVolume"); vol = go.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 20; vol.weight = 0f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            ca = prof.Add<ColorAdjustments>(true); ca.saturation.Override(-85f); ca.postExposure.Override(-0.4f);
            vg = prof.Add<Vignette>(true); vg.intensity.Override(0.5f); vg.color.Override(new Color(0.35f, 0f, 0.25f));
            vol.sharedProfile = prof;
            GetComponent<TagHealth>().Tagged += () => StartCoroutine(Play());
        }

        IEnumerator Play()
        {
            float t = 0f;
            while (t < respawnAt + 0.9f)
            {
                t += Time.deltaTime;
                vol.weight = Mathf.Clamp01(t / 0.35f) * (t < respawnAt + 0.1f ? 1f : Mathf.Clamp01(1f - (t - respawnAt - 0.1f) / 0.8f));
                // fade to dark just before the respawn teleport, fade back in after it
                float dark = Mathf.Clamp01((t - (respawnAt - 0.45f)) / 0.4f) * Mathf.Clamp01(1f - (t - respawnAt - 0.05f) / 0.5f);
                ca.postExposure.value = Mathf.Lerp(-0.4f, -7f, dark);
                if (rig) rig.distance = baseDistance + Mathf.Clamp01(t / 0.6f) * 1.4f * (t < respawnAt ? 1f : 0f);
                yield return null;
            }
            vol.weight = 0f; if (rig) rig.distance = baseDistance;
        }
    }
}
