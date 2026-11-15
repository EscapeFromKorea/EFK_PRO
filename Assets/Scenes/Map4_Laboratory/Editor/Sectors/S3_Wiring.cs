#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// 섹터3 "점프" 팀 기믹 배치·배선(R2 계약 C1-0·C1-5 S3행·C2-3·C3 S3행, 지시서 진행/지시서/R2/S3-W.md 09-29 재발행).
/// S3_Builder.Build가 지형을 짓고 Refs 검증을 마친 뒤 ③ 단계에서 1회 부른다.
/// 좌표 원천은 초안 진행/초안/S3_배치초안.md(2차 반영판 + 수정 라운드 초안1-2회차, 배치 좌표 불변) §2 줄다리·발사구 표,
/// §2 끝 OOB, §3 팀 기믹 배치표뿐이다. 기믹 코드는 만들거나 고치지 않는다 — 팀 컴포넌트 배치·인스펙터 값·배치 변환·UnityEvent 배선만.
///
/// [① 팀 기믹 — generated/S3_Gimmicks] (refs.gimmickRoot → generated.Find("S3_Gimmicks") → 새로 만듦 순)
/// - 발사구 14 `S3_Launcher_&lt;id&gt;`: 팀 메뉴 Tools/ProjectileTrapSystem/Create Projectile Launcher(S5_Builder.MenuCreate 방식) →
///   피벗·yaw만 지정. 인스펙터 값은 전부 팀 기본 [확정 설계 §2-2] — 대입하지 않고 확인만 한다.
/// - 줄다리 26 `S3_Bridge_&lt;id&gt;` + 끝 고리 52 `S3_Bridge_&lt;id&gt;_A/_B`: 빈 GO + AddComponent(팀 줄다리 생성 메뉴는 고리 ±5 고정·
///   실타래 컨트롤러를 함께 만들어 쓰지 않는다, S3_팀API §1-2). 값은 초안 §3 + [판정 9]. LineRenderer 재질은 Dress 몫(지정 안 함).
/// - Refs는 읽기만 한다(계약 C1-0). refs 목록이 0개면 여기서 만들어 **지역 목록**에 담고, 정확한 개수(14·26+52)면 만들지 않고
///   확인만 한다. 그 밖의 개수면 LogError 후 만들지 않는다(있는 것만 확인·배선).
///
/// [② 복귀 부품 — generated/S3_Respawn, 계약 C3 이름 그대로]
/// - CP_S3_Start·CP_S3_Save: 팀 RespawnZone(Create Checkpoint Pole) → 원점·박스·막대(Pole) 로컬 위치.
/// - SSP_S3_Start(sectionId "S3_Start")·SSP_S3_Save("S3_Save"): 팀 SectionSafePoint + 보조 지점 2개씩.
/// - S3_ProjectileCounter: 팀 SectionHitCounter(AddComponent), destination = SSP_S3_Start, hitsBeforeRespawn 1.
/// - OOB_S3: 팀 OutOfBoundsVolume(Create Respawn Scale) → refs.voidVolume 크기.
/// - ADAPT_S3_RespawnBridge: 우리 Lab_SectionRespawnBridge(카운터 → Master RespawnController를 실행 중에 잇는다).
///
/// [③ 배선] 발사구 14의 OnHazardHit → S3_ProjectileCounter.RegisterHitEvent(GameObject) 동적 인자 영구 배선 ×14
/// (UnityEventTools.AddPersistentListener, 같은 씬, 루프 1곳). 카운터 OnThresholdReached에는 영구 배선을 두지 않는다 —
/// Lab_SectionRespawnBridge는 영구 배선이 1개라도 있는 카운터를 건너뛴다(Lab_SectionRespawnBridge.cs:34).
/// 팀 발사구 배선 메뉴는 공용 체크포인트로 보내므로 쓰지 않는다(S3_팀API M5). 팀 private 리플렉션 없음.
///
/// [두지 않는 것 — 판정 8·10, 계약 C1-5, 초안 §3] 실타래 컨트롤러, 흔들기 고리, 세모 핀 배치기, 네모 닻, 주기 함정, 무중력 버블.
/// 여기서 만들지 않고, 실타래 계열(Thread*·DreamThread*) 컴포넌트가 줄다리·고리 말고 있으면 LogError만 낸다.
///
/// 좌표는 섹터 로컬(+Z 진행, 시작 발판 윗면 y 0). 팀 기믹은 g.TransformPoint/g.rotation *로 놓는다(S5_Builder.Place 방식).
/// 출처: [확정]=설계서 사용자 확정, [팀]=팀 코드, [제안]=초안 제안값, [계산]=계산값, [추정]=실측 필요, [판정]=컨트롤타워 판정,
/// [계약]=R2 계약·지시서에서 정한 구현 규약. 예외는 항목별 try로 잡아 LogError만 한다(지형·기믹은 지우지 않는다).
/// </summary>
public static class S3_Wiring
{
    // ── 이름 [계약 C2-3·C3 S3행] ──
    private const string GimmickGroupName = "S3_Gimmicks";
    private const string GroupName = "S3_Respawn";
    private const string LauncherPrefix = "S3_Launcher_";
    private const string BridgePrefix = "S3_Bridge_";
    private const string CpStartName = "CP_S3_Start";
    private const string CpSaveName = "CP_S3_Save";
    private const string SspStartName = "SSP_S3_Start";
    private const string SspSaveName = "SSP_S3_Save";
    private const string SectionIdStart = "S3_Start";       // [확정 초안 §3 / 계약 C1-5] ASCII
    private const string SectionIdSave = "S3_Save";         // [확정 초안 §3 / 계약 C1-5]
    private const string CounterName = "S3_ProjectileCounter";
    private const string OobName = "OOB_S3";
    private const string BridgeAdapterName = "ADAPT_S3_RespawnBridge"; // [계약 C2-0 ADAPT_ 접두사]
    private const string LauncherMenu = "Tools/ProjectileTrapSystem/Create Projectile Launcher"; // [팀] ProjectileTrapMenuItem.cs:17 (private → 메뉴 경로로 실행)

    // ── 발사구 14 [초안 §2 발사구 표 — 행 순서 = 계약 C1-1]. 피벗 = 발사 원점(ProjectileLauncher.cs:121-129), 방향 = 루트 forward(:145).
    //    yaw 0 = +z, 180 = −z. 피벗 높이 = 틈 양쪽 윗면 중 높은 쪽 + 0.6, 기둥 면에서 0.4 앞 [제안 초안 §3] ──
    private sealed class LauncherSpec
    {
        public readonly string Id; public readonly Vector3 Pivot; public readonly float Yaw;
        public LauncherSpec(string id, float x, float y, float z, float yaw) { Id = id; Pivot = new Vector3(x, y, z); Yaw = yaw; }
    }
    private static readonly LauncherSpec[] LauncherSpecs =
    {
        L("PL_L1_01", -10f, 1.4f, 9.6f, 180f),
        L("PL_L2_04", -5.5f, 3f, 20.6f, 180f),
        L("PL_L2_07", 5.25f, 1.4f, 12.4f, 0f),
        L("PL_L3_02", 15f, 2.2f, 33.1f, 180f),
        L("PL_L3_08", -2.75f, 2.2f, 24.9f, 0f),
        L("PL_L4_02", -15f, 3f, 45.6f, 180f),
        L("PL_L4_03", -10.5f, 3f, 37.4f, 0f),
        L("PL_L4_06", 2.5f, 3.8f, 45.6f, 180f),
        L("PL_L5_02", 10f, 3f, 49.9f, 0f),
        L("PL_L5_05", -0.75f, 3f, 58.1f, 180f),
        L("PL_L5_07", -9.5f, 3f, 49.9f, 0f),
        L("PL_L6_03", -10.75f, 3f, 70.6f, 180f),
        L("PL_L6_07", 6.5f, 3f, 62.4f, 0f),
        L("PL_L6_08", 11f, 3f, 70.6f, 180f),
    };
    private static LauncherSpec L(string id, float x, float y, float z, float yaw) => new LauncherSpec(id, x, y, z, yaw);

