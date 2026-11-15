#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 섹터3 "점프" 빌더 — 지형(과제 S3-B, R2) + ③′ 기믹 목록 모으기(과제 S3-B3, R3) + 레인 사이 벽·외벽 합침(S3-B3 수정 M3R1, [2차판정 12]).
/// 진행/초안/S3_배치초안.md(R3 초안 최종본 — S3-D3, [2차판정 1·12] 반영) §2 좌표표를 섹터 씬
/// Generated/ 아래에 짓는다. Map4SceneBuilder.BuildSectorScene이 SectorBuilderRegistry를 거쳐 S3일 때 빈 틀 대신 이것을
/// 부르고, 연결 통로(z 184~196)·마커는 공용 코드(BuildConnectorAndMarkers)가 이어서 만든다(1차 계약 C3).
///
/// [짓는 것 — 정적 162개, 초안 §2 :114] 걷는 면 107(= Map4Build.Floor 103 + L2 계단 2그룹 × 2단) · 외벽 12(합침) · 레인 사이 벽 15 ·
///   발사구 기둥·탄 받이 27 · 받침 바닥 1(= refs.catchFloor [2차판정 13]). refs.walls = 외벽 12 + 레인 사이 벽 15 = 27 [2차판정 12]. 천장 없음(허공 구조, 초안 §1 :24). 좌표는 전부 섹터 로컬(+Z 진행, 입구 발판 윗면 y 0).
///   아래 표(PathBoxes·WallBoxes·LaneWallBoxes·ColumnBoxes·CatchBox·StairStepExpected)는 `python S3_check.py --md` 출력을 스크립트로
///   기계 변환한 것이다(재타이핑 없음) — 값·순서 = 초안 §2 행 순서.
///
/// [이름 — R2 계약 C2-0·C2-3] 걷는 면 GEO_S3_Path_&lt;id&gt;, 계단 그룹 GEO_S3_Path_L2_StairsUp/Down + 자식 …_Step{i},
///   외벽 GEO_S3_Wall_&lt;id&gt;, 레인 사이 벽 GEO_S3_Wall_Lane_&lt;쌍&gt;[_조각](초안 id Lane_…), 기둥·탄 받이 GEO_S3_Col_&lt;id&gt;, 받침 바닥 GEO_S3_Floor_Catch. 시각물 자식 Visual → VIS_&lt;GEO_ 뗀 이름&gt;.
///   (과제 원문의 GEO_S3_Obst_는 S3 이름표에 없어 계약 C2-3의 GEO_S3_Col_을 따른다 — 보고서 reviewRequests.)
///
/// [계단 — 초안 C15 :529-530, 설계 §2-6] Map4Build.Stairs(g, baseMin, totalRise 1.6, totalRun 5, stepCount 2, widthZ 4).
///   오름: baseMin = 피벗 (−18.75, 0.8, 14.5), yaw 0. 내림: baseMin = 피벗 (4.5, 0, 18.5), 그룹 localRotation (0, 180, 0)
///   [구현 선택 — Stairs가 그룹 localPosition = baseMin으로 두므로 그룹을 제자리에서 돌리면 피벗이 곧 baseMin]. 결과 단 4개가
///   초안 §2 L2_01·L2_02(오름 Step0·1)·L2_06·L2_05(내림 Step0·1)와 ±0.02로 같은지 짓자마자 검사한다(StairStepExpected).
///
/// [기믹 목록 — R3 과제 S3-B3, 2차판정 2 (나)·4] 팀 기믹(발사구 14·줄다리 26·끝 고리 52)은 S3_Wiring이 만든다(이 파일은 만들지 않는다).
///   이 파일은 ③ Wire 뒤·④ Dress 앞의 ③′ CollectWireGimmicks에서 generated(S3_Gimmicks 우선) 아래 생성물을 이름으로 모아
///   refs.launchers·bridges·bridgeAnchors를 계약 C1-1 순서로 채우고 개수·순서를 다시 대조한다(LogError만 — throw 없음, 지형 유지).
///   id 순서표(LauncherIds·BridgeIds)는 초안 §2 발사구 표·줄다리 표에서 스크립트로 뽑았다(재타이핑 없음).
///   복귀 장치·장외 볼륨은 S3_Wiring, 머티리얼·조명·VIS_는 S3_Dress 몫(계약 C1-5·C1-6).
///
/// [2차판정 12 — 반영(S3-B3 수정 M3R1)] 허공 ≤ 9.26인 이웃 레인 쌍 12/12에 레인 사이 벽 15조각(LaneWallBoxes), 외벽 42조각 → 12조각
///   (WallBoxes — 옆벽 1조각씩, 끝벽은 통로·발판 구멍 조각만). 두 표 모두 초안 최종본(S3-D3) `python S3_check.py --md` 출력의 외벽·레인벽 27행을
///   스크립트로 기계 변환했다(재타이핑 없음). 합친 외벽 한 높이의 카메라 가림(초안 C7 판정 대기 — §7 판정 요청 8)은 판정 전이라 초안 값 그대로다.
///
/// [예외 — R2 계약 C5(판정 8)] 전제 불일치면 아무것도 만들지 않고 false. 지형 단계 예외면 Generated를 비우고 Build 중 새로 생긴
///   씬 루트를 지운 뒤 false. Wire·③′·Dress 예외는 LogError만 하고 지형을 남긴다. 정적 필드 g는 모든 경로에서 null로 돌린다.
///
/// 출처 표기: [확정] 설계서 사용자 확정 · [판정] 판정 문서 · [계약] R2/1차 계약 · [제안] 초안 제안값 · [계산] 계산값 · [추정] 실측 필요.
/// </summary>
public static class S3_Builder
{
    // ── 전제 (R2 계약 C5 · Map4Layout.asset · 설계 S3 :10) ──
    private const float SectorWidth = 56f;     // [계약 C5] S3 폭 56
    private const float SectorLength = 184f;   // [계약 C5] 길이 184
    private const float FloorHeight = 0f;      // [계약 C5] 바닥 높이 0
    private const float ExitHeight = 0f;       // [계약 C5] 출구 높이 0(들어올 때·나갈 때 0 → 0, 설계 §2-10)
    private const float PreconditionTolerance = 0.01f; // [계약 C5] ±0.01

    // ── L2 계단 (초안 C15 :529-530, [확정 §2-6] Map4Build.Stairs) ──
    private static readonly Vector3 StairsUpPivot = new Vector3(-18.75f, 0.8f, 14.5f); // [제안 초안 C15] 오름 피벗, yaw 0
    private static readonly Vector3 StairsDownPivot = new Vector3(4.5f, 0f, 18.5f);    // [제안 초안 C15] 내림 피벗, yaw 180
    private const float StairsUpYaw = 0f;          // [제안 초안 C15]
    private const float StairsDownYaw = 180f;      // [제안 초안 C15]
    private const float StairsTotalRise = 1.6f;    // [제안 초안 C15] 단 높이 0.8 × 2 [계산]
    private const float StairsTotalRun = 5f;       // [제안 초안 C15] 디딤 2.5 × 2 [계산]
    private const int StairsStepCount = 2;         // [제안 초안 C15]
    private const float StairsWidthZ = 4f;         // [제안 초안 C15] 레인 깊이 4
    private const string StairsUpName = "GEO_S3_Path_L2_StairsUp";     // [계약 C2-3]
    private const string StairsDownName = "GEO_S3_Path_L2_StairsDown"; // [계약 C2-3]
    private const float StairsCheckTolerance = 0.02f; // [계약 — 지시서 S3-B 수치표 "±0.02"]

