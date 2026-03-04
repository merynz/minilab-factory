Shader "FluxOut/PCR/ScreenFlash"
{
    Properties
    {
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0,1)) = 0
        _CenterBias ("Center Bias", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            half4 _FlashColor;
            half _Intensity;
            half _CenterBias;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = UnityObjectToClipPos(IN.positionOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half2 d = IN.uv * 2.0h - 1.0h;
                half radial = 1.0h - saturate(length(d) * 0.9h);
                half soft = lerp(1.0h, radial, _CenterBias);
                half alpha = saturate(_Intensity * soft);
                return half4(_FlashColor.rgb, alpha);
            }
            ENDCG
        }
    }
}
