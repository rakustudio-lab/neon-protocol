using UnityEngine;
using UnityEngine.Rendering;

namespace NeonProtocol
{
    // When the camera is pressed against the character (tight corners), render the body as shadows-only
    // so it never fills the screen or clips through the lens.
    public class CharacterFade : MonoBehaviour
    {
        public ShoulderRig rig;
        Renderer[] rends; bool hidden;
        void Start() { rends = GetComponentsInChildren<Renderer>(); if (!rig) rig = FindFirstObjectByType<ShoulderRig>(); }
        void LateUpdate()
        {
            bool hide = !FinalityElevator.Active && rig && rig.enabled && rig.CloseToCharacter && !FirstPersonBody.Active;   // true first person keeps the body (arms + rifle) visible
            if (hide == hidden) return;
            hidden = hide;
            foreach (var r in rends) if (r) r.shadowCastingMode = hide ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }
    }
}
