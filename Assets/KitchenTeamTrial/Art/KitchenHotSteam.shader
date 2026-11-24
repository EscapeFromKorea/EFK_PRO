Shader "Map2Kitchen/Hot rail steam"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct ParticleVertex
            {
                float4 position : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct ParticleFragment
            {
                float4 position : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            ParticleFragment vert(ParticleVertex input)
            {
                ParticleFragment output;
                output.position = UnityObjectToClipPos(input.position);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(ParticleFragment input) : SV_Target
            {
                float rise = input.uv.y;
                float bend = sin(rise * 7.6 - 0.5) * 0.19 + rise * 0.18;
                float crosswise = input.uv.x * 2.0 - 1.0 - bend;
                float width = lerp(0.25, 0.48, rise);
                float core = 1.0 - smoothstep(width * 0.25, width, abs(crosswise));
                float wisp = 1.0 - smoothstep(0.07, 0.59, abs(crosswise + 0.14));
                float taper = smoothstep(0.01, 0.24, rise) * (1.0 - smoothstep(0.65, 0.99, rise));
                float billow = 0.88 + 0.12 * sin((rise + crosswise * 0.7) * 12.0);
                fixed4 color = input.color;
                color.a *= taper * billow * (core * 0.79 + wisp * 0.26);
                return color;
            }
            ENDCG
        }
    }
}