    // ── 줄다리 26 [초안 §2 줄다리 표 — 경로 순서 = 계약 C1-1]. 고리 A·B = 양쪽 발판 윗면 모서리(초안 C11 52/52, ±0.02) ──
    private sealed class BridgeSpec
    {
        public readonly string Id; public readonly Vector3 A, B;
        public BridgeSpec(string id, float ax, float ay, float az, float bx, float by, float bz) { Id = id; A = new Vector3(ax, ay, az); B = new Vector3(bx, by, bz); }
    }
    private static readonly BridgeSpec[] BridgeSpecs =
    {
        B("BR_B1_1", 18f, 2.4f, 79.5f, 10f, 2.4f, 79.5f),
        B("BR_B1_2", 4f, 2.4f, 79.5f, -4f, 2.4f, 79.5f),
        B("BR_B1_3", -10f, 2.4f, 79.5f, -18f, 2.4f, 79.5f),
        B("BR_B1_turn", -23.25f, 1.6f, 82f, -23.25f, 1.6f, 90.5f),
        B("BR_B2_1", -18f, 1.6f, 93f, -10f, 1.6f, 93f),
        B("BR_B2_2", -4f, 1.6f, 93f, 4f, 1.6f, 93f),
        B("BR_B2_3", 10f, 1.6f, 93f, 18f, 1.6f, 93f),
        B("BR_B2_turn", 23.25f, 1.6f, 95.5f, 23.25f, 1.6f, 104f),
        B("BR_B3_1", 18f, 1.6f, 106.5f, 10f, 1.6f, 106.5f),
        B("BR_B3_2", 4f, 1.6f, 106.5f, -4f, 1.6f, 106.5f),
        B("BR_B3_3", -10f, 1.6f, 106.5f, -18f, 1.6f, 106.5f),
        B("BR_B3_turn", -23.25f, 0.8f, 109f, -23.25f, 0.8f, 117.5f),
        B("BR_B4_1", -18f, 0.8f, 120f, -10f, 0.8f, 120f),
        B("BR_B4_2", -4f, 0.8f, 120f, 4f, 0.8f, 120f),
        B("BR_B4_3", 10f, 0.8f, 120f, 18f, 0.8f, 120f),
        B("BR_B4_turn", 23.25f, 0.8f, 122.5f, 23.25f, 0.8f, 131f),
        B("BR_B5_1", 18f, 0.8f, 133.5f, 10f, 0.8f, 133.5f),
        B("BR_B5_2", 4f, 0.8f, 133.5f, -4f, 0.8f, 133.5f),
        B("BR_B5_3", -10f, 0.8f, 133.5f, -18f, 0.8f, 133.5f),
        B("BR_B5_turn", -23.25f, 0f, 136f, -23.25f, 0f, 144.5f),
        B("BR_B6_1", -18f, 0f, 147f, -10f, 0f, 147f),
        B("BR_B6_2", -4f, 0f, 147f, 4f, 0f, 147f),
        B("BR_B6_3", 10f, 0f, 147f, 18f, 0f, 147f),
        B("BR_B6_turn", 23.25f, 0f, 149.5f, 23.25f, 0f, 158f),
        B("BR_B7_1", 18f, 0f, 160.5f, 10f, 0f, 160.5f),
        B("BR_B7_2", 4f, 0f, 160.5f, -4f, 0f, 160.5f),
    };
    private static BridgeSpec B(string id, float ax, float ay, float az, float bx, float by, float bz) => new BridgeSpec(id, ax, ay, az, bx, by, bz);

    // ── 발사구 인스펙터 값: 전부 팀 기본 [확정 설계 §2-2 / 팀 ProjectileLauncher.cs:29-53] — 확인 전용(대입하지 않음) ──
    private const float LauncherWarning = 1f, LauncherRest = 2f, LauncherSpeed = 8f, LauncherLifetime = 5f, LauncherRadius = 0.3f;
    private const string LauncherPlayerTag = "Player";

    // ── 줄다리 인스펙터 값 [초안 §3 :350] ──
    private const int BridgeMaxSpans = 1;                   // [제안 초안 §3 M13] 팀 기본 3 — 고정 다리는 가지 경간이 필요 없다
    private const float BridgeSegmentWidth = 1.5f;          // [판정 9] 팀 기본 0.5
    private const float BridgeBaseSag = 0.2f;               // [판정 9] 팀 기본 0.4
    private const float BridgeSagPerWeight = 0.12f;         // [판정 9] 팀 기본 0.55
    private const float BridgeLineWidth = 1.5f;             // [판정 9] 팀 기본 0.06
    private static readonly Quaternion BridgeLocalRot = Quaternion.Euler(-90f, 0f, 0f); // [판정 9] 리본을 눕힘(LineRenderer TransformZ = 위쪽)
    // 팀 기본 그대로 두는 값(대입하지 않고 확인만) [팀 ThreadBridge.cs:60-95]
    private const float BridgeMaxSpanDefault = 14f, BridgeThicknessDefault = 0.2f, BridgeOverlapDefault = 1.2f;
    private const float BridgeMaxSagDefault = 4f, BridgeSagSpeedDefault = 5f, BridgeCaptureDefault = 1.6f;
    private const int BridgeSegmentCountDefault = 12;
    private const float BridgeMinSpan = 0.5f;               // [팀] ThreadBridge.cs:299 — 경간은 0.5 초과
    // 끝 고리 [제안 초안 §3 ThreadAnchor 행] connectRange 0 — 다른 섹터에 실타래 컨트롤러가 놓여도 발밑 고리에 매달리지 못하게.
    private const float AnchorConnectRange = 0f;
    private const float PoseTol = 0.001f;                   // [계약 지시서 완료 조건] 코드 좌표 = 초안 표 ±0.001
    private const float RotTolDeg = 0.1f;

    // ── 원점(섹터 로컬) — 실제로 쓰는 값은 refs.startLocalPos·saveLocalPos, 아래는 대조용 ──
    private static readonly Vector3 StartLocalDraft = new Vector3(0f, 0f, 3f);       // [초안 §3] CP_S3_Start·SSP_S3_Start 원점
    private static readonly Vector3 SaveLocalDraft = new Vector3(21f, 2.4f, 79.5f);  // [초안 §3] CP_S3_Save·SSP_S3_Save 원점
    private static readonly Bounds StartPadDraft = MinMax(new Vector3(-5f, -1f, 0f), new Vector3(5f, 0f, 6f));        // [초안 §2 P_start]
    private static readonly Bounds SavePadDraft = MinMax(new Vector3(18f, 1.4f, 77f), new Vector3(24f, 2.4f, 82f));   // [초안 §2 B1_P1]
    // [2차판정 1 · 계약 C1-3] OOB_S3 아랫면 −13 확정, S3-B VoidMin과 같이 개정 — 중심 y −8·높이 10 [계산]. 옛 아랫면 −12는 결함이었다
    // (M2R1 — 세모 루트 −12.211이 볼륨 밖, 아래 '장외 볼륨' 주석). −13은 필요 조건 ≤ −12.311을 여유 0.789로 충족한다 [계약 R3-S3].
    private static readonly Bounds VoidDraft = MinMax(new Vector3(-28f, -13f, 0f), new Vector3(28f, -3f, 184f));   // [2차판정 1 · 계약 C1-3]
    private const float CatchFloorTopDraft = -12f;          // [초안 §2 F_catch 윗면]

    // ── 체크포인트(팀 RespawnZone) [초안 §3] ──
    /// <summary>[제안 초안 §3] 시작 발판 10×6 전체를 덮는 박스. 높이 18·center (0,9,0)은 팀 메뉴 기본(RespawnMenuItem.cs:23·39).</summary>
    private static readonly Vector3 CpStartSize = new Vector3(10f, 18f, 6f);
    /// <summary>[제안 초안 §3] 세이브 발판 6×5 전체를 덮는 박스. 공유 체크포인트 — 팀 규칙 그대로 [판정 12 = 사용자 결정 (가)].</summary>
    private static readonly Vector3 CpSaveSize = new Vector3(6f, 18f, 5f);
    private static readonly Vector3 CpCenter = new Vector3(0f, 9f, 0f);   // [팀] RespawnMenuItem.cs:39 (ZoneHeight 18 × 0.5)
    /// <summary>[제안 초안 §1-3·§3 C12] 막대 로컬 위치 — 팀 기본 (−2.6,0,0)(RespawnMenuItem.cs:47)이면 시작 막대가 보조점과 0.1,
    /// 세이브 막대가 BR_B1_1 중심선 위라서 옮긴다. 월드(섹터 로컬) (3.6, 0, 5.6) = 시작 발판 북동 모서리.</summary>
    private static readonly Vector3 CpStartPoleLocal = new Vector3(3.6f, 0f, 2.6f);
    /// <summary>[제안 초안 §1-3·§3 C12] 월드(섹터 로컬) (18.4, 2.4, 81.6) = 세이브 발판 북서 모서리.</summary>
    private static readonly Vector3 CpSavePoleLocal = new Vector3(-2.6f, 0f, 2.1f);
    private const string PoleChildName = "Pole";            // [팀] RespawnMenuItem.cs:45

    // ── 안전점 보조 지점(주 지점 기준 오프셋) [초안 §3] ──
    private static readonly Vector3[] StartBackupOffsets = { new Vector3(-2.5f, 0f, 0f), new Vector3(2.5f, 0f, 0f) };  // (−2.5,0,3)·(2.5,0,3)
    private static readonly Vector3[] SaveBackupOffsets = { new Vector3(-1.5f, 0f, 0f), new Vector3(1.5f, 0f, 0f) };   // (19.5,2.4,79.5)·(22.5,2.4,79.5)
    private const float OccupancyRadius = 0.6f;             // [팀] SectionSafePoint.cs:29 기본값 — 바꾸지 않는다(검사용)

    // ── 카운터 ──
    /// <summary>[팀 PRD CH3 "직접 피격 = 즉시 복귀" / 초안 §3·S3_팀API M6] 팀 기본 2(SectionHitCounter.cs:36)를 1로 [2차판정 14 승인].</summary>
    private const int HitsBeforeRespawn = 1;
    // hitCooldown은 팀 기본 0.5(SectionHitCounter.cs:40) 그대로 — 대입하지 않는다.

    // ── 장외 볼륨 ──
    /// <summary>[팀] 장외 판정은 볼륨 안(또는 killY 아래)에 **연속** 3초(RespawnController.cs:59)이고, 판정점은 몸 중심이 아니라
    /// **루트 원점 = mover.transform.position**(RespawnController.cs:308 → OutOfBoundsVolume.AnyContains, ClosestPoint(p) == p :72)이다.
    /// 선 자리의 루트 원점 = 지지면 윗면 + 도형별 오프셋: 구 +0.5, 네모 0, 세모 −0.211 [실측 검증/F1_playtests_20260929_114901.txt 항목N1
    /// '발바닥오프셋'·항목4-c 세모 y −0.21]. 그래서 받침 바닥 윗면 −12에 멈춘 도형의 루트는 구 −11.5 · 네모 −12.000 · 세모 −12.211이다.
    /// 옛 볼륨 아랫면 −12(= 받침 윗면, [2차판정 1] 전 값)에서는 세모 루트가 볼륨 밖이고(killY −30보다도 위라 영구 미복귀, FUNC S3-2 (a) 실측 y −12.21),
    /// 네모는 경계에 걸려 신뢰할 수 없다 [추정 — 접촉 오프셋·부동소수]. 볼륨 아랫면은 받침 윗면보다 VoidBelowCatchMin 이상 낮아야 한다(①).</summary>
    private static readonly string[] ShapeNames = { "구", "네모", "세모" };
    private static readonly float[] ShapeRootOffsets = { 0.5f, 0f, -0.211f };   // [실측 F1_playtests 항목N1] 지지면 윗면 → 루트 원점 높이
    private const float ShapeRootOffsetMin = -0.211f;       // [실측] 세모 — 가장 낮은 루트
    private const float VoidBelowCatchMin = 0.5f;           // [제안 M2R1 지적] 볼륨 아랫면 ≤ 받침 윗면 − 0.5 (세모 −0.211 + 여유 0.289)
    private const float OpenSolidTopCeiling = -0.5f;        // [추정 — 지난 판 창 상한 유지] 윗면이 이 높이 이상인 솔리드는 길 높이로 보고 ③에서 빼고 ②로 본다
    private const float Tol = 0.02f;                        // [확정 00_기반 F1 판정 11] 턱·틈 허용 ±0.02

