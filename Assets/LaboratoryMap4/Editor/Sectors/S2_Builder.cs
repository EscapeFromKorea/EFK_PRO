#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 섹터2 "격리 구출" 빌더(과제 S2-B, R2 — 수정 라운드 S2검산1 반영). 진행/초안/S2_배치초안.md(2차 반영판 + 수정 라운드 초안1 +
/// 2차 재검 §15) §2 표를 섹터 씬 Generated/ 아래에 짓는다. Map4SceneBuilder.BuildSectorScene이 SectorBuilderRegistry를 거쳐 S2일 때
/// 빈 틀 대신 이것을 부르고, 연결 통로(z 84~96)·마커는 공용 코드(BuildConnectorAndMarkers)가 이어서 만든다(1차 계약 C3).
///
/// [짓는 것 — 계약 C1-5 S2 빌더 몫 "지형·문·블록·레버, 모든 doorPhysics.leverHead = null 확인, 마찰 0 재질 부여"]
///   ① 지형 상자 47(초안 §2-1 #1~#37·#39~#49에서 #14 GEO_S2_Wall_Housing_In 삭제 [2차판정 10 — R3 C3g]): GEO_S2_Floor_Room 1
///      (Map4Build.Floor) · GEO_S2_Wall_* 29 · GEO_S2_Ceil_* 6 · GEO_S2_Obst_* 11(모두 Map4Build.Wall) + 빈 표지 TEMP_S2_ExitS3_Open
///      (콜라이더 없음, 초안 §2-7). 발판·계단은 없다(초안 §2).
///      방에는 천장이 없고, 천장은 격리 공간·수납함·레버 칸 뚜껑 6개뿐이다(초안 §1-1). 외벽 높이 9 [제안]로 INF 빈 틀(6)을 대체한다.
///   ② 마찰 0 조각 14(초안 §2-2 둔덕 5 · §2-3 경사판 9 [2차판정 9]): 꼭짓점(§2-3a)의 볼록 껍질 메시 → 볼록 MeshCollider + 같은 GO의
///      렌더러(VIS 자식 없음 — 초안 §2-2 '만드는 법'). 헬퍼(LoadOrCreateNoFriction·ConvexHullMesh)는 이 파일 안에 둔다 — S2-B 소유
///      [2차판정 10]. 콜라이더 재질 PM_Map4_NoFriction(빌더가 Materials/Generated에 만들거나 재사용 — 계약 C1-6).
///      Map4Build.Ramp는 쓰지 않는다(바닥 0.3 관통 — 초안 §2-2).
///   ③ 레버 3(초안 §2-4·§3): 빈 부모 S2_Lever1_Trap·S2_Lever2·S2_Lever3(S2_Gimmicks 아래, 스케일 1, yaw 180·0·90)마다 팀 메뉴
///      Tools/DoorSystem/Create Lever → 새 루트 lever_pivot(LeverHead 보유)·lever_body를 루트 비교로 찾아 부모 아래로 옮긴다.
///      pivot 로컬 위치 0·로컬 yaw −45(메뉴값), 받침대 이름 = 부모 이름 + _Body, 로컬 (0.2, −0.2701553, 0.4338)·회전 0·크기
///      (1.3924, 0.4296894, 2.4055)(배치 변환) — 바닥 0 ~ pivot 아랫면 0.4296894(맞닿음). 팀 pivot·head와 관통 0 [2차판정 10 — R3].
///      LeverHead 인스펙터 값·팀 레버 Transform·콜라이더는 메뉴값 그대로 둔다(받침대만 바꾼다).
///   ④ S4행 문 DOOR_S2_ExitS4(초안 §2-1 #38): Map4Build.Door(닫힘, openHeight 3.7) → 같은 부모의 VIS_NoCollide_DoorFrame 삭제(계약 C2-2).
///   ⑤ 딱딱블록 39(초안 §2-5): 팀 메뉴 Tools/SnapBlockSystem/Create Snap Block → §2-5 이름·중심·회전 0, S2_Gimmicks 아래. 팀 기본값 그대로.
///   아래 표(TerrainBoxes·ExitS4DoorBox·NoFrictionPieces·Levers·Blocks)는 초안 §2 표(= `python S2_check.py --md` 출력)를 스크립트로
///   기계 변환한 것이다(재타이핑 없음). 행 순서 = 초안 행 순서 = 계약 C1-1 Refs 순서.
///
/// [레버 자동 연결 — 초안 §3 주] Create Lever는 SceneView 피벗(배치 모드면 원점)에서 가장 가까운 doorPhysics의 leverHead에 새 레버를
///   꽂는다(팀 DoorSystemMenuItem.cs:72-73·196). 그래서 레버를 문보다 먼저 만들고, 메뉴 호출 전후로 씬의 모든 doorPhysics.leverHead를
///   기록·복원한다(공개 필드 대입 — 리플렉션 아님). 끝에서 generated 아래 모든 doorPhysics.leverHead == null을 검증한다(계약 C1-5).
/// [숨은 벽 — 판정 2] GEO_S2_Wall_IsoHidden은 수납함 안 평소 위치(y 4.3~8.3)의 정지 벽이다. doorPhysics·Rigidbody·트리거·배선 없음.
///   천장 슬롯(x −14.2~−13.8, y 4.0~4.3)은 R3에서 GEO_S2_Ceil_Iso를 x −13.8까지 늘려 메운다 — 슬롯 0.4 틈이 일반 끼임 규칙
///   (0.02<g<1.0) 위반이었다 [2차판정 10 — C3g]. 수납함은 방 쪽 벽 Housing_Out + 뚜껑 Housing_Top(숨은 벽 윗면 8.3에 맞닿음)만 남긴다.
///   숨은 벽을 사이에 둔 양쪽 벽은 경계 상자 간격이 벽 두께 0.4로 고정되므로 격리 쪽 Housing_In을 뺐다(숨은 벽 위치·크기 불변 — 판정 2).
/// [S3행 출구 — 판정 1] 북벽 개구(x −4~4, 높이 3.5)는 문 없이 처음부터 열려 있다. 표지 TEMP_S2_ExitS3_Open = 개구 중심 (0, 0, 84).
///   개구 앞 둔덕 GEO_S2_Hump_S3_Room은 방 안(z 80~84, 능선 z 82)에 둔다 [2차판정 10]. 통로 구역 Hump_S3_Gap·Corr는
///   만들지 않는다(계약 C2-2).
/// [하지 않는 것] 격리 컨트롤러·배선은 S2_Wiring, 머티리얼 톤·조명·VIS_ 시각물은 S2_Dress 몫(계약 C1-5·C1-6). 팀 기믹 인스펙터 값은
///   바꾸지 않는다(블록·레버 모두 메뉴값). 팀 private 리플렉션 없음.
///
/// [예외 — R2 계약 C5(판정 8)] 전제 불일치면 아무것도 만들지 않고 false. 지형·기믹 단계 예외(메뉴 실패·팀 타입 못 찾음·Refs 검증
///   실패)면 Generated를 비우고 Build 중 새로 생긴 씬 루트를 지운 뒤 false. Wire·Dress 예외는 LogError만 하고 지형·기믹을 남긴다.
///   정적 필드 g·gimmickRoot는 모든 경로에서 null로 돌린다.
///
/// 출처 표기: [확정] 설계서 사용자 확정 · [판정] 판정 문서 · [계약] R2/1차 계약 · [팀] 팀 코드 · [제안] 초안 제안값 · [계산] 계산값 · [추정] 실측 필요.
/// </summary>
public static class S2_Builder
{
    // ── 전제 (R2 계약 C5 · Map4Layout.asset) ──
    private const float SectorWidth = 76f;     // [계약 C5] S2 폭 76
    private const float SectorLength = 84f;    // [계약 C5] 길이 84
    private const float FloorHeight = 0f;      // [계약 C5] 바닥 높이 0
    private const float ExitHeight = 0f;       // [계약 C5] 출구 높이 0
    private const float PreconditionTolerance = 0.01f; // [계약 C5] ±0.01

    // ── 이름 (R2 계약 C2-0·C2-2·C3) ──
    private const string FloorPrefix = "GEO_S2_Floor_";
    private const string WallPrefix = "GEO_S2_Wall_";
    private const string CeilPrefix = "GEO_S2_Ceil_";
    private const string ObstPrefix = "GEO_S2_Obst_";
    private const string IsoHiddenWallName = "GEO_S2_Wall_IsoHidden";   // [판정 2]·[계약 C2-2]
    private const string ExitS3OpeningName = "TEMP_S2_ExitS3_Open";     // [판정 1]·[계약 C2-2]
    private static readonly Vector3 ExitS3OpeningLocalPos = new Vector3(0f, 0f, 84f); // [판정 1]·[계약 C2-2] 초안 §2-7
    private const string ExitS4DoorName = "DOOR_S2_ExitS4";             // [계약 C2-2·C3]
    private const string DoorFrameName = "VIS_NoCollide_DoorFrame";     // [헬퍼 Map4Build.cs:207] 반환 뒤 삭제(계약 C2-2)
    private const string GimmickRootName = "S2_Gimmicks";               // [계약 C1-2 gimmickRoot]
    private const string BodySuffix = "_Body";                          // [제안 초안 §1-7·§2-4] 받침대 = 부모 이름 + _Body

    // ── 팀 메뉴·메뉴 생성물 이름 [팀] ──
    private const string LeverMenuPath = "Tools/DoorSystem/Create Lever";              // [팀] DoorSystemMenuItem.cs:69
    private const string SnapBlockMenuPath = "Tools/SnapBlockSystem/Create Snap Block"; // [팀] SnapBlockMenuItem.cs:12
    private const string TeamLeverBodyName = "lever_body";                             // [팀] DoorSystemMenuItem.cs:191

    // ── 마찰 0 재질 (초안 §1-5 · 계약 C1-6) ──
    private const string NoFrictionDir = "Assets/LaboratoryMap4/Materials/Generated";  // [계약 C1-6]
    private const string NoFrictionName = "PM_Map4_NoFriction";                        // [계약 C1-6·C3]
    private const string NoFrictionPalette = "Floor";  // [제안 — 초안 §2-2는 'Generated 팔레트 머티리얼'만 지정, Map4Build.Ramp 기본과 같게]
    private const string HumpPrefix = "GEO_S2_Hump_";
    private const string WedgePrefix = "GEO_S2_Wedge_";

    // ── S4행 문 (초안 §2-1 #38 :151·:168) ──
    private const float ExitS4DoorOpenHeight = 3.7f;   // [제안 초안 §2-1 #38] doorTargetYOffset +3.7 → 열림 y4.0~7.5

