Shader "FluxOut/PCR/Wake"
{
    Properties
    {
        _TintColor ("Tint", Color) = (0.24,0.92,1,0.72)
        _Intensity ("Intensity", Range(0,3)) = 1.55
        _Softness ("Softness", Range(0.05,1)) = 0.48
        _ForwardFade ("Forward Fade", Range(0.05,1)) = 0.9
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

            half4 _TintColor;
            half _Intensity;
            half _Softness;
            half _ForwardFade;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = UnityObjectToClipPos(IN.positionOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half2 uv = IN.uv;
                half x = abs(uv.x * 2.0h - 1.0h);
                half width = 1.0h - smoothstep(0.03h, max(0.08h, _Softness), x);
                half alongFront = pow(saturate(uv.y), lerp(0.62h, 1.34h, _ForwardFade));
                half alongBack = pow(saturate(1.0h - uv.y), lerp(1.0h, 1.9h, _ForwardFade));
                half along = max(alongFront * 0.92h, alongBack * 0.68h);
                half flicker = sin((uv.y * 24.0h) + (_Time.y * 8.2h) + (x * 20.0h)) * 0.5h + 0.5h;
                half spark = smoothstep(0.9h, 1.0h, flicker) * smoothstep(0.52h, 0.06h, x);
                half core = smoothstep(0.6h, 0.06h, x);
                half alpha = saturate(width * along * (0.72h + spark * 0.28h)) * _TintColor.a;
                half3 rgb = _TintColor.rgb * alpha * _Intensity * lerp(0.82h, 1.24h, core);
                return half4(rgb, alpha);
            }
            ENDCG
        }
    }
}
