#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public class S8_Refs
{
    public Transform archiveRoom;                // 서고 전체 그룹(탈출 공간 GEO 포함)
    public Bounds archiveBounds;                 // 서고 안쪽(섹터 로컬, z ≤131.5 — 탈출 공간 z132~144 제외) [판정 18]
    public Bounds workZoneBounds;                // 가운데 작업대 격자 구역(섹터 로컬) — L 밝은 스탠드 조명 범위
    public List<Transform> shelves;              // GEO_S8_Shelf_* — 벽 쪽 높은 책장 줄(바닥까지 채운 단순 박스)
    public List<Transform> tables;               // GEO_S8_Table_* — 가운데 작업대(바닥까지 채운 단순 박스, 높이 ≥0.75)
    public Transform startAnchor;                // ANCH_S8_Start — 전원 복귀 지점(S8 시작, 입구 안쪽 바닥), forward=+Z. 시작점 3개 = 이 점과 right×±2.5(W 파생) [판정 19]
    public Transform managerSpawnAnchor;         // ANCH_S8_Manager — 관리자 시작 자리 = WP_1 자리, y0(피벗 = 바닥, 눈 0.5) [판정 17]
    public List<Transform> patrolWaypoints;      // ANCH_S8_WP_1..k — 순찰 순서, 각 점 1회만(WP_1 중복 없음). y0(피벗 = 바닥) [판정 17]. 닫힘은 W가 waypoints 끝에 WP_1 재삽입 [판정 19]. 구간마다 직선 통행 가능(길찾기 없음)
    public Transform roleBookAnchor;             // ANCH_S8_Role_Book — 가운데 작업대 구역, x −4.0 [판정 20]
    public Transform roleMixerAnchor;            // ANCH_S8_Role_Mixer — 가운데 작업대 구역, x +4.0, 책과 거리 ≥2 [확정 §3-9 · 판정 20]
    public Transform roleMonitorAnchor;          // ANCH_S8_Role_Monitor — CCTV 콘솔, 대기실 가림 책장 줄 뒤(= '벽 쪽 구석 책장 줄 뒤' [판정 20])·순찰로에서 떨어진 곳
    public List<Transform> cctvCameraAnchors;    // ANCH_S8_Cam_1..3 — 개수 3, 순서: [0]책장 통로 A [1]책장 통로 B [2]가운데 작업대 구역 전체(대안 포즈 [판정 21]). forward=카메라 시선
    public doorPhysics exitDoor;                 // 출구 문(Map4Build.Door, 서고 출구 벽 z131.55~131.95 개구부 x −4~4 — 뒤는 탈출 공간) [판정 18]. W가 ManagerKeyDoor.door에 이 참조를 넣는다(팀 ManagerKeyDoor.cs:19 `public doorPhysics door`)
    public Transform exitDoorAnchor;             // ANCH_S8_ExitDoor — ManagerKeyDoor 사용 지점(문 앞 안쪽 바닥, forward=문 쪽). useRadius 2.5 [팀 ManagerKeyDoor.cs:23]
}
#endif
// 이름 규약 앵커(필드 아님) [판정 19]: ANCH_S8_Key — S8_Anchors 아래 빈 GO, 열쇠 자리(초안 (0,0.8,124)). W가 이름으로 찾는다.
