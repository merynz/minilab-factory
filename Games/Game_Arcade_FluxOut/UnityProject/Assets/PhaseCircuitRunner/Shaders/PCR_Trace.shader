Shader "FluxOut/PCR/Trace"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.15,0.9,0.9,1)
        _CoreColor ("Core Color", Color) = (0.9,1,1,1)
        _MidColor ("Mid Color", Color) = (0.4,0.92,1,1)
        _EdgeColor ("Edge Color", Color) = (0.24,0.12,0.42,1)
        _Glow ("Glow", Range(0,3)) = 1
        _PulseSpeed ("Pulse Speed", Range(0,4)) = 1.2
        _CoreWidth ("Core Width", Range(0.05,0.9)) = 0.38
        _EdgeSoftness ("Edge Softness", Range(0.02,0.6)) = 0.22
        _PulseContrast ("Pulse Contrast", Range(0,2)) = 0.85
        _FlowTiling ("Flow Tiling", Range(2,40)) = 16
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            half4 _BaseColor;
            half4 _CoreColor;
            half4 _MidColor;
            half4 _EdgeColor;
            half _Glow;
            half _PulseSpeed;
            half _CoreWidth;
            half _EdgeSoftness;
            half _PulseContrast;
            half _FlowTiling;

            inline half TriWave(half x)
            {
                return 1.0h - abs(frac(x) * 2.0h - 1.0h);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = UnityObjectToClipPos(IN.positionOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half y = abs(IN.uv.y * 2.0h - 1.0h);
                half center01 = saturate(1.0h - y);

                half coreStart = saturate(1.0h - _CoreWidth);
                half core = smoothstep(coreStart - _EdgeSoftness, 1.0h - _EdgeSoftness * 0.35h, center01);

                // Use multiplies instead of pow for mobile-friendly falloff.
                half glowBand = center01 * center01;
                glowBand *= glowBand;
                glowBand = saturate(glowBand * (1.0h + _Glow));

                half flow = TriWave(IN.uv.x * _FlowTiling - _Time.y * _PulseSpeed);
                half pulse = lerp(0.6h, 1.0h, flow);

                half intensity = core * 1.08h + glowBand * (0.2h + _Glow * 0.36h) + pulse * _PulseContrast * glowBand * 0.44h;
                half alpha = saturate(core * 0.28h + glowBand * (0.3h + _Glow * 0.15h));

                half3 grad = lerp(_EdgeColor.rgb, _MidColor.rgb, saturate(glowBand));
                grad = lerp(grad, _CoreColor.rgb, saturate(core));
                grad *= lerp(0.9h, 1.05h, pulse);
                half3 rgb = lerp(grad, _BaseColor.rgb, 0.2h) * intensity;
                return half4(rgb, alpha);
            }
            ENDCG
        }
    }
}