    private const string Tag = "[S3_Wiring]";

    /// <summary>계약 C1-0. 예외를 밖으로 던지지 않는다(항목별 try — 한 항목 실패가 나머지를 멈추지 않는다). 지형·기믹은 지우지 않는다.</summary>
    public static void Wire(Transform generated, S3_Refs refs)
    {
        if (generated == null) { Debug.LogError($"{Tag} generated가 null이다 — 기믹·복귀·안전망을 만들지 않는다."); return; }
        if (refs == null) { Debug.LogError($"{Tag} refs가 null이다 — 기믹·복귀·안전망을 만들지 않는다.", generated); return; }
        if (refs.generated != null && refs.generated != generated)
            Debug.LogError($"{Tag} refs.generated '{refs.generated.name}' ≠ 인자 generated '{generated.name}' — 인자 기준으로 진행한다.", generated);
        CheckAxisAligned(generated);

        // ① 팀 기믹 — 지역 목록(Refs에 대입하지 않는다, 계약 C1-0)
        List<ProjectileLauncher> launchers = new List<ProjectileLauncher>();
        List<ThreadBridge> bridges = new List<ThreadBridge>();
        List<ThreadAnchor> anchors = new List<ThreadAnchor>();
        Transform gimmicks = null;
        string launcherMode = "건너뜀", bridgeMode = "건너뜀";
        Try("기믹 그룹", () => gimmicks = ResolveGimmickGroup(generated, refs));
        if (gimmicks != null)
        {
            Try("발사구 배치", () => launcherMode = PrepareLaunchers(generated, gimmicks, refs, launchers));
            Try("줄다리·끝 고리 배치", () => bridgeMode = PrepareBridges(generated, gimmicks, refs, bridges, anchors));
        }

        // ② 복귀 부품
        Transform group;
        try { group = NewGroup(generated); }
        catch (System.Exception e) { Debug.LogError($"{Tag} '{GroupName}' 그룹 생성 예외 — 복귀 부품·배선 중단(기믹은 유지): {e}", generated); return; }

        // 원점·발판·볼륨 — refs 값을 쓰고 초안 값과 대조한다(다르면 LogError, 값은 refs 그대로 = 빌더 지형 기준).
        Vector3 start = CheckVec("startLocalPos", refs.startLocalPos, StartLocalDraft);
        Vector3 save = CheckVec("saveLocalPos", refs.saveLocalPos, SaveLocalDraft);
        Bounds startPad = CheckBounds("startPad", refs.startPad, StartPadDraft);
        Bounds savePad = CheckBounds("savePad", refs.savePad, SavePadDraft);
        Bounds voidVol = CheckBounds("voidVolume", refs.voidVolume, VoidDraft);
        float catchTop = refs.catchFloorTop;
        if (Mathf.Abs(catchTop - CatchFloorTopDraft) > Tol)
            Debug.LogError($"{Tag} refs.catchFloorTop {catchTop} ≠ 초안 {CatchFloorTopDraft} — S3-B 확인.", generated);

        RespawnZone cpStart = null, cpSave = null;
        SectionSafePoint sspStart = null, sspSave = null;
        SectionHitCounter counter = null;
        OutOfBoundsVolume oob = null;
        Lab_SectionRespawnBridge adapter = null;

        Try(CpStartName, () => cpStart = BuildCheckpoint(group, CpStartName, start, CpStartSize, CpStartPoleLocal, startPad));
        Try(CpSaveName, () => cpSave = BuildCheckpoint(group, CpSaveName, save, CpSaveSize, CpSavePoleLocal, savePad));
        Try(SspStartName, () => sspStart = BuildSafePoint(group, SspStartName, SectionIdStart, start, StartBackupOffsets, startPad));
        Try(SspSaveName, () => sspSave = BuildSafePoint(group, SspSaveName, SectionIdSave, save, SaveBackupOffsets, savePad));
        Try(CounterName, () => counter = BuildCounter(group, start, sspStart));
        Try(OobName, () => oob = BuildOutOfBounds(group, voidVol));
        Try(BridgeAdapterName, () =>
        {
            GameObject go = new GameObject(BridgeAdapterName);
            go.transform.SetParent(group, false);
            adapter = go.AddComponent<Lab_SectionRespawnBridge>();
        });

        // ③ 배선
        int wired = 0;
        Try("발사구 배선", () => wired = WireLaunchers(launchers, counter));
        Try("카운터 OnThresholdReached 확인", () => CheckNoThresholdWiring(counter));

        // 확인(값은 쓰지 않음) — 다르면 LogError만.
        int launcherBad = 0, bridgeBad = 0, anchorBad = 0, forbidden = 0;
        Try("발사구 확인", () => launcherBad = VerifyLaunchers(generated, launchers));
        Try("줄다리 확인", () => bridgeBad = VerifyBridges(generated, bridges, anchors));
        Try("끝 고리 확인", () => anchorBad = VerifyAnchors(generated, anchors));
        Try("두지 않는 부품 확인", () => forbidden = VerifyNoExtraThreadParts(generated, gimmicks));
        Try("장외 볼륨 진단", () => DiagnoseVoid(generated, group, voidVol, catchTop));
        Try("그룹 확인", () => VerifyGroups(generated, gimmicks, group));
        Try("씬 루트 잔류 확인", () => VerifyNoStrayRoots(generated));

        bool made = cpStart != null && cpSave != null && sspStart != null && sspSave != null && counter != null && oob != null && adapter != null;
        bool gimOk = launchers.Count == LauncherSpecs.Length && bridges.Count == BridgeSpecs.Length && anchors.Count == BridgeSpecs.Length * 2;
        string summary = $"{Tag} 기믹 — 발사구 {launchers.Count}/{LauncherSpecs.Length}({launcherMode}), 줄다리 {bridges.Count}/{BridgeSpecs.Length}·끝 고리 {anchors.Count}/{BridgeSpecs.Length * 2}({bridgeMode}); " +
                         $"복귀 {(made ? "7/7" : "일부 실패")} — CP 시작/세이브 {Ok(cpStart)}/{Ok(cpSave)}, SSP 시작/세이브 {Ok(sspStart)}/{Ok(sspSave)}, " +
                         $"카운터 {Ok(counter)}(임계 {HitsBeforeRespawn}), OOB {Ok(oob)}, 브리지 {Ok(adapter)}; 영구 배선 {wired}/{LauncherSpecs.Length}; " +
                         $"확인 불일치 — 발사구 {launcherBad}, 줄다리 {bridgeBad}, 끝 고리 {anchorBad}, 두지 않는 부품 {forbidden}";
        if (made && gimOk && wired == LauncherSpecs.Length && launcherBad + bridgeBad + anchorBad + forbidden == 0) Debug.Log(summary, group);
        else Debug.LogError(summary + " — 위 로그 확인.", group);
    }

    private static string Ok(Object o) => o != null ? "O" : "X";

    private static void Try(string label, System.Action a)
    {
        try { a(); }
        catch (System.Exception e) { Debug.LogError($"{Tag} '{label}' 예외 — 이 항목만 건너뛴다: {e}"); }
    }

    // ───────────────────────── 그룹 ─────────────────────────

    /// <summary>기믹 그룹: refs.gimmickRoot(generated 아래일 때) → generated.Find("S3_Gimmicks") → 새로 만듦(로컬 원점·무회전).</summary>
    private static Transform ResolveGimmickGroup(Transform generated, S3_Refs refs)
    {
        Transform given = refs.gimmickRoot;
        if (given != null)
        {
            if (given.IsChildOf(generated))
            {
                if (given.name != GimmickGroupName)
                    Debug.LogWarning($"{Tag} refs.gimmickRoot 이름 '{given.name}' ≠ '{GimmickGroupName}'(계약 C3·PTF-2 부모 검사) — 그대로 쓴다.", given);
                return given;
            }
            Debug.LogError($"{Tag} refs.gimmickRoot '{given.name}'가 generated 아래가 아니다 — 쓰지 않고 '{GimmickGroupName}'을 찾거나 만든다.", given);
        }
        Transform found = generated.Find(GimmickGroupName);
        if (found != null) return found;
        GameObject go = new GameObject(GimmickGroupName);
        go.transform.SetParent(generated, false); // 로컬 원점·무회전 = 섹터 로컬 좌표계 그대로
        return go.transform;
    }

    /// <summary>generated/S3_Respawn 1개. 같은 빌드에서 두 번 불려도 겹치지 않게 기존 그룹(우리 생성물만 있음)을 지운다(S5_Wiring과 같음).</summary>
    private static Transform NewGroup(Transform generated)
    {
        for (int i = generated.childCount - 1; i >= 0; i--)
        {
            Transform c = generated.GetChild(i);
            if (c.name == GroupName) Object.DestroyImmediate(c.gameObject);
        }
        GameObject go = new GameObject(GroupName);
        go.transform.SetParent(generated, false); // 로컬 원점·무회전 = 섹터 로컬 좌표계 그대로
        return go.transform;
    }

