// 포탈 쿼드(검정 단색/뷰스루 텍스처) 전용. 기본 Unlit/Texture는 backface culling이 있어, 패널
// 회전이나 Unity 기본 Quad 프리미티브의 앞/뒤 정의에 따라 플레이어가 접근하는 쪽에서 아예 안
// 보이는(반대쪽에서만 보이는) 문제가 났다(2026-09-11 실측 — 주황 포탈에서 확인). Cull Off로
// 양면 다 그려서 이 방향 문제를 원천 차단한다.
Shader "SpacePortalSystem/PortalVisual"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        Pass
        {
            SetTexture [_MainTex] { combine texture }
        }
    }
}
