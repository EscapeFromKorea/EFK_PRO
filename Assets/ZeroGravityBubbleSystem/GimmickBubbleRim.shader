// 무중력 기류 버블 경계 표시용. 외부 에셋 없이 내장 Surface Shader만으로 구현(Built-in RP 전제,
// 이 프로젝트는 URP/HDRP 패키지가 없다).
// 가운데는 거의 안 보이고, 시야각이 표면과 스칠수록(가장자리) 밝게 빛나는 rim-light 효과.
Shader "ZeroGravityBubble/Rim"
{
    Properties
    {
        _Color ("Base Color", Color) = (0.4, 0.8, 1, 0.12)
        _RimColor ("Rim Color", Color) = (0.6, 0.9, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        CGPROGRAM
        #pragma surface surf Lambert alpha:fade noshadow

        fixed4 _Color;
        fixed4 _RimColor;
        float _RimPower;

        struct Input
        {
            float3 viewDir;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            o.Albedo = _Color.rgb;

            float rim = 1.0 - saturate(dot(normalize(IN.viewDir), o.Normal));
            float rimAmount = pow(rim, _RimPower);

            o.Emission = _RimColor.rgb * rimAmount;
            o.Alpha = _Color.a + rimAmount * _RimColor.a;
        }
        ENDCG
    }
    FallBack "Transparent/VertexLit"
}
