#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F1-3/F1-6 정적 검사 — 팀 KitchenMapV3/Editor/V3_Audit.cs의 ComputePenetration 패턴을 이식한
/// 축소판(로직 복제가 아니라 같은 설계 원칙 재사용 — 크로스그룹 정적 솔리드↔솔리드 관통만 본다,
/// 공통규칙 §3 ①에 대응). 도형 스폰 겹침(F1-6 항목4)은 Map4Director.SpawnPlayersAt +
/// Map4PlayTestRunner가 플레이 모드에서 별도로 검사한다.
///
/// [F1 재작업 판정 R2 — 그룹 정의 수정] 이전 버전은 `a.transform.root == b.transform.root`로
/// "같은 그룹"을 판정했다 — 그런데 우리 섹터 구조에서는 한 섹터의 GEO_Floor·GEO_Wall이 전부
/// 같은 씬 루트(S{n}_Root) 아래에 있어, **섹터 내부의 모든 콜라이더 쌍이 무조건 제외**되고
/// 실제로는 섹터 간(12U 이격, 자명하게 안 겹침)만 검사하는 구조적 동어반복이었다("정적 콜라이더
/// 24개 · 관통 0건"이 아무것도 증명하지 못했다). 이제 "같은 GameObject"(예: 문의 솔리드+트리거
/// 콜라이더 — 트리거는 위에서 이미 걸러지므로 사실상 해당 없음)만 제외하고, 그 외 모든 쌍을
/// 검사한다. RunNegativeControlTest()가 일부러 겹치는 박스 2개(같은 씬 루트 아래)를 만들어 이
/// 수정이 실제로 검출하는지 증명한다.
///
/// [INF3 — 끼임 틈 검사 추가] 관통 판정(아래 RunStaticAudit의 첫 루프, dist ≥ 0.10)은 한 글자도
/// 바꾸지 않았다. 그 뒤에 AnalyzeGaps()가 같은 정적 솔리드 목록으로 "같은 섹터(=같은 씬) 콜라이더
/// 쌍의 최단 거리 g"를 재서 두 목록을 만든다(보고용 — exit code에 반영하지 않는다):
///   ① 일반 끼임 틈: ContactEpsilon < g < 1.0U, 그리고 틈이 걸을 수 있는 윗면 위에서 위로 열려 있을 때만
///      [확정: 00_기반_설계 F1-3 "끼임 틈 0 < 틈 < 1.0U"] (하한 ContactEpsilon은 아래 상수 주석 — 해석).
///   ② S1 장애물 규칙: 이름이 GEO_S1_Obst_ 인 것끼리, GEO_S1_Obst_ ↔ GEO_S1_Wall_ 쌍에서
///      ContactEpsilon < g < 2.5 → 위반 [제안→§11 사용자 확정: S1_설계 §11-3 "0(붙임) 또는 2.5 이상만 허용",
///      이름 규약은 [지시서] INF3 지시서 계약 C4]. 이 목록은 위로 열림 필터를 적용하지 않고(이름으로 이미
///      바닥에 선 장애물·벽만 고른다) 필터 결과를 꼬리표로만 붙인다 — 치명 여부는 검토 에이전트 몫.
///   ③ [INF3-2] S2 장애물 규칙: 이름이 GEO_S2_Obst_ 인 것끼리, GEO_S2_Obst_ ↔ GEO_S2_Wall_ 쌍에서
///      ContactEpsilon &lt; g &lt; 2.5 → 위반 [제안: S2_설계 §3(절 머리 [제안]) 본문 "고정 장애물 사이 틈은 0 또는
///      2.5 이상이다(S1과 같은 규칙)" — R2 계약 C2-1은 "[확정 S2 §3]"로 적음, 등급은 컨트롤타워 판정 대기;
///      이름 규약은 [계약] R2 계약 C2-1·C2-2]. ②와 같은 방식(열림 필터 없이 꼬리표만 — 1차 판정 4(b)).
///      S1·S2 접두사는 서로 섞지 않는다 — 예: GEO_S1_Obst_ ↔ GEO_S2_Wall_은 어느 규칙 대상도 아니다(일반 끼임만).
/// 필수 경로 여부는 자동 판정하지 않는다. RunGapNegativeControlTest()가 0.5U 틈을 실제로 잡는지 증명한다.
/// [INF3 R1] 위로 열림 필터는 정적 솔리드(쌍 목록과 같은 집합)만 메움재·덮개로 인정한다 — 가동체가 관여한
/// 틈은 제외하지 않고 "가동체 메움/덮음/바닥" 꼬리표로 따로 보고한다(ClassifyGapOpening 주석).
///
/// [INF3-3 — R3 추가 규칙, 전부 보고용(exit code 무관 — S1·S2와 같음 [해석], 컨트롤타워 판정 대기)]
///   ④ S8 구조물 규칙 [계약 R2-C2-1 S8 행 · 확정 S8_설계 §3-8]: GEO_S8_Shelf_·GEO_S8_Table_ 서로, 그리고 이 둘 ↔ GEO_S8_Wall_ 쌍에서
///      ContactEpsilon &lt; g &lt; 2.5 → 위반. ②·③과 같은 방식(열림 필터 없이 꼬리표만). 다른 섹터 접두사와 섞지 않는다.
///   ⑤ S3 필수 경로 규칙 [2차판정 17 · 계약 C2-1 S3 행]: 초안 §4-1 순서표(S3RequiredPathOrder, 132행)의 GEO↔GEO 직접 이웃 쌍마다
///      틈(월드 AABB의 XZ 사각형 사이 거리) &gt; 1.5+0.02 또는 |턱(윗면 차)| &gt; 0.8+0.02 → 위반(EvaluateS3Path — 순수 함수).
///   ⑥ S4 원통 볼록 조각 이음매 [2차판정 15·16 · 계약 C2-1 S4 행]: GEO_S4_Wall_Cyl_{nn} 번호순 k↔(k+1) mod N(마지막↔00 포함)의
///      틈 &gt; 0.02 또는 겹침(ComputePenetration dist) &gt; 0.02 → 위반.
/// 관통 루프·문턱(dist ≥ 0.10)·일반 끼임 판정은 바꾸지 않았다. 관통 ≥1이면 exit 1은 Map4Batch.Audit이 한다 [2차판정 17].
/// </summary>
public static class Map4Audit
{
    // ── [INF3] 끼임 틈 검사 수치 ────────────────────────────────────────
    /// <summary>[확정] 00_기반_설계 F1-3 — 0 &lt; g &lt; 1.0U(도형 최소 폭)는 끼임 틈.</summary>
    public const float PinchGapMax = 1.0f;
    /// <summary>[제안→§11 사용자 확정] S1_설계 §11-3(절 표기 [제안], 상위 §11 "09-28 사용자 확정") — S1 장애물끼리·장애물↔벽 틈은 0 또는 ≥ 2.5만 허용.</summary>
    public const float S1ObstacleGapMin = 2.5f;
    /// <summary>[해석 — 판정 11 확장, 컨트롤타워 판정 대기] F1 재작업 판정 11의 "틈 0(허용 오차 0.02U)"은 경사로 끝 규칙인데
    /// 일반 끼임 검사의 맞닿음 허용 오차로 넓혀 쓴다. S1 규칙 쪽은 INF3 지시서 계약 C4 "±0.02 허용"이 직접 근거.</summary>
    public const float ContactEpsilon = 0.02f;
    /// <summary>[지시서] INF3 지시서 — "걸을 수 있는 윗면" = 법선 y ≥ 0.5.</summary>
    public const float WalkableNormalY = 0.5f;
    /// <summary>[확정] 00_기반_설계 F1-3 도형 최소 폭 1.0U — 틈 바로 위 1U 안에 막는 면이 있으면 도형이 위에서
    /// 틈으로 떨어져 들어올 공간이 없으므로 "위로 열려 있지 않음"(덮인 틈·벽 속·바닥 아래)으로 제외한다.</summary>
    public const float OpenAboveClearance = 1.0f;
    /// <summary>[추정] 틈 아래로 걸을 면을 찾는 최대 거리 — 섹터 높이(수십 U) 안이면 충분하다고 본 값.</summary>
    public const float MaxDownProbe = 100f;
    public const string S1ObstaclePrefix = "GEO_S1_Obst_"; // [지시서] INF3 지시서 계약 C4
    public const string S1WallPrefix = "GEO_S1_Wall_";     // [지시서] INF3 지시서 계약 C4
    // ── [INF3-2] S2 고정 장애물 규칙 ─────────────────────────────────────
    /// <summary>[제안] S2_설계 §3(절 머리 [제안 — 크기는 여유 있게]) 본문 "고정 장애물 사이 틈은 0 또는 2.5 이상이다
    /// (S1과 같은 규칙)" — S2 장애물끼리·장애물↔벽 틈은 0 또는 ≥ 2.5만 허용. R2 계약 C2-1은 "[확정 S2 §3]"로 적었으나
    /// 설계서 절 등급은 [제안]이다(컨트롤타워 판정 대기 — 보고서 INF3-2.md). 값은 S1ObstacleGapMin과 같지만 근거가 달라
    /// 따로 둔다.</summary>
    public const float S2ObstacleGapMin = 2.5f;
    public const string S2ObstaclePrefix = "GEO_S2_Obst_"; // [계약] R2 계약 C2-1·C2-2(초안 GEO_S2_Clutter_* → Obst)
    public const string S2WallPrefix = "GEO_S2_Wall_";     // [계약] R2 계약 C2-1·C2-2(정적 벽 전부 Wall)
    // ── [INF3-3] S8 구조물 규칙 ─────────────────────────────────────────
    /// <summary>[확정] S8_설계 §3-8(09-28) "구조물 사이 틈은 0 또는 2.5 이상(끼임 규칙)" · [계약 R2-C2-1 S8 행] 0.02 &lt; g &lt; 2.5 위반.</summary>
    public const float S8StructureGapMin = 2.5f;
    public const string S8ShelfPrefix = "GEO_S8_Shelf_";   // [계약 R2-C2-1 S8 행] · 스테이징 S8_Builder.cs:134-141·398과 일치
    public const string S8TablePrefix = "GEO_S8_Table_";   // [계약 R2-C2-1 S8 행] · 스테이징 S8_Builder.cs:418과 일치
    public const string S8WallPrefix = "GEO_S8_Wall_";     // [계약 R2-C2-1 S8 행]
    // ── [INF3-3] S3 필수 경로 규칙 ──────────────────────────────────────
    /// <summary>[계약 R2-C2-1 S3 행 · 00_기반 F1 판정 11 · 2차판정 17] 필수 경로 이웃 틈 ≤ 1.5(위반 = 1.5 + 0.02 초과).</summary>
    public const float S3PathGapMax = 1.5f;
    /// <summary>[계약 R2-C2-1 S3 행] 필수 경로 이웃 턱 ≤ 0.8(오름·내림 모두 — 초안 §4-1 턱은 부호 있음, 위반 = |턱| &gt; 0.8 + 0.02) [해석].</summary>
    public const float S3PathStepMax = 0.8f;
    public const string S3SceneName = "Map4_S3";          // [팀 코드] Map4SceneBuilder.cs:128 $"Map4_S{def.id}"
    public const string S3BridgePrefix = "S3_Bridge_";     // [계약 R2-C2-2] 줄다리 행 — GEO 아님(판정 제외, 순서 유지용)
    // ── [INF3-3] S4 원통 볼록 조각 이음매 ───────────────────────────────
    /// <summary>[2차판정 15 · 계약 C2-1 S4 행] 이음매 틈 0·겹침 ≤ 0.02 — 틈 &gt; 0.02 또는 겹침 &gt; 0.02 → 위반.</summary>
    public const float S4SeamTolerance = 0.02f;
    /// <summary>[2차판정 16 · 계약 C2-4] 48각 7.5° — 개수 판정에는 쓰지 않고(쌍은 실제 개수 N으로 만든다) 다를 때 경고 줄만.</summary>
    public const int S4ExpectedCylPieces = 48;
    public const string S4LegacyCylName = "GEO_S4_Wall_Cyl"; // [계약 C2-4] (가) 비볼록 1개 — 폐기 대상(남아 있으면 "S4-B (나) 미반영")
    private static readonly System.Text.RegularExpressions.Regex S4CylPieceRegex =
        new System.Text.RegularExpressions.Regex("^GEO_S4_Wall_Cyl_([0-9]{2})$"); // [계약 C2-4] GEO_S4_Wall_Cyl_{nn}
    /// <summary>[팀 코드] Map4SceneBuilder.OpenAllSectors(:645-647) — Master + Map4_S1..S8 가산 로드. 감사 대상 섹터 1~8 [계약 R2-C4].</summary>
    public const int ExpectedSectorCount = 8;

