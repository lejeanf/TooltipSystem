// HDRP variant of the navigation marker. Same SDF/pulse math as the URP shader (NavigationMarker.hlsl).
// Uses an un-tagged pass (SRPDefaultUnlit), which HDRP renders for objects in the Transparent queue.
// NOTE: hand-written HDRP shaders are version-sensitive — verify in-Unity; if it fails to compile,
// rebuild it as an HDRP Unlit Shader Graph wrapping NavigationMarker.hlsl via a Custom Function node.
Shader "jeanf/Tooltip/NavigationMarker HDRP"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 0.627, 0.094, 1)
        _PulseColor("Pulse Color", Color) = (1, 0.957, 0.847, 1)
        _Shape("Shape (0 dot, 1 chevron, 2 line, 3 target)", Float) = 1
        _Weight("Chevron Weight", Range(0.001, 0.6)) = 0.145
        _PathLength("Path Length (m)", Float) = 10
        _PulseHead("Pulse Head Distance (m)", Float) = 0
        _PulseTrail("Pulse Trail (m)", Float) = 4
        _PulseInterval("Pulse Train Interval (m)", Float) = 10
        _PulseMode("Pulse Mode (0 single, 1 train)", Float) = 0
        _PlayerDist("Player Distance (m)", Float) = -100
        _HideDist("Hide Wipe Distance (m)", Float) = -100
        _GlobalFade("Global Fade", Range(0, 1)) = 1
        _TargetGlow("Target Glow Boost", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "NavigationMarkerUnlit"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "NavigationMarker.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _PulseColor;
                float _Shape;
                float _Weight;
                float _PathLength;
                float _PulseHead;
                float _PulseTrail;
                float _PulseInterval;
                float _PulseMode;
                float _PlayerDist;
                float _HideDist;
                float _GlobalFade;
                float _TargetGlow;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _PathDist01)
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionRWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionRWS);
                output.uv = input.uv;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float dist01 = UNITY_ACCESS_INSTANCED_PROP(Props, _PathDist01);
                float distM = (_Shape > 1.5 && _Shape < 2.5 ? input.uv.x : dist01) * _PathLength;

                float alpha = NavMarkerAlpha(input.uv, _Shape, _Weight);
                float glow = NavPulseGlow(distM, _PulseHead, _PulseTrail, _PulseInterval, _PulseMode);
                if (_Shape > 2.5) glow = max(glow, _TargetGlow);
                float fade = NavConsumeFade(distM, _PlayerDist) * NavHideWipe(distM, _HideDist) * _GlobalFade;

                float3 color = lerp(_BaseColor.rgb, _PulseColor.rgb, glow);
                color *= 1.0 + glow * 0.6;
                return float4(color, alpha * _BaseColor.a * fade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
