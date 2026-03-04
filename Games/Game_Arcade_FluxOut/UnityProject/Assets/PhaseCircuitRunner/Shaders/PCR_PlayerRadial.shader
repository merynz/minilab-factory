Shader "FluxOut/PCR/PlayerRadial"
{
    Properties
    {
        _CoreColor ("Core Color", Color) = (1,0.95,0.56,1)
        _OuterColor ("Outer Color", Color) = (1,0.66,0.24,1)
        _Intensity ("Intensity", Range(0,4)) = 2.2
        _Radius ("Radius", Range(0.2,1.6)) = 0.94
        _Feather ("Feather", Range(0.01,1)) = 0.42
        _Pulse ("Pulse", Range(0,2)) = 0.2
        _PulseSpeed ("Pulse Speed", Range(0,5)) = 1.25
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

            half4 _CoreColor;
            half4 _OuterColor;
            half _Intensity;
            half _Radius;
            half _Feather;
            half _Pulse;
            half _PulseSpeed;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = UnityObjectToClipPos(IN.positionOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half2 p = IN.uv * 2.0h - 1.0h;
                half dist = length(p);

                half edgeStart = max(0.001h, _Radius - _Feather);
                half radial = 1.0h - smoothstep(edgeStart, _Radius, dist);
                radial = saturate(radial);

                half core = saturate(1.0h - dist / max(0.001h, _Radius * 0.68h));
                half pulse = 1.0h + sin(_Time.y * _PulseSpeed + dist * 8.0h) * (_Pulse * 0.5h);

                half3 color = lerp(_OuterColor.rgb, _CoreColor.rgb, core);
                half intensity = radial * _Intensity * pulse * 0.72h;
                half alpha = saturate(radial * (0.45h + core * 0.24h));
                return half4(color * intensity, alpha);
            }
            ENDCG
        }
    }
}
