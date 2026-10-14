using System.Collections.Generic;
using UnityEngine;

namespace NeonProtocol
{
    // When the Core is taken the facility reacts: point lights shift toward alarm magenta and pulse; restored on reset.
    public class AlarmLighting : MonoBehaviour
    {
        public Transform lightsRoot;
        public Color alarm = new Color(1f, 0.18f, 0.55f);
        [Range(0, 1)] public float tint = 0.65f;
        struct L { public Light light; public Color color; public float intensity; }
        readonly List<L> lights = new List<L>();
        float k, kv;

        void Start()
        {
            if (!lightsRoot) { var g = GameObject.Find("Lighting"); if (g) lightsRoot = g.transform; }
            if (lightsRoot) foreach (var l in lightsRoot.GetComponentsInChildren<Light>()) if (l.type == LightType.Point) lights.Add(new L { light = l, color = l.color, intensity = l.intensity });
        }

        void Update()
        {
            var d = HeistDirector.Instance;
            float target = d && d.Current == HeistDirector.Phase.Pursuit ? 1f : 0f;
            k = Springs.Step(k, ref kv, target, 3f, Time.deltaTime);
            if (k < 0.001f && target == 0f) return;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 5.5f);
            foreach (var l in lights)
            {
                if (!l.light) continue;
                l.light.color = Color.Lerp(l.color, alarm, tint * k);
                l.light.intensity = l.intensity * Mathf.Lerp(1f, pulse, k);
            }
        }
    }
}