    // ── 레버 배치 변환 (초안 §2-4·§3) ──
    private const float LeverPivotLocalYaw = -45f;     // [팀] DoorSystemMenuItem.cs:33 LeverClosedAngleY(메뉴값)
    // 받침대 [2차판정 10 — R3]: 팀 pivot·head와 관통 6건(0.540·0.611)을 없애려고 높이를 바닥 0 ~ pivot 아랫면으로 줄인다(x·z는 팀 값 그대로).
    //   pivot 아랫면 = 부모 y 0.485 − pivot 크기 y 0.110621125/2 = 0.4296894375 [계산 — 팀 DoorSystemMenuItem.cs:30 LeverPivotScale.y]
    //   → 크기 y 0.4296894, 로컬 y = 0.4296894/2 − 0.485 = −0.2701553 [계산]. head 아랫면 0.4496044와 틈 0.019915(≤0.02 맞닿음) [계산].
    //   [M3R1] 경계값: 맞닿음 기준 0.02보다 0.000085 작을 뿐이다. F1 감사(Map4Audit 교대 투영)는 받침대가 먼저 오는 쌍 순서에서
    //   이 틈을 0.021961로 잰다(F1_audit_20260929_153843:38 L2 0.022, '위 덮임' 제외). head 반축이 pivot 비균일 스케일 때문에
    //   직교하지 않아(89.653°) ObbClosestPoint가 참 투영이 아니므로 상한값이 나온다[계산 — scratchpad s2b3/audit_head_repro.py].
    //   pivot 맞닿음(틈 0)을 지키면 head 틈은 0.019915보다 작아질 수 없다(윗면 한 평면) — 여유 확보는 판정 요청(보고 S2-B3 M3R1).
    private static readonly Vector3 LeverBodyLocalPos = new Vector3(0.2f, -0.2701553f, 0.4338f);   // [계산 초안 §2-4 x·z · 위 계산 y] 세 레버 같음
    private static readonly Vector3 LeverBodyLocalScale = new Vector3(1.3924f, 0.4296894f, 2.4055f); // [팀 LeverBodyScale x·z · 위 계산 y]
    private const float TouchTolerance = 0.02f;        // [공통 규칙 · 계약 C2-1 판정 4(a)] g ≤ 0.02 = 맞닿음
    private const float TrapGapMin = 1.0f;             // [공통 규칙] 끼임 틈 0 < g < 1.0 금지
    private const float PenetrationTolerance = 1e-4f;  // [계산] float 반올림 여유 — 받침대↔pivot은 설계상 겹침 0(맞닿음)
    private const float StaticOverlapTolerance = 0.001f; // [계산 — S2-B 보고 selfChecks 관통 기준 1e-3] 받침대↔경사판 _B/_C 5e-5(초안 소수 4자리)는 안
    /// <summary>경계 상자 기준으로 지형만으로 풀 수 없는 일반 끼임 쌍 [2차판정 10 — R3 reviewRequests]. 빌더 검사에서 throw 대신 경고만 남긴다
    /// (감사 제외 목록이 아니다 — 감사는 그대로 잰다). 사이 x 29.5~30은 전실 서벽 두께 0.5이고, 문(좌표 고정)·둔덕 윗면 식·조각 14 [2차판정 9]가
    /// 고정이라 두 조각 경계 상자를 잇거나 1.0 이상 떼는 배치가 없다.</summary>
    private static readonly string[][] UnresolvedGapPairs = { new[] { "GEO_S2_Hump_S4_Room", "GEO_S2_Hump_S4_Vest" } };

    /// <summary>Refs obstacles 순서 [계약 C1-1·C2-2] — 초안 §2-1 행 순서와 같아야 한다(검증에서 대조).</summary>
    private static readonly string[] ObstacleOrder =
    {
        "GEO_S2_Obst_Shelf1", "GEO_S2_Obst_Shelf2", "GEO_S2_Obst_Desk1", "GEO_S2_Obst_Shelf3", "GEO_S2_Obst_Desk2", "GEO_S2_Obst_Shelf4",
        "GEO_S2_Obst_Cab1", "GEO_S2_Obst_Shelf5", "GEO_S2_Obst_Desk3", "GEO_S2_Obst_Shelf6", "GEO_S2_Obst_Cab2",
    };
    /// <summary>Refs leverRoots·levers 순서 [계약 C1-1] L1_Trap, L2, L3.</summary>
    private static readonly string[] LeverOrder = { "S2_Lever1_Trap", "S2_Lever2", "S2_Lever3" };

    // ── Refs 지형 치수 (초안 §2-9 · 계약 C1-2) ──
    private const float OuterWallTop = 9f;     // [제안 초안 §1-1] 외벽 높이(수납함 뚜껑 윗면 8.5 — R3 [2차판정 10] — 을 덮음)
    private const float IsoLineX = -14f;       // [초안 §0] 숨은 벽 선
    private static readonly Vector3 IsoSpaceMin = new Vector3(-38f, 0f, 30.5f);  // [초안 §2-9] 격리 공간 안쪽
    private static readonly Vector3 IsoSpaceMax = new Vector3(-14f, 4f, 53.5f);  // [초안 §2-9] (천장 아랫면 4.0)
    private static readonly Vector3 ExitS4DoorCenter = new Vector3(29.75f, 1.75f, 77f); // [계산 초안 §2-9] 개구 x29.5~30·y0~3.5·z75~79 중심
    private const float ConsistencyTolerance = 0.001f; // [계약 — 지시서 S2-B 완료 조건 2 "±0.001"] 치수 ↔ 좌표표 대조

    // ── 개수 (초안 §2-1 :111 · §2-2·2-3 · §2-4 · §2-5, 계약 C1-1) ──
    private const int ExpectedBoxes = 47;       // [2차판정 10 — R3] 48 − Housing_In
    private const int ExpectedFloors = 1;
    private const int ExpectedWalls = 29;       // [2차판정 10 — R3] 30 − Housing_In
    private const int ExpectedCeilings = 6;
    private const int ExpectedObstacles = 11;
    private const int ExpectedNoFriction = 14;  // [초안 §2-3 :202] 둔덕 5 + 경사판 9
    private const int ExpectedLevers = 3;       // [계약 C1-1]
    private const int ExpectedBlocks = 39;      // [계약 C1-1] Pile 24 + Scatter 15

    private struct Box
    {
        public readonly string name;
        public readonly Vector3 min, max;
        public Box(string name, Vector3 min, Vector3 max) { this.name = name; this.min = min; this.max = max; }
    }

