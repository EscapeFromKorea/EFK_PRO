#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public class S6_Refs
{
    public Transform room;                       // GEO 방 전체 그룹(큰 방 1개, 약 60×90, 천장 로컬 약 24) [확정 09-28 §3-2]
    public Bounds roomInnerBounds;               // 방 안쪽 빈 공간(섹터 로컬). L 조명·W 경계 참고용
    public Transform startAnchor;                // ANCH_S6_Start — 시작 복귀 RespawnZone 루트 = 구역 아랫면 중심(바닥 윗면 y0), forward=+Z [판정 4]
    public Vector3 startZoneSize;                // 시작 RespawnZone BoxCollider size(루트 = startAnchor, center = (0, size.y/2, 0)). 초안 값 (12,3,6) [판정 4]
    public List<Transform> checkpointPads;       // ANCH_S6_Check_1, _2 — 체크포인트 RespawnZone 루트 = 구역 아랫면 중심(발판 윗면 높이, 로컬 y 5·10 = 절대 23·28 [판정 5]). 개수 2, 낮은 것부터 [판정 4]
    public List<Bounds> checkpointPadBounds;     // 같은 순서의 RespawnZone 부피(섹터 로컬 AABB, 가장자리 들임·높이 3 반영 완료). W는 그대로 쓰고 다시 들이지 않는다 [판정 4]
    public Transform exitLanding;                // ANCH_S6_ExitLanding — 출구 층(윗면 y16) 도착 발판 윗면 중앙
    public List<Transform> fixedPanelAnchors;    // ANCH_S6_Panel_<k> — 고정 PortalSurface 자리. 위치=패널 면 중심, forward=면 바깥 법선(방 안쪽). 순서 = 진행 단계 순
    public List<Transform> movablePanelAnchors;  // ANCH_S6_MovPanel_<k> — MovablePortalPanel 시작 자리(포즈 의미 위와 같음). 순서 = 진행 단계 순
    public List<Transform> movablePanelTravelEnd;// ANCH_S6_MovPanelEnd_<k> — 같은 k 패널의 이동/회전 끝 포즈. movablePanelAnchors와 같은 개수
    public List<Transform> leverAnchors;         // ANCH_S6_Lever_<k> — 같은 k 패널을 움직이는 PanelLever 자리(패널이 화면에 보이는 곳 [확정 §3-4 · 판정 3]). movablePanelAnchors와 같은 개수
    public Transform orangeBallAnchor;           // ANCH_S6_Ball_Orange — 입구용 에너지볼 고정 자리(받침 윗면 위)
    public Transform blueBallAnchor;             // ANCH_S6_Ball_Blue — 출구용 에너지볼 고정 자리
    public List<Transform> nonPortalWalls;       // 포탈 불가 벽 GEO(L이 패널과 구분되는 재질을 입힘). 개수 제한 없음, 1개 이상
}
#endif