    // ── Refs 지형 필드 (R2 계약 C1-3) ──
    private const string StartPadId = "P_start";   // [계약 C1-3] startPad = GEO_S3_Path_P_start
    private const string SavePadId = "B1_P1";      // [계약 C1-3] savePad = GEO_S3_Path_B1_P1
    // OOB 아랫면 −13 [2차판정 1 · 계약 C1-3·R3-S3] — 중심 y −8·크기 10·윗면 −3 [계산]. 옛 −12는 받침 윗면(catchFloorTop −12)과 같은 면이라
    // 받침 위 세모 루트(−12.211)가 볼륨 밖이었다(M2R1, 검증/M2R2_generate.log:5072·:5091). 필요 조건 아랫면 ≤ −12.311을 여유 0.789로 충족한다.
    // S3_Wiring VoidDraft와 대조된다(S3-W 몫) — 진단 LogError는 강등하지 않는다 [2차판정 1].
    private static readonly Vector3 VoidMin = new Vector3(-28f, -13f, 0f);  // [2차판정 1] OOB_S3 min = [계약 C1-3] voidVolume
    private static readonly Vector3 VoidMax = new Vector3(28f, -3f, 184f);  // [제안 초안 :328 · 계약 C1-3] OOB_S3 max

    // ── 개수 (초안 §2 :114, 지시서 S3-B 수치표 · [2차판정 12]) ──
    private const int ExpectedPathFloors = 103;  // 걷는 면 107 − 계단 4단
    private const int ExpectedOuterWalls = 12;   // [2차판정 12 · 초안 §2 :114] 합친 외벽(옆벽 2 + 끝벽 5×2) — 합치기 전 42
    private const int ExpectedLaneWalls = 15;    // [2차판정 12 · 초안 §2 :114] 레인 사이 벽 12곳 15조각
    private const int ExpectedWalls = ExpectedOuterWalls + ExpectedLaneWalls; // 27 = refs.walls [계산]
    private const string WallNamePrefix = "GEO_S3_Wall_";           // [계약 C2-3] 외벽 GEO_S3_Wall_<id>
    private const string LaneWallNamePrefix = "GEO_S3_Wall_Lane_";  // [계약 C2-3] 레인 사이 벽 GEO_S3_Wall_Lane_<id> (초안 id가 Lane_로 시작)
    private const int ExpectedColumns = 27;

    // ── ③′ 기믹 목록 [계약 C1-1·C2-3, 2차판정 2 (나)] — 이름 = 접두사 + 초안 id. Wire 생성 이름과 같다(S3_Wiring.cs LauncherPrefix·BridgePrefix) ──
    private const string GimmickGroupName = "S3_Gimmicks";   // [계약 C1-0]
    private const string LauncherPrefix = "S3_Launcher_";    // [계약 C2-3]
    private const string BridgePrefix = "S3_Bridge_";        // [계약 C2-3]
    private const string AnchorSuffixA = "_A", AnchorSuffixB = "_B"; // [계약 C2-3] 끝 고리 S3_Bridge_<id>_A/_B

    /// <summary>발사구 14 — 초안 §2 발사구 표 행 순서 = 계약 C1-1 launchers 순서 [제안 초안 :312-325](스크립트 추출).</summary>
    private static readonly string[] LauncherIds =
    {
        "PL_L1_01", "PL_L2_04", "PL_L2_07", "PL_L3_02", "PL_L3_08", "PL_L4_02", "PL_L4_03",
        "PL_L4_06", "PL_L5_02", "PL_L5_05", "PL_L5_07", "PL_L6_03", "PL_L6_07", "PL_L6_08",
    };

    /// <summary>줄다리 26 — 초안 §2 줄다리 표 = 경로 순서 = 계약 C1-1 bridges 순서 [제안 초안 :283-308](스크립트 추출).
    /// bridgeAnchors 52는 이 순서로 [A,B] 번갈아(i*2 = _A, i*2+1 = _B).</summary>
    private static readonly string[] BridgeIds =
    {
        "BR_B1_1", "BR_B1_2", "BR_B1_3", "BR_B1_turn",
        "BR_B2_1", "BR_B2_2", "BR_B2_3", "BR_B2_turn",
        "BR_B3_1", "BR_B3_2", "BR_B3_3", "BR_B3_turn",
        "BR_B4_1", "BR_B4_2", "BR_B4_3", "BR_B4_turn",
        "BR_B5_1", "BR_B5_2", "BR_B5_3", "BR_B5_turn",
        "BR_B6_1", "BR_B6_2", "BR_B6_3", "BR_B6_turn",
        "BR_B7_1", "BR_B7_2",
    };
    private const int ExpectedLaunchers = 14;   // [계약 C1-1]
    private const int ExpectedBridges = 26;     // [계약 C1-1]
    private const int ExpectedAnchors = 52;     // [계약 C1-1] = 26 × 2
    private const string CatchFloorName = "GEO_S3_Floor_Catch"; // [계약 C1-1·C2-3]

    private struct Box
    {
        public readonly string id;
        public readonly Vector3 min, max;
        public Box(string id, Vector3 min, Vector3 max) { this.id = id; this.min = min; this.max = max; }
    }

