Shader "FluxOut/PCR/SDFIcon"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Outline ("Outline", Range(0.01,0.4)) = 0.14
        _Glow ("Glow", Range(0,3)) = 0.8
        _PhaseActive ("Phase Active", Range(0,1)) = 1
        _ArrowDir ("Arrow Dir", Float) = 1
        _IconType ("Icon Type", Float) = 0
        _PulseOffset ("Pulse Offset", Float) = 0
        _FillBoost ("Fill Boost", Range(0.8,2.5)) = 1.35
        _PassiveDim ("Passive Dim", Range(0.1,1.0)) = 0.4
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
            #pragma target 3.0
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
            half _Outline;
            half _Glow;
            half _PhaseActive;
            half _ArrowDir;
            half _IconType;
            half _PulseOffset;
            half _FillBoost;
            half _PassiveDim;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = UnityObjectToClipPos(IN.positionOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            inline half sdBox(half2 p, half2 b)
            {
                half2 d = abs(p) - b;
                return length(max(d, 0.0h)) + min(max(d.x, d.y), 0.0h);
            }

            inline half sdCircle(half2 p, half r)
            {
                return length(p) - r;
            }

            inline half sdSegment(half2 p, half2 a, half2 b, half radius)
            {
                half2 pa = p - a;
                half2 ba = b - a;
                half h = saturate(dot(pa, ba) / max(dot(ba, ba), 0.0001h));
                return length(pa - ba * h) - radius;
            }

            inline half TriWave(half x)
            {
                return 1.0h - abs(frac(x) * 2.0h - 1.0h);
            }

            half IconDistance(half2 p)
            {
                int icon = (int)round(_IconType);

                if (icon == 0) // diode gate
                {
                    half triA = sdSegment(p, half2(-0.52h, -0.48h), half2(0.20h, 0.0h), 0.08h);
                    half triB = sdSegment(p, half2(0.20h, 0.0h), half2(-0.52h, 0.48h), 0.08h);
                    half triC = sdSegment(p, half2(-0.52h, 0.48h), half2(-0.52h, -0.48h), 0.08h);
                    half bar = sdBox(p - half2(0.34h, 0.0h), half2(0.07h, 0.56h));
                    return min(min(triA, min(triB, triC)), bar);
                }

                if (icon == 1) // inverter
                {
                    half c1 = sdSegment(p, half2(-0.50h, -0.42h), half2(0.50h, 0.42h), 0.09h);
                    half c2 = sdSegment(p, half2(-0.50h, 0.42h), half2(0.50h, -0.42h), 0.09h);
                    return min(c1, c2);
                }

                if (icon == 2) // mux arrow
                {
                    half dir = _ArrowDir >= 0.0h ? 1.0h : -1.0h;
                    half2 tip = half2(0.38h, dir * 0.46h);
                    half body = sdSegment(p, half2(-0.56h, dir * 0.46h), tip, 0.085h);
                    half wingA = sdSegment(p, tip, tip + half2(-0.24h, -dir * 0.20h), 0.075h);
                    half wingB = sdSegment(p, tip, tip + half2(-0.24h, dir * 0.20h), 0.075h);
                    return min(body, min(wingA, wingB));
                }

                if (icon == 3) // capacitor
                {
                    half left = sdBox(p - half2(-0.18h, 0.0h), half2(0.055h, 0.52h));
                    half right = sdBox(p - half2(0.18h, 0.0h), half2(0.055h, 0.52h));
                    half wireL = sdSegment(p, half2(-0.70h, 0.0h), half2(-0.26h, 0.0h), 0.055h);
                    half wireR = sdSegment(p, half2(0.26h, 0.0h), half2(0.70h, 0.0h), 0.055h);
                    return min(min(left, right), min(wireL, wireR));
                }

                if (icon == 4) // amplifier
                {
                    half a = sdSegment(p, half2(-0.56h, -0.50h), half2(-0.56h, 0.50h), 0.08h);
                    half b = sdSegment(p, half2(-0.56h, 0.50h), half2(0.50h, 0.0h), 0.08h);
                    half c = sdSegment(p, half2(0.50h, 0.0h), half2(-0.56h, -0.50h), 0.08h);
                    return min(a, min(b, c));
                }

                if (icon == 5) // ground clamp
                {
                    half stem = sdSegment(p, half2(0.0h, 0.62h), half2(0.0h, 0.12h), 0.06h);
                    half b0 = sdSegment(p, half2(-0.34h, 0.0h), half2(0.34h, 0.0h), 0.05h);
                    half b1 = sdSegment(p, half2(-0.24h, -0.16h), half2(0.24h, -0.16h), 0.05h);
                    half b2 = sdSegment(p, half2(-0.14h, -0.30h), half2(0.14h, -0.30h), 0.05h);
                    return min(stem, min(b0, min(b1, b2)));
                }

                if (icon == 6) // spark
                {
                    half a = sdSegment(p, half2(-0.42h, 0.52h), half2(0.02h, 0.08h), 0.085h);
                    half b = sdSegment(p, half2(0.02h, 0.08h), half2(-0.12h, 0.08h), 0.085h);
                    half c = sdSegment(p, half2(-0.12h, 0.08h), half2(0.42h, -0.52h), 0.085h);
                    return min(a, min(b, c));
                }

                // inductor coupler
                half i0 = abs(sdCircle(p + half2(0.36h, 0.0h), 0.22h)) - 0.08h;
                half i1 = abs(sdCircle(p + half2(0.06h, 0.0h), 0.22h)) - 0.08h;
                half i2 = abs(sdCircle(p + half2(-0.24h, 0.0h), 0.22h)) - 0.08h;
                return min(i0, min(i1, i2));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half2 p = IN.uv * 2.0h - 1.0h;
                half dist = IconDistance(p);

                half fill = smoothstep(0.06h, -0.02h, dist);
                half outline = smoothstep(_Outline + 0.02h, _Outline - 0.02h, abs(dist));
                half active = saturate(_PhaseActive);

                // Cheap pulse (triangle wave) for readability at distance.
                half pulse = lerp(0.72h, 1.0h, TriWave(_Time.y * 3.0h + _PulseOffset * 2.0h));
                half fillWeight = fill * _FillBoost * active;
                half outlineWeight = outline * lerp(1.0h, 1.22h, active);
                half intensity = (fillWeight + outlineWeight) * pulse;
                half dim = lerp(_PassiveDim, 1.0h, active);
                half alpha = saturate(max(fill * active, outline) * dim);
                half3 rgb = _BaseColor.rgb * intensity * (0.64h + _Glow * 0.44h) * dim;
                return half4(rgb, alpha);
            }
            ENDCG
        }
    }
}