    /// <summary>[INF3-3] S3 필수 경로 순서 — 초안 §4-1 표 '계약 이름(C2-3)' 열 132행을 스크립트로 추출(재타이핑 없음)
    /// [초안 §4-1:596-727 · `진행/초안/S3_배치초안.md`]. GEO 106 + 줄다리(S3_Bridge_*) 26. 줄다리 행은 순서 유지용이다.
    /// 한계: 초안 고정 사본이다 — S3_Builder가 이름·순서를 바꾸면 불일치할 수 있다("이름 없음" 목록으로 드러난다).</summary>
    public static readonly string[] S3RequiredPathOrder =
    {
        "GEO_S3_Path_P_start", "GEO_S3_Path_L1_00", "GEO_S3_Path_L1_01", "GEO_S3_Path_L1_02",
        "GEO_S3_Path_L1_03", "GEO_S3_Path_T1_b1", "GEO_S3_Path_T1_b2", "GEO_S3_Path_L2_00",
        "GEO_S3_Path_L2_StairsUp_Step0", "GEO_S3_Path_L2_StairsUp_Step1", "GEO_S3_Path_L2_03", "GEO_S3_Path_L2_04",
        "GEO_S3_Path_L2_StairsDown_Step1", "GEO_S3_Path_L2_StairsDown_Step0", "GEO_S3_Path_L2_07", "GEO_S3_Path_L2_08",
        "GEO_S3_Path_L2_09", "GEO_S3_Path_L2_10", "GEO_S3_Path_T2_b1", "GEO_S3_Path_T2_b2",
        "GEO_S3_Path_L3_00", "GEO_S3_Path_L3_01", "GEO_S3_Path_L3_02", "GEO_S3_Path_L3_03",
        "GEO_S3_Path_L3_04", "GEO_S3_Path_L3_05", "GEO_S3_Path_L3_06", "GEO_S3_Path_L3_07",
        "GEO_S3_Path_L3_08", "GEO_S3_Path_L3_09", "GEO_S3_Path_L3_10", "GEO_S3_Path_L3_11",
        "GEO_S3_Path_L3_12", "GEO_S3_Path_T3_b1", "GEO_S3_Path_T3_b2", "GEO_S3_Path_L4_00",
        "GEO_S3_Path_L4_01", "GEO_S3_Path_L4_02", "GEO_S3_Path_L4_03", "GEO_S3_Path_L4_04",
        "GEO_S3_Path_L4_05", "GEO_S3_Path_L4_06", "GEO_S3_Path_L4_07", "GEO_S3_Path_L4_08",
        "GEO_S3_Path_L4_09", "GEO_S3_Path_T4_b1", "GEO_S3_Path_T4_b2", "GEO_S3_Path_L5_00",
        "GEO_S3_Path_L5_01", "GEO_S3_Path_L5_02", "GEO_S3_Path_L5_03", "GEO_S3_Path_L5_04",
        "GEO_S3_Path_L5_05", "GEO_S3_Path_L5_06", "GEO_S3_Path_L5_07", "GEO_S3_Path_L5_08",
        "GEO_S3_Path_L5_09", "GEO_S3_Path_T5_b1", "GEO_S3_Path_T5_b2", "GEO_S3_Path_L6_00",
        "GEO_S3_Path_L6_01", "GEO_S3_Path_L6_02", "GEO_S3_Path_L6_03", "GEO_S3_Path_L6_04",
        "GEO_S3_Path_L6_05", "GEO_S3_Path_L6_06", "GEO_S3_Path_L6_07", "GEO_S3_Path_L6_08",
        "GEO_S3_Path_L6_09", "GEO_S3_Path_L6_10", "GEO_S3_Path_T6_b1", "GEO_S3_Path_T6_b2",
        "GEO_S3_Path_B1_P1", "S3_Bridge_BR_B1_1", "GEO_S3_Path_B1_P2", "S3_Bridge_BR_B1_2",
        "GEO_S3_Path_B1_P3", "S3_Bridge_BR_B1_3", "GEO_S3_Path_B1_P4a", "GEO_S3_Path_B1_P4b",
        "S3_Bridge_BR_B1_turn", "GEO_S3_Path_B2_P1", "S3_Bridge_BR_B2_1", "GEO_S3_Path_B2_P2",
        "S3_Bridge_BR_B2_2", "GEO_S3_Path_B2_P3", "S3_Bridge_BR_B2_3", "GEO_S3_Path_B2_P4",
        "S3_Bridge_BR_B2_turn", "GEO_S3_Path_B3_P1", "S3_Bridge_BR_B3_1", "GEO_S3_Path_B3_P2",
        "S3_Bridge_BR_B3_2", "GEO_S3_Path_B3_P3", "S3_Bridge_BR_B3_3", "GEO_S3_Path_B3_P4a",
        "GEO_S3_Path_B3_P4b", "S3_Bridge_BR_B3_turn", "GEO_S3_Path_B4_P1", "S3_Bridge_BR_B4_1",
        "GEO_S3_Path_B4_P2", "S3_Bridge_BR_B4_2", "GEO_S3_Path_B4_P3", "S3_Bridge_BR_B4_3",
        "GEO_S3_Path_B4_P4", "S3_Bridge_BR_B4_turn", "GEO_S3_Path_B5_P1", "S3_Bridge_BR_B5_1",
        "GEO_S3_Path_B5_P2", "S3_Bridge_BR_B5_2", "GEO_S3_Path_B5_P3", "S3_Bridge_BR_B5_3",
        "GEO_S3_Path_B5_P4a", "GEO_S3_Path_B5_P4b", "S3_Bridge_BR_B5_turn", "GEO_S3_Path_B6_P1",
        "S3_Bridge_BR_B6_1", "GEO_S3_Path_B6_P2", "S3_Bridge_BR_B6_2", "GEO_S3_Path_B6_P3",
        "S3_Bridge_BR_B6_3", "GEO_S3_Path_B6_P4", "S3_Bridge_BR_B6_turn", "GEO_S3_Path_B7_P1",
        "S3_Bridge_BR_B7_1", "GEO_S3_Path_B7_P2", "S3_Bridge_BR_B7_2", "GEO_S3_Path_B7_P3",
        "GEO_S3_Path_TX_b1", "GEO_S3_Path_TX_b2", "GEO_S3_Path_TX_b3", "GEO_S3_Path_P_exit",
    };
    // [계산] 교대 투영 반복 상한 — 파이썬 무작위 회전 박스 300쌍 검산(보고서 INF3.md): 상한 200이면
    // 수렴 미완 5쌍·과대평가 최대 0.028U, 2000이면 미완 0·최대 사용 850회·과대평가 ≤ 0.00003U.
    // 맵 대부분인 축정렬 박스는 1~3회에 끝나므로 비용은 비스듬한 쌍에만 든다.
    private const int MaxProjectionIterations = 2000;
    private const float ProjectionTolerance = 1e-6f;       // [추정] 거리 감소량이 이보다 작으면 수렴으로 본다

    public const string FilterOpen = "열림";
    public const string FilterFilled = "메워짐(틈 중앙이 다른 콜라이더 속 — 벽 속)";
    public const string FilterCovered = "위 덮임(틈 위 1U 안에 면 — 바닥 아래·덮인 틈)";
    public const string FilterNoWalkable = "아래 걸을 면 없음(법선 y<0.5 또는 없음)";

    // [INF3 R1] 가동체(Rigidbody 부착 — 예: 팀 SpikeTrap은 [RequireComponent(typeof(Rigidbody))]) 꼬리표.
    // 쌍 목록이 정적 콜라이더만 쓰므로 필터도 정적 콜라이더만 "막는 것"으로 인정한다 — 편집 시점 자세의
    // 가동체가 틈을 가려 제외되는 일이 없게 한다. 가동체가 관여한 틈은 아래 꼬리표로 따로 보고한다.
    public const string TagMovingFilled = "가동체 메움";   // 틈 중앙이 가동체 속(정적 메움재 없음)
    public const string TagMovingCovered = "가동체 덮음";  // 틈 위 1U 안에 가동체만 있음
    public const string TagMovingBelow = "가동체 바닥";    // 틈 아래 첫 정적 면보다 위에 가동체가 있음

    /// <summary>한 콜라이더 쌍의 틈 측정 결과.</summary>
    public struct GapItem
    {
        public string sector;      // 씬 이름(섹터 = 씬, Map4SceneBuilder: Map4_S{n})
        public string nameA, nameB;
        public float gap;          // 최단 거리(교대 투영 — 수렴 미완이면 상한값)
        public Vector3 point;      // 틈 중앙(월드)
        public Vector3 localPoint; // 틈 중앙(A의 씬 루트 기준 로컬 — 설계서 섹터 좌표와 대조용)
        public string rootName;
        public string filter;      // FilterOpen 또는 제외 사유
        public bool converged;
        public string movingNote;  // [INF3 R1] 가동체 관여 꼬리표(없으면 null) — 예: "가동체 메움: 'SpikeTrap'"
    }

    public struct Result
    {
        public int staticColliderCount;
        public int overlapCount;
        public List<string> overlapDetails;

        // [INF3] 끼임 틈(보고용 — exit code 무관)
        public List<string> sectors;          // 검사한 섹터(씬) 이름, 정렬
        public List<GapItem> pinchGaps;       // ContactEpsilon < g < 1.0, 위로 열림 → 보고
        public List<GapItem> pinchExcluded;   // ContactEpsilon < g < 1.0 이지만 필터로 제외(참고 목록)
        public List<GapItem> s1Violations;    // S1 이름 규칙 쌍, ContactEpsilon < g < 2.5
        public List<GapItem> s2Violations;    // [INF3-2] S2 이름 규칙 쌍, ContactEpsilon < g < 2.5
        public List<string> unverifiedGapPairs; // 거리 측정 불가 형상(비볼록 메시·지형 등)이 낀 쌍
        public int gapPairsMeasured;
        public int nonConvergedPairs;
        public int movingInvolvedCount; // [INF3 R1] movingNote가 붙은 틈 수(끼임·S1위반·S2위반·S8위반·제외 목록 합, 중복 없이)

        // [INF3-3] R3 추가 규칙(전부 보고용 — exit code 무관)
        public List<GapItem> s8Violations;    // S8 구조물 규칙 쌍, ContactEpsilon < g < 2.5
        public int s8RuleTargetCount;         // GEO_S8_Shelf_·GEO_S8_Table_ 정적 콜라이더 수(0이면 통합 전일 수 있음)
        public int s8WallCount;               // GEO_S8_Wall_ 정적 콜라이더 수
        public S3PathReport s3Path;           // S3 필수 경로 순서표 규칙
        public List<SeamItem> s4Seams;        // S4 원통 조각 이음매 전부(측정 결과)
        public List<SeamItem> s4SeamViolations;
        public List<string> s4SeamNotes;      // 조각 수·번호 불연속·(가) 잔존 등 경고 줄
        public int s4CylPieceCount;           // GEO_S4_Wall_Cyl_{nn} 정적 콜라이더 수(전 섹터 합)
        public bool s4LegacyCylPresent;       // (가) 비볼록 GEO_S4_Wall_Cyl가 남아 있음
    }

    /// <summary>[INF3-3] S3 필수 경로 이웃 쌍 한 개의 판정 결과.</summary>
    public struct S3PathItem
    {
        public string nameA, nameB;   // 경로 순서 앞 → 뒤
        public float gap;             // XZ 사각형 사이 거리
        public float step;            // 윗면 차(뒤 − 앞, 부호 있음)
        public bool gapBad, stepBad;
    }

    /// <summary>[INF3-3] EvaluateS3Path 결과.</summary>
    public struct S3PathReport
    {
        public int orderCount;            // 순서표 행 수
        public int geoRowCount;           // 줄다리가 아닌 행 수
        public int directPairs;           // GEO↔GEO 직접 이웃 쌍 수(순서표상)
        public int judgedPairs;           // 그중 두 이름이 각각 정확히 1개로 찾아져 판정한 쌍
        public int unjudgedPairs;         // 이름 없음·중복 때문에 판정 못 한 쌍
        public int bridgeSkippedPairs;    // 사이에 줄다리 행이 낀 GEO↔GEO 쌍(판정 제외 — 줄다리가 메움, 초안 C11)
        public List<S3PathItem> violations;
        public List<string> missing;      // 순서표 GEO 이름인데 콜라이더 0개
        public List<string> duplicates;   // 순서표 GEO 이름인데 콜라이더 2개 이상
    }

    /// <summary>[INF3-3] S4 원통 조각 이음매 한 개의 측정 결과.</summary>
    public struct SeamItem
    {
        public string sector, nameA, nameB;
        public bool measured;        // false = 측정 불가 형상·계산 예외
        public float gap;            // 교대 투영 최단 거리(수렴 미완이면 상한값)
        public float penetration;    // ComputePenetration dist(겹치지 않으면 0)
        public bool converged;
        public bool Violates => measured && (gap > S4SeamTolerance || penetration > S4SeamTolerance);
    }

    [MenuItem("Tools/Laboratory Map4/Run Static Audit")]
    public static void RunStaticAuditMenu()
    {
        Result r = RunStaticAudit();
        Debug.Log($"[Map4Audit] 정적 콜라이더 {r.staticColliderCount}개 · 관통 {r.overlapCount}건" +
                  (r.overlapCount > 0 ? " ❌" : " ✅"));
        foreach (string d in r.overlapDetails) Debug.LogWarning(d);
        // [INF3-3 · 2차판정 17] 표기와 판정을 맞춘다 — 관통 ≥1은 실패(배치 Map4Batch.Audit에서는 exit 1).
        // 대화형 메뉴는 에디터를 종료하지 않는다(에러 로그로만 알린다).
        if (r.overlapCount >= 1)
            Debug.LogError($"[Map4Audit] 관통 {r.overlapCount}건 — 판정 17: 실패(배치 Audit이면 exit 1, 대화형 메뉴는 종료 없음)");
        Debug.Log("[Map4Audit] 끼임 틈 검사(보고용)\n" + FormatGapReport(r));
    }