    // ───────────────────────── 발사구 ─────────────────────────

    /// <summary>refs.launchers 0개 → 팀 메뉴로 14개를 만든다. 14개 → 만들지 않고 그것을 쓴다(확인·배선). 그 밖 → LogError, 만들지 않는다.
    /// 결과는 into(지역 목록)에만 담는다. 반환값은 요약 로그용 설명.</summary>
    private static string PrepareLaunchers(Transform generated, Transform gimmicks, S3_Refs refs, List<ProjectileLauncher> into)
    {
        List<ProjectileLauncher> given = refs.launchers;
        if (given == null) { Debug.LogError($"{Tag} refs.launchers가 null이다 — 발사구를 만들지도 배선하지도 않는다.", generated); return "refs null"; }
        if (given.Count == LauncherSpecs.Length) { into.AddRange(given); return "빌더 것 확인"; }
        if (given.Count != 0)
        {
            Debug.LogError($"{Tag} refs.launchers {given.Count}개 — 0개도 계약 {LauncherSpecs.Length}개도 아니다. 만들지 않고 있는 것만 확인·배선한다(S3-B·컨트롤타워).", generated);
            into.AddRange(given);
            return "개수 불일치";
        }

        RemoveOwnLeftovers(gimmicks, LauncherPrefix + "PL_");
        for (int i = 0; i < LauncherSpecs.Length; i++)
        {
            LauncherSpec s = LauncherSpecs[i];
            ProjectileLauncher l = CreateByTeamMenu<ProjectileLauncher>(() => EditorApplication.ExecuteMenuItem(LauncherMenu), LauncherMenu, gimmicks);
            if (l == null) { Debug.LogError($"{Tag} 발사구 {s.Id} 생성 실패 — 건너뛴다.", gimmicks); continue; }
            l.gameObject.name = LauncherPrefix + s.Id;
            Place(generated, l.transform, s.Pivot, Quaternion.Euler(0f, s.Yaw, 0f));
            EditorUtility.SetDirty(l.transform);
            EditorUtility.SetDirty(l.gameObject);
            into.Add(l);
        }
        return "여기서 생성";
    }

    // ───────────────────────── 줄다리·끝 고리 ─────────────────────────

    /// <summary>refs.bridges·bridgeAnchors 둘 다 0개 → 26 + 52를 만든다. 26·52개 → 만들지 않고 그것을 쓴다. 그 밖 → LogError, 만들지 않는다.
    /// 결과는 into 목록(지역)에만 담는다 — anchors는 계약 C1-1 순서 [A,B] 번갈아.</summary>
    private static string PrepareBridges(Transform generated, Transform gimmicks, S3_Refs refs, List<ThreadBridge> intoBridges, List<ThreadAnchor> intoAnchors)
    {
        List<ThreadBridge> givenB = refs.bridges;
        List<ThreadAnchor> givenA = refs.bridgeAnchors;
        if (givenB == null || givenA == null)
        {
            Debug.LogError($"{Tag} refs.bridges 또는 refs.bridgeAnchors가 null이다 — 줄다리를 만들지 않는다.", generated);
            if (givenB != null) intoBridges.AddRange(givenB);
            if (givenA != null) intoAnchors.AddRange(givenA);
            return "refs null";
        }
        if (givenB.Count == BridgeSpecs.Length && givenA.Count == BridgeSpecs.Length * 2)
        {
            intoBridges.AddRange(givenB);
            intoAnchors.AddRange(givenA);
            return "빌더 것 확인";
        }
        if (givenB.Count != 0 || givenA.Count != 0)
        {
            Debug.LogError($"{Tag} refs.bridges {givenB.Count}개·bridgeAnchors {givenA.Count}개 — 0/0도 계약 {BridgeSpecs.Length}/{BridgeSpecs.Length * 2}도 아니다. " +
                           "만들지 않고 있는 것만 확인한다(S3-B·컨트롤타워).", generated);
            intoBridges.AddRange(givenB);
            intoAnchors.AddRange(givenA);
            return "개수 불일치";
        }

        RemoveOwnLeftovers(gimmicks, BridgePrefix + "BR_");
        for (int i = 0; i < BridgeSpecs.Length; i++)
        {
            BridgeSpec s = BridgeSpecs[i];
            ThreadAnchor a = NewAnchor(generated, gimmicks, BridgePrefix + s.Id + "_A", s.A);
            ThreadAnchor b = NewAnchor(generated, gimmicks, BridgePrefix + s.Id + "_B", s.B);
            intoAnchors.Add(a);
            intoAnchors.Add(b);

            // 줄다리 GO: 위치 = A·B 중점 [2차판정 14 승인 — 초안 미지정이던 값. 세그먼트·실은 고리의 월드 좌표로 정해진다(ThreadBridge.cs:401-411)],
            // 회전 (−90,0,0) [판정 9]. 고리는 줄다리의 형제(같은 S3_Gimmicks 아래) — 줄다리 회전을 물려받지 않게 [추정 — 구현 선택].
            GameObject go = new GameObject(BridgePrefix + s.Id);
            go.transform.SetParent(gimmicks, false);
            Place(generated, go.transform, (s.A + s.B) * 0.5f, BridgeLocalRot);
            ThreadBridge br = go.AddComponent<ThreadBridge>();   // [팀] RequireComponent(LineRenderer) — 같이 붙는다(ThreadBridge.cs:45)
            br.anchorA = a;
            br.anchorB = b;
            br.maxSpans = BridgeMaxSpans;
            br.segmentWidth = BridgeSegmentWidth;
            br.baseSag = BridgeBaseSag;
            br.sagPerWeight = BridgeSagPerWeight;
            br.lineWidth = BridgeLineWidth;
            // maxSpan 14·segmentCount 12·segmentThickness 0.2·segmentOverlap 1.2·maxSag 4·sagResponseSpeed 5·riderCaptureRadius 1.6은 팀 기본 — 대입하지 않는다.
            LineRenderer lr = go.GetComponent<LineRenderer>();
            if (lr == null) Debug.LogError($"{Tag} 줄다리 '{go.name}'에 LineRenderer가 없다 — alignment를 지정하지 못했다.", go);
            else
            {
                lr.alignment = LineAlignment.TransformZ;         // [판정 9] 재질(sharedMaterial)은 Dress 몫 — 여기서 지정하지 않는다
                EditorUtility.SetDirty(lr);
            }
            EditorUtility.SetDirty(br);
            EditorUtility.SetDirty(go);
            intoBridges.Add(br);
        }
        return "여기서 생성";
    }

