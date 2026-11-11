// 조준 실루엣 전용. 깊이 테스트를 무시하고(ZTest Always) 항상 맨 위에 그려, 조준 거리가 멀어져도
// 깊이버퍼 정밀도 때문에 패널 시각 메쉬에 묻혀 안 보이는 문제(2026-09-11 실측)를 원천 차단한다.
Shader "SpacePortalSystem/AimSilhouette"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            Color [_Color]
        }
    }
}