    public static Result RunStaticAudit()
    {
        List<Collider> statics = new List<Collider>();
        foreach (Collider c in Object.FindObjectsOfType<Collider>())
        {
            if (c.isTrigger) continue;
            if (c.attachedRigidbody != null) continue; // 정적만(F1-3 범위 — 가동체는 Play 검사 몫).
            statics.Add(c);
        }

        List<string> details = new List<string>();
        int overlaps = 0;
        for (int i = 0; i < statics.Count; i++)
        {
            for (int j = i + 1; j < statics.Count; j++)
            {
                Collider a = statics[i], b = statics[j];
                // [R2] "같은 GameObject"만 제외한다(한 오브젝트가 콜라이더 여러 개를 가진 합법적
                // 겹침 — 예: 문의 솔리드+트리거). 씬 루트가 같다는 이유로 제외하지 않는다.
                if (a.gameObject == b.gameObject) continue;
                if (!a.bounds.Intersects(b.bounds)) continue;

                bool ok; Vector3 dir; float dist;
                try
                {
                    ok = Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                        b, b.transform.position, b.transform.rotation, out dir, out dist);
                }
                catch { continue; } // 비지원 형상 조합 — 미검증 쌍(범위 밖, 로그만).

                if (ok && dist >= 0.10f)
                {
                    overlaps++;
                    details.Add($"관통 {dist:F3}U — '{a.name}'({a.transform.root.name}) <-> " +
                                $"'{b.name}'({b.transform.root.name})");
                }
            }
        }

