Shader "FluxOut/PCR/Background"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.02,0.05,0.09,1)
        _BottomColor ("Bottom", Color) = (0.005,0.01,0.02,1)
        _AccentA ("Accent A", Color) = (0.1,0.3,0.5,1)
        _AccentB ("Accent B", Color) = (0.08,0.2,0.38,1)
        _PatternScale ("Pattern Scale", Range(2,36)) = 10
        _PatternStrength ("Pattern Strength", Range(0,1)) = 0.2
        _DetailStrength ("Detail Strength", Range(0,1)) = 0.2
        _DriftSpeed ("Drift Speed", Range(0,2)) = 0.2
        _LayerDepth ("Layer Depth", Range(0,1)) = 0.5
        _ParallaxOffset ("Parallax Offset", Vector) = (0,0,0,0)
        _Alpha ("Alpha", Range(0,1)) = 1
        _Vignette ("Vignette", Range(0,1)) = 0.28
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-200" }
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

            half4 _TopColor;
            half4 _BottomColor;
            half4 _AccentA;
            half4 _AccentB;
            half _PatternScale;
            half _PatternStrength;
            half _DetailStrength;
            half _DriftSpeed;
            half _LayerDepth;
            float4 _ParallaxOffset;
            half _Alpha;
            half _Vignette;

            inline half Hash12(half2 p)
            {
                half h = dot(p, half2(127.1h, 311.7h));
                return frac(sin(h) * 43758.5453h);
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
                half2 uv = IN.uv;
                half2 parallax = _ParallaxOffset.xy * lerp(0.08h, 0.42h, _LayerDepth);
                uv += parallax;

                half2 driftUv = uv;
                driftUv.x += _Time.y * _DriftSpeed * (0.05h + _LayerDepth * 0.08h);
                driftUv.y -= _Time.y * _DriftSpeed * (0.03h + _LayerDepth * 0.06h);

                half skyLerp = smoothstep(0.0h, 1.0h, uv.y);
                half3 baseCol = lerp(_BottomColor.rgb, _TopColor.rgb, skyLerp);

                // Sparse "circuit traces" in the far background.
                half trace0 = abs(sin((driftUv.x + driftUv.y * 0.40h) * _PatternScale));
                half trace1 = abs(sin((driftUv.x * 1.8h - driftUv.y * 0.75h) * (_PatternScale * 0.62h) + 1.1h));
                half lineA = smoothstep(0.62h, 0.94h, trace0);
                half lineB = smoothstep(0.64h, 0.945h, trace1);

                // Tiny procedural nodes. Very subtle to avoid stealing focus.
                half2 cellUv = driftUv * (2.6h + _PatternScale * 0.12h);
                half2 cell = abs(frac(cellUv) - 0.5h);
                half nodeMask = smoothstep(0.48h, 0.43h, max(cell.x, cell.y));
                half sparkNoise = smoothstep(0.94h, 1.0h, Hash12(floor(cellUv * 2.0h)));
                half detail = nodeMask * sparkNoise * 0.62h;

                half haze = sin((driftUv.x * 1.3h + driftUv.y * 1.1h) * (_PatternScale * 0.35h)) * 0.5h + 0.5h;

                half3 color = baseCol;
                color += _AccentA.rgb * ((lineA * _PatternStrength * 0.56h) + (detail * _DetailStrength * 0.42h));
                color += _AccentB.rgb * ((lineB * _PatternStrength * 0.46h) + (haze * _DetailStrength * 0.22h));

                // Keep edges dark so gameplay stays dominant.
                half2 d = uv * 2.0h - 1.0h;
                half vignette = 1.0h - saturate(dot(d, d) * _Vignette);
                color = saturate(color * 1.42h);
                color *= lerp(0.82h, 1.0h, vignette);
                half alpha = saturate(_Alpha * (0.6h + vignette * 0.4h));
                return half4(color, alpha);
            }
            ENDCG
        }
    }
}