    private static Box B(string id, float x0, float y0, float z0, float x1, float y1, float z1) =>
        new Box(id, new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));

    // ───────────── 좌표표 [제안 초안 §2 :112-279] — S3_check.py --md 출력에서 기계 변환(재타이핑 없음) ─────────────

    /// <summary>걷는 면 103개(계단 4단 L2_01·L2_02·L2_05·L2_06 제외) → Map4Build.Floor → GEO_S3_Path_&lt;id&gt;. 초안 §2 행 순서.</summary>
    private static readonly Box[] PathBoxes =
    {
        B("P_start", -5f, -1f, 0f, 5f, 0f, 6f),
        B("L1_00", -9.25f, -0.2f, 3.5f, -6f, 0.8f, 7.5f),
        B("L1_01", -13.75f, -1f, 3.5f, -10.75f, 0f, 7.5f),
        B("L1_02", -18.75f, -1f, 3.5f, -15f, 0f, 7.5f),
        B("L1_03", -24f, -1f, 3.5f, -20f, 0f, 7.5f),
        B("L2_00", -23.5f, -0.2f, 14.5f, -19.75f, 0.8f, 18.5f),
        B("L2_03", -12.5f, 1.4f, 14.5f, -6.25f, 2.4f, 18.5f),
        B("L2_04", -4.75f, 1.4f, 14.5f, -1.75f, 2.4f, 18.5f),
        B("L2_07", 6f, -0.2f, 14.5f, 9f, 0.8f, 18.5f),
        B("L2_08", 10.25f, -0.2f, 14.5f, 14.5f, 0.8f, 18.5f),
        B("L2_09", 15.75f, -0.2f, 14.5f, 18.75f, 0.8f, 18.5f),
        B("L2_10", 20f, -0.2f, 14.5f, 24f, 0.8f, 18.5f),
        B("L3_00", 20f, 0.6f, 27f, 23.5f, 1.6f, 31f),
        B("L3_01", 15.75f, 0.6f, 27f, 18.75f, 1.6f, 31f),
        B("L3_02", 11.25f, 0.6f, 27f, 14.25f, 1.6f, 31f),
        B("L3_03", 7.5f, 0.6f, 27f, 10f, 1.6f, 31f),
        B("L3_04", 5.5f, 0.6f, 27f, 7.5f, 2.4f, 31f),
        B("L3_05", 3.5f, 0.6f, 27f, 5.5f, 3.2f, 31f),
        B("L3_06", 0.5f, 0.6f, 27f, 3.5f, 2.4f, 31f),
        B("L3_07", -2f, 0.6f, 27f, 0.5f, 1.6f, 31f),
        B("L3_08", -6.5f, 0.6f, 27f, -3.5f, 1.6f, 31f),
        B("L3_09", -10.75f, 1.4f, 27f, -7.5f, 2.4f, 31f),
        B("L3_10", -15f, 1.4f, 27f, -12f, 2.4f, 31f),
        B("L3_11", -18.75f, 0.6f, 27f, -16.25f, 1.6f, 31f),
        B("L3_12", -24f, 0.6f, 27f, -20f, 1.6f, 31f),
        B("L4_00", -23.5f, 1.4f, 39.5f, -20f, 2.4f, 43.5f),
        B("L4_01", -18.75f, 1.4f, 39.5f, -15.75f, 2.4f, 43.5f),
        B("L4_02", -14.25f, 1.4f, 39.5f, -11.25f, 2.4f, 43.5f),
        B("L4_03", -9.75f, 1.4f, 39.5f, -6.75f, 2.4f, 43.5f),
        B("L4_04", -5.75f, 1.4f, 39.5f, -2.5f, 3.2f, 43.5f),
        B("L4_05", -1.25f, 1.4f, 39.5f, 1.75f, 3.2f, 43.5f),
        B("L4_06", 3.25f, 2.2f, 39.5f, 6.25f, 3.2f, 43.5f),
        B("L4_07", 7.5f, 1.4f, 39.5f, 10.5f, 2.4f, 43.5f),
        B("L4_08", 11.75f, 1.4f, 39.5f, 18.75f, 2.4f, 43.5f),
        B("L4_09", 20f, 1.4f, 39.5f, 24f, 2.4f, 43.5f),
        B("L5_00", 20f, 1.4f, 52f, 23.5f, 2.4f, 56f),
        B("L5_01", 10.75f, 1.4f, 52f, 18.75f, 2.4f, 56f),
        B("L5_02", 6.25f, 1.4f, 52f, 9.25f, 2.4f, 56f),
        B("L5_03", 3f, 1.4f, 52f, 5.25f, 3.2f, 56f),
        B("L5_04", 0f, 1.4f, 52f, 3f, 2.4f, 56f),
        B("L5_05", -4.5f, 1.4f, 52f, -1.5f, 2.4f, 56f),
        B("L5_06", -8.75f, 1.4f, 52f, -5.75f, 2.4f, 56f),
        B("L5_07", -13.25f, 1.4f, 52f, -10.25f, 2.4f, 56f),
        B("L5_08", -18.75f, 1.4f, 52f, -14.5f, 2.4f, 56f),
        B("L5_09", -24f, 1.4f, 52f, -20f, 2.4f, 56f),
        B("L6_00", -23.5f, 1.4f, 64.5f, -20f, 2.4f, 68.5f),
        B("L6_01", -18.75f, 1.4f, 64.5f, -15.75f, 2.4f, 68.5f),
        B("L6_02", -14.5f, 1.4f, 64.5f, -11.5f, 2.4f, 68.5f),
        B("L6_03", -10f, 1.4f, 64.5f, -7f, 2.4f, 68.5f),
        B("L6_04", -6f, 1.4f, 64.5f, -2.75f, 3.2f, 68.5f),
        B("L6_05", -1.5f, 1.4f, 64.5f, 1.5f, 3.2f, 68.5f),
        B("L6_06", 2.75f, 1.4f, 64.5f, 5.75f, 2.4f, 68.5f),
        B("L6_07", 7.25f, 1.4f, 64.5f, 10.25f, 2.4f, 68.5f),
        B("L6_08", 11.75f, 1.4f, 64.5f, 14.75f, 2.4f, 68.5f),
        B("L6_09", 16f, 1.4f, 64.5f, 18.75f, 2.4f, 68.5f),
        B("L6_10", 20f, 1.4f, 64.5f, 24f, 2.4f, 68.5f),
        B("T1_b1", -24f, -0.6f, 8.75f, -20f, 0.4f, 10.5f),
        B("T1_b2", -24f, -0.2f, 11.75f, -20f, 0.8f, 13.25f),
        B("T2_b1", 20f, 0.2f, 19.75f, 24f, 1.2f, 22.25f),
        B("T2_b2", 20f, 0.6f, 23.5f, 24f, 1.6f, 25.75f),
        B("T3_b1", -24f, 1f, 32.25f, -20f, 2f, 34.75f),
        B("T3_b2", -24f, 1.4f, 36f, -20f, 2.4f, 38.25f),
        B("T4_b1", 20f, 1.4f, 44.75f, 24f, 2.4f, 47.25f),
        B("T4_b2", 20f, 1.4f, 48.5f, 24f, 2.4f, 50.75f),
        B("T5_b1", -24f, 1.4f, 57.25f, -20f, 2.4f, 59.75f),
        B("T5_b2", -24f, 1.4f, 61f, -20f, 2.4f, 63.25f),
        B("T6_b1", 20f, 1.4f, 69.75f, 24f, 2.4f, 72.25f),
        B("T6_b2", 20f, 1.4f, 73.5f, 24f, 2.4f, 75.75f),
        B("SC_ledge", 12f, 1.4f, 43.5f, 15f, 2.4f, 47.6f),
        B("B1_P1", 18f, 1.4f, 77f, 24f, 2.4f, 82f),
        B("B1_P2", 4f, 1.4f, 77f, 10f, 2.4f, 82f),
        B("B1_P3", -10f, 1.4f, 77f, -4f, 2.4f, 82f),
        B("B1_P4a", -21f, 1.4f, 77f, -18f, 2.4f, 82f),
        B("B1_P4b", -24f, 1.4f, 77f, -21f, 1.6f, 82f),
        B("B2_P1", -24f, 0.6f, 90.5f, -18f, 1.6f, 95.5f),
        B("B2_P2", -10f, 0.6f, 90.5f, -4f, 1.6f, 95.5f),
        B("B2_P3", 4f, 0.6f, 90.5f, 10f, 1.6f, 95.5f),
        B("B2_P4", 18f, 0.6f, 90.5f, 24f, 1.6f, 95.5f),
        B("B3_P1", 18f, 0.6f, 104f, 24f, 1.6f, 109f),
        B("B3_P2", 4f, 0.6f, 104f, 10f, 1.6f, 109f),
        B("B3_P3", -10f, 0.6f, 104f, -4f, 1.6f, 109f),
        B("B3_P4a", -21f, 0.6f, 104f, -18f, 1.6f, 109f),
        B("B3_P4b", -24f, 0.6f, 104f, -21f, 0.8f, 109f),
        B("B4_P1", -24f, -0.2f, 117.5f, -18f, 0.8f, 122.5f),
        B("B4_P2", -10f, -0.2f, 117.5f, -4f, 0.8f, 122.5f),
        B("B4_P3", 4f, -0.2f, 117.5f, 10f, 0.8f, 122.5f),
        B("B4_P4", 18f, -0.2f, 117.5f, 24f, 0.8f, 122.5f),
        B("B5_P1", 18f, -0.2f, 131f, 24f, 0.8f, 136f),
        B("B5_P2", 4f, -0.2f, 131f, 10f, 0.8f, 136f),
        B("B5_P3", -10f, -0.2f, 131f, -4f, 0.8f, 136f),
        B("B5_P4a", -21f, -0.2f, 131f, -18f, 0.8f, 136f),
        B("B5_P4b", -24f, -0.2f, 131f, -21f, 0f, 136f),
        B("B6_P1", -24f, -1f, 144.5f, -18f, 0f, 149.5f),
        B("B6_P2", -10f, -1f, 144.5f, -4f, 0f, 149.5f),
        B("B6_P3", 4f, -1f, 144.5f, 10f, 0f, 149.5f),
        B("B6_P4", 18f, -1f, 144.5f, 24f, 0f, 149.5f),
        B("B7_P1", 18f, -1f, 158f, 24f, 0f, 163f),
        B("B7_P2", 4f, -1f, 158f, 10f, 0f, 163f),
        B("B7_P3", -10f, -1f, 158f, -4f, 0f, 163f),
        B("TX_b1", -9f, -1f, 164.25f, -5f, 0f, 166.75f),
        B("TX_b2", -9f, -1f, 168f, -5f, 0f, 170.5f),
        B("TX_b3", -9f, -1f, 171.75f, -5f, 0f, 174.75f),
        B("P_exit", -10f, -1f, 176f, 5f, 0f, 184f),
    };

    /// <summary>외벽 12조각 [2차판정 12 · F1-3 "한 벽면 = 콜라이더 1개, 조각은 문·통로 구멍만"] → Map4Build.Wall → GEO_S3_Wall_&lt;id&gt;.
    /// 옆벽 W_side_W·W_side_E 1조각씩(윗면 4.65), 끝벽 남·북 각 5조각(_L·_LP·_RP·_R·_B — 통로·발판 구멍으로만 끊김, 윗면 3.05·2.25).
    /// 두께 0.2 [제안 초안 §1-1 :39 · §2 :225-236]. 초안 §2 행 순서.</summary>
    private static readonly Box[] WallBoxes =
    {
        B("W_side_W", -28f, -12f, 0f, -27.8f, 4.65f, 184f),
        B("W_side_E", 27.8f, -12f, 0f, 28f, 4.65f, 184f),
        B("W_end_S_L", -27.8f, -12f, 0f, -5f, 3.05f, 0.2f),
        B("W_end_S_LP", -5f, 0f, 0f, -4f, 3.05f, 0.2f),
        B("W_end_S_RP", 4f, 0f, 0f, 5f, 3.05f, 0.2f),
        B("W_end_S_R", 5f, -12f, 0f, 27.8f, 3.05f, 0.2f),
        B("W_end_S_B", -5f, -12f, 0f, 5f, -1f, 0.2f),
        B("W_end_N_L", -27.8f, -12f, 183.8f, -10f, 2.25f, 184f),
        B("W_end_N_LP", -10f, 0f, 183.8f, -4f, 2.25f, 184f),
        B("W_end_N_RP", 4f, 0f, 183.8f, 5f, 2.25f, 184f),
        B("W_end_N_R", 5f, -12f, 183.8f, 27.8f, 2.25f, 184f),
        B("W_end_N_B", -10f, -12f, 183.8f, 5f, -1f, 184f),
    };

    /// <summary>레인 사이 벽 12곳 15조각 [2차판정 12 — 허공 ≤ 9.26인 이웃 레인 쌍 전부] → Map4Build.Wall → GEO_S3_Wall_&lt;id&gt;
    /// (id가 Lane_&lt;쌍&gt;[_조각]이라 이름 = 계약 GEO_S3_Wall_Lane_&lt;쌍&gt;[_조각], 예 GEO_S3_Wall_Lane_L6B1_far [계약 C2-3 · 초안 §2 :114]).
    /// 두께 0.2, 받침 윗면 −12에서 선다. 윗면 2.25~5.45 = 이웃 걷는 면 최대 + 세모 점프 2.0 [팀 SHAPES] + 0.2 [제안 초안 §1-1 :40 · §2 :265-279]. 초안 §2 행 순서.</summary>
    private static readonly Box[] LaneWallBoxes =
    {
        B("Lane_L1L2_a", -19f, -12f, 10.8f, -10.5f, 4.65f, 11f),
        B("Lane_L1L2_b", -9.5f, -12f, 10.8f, 19f, 4.65f, 11f),
        B("Lane_L2L3", -19f, -12f, 22f, 19f, 5.4f, 22.2f),
        B("Lane_L3L4", -19f, -12f, 34.5f, 19f, 5.45f, 34.7f),
        B("Lane_L4L5", -19f, -12f, 47f, 11f, 5.45f, 47.2f),
        B("Lane_L5L6", -19f, -12f, 59.5f, 19f, 5.45f, 59.7f),
        B("Lane_L6B1_far", -27.8f, -12f, 69.5f, -12.25f, 5.4f, 69.7f),
        B("Lane_L6B1_jog", -12.45f, -12f, 69.7f, -12.25f, 5.4f, 73.7f),
        B("Lane_L6B1", -12.45f, -12f, 73.7f, 19f, 5.4f, 73.9f),
        B("Lane_B1B2", -21.5f, -12f, 83f, 27.8f, 4.65f, 83.2f),
        B("Lane_B2B3", -27.8f, -12f, 96.5f, 21.5f, 3.85f, 96.7f),
        B("Lane_B3B4", -21.5f, -12f, 110f, 27.8f, 3.85f, 110.2f),
        B("Lane_B4B5", -27.8f, -12f, 123.5f, 21.5f, 3.05f, 123.7f),
        B("Lane_B5B6", -21.5f, -12f, 137f, 27.8f, 3.05f, 137.2f),
        B("Lane_B6B7", -21.5f, -12f, 150.5f, 21.5f, 2.25f, 150.7f),
    };

    /// <summary>발사구 기둥·탄 받이 27개 → Map4Build.Wall → GEO_S3_Col_&lt;id&gt;. 1×1, 바닥 −12, 윗면 4.9~5.7 [제안 초안 §2 :238-264].</summary>
    private static readonly Box[] ColumnBoxes =
    {
        B("COL_L1_01_N", -10.5f, -12f, 10f, -9.5f, 4.9f, 11f),
        B("COL_L2_04_N", -6f, -12f, 21f, -5f, 4.9f, 22f),
        B("COL_L2_04_S", -6f, -12f, 11f, -5f, 4.9f, 12f),
        B("COL_L2_07_N", 4.75f, -12f, 21f, 5.75f, 5.7f, 22f),
        B("COL_L2_07_S", 4.75f, -12f, 11f, 5.75f, 4.9f, 12f),
        B("COL_L3_02_N", 14.5f, -12f, 33.5f, 15.5f, 4.9f, 34.5f),
        B("COL_L3_02_S", 14.5f, -12f, 23.5f, 15.5f, 4.9f, 24.5f),
        B("COL_L3_08_N", -3.25f, -12f, 33.5f, -2.25f, 5.7f, 34.5f),
        B("COL_L3_08_S", -3.25f, -12f, 23.5f, -2.25f, 5.7f, 24.5f),
        B("COL_L4_02_N", -15.5f, -12f, 46f, -14.5f, 4.9f, 47f),
        B("COL_L4_02_S", -15.5f, -12f, 36f, -14.5f, 4.9f, 37f),
        B("COL_L4_03_N", -11f, -12f, 46f, -10f, 5.7f, 47f),
        B("COL_L4_03_S", -11f, -12f, 36f, -10f, 5.7f, 37f),
        B("COL_L4_06_N", 2f, -12f, 46f, 3f, 5.7f, 47f),
        B("COL_L4_06_S", 2f, -12f, 36f, 3f, 5.7f, 37f),
        B("COL_L5_02_N", 9.5f, -12f, 58.5f, 10.5f, 5.7f, 59.5f),
        B("COL_L5_02_S", 9.5f, -12f, 48.5f, 10.5f, 5.7f, 49.5f),
        B("COL_L5_05_N", -1.25f, -12f, 58.5f, -0.25f, 5.7f, 59.5f),
        B("COL_L5_05_S", -1.25f, -12f, 48.5f, -0.25f, 5.7f, 49.5f),
        B("COL_L5_07_N", -10f, -12f, 58.5f, -9f, 5.7f, 59.5f),
        B("COL_L5_07_S", -10f, -12f, 48.5f, -9f, 5.7f, 49.5f),
        B("COL_L6_03_N", -11.25f, -12f, 71f, -10.25f, 5.7f, 72f),
        B("COL_L6_03_S", -11.25f, -12f, 61f, -10.25f, 5.7f, 62f),
        B("COL_L6_07_N", 6f, -12f, 71f, 7f, 5.7f, 72f),
        B("COL_L6_07_S", 6f, -12f, 61f, 7f, 5.7f, 62f),
        B("COL_L6_08_N", 10.5f, -12f, 71f, 11.5f, 4.9f, 72f),
        B("COL_L6_08_S", 10.5f, -12f, 61f, 11.5f, 5.7f, 62f),
    };

    /// <summary>받침 바닥 F_catch → Map4Build.Floor → GEO_S3_Floor_Catch [제안 초안 :237]. 윗면 = Refs.catchFloorTop.</summary>
    private static readonly Box CatchBox =
        B("F_catch", -28f, -13f, 0f, 28f, -12f, 184f);

    /// <summary>계단 단 4개의 기대 상자(초안 §2 :124-129) — 순서: 오름 Step0·Step1, 내림 Step0·Step1. 짓고 나서 ±0.02 대조.</summary>
    private static readonly Box[] StairStepExpected =
    {
        B("L2_01", -18.75f, 0.8f, 14.5f, -16.25f, 1.6f, 18.5f),
        B("L2_02", -16.25f, 0.8f, 14.5f, -13.75f, 2.4f, 18.5f),
        B("L2_06", 2f, 0f, 14.5f, 4.5f, 0.8f, 18.5f),
        B("L2_05", -0.5f, 0f, 14.5f, 2f, 1.6f, 18.5f),
    };

    private static Transform g; // Build 동안만 유효 — Generated 루트

    /// <summary>S3를 짓는다(R2 계약 C1-0 호출 순서 · C5 예외 표준). 전제(폭 56·길이 184·바닥 0·출구 0, ±0.01)와 다르면 아무것도
    /// 짓지 않고 false — 호출자가 빈 틀로 되돌아간다.</summary>
    public static bool Build(Transform generated, Map4Layout.SectorDef def)
    {
        // (1) 전제 검사 — 맨 앞. 아무것도 만들기 전에.
        if (!PreconditionsOk(def))
        {
            Debug.LogError(def == null
                ? "[S3_Builder] 전제 불일치 — SectorDef가 null이다. S3 구체화를 건너뛰고 빈 틀로 짓는다."
                : $"[S3_Builder] 전제 불일치 — 폭 {def.width}·길이 {def.length}·바닥 {def.floorHeight}·출구 {def.exitHeight}" +
                  $"(전제 {SectorWidth}·{SectorLength}·{FloorHeight}·{ExitHeight}, ±{PreconditionTolerance}). S3 구체화를 건너뛰고 빈 틀로 짓는다.");
            return false;
        }

        g = generated;
        var before = new HashSet<GameObject>(generated.gameObject.scene.GetRootGameObjects());
        S3_Refs refs = new S3_Refs { generated = generated };
        try
        {
            Map4Build.BeginSection();
            // 지형 → (팀 기믹은 Wire가 만든다 [2차판정 4]) → Refs 채우기 → 지형 Refs 검증. Map4Build가 null을 돌려주면 즉시 throw.
            BuildPaths(refs);
            BuildStairs(refs);
            BuildWalls(refs);
            BuildColumns(refs);
            BuildCatchFloor(refs);
            refs.voidVolume = MinMax(VoidMin, VoidMax);
            ValidateTerrainRefs(refs);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[S3_Builder] 섹터3 구체화 실패 — Generated를 비우고 빈 틀로 되돌린다: {e.Message}\n{e.StackTrace}");
            for (int i = generated.childCount - 1; i >= 0; i--) Object.DestroyImmediate(generated.GetChild(i).gameObject);
            foreach (GameObject r in generated.gameObject.scene.GetRootGameObjects())
                if (!before.Contains(r)) Object.DestroyImmediate(r);   // 이번 Build가 팀 메뉴로 만든 루트 잔류물
            g = null; return false;
        }
        try { S3_Wiring.Wire(generated, refs); } catch (System.Exception e) { Debug.LogError($"[S3_Builder] Wire 예외 — 지형 유지: {e}"); }
        // ③′ [2차판정 2 (나) · 계약 C1-0] Wire 생성물을 모아 refs 기믹 목록을 채운다 — 개수·순서 불일치는 LogError, 지형 유지.
        try { CollectWireGimmicks(generated, refs); } catch (System.Exception e) { Debug.LogError($"[S3_Builder] ③′ 기믹 목록 모으기 예외 — 지형 유지: {e}"); }
        try { S3_Dress.Apply(generated, refs); } catch (System.Exception e) { Debug.LogError($"[S3_Builder] Dress 예외 — 지형 유지: {e}"); }
        ReportStrayRoots();
        g = null; return true;
    }

    private static bool PreconditionsOk(Map4Layout.SectorDef def) =>
        def != null
        && Mathf.Abs(def.width - SectorWidth) <= PreconditionTolerance
        && Mathf.Abs(def.length - SectorLength) <= PreconditionTolerance
        && Mathf.Abs(def.floorHeight - FloorHeight) <= PreconditionTolerance
        && Mathf.Abs(def.exitHeight - ExitHeight) <= PreconditionTolerance;

    // ───────────────────────── 지형 ─────────────────────────

    /// <summary>걷는 면 103개. 앞(P_start·L1~L6·T*_b*·SC_ledge) → refs.frontPath, 뒤(B1~B7·TX_b*·P_exit) → refs.backPath [계약 C1-3 주석].
    /// startPad·savePad는 같은 상자 값으로 채운다.</summary>
    private static void BuildPaths(S3_Refs refs)
    {
        foreach (Box b in PathBoxes)
        {
            Transform t = Solid(Map4Build.Floor(g, b.min, b.max), "GEO_S3_Path_" + b.id);
            (IsBackPath(b.id) ? refs.backPath : refs.frontPath).Add(t);
            if (b.id == StartPadId) refs.startPad = MinMax(b.min, b.max);
            if (b.id == SavePadId) refs.savePad = MinMax(b.min, b.max);
        }
    }

    /// <summary>뒤 구간 걷는 면: B1~B7 발판, 출구 연결 블록 TX_b*, 출구 발판 P_exit [계약 C1-3 주석].</summary>
    private static bool IsBackPath(string id) => id.StartsWith("B", System.StringComparison.Ordinal) || id.StartsWith("TX_", System.StringComparison.Ordinal) || id == "P_exit";

    /// <summary>L2 계단 2그룹(오름·내림) — 그룹 Transform을 refs.frontPath에 넣는다 [계약 C1-3 주석 "L2 계단 그룹"].</summary>
    private static void BuildStairs(S3_Refs refs)
    {
        Transform up = StairsGroup(StairsUpName, StairsUpPivot, StairsUpYaw);
        Transform down = StairsGroup(StairsDownName, StairsDownPivot, StairsDownYaw);
        CheckStep(up, 0, StairStepExpected[0]);
        CheckStep(up, 1, StairStepExpected[1]);
        CheckStep(down, 0, StairStepExpected[2]);
        CheckStep(down, 1, StairStepExpected[3]);
        refs.frontPath.Add(up);
        refs.frontPath.Add(down);
    }

    /// <summary>Map4Build.Stairs(단 i = 로컬 x i·run~(i+1)·run, y 0~(i+1)·rise, z 0~widthZ) → 그룹을 yaw만큼 제자리에서 돌리고
    /// 이름을 계약대로 바꾼다(그룹 이름, 자식 GEO_Step_i → &lt;그룹&gt;_Step{i}, 그 Visual → VIS_…). null이면 즉시 throw.</summary>
    private static Transform StairsGroup(string name, Vector3 pivot, float yawDeg)
    {
        Transform grp = Map4Build.Stairs(g, pivot, StairsTotalRise, StairsTotalRun, StairsStepCount, StairsWidthZ);
        if (grp == null) throw new System.Exception($"[S3_Builder] 계단 생성 실패: {name} — 위 Map4Build 로그 확인.");
        grp.name = name;
        grp.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
        for (int i = 0; i < StairsStepCount; i++)
        {
            Transform step = grp.Find($"GEO_Step_{i}");
            if (step == null) throw new System.Exception($"[S3_Builder] 계단 단 GEO_Step_{i}을(를) {name}에서 찾지 못했다.");
            Solid(step, $"{name}_Step{i}");
        }
        return grp;
    }

    /// <summary>계단 단 하나의 결과 상자(섹터 로컬, yaw 0/180이라 축 정렬)를 초안 §2 기대 상자와 ±0.02로 대조 — 어긋나면 throw.</summary>
    private static void CheckStep(Transform grp, int index, Box expected)
    {
        Transform step = grp.Find($"{grp.name}_Step{index}");
        BoxCollider col = step != null ? step.GetComponent<BoxCollider>() : null;
        if (col == null) throw new System.Exception($"[S3_Builder] 계단 단 {grp.name}_Step{index}의 BoxCollider가 없다.");
        Vector3 c = g.InverseTransformPoint(step.TransformPoint(col.center));
        Vector3 h = col.size * 0.5f;
        Vector3 min = c - h, max = c + h;
        for (int a = 0; a < 3; a++)
        {
            if (Mathf.Abs(min[a] - expected.min[a]) > StairsCheckTolerance || Mathf.Abs(max[a] - expected.max[a]) > StairsCheckTolerance)
                throw new System.Exception($"[S3_Builder] 계단 단 {grp.name}_Step{index} 결과 {min}~{max}가 초안 §2 {expected.id} " +
                                           $"{expected.min}~{expected.max}와 ±{StairsCheckTolerance} 밖이다.");
        }
    }

    /// <summary>외벽 12조각 → 레인 사이 벽 15조각 순으로 refs.walls(27) [2차판정 12]. 이름: 외벽 GEO_S3_Wall_&lt;id&gt;,
    /// 레인 벽 GEO_S3_Wall_Lane_&lt;쌍&gt;[_조각](초안 id = Lane_…). 레인 벽 id가 Lane_로 시작하지 않으면 throw(지형 단계 → C5 false).</summary>
    private static void BuildWalls(S3_Refs refs)
    {
        foreach (Box b in WallBoxes)
            refs.walls.Add(Solid(Map4Build.Wall(g, b.min, b.max), WallNamePrefix + b.id));
        foreach (Box b in LaneWallBoxes)
        {
            string name = WallNamePrefix + b.id;
            if (!name.StartsWith(LaneWallNamePrefix, System.StringComparison.Ordinal))
                throw new System.Exception($"[S3_Builder] 레인 사이 벽 id '{b.id}'가 계약 이름 {LaneWallNamePrefix}<id>를 만들지 못한다.");
            refs.walls.Add(Solid(Map4Build.Wall(g, b.min, b.max), name));
        }
    }

    /// <summary>발사구 기둥·탄 받이 27개 → refs.columns.</summary>
    private static void BuildColumns(S3_Refs refs)
    {
        foreach (Box b in ColumnBoxes)
            refs.columns.Add(Solid(Map4Build.Wall(g, b.min, b.max), "GEO_S3_Col_" + b.id));
    }

    /// <summary>받침 바닥 1개(윗면 −12). 걷는 면과 XYZ로 겹치지 않는다(맞닿음도 없음 — 가장 낮은 걷는 면 바닥 −1).
    /// refs.catchFloor에 대입한다 — Dress가 Void 재질을 입힌다 [2차판정 13 · 계약 C1-1·C1-3].</summary>
    private static void BuildCatchFloor(S3_Refs refs)
    {
        refs.catchFloor = Solid(Map4Build.Floor(g, CatchBox.min, CatchBox.max), CatchFloorName);
        refs.catchFloorTop = CatchBox.max.y;
    }

    /// <summary>지형 Refs 검증(계약 C1-1의 지형 몫) — 개수·null·catchFloor. 기믹 목록(launchers 14·bridges 26·bridgeAnchors 52)은
    /// Wire가 만든 뒤 ③′ CollectWireGimmicks가 채우고 대조한다(여기서는 검사하지 않는다 [2차판정 4]). 실패면 throw → C5 표준으로 false.</summary>
    private static void ValidateTerrainRefs(S3_Refs refs)
    {
        if (PathBoxes.Length != ExpectedPathFloors || WallBoxes.Length != ExpectedOuterWalls || LaneWallBoxes.Length != ExpectedLaneWalls
            || ColumnBoxes.Length != ExpectedColumns || StairStepExpected.Length != StairsStepCount * 2)
            throw new System.Exception($"[S3_Builder] 좌표표 개수가 초안 §2와 다르다 — 걷는 면 {PathBoxes.Length}/{ExpectedPathFloors}, " +
                                       $"외벽 {WallBoxes.Length}/{ExpectedOuterWalls}, 레인 사이 벽 {LaneWallBoxes.Length}/{ExpectedLaneWalls}, " +
                                       $"기둥 {ColumnBoxes.Length}/{ExpectedColumns}, 계단 단 {StairStepExpected.Length}/4.");
        int laneWalls = 0;   // [2차판정 12 · 계약 C2-3] refs.walls 안 GEO_S3_Wall_Lane_* 개수 = 15, 나머지(외벽) = 12
        foreach (Transform w in refs.walls)
            if (w != null && w.name.StartsWith(LaneWallNamePrefix, System.StringComparison.Ordinal)) laneWalls++;
        if (laneWalls != ExpectedLaneWalls || refs.walls.Count - laneWalls != ExpectedOuterWalls)
            throw new System.Exception($"[S3_Builder] refs.walls 구성이 계약과 다르다 — 레인 사이 벽({LaneWallNamePrefix}*) {laneWalls}/{ExpectedLaneWalls}, " +
                                       $"외벽 {refs.walls.Count - laneWalls}/{ExpectedOuterWalls}.");
        int paths = refs.frontPath.Count + refs.backPath.Count;
        if (paths != ExpectedPathFloors + 2 || refs.walls.Count != ExpectedWalls || refs.columns.Count != ExpectedColumns
            || refs.frontPath.Contains(null) || refs.backPath.Contains(null) || refs.walls.Contains(null) || refs.columns.Contains(null))
            throw new System.Exception($"[S3_Builder] 지형 Refs가 계약과 다르다 — 걷는 면(그룹 포함) {paths}/{ExpectedPathFloors + 2}, " +
                                       $"walls {refs.walls.Count}/{ExpectedWalls}, columns {refs.columns.Count}/{ExpectedColumns}, 또는 null 포함.");
        if (refs.startPad.size == Vector3.zero || refs.savePad.size == Vector3.zero)
            throw new System.Exception($"[S3_Builder] startPad({StartPadId})·savePad({SavePadId}) 상자를 채우지 못했다.");
        if (refs.catchFloor == null || refs.catchFloor.name != CatchFloorName)   // [2차판정 13 · 계약 C1-1] 1개, null 없음
            throw new System.Exception($"[S3_Builder] refs.catchFloor가 계약과 다르다 — " +
                                       $"{(refs.catchFloor == null ? "null" : "'" + refs.catchFloor.name + "'")} (기대 '{CatchFloorName}').");
    }

    // ───────────────────────── ③′ 기믹 목록 [2차판정 2 (나)] ─────────────────────────

    /// <summary>③ Wire 뒤·④ Dress 앞. generated 아래 Wire 생성물(발사구·줄다리·끝 고리)을 이름으로 모아 refs.launchers·bridges·
    /// bridgeAnchors를 계약 C1-1 순서로 채운다(대입 전에 Clear). 찾는 곳은 기믹 그룹(refs.gimmickRoot → generated/S3_Gimmicks) 우선,
    /// 그룹에 없으면 generated 전체. 누락·초과(이름 중복·표에 없는 이름)·순서 불일치·줄다리 고리 참조 불일치는 LogError만 한다 —
    /// throw하지 않고 지형·기믹을 지우지 않는다(②가 끝난 뒤, 계약 C1-0·C1-1). 팀 컴포넌트는 읽기만 한다(값·Transform 변경 없음).
    /// 개수 검증은 만든 쪽(Wire)이 먼저 하고, 여기서는 모은 결과를 다시 대조한다 [2차판정 4].</summary>
    private static void CollectWireGimmicks(Transform generated, S3_Refs refs)
    {
        Transform group = refs.gimmickRoot != null && refs.gimmickRoot.IsChildOf(generated) ? refs.gimmickRoot : generated.Find(GimmickGroupName);
        if (group == null)
            Debug.LogError($"[S3_Builder] ③′ 기믹 그룹 '{GimmickGroupName}'이 generated 아래에 없다 — generated 전체에서 찾는다(S3-W 확인).", generated);

        refs.launchers.Clear();
        refs.bridges.Clear();
        refs.bridgeAnchors.Clear();
        int problems = 0;

        ProjectileLauncher[] allLaunchers = generated.GetComponentsInChildren<ProjectileLauncher>(true);
        ThreadBridge[] allBridges = generated.GetComponentsInChildren<ThreadBridge>(true);
        ThreadAnchor[] allAnchors = generated.GetComponentsInChildren<ThreadAnchor>(true);

        // 기대 이름(계약 순서)
        var launcherNames = new List<string>(LauncherIds.Length);
        foreach (string id in LauncherIds) launcherNames.Add(LauncherPrefix + id);
        var bridgeNames = new List<string>(BridgeIds.Length);
        var anchorNames = new List<string>(BridgeIds.Length * 2);
        foreach (string id in BridgeIds)
        {
            bridgeNames.Add(BridgePrefix + id);
            anchorNames.Add(BridgePrefix + id + AnchorSuffixA);   // i*2 = A
            anchorNames.Add(BridgePrefix + id + AnchorSuffixB);   // i*2+1 = B
        }

        problems += CollectByName(allLaunchers, launcherNames, group, refs.launchers, "발사구");
        problems += CollectByName(allBridges, bridgeNames, group, refs.bridges, "줄다리");
        problems += CollectByName(allAnchors, anchorNames, group, refs.bridgeAnchors, "끝 고리");

        // 줄다리 ↔ 끝 고리 참조 대조(읽기만): bridges[i].anchorA == bridgeAnchors[2i], anchorB == bridgeAnchors[2i+1]
        if (refs.bridges.Count == ExpectedBridges && refs.bridgeAnchors.Count == ExpectedAnchors)
        {
            for (int i = 0; i < refs.bridges.Count; i++)
            {
                ThreadBridge br = refs.bridges[i];
                if (br.anchorA != refs.bridgeAnchors[i * 2] || br.anchorB != refs.bridgeAnchors[i * 2 + 1])
                {
                    problems++;
                    Debug.LogError($"[S3_Builder] ③′ 줄다리 '{br.name}'의 anchorA/anchorB가 모은 끝 고리 " +
                                   $"'{refs.bridgeAnchors[i * 2].name}'/'{refs.bridgeAnchors[i * 2 + 1].name}'와 다르다(S3-W 확인).", br);
                }
            }
        }

        string summary = $"[S3_Builder] ③′ 기믹 목록 — 발사구 {refs.launchers.Count}/{ExpectedLaunchers}, 줄다리 {refs.bridges.Count}/{ExpectedBridges}, " +
                         $"끝 고리 {refs.bridgeAnchors.Count}/{ExpectedAnchors} (generated 안 전체: 발사구 {allLaunchers.Length}·줄다리 {allBridges.Length}·" +
                         $"끝 고리 {allAnchors.Length}), 문제 {problems}건";
        bool ok = problems == 0 && refs.launchers.Count == ExpectedLaunchers && refs.bridges.Count == ExpectedBridges
                  && refs.bridgeAnchors.Count == ExpectedAnchors;
        if (ok) Debug.Log(summary, generated);
        else Debug.LogError(summary + " — 위 로그 확인(S3-W·컨트롤타워). 지형·기믹은 그대로 둔다.", generated);
    }

    /// <summary>found(generated 안 해당 타입 전부, 계층 순서) 가운데 이름이 expected와 같은 것을 expected 순서로 into에 담는다.
    /// 같은 이름이 여럿이면 그룹 안 것을 먼저 쓴다. 반환값 = 문제 건수(누락·중복·표 밖 이름·그룹 밖·계층 순서 불일치). null은 담지 않는다.</summary>
    private static int CollectByName<T>(T[] found, List<string> expected, Transform group, List<T> into, string label) where T : Component
    {
        int problems = 0;
        var byName = new Dictionary<string, List<T>>();
        foreach (T c in found)
        {
            if (c == null) continue;
            if (!byName.TryGetValue(c.gameObject.name, out List<T> list)) byName[c.gameObject.name] = list = new List<T>();
            list.Add(c);
        }

        var expectedSet = new HashSet<string>(expected);
        foreach (KeyValuePair<string, List<T>> kv in byName)
        {
            if (expectedSet.Contains(kv.Key)) continue;
            problems += kv.Value.Count;   // 초과 — 표에 없는 이름
            foreach (T c in kv.Value)
                Debug.LogError($"[S3_Builder] ③′ {label} 초과 — '{c.gameObject.name}'는 계약 C1-1 표에 없는 이름이다(모으지 않는다).", c);
        }

        foreach (string name in expected)
        {
            if (!byName.TryGetValue(name, out List<T> list) || list.Count == 0)
            {
                problems++;
                Debug.LogError($"[S3_Builder] ③′ {label} 누락 — '{name}'을(를) generated 아래에서 찾지 못했다(Wire 생성 확인).", group);
                continue;
            }
            T pick = null;
            if (group != null)
                foreach (T c in list) if (c.transform.IsChildOf(group)) { pick = c; break; }
            if (pick == null)
            {
                pick = list[0];
                if (group != null)
                {
                    problems++;
                    Debug.LogError($"[S3_Builder] ③′ {label} '{name}'이(가) 기믹 그룹 '{group.name}' 밖에 있다 — 그대로 모은다(S3-W 확인).", pick);
                }
            }
            if (list.Count > 1)
            {
                problems += list.Count - 1;   // 초과 — 같은 이름 중복
                Debug.LogError($"[S3_Builder] ③′ {label} 초과 — '{name}'이(가) {list.Count}개다. 하나만 모은다(Wire 중복 생성 확인).", pick);
            }
            into.Add(pick);
        }

        // 순서 대조: 계층 순서(GetComponentsInChildren)로 본 모은 것들의 순서가 계약 순서와 같은가.
        var picked = new HashSet<T>(into);
        var hierarchyOrder = new List<T>(into.Count);
        foreach (T c in found) if (c != null && picked.Contains(c)) hierarchyOrder.Add(c);
        for (int i = 0; i < into.Count && i < hierarchyOrder.Count; i++)
        {
            if (hierarchyOrder[i] == into[i]) continue;
            problems++;
            Debug.LogError($"[S3_Builder] ③′ {label} 순서 불일치 — 계층 {i}번째 '{hierarchyOrder[i].gameObject.name}' ≠ 계약 순서 '{into[i].gameObject.name}'. " +
                           "refs 목록은 계약 순서로 채운다(Wire 생성 순서 확인).", into[i]);
            break;   // 첫 어긋남만 알린다
        }
        return problems;
    }

    // ───────────────────────── 점검 ─────────────────────────

    /// <summary>씬 루트에 Generated 밖 오브젝트가 남았는지 확인(S5_Builder와 같은 점검) — 남으면 재생성 때 중복된다.</summary>
    private static void ReportStrayRoots()
    {
        GameObject sectorRoot = g.root.gameObject;
        foreach (GameObject root in g.gameObject.scene.GetRootGameObjects())
            if (root != sectorRoot)
                Debug.LogError($"[S3_Builder] 씬 루트에 남은 오브젝트 '{root.name}' — Generated 아래로 옮기지 못했다(재생성 시 중복).");
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    private static Bounds MinMax(Vector3 min, Vector3 max)
    {
        Bounds b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    /// <summary>Map4Build.Floor/Wall/Stairs가 돌려준 상자에 S3 이름을 붙이고 시각물 자식 "Visual"을 VIS_ 접두사로 바꾼다(Map4Build 수정 없음).
    /// 생성이 거부돼 null이면 즉시 중단(부분 지형 방지 — Build가 Generated를 비운다).</summary>
    private static Transform Solid(Transform t, string name)
    {
        if (t == null) throw new System.Exception($"[S3_Builder] 지형 생성 실패: {name} — 위 Map4Build 로그 확인.");
        t.name = name;
        Transform vis = t.Find("Visual");
        if (vis != null) vis.name = "VIS_" + name.Substring("GEO_".Length);
        return t;
    }
}

// ───────────────────────── S3_Refs (R3 계약 C1-3, 과제 S3-B3) ─────────────────────────
// Refs 클래스는 빌더 파일 끝에 둔다 [2차판정 3 추인 · 계약 R3-2]. 아래 블록은 진행/지시서/R3/_계약.md C1-3 코드 블록(:197-219)을
// 스크립트로 글자 그대로 바꿔 끼운 것이다(재타이핑 없음) — catchFloor 추가 [2차판정 13], voidVolume −13 [2차판정 1].
// 별도 S3_Refs.cs를 만들면 CS0101 중복 정의가 된다.

/// <summary>S3 빌더 → S3_Wiring/S3_Dress 전달 묶음(R2 계약 C1-3). 좌표 섹터 로컬.</summary>
public sealed class S3_Refs
{
    public Transform generated;
    public Transform gimmickRoot;                 // generated/S3_Gimmicks
    public float halfWidth = 28f, length = 184f;
    public Vector3 startLocalPos = new Vector3(0f, 0f, 3f);      // [초안 §3] SSP_S3_Start·CP_S3_Start 원점(바닥 윗면 — 안전점은 W가 +0.1 [2차판정 15])
    public Vector3 saveLocalPos = new Vector3(21f, 2.4f, 79.5f); // [초안 §3] 세이브 원점(바닥 윗면 — 안전점은 W가 +0.1 [2차판정 15])
    public Bounds startPad;                       // GEO_S3_Path_P_start  min(-5,-1,0) max(5,0,6)
    public Bounds savePad;                        // GEO_S3_Path_B1_P1    min(18,1.4,77) max(24,2.4,82)
    public Bounds voidVolume;                     // OOB_S3 min(-28,-13,0) max(28,-3,184) [2차판정 1]
    public float catchFloorTop = -12f;            // GEO_S3_Floor_Catch 윗면
    public Transform catchFloor;                  // GEO_S3_Floor_Catch — Dress가 Void 재질을 입힌다 [2차판정 13]
    // 지형 분류(Dress 톤 구분용)
    public List<Transform> frontPath = new List<Transform>();    // 앞(L1~L6·턴·지름길·시작) 걷는 면
    public List<Transform> backPath = new List<Transform>();     // 뒤(B1~B7·출구 연결·출구) 걷는 면
    public List<Transform> walls = new List<Transform>();        // GEO_S3_Wall_*
    public List<Transform> columns = new List<Transform>();      // GEO_S3_Col_*
    // 팀 기믹
    public List<ProjectileLauncher> launchers = new List<ProjectileLauncher>(); // 14 — Wire가 만들고 빌더가 ③′에서 채운다 [2차판정 2·4]
    public List<ThreadBridge> bridges = new List<ThreadBridge>();                // 26 — 〃
    public List<ThreadAnchor> bridgeAnchors = new List<ThreadAnchor>();          // 52 — 〃
}
#endif