        Result result = new Result { staticColliderCount = statics.Count, overlapCount = overlaps, overlapDetails = details };
        AnalyzeGaps(statics, ref result); // [INF3] 관통 판정과 독립 — 위 루프·판정 기준은 그대로.
        AnalyzeS3Path(statics, ref result);  // [INF3-3] 보고용 — 관통·끼임 판정과 독립.
        AnalyzeS4Seams(statics, ref result); // [INF3-3] 보고용 — 관통·끼임 판정과 독립.
        return result;
    }

    // ── [INF3] 끼임 틈 검사 ─────────────────────────────────────────────

    private delegate Vector3 ClosestFn(Vector3 p);

    /// <summary>같은 섹터(=같은 씬) 정적 솔리드 쌍의 최단 거리를 재서 끼임 틈·S1 규칙 위반 목록을 만든다.
    /// statics는 RunStaticAudit가 이미 트리거·Rigidbody 부착 콜라이더를 뺀 목록(정적 솔리드).</summary>
    private static void AnalyzeGaps(List<Collider> statics, ref Result result)
    {
        result.sectors = new List<string>();
        result.pinchGaps = new List<GapItem>();
        result.pinchExcluded = new List<GapItem>();
        result.s1Violations = new List<GapItem>();
        result.s2Violations = new List<GapItem>();
        result.s8Violations = new List<GapItem>(); // [INF3-3]
        result.s8RuleTargetCount = 0;
        result.s8WallCount = 0;
        result.unverifiedGapPairs = new List<string>();
        result.gapPairsMeasured = 0;
        result.nonConvergedPairs = 0;
        result.movingInvolvedCount = 0;

        // [INF3 R1] 필터가 "막는 것"으로 인정하는 콜라이더 = 쌍 목록과 같은 정적 솔리드(켜진 것)만.
        HashSet<Collider> staticSet = new HashSet<Collider>();
        foreach (Collider c in statics) if (c.enabled) staticSet.Add(c);

        // 편집 모드 물리 쿼리(Raycast·OverlapSphere·ClosestPoint)가 최신 Transform을 보도록 동기화한다
        // (ProjectSettings/DynamicsManager.asset m_AutoSyncTransforms: 0 이라 자동 동기화가 꺼져 있다).
        Physics.SyncTransforms();

        // 섹터 = 씬(Map4SceneBuilder가 섹터마다 Map4_S{n}.unity를 만들고 OpenAllSectors가 가산 로드).
        SortedDictionary<string, List<Collider>> bySector = new SortedDictionary<string, List<Collider>>();
        foreach (Collider c in statics)
        {
            if (!c.enabled) continue; // 꺼진 콜라이더는 도형을 막지 않는다(틈 검사만 해당 — 관통 판정은 위 그대로).
            // [INF3-3] S8 규칙 대상 이름 수(0이면 통합 INT-2 전일 수 있다 — 보고 줄에 경고).
            if (c.name.StartsWith(S8ShelfPrefix, System.StringComparison.Ordinal) ||
                c.name.StartsWith(S8TablePrefix, System.StringComparison.Ordinal)) result.s8RuleTargetCount++;
            else if (c.name.StartsWith(S8WallPrefix, System.StringComparison.Ordinal)) result.s8WallCount++;
            string key = string.IsNullOrEmpty(c.gameObject.scene.name) ? "(이름 없는 씬)" : c.gameObject.scene.name;
            if (!bySector.TryGetValue(key, out List<Collider> list)) bySector[key] = list = new List<Collider>();
            list.Add(c);
        }

        foreach (KeyValuePair<string, List<Collider>> kv in bySector)
        {
            result.sectors.Add(kv.Key);
            List<Collider> cols = kv.Value;
            bool[] measurable = new bool[cols.Count];
            for (int i = 0; i < cols.Count; i++) measurable[i] = TryGetClosestFn(cols[i], Vector3.zero, out _);

            for (int i = 0; i < cols.Count; i++)
            {
                for (int j = i + 1; j < cols.Count; j++)
                {
                    Collider a = cols[i], b = cols[j];
                    if (a.gameObject == b.gameObject) continue; // 관통 판정과 같은 제외 기준.

                    bool s1Pair = IsS1RulePair(a.name, b.name);
                    bool s2Pair = IsS2RulePair(a.name, b.name); // [INF3-2] S1과 접두사가 달라 동시에 참일 수 없다.
                    bool s8Pair = IsS8RulePair(a.name, b.name); // [INF3-3] S1·S2와 접두사가 달라 동시에 참일 수 없다.
                    // [INF3-3] S8 쌍은 넓은 단계 문턱만 2.5로 늘린다 — g < 1.0 일반 끼임 판정(아래 PinchGapMax 비교)은 그대로다.
                    float threshold = s1Pair ? S1ObstacleGapMin : (s2Pair ? S2ObstacleGapMin : (s8Pair ? S8StructureGapMin : PinchGapMax));

                    // 넓은 단계: AABB를 각 방향 threshold만큼 늘려도 안 만나면 AABB 간격 ≥ threshold →
                    // 실제 최단 거리도 ≥ threshold(AABB가 형상을 감싸므로) — 보고 대상이 될 수 없다.
                    Bounds ea = a.bounds;
                    ea.Expand(2f * threshold);
                    if (!ea.Intersects(b.bounds)) continue;

                    if (!measurable[i] || !measurable[j])
                    {
                        result.unverifiedGapPairs.Add($"[{kv.Key}] '{a.name}'({ShapeName(a)}) <-> " +
                                                      $"'{b.name}'({ShapeName(b)}) — 거리 측정 불가 형상");
                        continue;
                    }

                    // 시작점: 두 AABB를 threshold/2씩 늘린 교집합의 중심 — 두 형상 "사이"의 점에서
                    // 출발해, 평행한 면 쌍이면 겹치는 높이 구간의 가운데를 틈 중앙으로 고르게 한다.
                    Bounds ha = a.bounds, hb = b.bounds;
                    ha.Expand(threshold); hb.Expand(threshold);
                    Vector3 start = (Vector3.Max(ha.min, hb.min) + Vector3.Min(ha.max, hb.max)) * 0.5f;

                    // 원점 이동: 교대 투영을 start 기준 상대 좌표에서 돌린다 — 월드 좌표가 크면(예: 음성 대조
                    // 5000U) float 반올림 잡음(ulp ≈ 5e-4)이 "거리 감소량 < 1e-6" 수렴 판정을 흔들기 때문.
                    float g;
                    Vector3 pa, pb;
                    bool converged;
                    try
                    {
                        TryGetClosestFn(a, start, out ClosestFn fa);
                        TryGetClosestFn(b, start, out ClosestFn fb);
                        g = ClosestPair(fa, fb, Vector3.zero, out pa, out pb, out converged);
                        pa += start;
                        pb += start;
                    }
                    catch (System.Exception e)
                    {
                        result.unverifiedGapPairs.Add($"[{kv.Key}] '{a.name}' <-> '{b.name}' — 거리 계산 예외: {e.Message}");
                        continue;
                    }
                    result.gapPairsMeasured++;
                    if (!converged) result.nonConvergedPairs++;

                    if (g <= ContactEpsilon || g >= threshold) continue; // 맞닿음(±0.02)·겹침 또는 충분히 넓음.

                    Vector3 mid = (pa + pb) * 0.5f;
                    Transform root = a.transform.root;
                    string filter = ClassifyGapOpening(mid, g, a, b, staticSet, out string movingNote);
                    if (movingNote != null) result.movingInvolvedCount++;
                    GapItem item = new GapItem
                    {
                        sector = kv.Key,
                        nameA = a.name,
                        nameB = b.name,
                        gap = g,
                        point = mid,
                        localPoint = root.InverseTransformPoint(mid),
                        rootName = root.name,
                        filter = filter,
                        converged = converged,
                        movingNote = movingNote,
                    };

                    if (s1Pair) result.s1Violations.Add(item);
                    if (s2Pair) result.s2Violations.Add(item); // [INF3-2] S1과 같은 방식 — 필터는 꼬리표로만.
                    if (s8Pair) result.s8Violations.Add(item); // [INF3-3] S1·S2와 같은 방식 — 필터는 꼬리표로만.
                    if (g < PinchGapMax)
                    {
                        if (item.filter == FilterOpen) result.pinchGaps.Add(item);
                        else result.pinchExcluded.Add(item);
                    }
                }
            }
        }
    }

    /// <summary>S1 규칙 대상 쌍: Obst↔Obst, Obst↔Wall(INF3 지시서 계약 C4 판정식 그대로).</summary>
    private static bool IsS1RulePair(string nameA, string nameB)
    {
        bool obstA = nameA.StartsWith(S1ObstaclePrefix, System.StringComparison.Ordinal);
        bool obstB = nameB.StartsWith(S1ObstaclePrefix, System.StringComparison.Ordinal);
        bool wallA = nameA.StartsWith(S1WallPrefix, System.StringComparison.Ordinal);
        bool wallB = nameB.StartsWith(S1WallPrefix, System.StringComparison.Ordinal);
        return (obstA && (obstB || wallB)) || (obstB && wallA);
    }

    /// <summary>[INF3-2] S2 규칙 대상 쌍: GEO_S2_Obst_↔GEO_S2_Obst_, GEO_S2_Obst_↔GEO_S2_Wall_(R2 계약 C2-1 — IsS1RulePair와
    /// 같은 판정식). S2 접두사끼리만 본다 — S1_Obst↔S2_Wall·S2_Obst↔S1_Wall은 거짓(접두사 섞지 않음).</summary>
    private static bool IsS2RulePair(string nameA, string nameB)
    {
        bool obstA = nameA.StartsWith(S2ObstaclePrefix, System.StringComparison.Ordinal);
        bool obstB = nameB.StartsWith(S2ObstaclePrefix, System.StringComparison.Ordinal);
        bool wallA = nameA.StartsWith(S2WallPrefix, System.StringComparison.Ordinal);
        bool wallB = nameB.StartsWith(S2WallPrefix, System.StringComparison.Ordinal);
        return (obstA && (obstB || wallB)) || (obstB && wallA);
    }

    /// <summary>[INF3-3] S8 구조물 규칙 대상 쌍 [계약 R2-C2-1 S8 행]: (Shelf_|Table_)↔(Shelf_|Table_), (Shelf_|Table_)↔Wall_.
    /// IsS2RulePair와 같은 판정식(구조물 = 장애물 자리). S8 접두사끼리만 본다 — 예: GEO_S8_Table_↔GEO_S2_Wall_은 거짓.
    /// Door·Floor·Ceil·Desk는 대상이 아니다([판정 23] 채워진 틈 Door_Exit↔Floor_Escape·Wall_EscapeL/R은 이 규칙 밖 — 일반 끼임만).</summary>
    private static bool IsS8RulePair(string nameA, string nameB)
    {
        bool structA = nameA.StartsWith(S8ShelfPrefix, System.StringComparison.Ordinal) ||
                       nameA.StartsWith(S8TablePrefix, System.StringComparison.Ordinal);
        bool structB = nameB.StartsWith(S8ShelfPrefix, System.StringComparison.Ordinal) ||
                       nameB.StartsWith(S8TablePrefix, System.StringComparison.Ordinal);
        bool wallA = nameA.StartsWith(S8WallPrefix, System.StringComparison.Ordinal);
        bool wallB = nameB.StartsWith(S8WallPrefix, System.StringComparison.Ordinal);
        return (structA && (structB || wallB)) || (structB && wallA);
    }

    // ── [INF3-3] S3 필수 경로 규칙 ──────────────────────────────────────

    /// <summary>[INF3-3] Map4_S3 씬의 켜진 정적 콜라이더에서 순서표 GEO 이름을 **정확한 GameObject 이름**으로 찾아 EvaluateS3Path에 넘긴다.
    /// 보고용(exit code 무관). 한계는 EvaluateS3Path 주석.</summary>
    private static void AnalyzeS3Path(List<Collider> statics, ref Result result)
    {
        HashSet<string> wanted = new HashSet<string>(S3RequiredPathOrder);
        Dictionary<string, List<Bounds>> byName = new Dictionary<string, List<Bounds>>();
        foreach (Collider c in statics)
        {
            if (!c.enabled) continue;
            if (c.gameObject.scene.name != S3SceneName) continue;
            string n = c.gameObject.name;
            if (!wanted.Contains(n)) continue;
            if (!byName.TryGetValue(n, out List<Bounds> list)) byName[n] = list = new List<Bounds>();
            list.Add(c.bounds);
        }
        result.s3Path = EvaluateS3Path(S3RequiredPathOrder, byName);
    }

    /// <summary>[INF3-3] S3 필수 경로 판정 — **순수 함수**(입력 = 순서 배열 + 이름→Bounds 목록, 물리·씬 조회 없음 → 음성 대조 ⑩이
    /// 실제 이름과 부딪히지 않는다). 순서에서 줄다리 행(S3_Bridge_*)을 뺀 GEO 행을 차례로 잇는다:
    ///  - 두 GEO 행이 원래 순서에서 바로 이웃이면 "직접 쌍" → 두 이름이 각각 정확히 1개일 때 판정한다.
    ///    틈 = 두 Bounds의 XZ 사각형 사이 거리(hypot(dx, dz)), 턱 = 윗면(max.y) 차(뒤 − 앞) [초안 §4-1:592 — C1과 같은 계산 · S3_check.py rect_gap].
    ///    틈 &gt; 1.5 + 0.02 또는 |턱| &gt; 0.8 + 0.02 → 위반 [계약 C2-1 S3 행].
    ///  - 사이에 줄다리 행이 끼면 "줄다리 사이 쌍" → 판정 제외, 건수만 센다(초안 §4-1: 줄다리가 메움, 끝 고리 틈 0·턱 0은 C11 근거).
    ///  - 이름 0개 → missing, 2개 이상 → duplicates(조용히 0건으로 넘어가지 않게 목록으로 보고). 그 쌍은 unjudgedPairs.
    /// 한계: ① 순서표는 초안 고정 사본(빌더 변경 시 불일치 가능) ② 줄다리 구간은 GEO가 아니라 끝 고리 틈0·턱0을 Audit이 검증하지 않음
    /// ③ 월드 AABB 기준 — 회전 요소는 AABB가 커져 틈이 작게·윗면이 높게 잡힌다(틈 쪽은 보수적이 아님을 주의: 틈은 과소, 턱은 과대 가능)
    /// ④ 점프 궤적·머리 공간·도달 가능성은 판정 안 함 ⑤ 지름길 SC_ledge는 순서표에 없어 제외.</summary>
    public static S3PathReport EvaluateS3Path(IList<string> order, IDictionary<string, List<Bounds>> boundsByName)
    {
        S3PathReport rep = new S3PathReport
        {
            orderCount = order.Count,
            violations = new List<S3PathItem>(),
            missing = new List<string>(),
            duplicates = new List<string>(),
        };

        // GEO 행 번호 목록 + 이름 상태.
        List<int> geoIdx = new List<int>();
        for (int i = 0; i < order.Count; i++)
        {
            string n = order[i];
            if (n.StartsWith(S3BridgePrefix, System.StringComparison.Ordinal)) continue;
            geoIdx.Add(i);
            int count = (boundsByName != null && boundsByName.TryGetValue(n, out List<Bounds> l) && l != null) ? l.Count : 0;
            if (count == 0) { if (!rep.missing.Contains(n)) rep.missing.Add(n); }
            else if (count > 1) { if (!rep.duplicates.Contains(n)) rep.duplicates.Add(n); }
        }
        rep.geoRowCount = geoIdx.Count;

        for (int k = 1; k < geoIdx.Count; k++)
        {
            int ia = geoIdx[k - 1], ib = geoIdx[k];
            if (ib - ia > 1) { rep.bridgeSkippedPairs++; continue; } // 사이에 줄다리 행 — 판정 제외.
            rep.directPairs++;
            string na = order[ia], nb = order[ib];
            if (!TryGetSingleBounds(boundsByName, na, out Bounds ba) || !TryGetSingleBounds(boundsByName, nb, out Bounds bb))
            {
                rep.unjudgedPairs++;
                continue;
            }
            rep.judgedPairs++;
            float dx = Mathf.Max(0f, Mathf.Max(ba.min.x - bb.max.x, bb.min.x - ba.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(ba.min.z - bb.max.z, bb.min.z - ba.max.z));
            float gap = Mathf.Sqrt(dx * dx + dz * dz);
            float step = bb.max.y - ba.max.y;
            bool gapBad = gap > S3PathGapMax + ContactEpsilon;
            bool stepBad = Mathf.Abs(step) > S3PathStepMax + ContactEpsilon;
            if (gapBad || stepBad)
                rep.violations.Add(new S3PathItem { nameA = na, nameB = nb, gap = gap, step = step, gapBad = gapBad, stepBad = stepBad });
        }
        return rep;
    }

    private static bool TryGetSingleBounds(IDictionary<string, List<Bounds>> map, string name, out Bounds b)
    {
        b = default(Bounds);
        if (map == null || !map.TryGetValue(name, out List<Bounds> l) || l == null || l.Count != 1) return false;
        b = l[0];
        return true;
    }

    // ── [INF3-3] S4 원통 볼록 조각 이음매 ───────────────────────────────

    /// <summary>[INF3-3] GEO_S4_Wall_Cyl_{nn}(켜진 정적 콜라이더)을 섹터(씬)별로 모아 번호순 정렬하고, 개수 N을 하드코딩하지 않고
    /// k↔(k+1) mod N(마지막↔첫 조각 포함, N = 2면 1쌍)을 MeasureSeam으로 잰다. 보고용(exit code 무관).
    /// 경고 줄: 조각 수 ≠ 48, 번호 중복·불연속(00..N−1이 아님), (가) 비볼록 GEO_S4_Wall_Cyl 잔존("S4-B (나) 미반영").</summary>
    private static void AnalyzeS4Seams(List<Collider> statics, ref Result result)
    {
        result.s4Seams = new List<SeamItem>();
        result.s4SeamViolations = new List<SeamItem>();
        result.s4SeamNotes = new List<string>();
        result.s4CylPieceCount = 0;
        result.s4LegacyCylPresent = false;

        SortedDictionary<string, List<KeyValuePair<int, Collider>>> bySector =
            new SortedDictionary<string, List<KeyValuePair<int, Collider>>>();
        foreach (Collider c in statics)
        {
            if (!c.enabled) continue;
            string sector = string.IsNullOrEmpty(c.gameObject.scene.name) ? "(이름 없는 씬)" : c.gameObject.scene.name;
            if (c.name == S4LegacyCylName)
            {
                result.s4LegacyCylPresent = true;
                result.s4SeamNotes.Add($"[{sector}] '{S4LegacyCylName}'({ShapeName(c)}) 남아 있음 — S4-B (나) 미반영 [2차판정 15]");
                continue;
            }
            System.Text.RegularExpressions.Match m = S4CylPieceRegex.Match(c.name);
            if (!m.Success) continue;
            int nn = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (!bySector.TryGetValue(sector, out List<KeyValuePair<int, Collider>> list))
                bySector[sector] = list = new List<KeyValuePair<int, Collider>>();
            list.Add(new KeyValuePair<int, Collider>(nn, c));
            result.s4CylPieceCount++;
        }

        if (bySector.Count == 0)
            result.s4SeamNotes.Add($"원통 조각 GEO_S4_Wall_Cyl_{{nn}} 0개 — 이음매 검사 대상 없음(기대 {S4ExpectedCylPieces}개 [2차판정 16])");

        foreach (KeyValuePair<string, List<KeyValuePair<int, Collider>>> kv in bySector)
        {
            List<KeyValuePair<int, Collider>> pieces = kv.Value;
            pieces.Sort((x, y) => x.Key.CompareTo(y.Key));
            int n = pieces.Count;
            if (n != S4ExpectedCylPieces)
                result.s4SeamNotes.Add($"[{kv.Key}] 경고: 원통 조각 {n}개 — 기대 {S4ExpectedCylPieces}개(48각 7.5° [2차판정 16])");
            bool contiguous = true;
            for (int k = 0; k < n; k++) if (pieces[k].Key != k) { contiguous = false; break; }
            if (!contiguous)
            {
                List<string> nums = new List<string>();
                foreach (KeyValuePair<int, Collider> p in pieces) nums.Add(p.Key.ToString("00"));
                result.s4SeamNotes.Add($"[{kv.Key}] 경고: 조각 번호가 00..{n - 1:00} 연속이 아님(중복·빠짐) — 실제 {string.Join(",", nums)}. " +
                                       "쌍은 정렬 순서로 잇는다(번호가 빠진 자리의 이음매는 두 조각이 떨어져 있어 위반으로 드러날 것 — 추정).");
            }
            int pairCount = n >= 3 ? n : (n == 2 ? 1 : 0);
            for (int k = 0; k < pairCount; k++)
            {
                Collider a = pieces[k].Value, b = pieces[(k + 1) % n].Value;
                SeamItem s = MeasureSeam(kv.Key, a, b);
                result.s4Seams.Add(s);
                if (s.Violates) result.s4SeamViolations.Add(s);
            }
        }
    }

    /// <summary>[INF3-3] 이음매 한 쌍 측정 — 틈 = 기존 ClosestPair(교대 투영, AnalyzeGaps와 같은 시작점·원점 이동),
    /// 겹침 = Physics.ComputePenetration dist(두 AABB가 만날 때만, 겹치지 않으면 0). 측정 불가 형상(비볼록 메시 등)이나
    /// 계산 예외면 measured = false. 음성 대조 ⑪이 같은 함수를 직접 부른다.</summary>
    public static SeamItem MeasureSeam(string sector, Collider a, Collider b)
    {
        SeamItem s = new SeamItem { sector = sector, nameA = a.name, nameB = b.name, measured = false, gap = -1f, penetration = 0f };
        if (!TryGetClosestFn(a, Vector3.zero, out _) || !TryGetClosestFn(b, Vector3.zero, out _)) return s;
        try
        {
            Bounds ha = a.bounds, hb = b.bounds;
            ha.Expand(PinchGapMax); hb.Expand(PinchGapMax);
            Vector3 start = (Vector3.Max(ha.min, hb.min) + Vector3.Min(ha.max, hb.max)) * 0.5f;
            TryGetClosestFn(a, start, out ClosestFn fa);
            TryGetClosestFn(b, start, out ClosestFn fb);
            s.gap = ClosestPair(fa, fb, Vector3.zero, out _, out _, out bool converged);
            s.converged = converged;
            if (a.bounds.Intersects(b.bounds) &&
                Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                                           b, b.transform.position, b.transform.rotation, out Vector3 _, out float dist))
                s.penetration = dist;
            s.measured = true;
        }
        catch (System.Exception)
        {
            s.measured = false; // 비지원 형상 조합 — 측정 불가로 보고.
        }
        return s;
    }

    /// <summary>틈이 "걸을 수 있는 윗면 위에서 위로 열려 있는가"(거짓 양성 줄이기, INF3 지시서).
    /// 틈 중앙 m(두 최근접점의 중점)에서 세 가지를 순서대로 본다:
    ///  1) m이 A·B 말고 다른 **정적** 솔리드 콜라이더 속이면 "메워짐"(벽 속 틈 — 예: 두 벽 조각 사이를 세 번째 조각이 채움).
    ///  2) m에서 위로 1U 안에 **정적** 면이 있으면 "위 덮임"(바닥 아래 틈·수직 틈 — 도형이 위에서 들어올 수 없다).
    ///     한쪽이 다른 쪽 바로 위에 있는 수직 틈(g &lt; 2)은 m에서 위쪽 형상까지 g/2 &lt; 1U라 여기서 걸린다.
    ///  3) m에서 아래로 쏜 레이의 첫 면이 법선 y ≥ 0.5(걸을 수 있는 윗면)여야 "열림". A·B 자신의 윗면도 된다.
    ///     두 볼록 형상의 최근접 선분이 수평이면 m을 지나는 연직선은 두 형상을 가르는 슬랩의 중앙 평면
    ///     안에 있어 A·B를 맞지 않는다 — 그 아래 바닥에 닿는다.
    /// 한계: 틈 전체가 아니라 중앙 한 점만 본다(목록 보고용 — 치명 판정은 검토 몫).
    ///
    /// [INF3 R1 — 가동체는 제외 사유가 될 수 없다] 쌍 목록은 정적 솔리드만 쓰는데, 예전 필터는 물리 쿼리
    /// (AllLayers)에 걸린 **모든** 콜라이더를 메움재·덮개로 인정했다 → Rigidbody가 붙은 가동 기믹(예: 팀
    /// SpikeTrap, kinematic)이 편집 시점 자세로 틈을 가리면 "메워짐"으로 빠졌다(R1 사례: S5 가시 슬롯 g=0.6
    /// 4건). 가동체는 움직이므로 편집 자세가 틈을 막는다는 보장이 없다. 그래서:
    ///  1)·2) 메움·덮음은 staticSet(쌍 목록과 같은 정적 솔리드)만 인정한다. 가동체만 걸리면 제외하지 않고
    ///        movingNote에 "가동체 메움/덮음"을 남긴다.
    ///  3) 아래 면: 첫 **정적** 면이 걸을 면이면 열림. 그보다 위에서 가동체를 맞으면 "가동체 바닥" 꼬리표를
    ///     남기고, 그 가동체 면이 걸을 면(법선 y ≥ 0.5)이면 열림으로 본다(포함 쪽으로만 — 가동체가 제외를
    ///     만들지 않게 하면서, 가동 발판 위 틈을 놓치지도 않는다).
    /// RaycastAll을 쓰는 이유: Raycast는 가장 가까운 한 개만 돌려줘 그것이 가동체면 뒤의 정적 면을 못 본다.</summary>
    private static string ClassifyGapOpening(Vector3 m, float g, Collider a, Collider b,
                                             HashSet<Collider> staticSet, out string movingNote)
    {
        List<string> notes = new List<string>();
        string verdict = null;

        // 1) 메움 — 정적 메움재만 인정.
        float r = Mathf.Min(0.005f, g * 0.25f);
        List<string> movingFillers = new List<string>();
        foreach (Collider c in Physics.OverlapSphere(m, r, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (c == a || c == b) continue;
            if (staticSet.Contains(c)) verdict = FilterFilled;
            else movingFillers.Add(c.name);
        }
        if (movingFillers.Count > 0) notes.Add(TagMovingFilled + ": " + JoinNames(movingFillers));

        // 2) 위 덮임 — 정적 면만 인정.
        if (verdict == null)
        {
            List<string> movingAbove = new List<string>();
            foreach (RaycastHit h in Physics.RaycastAll(m, Vector3.up, OpenAboveClearance, Physics.AllLayers,
                                                        QueryTriggerInteraction.Ignore))
            {
                if (staticSet.Contains(h.collider)) verdict = FilterCovered;
                else movingAbove.Add(h.collider.name);
            }
            if (movingAbove.Count > 0) notes.Add(TagMovingCovered + ": " + JoinNames(movingAbove));
        }

        // 3) 아래 걸을 면 — 첫 정적 면이 판정, 그 위의 가동체는 꼬리표(걸을 면이면 열림 쪽으로만 반영).
        if (verdict == null)
        {
            RaycastHit[] downs = Physics.RaycastAll(m, Vector3.down, MaxDownProbe, Physics.AllLayers,
                                                    QueryTriggerInteraction.Ignore);
            System.Array.Sort(downs, (x, y) => x.distance.CompareTo(y.distance));
            bool walkable = false;
            List<string> movingBelow = new List<string>();
            foreach (RaycastHit h in downs)
            {
                if (staticSet.Contains(h.collider))
                {
                    if (h.normal.y >= WalkableNormalY) walkable = true;
                    break;
                }
                movingBelow.Add(h.collider.name);
                if (h.normal.y >= WalkableNormalY) walkable = true;
            }
            if (movingBelow.Count > 0) notes.Add(TagMovingBelow + ": " + JoinNames(movingBelow));
            verdict = walkable ? FilterOpen : FilterNoWalkable;
        }

        movingNote = notes.Count > 0 ? string.Join(" / ", notes) : null;
        return verdict;
    }

    private static string JoinNames(List<string> names)
    {
        List<string> quoted = new List<string>();
        foreach (string n in names) if (!quoted.Contains("'" + n + "'")) quoted.Add("'" + n + "'");
        return string.Join(", ", quoted);
    }

    /// <summary>콜라이더별 "가장 가까운 표면 점" 함수.
    /// - BoxCollider: 직접 OBB 계산 — 중심 = TransformPoint(center), 반축 = TransformVector(size/2 각 축)
    ///   (회전·스케일 반영). 점을 각 단위 반축에 사영해 [-e, e]로 자르면 OBB 위(또는 속)의 최근접점이다.
    ///   Physics 엔진 상태와 무관하게 결정적이라 파이썬으로 같은 식을 재현해 검산할 수 있다.
    /// - Sphere/Capsule/볼록 MeshCollider: Physics.ClosestPoint(Unity 문서: 이 네 형상만 지원).
    /// - 그 외(비볼록 MeshCollider, TerrainCollider, WheelCollider 등): 측정 불가 → null(미검증 목록으로 보고).
    /// origin: 반환 함수는 "origin 기준 상대 좌표"를 받고 돌려준다(원점 이동 — ClosestPair 호출부 주석).</summary>
    private static bool TryGetClosestFn(Collider c, Vector3 origin, out ClosestFn fn)
    {
        if (c is BoxCollider box)
        {
            Transform t = box.transform;
            Vector3 center = t.TransformPoint(box.center) - origin;
            Vector3[] half =
            {
                t.TransformVector(new Vector3(box.size.x * 0.5f, 0f, 0f)),
                t.TransformVector(new Vector3(0f, box.size.y * 0.5f, 0f)),
                t.TransformVector(new Vector3(0f, 0f, box.size.z * 0.5f)),
            };
            fn = p => ObbClosestPoint(center, half, p);
            return true;
        }
        // 메시 없는 MeshCollider는 ClosestPoint가 에러를 낼 수 있어(추정) 측정 불가로 돌린다 — 감사 단계는
        // 억제 구간 밖이라 뜻밖의 에러 로그가 exitCode를 바꾸지 않게 한다.
        if (c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mc && mc.convex && mc.sharedMesh != null))
        {
            Collider cc = c;
            Vector3 pos = c.transform.position;
            Quaternion rot = c.transform.rotation;
            fn = p => Physics.ClosestPoint(p + origin, cc, pos, rot) - origin;
            return true;
        }
        fn = null;
        return false;
    }

    private static Vector3 ObbClosestPoint(Vector3 center, Vector3[] halfAxes, Vector3 p)
    {
        Vector3 d = p - center;
        Vector3 q = center;
        for (int k = 0; k < 3; k++)
        {
            float e = halfAxes[k].magnitude;
            if (e < 1e-6f) continue; // 두께 0 축 — 그 방향 성분은 중심 그대로.
            Vector3 u = halfAxes[k] / e;
            q += u * Mathf.Clamp(Vector3.Dot(d, u), -e, e);
        }
        return q;
    }

    /// <summary>두 볼록 형상의 최단 거리 — 교대 투영(alternating projection):
    /// a_k = P_A(b_{k-1}), b_k = P_B(a_k). 두 볼록 집합이 떨어져 있으면 (a_k, b_k)는 최단 거리를 이루는 쌍으로
    /// 수렴하고(Cheney &amp; Goldstein 1959, "Proximity maps for convex sets"), |a_k − b_k|는 단조 감소한다
    /// (|a_{k+1}−b_{k+1}| ≤ |a_{k+1}−b_k| ≤ |a_k−b_k|) — 그래서 중간에 멈춰도 결과는 참값의 **상한**이다.
    /// 겹치면 공통점으로 수렴해 0이 된다. 평행한 면 쌍·모서리↔면은 1~2회에 끝나고, 거의 평행한
    /// 모서리↔모서리만 느리다(반복 상한 도달 시 converged=false로 보고서에 표시).
    /// ComputePenetration은 겹칠 때만 값을 주므로(떨어진 쌍은 false) 틈 측정에 쓸 수 없어 이 방법을 택했다.</summary>
    private static float ClosestPair(ClosestFn fa, ClosestFn fb, Vector3 start,
                                     out Vector3 pa, out Vector3 pb, out bool converged)
    {
        pb = start;
        pa = start;
        float prev = float.MaxValue;
        for (int k = 0; k < MaxProjectionIterations; k++)
        {
            pa = fa(pb);
            pb = fb(pa);
            float d = Vector3.Distance(pa, pb);
            // 상한 d가 이미 맞닿음 오차 이하면 참값도 그 이하 — 분류가 확정되므로 더 돌 필요 없다.
            if (d <= ContactEpsilon || prev - d < ProjectionTolerance) { converged = true; return d; }
            prev = d;
        }
        converged = false;
        return Vector3.Distance(pa, pb);
    }

    private static string ShapeName(Collider c)
    {
        if (c is MeshCollider mc) return mc.convex ? "MeshCollider(볼록)" : "MeshCollider(비볼록)";
        return c.GetType().Name;
    }

    /// <summary>결과 txt용 — 섹터별 끼임 틈 건수·목록(쌍 이름·g·위치), S1·S2 규칙 위반, 제외·미검증 요약.</summary>
    public static string FormatGapReport(Result r, int maxExcludedPerSector = 40)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        if (r.sectors == null) return "(끼임 틈 검사 결과 없음)";
        sb.AppendLine($"기준: 일반 {ContactEpsilon:F2} < g < {PinchGapMax:F1}U(위로 열린 틈만) · " +
                      $"S1 장애물({S1ObstaclePrefix}↔{S1ObstaclePrefix}/{S1WallPrefix}) {ContactEpsilon:F2} < g < {S1ObstacleGapMin:F1} 위반 · " +
                      $"S2 장애물({S2ObstaclePrefix}↔{S2ObstaclePrefix}/{S2WallPrefix}) {ContactEpsilon:F2} < g < {S2ObstacleGapMin:F1} 위반 · " +
                      $"g ≤ {ContactEpsilon:F2}는 맞닿음(0). 필수 경로 여부는 판정하지 않음.");
        sb.AppendLine($"측정한 쌍 {r.gapPairsMeasured}개 · 수렴 미완(상한값) {r.nonConvergedPairs}개 · " +
                      $"측정 불가 쌍 {r.unverifiedGapPairs.Count}개");
        List<GapItem> s2All = r.s2Violations ?? new List<GapItem>(); // [INF3-2] 옛 Result(필드 없음)도 받게.
        sb.AppendLine($"합계: 끼임 틈 {r.pinchGaps.Count}건 · S1 장애물 규칙 위반 {r.s1Violations.Count}건 · " +
                      $"S2 장애물 규칙 위반 {s2All.Count}건 · " +
                      $"필터 제외 {r.pinchExcluded.Count}건 · 가동체 관여 {r.movingInvolvedCount}건");
        sb.AppendLine($"  (가동체 관여 = Rigidbody 부착 콜라이더가 틈 중앙·위·아래에 걸린 틈. 필터는 정적 콜라이더만 " +
                      $"막는 것으로 인정하므로 가동체 때문에 제외되지는 않는다 — 꼬리표 {{{TagMovingFilled}/{TagMovingCovered}/{TagMovingBelow}}}.)");
        foreach (string sector in r.sectors)
        {
            List<GapItem> pinch = r.pinchGaps.FindAll(x => x.sector == sector);
            List<GapItem> s1 = r.s1Violations.FindAll(x => x.sector == sector);
            List<GapItem> s2 = s2All.FindAll(x => x.sector == sector); // [INF3-2]
            List<GapItem> excl = r.pinchExcluded.FindAll(x => x.sector == sector);
            // [INF3 R1] 가동체 관여 제외 항목은 생략 한도와 무관하게 따로 먼저 적는다(따로 보고).
            List<GapItem> exclMoving = excl.FindAll(x => x.movingNote != null);
            List<GapItem> exclRest = excl.FindAll(x => x.movingNote == null);
            // g < 1.0인 S1·S2 위반 항목은 끼임·제외 목록에도 있으므로 g ≥ 1.0인 것만 더한다(중복 없이 — 전체 합과 같은 기준).
            // [INF3-3] S8 위반(g ≥ 1.0)도 같은 기준으로 더한다 — 섹터 합이 전체 movingInvolvedCount와 맞게.
            List<GapItem> s8 = (r.s8Violations ?? new List<GapItem>()).FindAll(x => x.sector == sector);
            int movingInSector = pinch.FindAll(x => x.movingNote != null).Count + exclMoving.Count +
                                 s1.FindAll(x => x.movingNote != null && x.gap >= PinchGapMax).Count +
                                 s2.FindAll(x => x.movingNote != null && x.gap >= PinchGapMax).Count +
                                 s8.FindAll(x => x.movingNote != null && x.gap >= PinchGapMax).Count;
            sb.AppendLine($"[{sector}] 끼임 틈 {pinch.Count}건 · S1 규칙 위반 {s1.Count}건 · S2 규칙 위반 {s2.Count}건 · " +
                          $"필터 제외 {excl.Count}건 · 가동체 관여 {movingInSector}건");
            foreach (GapItem x in pinch) sb.AppendLine("  끼임 " + FormatGapItem(x));
            foreach (GapItem x in s1) sb.AppendLine("  S1위반 " + FormatGapItem(x));
            foreach (GapItem x in s2) sb.AppendLine("  S2위반 " + FormatGapItem(x));
            foreach (GapItem x in exclMoving) sb.AppendLine("  제외(가동체 관여) " + FormatGapItem(x));
            for (int k = 0; k < exclRest.Count; k++)
            {
                if (k >= maxExcludedPerSector) { sb.AppendLine($"  (제외 목록 외 {exclRest.Count - k}건 생략)"); break; }
                sb.AppendLine("  제외 " + FormatGapItem(exclRest[k]));
            }
        }
        foreach (string u in r.unverifiedGapPairs) sb.AppendLine("  미검증 " + u);
        sb.AppendLine();
        sb.Append(FormatR3RulesReport(r)); // [INF3-3] 기존 문구 뒤에 덧붙인다.
        return sb.ToString().TrimEnd();
    }

    /// <summary>[INF3-3] R3 추가 규칙 보고(보고용 — exit code 무관): 섹터 목록(S1~S8 포함 여부), 측정 불가 쌍 섹터별,
    /// S8 구조물 규칙, S3 필수 경로, S4 원통 이음매. FormatGapReport 끝에 붙는다.</summary>
    public static string FormatR3RulesReport(Result r)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("── R3 추가 규칙(INF3-3 · 보고용 — exit code 무관, 판정 17의 exit는 관통만) ──");
        sb.AppendLine($"기준: S8 구조물({S8ShelfPrefix}·{S8TablePrefix} 서로, 둘↔{S8WallPrefix}) {ContactEpsilon:F2} < g < {S8StructureGapMin:F1} 위반 · " +
                      $"S3 필수 경로(초안 §4-1 순서표 GEO 직접 이웃) 틈 > {S3PathGapMax:F1}+{ContactEpsilon:F2} 또는 |턱| > {S3PathStepMax:F1}+{ContactEpsilon:F2} 위반 · " +
                      $"S4 원통 조각 이음매 틈 > {S4SeamTolerance:F2} 또는 겹침 > {S4SeamTolerance:F2} 위반. " +
                      "(위 기준 줄의 '필수 경로 여부는 판정하지 않음'은 일반 끼임 목록 얘기다 — S3 순서표 규칙은 여기서 따로 판정.)");

        // 섹터 목록 — S1~S8 포함 확인 [계약 R2-C4 "대상 섹터 1~8"].
        List<string> sectors = r.sectors ?? new List<string>();
        List<string> missingSectors = new List<string>();
        for (int i = 1; i <= ExpectedSectorCount; i++)
            if (!sectors.Contains($"Map4_S{i}")) missingSectors.Add($"Map4_S{i}");
        sb.AppendLine($"섹터 목록({sectors.Count}): {string.Join(", ", sectors)} · Map4_S1~S{ExpectedSectorCount} 중 빠진 섹터: " +
                      (missingSectors.Count == 0 ? "없음" : string.Join(", ", missingSectors) + " (정적 콜라이더 0개이거나 씬 미로드)"));

        // 측정 불가 쌍 섹터별.
        List<string> unv = r.unverifiedGapPairs ?? new List<string>();
        List<string> perSector = new List<string>();
        int counted = 0;
        foreach (string s in sectors)
        {
            int c = unv.FindAll(u => u.StartsWith("[" + s + "]", System.StringComparison.Ordinal)).Count;
            counted += c;
            if (c > 0) perSector.Add($"{s} {c}");
        }
        if (unv.Count - counted > 0) perSector.Add($"기타 {unv.Count - counted}");
        sb.AppendLine($"측정 불가 쌍 섹터별: 합 {unv.Count}개" + (perSector.Count > 0 ? " — " + string.Join(" · ", perSector) : ""));

        // S8 구조물 규칙.
        List<GapItem> s8All = r.s8Violations ?? new List<GapItem>();
        sb.AppendLine($"S8 구조물 규칙 위반 {s8All.Count}건 · 대상 이름(Shelf_·Table_) {r.s8RuleTargetCount}개 · Wall_ {r.s8WallCount}개");
        if (r.s8RuleTargetCount == 0)
            sb.AppendLine("  경고: S8 규칙 대상 0개 — 통합(INT-2) 전일 수 있음(추정)");
        foreach (GapItem x in s8All) sb.AppendLine("  S8위반 " + FormatGapItem(x));

        // S3 필수 경로.
        S3PathReport p = r.s3Path;
        List<S3PathItem> pv = p.violations ?? new List<S3PathItem>();
        List<string> pm = p.missing ?? new List<string>();
        List<string> pd = p.duplicates ?? new List<string>();
        sb.AppendLine($"S3 필수 경로({S3SceneName}) 위반 {pv.Count}건 · 순서표 {p.orderCount}행(GEO {p.geoRowCount}) · 직접 쌍 {p.directPairs}개" +
                      $"(판정 {p.judgedPairs} · 판정 못 함 {p.unjudgedPairs}) · 줄다리 사이 쌍 {p.bridgeSkippedPairs}개(판정 제외) · " +
                      $"이름 없음 {pm.Count}개 · 중복 {pd.Count}개");
        foreach (S3PathItem x in pv)
            sb.AppendLine($"  S3위반 '{x.nameA}' → '{x.nameB}' 틈 {x.gap:F3}{(x.gapBad ? "(초과)" : "")} · 턱 {x.step:+0.000;-0.000;0.000}{(x.stepBad ? "(초과)" : "")}");
        if (pm.Count > 0) sb.AppendLine("  이름 없음: " + string.Join(", ", pm));
        if (pd.Count > 0) sb.AppendLine("  중복: " + string.Join(", ", pd));
        sb.AppendLine("  한계: 순서표는 초안 §4-1 고정 사본 · 줄다리 구간(끝 고리 틈0·턱0, 초안 C11)은 Audit 미검증 · 월드 AABB 기준 · " +
                      "점프 궤적·머리 공간·도달 가능성은 판정 안 함 · 지름길 SC_ledge 제외");

        // S4 원통 이음매.
        List<SeamItem> seams = r.s4Seams ?? new List<SeamItem>();
        List<SeamItem> sv = r.s4SeamViolations ?? new List<SeamItem>();
        int seamUnmeasured = seams.FindAll(x => !x.measured).Count;
        sb.AppendLine($"S4 원통 조각 {r.s4CylPieceCount}개(기대 {S4ExpectedCylPieces}) · 이음매 {seams.Count}쌍 · 위반 {sv.Count}건 · 측정 불가 {seamUnmeasured}쌍" +
                      (r.s4LegacyCylPresent ? " · (가) 비볼록 잔존" : ""));
        foreach (string note in r.s4SeamNotes ?? new List<string>()) sb.AppendLine("  " + note);
        foreach (SeamItem x in sv) sb.AppendLine("  이음매위반 " + FormatSeamItem(x));
        foreach (SeamItem x in seams) if (!x.measured) sb.AppendLine("  이음매 측정 불가 " + FormatSeamItem(x));

        sb.AppendLine($"합계(R3 추가): S3 경로 위반 {pv.Count}건 · S4 이음매 위반 {sv.Count}건 · S8 구조물 규칙 위반 {s8All.Count}건");
        return sb.ToString();
    }

    private static string FormatSeamItem(SeamItem x)
    {
        if (!x.measured) return $"[{x.sector}] '{x.nameA}' <-> '{x.nameB}' — 측정 불가 형상·계산 예외";
        return $"[{x.sector}] '{x.nameA}' <-> '{x.nameB}' 틈 {x.gap:F3}{(x.converged ? "" : " (수렴 미완 — 상한값)")} · 겹침 {x.penetration:F3}";
    }

    private static string FormatGapItem(GapItem x)
    {
        string conv = x.converged ? "" : " (수렴 미완 — 상한값)";
        return $"g={x.gap:F3}U{conv} — '{x.nameA}' <-> '{x.nameB}' @월드({x.point.x:F2}, {x.point.y:F2}, {x.point.z:F2})" +
               $" / {x.rootName} 로컬({x.localPoint.x:F2}, {x.localPoint.y:F2}, {x.localPoint.z:F2}) [{x.filter}]" +
               (x.movingNote != null ? $" {{{x.movingNote}}}" : "");
    }

    /// <summary>[INF3] 끼임 틈 음성 대조 — 실제 맵과 멀리(5000, 500, 5000) 임시 정적 박스를 만들어
    /// 틈 검사가 실제로 동작하는지 증명하고 즉시 정리한다. 사례(각자 자기 바닥판 위, 60U씩 떨어뜨림):
    ///  ① 축정렬 1U 박스 두 개, 틈 0.5U → 끼임 목록에 "열림"으로 있어야 한다(필수 — 지시서).
    ///  ② 30° Y회전 박스 모서리 ↔ 축정렬 박스 면, 틈 0.5U → 끼임 목록에 있어야 한다(회전 OBB 경로 확인).
    ///  ③ GEO_S1_Obst_ ↔ GEO_S1_Wall_ 틈 1.5 → S1 위반 목록에 있고 일반 끼임 목록엔 없어야 한다(1.5 ≥ 1.0).
    ///  ④ 틈 1.2U → 어느 목록에도 없어야 한다(거짓 양성 대조).
    ///  ⑤ [R1] 0.5U 틈을 kinematic Rigidbody 박스가 메움 → 끼임 목록에 "열림" + "가동체 메움" 꼬리표로 있어야 한다.
    ///  ⑥ [R1] 같은 틈을 정적 박스가 메움 → 제외 목록에 "메워짐"으로 있고 끼임 목록엔 없어야 한다.
    ///  ⑦ [INF3-2] GEO_S2_Obst_ ↔ GEO_S2_Wall_ 틈 1.5 → S2 위반 목록에 있고 일반 끼임·S1 위반 목록엔 없어야 한다(③의 S2판).
    ///  ⑧ [INF3-2] GEO_S2_Obst_ 끼리 틈 2.5(경계 — 허용), 그 한쪽 ↔ GEO_S2_Wall_ 맞닿음 0 → 어느 목록에도 없어야 한다.
    ///  ⑨ [INF3-2] 접두사 섞임 대조: GEO_S1_Obst_ ↔ GEO_S2_Wall_ 1.5, GEO_S2_Obst_ ↔ GEO_S1_Wall_ 1.5 → S1·S2 위반·끼임
    ///     어느 목록에도 없어야 한다(S1·S2 규칙은 같은 섹터 접두사끼리만, 1.5 ≥ 1.0이라 일반 끼임도 아님).
    ///  ⑩ [INF3-3] S3 경로 순수 함수(EvaluateS3Path — 씬 물체 없음, 합성 이름 NC3_*): 합성 순서 3개
    ///     (가) 틈 1.5·턱 +0.8 → 위반 0 (나) 틈 1.6 → 틈 위반, 턱 +0.9 → 턱 위반, 턱 −0.9 → 턱 위반(내림)
    ///     (다) 사이에 줄다리 행 → 줄다리 사이 쌍 1(판정 제외), 이름 없음 1·중복 1 → 목록 보고·판정 못 함 2.
    ///  ⑪ [INF3-3] S4 이음매(MeasureSeam 직접 호출, 이름은 GEO_S4_Wall_Cyl_ 아님 → 실제 조각 모음과 안 섞임): 박스 맞닿음 0 → 위반 아님,
    ///     틈 0.05 → 위반, 겹침 0.05 → 위반, 볼록 MeshCollider 맞닿음 쌍 → 측정됨·위반 아님·끼임 측정 불가 목록에 없음.
    ///  ⑫ [INF3-3] S8: GEO_S8_Shelf_NC ↔ GEO_S8_Wall_NC 1.5 → S8 위반(끼임·S1·S2 목록엔 없음),
    ///     GEO_S8_Table_NC ↔ GEO_S2_Wall_NC_S8Mix 1.5 → 어느 목록에도 없음(접두사 섞임).
    /// ①~⑨ 배치·기대값은 바꾸지 않았다. ⑪·⑫ 바닥판은 ⑨ 바닥판(x 476~486)에서 48U 이상 떨어뜨렸다(⑪ x 554~566, ⑫ x 634~646).
    /// 에러 로그를 내지 않는다(판정은 반환값) — Map4Batch.Audit의 억제 건수 상수에 영향 없음.
    /// true = 전부 기대대로, false = 하나라도 어긋남(회귀).</summary>
    public static bool RunGapNegativeControlTest(out string detail)
    {
        GameObject root = new GameObject("Map4Audit_GapNC_Root");
        Mesh ncMesh = null; // [INF3-3] ⑪ 볼록 MeshCollider용 임시 메시 — finally에서 정리.
        try
        {
            root.transform.position = new Vector3(5000f, 500f, 5000f); // [추정] 맵 밖 — 레이가 실제 맵에 닿지 않게.
            const float halfRotExtent = 0.6830127f; // [계산] 0.5·(cos30°+sin30°) — 30° 회전 1U 상자의 x 반폭.

            // ① 0.5U 축정렬: A 오른면 x=0.5, B 왼면 x=1.0.
            NcBox(root, "Map4Audit_GNC_Floor1", new Vector3(0f, -0.1f, 0f), new Vector3(8f, 0.2f, 8f), 0f);
            NcBox(root, "Map4Audit_GNC_A", new Vector3(0f, 0.5f, 0f), Vector3.one, 0f);
            NcBox(root, "Map4Audit_GNC_B", new Vector3(1.5f, 0.5f, 0f), Vector3.one, 0f);
            // ② 30° 회전: C 오른면 x=60.5, R의 가장 왼쪽 모서리 x = 61.0 → 틈 0.5.
            NcBox(root, "Map4Audit_GNC_Floor2", new Vector3(60f, -0.1f, 0f), new Vector3(8f, 0.2f, 8f), 0f);
            NcBox(root, "Map4Audit_GNC_C", new Vector3(60f, 0.5f, 0f), Vector3.one, 0f);
            NcBox(root, "Map4Audit_GNC_R30", new Vector3(61.0f + halfRotExtent, 0.5f, 0f), Vector3.one, 30f);
            // ③ S1 규칙: 장애물 오른면 x=120.5, 벽 왼면 x=122.0 → 틈 1.5.
            NcBox(root, "Map4Audit_GNC_Floor3", new Vector3(120f, -0.1f, 0f), new Vector3(8f, 0.2f, 8f), 0f);
            NcBox(root, "GEO_S1_Obst_NC_Desk", new Vector3(120f, 0.5f, 0f), Vector3.one, 0f);
            NcBox(root, "GEO_S1_Wall_NC", new Vector3(122.5f, 1f, 0f), new Vector3(1f, 2f, 4f), 0f);
            // ④ 1.2U: E 오른면 x=180.5, F 왼면 x=181.7.
            NcBox(root, "Map4Audit_GNC_Floor4", new Vector3(180f, -0.1f, 0f), new Vector3(8f, 0.2f, 8f), 0f);
            NcBox(root, "Map4Audit_GNC_E", new Vector3(180f, 0.5f, 0f), Vector3.one, 0f);
            NcBox(root, "Map4Audit_GNC_F", new Vector3(182.2f, 0.5f, 0f), Vector3.one, 0f);
            // ⑤ [INF3 R1] 0.5U 틈을 kinematic 가동체(폭 0.5 — 양옆에 딱 맞닿음)가 메움: G 오른면 x=240.5,
            //    H 왼면 x=241.0 → 가동체 때문에 제외되면 안 된다(끼임 목록 + "가동체 메움" 꼬리표).
            NcBox(root, "Map4Audit_GNC_Floor5", new Vector3(240f, -0.1f, 0f), new Vector3(8f, 0.2f, 8f), 0f);
            NcBox(root, "Map4Audit_GNC_G", new Vector3(240f, 0.5f, 0f), Vector3.one, 0f);
            NcBox(root, "Map4Audit_GNC_H", new Vector3(241.5f, 0.5f, 0f), Vector3.one, 0f);
            GameObject kin = NcBox(root, "Map4Audit_GNC_KinFill", new Vector3(240.75f, 0.5f, 0f), new Vector3(0.5f, 1f, 1f), 0f);
            Rigidbody kinBody = kin.AddComponent<Rigidbody>();
            kinBody.isKinematic = true;
            kinBody.useGravity = false;
            // ⑥ [INF3 R1] 같은 배치를 정적 박스가 메움: I 오른면 x=300.5, J 왼면 x=301.0 → "메워짐"으로 제외돼야
            //    한다(정적 메움재 필터가 여전히 동작함 — 반대 방향 회귀 대조).
            NcBox(root, "Map4Audit_GNC_Floor6", new Vector3(300f, -0.1f, 0f), new Vector3(8f, 0.2f, 8f), 0f);
            NcBox(root, "Map4Audit_GNC_I", new Vector3(300f, 0.5f, 0f), Vector3.one, 0f);
            NcBox(root, "Map4Audit_GNC_J", new Vector3(301.5f, 0.5f, 0f), Vector3.one, 0f);
            NcBox(root, "Map4Audit_GNC_StaticFill", new Vector3(300.75f, 0.5f, 0f), new Vector3(0.5f, 1f, 1f), 0f);
            // ⑦ [INF3-2] S2 규칙: 장애물 오른면 x=360.5, 벽 왼면 x=362.0 → 틈 1.5(③과 같은 치수).
            NcBox(root, "Map4Audit_GNC_Floor7", new Vector3(360f, -0.1f, 0f), new Vector3(8f, 0.2f, 8f), 0f);
            NcBox(root, "GEO_S2_Obst_NC_Shelf", new Vector3(360f, 0.5f, 0f), Vector3.one, 0f);
            NcBox(root, "GEO_S2_Wall_NC", new Vector3(362.5f, 1f, 0f), new Vector3(1f, 2f, 4f), 0f);
            // ⑧ [INF3-2] S2 허용 쪽: P 오른면 x=420.5, Q 왼면 x=423.0 → 틈 2.5(허용 경계, g ≥ 2.5는 보고 안 함).
            //    Q 오른면 x=424.0 = 벽 왼면 → 맞닿음 0. P↔벽 3.5. 바닥판은 x 416~428(벽까지 받친다).
            NcBox(root, "Map4Audit_GNC_Floor8", new Vector3(422f, -0.1f, 0f), new Vector3(12f, 0.2f, 8f), 0f);
            NcBox(root, "GEO_S2_Obst_NC_P", new Vector3(420f, 0.5f, 0f), Vector3.one, 0f);
            NcBox(root, "GEO_S2_Obst_NC_Q", new Vector3(423.5f, 0.5f, 0f), Vector3.one, 0f);
            NcBox(root, "GEO_S2_Wall_NC_Touch", new Vector3(424.5f, 1f, 0f), new Vector3(1f, 2f, 4f), 0f);
            // ⑨ [INF3-2] 접두사 섞임: z=0 줄 S1장애물 오른면 x=480.5 ↔ S2벽 왼면 x=482.0(1.5),
            //    z=10 줄 S2장애물 오른면 x=480.5 ↔ S1벽 왼면 x=482.0(1.5). 두 줄 사이 z 간격 6(벽 z 끝 2 ↔ 8) ≥ 2.5 —
            //    같은 섹터 접두사끼리(S1장애물↔S1벽 등)는 틈이 2.5 이상이라 보고 대상이 아니다. 바닥판 x 476~486, z −5~15.
            NcBox(root, "Map4Audit_GNC_Floor9", new Vector3(481f, -0.1f, 5f), new Vector3(10f, 0.2f, 20f), 0f);
            NcBox(root, "GEO_S1_Obst_NC_Mix", new Vector3(480f, 0.5f, 0f), Vector3.one, 0f);
            NcBox(root, "GEO_S2_Wall_NC_Mix", new Vector3(482.5f, 1f, 0f), new Vector3(1f, 2f, 4f), 0f);
            NcBox(root, "GEO_S2_Obst_NC_Mix", new Vector3(480f, 0.5f, 10f), Vector3.one, 0f);
            NcBox(root, "GEO_S1_Wall_NC_Mix", new Vector3(482.5f, 1f, 10f), new Vector3(1f, 2f, 4f), 0f);
            // ⑪ [INF3-3] S4 이음매: 네 줄(z 0·6·12·18, 줄 사이 z 간격 5 ≥ 1.0). 바닥판 x 554~566, z −5~25.
            //    T 맞닿음: A 오른면 x=558.5 = B 왼면 → 틈 0. G 틈 0.05: B 왼면 x=558.55. O 겹침 0.05: B 왼면 x=558.45.
            //    M 볼록 메시 맞닿음: 1U 정육면체 메시, A 오른면 x=558.5 = B 왼면.
            NcBox(root, "Map4Audit_GNC_Floor11", new Vector3(560f, -0.1f, 10f), new Vector3(12f, 0.2f, 30f), 0f);
            GameObject seamTA = NcBox(root, "Map4Audit_GNC_SeamT_A", new Vector3(558f, 0.5f, 0f), Vector3.one, 0f);
            GameObject seamTB = NcBox(root, "Map4Audit_GNC_SeamT_B", new Vector3(559f, 0.5f, 0f), Vector3.one, 0f);
            GameObject seamGA = NcBox(root, "Map4Audit_GNC_SeamG_A", new Vector3(558f, 0.5f, 6f), Vector3.one, 0f);
            GameObject seamGB = NcBox(root, "Map4Audit_GNC_SeamG_B", new Vector3(559.05f, 0.5f, 6f), Vector3.one, 0f);
            GameObject seamOA = NcBox(root, "Map4Audit_GNC_SeamO_A", new Vector3(558f, 0.5f, 12f), Vector3.one, 0f);
            GameObject seamOB = NcBox(root, "Map4Audit_GNC_SeamO_B", new Vector3(558.95f, 0.5f, 12f), Vector3.one, 0f);
            ncMesh = NcUnitCubeMesh();
            GameObject seamMA = NcConvexMesh(root, "Map4Audit_GNC_SeamM_A", new Vector3(558f, 0.5f, 18f), ncMesh);
            GameObject seamMB = NcConvexMesh(root, "Map4Audit_GNC_SeamM_B", new Vector3(559f, 0.5f, 18f), ncMesh);
            // ⑫ [INF3-3] S8: z=0 줄 Shelf 오른면 x=638.5 ↔ S8벽 왼면 x=640.0(1.5), z=10 줄 Table 오른면 x=638.5 ↔ S2벽 왼면 x=640.0(1.5).
            //    두 줄 사이 z 간격 6(벽 z 끝 2 ↔ 8) ≥ 2.5 — 같은 접두사끼리(Shelf↔Table 9, Table↔S8벽 7.5)는 보고 대상 아님. 바닥판 x 634~646, z −5~15.
            NcBox(root, "Map4Audit_GNC_Floor12", new Vector3(640f, -0.1f, 5f), new Vector3(12f, 0.2f, 20f), 0f);
            NcBox(root, "GEO_S8_Shelf_NC", new Vector3(638f, 0.5f, 0f), Vector3.one, 0f);
            NcBox(root, "GEO_S8_Wall_NC", new Vector3(640.5f, 1f, 0f), new Vector3(1f, 2f, 4f), 0f);
            NcBox(root, "GEO_S8_Table_NC", new Vector3(638f, 0.5f, 10f), Vector3.one, 0f);
            NcBox(root, "GEO_S2_Wall_NC_S8Mix", new Vector3(640.5f, 1f, 10f), new Vector3(1f, 2f, 4f), 0f);

            Result r = RunStaticAudit(); // 안에서 Physics.SyncTransforms() 호출.

            bool c1 = HasPair(r.pinchGaps, "Map4Audit_GNC_A", "Map4Audit_GNC_B", 0.5f);
            bool c2 = HasPair(r.pinchGaps, "Map4Audit_GNC_C", "Map4Audit_GNC_R30", 0.5f);
            bool c3 = HasPair(r.s1Violations, "GEO_S1_Obst_NC_Desk", "GEO_S1_Wall_NC", 1.5f)
                      && !HasPair(r.pinchGaps, "GEO_S1_Obst_NC_Desk", "GEO_S1_Wall_NC", -1f);
            bool c4 = !HasPair(r.pinchGaps, "Map4Audit_GNC_E", "Map4Audit_GNC_F", -1f)
                      && !HasPair(r.pinchExcluded, "Map4Audit_GNC_E", "Map4Audit_GNC_F", -1f)
                      && !HasPair(r.s1Violations, "Map4Audit_GNC_E", "Map4Audit_GNC_F", -1f);
            bool c5 = HasPairWhere(r.pinchGaps, "Map4Audit_GNC_G", "Map4Audit_GNC_H", 0.5f,
                                   x => x.filter == FilterOpen && x.movingNote != null && x.movingNote.Contains(TagMovingFilled));
            bool c6 = HasPairWhere(r.pinchExcluded, "Map4Audit_GNC_I", "Map4Audit_GNC_J", 0.5f, x => x.filter == FilterFilled)
                      && !HasPair(r.pinchGaps, "Map4Audit_GNC_I", "Map4Audit_GNC_J", -1f);
            // [INF3-2] ⑦~⑨ — ①~⑥ 기대값은 그대로.
            bool c7 = HasPair(r.s2Violations, "GEO_S2_Obst_NC_Shelf", "GEO_S2_Wall_NC", 1.5f)
                      && !HasPair(r.pinchGaps, "GEO_S2_Obst_NC_Shelf", "GEO_S2_Wall_NC", -1f)
                      && !HasPair(r.pinchExcluded, "GEO_S2_Obst_NC_Shelf", "GEO_S2_Wall_NC", -1f)
                      && !HasPair(r.s1Violations, "GEO_S2_Obst_NC_Shelf", "GEO_S2_Wall_NC", -1f);
            bool c8 = !InAnyGapList(r, "GEO_S2_Obst_NC_P", "GEO_S2_Obst_NC_Q")
                      && !InAnyGapList(r, "GEO_S2_Obst_NC_Q", "GEO_S2_Wall_NC_Touch")
                      && !InAnyGapList(r, "GEO_S2_Obst_NC_P", "GEO_S2_Wall_NC_Touch");
            bool c9 = !InAnyGapList(r, "GEO_S1_Obst_NC_Mix", "GEO_S2_Wall_NC_Mix")
                      && !InAnyGapList(r, "GEO_S2_Obst_NC_Mix", "GEO_S1_Wall_NC_Mix");
            // [INF3-3] ⑩~⑫ — ①~⑨ 기대값은 그대로.
            bool c10 = RunS3PathPureControl(out string c10Detail);

            SeamItem sT = MeasureSeam("NC", seamTA.GetComponent<Collider>(), seamTB.GetComponent<Collider>());
            SeamItem sG = MeasureSeam("NC", seamGA.GetComponent<Collider>(), seamGB.GetComponent<Collider>());
            SeamItem sO = MeasureSeam("NC", seamOA.GetComponent<Collider>(), seamOB.GetComponent<Collider>());
            SeamItem sM = MeasureSeam("NC", seamMA.GetComponent<Collider>(), seamMB.GetComponent<Collider>());
            bool meshNotUnverified = !r.unverifiedGapPairs.Exists(u => u.Contains("Map4Audit_GNC_SeamM_A") && u.Contains("Map4Audit_GNC_SeamM_B"));
            bool c11 = sT.measured && !sT.Violates
                       && sG.measured && sG.Violates && Mathf.Abs(sG.gap - 0.05f) < 0.01f
                       && sO.measured && sO.Violates && Mathf.Abs(sO.penetration - 0.05f) < 0.01f
                       && sM.measured && !sM.Violates && meshNotUnverified;
            string c11Detail = $"(맞닿음 {FormatSeamNc(sT)} / 틈 {FormatSeamNc(sG)} / 겹침 {FormatSeamNc(sO)} / 볼록 메시 {FormatSeamNc(sM)}" +
                               $"{(meshNotUnverified ? "" : " — 측정 불가 목록에 있음")})";

            List<GapItem> s8List = r.s8Violations ?? new List<GapItem>();
            bool c12 = HasPair(s8List, "GEO_S8_Shelf_NC", "GEO_S8_Wall_NC", 1.5f)
                       && !HasPair(r.pinchGaps, "GEO_S8_Shelf_NC", "GEO_S8_Wall_NC", -1f)
                       && !HasPair(r.pinchExcluded, "GEO_S8_Shelf_NC", "GEO_S8_Wall_NC", -1f)
                       && !HasPair(r.s1Violations, "GEO_S8_Shelf_NC", "GEO_S8_Wall_NC", -1f)
                       && !HasPair(r.s2Violations, "GEO_S8_Shelf_NC", "GEO_S8_Wall_NC", -1f)
                       && !InAnyGapList(r, "GEO_S8_Table_NC", "GEO_S2_Wall_NC_S8Mix");

            bool all = c1 && c2 && c3 && c4 && c5 && c6 && c7 && c8 && c9 && c10 && c11 && c12;

            detail = (all ? "검출 성공" : "검출 실패(회귀)") +
                     $" — ①0.5U 축정렬 {Mark(c1)} {DescribePair(r, "Map4Audit_GNC_A", "Map4Audit_GNC_B")}" +
                     $" · ②30° 회전 0.5U {Mark(c2)} {DescribePair(r, "Map4Audit_GNC_C", "Map4Audit_GNC_R30")}" +
                     $" · ③S1 1.5 위반 {Mark(c3)} {DescribePair(r, "GEO_S1_Obst_NC_Desk", "GEO_S1_Wall_NC")}" +
                     $" · ④1.2U 미검출 {Mark(c4)} {DescribePair(r, "Map4Audit_GNC_E", "Map4Audit_GNC_F")}" +
                     $" · ⑤가동체 메움 0.5U 보고 {Mark(c5)} {DescribePair(r, "Map4Audit_GNC_G", "Map4Audit_GNC_H")}" +
                     $" · ⑥정적 메움 0.5U 제외 {Mark(c6)} {DescribePair(r, "Map4Audit_GNC_I", "Map4Audit_GNC_J")}" +
                     $" · ⑦S2 1.5 위반 {Mark(c7)} {DescribePair(r, "GEO_S2_Obst_NC_Shelf", "GEO_S2_Wall_NC")}" +
                     $" · ⑧S2 2.5·맞닿음 0 미검출 {Mark(c8)} {DescribePair(r, "GEO_S2_Obst_NC_P", "GEO_S2_Obst_NC_Q")}" +
                     $" {DescribePair(r, "GEO_S2_Obst_NC_Q", "GEO_S2_Wall_NC_Touch")}" +
                     $" · ⑨S1↔S2 접두사 섞임 미검출 {Mark(c9)} {DescribePair(r, "GEO_S1_Obst_NC_Mix", "GEO_S2_Wall_NC_Mix")}" +
                     $" {DescribePair(r, "GEO_S2_Obst_NC_Mix", "GEO_S1_Wall_NC_Mix")}" +
                     $" · ⑩S3 경로 순수 함수 {Mark(c10)} {c10Detail}" +
                     $" · ⑪S4 이음매 {Mark(c11)} {c11Detail}" +
                     $" · ⑫S8 1.5 위반·섞임 미검출 {Mark(c12)} {DescribePair(r, "GEO_S8_Shelf_NC", "GEO_S8_Wall_NC")}" +
                     $" {DescribePair(r, "GEO_S8_Table_NC", "GEO_S2_Wall_NC_S8Mix")}";
            return all;
        }
        finally
        {
            Object.DestroyImmediate(root); // 자식 박스 전부 같이 정리.
            if (ncMesh != null) Object.DestroyImmediate(ncMesh); // [INF3-3] 임시 메시(에셋 아님) 정리.
            Physics.SyncTransforms();
        }
    }

    /// <summary>[INF3-3] ⑩ — EvaluateS3Path 순수 함수 대조(씬 물체 없음). 상자 = 폭 x 2·길이 z 2, 윗면 y = top.
    /// (가) [A, B]: 틈 1.5·턱 +0.8 → 위반 0, 직접 쌍 1.
    /// (나) [A, C, D, F]: A→C 틈 1.6(틈 위반), C→D 맞닿음 턱 +0.9(턱 위반), D→F 맞닿음 턱 −0.9(턱 위반, 내림) → 위반 3.
    /// (다) [A, 줄다리, E, 없음, 중복]: A↔E 줄다리 사이 1(제외, E는 8 떨어져 있어도 위반 아님), E→없음·없음→중복 판정 못 함 2,
    ///      이름 없음 1·중복 1, 위반 0.</summary>
    private static bool RunS3PathPureControl(out string detail)
    {
        Dictionary<string, List<Bounds>> map = new Dictionary<string, List<Bounds>>
        {
            { "NC3_A", new List<Bounds> { NcRect(0f, 0f) } },          // x 0~2, 윗면 0
            { "NC3_B", new List<Bounds> { NcRect(3.5f, 0.8f) } },      // x 3.5~5.5 → 틈 1.5, 턱 +0.8
            { "NC3_C", new List<Bounds> { NcRect(3.6f, 0f) } },        // x 3.6~5.6 → 틈 1.6
            { "NC3_D", new List<Bounds> { NcRect(5.6f, 0.9f) } },      // C와 맞닿음 → 턱 +0.9
            { "NC3_F", new List<Bounds> { NcRect(7.6f, 0f) } },        // D와 맞닿음 → 턱 −0.9
            { "NC3_E", new List<Bounds> { NcRect(10f, 0f) } },         // A와 틈 8 — 줄다리 사이라 판정 제외
            { "NC3_Dup", new List<Bounds> { NcRect(12f, 0f), NcRect(12f, 0f) } },
        };
        S3PathReport a = EvaluateS3Path(new[] { "NC3_A", "NC3_B" }, map);
        S3PathReport b = EvaluateS3Path(new[] { "NC3_A", "NC3_C", "NC3_D", "NC3_F" }, map);
        S3PathReport c = EvaluateS3Path(new[] { "NC3_A", S3BridgePrefix + "NC", "NC3_E", "NC3_Missing", "NC3_Dup" }, map);

        bool okA = a.violations.Count == 0 && a.directPairs == 1 && a.judgedPairs == 1 && a.missing.Count == 0;
        bool okB = b.violations.Count == 3 && b.judgedPairs == 3
                   && HasS3(b, "NC3_A", "NC3_C", true, false)
                   && HasS3(b, "NC3_C", "NC3_D", false, true)
                   && HasS3(b, "NC3_D", "NC3_F", false, true);
        bool okC = c.violations.Count == 0 && c.bridgeSkippedPairs == 1 && c.directPairs == 2 && c.judgedPairs == 0
                   && c.unjudgedPairs == 2 && c.missing.Count == 1 && c.missing[0] == "NC3_Missing"
                   && c.duplicates.Count == 1 && c.duplicates[0] == "NC3_Dup" && c.geoRowCount == 4;
        detail = $"((가) 위반 {a.violations.Count}/기대 0 {Mark(okA)} · (나) 위반 {b.violations.Count}/기대 3 {Mark(okB)} · " +
                 $"(다) 줄다리 제외 {c.bridgeSkippedPairs}/1·판정 못 함 {c.unjudgedPairs}/2·이름 없음 {c.missing.Count}/1·중복 {c.duplicates.Count}/1 {Mark(okC)})";
        return okA && okB && okC;
    }

    private static bool HasS3(S3PathReport rep, string na, string nb, bool gapBad, bool stepBad)
    {
        foreach (S3PathItem x in rep.violations)
            if (x.nameA == na && x.nameB == nb && x.gapBad == gapBad && x.stepBad == stepBad) return true;
        return false;
    }

    /// <summary>⑩용 합성 상자: x minX~minX+2, z 0~2, y top−0.2~top.</summary>
    private static Bounds NcRect(float minX, float top)
    {
        Bounds b = new Bounds();
        b.SetMinMax(new Vector3(minX, top - 0.2f, 0f), new Vector3(minX + 2f, top, 2f));
        return b;
    }

    private static string FormatSeamNc(SeamItem s)
    {
        if (!s.measured) return "측정 불가";
        return $"틈 {s.gap:F3}·겹침 {s.penetration:F3}{(s.Violates ? " 위반" : "")}";
    }

    /// <summary>[INF3-3] ⑪용 1U 정육면체 메시(중심 원점, ±0.5) — 에셋으로 저장하지 않는다(finally에서 DestroyImmediate).</summary>
    private static Mesh NcUnitCubeMesh()
    {
        Mesh m = new Mesh { name = "Map4Audit_GNC_Cube" };
        m.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
        };
        m.triangles = new[]
        {
            0, 2, 1, 0, 3, 2,   // −z
            4, 5, 6, 4, 6, 7,   // +z
            0, 1, 5, 0, 5, 4,   // −y
            3, 7, 6, 3, 6, 2,   // +y
            0, 4, 7, 0, 7, 3,   // −x
            1, 2, 6, 1, 6, 5,   // +x
        };
        m.RecalculateBounds();
        return m;
    }

    private static GameObject NcConvexMesh(GameObject root, string name, Vector3 localCenter, Mesh mesh)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = localCenter;
        MeshCollider mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = true;
        return go;
    }

    private static GameObject NcBox(GameObject root, string name, Vector3 localCenter, Vector3 size, float yawDeg)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = localCenter;
        go.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
        go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    /// <summary>expectedGap &lt; 0이면 값은 보지 않고 쌍 존재만 본다. 값 허용 오차 0.01U [추정].</summary>
    private static bool HasPair(List<GapItem> list, string n1, string n2, float expectedGap)
    {
        return HasPairWhere(list, n1, n2, expectedGap, null);
    }

    /// <summary>[INF3 R1] HasPair + 항목 조건(필터·꼬리표 확인용). cond == null이면 조건 없음.</summary>
    private static bool HasPairWhere(List<GapItem> list, string n1, string n2, float expectedGap,
                                     System.Predicate<GapItem> cond)
    {
        foreach (GapItem x in list)
        {
            bool same = (x.nameA == n1 && x.nameB == n2) || (x.nameA == n2 && x.nameB == n1);
            if (!same) continue;
            if (expectedGap >= 0f && Mathf.Abs(x.gap - expectedGap) >= 0.01f) continue;
            if (cond == null || cond(x)) return true;
        }
        return false;
    }

    /// <summary>[INF3-2] 쌍이 끼임·제외·S1 위반·S2 위반 목록 어디에든 있으면 true(값은 보지 않음).</summary>
    private static bool InAnyGapList(Result r, string n1, string n2)
    {
        return HasPair(r.pinchGaps, n1, n2, -1f) || HasPair(r.pinchExcluded, n1, n2, -1f)
               || HasPair(r.s1Violations, n1, n2, -1f) || HasPair(r.s2Violations, n1, n2, -1f)
               || (r.s8Violations != null && HasPair(r.s8Violations, n1, n2, -1f)); // [INF3-3] S8 목록도 본다(⑧·⑨는 더 엄격해질 뿐 기대값 불변).
    }

    private static string DescribePair(Result r, string n1, string n2)
    {
        List<GapItem> all = new List<GapItem>();
        all.AddRange(r.pinchGaps); all.AddRange(r.pinchExcluded); all.AddRange(r.s1Violations); all.AddRange(r.s2Violations);
        if (r.s8Violations != null) all.AddRange(r.s8Violations); // [INF3-3]
        foreach (GapItem x in all)
            if ((x.nameA == n1 && x.nameB == n2) || (x.nameA == n2 && x.nameB == n1))
                return $"(g={x.gap:F3} [{x.filter}]" + (x.movingNote != null ? $" {{{x.movingNote}}}" : "") + ")";
        return "(목록에 없음)";
    }

    private static string Mark(bool ok) => ok ? "✅" : "❌";

    /// <summary>[F1 재작업 판정 R2] 음성 대조 — 같은 씬 루트 아래 일부러 0.5U 겹치는 정적 박스
    /// 2개를 만들고 RunStaticAudit()이 그것을 실제로 잡는지 확인한다. 확인 후 즉시 정리한다.
    /// true = 검출 성공(감사 로직이 실제로 동작함을 증명), false = 검출 실패(회귀).</summary>
    public static bool RunNegativeControlTest(out string detail)
    {
        GameObject root = new GameObject("Map4Audit_NegativeControlRoot");
        GameObject a = new GameObject("Map4Audit_NC_A");
        GameObject b = new GameObject("Map4Audit_NC_B");
        try
        {
            a.transform.SetParent(root.transform, false);
            b.transform.SetParent(root.transform, false);
            a.transform.position = Vector3.zero;
            b.transform.position = new Vector3(0.5f, 0f, 0f); // 두 1U 박스가 0.5U 겹친다.
            BoxCollider ca = a.AddComponent<BoxCollider>();
            BoxCollider cb = b.AddComponent<BoxCollider>();
            ca.size = cb.size = Vector3.one;

            Result r = RunStaticAudit();
            bool caught = false;
            foreach (string d in r.overlapDetails)
                if (d.Contains("Map4Audit_NC_A") && d.Contains("Map4Audit_NC_B")) { caught = true; break; }

            detail = caught
                ? $"검출 성공 — 겹침 목록에 NC_A/NC_B 쌍 포함(전체 관통 {r.overlapCount}건 중)."
                : $"검출 실패(회귀) — 전체 관통 {r.overlapCount}건 중 NC_A/NC_B 쌍이 없다.";
            return caught;
        }
        finally
        {
            Object.DestroyImmediate(root); // a·b는 자식이라 같이 정리된다.
        }
    }
}
#endif