    private static Box B(string name, float x0, float y0, float z0, float x1, float y1, float z1) =>
        new Box(name, new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));

    /// <summary>마찰 0 조각 — 꼭짓점(섹터 로컬, 초안 §2-3a)과 표의 x·z 범위·윗면 최고 y(초안 §2-2·2-3, 메시 대조용).</summary>
    private struct Piece
    {
        public readonly string name;
        public readonly Vector3[] verts;
        public readonly float xMin, xMax, topMax, zMin, zMax;
        public Piece(string name, float xMin, float xMax, float topMax, float zMin, float zMax, params Vector3[] verts)
        { this.name = name; this.xMin = xMin; this.xMax = xMax; this.topMax = topMax; this.zMin = zMin; this.zMax = zMax; this.verts = verts; }
    }

    private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    private struct LeverDef
    {
        public readonly string name;
        public readonly Vector3 pos;
        public readonly float yaw;
        public LeverDef(string name, Vector3 pos, float yaw) { this.name = name; this.pos = pos; this.yaw = yaw; }
    }

    private struct BlockDef
    {
        public readonly string name;
        public readonly Vector3 center;
        public BlockDef(string name, Vector3 center) { this.name = name; this.center = center; }
    }

    // ───────────── 좌표표 — 초안 §2(= S2_check.py --md 출력)에서 기계 변환(재타이핑 없음) ─────────────

    /// <summary>지형 상자 47개 [초안 §2-1 #1~#37·#39~#49, #14 삭제 — 2차판정 10 R3]. GEO_S2_Floor_* → Map4Build.Floor, 나머지 → Map4Build.Wall. 이름은 계약 C2-2.</summary>
    private static readonly Box[] TerrainBoxes =
    {
        B("GEO_S2_Floor_Room", -38f, -0.3f, 0f, 38f, 0f, 84f), // #1 [계약 C2-2]
        B("GEO_S2_Wall_W", -38.5f, 0f, 0f, -38f, 9f, 84f), // #2 [제안]
        B("GEO_S2_Wall_E", 38f, 0f, 0f, 38.5f, 9f, 84f), // #3 [제안]
        B("GEO_S2_Wall_S_L", -38.5f, 0f, -0.5f, -4f, 9f, 0f), // #4 [제안] z −0.5~0 입구 벽 두께 허용 [2차판정 8]
        B("GEO_S2_Wall_S_R", 4f, 0f, -0.5f, 38.5f, 9f, 0f), // #5 [제안] z −0.5~0 입구 벽 두께 허용 [2차판정 8]
        B("GEO_S2_Wall_S_Lintel", -4f, 3.5f, -0.5f, 4f, 9f, 0f), // #6 [제안] z −0.5~0 입구 벽 두께 허용 [2차판정 8]
        B("GEO_S2_Wall_N_L", -38.5f, 0f, 84f, -4f, 9f, 84.5f), // #7 [제안]
        B("GEO_S2_Wall_N_R", 4f, 0f, 84f, 38.5f, 9f, 84.5f), // #8 [제안]
        B("GEO_S2_Wall_N_Lintel", -4f, 3.5f, 84f, 4f, 9f, 84.5f), // #9 [제안]
        B("GEO_S2_Wall_Iso_S", -38f, 0f, 30f, -12f, 9f, 30.5f), // #10 [제안]
        B("GEO_S2_Wall_Iso_N", -38f, 0f, 53.5f, -12f, 9f, 54f), // #11 [제안]
        B("GEO_S2_Ceil_Iso", -38f, 4f, 30.5f, -13.8f, 4.3f, 53.5f), // #12 [판정 7 사용자 (가)] x max −14.2 → −13.8: 슬롯 메움 [2차판정 10 — R3 C3g]
        B("GEO_S2_Ceil_IsoLip", -13.8f, 4f, 30.5f, -12f, 4.3f, 53.5f), // #13 [판정 7 사용자 (가)]
        // #14 GEO_S2_Wall_Housing_In(-14.45, 4.3, 30.5)~(-14.2, 8.6, 53.5) 삭제 [2차판정 10 — R3 C3g: Housing_Out과 숨은 벽 두께 0.4 틈 · IsoLip과 0.4 틈]
        B("GEO_S2_Wall_Housing_Out", -13.8f, 4.3f, 30.5f, -13.55f, 8.3f, 53.5f), // #15 [제안] y max 8.6 → 8.3(숨은 벽 윗면) [2차판정 10 — R3 C3g]
        B("GEO_S2_Ceil_Housing_Top", -14.2f, 8.3f, 30.5f, -13.55f, 8.5f, 53.5f), // #16 [제안] 숨은 벽 윗면 8.3에 맞닿게 y 8.6~8.8 → 8.3~8.5, x min −14.45 → −14.2 [2차판정 10 — R3 C3g]
        B("GEO_S2_Wall_IsoHidden", -14.2f, 4.3f, 30.5f, -13.8f, 8.3f, 53.5f), // #17 [판정 2]·[계약 C2-2]
        B("GEO_S2_Wall_Bay1_Part_S", -38f, 0f, 37f, -31f, 4f, 37.5f), // #18 [제안]
        B("GEO_S2_Wall_Bay1_Part_N", -38f, 0f, 46.5f, -31f, 4f, 47f), // #19 [제안]
        B("GEO_S2_Wall_Bay1_Jamb_S", -31f, 0f, 37f, -30.5f, 4f, 40f), // #20 [제안]
        B("GEO_S2_Wall_Bay1_Jamb_N", -31f, 0f, 44f, -30.5f, 4f, 47f), // #21 [제안]
        B("GEO_S2_Wall_Bay1_Lintel", -31f, 1.8f, 40f, -30.5f, 4f, 44f), // #22 [제안]
        B("GEO_S2_Ceil_Bay1_Lid", -38f, 1.8f, 37.5f, -31f, 2.1f, 46.5f), // #23 [판정 7 사용자 (가)]
        B("GEO_S2_Wall_Bay2_Part_S", 31f, 0f, 37f, 38f, 1.8f, 37.5f), // #24 [제안]
        B("GEO_S2_Wall_Bay2_Part_N", 31f, 0f, 46.5f, 38f, 1.8f, 47f), // #25 [제안]
        B("GEO_S2_Wall_Bay2_Jamb_S", 30.5f, 0f, 37f, 31f, 1.8f, 40f), // #26 [제안]
        B("GEO_S2_Wall_Bay2_Jamb_N", 30.5f, 0f, 44f, 31f, 1.8f, 47f), // #27 [제안]
        B("GEO_S2_Ceil_Bay2_Lid", 30.5f, 1.8f, 37f, 38f, 2.1f, 47f), // #28 [판정 7 사용자 (가)]
        B("GEO_S2_Wall_Bay3_Part_W", 15f, 0f, 0f, 15.5f, 1.8f, 7f), // #29 [제안]
        B("GEO_S2_Wall_Bay3_Part_E", 24.5f, 0f, 0f, 25f, 1.8f, 7f), // #30 [제안]
        B("GEO_S2_Wall_Bay3_Jamb_W", 15f, 0f, 7f, 18f, 1.8f, 7.5f), // #31 [제안]
        B("GEO_S2_Wall_Bay3_Jamb_E", 22f, 0f, 7f, 25f, 1.8f, 7.5f), // #32 [제안]
        B("GEO_S2_Ceil_Bay3_Lid", 15f, 1.8f, 0f, 25f, 2.1f, 7.5f), // #33 [판정 7 사용자 (가)]
        B("GEO_S2_Wall_Vest_W_S", 29.5f, 0f, 69.5f, 30f, 9f, 75f), // #34 [제안]
        B("GEO_S2_Wall_Vest_W_N", 29.5f, 0f, 79f, 30f, 9f, 84f), // #35 [제안]
        B("GEO_S2_Wall_Vest_Lintel", 29.5f, 3.5f, 75f, 30f, 9f, 79f), // #36 [제안]
        B("GEO_S2_Wall_Vest_S", 30f, 0f, 69.5f, 38f, 9f, 70f), // #37 [제안]
        B("GEO_S2_Obst_Shelf1", -30f, 0f, 10f, -24f, 1.5f, 11.2f), // #39 [제안]·[추정]
        B("GEO_S2_Obst_Shelf2", -38f, 0f, 20f, -32f, 1.5f, 21.2f), // #40 [제안]·[추정]
        B("GEO_S2_Obst_Desk1", -8f, 0f, 14f, -5f, 1.2f, 15.5f), // #41 [제안]·[추정]
        B("GEO_S2_Obst_Shelf3", 4f, 0f, 18f, 5.2f, 1.5f, 24f), // #42 [제안]·[추정]
        B("GEO_S2_Obst_Desk2", 12f, 0f, 28f, 15f, 1.2f, 29.5f), // #43 [제안]·[추정]
        B("GEO_S2_Obst_Shelf4", 32f, 0f, 20f, 38f, 1.5f, 21.2f), // #44 [제안]·[추정]
        B("GEO_S2_Obst_Cab1", -30f, 0f, 62f, -28f, 2f, 66f), // #45 [제안]·[추정]
        B("GEO_S2_Obst_Shelf5", 4f, 0f, 44f, 10f, 1.5f, 45.2f), // #46 [제안]·[추정]
        B("GEO_S2_Obst_Desk3", 12f, 0f, 74f, 15f, 1.2f, 75.5f), // #47 [제안]·[추정]
        B("GEO_S2_Obst_Shelf6", -24f, 0f, 82.8f, -18f, 1.5f, 84f), // #48 [제안]·[추정]
        B("GEO_S2_Obst_Cab2", 20f, 0f, 44f, 22f, 2f, 48f), // #49 [제안]·[추정]
    };

    /// <summary>S4행 문(닫힘) [초안 §2-1 #38].</summary>
    private static readonly Vector3 ExitS4DoorMin = new Vector3(29.1f, 0.3f, 74.5f); // #38 [제안]·[팀 기본]
    private static readonly Vector3 ExitS4DoorMax = new Vector3(29.5f, 3.8f, 79.5f); // #38 닫힘 위치

    /// <summary>마찰 0 조각 14 [초안 §2-2·§2-3 범위 · §2-3a 꼭짓점]. 행 순서 = 초안 = Refs noFrictionSolids 순서.</summary>
    private static readonly Piece[] NoFrictionPieces =
    {
        new Piece("GEO_S2_Hump_IsoLine", -16f, -12f, 0.3f, 30.5f, 53.5f, // §2-2 · 꼭짓점 6개 §2-3a
            V(-16f, 0f, 30.5f), V(-16f, 0f, 53.5f), V(-12f, 0f, 30.5f), V(-12f, 0f, 53.5f), V(-14f, 0.3f, 30.5f), V(-14f, 0.3f, 53.5f)),
        new Piece("GEO_S2_Hump_S3_Room", -6f, 6f, 0.3f, 80f, 84f, // §2-2 · 꼭짓점 6개 §2-3a
            V(-6f, 0f, 80f), V(-6f, 0f, 84f), V(6f, 0f, 80f), V(6f, 0f, 84f), V(-4f, 0.3f, 82f), V(4f, 0.3f, 82f)),
        new Piece("GEO_S2_Hump_S4_Room", 27.3f, 29.5f, 0.3f, 73f, 81f, // §2-2 · 꼭짓점 8개 §2-3a
            V(27.3f, 0f, 73f), V(27.3f, 0f, 81f), V(29.5f, 0f, 73f), V(29.5f, 0f, 81f), V(29.3f, 0.3f, 75f), V(29.3f, 0.3f, 79f), V(29.5f, 0.27f, 74.8f), V(29.5f, 0.27f, 79.2f)),
        new Piece("GEO_S2_Hump_S4_Gap", 29.5f, 30f, 0.27f, 75f, 79f, // §2-2 · 꼭짓점 8개 §2-3a
            V(29.5f, 0f, 75f), V(29.5f, 0f, 79f), V(30f, 0f, 75f), V(30f, 0f, 79f), V(29.5f, 0.27f, 75f), V(29.5f, 0.27f, 79f), V(30f, 0.195f, 75f), V(30f, 0.195f, 79f)),
        new Piece("GEO_S2_Hump_S4_Vest", 30f, 31.3f, 0.195f, 73f, 81f, // §2-2 · 꼭짓점 6개 §2-3a
            V(30f, 0f, 73f), V(30f, 0f, 81f), V(31.3f, 0f, 73f), V(31.3f, 0f, 81f), V(30f, 0.195f, 74.3f), V(30f, 0.195f, 79.7f)),
        new Piece("GEO_S2_Wedge_Bay1_A", -36.6076f, -31f, 0.3204f, 37.5f, 46.5f, // §2-3 · 꼭짓점 6개 §2-3a
            V(-36.6076f, 0f, 37.5f), V(-36.6076f, 0f, 46.5f), V(-31f, 0f, 37.5f), V(-31f, 0f, 46.5f), V(-36.6076f, 0.3204f, 37.5f), V(-36.6076f, 0.3204f, 46.5f)),
        new Piece("GEO_S2_Wedge_Bay1_B", -38f, -36.6076f, 0.4f, 37.5f, 40.3635f, // §2-3 · 꼭짓점 8개 §2-3a
            V(-38f, 0f, 37.5f), V(-38f, 0f, 40.3635f), V(-36.6076f, 0f, 37.5f), V(-36.6076f, 0f, 40.3635f), V(-38f, 0.4f, 37.5f), V(-38f, 0.4f, 40.3635f), V(-36.6076f, 0.3204f, 37.5f), V(-36.6076f, 0.3204f, 40.3635f)),
        new Piece("GEO_S2_Wedge_Bay1_C", -38f, -36.6076f, 0.4f, 42.7689f, 46.5f, // §2-3 · 꼭짓점 8개 §2-3a
            V(-38f, 0f, 42.7689f), V(-38f, 0f, 46.5f), V(-36.6076f, 0f, 42.7689f), V(-36.6076f, 0f, 46.5f), V(-38f, 0.4f, 42.7689f), V(-38f, 0.4f, 46.5f), V(-36.6076f, 0.3204f, 42.7689f), V(-36.6076f, 0.3204f, 46.5f)),
        new Piece("GEO_S2_Wedge_Bay2_A", 31f, 36.6076f, 0.3204f, 37.5f, 46.5f, // §2-3 · 꼭짓점 6개 §2-3a
            V(31f, 0f, 37.5f), V(31f, 0f, 46.5f), V(36.6076f, 0f, 37.5f), V(36.6076f, 0f, 46.5f), V(36.6076f, 0.3204f, 37.5f), V(36.6076f, 0.3204f, 46.5f)),
        new Piece("GEO_S2_Wedge_Bay2_B", 36.6076f, 38f, 0.4f, 37.5f, 41.2311f, // §2-3 · 꼭짓점 8개 §2-3a
            V(36.6076f, 0f, 37.5f), V(36.6076f, 0f, 41.2311f), V(38f, 0f, 37.5f), V(38f, 0f, 41.2311f), V(36.6076f, 0.3204f, 37.5f), V(36.6076f, 0.3204f, 41.2311f), V(38f, 0.4f, 37.5f), V(38f, 0.4f, 41.2311f)),
        new Piece("GEO_S2_Wedge_Bay2_C", 36.6076f, 38f, 0.4f, 43.6365f, 46.5f, // §2-3 · 꼭짓점 8개 §2-3a
            V(36.6076f, 0f, 43.6365f), V(36.6076f, 0f, 46.5f), V(38f, 0f, 43.6365f), V(38f, 0f, 46.5f), V(36.6076f, 0.3204f, 43.6365f), V(36.6076f, 0.3204f, 46.5f), V(38f, 0.4f, 43.6365f), V(38f, 0.4f, 46.5f)),
        new Piece("GEO_S2_Wedge_Bay3_A", 15.5f, 24.5f, 0.3204f, 1.3924f, 7f, // §2-3 · 꼭짓점 6개 §2-3a
            V(15.5f, 0f, 1.3924f), V(15.5f, 0f, 7f), V(24.5f, 0f, 1.3924f), V(24.5f, 0f, 7f), V(15.5f, 0.3204f, 1.3924f), V(24.5f, 0.3204f, 1.3924f)),
        new Piece("GEO_S2_Wedge_Bay3_B", 15.5f, 19.231f, 0.4f, 0f, 1.3924f, // §2-3 · 꼭짓점 8개 §2-3a
            V(15.5f, 0f, 0f), V(15.5f, 0f, 1.3924f), V(19.231f, 0f, 0f), V(19.231f, 0f, 1.3924f), V(15.5f, 0.4f, 0f), V(15.5f, 0.3204f, 1.3924f), V(19.231f, 0.4f, 0f), V(19.231f, 0.3204f, 1.3924f)),
        new Piece("GEO_S2_Wedge_Bay3_C", 21.6365f, 24.5f, 0.4f, 0f, 1.3924f, // §2-3 · 꼭짓점 8개 §2-3a
            V(21.6365f, 0f, 0f), V(21.6365f, 0f, 1.3924f), V(24.5f, 0f, 0f), V(24.5f, 0f, 1.3924f), V(21.6365f, 0.4f, 0f), V(21.6365f, 0.3204f, 1.3924f), V(24.5f, 0.4f, 0f), V(24.5f, 0.3204f, 1.3924f)),
    };

    /// <summary>레버 부모 3 [초안 §2-4 첫 표 — 위치 [계산]·yaw [계산]]. 순서 = 계약 C1-1.</summary>
    private static readonly LeverDef[] Levers =
    {
        new LeverDef("S2_Lever1_Trap", new Vector3(-37.1038f, 0.485f, 42f), 180f), // [계산 초안 §2-4] 위치 · yaw
        new LeverDef("S2_Lever2", new Vector3(37.1038f, 0.485f, 42f), 0f), // [계산 초안 §2-4] 위치 · yaw
        new LeverDef("S2_Lever3", new Vector3(20f, 0.485f, 0.8962f), 90f), // [계산 초안 §2-4] 위치 · yaw
    };

    /// <summary>딱딱블록 39 [초안 §2-5 — 중심 [제안], 1×1×1·회전 0]. 행 순서 = 계약 C1-1 blocks 순서.</summary>
    private static readonly BlockDef[] Blocks =
    {
        new BlockDef("S2_Block_Pile1_B0", new Vector3(-30f, 0.5f, 40.5f)), // #1 L1 칸 입구 더미
        new BlockDef("S2_Block_Pile1_B1", new Vector3(-30f, 0.5f, 41.5f)), // #2 L1 칸 입구 더미
        new BlockDef("S2_Block_Pile1_B2", new Vector3(-30f, 0.5f, 42.5f)), // #3 L1 칸 입구 더미
        new BlockDef("S2_Block_Pile1_B3", new Vector3(-30f, 0.5f, 43.5f)), // #4 L1 칸 입구 더미
        new BlockDef("S2_Block_Pile1_T0", new Vector3(-30f, 1.5f, 40.5f)), // #5 L1 칸 입구 더미
        new BlockDef("S2_Block_Pile1_T1", new Vector3(-30f, 1.5f, 41.5f)), // #6 L1 칸 입구 더미
        new BlockDef("S2_Block_Pile1_T2", new Vector3(-30f, 1.5f, 42.5f)), // #7 L1 칸 입구 더미
        new BlockDef("S2_Block_Pile1_T3", new Vector3(-30f, 1.5f, 43.5f)), // #8 L1 칸 입구 더미
        new BlockDef("S2_Block_Pile2_B0", new Vector3(30f, 0.5f, 40.5f)), // #9 L2 칸 입구 더미
        new BlockDef("S2_Block_Pile2_B1", new Vector3(30f, 0.5f, 41.5f)), // #10 L2 칸 입구 더미
        new BlockDef("S2_Block_Pile2_B2", new Vector3(30f, 0.5f, 42.5f)), // #11 L2 칸 입구 더미
        new BlockDef("S2_Block_Pile2_B3", new Vector3(30f, 0.5f, 43.5f)), // #12 L2 칸 입구 더미
        new BlockDef("S2_Block_Pile2_T0", new Vector3(30f, 1.5f, 40.5f)), // #13 L2 칸 입구 더미
        new BlockDef("S2_Block_Pile2_T1", new Vector3(30f, 1.5f, 41.5f)), // #14 L2 칸 입구 더미
        new BlockDef("S2_Block_Pile2_T2", new Vector3(30f, 1.5f, 42.5f)), // #15 L2 칸 입구 더미
        new BlockDef("S2_Block_Pile2_T3", new Vector3(30f, 1.5f, 43.5f)), // #16 L2 칸 입구 더미
        new BlockDef("S2_Block_Pile3_B0", new Vector3(18.5f, 0.5f, 8f)), // #17 L3 칸 입구 더미
        new BlockDef("S2_Block_Pile3_B1", new Vector3(19.5f, 0.5f, 8f)), // #18 L3 칸 입구 더미
        new BlockDef("S2_Block_Pile3_B2", new Vector3(20.5f, 0.5f, 8f)), // #19 L3 칸 입구 더미
        new BlockDef("S2_Block_Pile3_B3", new Vector3(21.5f, 0.5f, 8f)), // #20 L3 칸 입구 더미
        new BlockDef("S2_Block_Pile3_T0", new Vector3(18.5f, 1.5f, 8f)), // #21 L3 칸 입구 더미
        new BlockDef("S2_Block_Pile3_T1", new Vector3(19.5f, 1.5f, 8f)), // #22 L3 칸 입구 더미
        new BlockDef("S2_Block_Pile3_T2", new Vector3(20.5f, 1.5f, 8f)), // #23 L3 칸 입구 더미
        new BlockDef("S2_Block_Pile3_T3", new Vector3(21.5f, 1.5f, 8f)), // #24 L3 칸 입구 더미
        new BlockDef("S2_Block_Scatter_00", new Vector3(-9f, 0.5f, 6f)), // #25 흩어짐
        new BlockDef("S2_Block_Scatter_01", new Vector3(9f, 0.5f, 4f)), // #26 흩어짐
        new BlockDef("S2_Block_Scatter_02", new Vector3(-3f, 0.5f, 13f)), // #27 흩어짐
        new BlockDef("S2_Block_Scatter_03", new Vector3(11f, 0.5f, 13f)), // #28 흩어짐
        new BlockDef("S2_Block_Scatter_04", new Vector3(-16f, 0.5f, 20f)), // #29 흩어짐
        new BlockDef("S2_Block_Scatter_05", new Vector3(18f, 0.5f, 19f)), // #30 흩어짐
        new BlockDef("S2_Block_Scatter_06", new Vector3(-2f, 0.5f, 30f)), // #31 흩어짐
        new BlockDef("S2_Block_Scatter_07", new Vector3(9f, 0.5f, 36f)), // #32 흩어짐
        new BlockDef("S2_Block_Scatter_08", new Vector3(-5f, 0.5f, 62f)), // #33 흩어짐
        new BlockDef("S2_Block_Scatter_09", new Vector3(10f, 0.5f, 66f)), // #34 흩어짐
        new BlockDef("S2_Block_Scatter_10", new Vector3(19f, 0.5f, 58f)), // #35 흩어짐
        new BlockDef("S2_Block_Scatter_11", new Vector3(-13f, 0.5f, 74f)), // #36 흩어짐
        new BlockDef("S2_Block_Scatter_12", new Vector3(-26f, 0.5f, 35f)), // #37 흩어짐(격리 공간 안)
        new BlockDef("S2_Block_Scatter_13", new Vector3(-24f, 0.5f, 49f)), // #38 흩어짐(격리 공간 안)
        new BlockDef("S2_Block_Scatter_14", new Vector3(-20f, 0.5f, 42f)), // #39 흩어짐(격리 공간 안)
    };

    private static Transform g;           // Build 동안만 유효 — Generated 루트
    private static Transform gimmickRoot; // Build 동안만 유효 — generated/S2_Gimmicks

    /// <summary>S2를 짓는다(R2 계약 C1-0 호출 순서 · C5 예외 표준). 전제(폭 76·길이 84·바닥 0·출구 0, ±0.01)와 다르면 아무것도
    /// 짓지 않고 false — 호출자가 빈 틀로 되돌아간다.</summary>
    public static bool Build(Transform generated, Map4Layout.SectorDef def)
    {
        // (1) 전제 검사 — 맨 앞. 아무것도 만들기 전에.
        if (!PreconditionsOk(generated, def))
        {
            Debug.LogError(generated == null || def == null
                ? "[S2_Builder] 전제 불일치 — generated 또는 SectorDef가 null이다. S2 구체화를 건너뛰고 빈 틀로 짓는다."
                : $"[S2_Builder] 전제 불일치 — 폭 {def.width}·길이 {def.length}·바닥 {def.floorHeight}·출구 {def.exitHeight}" +
                  $"(전제 {SectorWidth}·{SectorLength}·{FloorHeight}·{ExitHeight}, ±{PreconditionTolerance}). S2 구체화를 건너뛰고 빈 틀로 짓는다.");
            return false;
        }

        g = generated;
        var before = new HashSet<GameObject>(generated.gameObject.scene.GetRootGameObjects());
        S2_Refs refs = new S2_Refs { generated = generated };
        try
        {
            Map4Build.BeginSection();
            // 지형 → 마찰 0 조각 → 팀 기믹(레버 → 문 → 블록) → Refs 채우기 → Refs 검증(C1-1). 실패는 모두 throw.
            BuildTerrain(refs);
            BuildExitS3Opening(refs);
            BuildNoFriction(refs);
            gimmickRoot = NewGroup(GimmickRootName);
            refs.gimmickRoot = gimmickRoot;
            BuildLevers(refs);      // 문보다 먼저 — 메뉴의 가장 가까운 문 자동 연결을 피한다(초안 §3 주)
            BuildExitS4Door(refs);
            BuildBlocks(refs);
            FillTerrainRefs(refs);
            ValidateRefs(refs, before);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[S2_Builder] 섹터2 구체화 실패 — Generated를 비우고 빈 틀로 되돌린다: {e.Message}\n{e.StackTrace}");
            for (int i = generated.childCount - 1; i >= 0; i--) Object.DestroyImmediate(generated.GetChild(i).gameObject);
            foreach (GameObject r in generated.gameObject.scene.GetRootGameObjects())
                if (!before.Contains(r)) Object.DestroyImmediate(r);   // 이번 Build가 팀 메뉴로 만든 루트 잔류물
            g = null; gimmickRoot = null; return false;
        }
        try { S2_Wiring.Wire(generated, refs); } catch (System.Exception e) { Debug.LogError($"[S2_Builder] Wire 예외 — 지형 유지: {e}"); }
        try { S2_Dress.Apply(generated, refs); } catch (System.Exception e) { Debug.LogError($"[S2_Builder] Dress 예외 — 지형 유지: {e}"); }
        ReportStrayRoots();
        g = null; gimmickRoot = null; return true;
    }

    private static bool PreconditionsOk(Transform generated, Map4Layout.SectorDef def) =>
        generated != null
        && def != null
        && Mathf.Abs(def.width - SectorWidth) <= PreconditionTolerance
        && Mathf.Abs(def.length - SectorLength) <= PreconditionTolerance
        && Mathf.Abs(def.floorHeight - FloorHeight) <= PreconditionTolerance
        && Mathf.Abs(def.exitHeight - ExitHeight) <= PreconditionTolerance;

    // ───────────────────────── 지형 ─────────────────────────

    /// <summary>지형 상자 47개. 이름 접두사로 만드는 법과 Refs 목록을 정한다: Floor → Map4Build.Floor(목록 없음),
    /// Wall → refs.walls, Ceil → refs.ceilings, Obst → refs.obstacles(모두 Map4Build.Wall, 행 순서 그대로). 숨은 벽은 refs.isoHiddenWall.</summary>
    private static void BuildTerrain(S2_Refs refs)
    {
        foreach (Box b in TerrainBoxes)
        {
            bool isFloor = b.name.StartsWith(FloorPrefix, System.StringComparison.Ordinal);
            Transform t = Solid(isFloor ? Map4Build.Floor(g, b.min, b.max) : Map4Build.Wall(g, b.min, b.max), b.name);
            if (isFloor) continue; // GEO_S2_Floor_Room — Refs 목록 없음(계약 C1-2)
            if (b.name.StartsWith(WallPrefix, System.StringComparison.Ordinal)) refs.walls.Add(t);
            else if (b.name.StartsWith(CeilPrefix, System.StringComparison.Ordinal)) refs.ceilings.Add(t);
            else if (b.name.StartsWith(ObstPrefix, System.StringComparison.Ordinal)) refs.obstacles.Add(t);
            else throw new System.Exception($"[S2_Builder] 계약 C2-2 이름표에 없는 지형 이름: {b.name}");
            if (b.name == IsoHiddenWallName) refs.isoHiddenWall = t;
        }
    }

    /// <summary>S3행 개구 표지 — 빈 GO, 콜라이더 없음, 로컬 (0, 0, 84)·회전 0 [판정 1]·[계약 C2-2]. 팀 격리 기능 반영 때 문으로 교체.</summary>
    private static void BuildExitS3Opening(S2_Refs refs)
    {
        GameObject go = new GameObject(ExitS3OpeningName);
        go.transform.SetParent(g, false);
        go.transform.localPosition = ExitS3OpeningLocalPos;
        go.transform.localRotation = Quaternion.identity;
        refs.exitS3Opening = go.transform;
    }

    // ───────────────────────── 마찰 0 조각 ─────────────────────────

    /// <summary>마찰 0 조각 14 [초안 §2-2·2-3·2-3a]. GO는 generated 바로 아래 로컬 위치 0·회전 0(Map4Build.Ramp와 같은 방식 — 꼭짓점이
    /// 섹터 로컬 좌표 그대로). 볼록 MeshCollider(convex) + MeshFilter/MeshRenderer, 콜라이더 sharedMaterial = PM_Map4_NoFriction.
    /// 메시 경계가 초안 표의 x·z 범위·아랫면 0·윗면 최고 y와 ±0.001로 맞지 않으면 throw.</summary>
    private static void BuildNoFriction(S2_Refs refs)
    {
        PhysicMaterial pm = LoadOrCreateNoFriction();
        Material mat = Map4Build.GetMaterial(NoFrictionPalette);
        foreach (Piece p in NoFrictionPieces)
        {
            if (!p.name.StartsWith(HumpPrefix, System.StringComparison.Ordinal) && !p.name.StartsWith(WedgePrefix, System.StringComparison.Ordinal))
                throw new System.Exception($"[S2_Builder] 계약 C2-2 이름표에 없는 마찰 0 조각 이름: {p.name}");
            Mesh mesh = ConvexHullMesh(p.name, p.verts);
            Bounds bb = mesh.bounds;
            Check($"{p.name} x min", bb.min.x, p.xMin);
            Check($"{p.name} x max", bb.max.x, p.xMax);
            Check($"{p.name} z min", bb.min.z, p.zMin);
            Check($"{p.name} z max", bb.max.z, p.zMax);
            Check($"{p.name} 아랫면 y", bb.min.y, 0f);
            Check($"{p.name} 윗면 최고 y", bb.max.y, p.topMax);

            GameObject go = new GameObject(p.name);
            go.transform.SetParent(g, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            MeshCollider col = go.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            col.convex = true;
            col.sharedMaterial = pm;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            refs.noFrictionSolids.Add(go.transform);
        }
        refs.noFriction = pm;
    }

    /// <summary>[마찰 0 헬퍼 — S2-B 소유, 2차판정 10] PM_Map4_NoFriction(dynamic 0·static 0·frictionCombine Minimum·bounce 0 — 초안 §1-5·§2-9)을
    /// 우리 폴더에 만들거나 재사용.
    /// 값이 다를 때만 고치고 SetDirty(같으면 파일을 건드리지 않는다). 팀 파일 무수정.</summary>
    private static PhysicMaterial LoadOrCreateNoFriction()
    {
        EnsureFolder(NoFrictionDir);
        string path = $"{NoFrictionDir}/{NoFrictionName}.physicMaterial";
        PhysicMaterial pm = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(path);
        if (pm == null)
        {
            pm = new PhysicMaterial(NoFrictionName);
            AssetDatabase.CreateAsset(pm, path);
        }
        bool changed = pm.dynamicFriction != 0f || pm.staticFriction != 0f || pm.bounciness != 0f
                       || pm.frictionCombine != PhysicMaterialCombine.Minimum;
        if (changed)
        {
            pm.dynamicFriction = 0f;                              // [제안 초안 §1-5]
            pm.staticFriction = 0f;                               // [제안 초안 §1-5]
            pm.bounciness = 0f;                                   // [제안 초안 §1-5]
            pm.frictionCombine = PhysicMaterialCombine.Minimum;   // [제안 초안 §1-5] 블록(기본 0.6)·도형(Average)과 결합해도 0
            EditorUtility.SetDirty(pm);
        }
        if (pm.name != NoFrictionName) throw new System.Exception($"[S2_Builder] 물리 재질 이름 '{pm.name}' ≠ {NoFrictionName}(PTF-2가 이름으로 찾는다).");
        return pm;
    }

    /// <summary>[마찰 0 헬퍼 — S2-B 소유, 2차판정 10] 작은 점 집합(6~8개)의 볼록 껍질 메시 — 세 점마다 평면을 세워 나머지 점이 모두 한쪽에 있으면 면으로 채택(중복 평면 제거),
    /// 면 위 점을 면 중심 둘레 각도로 정렬해 부채꼴 삼각형으로 나눈다. 면마다 정점을 따로 두어(플랫 셰이딩) 바깥 법선 방향으로 감는다.
    /// 볼록 MeshCollider는 정점의 볼록 껍질을 쓰므로 콜라이더 형상 = 이 점들의 껍질(초안 §2-3a). 면이 4개 미만이거나 껍질 밖 점이 남으면 throw.</summary>
    private static Mesh ConvexHullMesh(string name, Vector3[] pts)
    {
        const float Eps = 1e-4f;
        int n = pts.Length;
        if (n < 4) throw new System.Exception($"[S2_Builder] {name} 꼭짓점 {n}개 — 볼록 입체가 아니다.");
        Vector3 centroid = Vector3.zero;
        foreach (Vector3 p in pts) centroid += p;
        centroid /= n;

        List<Vector3> planeN = new List<Vector3>();
        List<float> planeD = new List<float>();
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        bool[] used = new bool[n];
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                for (int k = j + 1; k < n; k++)
                {
                    Vector3 nrm = Vector3.Cross(pts[j] - pts[i], pts[k] - pts[i]);
                    if (nrm.sqrMagnitude < 1e-10f) continue;
                    nrm.Normalize();
                    float d = Vector3.Dot(nrm, pts[i]);
                    if (Vector3.Dot(nrm, centroid) - d > 0f) { nrm = -nrm; d = -d; } // 바깥 방향
                    bool supporting = true;
                    for (int m = 0; m < n && supporting; m++)
                        if (Vector3.Dot(nrm, pts[m]) - d > Eps) supporting = false;
                    if (!supporting) continue;
                    bool dup = false;
                    for (int f = 0; f < planeN.Count && !dup; f++)
                        if (Vector3.Dot(planeN[f], nrm) > 1f - 1e-5f && Mathf.Abs(planeD[f] - d) < Eps) dup = true;
                    if (dup) continue;
                    planeN.Add(nrm);
                    planeD.Add(d);

                    List<Vector3> face = new List<Vector3>();
                    for (int m = 0; m < n; m++)
                        if (Mathf.Abs(Vector3.Dot(nrm, pts[m]) - d) <= Eps) { face.Add(pts[m]); used[m] = true; }
                    Vector3 c = Vector3.zero;
                    foreach (Vector3 p in face) c += p;
                    c /= face.Count;
                    Vector3 u = (face[0] - c).normalized;
                    Vector3 w = Vector3.Cross(nrm, u);
                    face.Sort((a, b) => Mathf.Atan2(Vector3.Dot(a - c, w), Vector3.Dot(a - c, u))
                        .CompareTo(Mathf.Atan2(Vector3.Dot(b - c, w), Vector3.Dot(b - c, u))));
                    int baseIndex = verts.Count;
                    verts.AddRange(face);
                    for (int t = 1; t + 1 < face.Count; t++)
                    {
                        int a = 0, b = t, e = t + 1;
                        // Unity 앞면 = Cross(b − a, e − a)가 바깥을 향하는 감김(Map4Build.Ramp 윗면과 같은 규약)
                        if (Vector3.Dot(Vector3.Cross(face[b] - face[a], face[e] - face[a]), nrm) < 0f) { int s = b; b = e; e = s; }
                        tris.Add(baseIndex + a); tris.Add(baseIndex + b); tris.Add(baseIndex + e);
                    }
                }
        if (planeN.Count < 4) throw new System.Exception($"[S2_Builder] {name} 볼록 껍질 면 {planeN.Count}개 — 입체가 아니다.");
        for (int m = 0; m < n; m++)
            if (!used[m]) throw new System.Exception($"[S2_Builder] {name} 꼭짓점 {pts[m]}이(가) 껍질 면 위에 없다 — 초안 §2-3a 꼭짓점이 볼록 다면체 꼭짓점이 아니다.");

        Mesh mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ───────────────────────── 팀 기믹 ─────────────────────────

    /// <summary>레버 3 [초안 §2-4·§3]. 부모(스케일 1)를 S2_Gimmicks 아래 §2-4 위치·yaw로 먼저 만들고, 팀 메뉴 Create Lever가 씬 루트에 만든
    /// lever_pivot(자식 lever_head + LeverHead)·lever_body를 루트 비교로 찾아(같은 이름이 여럿 생기므로 이름 검색 아님 — 초안 §3 ④)
    /// 부모 아래로 옮긴다. 메뉴가 가장 가까운 문의 leverHead에 꽂는 자동 연결은 호출 전후 기록·복원으로 되돌린다.</summary>
    private static void BuildLevers(S2_Refs refs)
    {
        foreach (LeverDef d in Levers)
        {
            GameObject parent = new GameObject(d.name);
            parent.transform.SetParent(gimmickRoot, false);
            parent.transform.localScale = Vector3.one;
            Place(parent.transform, d.pos, Quaternion.Euler(0f, d.yaw, 0f));

            Scene scene = SceneManager.GetActiveScene();
            HashSet<GameObject> rootsBefore = new HashSet<GameObject>(scene.GetRootGameObjects());
            Dictionary<doorPhysics, LeverHead> doorsBefore = SnapshotDoorLevers();
            Selection.activeGameObject = null;
            bool ran = EditorApplication.ExecuteMenuItem(LeverMenuPath);
            RestoreDoorLevers(doorsBefore);
            if (!ran) throw new System.Exception($"[S2_Builder] 메뉴 실행 실패: {LeverMenuPath}");

            GameObject pivot = null, body = null;
            List<string> others = new List<string>();
            foreach (GameObject r in scene.GetRootGameObjects())
            {
                if (rootsBefore.Contains(r)) continue;
                if (r.GetComponentInChildren<LeverHead>(true) != null)
                {
                    if (pivot != null) throw new System.Exception($"[S2_Builder] {LeverMenuPath}가 LeverHead 루트를 2개 이상 만들었다({pivot.name}, {r.name}).");
                    pivot = r;
                }
                else if (r.name == TeamLeverBodyName)
                {
                    if (body != null) throw new System.Exception($"[S2_Builder] {LeverMenuPath}가 {TeamLeverBodyName}를 2개 이상 만들었다.");
                    body = r;
                }
                else others.Add(r.name);
            }
            if (pivot == null || body == null)
                throw new System.Exception($"[S2_Builder] {LeverMenuPath} 생성물을 찾지 못했다 — pivot {(pivot != null ? pivot.name : "없음")}, body {(body != null ? body.name : "없음")}.");
            if (others.Count > 0)
                throw new System.Exception($"[S2_Builder] {LeverMenuPath}가 예상 밖 루트를 만들었다: {string.Join(", ", others)}.");
            if (Selection.activeGameObject != null && Selection.activeGameObject != pivot)
                Debug.LogWarning($"[S2_Builder] {LeverMenuPath} 직후 선택 '{Selection.activeGameObject.name}' ≠ 루트 비교로 찾은 '{pivot.name}' — 루트 비교 결과를 쓴다.");

            // pivot: 부모 아래 로컬 위치 0 · 로컬 yaw −45(메뉴값) · 스케일 메뉴값 그대로(worldPositionStays false — 로컬 스케일 유지) [초안 §3]
            pivot.transform.SetParent(parent.transform, false);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.Euler(0f, LeverPivotLocalYaw, 0f);

            // 받침대: 이름 = 부모 이름 + _Body, 로컬 위치·회전 0·크기 — 높이만 바닥 0 ~ pivot 아랫면(배치 변환) [초안 §2-4 · 2차판정 10 — R3]
            body.name = d.name + BodySuffix;
            body.transform.SetParent(parent.transform, false);
            body.transform.localPosition = LeverBodyLocalPos;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = LeverBodyLocalScale;

            LeverHead lh = pivot.GetComponentInChildren<LeverHead>(true);
            if (lh.leverPivot != pivot.transform)
                throw new System.Exception($"[S2_Builder] {d.name}: LeverHead.leverPivot이 메뉴가 만든 lever_pivot이 아니다.");
            refs.leverRoots.Add(parent.transform);
            refs.levers.Add(lh);
        }
    }

    /// <summary>S4행 문 [초안 §2-1 #38·§3]: Map4Build.Door(닫힘, openHeight 3.7) — doorSpeed는 팀 기본 2, leverHead None.
    /// 헬퍼가 같은 부모(generated)에 만든 VIS_NoCollide_DoorFrame은 문판이 열릴 때 뚫고 지나가므로 즉시 삭제한다(계약 C2-2).
    /// 자식 Visual → VIS_S2_ExitS4(계약 C2-0 규칙을 DOOR_에 적용 — DOOR_ 뗀 이름).</summary>
    private static void BuildExitS4Door(S2_Refs refs)
    {
        doorPhysics dp = Map4Build.Door(g, ExitS4DoorMin, ExitS4DoorMax, ExitS4DoorOpenHeight);
        if (dp == null) throw new System.Exception($"[S2_Builder] {ExitS4DoorName} 생성 실패 — 위 Map4Build 로그 확인.");
        dp.name = ExitS4DoorName;
        Transform vis = dp.transform.Find("Visual");
        if (vis != null) vis.name = "VIS_" + ExitS4DoorName.Substring("DOOR_".Length);

        int removed = 0;
        for (int i = g.childCount - 1; i >= 0; i--)
        {
            Transform c = g.GetChild(i);
            if (c.name != DoorFrameName) continue;
            Object.DestroyImmediate(c.gameObject);
            removed++;
        }
        if (removed != 1) throw new System.Exception($"[S2_Builder] {DoorFrameName} 삭제 {removed}개 — Map4Build.Door가 만든 1개여야 한다.");

        if (dp.leverHead != null)
        {
            Debug.LogWarning($"[S2_Builder] {ExitS4DoorName}.leverHead = '{dp.leverHead.name}' — null로 비운다(계약 C1-5).");
            dp.leverHead = null;
        }
        refs.exitS4Door = dp;
    }

    /// <summary>딱딱블록 39 [초안 §2-5·§3]: 팀 메뉴 Create Snap Block → §2-5 이름으로 바꾸고 §2-5 중심·회전 0에 둔다(S2_Gimmicks 아래).
    /// 인스펙터 값(1×1×1, mass 1, Interpolate·ContinuousDynamic)은 팀 기본 그대로.</summary>
    private static void BuildBlocks(S2_Refs refs)
    {
        foreach (BlockDef b in Blocks)
        {
            SnapBlock sb = MenuCreate<SnapBlock>(SnapBlockMenuPath);
            sb.name = b.name;
            Place(sb.transform, b.center, Quaternion.identity);
            refs.blocks.Add(sb);
        }
    }

    // ───────────────────────── Refs ─────────────────────────

    /// <summary>Refs 지형 치수(초안 §2-9). roomXMin·roomXMax·roomZMax는 계약 C1-2 초기값(−38·38·84) 그대로 둔다.</summary>
    private static void FillTerrainRefs(S2_Refs refs)
    {
        refs.outerWallTop = OuterWallTop;
        refs.isoLineX = IsoLineX;
        refs.isoSpace = MinMax(IsoSpaceMin, IsoSpaceMax);
        refs.exitS4DoorCenter = ExitS4DoorCenter;
    }

    /// <summary>Refs 검증(계약 C1-1 S2행 전부 + 지형 치수 대조 + 계약 C1-5 leverHead null + 씬 루트 잔류 0). 실패면 throw → C5 표준으로 false.</summary>
    private static void ValidateRefs(S2_Refs refs, HashSet<GameObject> rootsBeforeBuild)
    {
        // ── 표 개수 ──
        int floors = 0;
        foreach (Box b in TerrainBoxes)
            if (b.name.StartsWith(FloorPrefix, System.StringComparison.Ordinal)) floors++;
        if (TerrainBoxes.Length != ExpectedBoxes || floors != ExpectedFloors || NoFrictionPieces.Length != ExpectedNoFriction
            || Levers.Length != ExpectedLevers || Blocks.Length != ExpectedBlocks)
            throw new System.Exception($"[S2_Builder] 좌표표 개수가 초안과 다르다 — 상자 {TerrainBoxes.Length}/{ExpectedBoxes}, 바닥 {floors}/{ExpectedFloors}, " +
                                       $"마찰 0 {NoFrictionPieces.Length}/{ExpectedNoFriction}, 레버 {Levers.Length}/{ExpectedLevers}, 블록 {Blocks.Length}/{ExpectedBlocks}.");

        // ── 지형 목록 ──
        if (refs.walls.Count != ExpectedWalls || refs.ceilings.Count != ExpectedCeilings || refs.obstacles.Count != ExpectedObstacles
            || refs.walls.Contains(null) || refs.ceilings.Contains(null) || refs.obstacles.Contains(null))
            throw new System.Exception($"[S2_Builder] 지형 Refs가 계약과 다르다 — walls {refs.walls.Count}/{ExpectedWalls}, " +
                                       $"ceilings {refs.ceilings.Count}/{ExpectedCeilings}, obstacles {refs.obstacles.Count}/{ExpectedObstacles}, 또는 null 포함.");
        for (int i = 0; i < ObstacleOrder.Length; i++)
            if (refs.obstacles[i].name != ObstacleOrder[i])
                throw new System.Exception($"[S2_Builder] obstacles[{i}] = {refs.obstacles[i].name} — 계약 C1-1 순서는 {ObstacleOrder[i]}.");

        // ── 각 1개 (C1-1) ──
        if (refs.isoHiddenWall == null || refs.exitS3Opening == null || refs.exitS4Door == null || refs.noFriction == null || refs.gimmickRoot == null)
            throw new System.Exception("[S2_Builder] isoHiddenWall·exitS3Opening·exitS4Door·noFriction·gimmickRoot 가운데 null이 있다.");

        // ── 마찰 0 조각 14 (C3 PTF-2: 콜라이더 sharedMaterial = PM_Map4_NoFriction) ──
        if (refs.noFrictionSolids.Count != ExpectedNoFriction || refs.noFrictionSolids.Contains(null))
            throw new System.Exception($"[S2_Builder] noFrictionSolids {refs.noFrictionSolids.Count}/{ExpectedNoFriction} 또는 null 포함.");
        for (int i = 0; i < ExpectedNoFriction; i++)
        {
            Transform t = refs.noFrictionSolids[i];
            MeshCollider mc = t.GetComponent<MeshCollider>();
            if (t.name != NoFrictionPieces[i].name || mc == null || !mc.convex || mc.sharedMaterial != refs.noFriction)
                throw new System.Exception($"[S2_Builder] noFrictionSolids[{i}] '{t.name}' — 이름·볼록 MeshCollider·재질 가운데 초안과 다른 것이 있다.");
        }

        // ── 레버 3 (C1-1 순서 L1_Trap, L2, L3) ──
        if (refs.leverRoots.Count != ExpectedLevers || refs.levers.Count != ExpectedLevers || refs.leverRoots.Contains(null) || refs.levers.Contains(null))
            throw new System.Exception($"[S2_Builder] leverRoots {refs.leverRoots.Count}·levers {refs.levers.Count} — 계약 C1-1은 {ExpectedLevers}개, null 없음.");
        for (int i = 0; i < ExpectedLevers; i++)
        {
            if (Levers[i].name != LeverOrder[i] || refs.leverRoots[i].name != LeverOrder[i] || !refs.levers[i].transform.IsChildOf(refs.leverRoots[i])
                || refs.leverRoots[i].parent != gimmickRoot || refs.leverRoots[i].Find(LeverOrder[i] + BodySuffix) == null)
                throw new System.Exception($"[S2_Builder] 레버 [{i}] '{refs.leverRoots[i].name}' — 계약 C1-1 순서({LeverOrder[i]})·부모 {GimmickRootName}·받침대·LeverHead 위치가 다르다.");
        }
        if (g.GetComponentsInChildren<LeverHead>(true).Length != ExpectedLevers)
            throw new System.Exception($"[S2_Builder] generated 아래 LeverHead {g.GetComponentsInChildren<LeverHead>(true).Length}개 ≠ {ExpectedLevers}.");

        // ── [2차판정 10 — R3] 받침대 ↔ 팀 pivot·head 관통 0 · 정적 일반 끼임(C3g) ──
        ValidateLeverClearance(refs);
        ValidateStaticGaps(refs);

        // ── 블록 39 (C1-1 순서 Pile1 B0~B3·T0~T3 → Pile2 → Pile3 → Scatter_00~14) ──
        List<string> blockOrder = ExpectedBlockOrder();
        if (refs.blocks.Count != ExpectedBlocks || refs.blocks.Contains(null))
            throw new System.Exception($"[S2_Builder] blocks {refs.blocks.Count}/{ExpectedBlocks} 또는 null 포함.");
        for (int i = 0; i < ExpectedBlocks; i++)
            if (Blocks[i].name != blockOrder[i] || refs.blocks[i].name != blockOrder[i] || refs.blocks[i].transform.parent != gimmickRoot)
                throw new System.Exception($"[S2_Builder] blocks[{i}] '{refs.blocks[i].name}' — 계약 C1-1 순서는 {blockOrder[i]}(부모 {GimmickRootName}).");
        if (g.GetComponentsInChildren<SnapBlock>(true).Length != ExpectedBlocks)
            throw new System.Exception($"[S2_Builder] generated 아래 SnapBlock {g.GetComponentsInChildren<SnapBlock>(true).Length}개 ≠ {ExpectedBlocks}.");

        // ── 문: generated 아래 doorPhysics는 DOOR_S2_ExitS4 1개, 모든 leverHead = null (계약 C1-5), 문틀 0 ──
        doorPhysics[] doors = g.GetComponentsInChildren<doorPhysics>(true);
        if (doors.Length != 1 || doors[0] != refs.exitS4Door || refs.exitS4Door.name != ExitS4DoorName)
            throw new System.Exception($"[S2_Builder] generated 아래 doorPhysics {doors.Length}개 — {ExitS4DoorName} 1개뿐이어야 한다(판정 1·2).");
        foreach (doorPhysics dp in doors)
            if (dp.leverHead != null) throw new System.Exception($"[S2_Builder] {dp.name}.leverHead = '{dp.leverHead.name}' — null이어야 한다(계약 C1-5).");
        for (int i = 0; i < g.childCount; i++)
            if (g.GetChild(i).name == DoorFrameName) throw new System.Exception($"[S2_Builder] {DoorFrameName}가 남았다(계약 C2-2 — 삭제 대상).");

        // ── 씬 루트 잔류 0 (1차 C3 · 계약 C1-0) ──
        List<string> stray = new List<string>();
        foreach (GameObject r in g.gameObject.scene.GetRootGameObjects())
            if (!rootsBeforeBuild.Contains(r)) stray.Add(r.name);
        if (stray.Count > 0) throw new System.Exception($"[S2_Builder] 팀 메뉴 생성물이 씬 루트에 남았다: {string.Join(", ", stray)}.");

        // ── Refs 치수 ↔ 좌표표 대조 [계산]: 외벽 윗면 · 격리 공간(옆벽 안쪽 면·천장 아랫면·숨은 벽 슬롯 중심) · S4행 개구 중심 ──
        Box wallW = FindBox("GEO_S2_Wall_W"), wallE = FindBox("GEO_S2_Wall_E");
        Box isoS = FindBox("GEO_S2_Wall_Iso_S"), isoN = FindBox("GEO_S2_Wall_Iso_N"), ceilIso = FindBox("GEO_S2_Ceil_Iso");
        Box hidden = FindBox(IsoHiddenWallName);
        Box vestS = FindBox("GEO_S2_Wall_Vest_W_S"), vestN = FindBox("GEO_S2_Wall_Vest_W_N"), vestLintel = FindBox("GEO_S2_Wall_Vest_Lintel");
        Check("outerWallTop(서벽)", wallW.max.y, OuterWallTop);
        Check("outerWallTop(동벽)", wallE.max.y, OuterWallTop);
        Check("isoLineX", (hidden.min.x + hidden.max.x) * 0.5f, IsoLineX);
        Check("isoSpace.min.x(서벽 안쪽 면)", wallW.max.x, IsoSpaceMin.x);
        Check("isoSpace.max.x(숨은 벽 선)", (hidden.min.x + hidden.max.x) * 0.5f, IsoSpaceMax.x);
        Check("isoSpace.min.y(바닥 윗면)", FindBox("GEO_S2_Floor_Room").max.y, IsoSpaceMin.y);
        Check("isoSpace.max.y(천장 아랫면)", ceilIso.min.y, IsoSpaceMax.y);
        Check("isoSpace.min.z(남쪽 옆벽 안쪽 면)", isoS.max.z, IsoSpaceMin.z);
        Check("isoSpace.max.z(북쪽 옆벽 안쪽 면)", isoN.min.z, IsoSpaceMax.z);
        Check("exitS4DoorCenter.x", (vestLintel.min.x + vestLintel.max.x) * 0.5f, ExitS4DoorCenter.x);
        Check("exitS4DoorCenter.y", vestLintel.min.y * 0.5f, ExitS4DoorCenter.y);
        Check("exitS4DoorCenter.z", (vestS.max.z + vestN.min.z) * 0.5f, ExitS4DoorCenter.z);
        // 문판은 개구(z 75~79) 양쪽으로 0.5씩 걸치고 방 쪽 벽면(x 29.5)에 붙는다 [초안 §1-4]
        Check("문판 x max = 전실 서벽 방 쪽 면", ExitS4DoorMax.x, vestS.min.x);
    }

    /// <summary>[2차판정 10 — R3] 받침대 ↔ 팀 lever_pivot·lever_head: pivot 로컬 yaw −maxAngle~+maxAngle(1° 간격)마다 관통 0, 틈 0(≤0.02) 또는
    /// ≥1.0을 부모 로컬(스케일 1)에서 잰다. 팀 Transform은 읽기만 한다 — 쓸림 자세는 행렬로 만들고 pivot에 대입하지 않는다.
    /// pivot·head는 y축 회전뿐이라 y 띠는 자세와 무관하고, xz 투영(평행사변형)이 받침대 발자국과 겹치면 3D 틈 = 수직 틈이다.
    /// xz가 떨어지는 자세가 있으면 이 가정 밖이므로 throw(현 배치에서는 pivot이 받침대 발자국 안 — [계산] 겹침 최소 0.2156).</summary>
    private static void ValidateLeverClearance(S2_Refs refs)
    {
        for (int i = 0; i < ExpectedLevers; i++)
        {
            Transform root = refs.leverRoots[i];
            LeverHead lh = refs.levers[i];
            Transform pivot = lh.leverPivot, head = lh.transform;
            Transform body = root.Find(LeverOrder[i] + BodySuffix);
            if (pivot == null || pivot.parent != root || head.parent != pivot || body == null || body.parent != root)
                throw new System.Exception($"[S2_Builder] {root.name}: pivot·head·받침대 계층이 예상(부모 → lever_pivot → lever_head, 부모 → _Body)과 다르다.");
            if (Quaternion.Angle(body.localRotation, Quaternion.identity) > 0.01f)
                throw new System.Exception($"[S2_Builder] {root.name}{BodySuffix} 로컬 회전 ≠ 0.");
            Vector3 bMin = body.localPosition - body.localScale * 0.5f, bMax = body.localPosition + body.localScale * 0.5f;
            Check($"{root.name}{BodySuffix} 아랫면(바닥 윗면 y0, 섹터 로컬)", g.InverseTransformPoint(body.TransformPoint(new Vector3(0f, -0.5f, 0f))).y, FloorHeight);

            Matrix4x4 headLocal = Matrix4x4.TRS(head.localPosition, head.localRotation, head.localScale);
            int sweep = Mathf.RoundToInt(lh.maxAngle);
            for (int a = -sweep; a <= sweep; a++)
            {
                Matrix4x4 pv = Matrix4x4.TRS(pivot.localPosition, Quaternion.Euler(0f, a, 0f), pivot.localScale);
                CheckLeverPart(root.name, pivot.name, a, pv, bMin, bMax);
                CheckLeverPart(root.name, head.name, a, pv * headLocal, bMin, bMax);
            }
        }
    }

    /// <summary>단위 상자(±0.5)를 m으로 옮긴 부품 ↔ 받침대 AABB(bMin~bMax, 같은 부모 로컬). 관통·틈 규칙 위반이면 throw.</summary>
    private static void CheckLeverPart(string lever, string part, int angle, Matrix4x4 m, Vector3 bMin, Vector3 bMax)
    {
        float yMin = float.MaxValue, yMax = float.MinValue;
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float y = m.MultiplyPoint3x4(new Vector3(sx * 0.5f, sy * 0.5f, sz * 0.5f)).y;
                    yMin = Mathf.Min(yMin, y);
                    yMax = Mathf.Max(yMax, y);
                }
        Vector2[] quad =
        {
            XZ(m.MultiplyPoint3x4(new Vector3(-0.5f, -0.5f, -0.5f))), XZ(m.MultiplyPoint3x4(new Vector3(0.5f, -0.5f, -0.5f))),
            XZ(m.MultiplyPoint3x4(new Vector3(0.5f, -0.5f, 0.5f))), XZ(m.MultiplyPoint3x4(new Vector3(-0.5f, -0.5f, 0.5f))),
        };
        Vector2[] rect = { new Vector2(bMin.x, bMin.z), new Vector2(bMax.x, bMin.z), new Vector2(bMax.x, bMax.z), new Vector2(bMin.x, bMax.z) };
        float xz = SatOverlap2D(quad, rect);
        float yOverlap = Mathf.Min(yMax, bMax.y) - Mathf.Max(yMin, bMin.y);
        string at = $"[S2_Builder] {lever}: {part} ↔ {lever}{BodySuffix} (pivot 로컬 yaw {angle}°)";
        if (xz > PenetrationTolerance && yOverlap > PenetrationTolerance)
            throw new System.Exception($"{at} 관통 — y 겹침 {yOverlap:0.#####}·xz 겹침 {xz:0.####} [2차판정 10: 관통 0].");
        if (xz <= 0f)
            throw new System.Exception($"{at} xz 투영이 받침대 발자국 밖(간격 {-xz:0.####}) — 수직 틈 검사 가정 밖, 배치 재확인.");
        float gap = Mathf.Max(0f, yMin - bMax.y, bMin.y - yMax);
        if (gap > TouchTolerance && gap < TrapGapMin)
            throw new System.Exception($"{at} 틈 {gap:0.#####} — 0(≤{TouchTolerance}) 또는 ≥{TrapGapMin}이어야 한다 [2차판정 10].");
    }

    private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);

    /// <summary>2D 볼록 다각형 분리축 검사 — 모든 변 법선 축에서 투영 겹침 폭의 최솟값(≤0이면 떨어짐, −값은 그 축의 간격).</summary>
    private static float SatOverlap2D(Vector2[] a, Vector2[] b)
    {
        float best = float.MaxValue;
        for (int pass = 0; pass < 2; pass++)
        {
            Vector2[] poly = pass == 0 ? a : b;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 e = poly[(i + 1) % poly.Length] - poly[i];
                if (e.sqrMagnitude < 1e-12f) continue;
                Vector2 n = new Vector2(-e.y, e.x).normalized;
                float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                foreach (Vector2 p in a) { float d = Vector2.Dot(n, p); aMin = Mathf.Min(aMin, d); aMax = Mathf.Max(aMax, d); }
                foreach (Vector2 p in b) { float d = Vector2.Dot(n, p); bMin = Mathf.Min(bMin, d); bMax = Mathf.Max(bMax, d); }
                best = Mathf.Min(best, Mathf.Min(aMax, bMax) - Mathf.Max(aMin, bMin));
            }
        }
        return best;
    }

    /// <summary>[2차판정 10 — R3 C3g] 정적 솔리드 경계 상자(지형 상자 47 + 마찰 0 조각 14 + 받침대 3, 섹터 로컬) 모든 쌍: 3D 간격
    /// 0.02 < g < 1.0(일반 끼임, 높이 제한 없음 — 초안 C3g와 같은 기준)이면 throw, 세 축 모두 0.001 넘게 겹치면(관통) throw.
    /// UnresolvedGapPairs만 LogWarning(판정 대기 — reviewRequests). 경계 상자 간격은 실제 간격의 하한이다.</summary>
    private static void ValidateStaticGaps(S2_Refs refs)
    {
        List<Box> solids = new List<Box>(TerrainBoxes);
        foreach (Piece p in NoFrictionPieces)
        {
            Vector3 mn = p.verts[0], mx = p.verts[0];
            foreach (Vector3 v in p.verts) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
            solids.Add(new Box(p.name, mn, mx));
        }
        for (int i = 0; i < ExpectedLevers; i++)
        {
            Transform body = refs.leverRoots[i].Find(LeverOrder[i] + BodySuffix);
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 c = g.InverseTransformPoint(body.TransformPoint(new Vector3(sx * 0.5f, sy * 0.5f, sz * 0.5f)));
                        mn = Vector3.Min(mn, c);
                        mx = Vector3.Max(mx, c);
                    }
            solids.Add(new Box(body.name, mn, mx));
        }

        List<string> trap = new List<string>(), pen = new List<string>();
        for (int i = 0; i < solids.Count; i++)
            for (int j = i + 1; j < solids.Count; j++)
            {
                Box a = solids[i], b = solids[j];
                float sq = 0f, minOverlap = float.MaxValue;
                for (int k = 0; k < 3; k++)
                {
                    float sep = Mathf.Max(a.min[k] - b.max[k], b.min[k] - a.max[k]);
                    if (sep > 0f) sq += sep * sep;
                    minOverlap = Mathf.Min(minOverlap, -sep);
                }
                float gap = Mathf.Sqrt(sq);
                if (minOverlap > StaticOverlapTolerance) pen.Add($"{a.name}↔{b.name} {minOverlap:0.####}");
                else if (gap > TouchTolerance && gap < TrapGapMin)
                {
                    if (IsUnresolvedPair(a.name, b.name))
                        Debug.LogWarning($"[S2_Builder] 일반 끼임 미해소(판정 대기 — 지형만으로 풀 수 없음, S2-B3 reviewRequests): {a.name}↔{b.name} 경계 상자 간격 {gap:0.####}.");
                    else trap.Add($"{a.name}↔{b.name} {gap:0.####}");
                }
            }
        if (pen.Count > 0) throw new System.Exception($"[S2_Builder] 정적 솔리드 관통(>{StaticOverlapTolerance}) {pen.Count}쌍: {string.Join(", ", pen)}.");
        if (trap.Count > 0) throw new System.Exception($"[S2_Builder] 일반 끼임 {TouchTolerance}<g<{TrapGapMin} {trap.Count}쌍 [2차판정 10 — C3g]: {string.Join(", ", trap)}.");
    }

    private static bool IsUnresolvedPair(string a, string b)
    {
        foreach (string[] p in UnresolvedGapPairs)
            if ((p[0] == a && p[1] == b) || (p[0] == b && p[1] == a)) return true;
        return false;
    }

    /// <summary>계약 C1-1 blocks 순서 이름표: Pile1(B0~B3, T0~T3) → Pile2 → Pile3 → Scatter_00~14.</summary>
    private static List<string> ExpectedBlockOrder()
    {
        List<string> names = new List<string>();
        for (int p = 1; p <= 3; p++)
        {
            for (int i = 0; i < 4; i++) names.Add($"S2_Block_Pile{p}_B{i}");
            for (int i = 0; i < 4; i++) names.Add($"S2_Block_Pile{p}_T{i}");
        }
        for (int i = 0; i < 15; i++) names.Add($"S2_Block_Scatter_{i:00}");
        return names;
    }

    private static Box FindBox(string name)
    {
        foreach (Box b in TerrainBoxes)
            if (b.name == name) return b;
        throw new System.Exception($"[S2_Builder] 좌표표에 {name}이(가) 없다.");
    }

    private static void Check(string what, float actual, float expected)
    {
        if (Mathf.Abs(actual - expected) > ConsistencyTolerance)
            throw new System.Exception($"[S2_Builder] {what} = {actual} — 기대값 {expected}과 ±{ConsistencyTolerance} 밖이다.");
    }

    // ───────────────────────── 점검 ─────────────────────────

    /// <summary>씬 루트에 Generated 밖 오브젝트가 남았는지 확인(S5_Builder와 같은 점검) — 남으면 재생성 때 중복된다.</summary>
    private static void ReportStrayRoots()
    {
        GameObject sectorRoot = g.root.gameObject;
        foreach (GameObject root in g.gameObject.scene.GetRootGameObjects())
            if (root != sectorRoot)
                Debug.LogError($"[S2_Builder] 씬 루트에 남은 오브젝트 '{root.name}' — Generated 아래로 옮기지 못했다(재생성 시 중복).");
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    private static Bounds MinMax(Vector3 min, Vector3 max)
    {
        Bounds b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    /// <summary>Map4Build.Floor/Wall이 돌려준 상자에 S2 이름을 붙이고 시각물 자식 "Visual"을 VIS_ 접두사로 바꾼다(Map4Build 수정 없음).
    /// 생성이 거부돼 null이면 즉시 중단(부분 지형 방지 — Build가 Generated를 비운다).</summary>
    private static Transform Solid(Transform t, string name)
    {
        if (t == null) throw new System.Exception($"[S2_Builder] 지형 생성 실패: {name} — 위 Map4Build 로그 확인.");
        t.name = name;
        Transform vis = t.Find("Visual");
        if (vis != null) vis.name = "VIS_" + name.Substring("GEO_".Length);
        return t;
    }

    private static Transform NewGroup(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(g, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go.transform;
    }

    /// <summary>섹터 로컬 위치·회전으로 놓는다(Generated의 세계 변환을 곱한다 — S5_Builder.Place 방식, 계약 C1-0).</summary>
    private static void Place(Transform t, Vector3 local, Quaternion localRot) =>
        t.SetPositionAndRotation(g.TransformPoint(local), g.rotation * localRot);

    /// <summary>팀 생성 메뉴(private) 실행 — S5_Builder.MenuCreate 방식(생성물이 선택된다, 배치 모드 대비 실행 전후 루트 비교 대안).
    /// 찾은 오브젝트는 곧바로 S2_Gimmicks 아래로 옮기고, 그 밖의 새 루트가 남으면 throw(씬 루트 잔류 0 — 1차 C3). 못 찾으면 throw.</summary>
    private static T MenuCreate<T>(string path) where T : Component
    {
        Scene active = SceneManager.GetActiveScene();
        HashSet<GameObject> before = new HashSet<GameObject>(active.GetRootGameObjects());
        Selection.activeGameObject = null;
        if (!EditorApplication.ExecuteMenuItem(path)) throw new System.Exception($"[S2_Builder] 메뉴 실행 실패: {path}");

        GameObject go = Selection.activeGameObject;
        if (go == null || go.GetComponent<T>() == null || before.Contains(go))
        {
            go = null;
            foreach (GameObject root in active.GetRootGameObjects())
                if (!before.Contains(root) && root.GetComponent<T>() != null) { go = root; break; }
        }
        if (go == null) throw new System.Exception($"[S2_Builder] '{path}'가 만든 {typeof(T).Name} 오브젝트를 찾지 못했다.");
        go.transform.SetParent(gimmickRoot, true);

        foreach (GameObject root in active.GetRootGameObjects())
            if (!before.Contains(root)) throw new System.Exception($"[S2_Builder] '{path}'가 예상 밖 루트 '{root.name}'를 남겼다.");
        return go.GetComponent<T>();
    }

    /// <summary>씬의 모든 doorPhysics.leverHead 기록(Create Lever 자동 연결 되돌리기용 — 팀 FindNearestDoor와 같은 FindObjectsOfType 범위).</summary>
    private static Dictionary<doorPhysics, LeverHead> SnapshotDoorLevers()
    {
        Dictionary<doorPhysics, LeverHead> map = new Dictionary<doorPhysics, LeverHead>();
        foreach (doorPhysics d in Object.FindObjectsOfType<doorPhysics>()) map[d] = d.leverHead;
        return map;
    }

    /// <summary>기록한 leverHead로 되돌린다(메뉴가 바꾼 것만 경고와 함께 복원).</summary>
    private static void RestoreDoorLevers(Dictionary<doorPhysics, LeverHead> map)
    {
        foreach (KeyValuePair<doorPhysics, LeverHead> kv in map)
        {
            if (kv.Key == null || kv.Key.leverHead == kv.Value) continue;
            Debug.LogWarning($"[S2_Builder] Create Lever가 문 '{kv.Key.name}'의 leverHead를 바꿨다 — 원래 값으로 되돌린다(초안 §3 주).");
            kv.Key.leverHead = kv.Value;
        }
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}

// ───────────────────────── S2_Refs (R2 계약 C1-2, 수정 라운드 M2R1-컴파일) ─────────────────────────
// 계약 C1-0·C6은 Editor/Sectors/S2_Refs.cs를 S2-B 소유로 적었지만 이 과제 owns는 S2_Builder.cs 하나다. 컴파일을 살리려고
// 계약 C1-2 코드 블록의 클래스 본문을 스크립트로 글자 그대로 옮겨 이 파일에 둔다(재타이핑 없음, 필드 추가·변경 없음).
// 나중에 S2_Refs.cs를 따로 만들면 이 블록을 지워야 한다(CS0101 중복 정의) — 보고서 S2-B.md reviewRequests.

/// <summary>S2 빌더 → S2_Wiring/S2_Dress 전달 묶음(R2 계약 C1-2). 좌표 섹터 로컬.</summary>
public sealed class S2_Refs
{
    public Transform generated;
    public Transform gimmickRoot;                 // generated/S2_Gimmicks
    // 치수 [초안 §1·§2]
    public float roomXMin = -38f, roomXMax = 38f, roomZMax = 84f;
    public float outerWallTop = 9f;               // [제안] 외벽 높이
    public float isoLineX = -14f;                 // 숨은 벽 선 [초안 §0]
    public Bounds isoSpace;                       // 격리 공간 안쪽 min(-38,0,30.5) max(-14,4,53.5)
    public Vector3 exitS4DoorCenter;              // S4행 문 개구 중심(초안 §1-4)
    // 지형·구조물
    public Transform isoHiddenWall;               // GEO_S2_Wall_IsoHidden — 움직이지 않는 벽(수납함 안, 판정 2)
    public Transform exitS3Opening;               // TEMP_S2_ExitS3_Open — 빈 GO, 북벽 개구 중심 (0,0,84)(판정 1)
    public doorPhysics exitS4Door;                // DOOR_S2_ExitS4 (Map4Build.Door)
    public PhysicMaterial noFriction;             // PM_Map4_NoFriction
    public List<Transform> noFrictionSolids = new List<Transform>(); // GEO_S2_Hump_* + GEO_S2_Wedge_*
    public List<Transform> obstacles = new List<Transform>();        // GEO_S2_Obst_* 11
    public List<Transform> walls = new List<Transform>();            // GEO_S2_Wall_* 전부
    public List<Transform> ceilings = new List<Transform>();         // GEO_S2_Ceil_* 전부
    // 팀 기믹
    public List<Transform> leverRoots = new List<Transform>();       // S2_Lever1_Trap, S2_Lever2, S2_Lever3
    public List<LeverHead> levers = new List<LeverHead>();           // leverRoots와 같은 순서
    public List<SnapBlock> blocks = new List<SnapBlock>();           // 39
}
#endif
