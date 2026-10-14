// Rescue 2026-10-11: far sky / moon cards for the arrival window view. Unlit, NO fog (fog would erase them), drawn
// behind everything (Background queue, no depth write).
Shader "NeonProtocol/SkyBackdrop"
{
    Properties
    {
        _BaseMap("Gradient", 2D) = "white" {}
        [HDR] _Color("Tint", Color) = (1,1,1,1)
        _Round("Disc (0 = card, 1 = round moon)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial) float4 _BaseMap_ST; float4 _Color; float _Round; CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _Color;
                if (_Round > 0.5) { float d = length(i.uv - 0.5) * 2.0; c.a *= saturate((1.0 - d) * 30.0); c.rgb *= 0.75 + 0.25 * saturate(1.0 - d * d); }
                return c;
            }
            ENDHLSL
        }
    }
}