    /// <summary>끝 고리: 빈 GO + 팀 ThreadAnchor(물리·콜라이더 없는 위치 마커, ThreadAnchor.cs 머리말). 위치 = 발판 윗면 모서리.
    /// connectRange 0 [제안 초안 §3]. lockToSidePlane은 팀 기본(false) — 대입하지 않는다.</summary>
    private static ThreadAnchor NewAnchor(Transform generated, Transform gimmicks, string name, Vector3 local)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(gimmicks, false);
        Place(generated, go.transform, local, Quaternion.identity);
        ThreadAnchor a = go.AddComponent<ThreadAnchor>();
        a.connectRange = AnchorConnectRange;
        EditorUtility.SetDirty(a);
        EditorUtility.SetDirty(go);
        return a;
    }

    /// <summary>refs 목록이 비어 이번에 만들 차례일 때, 같은 이름의 우리 잔류물(같은 빌드에서 Wire가 두 번 불린 경우)을 먼저 지운다 — 중복 방지.
    /// 이름 접두사가 계약 C2-3 우리 이름(S3_Launcher_PL_·S3_Bridge_BR_)인 자식만 대상이다.</summary>
    private static void RemoveOwnLeftovers(Transform gimmicks, string prefix)
    {
        int n = 0;
        for (int i = gimmicks.childCount - 1; i >= 0; i--)
        {
            Transform c = gimmicks.GetChild(i);
            if (!c.name.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
            Object.DestroyImmediate(c.gameObject);
            n++;
        }
        if (n > 0) Debug.LogWarning($"{Tag} '{gimmicks.name}' 아래 '{prefix}*' 잔류 {n}개를 지우고 다시 만든다(refs 목록은 비어 있었다).", gimmicks);
    }

    // ───────────────────────── 체크포인트 ─────────────────────────

    /// <summary>팀 RespawnZone. 밟으면 공용 체크포인트 저장(RespawnZone.cs:132-139), 추락·R 복귀 목적지.
    /// 페이드 바닥은 박스 중앙에서 아래로 9 + 2 레이(RespawnZone.cs:163-184) → 원점 = 발판 윗면. 축 정렬 전제(:186-188).</summary>
    private static RespawnZone BuildCheckpoint(Transform group, string name, Vector3 origin, Vector3 size, Vector3 poleLocal, Bounds pad)
    {
        RespawnZone zone = CreateByTeamMenu<RespawnZone>(() => { RespawnMenuItem.CreateCheckpoint(); return true; }, "Tools/Respawn/Create Checkpoint Pole", group);
        if (zone == null) return null;
        GameObject go = zone.gameObject;
        go.name = name;
        go.transform.SetParent(group, false);
        go.transform.localPosition = origin;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        BoxCollider box = go.GetComponent<BoxCollider>();
        if (box == null) { Debug.LogError($"{Tag} {name}: 팀 체크포인트에 BoxCollider가 없다.", go); return zone; }
        box.isTrigger = true;
        box.size = size;
        box.center = CpCenter;
        EditorUtility.SetDirty(box);
        // dropSpawnInset 0.5·groundRayExtra 2·깃발 값은 팀 기본 그대로 — 대입하지 않는다.

        Transform pole = go.transform.Find(PoleChildName);
        if (pole == null) Debug.LogError($"{Tag} {name}: 팀 막대 자식 '{PoleChildName}'을 찾지 못했다 — 막대 위치를 옮기지 못함(C12).", go);
        else { pole.localPosition = poleLocal; EditorUtility.SetDirty(pole); }

        // 확인: 박스 발자국이 발판 발자국을 덮는가(±0.02), 원점 y = 발판 윗면, 막대 발이 발판 위.
        if (HasSize(pad))
        {
            float x0 = origin.x - size.x * 0.5f, x1 = origin.x + size.x * 0.5f, z0 = origin.z - size.z * 0.5f, z1 = origin.z + size.z * 0.5f;
            if (x0 > pad.min.x + Tol || x1 < pad.max.x - Tol || z0 > pad.min.z + Tol || z1 < pad.max.z - Tol)
                Debug.LogError($"{Tag} {name}: 박스 x[{x0},{x1}] z[{z0},{z1}]가 발판 x[{pad.min.x},{pad.max.x}] z[{pad.min.z},{pad.max.z}]를 덮지 못한다.", go);
            if (Mathf.Abs(origin.y - pad.max.y) > Tol)
                Debug.LogError($"{Tag} {name}: 원점 y {origin.y} ≠ 발판 윗면 {pad.max.y} — 팀 페이드 바닥 레이가 다른 면을 잡을 수 있다.", go);
            Vector3 foot = origin + poleLocal;
            if (foot.x < pad.min.x - Tol || foot.x > pad.max.x + Tol || foot.z < pad.min.z - Tol || foot.z > pad.max.z + Tol)
                Debug.LogError($"{Tag} {name}: 막대 발 {foot}가 발판 밖이다.", go);
        }
        EditorUtility.SetDirty(go);
        return zone;
    }

    // ───────────────────────── 안전점 ─────────────────────────

    /// <summary>팀 SectionSafePoint — 위치 = 착지 바닥점 그대로(레이 없음, SectionSafePoint.cs:8-11) → y = 발판 윗면.
    /// 보조 지점은 그룹 아래 빈 오브젝트(S5_Wiring과 같은 꼴). occupancyRadius는 팀 기본 0.6 그대로.</summary>
    private static SectionSafePoint BuildSafePoint(Transform group, string name, string sectionId, Vector3 main, Vector3[] backupOffsets, Bounds pad)
    {
        SectionSafePoint point = CreateByTeamMenu<SectionSafePoint>(() => { RespawnMenuItem.CreateSectionSafePoint(); return true; }, "Tools/Respawn/Create Section Safe Point", group);
        if (point == null) return null;
        point.gameObject.name = name;
        point.transform.SetParent(group, false);
        point.transform.localPosition = main;
        point.transform.localRotation = Quaternion.identity;
        point.sectionId = sectionId;

        List<Vector3> pts = new List<Vector3> { main };
        Transform[] backups = new Transform[backupOffsets.Length];
        for (int i = 0; i < backupOffsets.Length; i++)
        {
            GameObject b = new GameObject($"{name}_Backup{i}");
            b.transform.SetParent(group, false);
            b.transform.localPosition = main + backupOffsets[i];
            backups[i] = b.transform;
            pts.Add(b.transform.localPosition);
        }
        point.backupPoints = backups;

        // 확인: 모든 점이 발판 윗면 위, 가장자리에서 점유 반경 이상 안쪽, 서로 ≥ 반경 × 2.
        if (HasSize(pad))
            foreach (Vector3 p in pts)
                if (Mathf.Abs(p.y - pad.max.y) > Tol || p.x < pad.min.x + OccupancyRadius - 1e-4f || p.x > pad.max.x - OccupancyRadius + 1e-4f ||
                    p.z < pad.min.z + OccupancyRadius - 1e-4f || p.z > pad.max.z - OccupancyRadius + 1e-4f)
                    Debug.LogError($"{Tag} {name}: 점 {p}가 발판 윗면(y {pad.max.y}) 안쪽 {OccupancyRadius} 밖이다.", point);
        for (int i = 0; i < pts.Count; i++)
            for (int j = i + 1; j < pts.Count; j++)
                if (Vector3.Distance(pts[i], pts[j]) < OccupancyRadius * 2f - 1e-4f)
                    Debug.LogError($"{Tag} {name}: 점 {pts[i]}·{pts[j]} 간격 {Vector3.Distance(pts[i], pts[j]):0.##} < {OccupancyRadius * 2f}.", point);
        EditorUtility.SetDirty(point);
        return point;
    }

    // ───────────────────────── 카운터 ─────────────────────────

    /// <summary>팀 SectionHitCounter(AddComponent — 팀도 메뉴 없이 붙인다, PathChaserMenuItem.cs:147). "시작 발판 근처 빈 오브젝트"[초안 §3]:
    /// 초안에 좌표가 없어 시작 원점과 같은 자리에 둔다 — 콜라이더가 없어 위치는 동작과 무관하다 [추정].
    /// SSP_S3_Start.counter = 이 카운터[초안 §3] — 안전점에 트리거 콜라이더가 없으므로 팀 설계상 수동 복귀 리셋은 꺼진 채다(SectionSafePoint.cs:18-21).
    /// OnThresholdReached는 손대지 않는다(영구 배선 0 — Lab_SectionRespawnBridge가 실행 중에 붙인다).</summary>
    private static SectionHitCounter BuildCounter(Transform group, Vector3 start, SectionSafePoint destination)
    {
        GameObject go = new GameObject(CounterName);
        go.transform.SetParent(group, false);
        go.transform.localPosition = start;
        SectionHitCounter counter = go.AddComponent<SectionHitCounter>();
        counter.hitsBeforeRespawn = HitsBeforeRespawn;
        if (destination != null)
        {
            counter.destination = destination;
            destination.counter = counter;
            EditorUtility.SetDirty(destination);
        }
        else Debug.LogError($"{Tag} {CounterName}: 목적지 {SspStartName}가 없다 — destination 비움(피격 시 복귀 안 함).", go);
        EditorUtility.SetDirty(counter);
        return counter;
    }

    private static void CheckNoThresholdWiring(SectionHitCounter counter)
    {
        if (counter == null) return;
        int n = counter.OnThresholdReached != null ? counter.OnThresholdReached.GetPersistentEventCount() : 0;
        if (n > 0) Debug.LogError($"{Tag} {CounterName}.OnThresholdReached 영구 배선 {n}개 — Lab_SectionRespawnBridge가 이 카운터를 건너뛴다(계약: 0).", counter);
    }

    // ───────────────────────── 장외 볼륨 ─────────────────────────

    /// <summary>팀 OutOfBoundsVolume — 필드 없음, 트리거 필수(OutOfBoundsVolume.cs:50-52). 변환 = 볼륨 중심, 스케일 1, BoxCollider size = 볼륨 크기
    /// [초안 §3: 중심 (0,−7.5,92), 56×9×184]. 트리거라 Map4Audit 관통 검사 대상이 아니다.</summary>
    private static OutOfBoundsVolume BuildOutOfBounds(Transform group, Bounds vol)
    {
        if (!HasSize(vol)) { Debug.LogError($"{Tag} 장외 볼륨 크기가 0이다 — {OobName}을 만들지 않는다.", group); return null; }
        OutOfBoundsVolume oob = CreateByTeamMenu<OutOfBoundsVolume>(() => { RespawnMenuItem.CreateRespawnScale(); return true; }, "Tools/Respawn/Create Respawn Scale", group);
        if (oob == null) return null;
        GameObject go = oob.gameObject;
        go.name = OobName;
        go.transform.SetParent(group, false);
        go.transform.localPosition = vol.center;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        BoxCollider box = go.GetComponent<BoxCollider>();
        if (box == null) { Debug.LogError($"{Tag} {OobName}: 팀 장외 볼륨에 BoxCollider가 없다.", go); return oob; }
        box.isTrigger = true;
        box.center = Vector3.zero;
        box.size = vol.size;
        EditorUtility.SetDirty(box);
        EditorUtility.SetDirty(go);
        return oob;
    }

    /// <summary>진단만(생성·수정 없음). 판정점은 도형 루트 원점(transform.position — RespawnController.cs:308)이고, 선 자리 루트 = 지지면 윗면 +
    /// ShapeRootOffsets(구 +0.5 / 네모 0 / 세모 −0.211).
    /// ① 볼륨 아랫면 ≤ 받침 바닥 윗면 − VoidBelowCatchMin(0.5) — 받침 위에 멈춘 세 도형의 루트가 모두 볼륨 안이어야 연속 3초가 끊기지 않는다.
    /// ② 볼륨 윗면 &lt; 걷는 면(GEO_S3_Path_*) 윗면 최저 + 세모 오프셋 − 0.02 — 길 위 도형의 루트가 볼륨에 들지 않는다.
    /// ③ 윗면이 OpenSolidTopCeiling(−0.5)보다 낮고 위가 열린 솔리드(받침 바닥 제외 — ①이 본다)에 도형이 서면 루트가 볼륨 [아랫면+0.02, 윗면−0.02]
    /// 밖인 도형이 있는지 — 있으면 그 도형은 자동 복귀하지 않는다(수동 R만) — 경고.</summary>
    private static void DiagnoseVoid(Transform generated, Transform group, Bounds vol, float catchTop)
    {
        if (!HasSize(vol)) return;
        if (vol.min.y > catchTop - VoidBelowCatchMin + 0.001f)
            Debug.LogError($"{Tag} 볼륨 아랫면 {vol.min.y} > 받침 바닥 윗면 {catchTop} − {VoidBelowCatchMin} — 받침 위에 멈춘 도형 루트(구 {catchTop + ShapeRootOffsets[0]:0.###} · " +
                           $"네모 {catchTop + ShapeRootOffsets[1]:0.###} · 세모 {catchTop + ShapeRootOffsets[2]:0.###})가 볼륨 밖이거나 경계라 연속 3초 판정이 서지 않는다" +
                           $"(팀 기본 killY −30보다 위라 영구 미복귀 — FUNC S3-2). 볼륨 아랫면은 [2차판정 1] −13이어야 한다(refs.voidVolume·S3_Builder VoidMin 확인).", generated);

        Physics.SyncTransforms();
        List<Bounds> solids = new List<Bounds>();
        List<string> names = new List<string>();
        float minPathTop = float.PositiveInfinity;
        Collider catchCol = null;
        foreach (Collider c in generated.GetComponentsInChildren<Collider>(true))
        {
            if (c == null || c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            if (c.transform.IsChildOf(group)) continue;
            Bounds b = LocalBounds(generated, c.bounds);
            solids.Add(b); names.Add(c.name);
            if (c.name.StartsWith("GEO_S3_Path_", System.StringComparison.Ordinal)) minPathTop = Mathf.Min(minPathTop, b.max.y);
            if (c.name == "GEO_S3_Floor_Catch") catchCol = c;
        }
        if (catchCol == null) Debug.LogWarning($"{Tag} GEO_S3_Floor_Catch 콜라이더를 찾지 못했다 — 받침 바닥이 없으면 떨어진 도형이 볼륨을 지나 killY(팀 기본 −30)까지 간다(연속 3초는 killY 아래에서 이어진다, 약 5.5초 [S3_팀API §3-3]).", generated);
        else
        {
            float top = LocalBounds(generated, catchCol.bounds).max.y;
            if (Mathf.Abs(top - catchTop) > Tol)
                Debug.LogError($"{Tag} GEO_S3_Floor_Catch 실제 윗면 {top:0.###} ≠ refs.catchFloorTop {catchTop}.", catchCol);
            if (vol.min.y > top - VoidBelowCatchMin + 0.001f)
                Debug.LogError($"{Tag} GEO_S3_Floor_Catch 실제 윗면 {top:0.###} 기준 볼륨 아랫면 {vol.min.y} > 윗면 − {VoidBelowCatchMin} — 받침 위 세모 루트 {top + ShapeRootOffsetMin:0.###}가 볼륨 밖이다(①과 같은 결함).", catchCol);
        }
        if (!float.IsPositiveInfinity(minPathTop) && vol.max.y >= minPathTop + ShapeRootOffsetMin - Tol)
            Debug.LogError($"{Tag} 볼륨 윗면 {vol.max.y}가 걷는 면 최저 윗면 {minPathTop:0.##}에 선 세모 루트 {minPathTop + ShapeRootOffsetMin:0.###}(−0.02)보다 낮지 않다 — 길 위 도형이 볼륨 판정에 든다.", generated);

        // ③ 도형별 루트(윗면 + 오프셋)가 볼륨 [아랫면+Tol, 윗면−Tol] 밖이면 그 도형은 그 솔리드 위에서 자동 복귀하지 않는다. 경계(±Tol)도 신뢰하지 않는다.
        List<string> found = new List<string>();
        for (int i = 0; i < solids.Count; i++)
        {
            Bounds b = solids[i];
            if (catchCol != null && names[i] == catchCol.name) continue;   // 받침 바닥은 ①이 본다
            if (b.max.y >= OpenSolidTopCeiling - 0.01f) continue;
            bool capped = false;
            for (int j = 0; j < solids.Count && !capped; j++)
            {
                if (j == i) continue;
                Bounds q = solids[j];
                capped = Mathf.Abs(q.min.y - b.max.y) <= Tol && q.min.x <= b.min.x + Tol && q.max.x >= b.max.x - Tol &&
                         q.min.z <= b.min.z + Tol && q.max.z >= b.max.z - Tol;
            }
            if (capped) continue;
            List<string> outShapes = new List<string>();
            for (int s = 0; s < ShapeRootOffsets.Length; s++)
            {
                float root = b.max.y + ShapeRootOffsets[s];
                if (root > vol.max.y - Tol || root < vol.min.y + Tol) outShapes.Add($"{ShapeNames[s]} {root:0.###}");
            }
            if (outShapes.Count > 0) found.Add($"{names[i]}(윗면 y {b.max.y:0.##} → 루트 " + string.Join("·", outShapes) + ")");
        }
        if (found.Count > 0)
            Debug.LogWarning($"{Tag} 윗면이 y {OpenSolidTopCeiling}보다 낮고 위가 열린 솔리드 {found.Count}개에서 선 도형 루트가 볼륨 y [{vol.min.y},{vol.max.y}](±{Tol}) 밖: " +
                             string.Join(", ", found.GetRange(0, Mathf.Min(5, found.Count))) +
                             " — 그 도형은 그 위에 서면 자동 복귀하지 않는다(수동 R만). reviewRequests 대상.", generated);
    }

    // ───────────────────────── 발사구 배선 ─────────────────────────

    /// <summary>launchers[i].OnHazardHit(UnityEvent&lt;GameObject&gt;, 맞은 플레이어 Root — ProjectileLauncher.cs:25·60) →
    /// counter.RegisterHitEvent(GameObject)(SectionHitCounter.cs:95) 동적 인자 영구 배선. 팀 ProjectileTrapRespawnWiring.cs:47-48과 같은 API,
    /// 대상만 카운터다. 발사구 필드는 대입하지 않는다 — 이벤트가 null이면 LogError 후 건너뛴다.</summary>
    private static int WireLaunchers(List<ProjectileLauncher> launchers, SectionHitCounter counter)
    {
        if (counter == null) { Debug.LogError($"{Tag} 카운터가 없어 발사구 배선을 건너뛴다(0/{LauncherSpecs.Length})."); return 0; }
        if (launchers.Count != LauncherSpecs.Length)
            Debug.LogError($"{Tag} 발사구 {launchers.Count}개 — 계약 C1-1은 {LauncherSpecs.Length}개. 있는 것만 배선한다.", counter);

        int wired = 0;
        for (int i = 0; i < launchers.Count; i++)
        {
            ProjectileLauncher l = launchers[i];
            if (l == null) { Debug.LogError($"{Tag} 발사구[{i}]가 null이다."); continue; }
            if (l.gameObject.scene != counter.gameObject.scene)
            {
                Debug.LogError($"{Tag} 발사구[{i}] '{l.name}'가 카운터와 다른 씬이다 — 영구 배선 불가.", l);
                continue;
            }
            UnityEvent<GameObject> evt = l.OnHazardHit;
            if (evt == null)
            {
                Debug.LogError($"{Tag} 발사구 '{l.name}'.OnHazardHit가 null이다 — 팀 필드에 대입하지 않으므로 배선하지 못했다(reviewRequests).", l);
                continue;
            }
            if (AddHitListener(evt, counter, l)) wired++;
        }
        return wired;
    }

    /// <summary>같은 이벤트에 남은 RegisterHitEvent 배선(재실행·사라진 옛 카운터)을 먼저 걷어 중복 발화를 막고 1개를 건다(S5_Wiring과 같음).</summary>
    private static bool AddHitListener(UnityEvent<GameObject> evt, SectionHitCounter counter, Object owner)
    {
        for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            Object target = evt.GetPersistentTarget(i);
            if (evt.GetPersistentMethodName(i) == nameof(SectionHitCounter.RegisterHitEvent) && (target == null || target is SectionHitCounter))
                UnityEventTools.RemovePersistentListener(evt, i);
        }
        int before = evt.GetPersistentEventCount();
        UnityEventTools.AddPersistentListener(evt, new UnityAction<GameObject>(counter.RegisterHitEvent));
        EditorUtility.SetDirty(owner);
        bool ok = evt.GetPersistentEventCount() == before + 1 && evt.GetPersistentTarget(before) == counter &&
                  evt.GetPersistentMethodName(before) == nameof(SectionHitCounter.RegisterHitEvent);
        if (!ok) Debug.LogError($"{Tag} '{owner.name}' 영구 배선 확인 실패.", owner);
        return ok;
    }

    // ───────────────────────── 확인(값은 쓰지 않는다) ─────────────────────────

    /// <summary>발사구 14: 이름·부모·순서(C1-1·C2-3), 피벗·yaw(초안 §2 ±0.001·0.1°), 인스펙터 값 = 팀 기본 [확정 §2-2].</summary>
    private static int VerifyLaunchers(Transform generated, List<ProjectileLauncher> launchers)
    {
        if (launchers.Count != LauncherSpecs.Length) Debug.LogError($"{Tag} 발사구 {launchers.Count}개 — 계약 C1-1은 {LauncherSpecs.Length}개.", generated);
        int bad = 0;
        for (int i = 0; i < launchers.Count; i++)
        {
            ProjectileLauncher l = launchers[i];
            if (l == null) { bad++; Debug.LogError($"{Tag} 발사구[{i}]가 null이다."); continue; }
            List<string> d = new List<string>();
            if (i < LauncherSpecs.Length)
            {
                LauncherSpec s = LauncherSpecs[i];
                if (l.name != LauncherPrefix + s.Id) d.Add($"이름 ≠ {LauncherPrefix}{s.Id}(C1-1 순서)");
                CheckPose(generated, l.transform, s.Pivot, Quaternion.Euler(0f, s.Yaw, 0f), d);
            }
            if (l.transform.parent == null || l.transform.parent.name != GimmickGroupName) d.Add($"부모 ≠ {GimmickGroupName}(C3·PTF-2)");
            if (!Near(l.warningSeconds, LauncherWarning)) d.Add($"warningSeconds {l.warningSeconds}≠{LauncherWarning}");
            if (!Near(l.restSeconds, LauncherRest)) d.Add($"restSeconds {l.restSeconds}≠{LauncherRest}");
            if (!Near(l.projectileSpeed, LauncherSpeed)) d.Add($"projectileSpeed {l.projectileSpeed}≠{LauncherSpeed}");
            if (!Near(l.projectileLifetime, LauncherLifetime)) d.Add($"projectileLifetime {l.projectileLifetime}≠{LauncherLifetime}");
            if (!Near(l.projectileRadius, LauncherRadius)) d.Add($"projectileRadius {l.projectileRadius}≠{LauncherRadius}");
            if (l.projectilePrefab != null) d.Add($"projectilePrefab '{l.projectilePrefab.name}'≠null");
            if (!l.autoActivateOnStart) d.Add("autoActivateOnStart false≠true");
            if (l.playerTag != LauncherPlayerTag) d.Add($"playerTag '{l.playerTag}'≠'{LauncherPlayerTag}'");
            if (d.Count > 0) { bad++; Debug.LogError($"{Tag} 발사구 '{l.name}' 불일치 [초안 §2·확정 §2-2] — 고치지 않음: {string.Join(", ", d)}", l); }
        }
        return bad;
    }

    /// <summary>줄다리 26: 이름·부모·순서, 위치(A·B 중점)·회전 (−90,0,0), 인스펙터 값 전부(초안 §3 + 판정 9), LineRenderer alignment,
    /// anchorA/B = anchors[i*2]·[i*2+1](C1-1), 경간 0.5 초과·maxSpan 이하.</summary>
    private static int VerifyBridges(Transform generated, List<ThreadBridge> bridges, List<ThreadAnchor> anchors)
    {
        if (bridges.Count != BridgeSpecs.Length) Debug.LogError($"{Tag} 줄다리 {bridges.Count}개 — 계약 C1-1은 {BridgeSpecs.Length}개.", generated);
        int bad = 0;
        for (int i = 0; i < bridges.Count; i++)
        {
            ThreadBridge b = bridges[i];
            if (b == null) { bad++; Debug.LogError($"{Tag} 줄다리[{i}]가 null이다."); continue; }
            List<string> d = new List<string>();
            if (i < BridgeSpecs.Length)
            {
                BridgeSpec s = BridgeSpecs[i];
                if (b.name != BridgePrefix + s.Id) d.Add($"이름 ≠ {BridgePrefix}{s.Id}(C1-1 순서)");
                CheckPose(generated, b.transform, (s.A + s.B) * 0.5f, BridgeLocalRot, d);
            }
            if (b.transform.parent == null || b.transform.parent.name != GimmickGroupName) d.Add($"부모 ≠ {GimmickGroupName}(C3)");
            if (b.maxSpans != BridgeMaxSpans) d.Add($"maxSpans {b.maxSpans}≠{BridgeMaxSpans}");
            if (!Near(b.segmentWidth, BridgeSegmentWidth)) d.Add($"segmentWidth {b.segmentWidth}≠{BridgeSegmentWidth}");
            if (!Near(b.baseSag, BridgeBaseSag)) d.Add($"baseSag {b.baseSag}≠{BridgeBaseSag}");
            if (!Near(b.sagPerWeight, BridgeSagPerWeight)) d.Add($"sagPerWeight {b.sagPerWeight}≠{BridgeSagPerWeight}");
            if (!Near(b.lineWidth, BridgeLineWidth)) d.Add($"lineWidth {b.lineWidth}≠{BridgeLineWidth}");
            if (!Near(b.maxSpan, BridgeMaxSpanDefault)) d.Add($"maxSpan {b.maxSpan}≠{BridgeMaxSpanDefault}");
            if (b.segmentCount != BridgeSegmentCountDefault) d.Add($"segmentCount {b.segmentCount}≠{BridgeSegmentCountDefault}");
            if (!Near(b.segmentThickness, BridgeThicknessDefault)) d.Add($"segmentThickness {b.segmentThickness}≠{BridgeThicknessDefault}");
            if (!Near(b.segmentOverlap, BridgeOverlapDefault)) d.Add($"segmentOverlap {b.segmentOverlap}≠{BridgeOverlapDefault}");
            if (!Near(b.maxSag, BridgeMaxSagDefault)) d.Add($"maxSag {b.maxSag}≠{BridgeMaxSagDefault}");
            if (!Near(b.sagResponseSpeed, BridgeSagSpeedDefault)) d.Add($"sagResponseSpeed {b.sagResponseSpeed}≠{BridgeSagSpeedDefault}");
            if (!Near(b.riderCaptureRadius, BridgeCaptureDefault)) d.Add($"riderCaptureRadius {b.riderCaptureRadius}≠{BridgeCaptureDefault}");
            LineRenderer lr = b.GetComponent<LineRenderer>();
            if (lr == null) d.Add("LineRenderer 없음");
            else if (lr.alignment != LineAlignment.TransformZ) d.Add($"LineRenderer.alignment {lr.alignment}≠TransformZ");
            if (b.anchorA == null || b.anchorB == null) d.Add("anchorA/B 비어 있음(고정 다리가 아니게 된다 — 핀을 요구)");
            else
            {
                if (anchors.Count > i * 2 + 1 && (b.anchorA != anchors[i * 2] || b.anchorB != anchors[i * 2 + 1]))
                    d.Add("anchorA/B ≠ 끝 고리 목록 [i*2], [i*2+1](C1-1)");
                float span = Vector3.Distance(b.anchorA.transform.position, b.anchorB.transform.position);
                if (span <= BridgeMinSpan || span > b.maxSpan) d.Add($"경간 {span:0.###} ∉ ({BridgeMinSpan}, {b.maxSpan}]");
            }
            if (d.Count > 0) { bad++; Debug.LogError($"{Tag} 줄다리 '{b.name}' 불일치 [판정 9·초안 §2·§3] — 고치지 않음: {string.Join(", ", d)}", b); }
        }
        return bad;
    }

    /// <summary>끝 고리 52: 이름 _A/_B·부모·위치(초안 §2 줄다리 표 ±0.001), connectRange 0, lockToSidePlane 팀 기본 false.
    /// generated 아래 ThreadAnchor는 이 52개뿐이어야 한다(흔들기 고리 없음 [판정 8]).</summary>
    private static int VerifyAnchors(Transform generated, List<ThreadAnchor> anchors)
    {
        if (anchors.Count != BridgeSpecs.Length * 2) Debug.LogError($"{Tag} 끝 고리 {anchors.Count}개 — 계약 C1-1은 {BridgeSpecs.Length * 2}개.", generated);
        int bad = 0;
        HashSet<ThreadAnchor> known = new HashSet<ThreadAnchor>();
        for (int i = 0; i < anchors.Count; i++)
        {
            ThreadAnchor a = anchors[i];
            if (a == null) { bad++; Debug.LogError($"{Tag} 끝 고리[{i}]가 null이다."); continue; }
            known.Add(a);
            List<string> d = new List<string>();
            if (i / 2 < BridgeSpecs.Length)
            {
                BridgeSpec s = BridgeSpecs[i / 2];
                bool isA = i % 2 == 0;
                string expect = BridgePrefix + s.Id + (isA ? "_A" : "_B");
                if (a.name != expect) d.Add($"이름 ≠ {expect}(C1-1 [A,B] 번갈아)");
                Vector3 lp = generated.InverseTransformPoint(a.transform.position);
                Vector3 want = isA ? s.A : s.B;
                if ((lp - want).sqrMagnitude > PoseTol * PoseTol) d.Add($"위치(섹터 로컬) {V(lp)} ≠ 초안 {V(want)}");
            }
            if (a.transform.parent == null || a.transform.parent.name != GimmickGroupName) d.Add($"부모 ≠ {GimmickGroupName}");
            if (!Near(a.connectRange, AnchorConnectRange)) d.Add($"connectRange {a.connectRange}≠{AnchorConnectRange}");
            if (a.lockToSidePlane) d.Add("lockToSidePlane true≠팀 기본 false");
            if (d.Count > 0) { bad++; Debug.LogError($"{Tag} 끝 고리 '{a.name}' 불일치 [초안 §2·§3] — 고치지 않음: {string.Join(", ", d)}", a); }
        }
        foreach (ThreadAnchor a in generated.GetComponentsInChildren<ThreadAnchor>(true))
            if (!known.Contains(a)) { bad++; Debug.LogError($"{Tag} 끝 고리 목록에 없는 ThreadAnchor '{a.name}' — 흔들기 고리 금지 [판정 8].", a); }
        return bad;
    }

    /// <summary>[판정 8·10·계약 C1-5·초안 §3 "두지 않는 것"] generated 아래 실타래 계열 컴포넌트(형식 이름 Thread*·DreamThread*)는 줄다리·끝 고리만 허용한다.
    /// 기믹 그룹 아래에는 발사구(+예고 표시)·줄다리·끝 고리 말고 스크립트가 없어야 한다.</summary>
    private static int VerifyNoExtraThreadParts(Transform generated, Transform gimmicks)
    {
        int n = 0;
        foreach (MonoBehaviour mb in generated.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            System.Type t = mb.GetType();
            if (t == typeof(ThreadBridge) || t == typeof(ThreadAnchor)) continue;
            if (t.Name.StartsWith("Thread", System.StringComparison.Ordinal) || t.Name.StartsWith("DreamThread", System.StringComparison.Ordinal))
            { n++; Debug.LogError($"{Tag} 실타래 계열 '{t.Name}' on '{mb.name}' — S3에 두지 않는다 [판정 8·10·계약 C1-5].", mb); }
        }
        if (gimmicks != null)
            foreach (MonoBehaviour mb in gimmicks.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                System.Type t = mb.GetType();
                if (t == typeof(ProjectileLauncher) || t == typeof(ProjectileWarning) || t == typeof(ThreadBridge) || t == typeof(ThreadAnchor)) continue;
                if (t.Name.StartsWith("Thread", System.StringComparison.Ordinal) || t.Name.StartsWith("DreamThread", System.StringComparison.Ordinal)) continue; // 위에서 셈
                n++; Debug.LogError($"{Tag} '{GimmickGroupName}' 아래 초안 §3에 없는 컴포넌트 '{t.Name}' on '{mb.name}'.", mb);
            }
        return n;
    }

    /// <summary>계약 C3·지시서 완료 조건: generated 아래 S3_Gimmicks·S3_Respawn 각 1개, 기믹 이름 개수(14·26·52), 복귀 C3 이름 7개.</summary>
    private static void VerifyGroups(Transform generated, Transform gimmicks, Transform group)
    {
        int gim = 0, res = 0;
        for (int i = 0; i < generated.childCount; i++)
        {
            string n = generated.GetChild(i).name;
            if (n == GimmickGroupName) gim++;
            else if (n == GroupName) res++;
        }
        if (res != 1) Debug.LogError($"{Tag} generated 아래 '{GroupName}' {res}개 — 1개여야 한다.", generated);
        if (gimmicks != null && gimmicks.parent == generated && gim != 1) Debug.LogError($"{Tag} generated 아래 '{GimmickGroupName}' {gim}개 — 1개여야 한다.", generated);
        if (gimmicks != null)
        {
            int nl = 0, nb = 0, na = 0;
            for (int i = 0; i < gimmicks.childCount; i++)
            {
                Transform c = gimmicks.GetChild(i);
                if (c.name.StartsWith(LauncherPrefix + "PL_", System.StringComparison.Ordinal) && c.GetComponent<ProjectileLauncher>() != null) nl++;
                else if (c.name.StartsWith(BridgePrefix + "BR_", System.StringComparison.Ordinal) && c.GetComponent<ThreadBridge>() != null) nb++;
                else if (c.name.StartsWith(BridgePrefix + "BR_", System.StringComparison.Ordinal) && c.GetComponent<ThreadAnchor>() != null) na++;
            }
            if (nl != LauncherSpecs.Length || nb != BridgeSpecs.Length || na != BridgeSpecs.Length * 2)
                Debug.LogError($"{Tag} '{gimmicks.name}' 아래 발사구 {nl}/{LauncherSpecs.Length}·줄다리 {nb}/{BridgeSpecs.Length}·끝 고리 {na}/{BridgeSpecs.Length * 2}(계약 C3).", gimmicks);
        }
        foreach (string n in new[] { CpStartName, CpSaveName, SspStartName, SspSaveName, CounterName, OobName, BridgeAdapterName })
            if (group.Find(n) == null) Debug.LogError($"{Tag} '{GroupName}/{n}' 없음(계약 C3).", group);
    }

    /// <summary>1차 C3: 섹터 씬 루트는 섹터 루트 1개뿐이어야 한다(팀 메뉴 생성물 잔류 0). 확인만 — 지우는 것은 CreateByTeamMenu가 한다.</summary>
    private static void VerifyNoStrayRoots(Transform generated)
    {
        GameObject sectorRoot = generated.root.gameObject;
        foreach (GameObject r in generated.gameObject.scene.GetRootGameObjects())
            if (r != sectorRoot) Debug.LogError($"{Tag} 씬 루트에 남은 오브젝트 '{r.name}'(씬 루트 잔류 0 위반).", r);
    }

    // ───────────────────────── 팀 생성 메뉴 ─────────────────────────

    /// <summary>팀 생성 메뉴를 실행하고(public static 함수는 직접, private 메뉴는 EditorApplication.ExecuteMenuItem) 생성물을 즉시 parent 아래로 옮긴다.
    /// S5_Builder.MenuCreate 방식: Selection으로 받고, 배치 모드에서 Selection이 비면(S5-R §0-2 "모르겠다") 실행 전후 씬 루트 비교로 찾는다.
    /// 이번 호출로 새로 생긴 루트 중 옮기지 못한 것은 지운다(씬 루트 잔류 0, 1차 C3).</summary>
    private static T CreateByTeamMenu<T>(System.Func<bool> create, string menuLabel, Transform parent) where T : Component
    {
        HashSet<GameObject> before = AllRoots();
        Selection.activeGameObject = null;
        if (!create())
        {
            Debug.LogError($"{Tag} 팀 메뉴 '{menuLabel}' 실행 실패.");
            return null;
        }

        T made = null;
        GameObject sel = Selection.activeGameObject;
        if (sel != null && !before.Contains(sel) && sel.transform.parent == null) made = sel.GetComponent<T>();
        List<GameObject> fresh = new List<GameObject>();
        foreach (GameObject r in AllRoots()) if (!before.Contains(r)) fresh.Add(r);
        if (made == null)
            foreach (GameObject r in fresh)
                if (r.GetComponent<T>() != null) { made = r.GetComponent<T>(); break; }

        if (made != null) made.transform.SetParent(parent, true);
        foreach (GameObject r in fresh)
        {
            if (r == null || (made != null && r == made.gameObject)) continue;
            Debug.LogError($"{Tag} 팀 메뉴 '{menuLabel}' 호출 뒤 남은 씬 루트 '{r.name}' — 지운다(씬 루트 잔류 0).");
            Object.DestroyImmediate(r);
        }
        if (made == null) Debug.LogError($"{Tag} 팀 메뉴 '{menuLabel}' 생성물({typeof(T).Name})을 찾지 못했다.");
        return made;
    }

    private static HashSet<GameObject> AllRoots()
    {
        HashSet<GameObject> set = new HashSet<GameObject>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (s.isLoaded) foreach (GameObject r in s.GetRootGameObjects()) set.Add(r);
        }
        return set;
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    /// <summary>섹터 로컬 위치·회전으로 놓는다(Generated의 세계 변환을 곱한다 — S5_Builder.Place와 같음).</summary>
    private static void Place(Transform generated, Transform t, Vector3 local, Quaternion localRot) =>
        t.SetPositionAndRotation(generated.TransformPoint(local), generated.rotation * localRot);

    /// <summary>섹터 로컬 위치(±0.001)·회전(±0.1°) 대조 — 다르면 d에 적는다.</summary>
    private static void CheckPose(Transform generated, Transform t, Vector3 local, Quaternion localRot, List<string> d)
    {
        Vector3 lp = generated.InverseTransformPoint(t.position);
        if ((lp - local).sqrMagnitude > PoseTol * PoseTol) d.Add($"위치(섹터 로컬) {V(lp)} ≠ 초안 {V(local)}");
        Quaternion lr = Quaternion.Inverse(generated.rotation) * t.rotation;
        float ang = Quaternion.Angle(lr, localRot);
        if (ang > RotTolDeg) d.Add($"회전(섹터 로컬) {V(lr.eulerAngles)} ≠ {V(localRot.eulerAngles)} [{ang:0.##}°]");
    }

    private static string V(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
    private static Bounds MinMax(Vector3 min, Vector3 max) { Bounds b = new Bounds(); b.SetMinMax(min, max); return b; }
    private static bool HasSize(Bounds b) => b.size.x > 1e-4f && b.size.y > 1e-4f && b.size.z > 1e-4f;
    private static bool Near(float a, float b) => Mathf.Abs(a - b) <= 1e-4f;

    private static Vector3 CheckVec(string field, Vector3 v, Vector3 draft)
    {
        if ((v - draft).sqrMagnitude > Tol * Tol)
            Debug.LogError($"{Tag} refs.{field} {v} ≠ 초안 {draft} — refs 값을 쓴다(빌더 지형 기준). S3-B·초안 확인.");
        return v;
    }

    /// <summary>refs Bounds가 비었으면(빌더가 채우지 않음) 초안 값으로 대신하고 LogError, 다르면 LogError 후 refs 값.</summary>
    private static Bounds CheckBounds(string field, Bounds b, Bounds draft)
    {
        if (!HasSize(b))
        {
            Debug.LogError($"{Tag} refs.{field}가 비어 있다 — 초안 값 min{draft.min} max{draft.max}로 대신한다(S3-B 확인).");
            return draft;
        }
        if ((b.min - draft.min).sqrMagnitude > Tol * Tol || (b.max - draft.max).sqrMagnitude > Tol * Tol)
            Debug.LogError($"{Tag} refs.{field} min{b.min} max{b.max} ≠ 초안 min{draft.min} max{draft.max} — refs 값을 쓴다. S3-B·초안 확인.");
        return b;
    }

    /// <summary>세계 AABB → generated 로컬 AABB(generated가 축 정렬이면 정확).</summary>
    private static Bounds LocalBounds(Transform generated, Bounds world)
    {
        Bounds b = new Bounds(generated.InverseTransformPoint(world.center), Vector3.zero);
        Vector3 e = world.extents;
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    b.Encapsulate(generated.InverseTransformPoint(world.center + new Vector3(sx * e.x, sy * e.y, sz * e.z)));
        return b;
    }

    /// <summary>팀 RespawnZone은 축 정렬 배치를 전제한다(RespawnZone.cs:186-188) — Generated가 y축 90° 배수 회전이 아니면 경고.</summary>
    private static void CheckAxisAligned(Transform generated)
    {
        Vector3 e = generated.rotation.eulerAngles;
        bool ok = Mathf.Abs(Mathf.DeltaAngle(e.x, 0f)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(e.z, 0f)) < 0.01f &&
                  Mathf.Abs(Mathf.DeltaAngle(e.y, Mathf.Round(e.y / 90f) * 90f)) < 0.01f;
        if (!ok) Debug.LogWarning($"{Tag} Generated 회전 {e}가 축 정렬이 아니다 — 팀 RespawnZone 박스가 커진다(RespawnZone.cs:186-188).", generated);
    }
}
#endif
