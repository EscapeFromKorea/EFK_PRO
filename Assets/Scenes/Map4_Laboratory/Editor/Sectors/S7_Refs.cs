#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public class S7_Refs
{
    public Transform entryHall;                  // 입구 쪽 전실 그룹(S6에서 올라와 서버실 문 앞)
    public Transform serverRoom;                 // 큰 서버실 그룹
    public Transform computerRoom;               // 붙은 컴퓨터실 그룹
    public Bounds serverRoomBounds;              // 서버실 안쪽만(섹터 로컬, 컴퓨터실 제외 — computerRoomBounds를 포함하지 않는다) — L 어두운 조명 범위 [판정 15]
    public Bounds computerRoomBounds;            // 컴퓨터실 안쪽(섹터 로컬) — L 조금 밝은 조명 범위
    public doorPhysics entryDoor;                // 전실→서버실 문(Map4Build.Door). W_ENTRY 풀면 W가 연다 [확정 §3-2]. TEMP 기간 null(TEMP_S7_EntryDoor_Open) [판정 17-S7]
    public doorPhysics exitDoor;                 // 서버실 끝 출구 문(Map4Build.Door, z=length 쪽). QuizTerminal.onAllCleared로 W가 연다 [확정 §3-7 · 판정 10]. TEMP 기간 null(TEMP_S7_ExitDoor_Open) [판정 17-S7]
    public Transform entryPanelAnchor;           // ANCH_S7_EntryPanel — W_ENTRY 배선 패널 자리(전실 동벽 x16 [판정 9])
    public List<Transform> servers;              // GEO_S7_Server_1..6 — 서버 6대 본체(콜라이더 박스). 개수 6, 격자 = 안C 틈 11 [사용자 09-29 나]
    public List<Transform> portAnchors;          // ANCH_S7_Port_* — 개수 6, 순서 고정: [0]OUT_A [1]OUT_B [2]OUT_C [3]IN_1 [4]IN_2 [5]IN_3. 서버 앞면 위, forward=플레이어 쪽
    public List<int> portServerIndex;            // portAnchors[i]가 붙은 servers 인덱스(0..5). 개수 6, 서로 다름(서버 1대에 포트 1개 [확정 §3-1])
    public Transform roleBookAnchor;             // ANCH_S7_Role_Book — 컴퓨터실 책상 위 가이드북(RoleSlot 책)
    public Transform roleComputerAnchor;         // ANCH_S7_Role_Computer — 같은 책상 위 컴퓨터(RoleSlot 컴퓨터). 책과 거리 ≥2 [제안 §3-6]
    public Transform rolePowerAnchor;            // ANCH_S7_Role_Power — 서버실 안 전력 역할 사물
    public List<Transform> glassWalls;           // GEO_S7_Glass_* — 컴퓨터실↔서버실 유리벽(솔리드 BoxCollider). L이 투명 재질. 1개 이상
    public Transform computerRoomDoorway;        // ANCH_S7_CRDoorway — 컴퓨터실 드나드는 개구부 중심(문 오브젝트 없음, 틈 폭 ≥ 통행 규격)
}
#endif
