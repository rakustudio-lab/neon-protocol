using UnityEngine;

namespace NeonProtocol
{
    // While the run hasn't started (menu / briefing), the main camera drifts around the Core reactor so the menu is
    // backed by the live game world instead of a static picture. Runs after ShoulderRig and simply overrides it.
    [DefaultExecutionOrder(500)]
    public class MenuCamera : MonoBehaviour
    {
        public Vector3 focus = new Vector3(0f, 6.2f, 23f);
        public float radius = 10.5f;
        public float height = 2.6f;
        public float speed = 0.045f;
        float t;

        void LateUpdate()
        {
            var d = HeistDirector.Instance;
            if (!d || d.Current != HeistDirector.Phase.Briefing || NPInput.UseOverride || (VaultRoom.Instance && VaultRoom.Instance.Active)) return;   // the Vault uses the player camera
            var cam = Camera.main; if (!cam) return;
            t += Time.unscaledDeltaTime * speed;
            float a = (-90f + 62f * Mathf.Sin(t * 6.2831853f)) * Mathf.Deg2Rad;     // sweep the south half of the vault
            var pos = focus + new Vector3(Mathf.Cos(a) * radius, height - focus.y + 1.2f * Mathf.Sin(t * 9.1f), Mathf.Sin(a) * radius);
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(focus - pos + new Vector3(Mathf.Cos(a) * 2.5f, 0f, 0f));
            cam.fieldOfView = 64f;
        }
    }
}
