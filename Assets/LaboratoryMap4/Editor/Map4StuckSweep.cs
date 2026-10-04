#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// [INF2 — HANDOFF §3-2] 끼임 스윕. 도형 3종(구·정육면체·정사면체) "탐침"을 섹터 격자의 모든 윗면에
/// 떨어뜨려 플레이 모드 물리로 관통(FELL_THROUGH)·박힘(PENETRATING)·끼임(WEDGED)을 찾는다.
///
/// 실행 규약(지시서 C5): <c>tools/run_unity.ps1 -Method Map4StuckSweep.Run -Tag R1_sweep
/// -Extra 'sweepSectors=1,5'</c> — <b>-quit 없이</b>(플레이 모드 전환이 비동기라 -quit이면 즉시 종료된다).
/// 틀은 Map4PlayTestRunner.cs:38-160과 같다 — SessionState pending 키 + [InitializeOnLoadMethod]
/// Bootstrap + playModeStateChanged → EnteredPlayMode에서 Driver 생성. 종료는 배치 모드면
/// EditorApplication.Exit(code), 아니면 isPlaying=false. <b>씬은 저장하지 않는다.</b>
///
/// 명령행 인자(모두 선택, Environment.GetCommandLineArgs):
///   -sweepSectors 1,5       스윕할 섹터(기본 1~8) [제안 컨트롤타워]
///   -sweepSpacing 2         상세 섹터(DetailedSectors = 1~8) 격자 간격 U [제안] — [INF2-2] 판정 B9로 2·3·4, [INF2-3] 계약 R2-C4로 6·7·8 추가
///   -sweepCoarseSpacing 4   DetailedSectors 밖 섹터의 격자 간격 U [제안] — [INF2-3] 이후 기본 적용 섹터 없음(인자는 호환용으로 남김)
///   -sweepTimeScale 20      Time.timeScale 가속(1~100, 끝나면 원복) [추정 — 지시서에 값 없음, 결과엔 영향 없음:
///                           물리 스텝 수를 fixedDeltaTime으로 세므로 같은 스텝 수가 더 빨리 돌 뿐]
///   -sweepWave 150          웨이브당 도형별 탐침 수(동시 시뮬 = 3×이 값) [추정 — 성능용]
///
/// 금지 준수: fixedDeltaTime·ProjectSettings·레이어 충돌 행렬 불변(행렬은 읽기만 — 탐침과 실제로 부딪히는
/// 레이어만 레이캐스트 대상으로 삼기 위해 GetIgnoreLayerCollision을 읽는다), 팀 코드 무수정, 팀 private
/// 리플렉션 없음, 다른 러너 호출 없음. 실제 플레이어 3명은 팀 공개 API(PlayerMover.ExternallyDriven —
/// RespawnController가 쓰는 "조작 차단" 창구와 같은 것)로 조작을 끊고 멀리 치운 뒤 kinematic으로 둔다
/// (런타임만, 저장 안 함).
///
/// [R2] 표본 = 주 격자(칸 중앙) + 반 칸 보조 격자(x·z 모두 s/2 어긋남) + 벽 인접 표본(선 면에서 탐침 가장자리가
/// 0.05U 떨어지게). 주 격자 열이 벽면 위에 겹쳐 매몰로 빠지는 띠(예: S1 복도 x=±5)를 메운다. 시작 겹침(>0.05) 표본은
/// 끝 분류와 무관하게 도형별 최대 300건까지 섹터 로컬 좌표·상대 콜라이더를 결과 파일에 남긴다(exit 규칙 불변).
///
/// [INF2-2 — 판정 B9, R3 지적] ① 벽 인접 표본은 맞은 면의 수평 법선 n_h 기준으로 둔다(q = 맞은 점 + n_h·wallClear —
/// 비스듬한 벽에서 광선 방향 기준 배치가 시작 겹침을 만들던 문제). 둔 뒤 탐침 형판(도형 3종 콜라이더·자세·스폰 높이 =
/// 윗면 + spawnOffset)으로 사전 겹침(> 0.05)을 검사해, 겹치면 n_h 방향으로 WallBackoffStep씩 WallBackoffMax까지 물리고,
/// 그래도 겹치면 버린다. 결과에 '벽 인접 시작 겹침'(도형별, 목표 0)·물림·버림 수를 낸다.
/// ② 음성 대조에 WEDGED 양성 대조 [wedge]를 더한다 — 4면 수직 벽 + V바닥 좁은 구덩이(실행 중 생성). Sphere·Cube가 낙하 후
/// (시작 깊이 ≤ 0.05) WEDGED로 잡혀야 negPass. ③ DetailedSectors = {1,2,3,4,5}. exit 식·Classify 문턱·요약 줄 형식 불변.
///
/// [INF2-2 M2R1] 사전 겹침 검사의 버림·물림을 도형별로 나눈다: 한 후보에서 통과한 도형만 그 자리(물린 자리)를 쓰고
/// (Surface.shapeMask), 겹친 도형만 물리거나 버린다(예전엔 한 도형만 겹쳐도 세 도형 모두 버렸다). 결과에 섹터별 버림 비율
/// (전 도형·도형별)과 '저머리(스폰 불가) 구역' 목록(윗면에서 위로 스폰 윗면 높이 안에 정적 콜라이더가 있어 버려지거나
/// 시작 겹침이 난 자리 — 천장 콜라이더별)을 따로 낸다. 판정 로직·exit 규칙 불변.
///
/// [INF2-3 — 계약 R2-C4] DetailedSectors = 1~8. exit 식·Classify 문턱·요약 줄 형식 불변.
///
/// [컨트롤타워 FIN2 판정 2026-09-29 — 순간이동 생성 인공물 제거, 진짜 좁은 틈은 원위치로 유지] 격자·반칸 표본에도 벽 인접 표본과
/// 같은 사전 겹침 검사(WallPrecheck 재사용)를 한다(PrecheckGridSamples). 반칸 격자점이 벽면 0.25U 옆이면 세모(반폭 ≈0.6)가 벽 두께를
/// 통째로 품은 채 순간이동으로 생성돼 물리가 밀어내지 못하는 표본 배치 인공물이 생긴다(S3 stagger PENETRATING 1건). 겹친 도형만
/// (도형별) 수평 8방향으로 WallBackoffStep씩 WallBackoffMax까지 옮겨 '같은 윗면 콜라이더'(높이 차 ≤ GridShiftHeightTol)에서 겹침 없는
/// 자리를 찾고, 못 찾으면 원래 자리를 그대로 쓴다(버리지 않음 — 도형 폭보다 좁은 진짜 틈은 계속 PENETRATING/WEDGED로 검출).
/// 음성 대조([gap]·[wedge] 등)는 SweepSector를 거치지 않고 RunNegativeControl이 직접 만든 표본이라 이 이동을 적용하지 않는다.
/// exit 식·분류 규칙·WALL_OV는 불변. 섹터×도형별 이동·원위치 수를 결과 txt·json에 낸다.
/// </summary>
public static class Map4StuckSweep
{
    private const string PendingKey = "Map4StuckSweep_Pending_20260928";
    private const string MasterScenePath = "Assets/LaboratoryMap4/Scenes/Map4_Master.unity";
    private const string LayoutPath = "Assets/LaboratoryMap4/Data/Map4Layout.asset";
    private const string LogTag = "[Map4StuckSweep]";

    // ── 수치표(지시서 INF2 "정확한 수치표") ────────────────────────────────────────────
    public const float WatchdogSeconds = 25f * 60f;       // [제안] 25분(realtime)
    public const float SurfaceMinNormalY = 0.5f;          // [제안] 윗면 판정 법선 y ≥ 0.5
    public const float DropHeight = 0.6f;                 // [제안] 윗면 + 0.6U (도형 중심 기준, 아래 SpawnOffset 참고)
    public const float SimSeconds = 2.0f;                 // [제안] 시뮬 2초
    public const float FellDrop = 1.0f;                   // [제안] 시작 윗면보다 1U 이상 아래
    public const float FellSupportRange = 0.6f;           // [제안] 아래 0.6U 이내 콜라이더 없음
    public const float PenetrationFail = 0.05f;           // [제안] ComputePenetration 깊이 > 0.05U
    public const float PushSpeed = 2.0f;                  // [제안] 수평 2U/s
    public const float PushSeconds = 0.4f;                // [제안] 0.4초씩
    public const float PushMinMove = 0.15f;               // [제안] 0.15U 이상 못 움직임
    public const float FlatFloorNormalY = 0.95f;          // [제안] 평평한 바닥 법선 y ≥ 0.95
    public const float NegativeGap = 0.8f;                // [제안] 음성 대조 틈 폭
    public const float DefaultSpacing = 2f;               // [제안]
    public const float DefaultCoarseSpacing = 4f;         // [제안]
    public const float DefaultTimeScale = 20f;            // [추정] 성능용
    public const int DefaultWave = 150;                   // [추정] 성능용
    public const int MaxFindingsPerBucket = 300;          // [추정] 출력 크기 제한(개수 집계는 전부)

    // ── [R2] 보조 표본(검문 R2 지적 1: 주 격자 열이 벽면 위에 겹치면 매몰로 빠져 벽 1U 안쪽 띠에 표본이 없다) ──
    // 표본 종류: 0=주 격자(칸 중앙), 1=반 칸 보조 격자(주 격자 네 점의 한가운데 — x·z 모두 s/2 어긋남),
    //            2=벽 인접(주·보조 윗면에서 수평 광선으로 선 면(|법선 y|<0.5)을 찾아, [INF2-2] 맞은 점에서 그 면의
    //              수평 법선 방향으로 wallClear(= 도형 최대 수평 반폭 + WallGapMargin) 떨어진 곳에 둔 표본. 형판 사전 겹침 검사 통과분만).
    public const int KGrid = 0, KStagger = 1, KWall = 2;
    public static readonly string[] KindNames = { "grid", "stagger", "wall" };
    public static readonly float[] WallProbeHeights = { 0.3f, 0.75f }; // [추정] 윗면 위 광선 높이 — 낮은 턱(0.3)·도형 키 1U 아래로 내려온 돌출부(0.75)
    public const float WallGapMargin = 0.05f;             // [추정] 탐침 가장자리 ↔ 벽면 여유(시작 겹침 방지)
    public const float WallSampleMinShift = 0.25f;        // [추정] 원래 표본에서 이만큼도 안 옮겨지면 이미 벽 인접 — 추가 안 함
    public const float WallDropSearch = 0.5f;             // [추정] 벽 인접 점의 바닥은 원래 윗면보다 최대 0.5 아래까지 찾는다
    // [INF2-2] 사전 겹침 검사로 n_h 방향 뒤로 물리는 간격·한도 [추정]. 한도 0.5 = 도형 폭 1.0U의 절반 — 이보다 더 물리면
    // 탐침 가장자리가 벽에서 0.55U 넘게 떨어져 '벽 인접' 띠(도형 폭 1U 안)를 벗어나고, 그 거리는 반 칸 보조 격자가 덮는다.
    // 간격 0.1 = 시작 겹침 문턱 0.05의 2배(한 번 물릴 때 문턱 한 개 이상을 확실히 넘는 폭).
    public const float WallBackoffStep = 0.1f;
    public const float WallBackoffMax = 0.5f;
    // [INF2-2 M2R1] 저머리 판정 광선 출발 높이(윗면 위) [추정] — BuriedBy 검사 높이(0.05)와 같게. 윗면 콜라이더 속에서 쏘지 않기 위함.
    public const float HeadRayStart = 0.05f;

    // [컨트롤타워 FIN2 판정 2026-09-29 — 순간이동 생성 인공물 제거, 진짜 좁은 틈은 원위치로 유지]
    // 격자·반칸 표본 사전 겹침 이동. 이동 간격·한도는 벽 인접과 같은 WallBackoffStep(0.1)·WallBackoffMax(0.5)를 그대로 쓴다.
    public const float GridShiftHeightTol = 0.02f;  // [컨트롤타워 지정] 옮긴 자리 윗면 높이가 원래 윗면과 이 이내여야 '같은 윗면'
    public const float GridShiftRayUp = 0.1f;       // [추정] 옮긴 자리 재탐색 광선 출발 = 원래 윗면 + 0.1(윗면 위, BuriedBy 검사 높이 0.05보다 위)
    public const float GridShiftRayDown = 0.05f;    // [추정] 광선 길이 = 위 0.1 + 아래 0.05 — 높이 허용 0.02를 여유 있게 덮는다

    // [INF2-2] WEDGED 양성 대조 구덩이 [제안 — 치수 근거는 RunNegativeControl 주석·보고서 [계산]].
    public const float WedgeSlopeDeg = 25f;       // V바닥 경사. 발밑 ny = cos25 = 0.906 < 0.95, 네모 하단 높이 0.5·tan25 = 0.233 < 발밑 광선 여유 0.3
    public const float WedgeClearance = 0.05f;    // 도형 폭(bounds 최대 수평 폭)과 수직 벽 사이 한쪽 틈 → 최대 수평 이동 0.1 < 0.15
    public const float WedgeWallTop = 3.0f;       // 벽 윗면(꼭짓점 기준) — 스폰 상단(네모 0.6+1.0 = 1.6)보다 높게
    public const float WedgeWallBottom = -1.0f;   // 벽 밑면(꼭짓점 기준) — 경사 상자 밑면(최저 약 −0.67)보다 낮게
    public const float WedgeWallThick = 1.0f;
    public const float WedgeSlopeThick = 0.6f;
    public const float WedgeSlopePastApex = 0.3f; // 경사 상자를 꼭짓점 너머로 늘여 두 상자가 겹치게(꼭짓점 이음매 틈 방지)

    // [판정 B9 · INF2-3 계약 R2-C4] 상세 섹터 = 1~8 전부(간격 sweepSpacing). 이 목록 밖 섹터만 sweepCoarseSpacing — 지금은 없음.
    // 섹터를 구체화할 때마다 넓힌다(1차 판정 9) — R3 계약 R2-C4로 1~8 전부(S6~S8 실지오메트리는 INT-2 반영 뒤).
    private static readonly int[] DetailedSectors = { 1, 2, 3, 4, 5, 6, 7, 8 };
    public static readonly string[] ShapeNames = { "Sphere", "Cube", "Tetrahedron" };

    public enum Cls { OK, FELL_THROUGH, PENETRATING, WEDGED, MOVING_CONTACT, FELL_OFF_EDGE }
    private static readonly Cls[] AllCls =
        { Cls.OK, Cls.FELL_THROUGH, Cls.PENETRATING, Cls.WEDGED, Cls.MOVING_CONTACT, Cls.FELL_OFF_EDGE };

    // ── 진입 ────────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Laboratory Map4/Run Stuck Sweep (INF2)")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning($"{LogTag} 이미 플레이 모드다 — 먼저 정지해라.");
            return;
        }
        if (!Map4SceneGuard.CanReplaceCurrentScenes())
        {
            Debug.LogWarning($"{LogTag} 사용자가 저장을 취소해 스윕을 중단한다.");
            return;
        }
        EditorSceneManager.OpenScene(MasterScenePath, OpenSceneMode.Single);
        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void Bootstrap()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode) return;
        if (!SessionState.GetBool(PendingKey, false)) return;
        SessionState.SetBool(PendingKey, false);

        if (Object.FindObjectOfType<Driver>() != null) return;
        new GameObject("Map4StuckSweep_RUNTIME").AddComponent<Driver>();
        Debug.Log($"{LogTag} 플레이 모드 진입 — 스윕 러너 생성.");
    }

    // ── 인자 ────────────────────────────────────────────────────────────────────────
    public sealed class SweepArgs
    {
        public List<int> sectors = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };
        public float spacing = DefaultSpacing;
        public float coarseSpacing = DefaultCoarseSpacing;
        public float timeScale = DefaultTimeScale;
        public int wave = DefaultWave;
        public readonly List<string> warnings = new List<string>();
    }

    /// <summary>명령행 파서(순수 함수). 값 토큰이 여러 개로 쪼개져 들어와도("-sweepSectors 1 5") 다음 '-' 토큰
    /// 전까지 모아 쉼표·세미콜론·공백으로 나눈다.</summary>
    public static SweepArgs ParseArgs(string[] argv)
    {
        SweepArgs a = new SweepArgs();
        if (argv == null) return a;
        for (int i = 0; i < argv.Length; i++)
        {
            string key = argv[i];
            if (key == null || !key.StartsWith("-sweep", StringComparison.OrdinalIgnoreCase)) continue;
            StringBuilder val = new StringBuilder();
            int j = i + 1;
            while (j < argv.Length && argv[j] != null && !argv[j].StartsWith("-"))
            {
                if (val.Length > 0) val.Append(',');
                val.Append(argv[j]);
                j++;
            }
            string v = val.ToString();
            switch (key.ToLowerInvariant())
            {
                case "-sweepsectors":
                {
                    List<int> ids = new List<int>();
                    foreach (string part in v.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                            && id >= 1 && id <= 8)
                        {
                            if (!ids.Contains(id)) ids.Add(id);
                        }
                        else a.warnings.Add($"-sweepSectors 값 '{part}' 무시(1~8 정수 아님)");
                    }
                    if (ids.Count > 0) { ids.Sort(); a.sectors = ids; }
                    else a.warnings.Add("-sweepSectors 유효 값 없음 — 기본 1~8 사용");
                    break;
                }
                case "-sweepspacing":
                    a.spacing = ParsePositive(v, DefaultSpacing, key, a.warnings, 0.25f, 50f);
                    break;
                case "-sweepcoarsespacing":
                    a.coarseSpacing = ParsePositive(v, DefaultCoarseSpacing, key, a.warnings, 0.25f, 50f);
                    break;
                case "-sweeptimescale":
                    a.timeScale = ParsePositive(v, DefaultTimeScale, key, a.warnings, 1f, 100f);
                    break;
                case "-sweepwave":
                    a.wave = Mathf.RoundToInt(ParsePositive(v, DefaultWave, key, a.warnings, 1f, 2000f));
                    break;
                default:
                    a.warnings.Add($"알 수 없는 인자 {key} 무시");
                    break;
            }
            i = j - 1;
        }
        return a;
    }

    private static float ParsePositive(string v, float def, string key, List<string> warn, float min, float max)
    {
        string first = v.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Length > 0
            ? v.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)[0] : "";
        if (float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && f >= min && f <= max)
            return f;
        warn.Add($"{key} 값 '{v}' 무효({min}~{max}) — 기본 {def} 사용");
        return def;
    }

    // ── 분류(순수 함수 — 파이썬 자기 검사가 같은 임계로 재구현해 대조한다) ─────────────────
    /// <param name="dropBelowSurface">시작 윗면 y − 끝 시점 탐침 하단 y(양수 = 윗면보다 아래).</param>
    /// <param name="supportBelow">끝 시점 탐침 하단 아래 0.6U 안에 (탐침·플레이어 아닌) 콜라이더가 있는가.</param>
    /// <param name="somethingAbove">끝 위치 위쪽(낙하 구간)에 (탐침·플레이어 아닌) 콜라이더가 있는가 — 관통과
    /// 가장자리 추락을 가르는 보조 판정([추정] 지시서 확장, 보고서 검토 요청 참고).</param>
    /// <param name="maxStaticPenetration">끝 시점 정적 콜라이더와의 최대 ComputePenetration 깊이.</param>
    /// <param name="maxPushMove">수평 4방향 밀기에서 가장 많이 움직인 수평 거리.</param>
    /// <param name="footNormalY">발밑 법선 y(발밑 콜라이더가 없으면 -1).</param>
    /// <param name="movingContact">낙하 시뮬 동안 움직이는 팀 기믹 콜라이더와 접촉했는가.</param>
    /// <remarks>[R1] movingContact면 반환값은 MOVING_CONTACT라서 기저 FELL_THROUGH/PENETRATING이 가려진다 —
    /// Finish가 underlying을 따로 세어 요약 줄 MC_FAIL·결과 파일에 드러낸다(exit 반영 여부는 검토 요청).</remarks>
    public static Cls Classify(float dropBelowSurface, bool supportBelow, bool somethingAbove,
        float maxStaticPenetration, float maxPushMove, float footNormalY, bool movingContact, out Cls underlying)
    {
        if (dropBelowSurface >= FellDrop && !supportBelow)
            underlying = somethingAbove ? Cls.FELL_THROUGH : Cls.FELL_OFF_EDGE;
        else if (maxStaticPenetration > PenetrationFail)
            underlying = Cls.PENETRATING;
        else if (maxPushMove < PushMinMove && footNormalY < FlatFloorNormalY)
            underlying = Cls.WEDGED;
        else
            underlying = Cls.OK;
        return movingContact ? Cls.MOVING_CONTACT : underlying;
    }

    // ── 탐침 ────────────────────────────────────────────────────────────────────────
    /// <summary>탐침 1개. 팀 조작 스크립트·Lab_AntiStuck 없이 콜라이더 + Rigidbody만. 낙하 시뮬 동안 닿은
    /// 콜라이더를 기록한다(MOVING_CONTACT 판정용).</summary>
    private class SweepProbe : MonoBehaviour
    {
        public int shape;
        public Rigidbody rb;
        public Collider col;
        public Vector3 colLocalPos;
        public Quaternion colLocalRot = Quaternion.identity;
        public Quaternion baseRot = Quaternion.identity;
        public CollisionDetectionMode cdm;
        public float bottomOffset;   // rb.position.y − 콜라이더 하단 y [계산 — 실행 시 bounds로 측정]
        public float queryRadius;    // 콜라이더 bounds extents 크기
        // [R1] 콜라이더 bounds 중심의 rb 로컬 오프셋(기준 자세에서 측정) [계산]. 발밑 법선 광선의 출발점으로 쓴다 —
        // 피벗은 네모에선 하단면, 세모에선 하단 0.211 아래라 피벗에서 쏘면 바닥을 못 맞힌다.
        public Vector3 centerLocal;
        public Vector3 parkPos;
        public bool recording;
        public readonly HashSet<Collider> contacts = new HashSet<Collider>();

        void OnCollisionEnter(Collision c) { if (recording && c.collider != null) contacts.Add(c.collider); }
        void OnCollisionStay(Collision c) { if (recording && c.collider != null) contacts.Add(c.collider); }
    }

    private sealed class ShapeTemplate
    {
        public string shape;
        public string sourceName;
        public string describe;
        public float spawnOffset;
        public float bottomOffset;
        public Vector3 centerLocal; // [R1] bounds 중심 − rb 위치(rb 로컬) [계산]
        public Vector3 size;
        public int layerMask; // 이 도형 콜라이더 레이어와 충돌하는 레이어(읽기만)
    }

    private sealed class Surface
    {
        public Vector3 world;
        public float normalY;
        public Collider collider;
        public string tag; // 음성 대조용("gap"/"open"), 섹터는 null
        public int kind;   // [R2] KGrid/KStagger/KWall
        public string wallName; // [R2] 벽 인접 표본이 붙은 면의 콜라이더 경로
        public int shapeMask = 7; // [INF2-2 M2R1] 이 표본을 쓰는 도형 비트(1<<도형). 격자·음성 대조는 세 도형 모두(7).
    }

    /// <summary>[INF2-2 M2R1] 저머리(스폰 불가) 구역 — 윗면에서 수직 위로 스폰 윗면 높이 안에 정적 콜라이더가 있는 자리를
    /// 그 천장 콜라이더별로 모은다(정보, exit 미반영).</summary>
    private sealed class LowHeadZone
    {
        public string ceiling;
        public readonly int[] wallDrop = new int[3]; // 벽 인접 사전 검사에서 버려진 도형별 수(첫 위치 기준)
        public readonly int[] startOv = new int[3];  // 격자·반칸(·벽 인접) 표본의 시작 겹침(>0.05) 도형별 수
        public float minHead = float.PositiveInfinity, maxHead = float.NegativeInfinity;
        public Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        public Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
    }

    private sealed class Finding
    {
        public string shape;
        public Cls cls;
        public Cls underlying;
        public Vector3 surfLocal;
        public Vector3 restLocal;
        public float drop;
        public float depth;
        public float startDepth;
        public float maxMove;
        public float footNy;
        public string surfCollider;
        public List<string> names = new List<string>();
        public string tag;
        public int kind;             // [R2] 표본 종류
        public string wallName;      // [R2] 벽 인접 표본의 벽 콜라이더
        public string startCollider; // [R2] 시작 시점 최대 관통 상대(정적 콜라이더), 없으면 null
    }

    private sealed class SweepCtx
    {
        public string label;
        public int id;
        public string name;
        public Transform root;       // 섹터 루트(로컬 좌표 기준). 음성 대조는 null → origin 사용.
        public Vector3 origin;
        public float spacing;
        public int columns;
        public int buried;
        public int dedup;
        public int bufferFull;
        public readonly List<Surface> surfaces = new List<Surface>();
        public readonly int[] samples = new int[3];
        public readonly int[] startOverlap = new int[3];
        // [R2] 표본 구성·시작 겹침 기록
        public int staggerColumns;
        public int wallDedup;
        public readonly int[] surfKind = new int[3];
        public readonly int[] buriedKind = new int[3];
        public readonly int[,] startOverlapKind = new int[3, 3]; // [도형, 종류]
        public readonly Dictionary<string, int> buriedBy = new Dictionary<string, int>();
        public readonly List<Finding> startFindings = new List<Finding>();
        public readonly int[] startFindingsCount = new int[3];
        public readonly int[,] counts = new int[3, 6];
        public readonly int[,] movingUnder = new int[3, 6]; // [R1] MOVING_CONTACT 표본의 기저 분류 집계
        public readonly List<Finding> findings = new List<Finding>();
        public readonly List<Finding> allNeg = new List<Finding>(); // 음성 대조 전용(전부 보관)
        public readonly Dictionary<string, int> bucketCount = new Dictionary<string, int>();
        public readonly SortedSet<string> movingNames = new SortedSet<string>();
        public readonly Dictionary<Collider, Pose> snapshot = new Dictionary<Collider, Pose>();
        // [INF2-2] 벽 인접 사전 겹침 검사 집계(표본 단위 — 한 표본을 세 도형이 같이 쓴다).
        public int wallCandidates;                               // 사전 검사까지 온 벽 인접 후보 수(중복 제거 뒤)
        public int wallPreShifted;                               // 첫 위치에서 겹쳐 n_h 방향으로 물린 뒤 통과
        public int wallPreDropped;                               // 한도까지 물려도 겹치거나 물린 자리에 윗면이 없어 버림
        public readonly int[] wallPreFirstOverlap = new int[3];  // 첫 위치(q = 맞은 점 + n_h·wallClear)에서 겹친 도형별 수
        public readonly List<string> wallPreDropExamples = new List<string>();
        // [컨트롤타워 FIN2 판정 2026-09-29 — 순간이동 생성 인공물 제거, 진짜 좁은 틈은 원위치로 유지]
        // 격자(0)·반칸(1) 표본 사전 겹침 이동 집계 [종류, 도형]. 도형 단위: 원위치에서 겹친 도형이 옮겨져 통과 = Moved,
        // 8방향·0.5 안에서 자리를 못 찾아 원위치 유지 = Stay. (겹침 도형 수 = Moved + Stay)
        public readonly int[,] gridPreMoved = new int[2, 3];
        public readonly int[,] gridPreStay = new int[2, 3];
        public static int GridPreSum(int[,] a) { int t = 0; foreach (int v in a) t += v; return t; }
        // [INF2-2 M2R1] 도형별 물림·버림(한 후보에서 도형마다 따로 판정). wallPreShifted = 물린 자리에서 통과한 도형이 하나라도
        // 있는 후보, wallPreDropped = 세 도형 모두 버린 후보, wallPrePartial = 일부 도형만 버린 후보.
        public int wallPrePartial;
        public readonly int[] wallPreShiftedByShape = new int[3];
        public readonly int[] wallPreDroppedByShape = new int[3];
        public readonly Dictionary<string, LowHeadZone> lowHead = new Dictionary<string, LowHeadZone>();
        public int lowHeadUnknownWallDrop;                       // 버렸지만 중심 수직 광선에 천장이 없는 도형-후보 수(수평 겹침)
        public float seconds = -1f;                             // 섹터 스윕 소요(realtime 초), 음성 대조는 -1
        public string error;
        public bool done;

        public Vector3 ToLocal(Vector3 w) => root != null ? root.InverseTransformPoint(w) : w - origin;
    }

    // ── 실행기 ──────────────────────────────────────────────────────────────────────
    private class Driver : MonoBehaviour
    {
        private readonly StringBuilder log = new StringBuilder();
        private readonly List<string> fatal = new List<string>();
        private readonly List<SweepCtx> ctxs = new List<SweepCtx>();
        private SweepCtx neg;
        private bool negPass;
        private string negDetail = "미실행";
        private float wedgeWidthUsed = -1f; // [INF2-2] WEDGED 양성 대조 구덩이 안쪽 폭(실측 형판 기준)
        private SweepArgs args;
        private bool finished;
        private bool watchdogHit;
        private float deadline;
        private float origTimeScale = 1f;
        private int dropSteps, pushSteps;
        private Map4Layout layout;
        private Map4Director director;

        private readonly ShapeTemplate[] templates = new ShapeTemplate[3];
        private readonly List<SweepProbe>[] pool = { new List<SweepProbe>(), new List<SweepProbe>(), new List<SweepProbe>() };
        private readonly HashSet<Collider> probeCols = new HashSet<Collider>();
        private readonly HashSet<Collider> playerCols = new HashSet<Collider>();
        private int surfaceMask = ~0;

        private readonly RaycastHit[] rayBuf = new RaycastHit[128];
        private readonly Collider[] ovBuf = new Collider[128];
        private static readonly WaitForFixedUpdate Wfu = new WaitForFixedUpdate();
        private readonly List<GameObject> negObjects = new List<GameObject>();
        private SweepCtx currentCtx; // 예외가 난 섹터를 오류로 표시하기 위한 현재 대상

        void Start()
        {
            origTimeScale = Time.timeScale;
            runStartRealtime = Time.realtimeSinceStartup;
            deadline = Time.realtimeSinceStartup + WatchdogSeconds;
            StartCoroutine(Guarded(MainBody(), "MainBody"));
        }

        void Update()
        {
            if (!finished && Time.realtimeSinceStartup > deadline)
            {
                watchdogHit = true;
                fatal.Add($"[치명] watchdog — {WatchdogSeconds / 60f:F0}분(realtime) 안에 스윕이 끝나지 않았다.");
                Finish();
            }
        }

        /// <summary>중첩 IEnumerator를 스택으로 직접 펼친다 — 어느 깊이에서 예외가 나도 이 자리에서 잡아
        /// 그 하위 단계만 끊고 계속한다(Map4PlayTestRunner의 "중첩 코루틴 예외 경로" 대응과 같은 취지).</summary>
        private IEnumerator Guarded(IEnumerator rootIt, string rootName)
        {
            Stack<KeyValuePair<IEnumerator, string>> stack = new Stack<KeyValuePair<IEnumerator, string>>();
            stack.Push(new KeyValuePair<IEnumerator, string>(rootIt, rootName));
            while (stack.Count > 0)
            {
                KeyValuePair<IEnumerator, string> top = stack.Peek();
                bool moved;
                object cur = null;
                try
                {
                    moved = top.Key.MoveNext();
                    if (moved) cur = top.Key.Current;
                }
                catch (Exception e)
                {
                    fatal.Add($"[예외] {top.Value}{(currentCtx != null ? " (" + currentCtx.label + ")" : "")}: {e}");
                    if (currentCtx != null && currentCtx.error == null) currentCtx.error = "예외: " + e.GetType().Name + " " + e.Message;
                    stack.Pop();
                    continue;
                }
                if (!moved) { stack.Pop(); continue; }
                if (cur is IEnumerator nested)
                {
                    stack.Push(new KeyValuePair<IEnumerator, string>(nested, top.Value + ">" + nested.GetType().Name));
                    continue;
                }
                yield return cur;
            }
            if (rootName == "MainBody") Finish();
        }

        private IEnumerator MainBody()
        {
            args = ParseArgs(Environment.GetCommandLineArgs());
            foreach (string w in args.warnings) log.AppendLine("[인자 경고] " + w);

            float fdt = Time.fixedDeltaTime; // 읽기만(불변)
            dropSteps = Mathf.CeilToInt(SimSeconds / fdt - 1e-4f);
            pushSteps = Mathf.CeilToInt(PushSeconds / fdt - 1e-4f);

            layout = AssetDatabase.LoadAssetAtPath<Map4Layout>(LayoutPath);
            if (layout == null) { fatal.Add($"[치명] {LayoutPath} 없음 — 섹터 경계를 알 수 없다."); yield break; }

            director = Object.FindObjectOfType<Map4Director>();
            if (director == null) { fatal.Add("[치명] Map4Director를 찾지 못했다."); yield break; }

            float loadDeadline = Time.realtimeSinceStartup + 30f;
            while (director.LoadedSectorCount < 8 && Time.realtimeSinceStartup < loadDeadline) yield return null;
            log.AppendLine($"섹터 로드: {director.LoadedSectorCount}/8");
            // Director.RunStartup이 스폰을 끝내면 플레이어 useGravity를 되돌린다(Map4Director.cs RunStartup) —
            // 그 뒤에 파킹해야 Director가 나중에 섹터1 슬롯으로 다시 순간이동시키지 않는다.
            PlayerMover[] players = Object.FindObjectsOfType<PlayerMover>();
            float spawnDeadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < spawnDeadline)
            {
                bool allGravity = true;
                foreach (PlayerMover p in players)
                {
                    Rigidbody prb = p != null ? p.GetComponent<Rigidbody>() : null;
                    if (prb != null && !prb.useGravity) { allGravity = false; break; }
                }
                if (allGravity) break;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f); // PlayerShapeIdentity.Start 적용 여유

            if (!BuildTemplates(players)) yield break;
            ParkPlayers(players);
            BuildPool();
            ComputeSurfaceMask();

            Time.timeScale = args.timeScale;
            log.AppendLine($"인자: sectors={string.Join(",", args.sectors)} spacing={F(args.spacing)} " +
                           $"coarse={F(args.coarseSpacing)} timeScale={F(args.timeScale)} wave={args.wave} " +
                           $"fixedDeltaTime={F(fdt)}(불변) dropSteps={dropSteps} pushSteps={pushSteps}");

            yield return RunNegativeControl();
            currentCtx = null;

            foreach (int id in args.sectors)
            {
                SweepCtx ctx = new SweepCtx { id = id, label = "S" + id };
                ctxs.Add(ctx);
                currentCtx = ctx;
                float t0 = Time.realtimeSinceStartup;
                yield return SweepSector(ctx);
                ctx.seconds = Time.realtimeSinceStartup - t0; // [INF2-2] 섹터별 소요(상세 섹터 확대 뒤 watchdog 25분 여유 확인용)
                ctx.done = ctx.error == null;
                currentCtx = null;
            }
        }

        private float runStartRealtime = -1f; // [INF2-2] Start 시각(realtime) — 전체 소요 출력용

        // ── 준비 ──────────────────────────────────────────────────────────────────
        private bool BuildTemplates(PlayerMover[] players)
        {
            for (int s = 0; s < 3; s++)
            {
                PlayerMover src = null;
                foreach (PlayerMover p in players)
                    if (p != null && p.name.Contains(ShapeNames[s])) { src = p; break; }
                if (src == null) { fatal.Add($"[치명] 씬에서 '{ShapeNames[s]}' 플레이어(Variant 인스턴스)를 찾지 못했다."); return false; }

                Rigidbody rb = src.GetComponent<Rigidbody>();
                Collider solid = FindSolidCollider(rb);
                if (rb == null || solid == null) { fatal.Add($"[치명] '{src.name}' Rigidbody/솔리드 콜라이더 없음."); return false; }

                savedCdm[s] = rb.collisionDetectionMode;
                templates[s] = new ShapeTemplate
                {
                    shape = ShapeNames[s],
                    sourceName = src.name,
                    describe = $"원천='{src.name}'(런타임 인스턴스 — Variant 프리팹 + Lab_ShapeProfileApplier·PlayerShapeIdentity 적용 후) " +
                               $"collider={DescribeCollider(solid)} mat={(solid.sharedMaterial != null ? solid.sharedMaterial.name : "(없음)")} " +
                               $"mass={F(rb.mass)} drag={F(rb.drag)} angularDrag={F(rb.angularDrag)} cdm={rb.collisionDetectionMode} " +
                               $"interp={rb.interpolation} constraints={rb.constraints} maxAngVel={F(rb.maxAngularVelocity)} " +
                               $"solverIt={rb.solverIterations}/{rb.solverVelocityIterations} layer={LayerMask.LayerToName(solid.gameObject.layer)}({solid.gameObject.layer})"
                };
            }
            return true;
        }

        private static Collider FindSolidCollider(Rigidbody rb)
        {
            if (rb == null) return null;
            foreach (Collider c in rb.GetComponentsInChildren<Collider>())
                if (!c.isTrigger) return c;
            return null;
        }

        private static string DescribeCollider(Collider c)
        {
            switch (c)
            {
                case SphereCollider sc: return $"Sphere(r={F(sc.radius)},c={V(sc.center)})";
                case BoxCollider bc: return $"Box(size={V(bc.size)},c={V(bc.center)})";
                case CapsuleCollider cc: return $"Capsule(r={F(cc.radius)},h={F(cc.height)},dir={cc.direction})";
                case MeshCollider mc: return $"Mesh({(mc.sharedMesh != null ? mc.sharedMesh.name : "null")},convex={mc.convex})";
                default: return c.GetType().Name;
            }
        }

        /// <summary>실제 플레이어 3명: 팀 공개 API ExternallyDriven으로 조작·감쇠 경로를 끊고(RespawnController와 같은
        /// 창구), 멀리 치우고, kinematic(CCD 경고 방지로 Discrete 먼저). 런타임만 — 씬 저장 없음.</summary>
        private void ParkPlayers(PlayerMover[] players)
        {
            int i = 0;
            foreach (PlayerMover p in players)
            {
                if (p == null) continue;
                foreach (Collider c in p.GetComponentsInChildren<Collider>(true)) playerCols.Add(c);
                p.ExternallyDriven = true;
                Rigidbody rb = p.GetComponent<Rigidbody>();
                if (rb == null) continue;
                Vector3 park = new Vector3(6000f + i * 20f, 300f, 6000f);
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rb.isKinematic = true;
                rb.position = park;
                rb.transform.position = park;
                i++;
            }
            log.AppendLine($"실제 플레이어 {i}명 파킹(ExternallyDriven=true·kinematic, x≥6000).");
        }

        private void BuildPool()
        {
            GameObject holder = new GameObject("SWEEP_Probes");
            PlayerMover[] players = Object.FindObjectsOfType<PlayerMover>();
            for (int s = 0; s < 3; s++)
            {
                PlayerMover src = null;
                foreach (PlayerMover p in players)
                    if (p != null && p.name.Contains(ShapeNames[s])) { src = p; break; }
                Rigidbody srcRb = src.GetComponent<Rigidbody>();
                Collider srcCol = FindSolidCollider(srcRb);
                // 프리팹 루트 회전(FreezeRotation 도형의 고정 자세) — 인스턴스 현재 회전이 아니라 에셋에서 읽는다.
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/LaboratoryMap4/Players/Player_{ShapeNames[s]}.prefab");
                Quaternion baseRot = prefab != null ? prefab.transform.rotation : Quaternion.identity;
                CollisionDetectionMode srcCdm = savedCdm[s]; // 파킹 전 런타임 값

                for (int i = 0; i < args.wave; i++)
                {
                    GameObject go = new GameObject($"SWEEP_Probe_{ShapeNames[s]}_{i:D3}");
                    go.transform.SetParent(holder.transform, false);
                    go.layer = srcRb.gameObject.layer;
                    go.transform.localScale = srcRb.transform.lossyScale;
                    Vector3 park = new Vector3(-8000f - s * 1000f - (i % 30) * 4f, 500f, -8000f - (i / 30) * 4f);
                    go.transform.SetPositionAndRotation(park, baseRot);

                    GameObject cgo = new GameObject("Collider");
                    cgo.layer = srcCol.gameObject.layer;
                    cgo.transform.SetParent(go.transform, false);
                    Transform rt = srcRb.transform, ct = srcCol.transform;
                    cgo.transform.localPosition = rt.InverseTransformPoint(ct.position);
                    cgo.transform.localRotation = Quaternion.Inverse(rt.rotation) * ct.rotation;
                    Vector3 rs = rt.lossyScale, cs = ct.lossyScale;
                    cgo.transform.localScale = new Vector3(SafeDiv(cs.x, rs.x), SafeDiv(cs.y, rs.y), SafeDiv(cs.z, rs.z));
                    Collider col = CopyCollider(srcCol, cgo);

                    Rigidbody rb = go.AddComponent<Rigidbody>();
                    rb.mass = srcRb.mass;
                    rb.drag = srcRb.drag;
                    rb.angularDrag = srcRb.angularDrag;
                    rb.useGravity = true; // Director가 로드 중 잠시 끄는 값이라 원천 대신 기본값 [계산]
                    rb.interpolation = srcRb.interpolation;
                    rb.constraints = srcRb.constraints;
                    rb.maxAngularVelocity = srcRb.maxAngularVelocity;
                    rb.maxDepenetrationVelocity = srcRb.maxDepenetrationVelocity;
                    rb.solverIterations = srcRb.solverIterations;
                    rb.solverVelocityIterations = srcRb.solverVelocityIterations;
                    rb.sleepThreshold = srcRb.sleepThreshold;
                    rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    rb.isKinematic = true;

                    SweepProbe probe = go.AddComponent<SweepProbe>();
                    probe.shape = s;
                    probe.rb = rb;
                    probe.col = col;
                    probe.colLocalPos = cgo.transform.localPosition;
                    probe.colLocalRot = cgo.transform.localRotation;
                    probe.baseRot = baseRot;
                    probe.cdm = srcCdm;
                    probe.parkPos = park;
                    pool[s].Add(probe);
                    probeCols.Add(col);
                }
            }

            Physics.SyncTransforms();
            // 형판 치수(첫 탐침 bounds로 측정) → 스폰 높이.
            float maxHalf = 0f;
            for (int s = 0; s < 3; s++)
            {
                SweepProbe p0 = pool[s][0];
                Bounds b = p0.col.bounds;
                float bottom = p0.transform.position.y - b.min.y;
                // [R2] 피벗에서 bounds 옆면까지 수평 최대 거리(x·z 중 큰 값) [계산] — 벽 인접 표본의 벽↔피벗 거리 산출용.
                Vector3 pv = p0.transform.position;
                float hx = Mathf.Max(b.max.x - pv.x, pv.x - b.min.x);
                float hz = Mathf.Max(b.max.z - pv.z, pv.z - b.min.z);
                maxHalf = Mathf.Max(maxHalf, Mathf.Max(hx, hz));
                templates[s].bottomOffset = bottom;
                templates[s].size = b.size;
                // [계산] 지시서 "윗면 + 0.6U" = 구·정육면체 중심(하단 0.1U 위). 하단 오프셋이 0.5보다 큰 도형은
                // 윗면에 파묻혀 시작하지 않도록 하단 0.1U 위로 올린다(= max(0.6, 하단오프셋+0.1)).
                templates[s].spawnOffset = Mathf.Max(DropHeight, bottom + 0.1f);
                // [R1] bounds 중심을 rb 로컬로 저장(파킹 자세 = baseRot). 회전하는 구는 중심이 피벗과 같거나 몸체에
                // 고정된 점이라 rot을 곱하면 되고, 네모·세모는 FreezeRotation이라 기준 자세 그대로다.
                Vector3 centerLocal = Quaternion.Inverse(p0.transform.rotation) * (b.center - p0.transform.position);
                templates[s].centerLocal = centerLocal;
                foreach (SweepProbe p in pool[s])
                {
                    p.bottomOffset = bottom;
                    p.queryRadius = b.extents.magnitude;
                    p.centerLocal = centerLocal;
                }
            }
            // [R2] 벽 인접 표본은 세 도형이 같은 점을 쓰므로 가장 넓은 도형 기준으로 떨어뜨린다 [계산].
            wallClear = maxHalf + WallGapMargin;

            // 탐침끼리 충돌 무시 — 풀 전체(=한 웨이브) 쌍마다 1회. 콜라이더를 끄지 않으므로 유지된다.
            List<Collider> all = new List<Collider>(probeCols);
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                    Physics.IgnoreCollision(all[i], all[j], true);
            // 파킹된 실제 플레이어와도 무시(멀리 있어 닿을 일은 없지만 안전장치).
            foreach (Collider pc in playerCols)
                foreach (Collider c in all)
                    if (pc != null) Physics.IgnoreCollision(pc, c, true);
            log.AppendLine($"탐침 풀: 도형별 {args.wave}개 × 3, IgnoreCollision 쌍 {all.Count * (all.Count - 1) / 2}개(+플레이어 {playerCols.Count}×{all.Count}).");
            for (int s = 0; s < 3; s++)
                log.AppendLine($"탐침 {ShapeNames[s]}: {templates[s].describe} | 하단오프셋={F(templates[s].bottomOffset)} " +
                               $"스폰높이=윗면+{F(templates[s].spawnOffset)} bounds={V(templates[s].size)} " +
                               $"중심오프셋(rb로컬)={V(templates[s].centerLocal)}");
            log.AppendLine($"[R2] 벽 인접 표본: 피벗↔벽면 거리 wallClear={F(wallClear)}(= 도형 최대 수평 반폭 {F(maxHalf)} + 여유 {F(WallGapMargin)}), " +
                           $"광선 높이 윗면+[{F(WallProbeHeights[0])}, {F(WallProbeHeights[1])}], 탐색 거리 = 섹터 격자 간격 + 0.05. " +
                           $"[INF2-2] 배치 q = 맞은 점 + 수평 법선 n_h·wallClear, 형판 사전 겹침(>{F(PenetrationFail)}) 시 n_h로 " +
                           $"{F(WallBackoffStep)}씩 최대 {F(WallBackoffMax)} 물림, 그래도 겹치면 버림 — [M2R1] 물림·버림은 도형별(통과한 도형만 그 자리 사용). " +
                           $"상세 섹터={string.Join(",", DetailedSectors)}.");
            for (int s = 0; s < 3; s++)
                log.AppendLine($"[M2R1] 저머리 판정 {ShapeNames[s]}: 윗면+{F(HeadRayStart)}에서 위로 스폰 윗면 높이 {F(SpawnTop(s))}" +
                               $"(= 스폰높이 {F(templates[s].spawnOffset)} + bounds 높이 {F(templates[s].size.y)} − 하단오프셋 {F(templates[s].bottomOffset)}) " +
                               "안에 정적 콜라이더가 있으면 저머리(스폰 불가).");
        }

        private float wallClear = 0.55f; // [R2] BuildPool에서 실측으로 덮어쓴다(초기값은 구·네모 0.5+0.05 [계산])

        // 런타임 원천의 CCD 모드. 플레이어는 파킹 때 Discrete로 바꾸므로 BuildTemplates(파킹 전)에서 저장해 둔 값을 쓴다.
        private readonly CollisionDetectionMode[] savedCdm = new CollisionDetectionMode[3];

        private static float SafeDiv(float a, float b) => Mathf.Abs(b) < 1e-6f ? a : a / b;

        private static Collider CopyCollider(Collider src, GameObject dst)
        {
            Collider d;
            switch (src)
            {
                case SphereCollider sc:
                { SphereCollider n = dst.AddComponent<SphereCollider>(); n.radius = sc.radius; n.center = sc.center; d = n; break; }
                case BoxCollider bc:
                { BoxCollider n = dst.AddComponent<BoxCollider>(); n.size = bc.size; n.center = bc.center; d = n; break; }
                case CapsuleCollider cc:
                { CapsuleCollider n = dst.AddComponent<CapsuleCollider>(); n.radius = cc.radius; n.height = cc.height; n.direction = cc.direction; n.center = cc.center; d = n; break; }
                case MeshCollider mc:
                { MeshCollider n = dst.AddComponent<MeshCollider>(); n.cookingOptions = mc.cookingOptions; n.sharedMesh = mc.sharedMesh; n.convex = mc.convex; d = n; break; }
                default:
                    throw new Exception($"지원하지 않는 콜라이더 형식 {src.GetType().Name}");
            }
            d.isTrigger = false;
            d.sharedMaterial = src.sharedMaterial;
            return d;
        }

        /// <summary>탐침 레이어와 충돌하는 레이어만 윗면 레이캐스트 대상으로(레이어 행렬은 읽기만).</summary>
        private void ComputeSurfaceMask()
        {
            int mask = 0;
            for (int s = 0; s < 3; s++)
            {
                int pl = pool[s][0].col.gameObject.layer;
                int m = 0;
                for (int l = 0; l < 32; l++)
                    if (!Physics.GetIgnoreLayerCollision(pl, l)) m |= 1 << l;
                templates[s].layerMask = m;
                mask |= m;
            }
            surfaceMask = mask;
        }

        // ── 음성 대조 ──────────────────────────────────────────────────────────────
        private IEnumerator RunNegativeControl()
        {
            Vector3 o = new Vector3(-3000f, 0f, -3000f); // 섹터 밖 먼 곳 [추정]
            float half = NegativeGap / 2f;
            negObjects.Add(MakeBox("SWEEP_NEG_Floor", o + new Vector3(0f, -0.5f, 0f), new Vector3(16f, 1f, 12f)));
            negObjects.Add(MakeBox("SWEEP_NEG_WallA", o + new Vector3(-(half + 1.5f), 1.5f, 0f), new Vector3(3f, 3f, 8f)));
            negObjects.Add(MakeBox("SWEEP_NEG_WallB", o + new Vector3(half + 1.5f, 1.5f, 0f), new Vector3(3f, 3f, 8f)));
            // [R1] FELL_THROUGH 경로 대조: 따로 떨어진 바닥 판(z=10~14)을 두고 이 판과 모든 탐침 사이에
            // Physics.IgnoreCollision을 건다 → 탐침은 판을 뚫고 떨어지고(관통 모사), 판은 레이캐스트에는 그대로
            // 보이므로 '위쪽에 콜라이더 있음 + 아래 지지 없음 + 1U 이상 낙하' = FELL_THROUGH가 나와야 한다.
            // 판은 이 코루틴 끝에서 파괴되므로 무시 쌍도 함께 사라진다(레이어 행렬·ProjectSettings 불변).
            GameObject thru = MakeBox("SWEEP_NEG_ThruFloor", o + new Vector3(0f, -0.5f, 12f), new Vector3(4f, 1f, 4f));
            negObjects.Add(thru);
            Collider thruCol = thru.GetComponent<Collider>();
            foreach (Collider pc in probeCols) Physics.IgnoreCollision(thruCol, pc, true);
            // [INF2-2] WEDGED 양성 대조 구덩이 2개(z=22 — 구덩이 외곽 z 20.45가 관통 판 z 14와 6.45 떨어짐 [계산]) — 홈 방향 z(꼭짓점 선 ∥ z)와 x.
            Vector3[] wedgeCols = { new Vector3(-3f, 0f, 22f), new Vector3(3f, 0f, 22f) };
            float wedgeW = WedgeInnerWidth();
            wedgeWidthUsed = wedgeW;
            MakeWedgePit("A", o + wedgeCols[0], wedgeW, 0f);
            MakeWedgePit("B", o + wedgeCols[1], wedgeW, 90f);
            Physics.SyncTransforms();

            neg = new SweepCtx { id = 0, label = "NEG", name = "음성 대조(0.8U 틈 + 관통 판 + V바닥 구덩이)", origin = o, spacing = 0f };
            currentCtx = neg;
            // 틈 중앙선(x=0) 3점 + 트인 바닥 3점(양성 대조 — OK여야 한다) + 관통 판 1점(FELL_THROUGH여야 한다)
            // + [INF2-2] V바닥 구덩이 꼭짓점 2점(Sphere·Cube 낙하 후 WEDGED여야 한다).
            Vector3[] gapCols = { new Vector3(0f, 0f, -2f), new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 2f) };
            Vector3[] openCols = { new Vector3(-6f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(0f, 0f, -5f) };
            Vector3[] thruCols = { new Vector3(0f, 0f, 12f) };
            foreach (Vector3 c in gapCols) CollectColumn(neg, o + c, 5f + o.y, -3f + o.y, "gap");
            foreach (Vector3 c in openCols) CollectColumn(neg, o + c, 5f + o.y, -3f + o.y, "open");
            foreach (Vector3 c in thruCols) CollectColumn(neg, o + c, 5f + o.y, -3f + o.y, "thru");
            foreach (Vector3 c in wedgeCols) CollectColumn(neg, o + c, 5f + o.y, -3f + o.y, "wedge");
            neg.columns = gapCols.Length + openCols.Length + thruCols.Length + wedgeCols.Length;
            TakeSnapshot(neg);

            yield return RunCtxWaves(neg);

            // 판정:
            //  gap  — Sphere·Cube 전부 WEDGED 또는 PENETRATING(두 도형 최소 폭 1.0U > 0.8U [계산]). Tetrahedron은 정보.
            //         [R1 명시] 폭 1.0 도형을 0.8 틈 중앙에 스폰하므로 시작부터 양 벽에 0.1씩 겹친다 — 이 항목은
            //         '정적 관통 측정 경로(ComputePenetration) 검증'이지 '낙하 후 끼임 검출'의 증거가 아니다.
            //  open — 3도형 전부 OK이고 발밑 법선 y ≥ 0.95([R1] FootNormalY 광선이 평지를 맞히는지 확인).
            //  thru — 3도형 전부 FELL_THROUGH([R1] 관통 분류 경로 검증).
            //  wedge — [INF2-2] Sphere·Cube 전부 WEDGED이고 시작 깊이 ≤ 0.05(= 낙하 후 끼임 검출). Tetrahedron은 정보
            //         (bounds 폭 1.19 > 구덩이 폭이라 시작부터 벽과 겹칠 수 있다).
            int gapSurf = 0, openSurf = 0, thruSurf = 0, wedgeSurf = 0;
            foreach (Surface sf in neg.surfaces)
            {
                if (sf.tag == "gap") gapSurf++;
                else if (sf.tag == "thru") thruSurf++;
                else if (sf.tag == "wedge") wedgeSurf++;
                else openSurf++;
            }
            bool pass = gapSurf >= 3 && openSurf >= 3 && thruSurf >= 1 && wedgeSurf >= wedgeCols.Length;
            StringBuilder d = new StringBuilder();
            d.AppendLine($"  윗면: 틈 {gapSurf}(기대 3) · 트인 바닥 {openSurf}(기대 3) · 관통 판 {thruSurf}(기대 1) · " +
                         $"V바닥 구덩이 {wedgeSurf}(기대 {wedgeCols.Length})");
            d.AppendLine("  범위: [gap]=정적 관통 측정 경로 검증(시작 겹침으로 검출 — 낙하 끼임 증거 아님) · " +
                         "[open]=OK·발밑ny≥0.95 양성 대조 · [thru]=FELL_THROUGH 분류 경로 검증(판과 탐침 IgnoreCollision) · " +
                         $"[wedge]=낙하 후 WEDGED 검출 경로 검증(4면 수직 벽 안쪽 폭 {F(wedgeW)} + V바닥 {F(WedgeSlopeDeg)}° 구덩이, " +
                         "홈 A=꼭짓점 선 ∥z·B=∥x — Sphere·Cube가 시작 깊이 ≤0.05에서 WEDGED여야 PASS, 못 잡으면 스윕 실패).");
            foreach (Finding f in neg.allNeg)
            {
                bool caught = f.cls == Cls.WEDGED || f.cls == Cls.PENETRATING;
                string verdict;
                if (f.tag == "gap")
                {
                    string how = f.startDepth > PenetrationFail ? " (시작 겹침으로 검출)" : " (낙하 후 검출)";
                    if (f.shape == "Tetrahedron") verdict = caught ? "검출(정보)" + how : "미검출(정보 — 판정 제외)";
                    else { verdict = caught ? "검출 ✅" + how : "미검출 ❌"; if (!caught) pass = false; }
                }
                else if (f.tag == "wedge")
                {
                    bool afterDrop = f.startDepth <= PenetrationFail;
                    bool ok = f.cls == Cls.WEDGED && afterDrop;
                    if (f.shape == "Tetrahedron")
                        verdict = (ok ? "낙하 후 WEDGED(정보)" : "WEDGED 아님 또는 시작 겹침(정보 — 판정 제외)");
                    else
                    {
                        verdict = ok ? "낙하 후 WEDGED ✅"
                                     : (f.cls != Cls.WEDGED ? "WEDGED 아님 ❌" : "시작 겹침 > 0.05(낙하 후 검출 아님) ❌");
                        if (!ok) pass = false;
                    }
                }
                else if (f.tag == "thru")
                {
                    bool ok = f.cls == Cls.FELL_THROUGH;
                    verdict = ok ? "FELL_THROUGH ✅" : "FELL_THROUGH 아님 ❌";
                    if (!ok) pass = false;
                }
                else
                {
                    bool ok = f.cls == Cls.OK && f.footNy >= FlatFloorNormalY;
                    verdict = ok ? "OK·발밑평지 ✅" : (f.cls == Cls.OK ? "발밑ny<0.95 ❌" : "OK 아님 ❌");
                    if (!ok) pass = false;
                }
                d.AppendLine($"  [{f.tag}] {f.shape} → {f.cls}(기저 {f.underlying}) 깊이={F(f.depth)} 시작깊이={F(f.startDepth)} " +
                             $"최대밀림={F(f.maxMove)} 발밑ny={F(f.footNy)} 낙하={F(f.drop)} 정지로컬={V(f.restLocal)} — {verdict}");
            }
            negPass = pass;
            negDetail = d.ToString();
            foreach (GameObject g in negObjects) if (g != null) Object.Destroy(g);
            negObjects.Clear();
            yield return Wfu;
        }

        private static GameObject MakeBox(string name, Vector3 center, Vector3 size)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.position = center;
            g.transform.localScale = size;
            return g;
        }

        private static GameObject MakeBox(string name, Vector3 center, Vector3 size, Quaternion rot)
        {
            GameObject g = MakeBox(name, center, size);
            g.transform.rotation = rot;
            return g;
        }

        /// <summary>[INF2-2] 구덩이 안쪽 폭 = Sphere·Cube 형판 bounds의 최대 수평 폭 + 한쪽 WedgeClearance×2 [계산].
        /// 형판 bounds (1,1,1)이면 1.1 — 두 도형이 시작 겹침 없이 들어가고(한쪽 0.05), 수평으로는 최대 0.1만 움직인다(&lt; 0.15).</summary>
        private float WedgeInnerWidth()
        {
            float w = 0f;
            for (int s = 0; s < 2; s++) // 0 = Sphere, 1 = Cube (Tetrahedron은 정보라 폭 산정에서 뺀다)
                if (templates[s] != null) w = Mathf.Max(w, Mathf.Max(templates[s].size.x, templates[s].size.z));
            if (w <= 0f) w = 1f; // [추정] 형판 없음 대비(BuildTemplates 실패면 여기 오지 않는다)
            return w + 2f * WedgeClearance;
        }

        /// <summary>[INF2-2] WEDGED 양성 대조 구덩이 1개. c = V바닥 꼭짓점(홈 가운데), yaw 0 → 꼭짓점 선 ∥ z(경사는 ±x),
        /// yaw 90 → 꼭짓점 선 ∥ x(경사는 ±z). 구성(꼭짓점 기준):
        ///  · 경사 상자 2개: 윗면이 꼭짓점을 지나 WedgeSlopeDeg로 오르는 판(두께 WedgeSlopeThick). 경사 방향 범위
        ///    s ∈ [−WedgeSlopePastApex, (w/2 + 벽두께/2)/cosα] — 꼭짓점 너머로 서로 겹치고 벽 속까지 들어간다(틈 0).
        ///  · 수직 벽 4개: 안쪽 면 = 꼭짓점에서 ±w/2(두 축), 높이 WedgeWallBottom~WedgeWallTop, 두께 WedgeWallThick.
        ///    ±x(yaw 기준 가로) 벽은 모서리까지 덮도록 길이 w + 2·두께.
        /// 탐침은 꼭짓점 윗면 + spawnOffset에서 떨어져 두 경사면(또는 네모는 아래 모서리 두 개)에 얹히고, 수평 4방향이 수직 벽에
        /// 막힌다 → 최대 밀림 ≤ w − 도형 폭 = 0.1 &lt; 0.15, 발밑 ny = cosα = 0.906 &lt; 0.95 → WEDGED [계산].</summary>
        private void MakeWedgePit(string id, Vector3 c, float w, float yaw)
        {
            float a = WedgeSlopeDeg;
            float ca = Mathf.Cos(a * Mathf.Deg2Rad);
            float s0 = -WedgeSlopePastApex;
            float s1 = (w / 2f + WedgeWallThick / 2f) / ca;
            Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
            for (int side = 0; side < 2; side++)
            {
                // side 0: 로컬 x축이 (yaw 기준) +가로 쪽으로 오른다. side 1: yaw+180 → −가로 쪽으로 오른다.
                // Quaternion.Euler(0, y, a): 먼저 z축 a(로컬 x → (cos a, sin a, 0)), 다음 y축 회전.
                Quaternion r = Quaternion.Euler(0f, yaw + side * 180f, a);
                Vector3 localCenter = new Vector3((s0 + s1) / 2f, -WedgeSlopeThick / 2f, 0f);
                negObjects.Add(MakeBox($"SWEEP_NEG_Wedge{id}_Slope{side}", c + r * localCenter,
                                       new Vector3(s1 - s0, WedgeSlopeThick, w + 2f * WedgeWallThick), r));
            }
            float h = WedgeWallTop - WedgeWallBottom;
            float midY = (WedgeWallTop + WedgeWallBottom) / 2f;
            float off = w / 2f + WedgeWallThick / 2f;
            // 가로(yaw 기준 로컬 x) 벽: 모서리까지 덮는다.
            negObjects.Add(MakeBox($"SWEEP_NEG_Wedge{id}_WallXp", c + yawRot * new Vector3(off, midY, 0f),
                                   new Vector3(WedgeWallThick, h, w + 2f * WedgeWallThick), yawRot));
            negObjects.Add(MakeBox($"SWEEP_NEG_Wedge{id}_WallXn", c + yawRot * new Vector3(-off, midY, 0f),
                                   new Vector3(WedgeWallThick, h, w + 2f * WedgeWallThick), yawRot));
            // 세로(yaw 기준 로컬 z) 벽 = 홈 양 끝 막음.
            negObjects.Add(MakeBox($"SWEEP_NEG_Wedge{id}_WallZp", c + yawRot * new Vector3(0f, midY, off),
                                   new Vector3(w, h, WedgeWallThick), yawRot));
            negObjects.Add(MakeBox($"SWEEP_NEG_Wedge{id}_WallZn", c + yawRot * new Vector3(0f, midY, -off),
                                   new Vector3(w, h, WedgeWallThick), yawRot));
        }

        // ── 섹터 ──────────────────────────────────────────────────────────────────
        private IEnumerator SweepSector(SweepCtx ctx)
        {
            Map4Layout.SectorDef def = layout.GetSector(ctx.id);
            if (def == null) { ctx.error = "Map4Layout에 섹터 정의 없음"; yield break; }
            ctx.name = def.sectorName;
            if (!director.TryGetSector(ctx.id, out SectorController sc) || sc == null)
            { ctx.error = "섹터 미로드(Director 레지스트리에 없음)"; yield break; }
            ctx.root = sc.transform;
            ctx.origin = sc.transform.position;
            ctx.spacing = Array.IndexOf(DetailedSectors, ctx.id) >= 0 ? args.spacing : args.coarseSpacing;

            // Y 범위 = 이 섹터 씬 콜라이더 bounds 전체.
            float topY = float.NegativeInfinity, bottomY = float.PositiveInfinity;
            Scene scene = sc.gameObject.scene;
            foreach (GameObject rootGo in scene.GetRootGameObjects())
                foreach (Collider c in rootGo.GetComponentsInChildren<Collider>())
                {
                    if (!c.enabled || c.isTrigger) continue;
                    topY = Mathf.Max(topY, c.bounds.max.y);
                    bottomY = Mathf.Min(bottomY, c.bounds.min.y);
                }
            if (float.IsInfinity(topY)) { topY = ctx.origin.y + 50f; bottomY = ctx.origin.y - 5f; }

            ParkAll();
            Physics.SyncTransforms();

            // 격자(섹터 로컬): 섹터 본체 x∈[−w/2,w/2]·z∈[0,length] + 연결 통로 x∈±cw/2·z∈[length,length+cl].
            List<Vector2> cols = new List<Vector2>();
            AddGrid(cols, -def.width / 2f, def.width / 2f, 0f, def.length, ctx.spacing);
            if (layout.GetSector(ctx.id + 1) != null && def.connectorLengthToNext > 0f)
                AddGrid(cols, -def.connectorWidthToNext / 2f, def.connectorWidthToNext / 2f,
                        def.length, def.length + def.connectorLengthToNext, ctx.spacing);
            ctx.columns = cols.Count;
            foreach (Vector2 c in cols)
            {
                Vector3 w = ctx.root.TransformPoint(new Vector3(c.x, 0f, c.y));
                CollectColumn(ctx, w, topY + 1f, bottomY - 1f, null, KGrid);
            }
            // [R2] 반 칸 보조 격자 — 주 격자 열이 벽면(예: S1 복도 x=±5)에 겹쳐 매몰로 빠지는 띠를 메운다.
            List<Vector2> stag = new List<Vector2>();
            AddStaggerGrid(stag, -def.width / 2f, def.width / 2f, 0f, def.length, ctx.spacing);
            if (layout.GetSector(ctx.id + 1) != null && def.connectorLengthToNext > 0f)
                AddStaggerGrid(stag, -def.connectorWidthToNext / 2f, def.connectorWidthToNext / 2f,
                               def.length, def.length + def.connectorLengthToNext, ctx.spacing);
            ctx.staggerColumns = stag.Count;
            foreach (Vector2 c in stag)
            {
                Vector3 w = ctx.root.TransformPoint(new Vector3(c.x, 0f, c.y));
                CollectColumn(ctx, w, topY + 1f, bottomY - 1f, null, KStagger);
            }
            // [R2] 벽 인접 보조 표본.
            int gridSurfCount = ctx.surfaces.Count; // 여기까지가 격자·반칸 표본(벽 인접은 이 뒤에 붙는다)
            AddWallAdjacent(ctx);
            // [컨트롤타워 FIN2 판정 2026-09-29 — 순간이동 생성 인공물 제거, 진짜 좁은 틈은 원위치로 유지]
            // 벽 인접 표본을 만든 '뒤에' 격자·반칸 표본을 옮긴다 — 벽 인접 후보 생성(원래 격자 위치 기준 광선·중복 제거)이 바뀌지 않게 한다.
            PrecheckGridSamples(ctx, gridSurfCount);
            TakeSnapshot(ctx);
            Debug.Log($"{LogTag} {ctx.label} 준비: 격자 {ctx.columns}열+반칸 {ctx.staggerColumns}열(간격 {F(ctx.spacing)}) 윗면 {ctx.surfaces.Count}" +
                      $"(격자 {ctx.surfKind[KGrid]}/반칸 {ctx.surfKind[KStagger]}/벽인접 {ctx.surfKind[KWall]}) " +
                      $"매몰제외 {ctx.buried} 벽인접 사전검사(물림 {ctx.wallPreShifted}/전도형 버림 {ctx.wallPreDropped}/일부 버림 {ctx.wallPrePartial}) " +
                      $"격자·반칸 사전검사[FIN2](도형 이동 {SweepCtx.GridPreSum(ctx.gridPreMoved)}/원위치 {SweepCtx.GridPreSum(ctx.gridPreStay)}) — " +
                      $"웨이브 {Mathf.CeilToInt(ctx.surfaces.Count / (float)args.wave)}회");

            yield return RunCtxWaves(ctx);

            Debug.Log($"{LogTag} {ctx.label} 완료: FELL_THROUGH={Sum(ctx, Cls.FELL_THROUGH)} PENETRATING={Sum(ctx, Cls.PENETRATING)} " +
                      $"WEDGED={Sum(ctx, Cls.WEDGED)} MOVING_CONTACT={Sum(ctx, Cls.MOVING_CONTACT)} " +
                      $"(경과 {Time.realtimeSinceStartup:F0}s)");
        }

        /// <summary>주 격자 원점·칸 수(칸 중앙 표본, 남는 폭은 양쪽에 고르게). 파이썬 자기 검사가 같은 식으로 재현한다.</summary>
        public static void GridOrigin(float x0, float x1, float z0, float z1, float s,
                                      out int nx, out int nz, out float ox, out float oz)
        {
            nx = Mathf.Max(1, Mathf.FloorToInt((x1 - x0) / s + 1e-4f));
            nz = Mathf.Max(1, Mathf.FloorToInt((z1 - z0) / s + 1e-4f));
            ox = x0 + ((x1 - x0) - nx * s) / 2f + s / 2f;
            oz = z0 + ((z1 - z0) - nz * s) / 2f + s / 2f;
        }

        private static void AddGrid(List<Vector2> cols, float x0, float x1, float z0, float z1, float s)
        {
            GridOrigin(x0, x1, z0, z1, s, out int nx, out int nz, out float ox, out float oz);
            // 칸 중앙 표본(벽·경계선 바로 위를 피한다), 남는 폭은 양쪽에 고르게.
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                    cols.Add(new Vector2(ox + i * s, oz + k * s));
        }

        /// <summary>[R2] 반 칸 보조 격자: 주 격자 이웃 네 점의 한가운데(x·z 모두 s/2 어긋남, (nx−1)×(nz−1)점).
        /// 한 축이 1칸뿐이면 그 축은 주 격자 값을 그대로 쓰고 다른 축만 어긋낸다. 두 축 모두 1칸이면 없음.</summary>
        public static void AddStaggerGrid(List<Vector2> cols, float x0, float x1, float z0, float z1, float s)
        {
            GridOrigin(x0, x1, z0, z1, s, out int nx, out int nz, out float ox, out float oz);
            if (nx == 1 && nz == 1) return;
            int mx = nx > 1 ? nx - 1 : 1, mz = nz > 1 ? nz - 1 : 1;
            float sx = nx > 1 ? ox + s / 2f : ox, sz = nz > 1 ? oz + s / 2f : oz;
            for (int i = 0; i < mx; i++)
                for (int k = 0; k < mz; k++)
                    cols.Add(new Vector2(sx + i * s, sz + k * s));
        }

        /// <summary>[R2] 벽 인접 보조 표본. 주·보조 윗면마다 윗면+{0.3, 0.75} 높이에서 섹터 로컬 ±x·±z로
        /// (격자 간격+0.05)까지 수평 광선을 쏜다. 처음 맞은 것이 선 면(|법선 y|&lt;0.5, 정적)이면
        /// [INF2-2 — 판정 B9] 맞은 점에서 그 면의 수평 법선 n_h(법선의 xz 성분 정규화) 방향으로 wallClear 떨어진 점
        /// q = 맞은 점 + n_h·wallClear를 잡는다(광선 방향 d 기준이던 R2 배치는 비스듬한 면에서 도형이 면에 겹쳤다).
        /// q에서 아래로 가장 가까운 윗면(원래 윗면 − 0.5까지)을 후보로 두고, 섹터 로컬 0.1U 반올림 좌표로 중복 제거한 뒤
        /// 탐침 형판 사전 겹침 검사(WallPrecheck)를 한다: [M2R1] 도형마다 따로 — 정적 콜라이더와 > 0.05 겹친 도형만 n_h 방향으로
        /// WallBackoffStep씩 WallBackoffMax까지 물려 다시 바닥을 찾고 검사하고, 통과한 도형은 그 자리의 표본(shapeMask에 그 도형
        /// 비트)을 쓴다. 끝까지 겹치거나 물린 자리에 윗면이 없는 도형만 버린다(다른 도형 표본은 남는다).
        /// 통과한 점이 매몰이면 제외(매몰 집계에 종류 KWall로).</summary>
        private void AddWallAdjacent(SweepCtx ctx)
        {
            int baseCount = ctx.surfaces.Count;
            HashSet<string> seen = new HashSet<string>();
            // [M2R1] 자리(0.1U 반올림)별로 이미 표본을 가진 도형 비트 — 격자·반칸 윗면은 세 도형 모두(7).
            Dictionary<string, int> covered = new Dictionary<string, int>();
            for (int i = 0; i < baseCount; i++)
            {
                string bk = PosKey(ctx.ToLocal(ctx.surfaces[i].world));
                seen.Add(bk);
                covered[bk] = 7;
            }
            Vector3 fw = ctx.root != null ? ctx.root.forward : Vector3.forward;
            Vector3 rt = ctx.root != null ? ctx.root.right : Vector3.right;
            fw.y = 0f; rt.y = 0f;
            fw = fw.sqrMagnitude > 1e-6f ? fw.normalized : Vector3.forward;
            rt = rt.sqrMagnitude > 1e-6f ? rt.normalized : Vector3.right;
            Vector3[] dirs = { fw, -fw, rt, -rt };
            float reach = ctx.spacing + 0.05f;
            int backoffSteps = Mathf.RoundToInt(WallBackoffMax / WallBackoffStep); // 5 [계산]
            for (int i = 0; i < baseCount; i++)
            {
                Surface sf = ctx.surfaces[i];
                foreach (float h in WallProbeHeights)
                {
                    Vector3 o = sf.world + Vector3.up * h;
                    if (BuriedBy(o, null) != null) continue; // 광선 출발점이 콜라이더 속이면 그 높이는 건너뛴다
                    foreach (Vector3 d in dirs)
                    {
                        if (!NearestForeignHit(o, d, reach, out RaycastHit wh)) continue;
                        if (Mathf.Abs(wh.normal.y) >= SurfaceMinNormalY) continue;   // 선 면(벽·돌출부 옆면)만
                        if (wh.collider.attachedRigidbody != null) continue;         // 동적 물체 제외
                        // [INF2-2] 수평 법선 기준 배치. |법선 y| < 0.5라 xz 성분 크기 ≥ 0.866 — 정규화 안전 [계산].
                        Vector3 nh = new Vector3(wh.normal.x, 0f, wh.normal.z);
                        if (nh.sqrMagnitude < 1e-4f) continue;
                        nh.Normalize();
                        Vector3 q = wh.point + nh * wallClear;                        // 맞은 점과 같은 높이(o.y)
                        Vector3 moved = q - o;
                        moved.y = 0f;
                        if (moved.magnitude < WallSampleMinShift) continue;           // 원래 표본이 이미 벽 인접
                        if (!NearestForeignHit(q, Vector3.down, h + WallDropSearch, out RaycastHit fh)) continue;
                        if (fh.normal.y < SurfaceMinNormalY) continue;
                        if (!seen.Add(PosKey(ctx.ToLocal(fh.point)))) { ctx.wallDedup++; continue; }
                        ctx.wallCandidates++;

                        // [INF2-2] 탐침 형판 사전 겹침 검사 + n_h 방향 물림. [M2R1] 도형별: pending = 아직 자리를 못 찾은 도형.
                        Vector3 firstPoint = fh.point;
                        int pending = 7;
                        bool anyShifted = false;
                        string lastOv = null;
                        string wallPath = PathOf(wh.collider.transform);
                        for (int step = 0; step <= backoffSteps && pending != 0; step++)
                        {
                            if (step > 0)
                            {
                                Vector3 qb = q + nh * (step * WallBackoffStep);
                                if (!NearestForeignHit(qb, Vector3.down, h + WallDropSearch, out RaycastHit bh) ||
                                    bh.normal.y < SurfaceMinNormalY)
                                { lastOv = "물린 자리 윗면 없음(" + F(step * WallBackoffStep) + ")"; break; }
                                fh = bh;
                            }
                            int mask = WallPrecheck(fh.point, pending, out string ovName, out float ovDepth);
                            if (step == 0)
                                for (int s = 0; s < 3; s++) if ((mask & (1 << s)) != 0) ctx.wallPreFirstOverlap[s]++;
                            int pass = pending & ~mask;
                            if (pass != 0)
                            {
                                if (step > 0)
                                {
                                    anyShifted = true;
                                    for (int s = 0; s < 3; s++) if ((pass & (1 << s)) != 0) ctx.wallPreShiftedByShape[s]++;
                                }
                                AddWallSurface(ctx, covered, seen, fh, wallPath, pass);
                            }
                            pending &= mask;
                            if (pending != 0) lastOv = $"{ovName}({F(ovDepth)}, 물림 {F(step * WallBackoffStep)})";
                        }
                        if (anyShifted) ctx.wallPreShifted++;
                        if (pending == 0) continue;
                        // [M2R1] 끝까지 자리를 못 찾은 도형만 버린다(다른 도형 표본은 이미 추가됨).
                        if (pending == 7) ctx.wallPreDropped++;
                        else ctx.wallPrePartial++;
                        for (int s = 0; s < 3; s++)
                        {
                            if ((pending & (1 << s)) == 0) continue;
                            ctx.wallPreDroppedByShape[s]++;
                            if (LowHeadroom(firstPoint, s, out float head, out string ceil))
                                RecordLowHead(ctx, firstPoint, head, ceil, s, true);
                            else
                                ctx.lowHeadUnknownWallDrop++;
                        }
                        if (ctx.wallPreDropExamples.Count < 10)
                            ctx.wallPreDropExamples.Add($"{V(ctx.ToLocal(q))}←{wallPath} 버린 도형={MaskNames(pending)} 겹침={lastOv}");
                    }
                }
            }
        }

        /// <summary>[컨트롤타워 FIN2 판정 2026-09-29 — 순간이동 생성 인공물 제거, 진짜 좁은 틈은 원위치로 유지]
        /// 격자·반칸 표본(ctx.surfaces[0..gridCount))의 사전 겹침 검사와 도형별 이동. 새 판정식은 만들지 않고 벽 인접과 같은
        /// WallPrecheck(형판을 RunWave와 같은 자세로 윗면+spawnOffset에 두고 정적 콜라이더와 ComputePenetration &gt; PenetrationFail)를 쓴다.
        /// 원위치에서 겹친 도형([M2R1]처럼 도형별)만: 섹터 로컬 수평 8방향(+x, −x, +z, −z, 대각 +x+z, +x−z, −x+z, −x−z — 이 고정 순서)으로
        /// WallBackoffStep씩 WallBackoffMax까지(거리 작은 순, 같은 거리는 방향 순서) 옮기며, 옮긴 자리 아래에서 <b>같은 윗면 콜라이더</b>를
        /// 다시 찾고(높이 차 ≤ GridShiftHeightTol) 사전 겹침이 없으면 그 자리를 그 도형의 표본으로 쓴다. 도형마다 가장 가까운 통과 자리를 받는다.
        /// 8방향·한도 안에서 자리를 못 찾은 도형은 <b>원래 자리를 그대로 쓴다</b>(버리지 않음) — 도형 폭보다 좁은 진짜 틈은 계속
        /// PENETRATING/WEDGED로 검출된다(한계: 겹침이 없는 자리가 0.5U 안에 있는 짧은 틈은 그 표본이 옮겨진다). 옮겨진 도형은 원래 자리 표본에서
        /// 빠지고(shapeMask), 도형이 남지 않은 원래 표본은 지운다. 이동 자리는 CollectColumn과 같은 매몰 규칙(BuriedBy)을 적용한다.
        /// 음성 대조 표본은 이 함수를 거치지 않는다(RunNegativeControl이 직접 수집 — [gap]은 도형이 시작부터 두 벽과 겹치는 것이 검증 목적이고
        /// [wedge]는 4면 벽 구덩이 안 낙하 후 끼임이 목적이라, 옮기면 틈 밖 트인 바닥으로 나가 양성 대조가 사라진다).</summary>
        private void PrecheckGridSamples(SweepCtx ctx, int gridCount)
        {
            Vector3 fw = ctx.root != null ? ctx.root.forward : Vector3.forward;
            Vector3 rt = ctx.root != null ? ctx.root.right : Vector3.right;
            fw.y = 0f; rt.y = 0f;
            fw = fw.sqrMagnitude > 1e-6f ? fw.normalized : Vector3.forward;
            rt = rt.sqrMagnitude > 1e-6f ? rt.normalized : Vector3.right;
            Vector3[] dirs =
            {
                rt, -rt, fw, -fw,
                (rt + fw).normalized, (rt - fw).normalized, (-rt + fw).normalized, (-rt - fw).normalized
            };
            int steps = Mathf.RoundToInt(WallBackoffMax / WallBackoffStep); // 5 [계산]
            List<Surface> added = new List<Surface>();
            for (int i = 0; i < gridCount; i++)
            {
                Surface sf = ctx.surfaces[i];
                if (sf.kind != KGrid && sf.kind != KStagger) continue;
                int kd = sf.kind == KStagger ? 1 : 0;
                int overlap = WallPrecheck(sf.world, sf.shapeMask, out _, out _);
                if (overlap == 0) continue;
                int pending = overlap; // 아직 자리를 못 찾은 도형
                for (int step = 1; step <= steps && pending != 0; step++)
                {
                    float dist = step * WallBackoffStep;
                    for (int di = 0; di < dirs.Length && pending != 0; di++)
                    {
                        if (!FindSameFloor(sf, sf.world + dirs[di] * dist, out RaycastHit fh)) continue;
                        int mask = WallPrecheck(fh.point, pending, out _, out _);
                        int pass = pending & ~mask;
                        if (pass == 0) continue;
                        added.Add(new Surface
                        {
                            world = fh.point, normalY = fh.normal.y, collider = sf.collider, tag = sf.tag, kind = sf.kind,
                            shapeMask = pass
                        });
                        for (int s = 0; s < 3; s++) if ((pass & (1 << s)) != 0) ctx.gridPreMoved[kd, s]++;
                        pending &= mask;
                    }
                }
                for (int s = 0; s < 3; s++) if ((pending & (1 << s)) != 0) ctx.gridPreStay[kd, s]++; // 못 찾음 → 원위치 유지
                sf.shapeMask &= ~(overlap & ~pending);                                              // 옮겨진 도형은 원래 자리에서 뺀다
            }
            for (int i = gridCount - 1; i >= 0; i--)
                if (ctx.surfaces[i].shapeMask == 0)
                {
                    ctx.surfKind[ctx.surfaces[i].kind]--;
                    ctx.surfaces.RemoveAt(i);
                }
            foreach (Surface a in added)
            {
                ctx.surfaces.Add(a);
                ctx.surfKind[a.kind]++;
            }
        }

        /// <summary>[컨트롤타워 FIN2 판정 2026-09-29] 옮긴 수평 위치 pos 아래에서 원래 표본 from과 <b>같은 윗면 콜라이더</b>의 윗면을 찾는다:
        /// 원래 윗면 + GridShiftRayUp에서 아래로 (GridShiftRayUp + GridShiftRayDown) 레이캐스트, 같은 콜라이더·법선 y ≥ SurfaceMinNormalY·
        /// 높이 차 ≤ GridShiftHeightTol인 가장 가까운 맞은 점. CollectColumn과 같은 매몰 규칙(BuriedBy)을 통과해야 한다.</summary>
        private bool FindSameFloor(Surface from, Vector3 pos, out RaycastHit best)
        {
            best = default;
            Vector3 o = new Vector3(pos.x, from.world.y + GridShiftRayUp, pos.z);
            int n = Physics.RaycastNonAlloc(o, Vector3.down, rayBuf, GridShiftRayUp + GridShiftRayDown, surfaceMask,
                                            QueryTriggerInteraction.Ignore);
            float bd = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = rayBuf[i];
                if (h.collider != from.collider) continue;
                if (h.normal.y < SurfaceMinNormalY) continue;
                if (Mathf.Abs(h.point.y - from.world.y) > GridShiftHeightTol) continue;
                if (h.distance < bd) { bd = h.distance; best = h; found = true; }
            }
            return found && BuriedBy(best.point + Vector3.up * 0.05f, from.collider) == null;
        }

        /// <summary>[INF2-2] 탐침 형판 사전 겹침 검사. 윗면 점 surf에 세 도형을 RunWave와 같은 자세(rb 위치 = 윗면 +
        /// spawnOffset, 회전 baseRot, 콜라이더 = 풀 첫 탐침의 모양·로컬 변환)로 둘 때 정적 콜라이더와의 ComputePenetration
        /// 깊이가 PenetrationFail(0.05 — 시작 겹침 문턱과 같다)을 넘는 도형의 비트(1&lt;&lt;도형)를 돌려준다. 가장 깊은 겹침의
        /// 상대·깊이를 out으로. 이 시점엔 섹터 스냅샷 전이라 IsMoving 대신 attachedRigidbody 유무로 동적 물체를 뺀다
        /// (RunWave의 MaxStaticPenetration도 attachedRigidbody 콜라이더는 늘 빼므로 같은 집합이다 — 스윕 중 움직인
        /// 리지드바디 없는 기믹만 이 검사에 더 들어간다 = 더 엄격).</summary>
        private int WallPrecheck(Vector3 surf, int shapes, out string worstName, out float worstDepth)
        {
            int mask = 0;
            worstName = null;
            worstDepth = 0f;
            for (int s = 0; s < 3; s++)
            {
                if ((shapes & (1 << s)) == 0) continue; // [M2R1] 이미 자리를 찾은 도형은 검사하지 않는다
                SweepProbe p = pool[s][0];
                Vector3 rbPos = surf + Vector3.up * templates[s].spawnOffset;
                Quaternion rbRot = p.baseRot;
                Vector3 cPos = rbPos + rbRot * p.colLocalPos;
                Quaternion cRot = rbRot * p.colLocalRot;
                int n = Physics.OverlapSphereNonAlloc(cPos, p.queryRadius + 0.1f, ovBuf, templates[s].layerMask,
                                                      QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    Collider o = ovBuf[i];
                    if (!IsForeign(o) || o.attachedRigidbody != null) continue;
                    bool hit;
                    float dist;
                    try
                    {
                        hit = Physics.ComputePenetration(p.col, cPos, cRot, o, o.transform.position, o.transform.rotation,
                                                         out _, out dist);
                    }
                    catch { continue; }
                    if (!hit || dist <= PenetrationFail) continue;
                    mask |= 1 << s;
                    if (dist > worstDepth) { worstDepth = dist; worstName = PathOf(o.transform); }
                }
            }
            return mask;
        }

        /// <summary>[INF2-2 M2R1] 벽 인접 표본 추가(도형 비트 shapes만). 같은 자리(0.1U 반올림)에 이미 표본을 가진 도형은 빼고,
        /// 남는 도형이 없으면 중복 제거로 센다. 매몰이면 제외.</summary>
        private void AddWallSurface(SweepCtx ctx, Dictionary<string, int> covered, HashSet<string> seen, RaycastHit fh,
                                    string wallPath, int shapes)
        {
            string key = PosKey(ctx.ToLocal(fh.point));
            seen.Add(key);
            covered.TryGetValue(key, out int have);
            int eff = shapes & ~have;
            if (eff == 0) { ctx.wallDedup++; return; }
            covered[key] = have | eff;
            string bur = BuriedBy(fh.point + Vector3.up * 0.05f, fh.collider);
            if (bur != null) { CountBuried(ctx, KWall, bur); return; }
            ctx.surfaces.Add(new Surface
            {
                world = fh.point, normalY = fh.normal.y, collider = fh.collider, kind = KWall,
                wallName = wallPath, shapeMask = eff
            });
            ctx.surfKind[KWall]++;
        }

        /// <summary>[INF2-2 M2R1] 도형 s의 스폰 윗면 높이(윗면 기준) = spawnOffset + (bounds 윗면 − 피벗) [계산].
        /// bottomOffset = 피벗 − bounds 하단이므로 bounds 윗면 − 피벗 = size.y − bottomOffset.</summary>
        private float SpawnTop(int s) => templates[s].spawnOffset + templates[s].size.y - templates[s].bottomOffset;

        /// <summary>[INF2-2 M2R1] 저머리 판정: 윗면 surf + HeadRayStart에서 수직 위로 (SpawnTop(s) − HeadRayStart) 안에 정적
        /// 콜라이더(리지드바디 없음)가 있으면 true, head = 윗면→천장 거리, ceiling = 그 콜라이더 경로. 중심 수직 광선 하나라
        /// 도형 가장자리 위만 덮는 천장은 못 잡는다(그 경우 false — 호출 쪽에서 '수평 겹침'으로 센다).</summary>
        private bool LowHeadroom(Vector3 surf, int s, out float head, out string ceiling)
        {
            head = float.PositiveInfinity;
            ceiling = null;
            float len = SpawnTop(s) - HeadRayStart;
            if (len <= 0f) return false;
            int n = Physics.RaycastNonAlloc(surf + Vector3.up * HeadRayStart, Vector3.up, rayBuf, len, templates[s].layerMask,
                                            QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                Collider c = rayBuf[i].collider;
                if (!IsForeign(c) || c.attachedRigidbody != null) continue;
                if (rayBuf[i].distance < best) { best = rayBuf[i].distance; ceiling = PathOf(c.transform); }
            }
            if (ceiling == null) return false;
            head = best + HeadRayStart;
            return true;
        }

        private static void RecordLowHead(SweepCtx ctx, Vector3 surfWorld, float head, string ceiling, int s, bool wallDrop)
        {
            if (!ctx.lowHead.TryGetValue(ceiling, out LowHeadZone z))
            {
                z = new LowHeadZone { ceiling = ceiling };
                ctx.lowHead[ceiling] = z;
            }
            if (wallDrop) z.wallDrop[s]++;
            else z.startOv[s]++;
            z.minHead = Mathf.Min(z.minHead, head);
            z.maxHead = Mathf.Max(z.maxHead, head);
            Vector3 l = ctx.ToLocal(surfWorld);
            z.min = Vector3.Min(z.min, l);
            z.max = Vector3.Max(z.max, l);
        }

        private static string MaskNames(int mask)
        {
            List<string> n = new List<string>();
            for (int s = 0; s < 3; s++) if ((mask & (1 << s)) != 0) n.Add(ShapeNames[s]);
            return n.Count > 0 ? string.Join("+", n) : "-";
        }

        private static string PosKey(Vector3 local) =>
            Mathf.RoundToInt(local.x * 10f) + "," + Mathf.RoundToInt(local.y * 10f) + "," + Mathf.RoundToInt(local.z * 10f);

        private bool NearestForeignHit(Vector3 origin, Vector3 dir, float len, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, rayBuf, len, surfaceMask, QueryTriggerInteraction.Ignore);
            float bd = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (!IsForeign(rayBuf[i].collider)) continue;
                if (rayBuf[i].distance < bd) { bd = rayBuf[i].distance; best = rayBuf[i]; found = true; }
            }
            return found;
        }

        private static void CountBuried(SweepCtx ctx, int kind, string by)
        {
            ctx.buried++;
            ctx.buriedKind[kind]++;
            ctx.buriedBy.TryGetValue(by, out int c);
            ctx.buriedBy[by] = c + 1;
        }

        /// <summary>한 열에서 위→아래로 모든 윗면(법선 y ≥ 0.5)을 층마다 모은다. 윗면 0.05U 위가 다른 콜라이더
        /// 속이면(벽 밑에 깔린 바닥 등) 매몰로 제외. [R2] 매몰은 덮은 콜라이더 이름별로 센다.</summary>
        private void CollectColumn(SweepCtx ctx, Vector3 worldXZ, float fromY, float toY, string tag, int kind = KGrid)
        {
            Vector3 origin = new Vector3(worldXZ.x, fromY, worldXZ.z);
            float dist = Mathf.Max(0.1f, fromY - toY);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, rayBuf, dist, surfaceMask, QueryTriggerInteraction.Ignore);
            if (n >= rayBuf.Length) ctx.bufferFull++;
            Array.Sort(rayBuf, 0, n, HitDistanceComparer.Instance);
            float lastY = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = rayBuf[i];
                Collider c = h.collider;
                if (c == null || probeCols.Contains(c) || playerCols.Contains(c)) continue;
                if (h.normal.y < SurfaceMinNormalY) continue;
                if (lastY - h.point.y < 0.05f) { ctx.dedup++; continue; }
                lastY = h.point.y;
                string bur = BuriedBy(h.point + Vector3.up * 0.05f, c);
                if (bur != null) { CountBuried(ctx, kind, bur); continue; }
                ctx.surfaces.Add(new Surface { world = h.point, normalY = h.normal.y, collider = c, tag = tag, kind = kind });
                ctx.surfKind[kind]++;
            }
        }

        /// <summary>점 p(반지름 0.02)가 self 아닌 콜라이더 속이면 그 콜라이더 경로, 아니면 null.</summary>
        private string BuriedBy(Vector3 p, Collider self)
        {
            int n = Physics.OverlapSphereNonAlloc(p, 0.02f, ovBuf, surfaceMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = ovBuf[i];
                if (c == null || c == self || c.isTrigger || probeCols.Contains(c) || playerCols.Contains(c)) continue;
                return PathOf(c.transform);
            }
            return null;
        }

        private sealed class HitDistanceComparer : IComparer<RaycastHit>
        {
            public static readonly HitDistanceComparer Instance = new HitDistanceComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        private static void TakeSnapshot(SweepCtx ctx)
        {
            ctx.snapshot.Clear();
            foreach (Collider c in Object.FindObjectsOfType<Collider>())
                ctx.snapshot[c] = new Pose(c.transform.position, c.transform.rotation);
        }

        private bool IsMoving(SweepCtx ctx, Collider c)
        {
            if (c == null) return false;
            if (c.attachedRigidbody != null && !probeCols.Contains(c) && !playerCols.Contains(c)) return true;
            if (!ctx.snapshot.TryGetValue(c, out Pose p)) return true; // 스윕 중 새로 생긴 콜라이더(기믹 생성물)
            return (c.transform.position - p.position).sqrMagnitude > 0.0001f ||
                   Quaternion.Angle(c.transform.rotation, p.rotation) > 0.5f;
        }

        private bool IsForeign(Collider c) =>
            c != null && !c.isTrigger && !probeCols.Contains(c) && !playerCols.Contains(c);

        // ── 웨이브 ────────────────────────────────────────────────────────────────
        private IEnumerator RunCtxWaves(SweepCtx ctx)
        {
            for (int start = 0; start < ctx.surfaces.Count; start += args.wave)
            {
                int count = Mathf.Min(args.wave, ctx.surfaces.Count - start);
                yield return RunWave(ctx, start, count);
            }
        }

        private void ParkAll()
        {
            for (int s = 0; s < 3; s++)
                foreach (SweepProbe p in pool[s]) Park(p);
        }

        private static void Park(SweepProbe p)
        {
            p.recording = false;
            if (!p.rb.isKinematic)
            {
                p.rb.velocity = Vector3.zero;
                p.rb.angularVelocity = Vector3.zero;
                p.rb.collisionDetectionMode = CollisionDetectionMode.Discrete; // kinematic+CCD 경고 방지
                p.rb.isKinematic = true;
            }
            p.rb.position = p.parkPos;
            p.rb.rotation = p.baseRot;
            p.transform.SetPositionAndRotation(p.parkPos, p.baseRot);
        }

        private static void Place(SweepProbe p, Vector3 pos, Quaternion rot)
        {
            if (p.rb.isKinematic)
            {
                p.rb.isKinematic = false;
                p.rb.collisionDetectionMode = p.cdm;
            }
            p.rb.position = pos;
            p.rb.rotation = rot;
            p.transform.SetPositionAndRotation(pos, rot);
            p.rb.velocity = Vector3.zero;
            p.rb.angularVelocity = Vector3.zero;
            p.rb.WakeUp();
        }

        private IEnumerator RunWave(SweepCtx ctx, int start, int count)
        {
            // [M2R1] 표본의 shapeMask에 든 도형만 띄운다(벽 인접 표본은 사전 검사를 통과한 도형만). 나머지 탐침은 파킹 그대로.
            int total = 0;
            for (int i = 0; i < count; i++)
                for (int s = 0; s < 3; s++)
                    if ((ctx.surfaces[start + i].shapeMask & (1 << s)) != 0) total++;
            SweepProbe[] probes = new SweepProbe[total];
            Surface[] surf = new Surface[total];
            float[] startDepth = new float[total];
            int kk = 0;
            for (int i = 0; i < count; i++)
                for (int s = 0; s < 3; s++)
                {
                    Surface sf = ctx.surfaces[start + i];
                    if ((sf.shapeMask & (1 << s)) == 0) continue;
                    int k = kk++;
                    SweepProbe p = pool[s][i];
                    probes[k] = p;
                    surf[k] = sf;
                    p.contacts.Clear();
                    p.recording = true;
                    Place(p, sf.world + Vector3.up * templates[s].spawnOffset, p.baseRot);
                }
            Physics.SyncTransforms();
            string[] startName = new string[total]; // [R2] 시작 겹침 상대 콜라이더
            for (int k = 0; k < total; k++)
                startDepth[k] = MaxStaticPenetration(ctx, probes[k], probes[k].rb.position, probes[k].rb.rotation, out startName[k]);

            for (int step = 0; step < dropSteps; step++) yield return Wfu;

            // 정착 측정 — 보간 영향을 피하려고 transform이 아니라 rb 자세로 계산한다.
            Vector3[] restPos = new Vector3[total];
            Quaternion[] restRot = new Quaternion[total];
            float[] depth = new float[total];
            string[] depthName = new string[total];
            for (int k = 0; k < total; k++)
            {
                SweepProbe p = probes[k];
                p.recording = false;
                restPos[k] = p.rb.position;
                restRot[k] = p.rb.rotation;
                depth[k] = MaxStaticPenetration(ctx, p, restPos[k], restRot[k], out depthName[k]);
            }
            bool[] support = new bool[total];
            bool[] above = new bool[total];
            float[] footNy = new float[total];
            float[] drop = new float[total];
            for (int k = 0; k < total; k++)
            {
                SweepProbe p = probes[k];
                float bottomY = restPos[k].y - p.bottomOffset;
                drop[k] = surf[k].world.y - bottomY;
                support[k] = HasSupportBelow(restPos[k], bottomY, templates[p.shape].layerMask);
                above[k] = HasSomethingAbove(restPos[k], Mathf.Max(0f, drop[k]) + 2f, templates[p.shape].layerMask);
                // [R1] 광선 출발 = 콜라이더 bounds 중심(피벗 아님), 길이 = 중심→하단 + 0.3 [계산].
                Vector3 center = restPos[k] + restRot[k] * p.centerLocal;
                float centerToBottom = Mathf.Max(0.05f, center.y - bottomY);
                footNy[k] = FootNormalY(center, centerToBottom + 0.3f, templates[p.shape].layerMask);
            }

            // 끼임 시험: 수평 4방향, 방향마다 정착 자세에서 다시 시작해 매 스텝 수평 속도 2U/s 유지(수직은 물리).
            float[] maxMove = new float[total];
            Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            foreach (Vector3 d in dirs)
            {
                for (int k = 0; k < total; k++)
                {
                    Place(probes[k], restPos[k], restRot[k]);
                    probes[k].rb.velocity = d * PushSpeed;
                }
                Physics.SyncTransforms();
                for (int step = 0; step < pushSteps; step++)
                {
                    yield return Wfu;
                    if (step == pushSteps - 1) break;
                    for (int k = 0; k < total; k++)
                    {
                        Rigidbody rb = probes[k].rb;
                        rb.velocity = new Vector3(d.x * PushSpeed, rb.velocity.y, d.z * PushSpeed);
                    }
                }
                for (int k = 0; k < total; k++)
                {
                    Vector3 dp = probes[k].rb.position - restPos[k];
                    float moved = new Vector2(dp.x, dp.z).magnitude;
                    if (moved > maxMove[k]) maxMove[k] = moved;
                }
            }

            // 분류·기록.
            for (int k = 0; k < total; k++)
            {
                SweepProbe p = probes[k];
                int s = p.shape;
                List<string> movingNames = new List<string>();
                foreach (Collider c in p.contacts)
                    if (IsForeign(c) && IsMoving(ctx, c))
                    {
                        string nm = PathOf(c.transform);
                        if (!movingNames.Contains(nm)) movingNames.Add(nm);
                        ctx.movingNames.Add(nm);
                    }
                Cls cls = Classify(drop[k], support[k], above[k], depth[k], maxMove[k], footNy[k],
                                   movingNames.Count > 0, out Cls under);
                ctx.samples[s]++;
                ctx.counts[s, (int)cls]++;
                if (cls == Cls.MOVING_CONTACT) ctx.movingUnder[s, (int)under]++; // [R1] 가려진 기저 분류 집계

                bool isNeg = ctx == neg;
                bool startOv = startDepth[k] > PenetrationFail;
                if (cls == Cls.OK && !isNeg && !startOv) continue;

                Finding f = new Finding
                {
                    shape = ShapeNames[s], cls = cls, underlying = under,
                    surfLocal = ctx.ToLocal(surf[k].world), restLocal = ctx.ToLocal(restPos[k]),
                    drop = drop[k], depth = depth[k], startDepth = startDepth[k], maxMove = maxMove[k], footNy = footNy[k],
                    surfCollider = surf[k].collider != null ? PathOf(surf[k].collider.transform) : "(없음)",
                    tag = surf[k].tag, kind = surf[k].kind, wallName = surf[k].wallName,
                    startCollider = startOv ? startName[k] : null
                };
                if (cls == Cls.MOVING_CONTACT) f.names.AddRange(movingNames);
                else if (under == Cls.PENETRATING && depthName[k] != null) f.names.Add(depthName[k]);
                else if (under == Cls.FELL_THROUGH || under == Cls.FELL_OFF_EDGE) f.names.Add(f.surfCollider);
                else if (under == Cls.WEDGED) f.names.AddRange(NearbyNames(p, restPos[k], restRot[k], 4));

                // [R2] 시작 겹침(>0.05) 표본: 판정이 PhysX 밀어내기에 좌우될 수 있는 표본이라 끝 분류와 무관하게
                // 도형별 최대 MaxFindingsPerBucket건까지 위치·상대 콜라이더를 남긴다(개수는 전부 센다). exit 규칙은 불변.
                if (startOv)
                {
                    ctx.startOverlap[s]++;
                    ctx.startOverlapKind[s, Mathf.Clamp(surf[k].kind, 0, 2)]++;
                    if (!isNeg && ctx.startFindingsCount[s]++ < MaxFindingsPerBucket) ctx.startFindings.Add(f);
                    // [M2R1] 시작 겹침이 저머리(스폰 윗면 높이 안에 천장) 때문인지 — 저머리 구역 목록에 모은다(정보).
                    if (!isNeg && LowHeadroom(surf[k].world, s, out float head, out string ceil))
                        RecordLowHead(ctx, surf[k].world, head, ceil, s, false);
                }
                if (cls == Cls.OK && !isNeg) continue;

                if (isNeg) { ctx.allNeg.Add(f); continue; }
                // [R1] MOVING_CONTACT는 기저 분류별로 따로 담아, 기저 실패 지점이 OK 기저 지점 300건 한도에 밀려 빠지지 않게 한다.
                string bucket = f.shape + "|" + cls + (cls == Cls.MOVING_CONTACT ? "/" + under : "");
                ctx.bucketCount.TryGetValue(bucket, out int bc);
                ctx.bucketCount[bucket] = bc + 1;
                if (bc < MaxFindingsPerBucket) ctx.findings.Add(f);
            }

            for (int k = 0; k < total; k++) Park(probes[k]);
            Physics.SyncTransforms();
        }

        private float MaxStaticPenetration(SweepCtx ctx, SweepProbe p, Vector3 rbPos, Quaternion rbRot, out string name)
        {
            name = null;
            Vector3 cPos = rbPos + rbRot * p.colLocalPos;
            Quaternion cRot = rbRot * p.colLocalRot;
            int n = Physics.OverlapSphereNonAlloc(cPos, p.queryRadius + 0.1f, ovBuf, templates[p.shape].layerMask,
                                                  QueryTriggerInteraction.Ignore);
            float best = 0f;
            for (int i = 0; i < n; i++)
            {
                Collider o = ovBuf[i];
                if (!IsForeign(o) || IsMoving(ctx, o)) continue; // 정적 콜라이더만
                bool hit;
                float dist;
                try
                {
                    hit = Physics.ComputePenetration(p.col, cPos, cRot, o, o.transform.position, o.transform.rotation,
                                                     out _, out dist);
                }
                catch { continue; }
                if (hit && dist > best) { best = dist; name = PathOf(o.transform); }
            }
            return best;
        }

        private bool HasSupportBelow(Vector3 pos, float bottomY, int mask)
        {
            Vector3 center = new Vector3(pos.x, bottomY - FellSupportRange / 2f, pos.z);
            Vector3 half = new Vector3(0.45f, FellSupportRange / 2f, 0.45f); // 수평 반폭 0.45 [추정] ≈ 도형 폭 1U의 절반 이내
            int n = Physics.OverlapBoxNonAlloc(center, half, ovBuf, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (IsForeign(ovBuf[i])) return true;
            return false;
        }

        private bool HasSomethingAbove(Vector3 pos, float len, int mask)
        {
            int n = Physics.RaycastNonAlloc(pos, Vector3.up, rayBuf, len, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (IsForeign(rayBuf[i].collider)) return true;
            return false;
        }

        private float FootNormalY(Vector3 pos, float len, int mask)
        {
            int n = Physics.RaycastNonAlloc(pos, Vector3.down, rayBuf, len, mask, QueryTriggerInteraction.Ignore);
            float bestDist = float.PositiveInfinity, ny = -1f;
            for (int i = 0; i < n; i++)
            {
                if (!IsForeign(rayBuf[i].collider)) continue;
                if (rayBuf[i].distance < bestDist) { bestDist = rayBuf[i].distance; ny = rayBuf[i].normal.y; }
            }
            return ny;
        }

        private List<string> NearbyNames(SweepProbe p, Vector3 rbPos, Quaternion rbRot, int max)
        {
            List<string> names = new List<string>();
            Vector3 cPos = rbPos + rbRot * p.colLocalPos;
            int n = Physics.OverlapSphereNonAlloc(cPos, p.queryRadius + 0.1f, ovBuf, templates[p.shape].layerMask,
                                                  QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n && names.Count < max; i++)
            {
                if (!IsForeign(ovBuf[i])) continue;
                string nm = PathOf(ovBuf[i].transform);
                if (!names.Contains(nm)) names.Add(nm);
            }
            return names;
        }

        private static string PathOf(Transform t)
        {
            string s = t.name;
            Transform cur = t.parent;
            for (int d = 0; d < 2 && cur != null; d++) { s = cur.name + "/" + s; cur = cur.parent; }
            return s;
        }

        /// <summary>[M2R1] 비율 문자열(소수 1자리 %). 분모 0이면 "-".</summary>
        private static string Pct(int a, int b) =>
            b > 0 ? (100f * a / b).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "-";

        private static int Sum(SweepCtx c, Cls k) =>c.counts[0, (int)k] + c.counts[1, (int)k] + c.counts[2, (int)k];

        /// <summary>[INF2-2] 벽 인접 시작 겹침(>0.05) 수 — 도형 s(0~2), 전 섹터 합. s = -1이면 세 도형 합.</summary>
        private int WallStartOverlapTotal(int s = -1)
        {
            int t = 0;
            foreach (SweepCtx c in ctxs)
                for (int k = 0; k < 3; k++)
                    if (s < 0 || s == k) t += c.startOverlapKind[k, KWall];
            return t;
        }
        private static int SumUnder(SweepCtx c, Cls k) => c.movingUnder[0, (int)k] + c.movingUnder[1, (int)k] + c.movingUnder[2, (int)k];
        private int mcFail;
        private string mcDetail = "-";

        // ── 종료·출력 ──────────────────────────────────────────────────────────────
        private void Finish()
        {
            if (finished) return;
            finished = true;
            StopAllCoroutines();
            Time.timeScale = origTimeScale; // 원복(fixedDeltaTime은 처음부터 건드리지 않았다)

            int fellThrough = 0, penetrating = 0, wedged = 0, moving = 0, fellOff = 0;
            int mcFellThrough = 0, mcPenetrating = 0, mcWedged = 0;
            bool sectorError = false;
            foreach (SweepCtx c in ctxs)
            {
                fellThrough += Sum(c, Cls.FELL_THROUGH);
                penetrating += Sum(c, Cls.PENETRATING);
                wedged += Sum(c, Cls.WEDGED);
                moving += Sum(c, Cls.MOVING_CONTACT);
                fellOff += Sum(c, Cls.FELL_OFF_EDGE);
                mcFellThrough += SumUnder(c, Cls.FELL_THROUGH);
                mcPenetrating += SumUnder(c, Cls.PENETRATING);
                mcWedged += SumUnder(c, Cls.WEDGED);
                if (c.error != null || !c.done) sectorError = true;
            }
            // [R1] MOVING_CONTACT에 가려진 기저 실패. 지시서가 움직이는 기믹 접촉을 별도 분류로 두라고 했으므로
            // exit에는 넣지 않고(함정 문이 열려 떨어진 것 등 정상 동작일 수 있다) 요약 줄 MC_FAIL로 드러낸다 — 검토 요청.
            mcFail = mcFellThrough + mcPenetrating;
            mcDetail = $"FELL_THROUGH={mcFellThrough} PENETRATING={mcPenetrating} WEDGED={mcWedged}";
            bool requestedAll = args != null && ctxs.Count == args.sectors.Count;
            int exitCode = (fellThrough == 0 && penetrating == 0 && negPass && fatal.Count == 0 && !watchdogHit
                            && !sectorError && requestedAll) ? 0 : 1;
            int failCount = fellThrough + penetrating;

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string outDir = ResolveOutDir();
            Directory.CreateDirectory(outDir);
            string baseName = "SWEEP_" + stamp;
            int suffix = 1;
            while (File.Exists(Path.Combine(outDir, baseName + ".txt")) || File.Exists(Path.Combine(outDir, baseName + ".json")))
                baseName = $"SWEEP_{stamp}_{++suffix}";
            string txtPath = Path.Combine(outDir, baseName + ".txt");
            string jsonPath = Path.Combine(outDir, baseName + ".json");

            string txt = BuildText(exitCode, fellThrough, penetrating, wedged, moving, fellOff, sectorError, requestedAll);
            string json = BuildJson(exitCode, fellThrough, penetrating, wedged, moving, fellOff);
            try
            {
                File.WriteAllText(txtPath, txt, new UTF8Encoding(true));
                File.WriteAllText(jsonPath, json, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} 출력 파일 쓰기 실패: {e}");
                exitCode = 1;
            }
            Debug.Log($"{LogTag} 상세 저장: {txtPath}\n{txt}");
            // [INF2-2] WALL_OV = 벽 인접 시작 겹침(>0.05) 합(세 도형·전 섹터, 목표 0) — 정보 항목, exit 미반영(판정 B9).
            string summary = $"FAIL={failCount} WARN={wedged} MC_FAIL={mcFail} WALL_OV={WallStartOverlapTotal()} NEG={(negPass ? "PASS" : "FAIL")}" + (watchdogHit ? " WATCHDOG" : "");
            Debug.Log($"{LogTag} 결과 {summary} exit={exitCode} file={Path.GetFileName(txtPath)}");

            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode);
            else
                EditorApplication.isPlaying = false;
        }

        private string BuildText(int exitCode, int ft, int pen, int wed, int mov, int off, bool sectorError, bool requestedAll)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Map4StuckSweep (INF2) — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"판정: {(exitCode == 0 ? "통과" : "실패")} (exit={exitCode}) — FELL_THROUGH={ft} PENETRATING={pen} " +
                          $"음성대조={(negPass ? "PASS" : "FAIL")} | 경고 WEDGED={wed} | 별도 MOVING_CONTACT={mov} | 정보 FELL_OFF_EDGE={off}");
            sb.AppendLine($"MOVING_CONTACT 기저 실패(MC_FAIL, exit 미반영 — 움직이는 기믹 접촉이라 별도): {mcFail} ({mcDetail})");
            // [INF2-2] 벽 인접 시작 겹침(목표 0, exit 미반영 — 판정 B9).
            sb.AppendLine($"벽 인접 시작 겹침(>{F(PenetrationFail)}, 목표 0, exit 미반영): 합 {WallStartOverlapTotal()} — " +
                          $"{ShapeNames[0]} {WallStartOverlapTotal(0)} · {ShapeNames[1]} {WallStartOverlapTotal(1)} · " +
                          $"{ShapeNames[2]} {WallStartOverlapTotal(2)}");
            // [M2R1] 벽 인접 버림(도형별)·저머리 구역 수 — WALL_OV=0이 버림으로 얻은 몫을 함께 보인다(정보, exit 미반영).
            {
                int cand = 0, dropAll = 0, partial = 0, zones = 0;
                int[] dropS = new int[3];
                foreach (SweepCtx c in ctxs)
                {
                    cand += c.wallCandidates; dropAll += c.wallPreDropped; partial += c.wallPrePartial; zones += c.lowHead.Count;
                    for (int s = 0; s < 3; s++) dropS[s] += c.wallPreDroppedByShape[s];
                }
                sb.AppendLine($"벽 인접 버림(정보, exit 미반영): 후보 {cand} · 전 도형 버림 {dropAll}({Pct(dropAll, cand)}) · 일부 도형만 버림 {partial} · " +
                              $"도형별 {ShapeNames[0]} {dropS[0]}({Pct(dropS[0], cand)}) / {ShapeNames[1]} {dropS[1]}({Pct(dropS[1], cand)}) / " +
                              $"{ShapeNames[2]} {dropS[2]}({Pct(dropS[2], cand)}) | 저머리(스폰 불가) 구역 {zones}곳 — 섹터별 '저머리' 줄 참고");
            }
            {
                float total = runStartRealtime >= 0f ? Time.realtimeSinceStartup - runStartRealtime : -1f;
                StringBuilder ts = new StringBuilder();
                foreach (SweepCtx c in ctxs) ts.Append($" {c.label}={(c.seconds >= 0f ? F(c.seconds) + "s" : "미완료")}");
                sb.AppendLine($"소요(realtime): 전체 {F(total)}s (watchdog {F(WatchdogSeconds)}s) | 섹터별{ts}");
            }
            if (watchdogHit) sb.AppendLine("watchdog: 발동 — 아래 결과는 발동 시점까지의 부분 결과다.");
            if (sectorError || !requestedAll) sb.AppendLine("섹터 오류/미완료 있음 — 아래 섹터 표의 '오류' 열 참고.");
            foreach (string f in fatal) sb.AppendLine(f);
            sb.AppendLine();
            sb.AppendLine("분류 규칙(지시서 INF2 수치표): FELL_THROUGH=시작 윗면보다 하단이 1U 이상 아래 + 아래 0.6U 안 콜라이더 없음 + " +
                          "위쪽에 콜라이더 있음(관통) / FELL_OFF_EDGE=앞 두 조건 + 위쪽 비어 있음(가장자리 추락, 정보) / " +
                          "PENETRATING=끝 시점 정적 콜라이더 관통 > 0.05U / WEDGED=수평 4방향 2U/s×0.4s 모두 < 0.15U + 발밑 법선 y < 0.95 / " +
                          "MOVING_CONTACT=움직이는 팀 기믹(attachedRigidbody 또는 스윕 중 이동한 콜라이더)과 접촉(기저 분류 병기).");
            sb.AppendLine();
            sb.Append(log);
            sb.AppendLine();
            sb.AppendLine($"음성 대조: {(negPass ? "PASS" : "FAIL")}");
            sb.Append(negDetail);
            sb.AppendLine();
            sb.AppendLine("섹터 | 이름 | 간격 | 격자열(주+반칸) | 윗면(격자/반칸/벽인접) | 매몰제외(격자/반칸/벽인접) | 도형 | 표본 | OK | FELL_THROUGH | " +
                          "PENETRATING | WEDGED | MOVING_CONTACT | MC기저(OK/FT/PEN/WED/OFF) | FELL_OFF_EDGE | 시작겹침>0.05(격자/반칸/벽인접) | 오류");
            foreach (SweepCtx c in ctxs)
                for (int s = 0; s < 3; s++)
                    sb.AppendLine($"{c.label} | {c.name} | {F(c.spacing)} | {c.columns}+{c.staggerColumns} | " +
                                  $"{c.surfaces.Count}({c.surfKind[0]}/{c.surfKind[1]}/{c.surfKind[2]}) | " +
                                  $"{c.buried}({c.buriedKind[0]}/{c.buriedKind[1]}/{c.buriedKind[2]}) | {ShapeNames[s]} | " +
                                  $"{c.samples[s]} | {c.counts[s, 0]} | {c.counts[s, 1]} | {c.counts[s, 2]} | {c.counts[s, 3]} | " +
                                  $"{c.counts[s, 4]} | {c.movingUnder[s, 0]}/{c.movingUnder[s, 1]}/{c.movingUnder[s, 2]}/{c.movingUnder[s, 3]}/{c.movingUnder[s, 5]} | " +
                                  $"{c.counts[s, 5]} | {c.startOverlap[s]}({c.startOverlapKind[s, 0]}/{c.startOverlapKind[s, 1]}/{c.startOverlapKind[s, 2]}) | " +
                                  $"{(c.error ?? (c.done ? "-" : "미완료"))}");
            sb.AppendLine();
            // [컨트롤타워 FIN2 판정 2026-09-29 — 순간이동 생성 인공물 제거, 진짜 좁은 틈은 원위치로 유지]
            // 격자·반칸 표본 사전 겹침 이동 집계(섹터×도형). 이동 = 원위치에서 겹쳤다가 옮겨져 통과한 도형 수,
            // 원위치 = 8방향·한도 안에서 자리를 못 찾아 원래 자리를 그대로 쓴 도형 수(이 표본은 시작 겹침/PENETRATING/WEDGED로 그대로 검출된다).
            {
                sb.AppendLine($"격자·반칸 사전 겹침 이동(FIN2, 도형별 — 원위치에서 사전 겹침(>{F(PenetrationFail)})인 도형만 수평 8방향으로 " +
                              $"{F(WallBackoffStep)}씩 최대 {F(WallBackoffMax)} 이동해 같은 윗면에서 겹침 없는 자리를 찾고, 못 찾으면 원위치 유지 — exit 미반영):");
                sb.AppendLine("섹터 | 도형 | 격자 이동 | 격자 원위치 | 반칸 이동 | 반칸 원위치 | 이동 합 | 원위치 합");
                int[] gpMoved = new int[3], gpStay = new int[3];
                foreach (SweepCtx c in ctxs)
                    for (int s = 0; s < 3; s++)
                    {
                        int mvG = c.gridPreMoved[0, s], mvS = c.gridPreMoved[1, s];
                        int stG = c.gridPreStay[0, s], stS = c.gridPreStay[1, s];
                        gpMoved[s] += mvG + mvS; gpStay[s] += stG + stS;
                        sb.AppendLine($"{c.label} | {ShapeNames[s]} | {mvG} | {stG} | {mvS} | {stS} | {mvG + mvS} | {stG + stS}");
                    }
                sb.AppendLine($"격자·반칸 사전 겹침 이동 합계(전 섹터) — 이동: {ShapeNames[0]} {gpMoved[0]} · {ShapeNames[1]} {gpMoved[1]} · {ShapeNames[2]} {gpMoved[2]} " +
                              $"| 원위치: {ShapeNames[0]} {gpStay[0]} · {ShapeNames[1]} {gpStay[1]} · {ShapeNames[2]} {gpStay[2]}");
            }
            sb.AppendLine();
            // [R2] 표본 구성·매몰 원인·벽 인접 예시.
            foreach (SweepCtx c in ctxs)
            {
                sb.AppendLine($"{c.label} 표본 구성: 주 격자 {c.columns}열→윗면 {c.surfKind[KGrid]} · 반 칸 보조 {c.staggerColumns}열→윗면 {c.surfKind[KStagger]} · " +
                              $"벽 인접 {c.surfKind[KWall]}(중복 제거 {c.wallDedup}) · wallClear={F(wallClear)}");
                // [INF2-2] 벽 인접 배치(n_h 기준)·사전 겹침 검사 결과와 벽 인접 시작 겹침(도형별, 목표 0).
                sb.AppendLine($"{c.label} 벽 인접 사전 겹침 검사(q = 맞은 점 + n_h·{F(wallClear)}, 물림 {F(WallBackoffStep)}×최대 {F(WallBackoffMax)}): " +
                              $"후보 {c.wallCandidates} · 첫 위치 겹침(도형별 {ShapeNames[0]}/{ShapeNames[1]}/{ShapeNames[2]}) " +
                              $"{c.wallPreFirstOverlap[0]}/{c.wallPreFirstOverlap[1]}/{c.wallPreFirstOverlap[2]} · 물려서 통과 {c.wallPreShifted}" +
                              $"(도형별 {c.wallPreShiftedByShape[0]}/{c.wallPreShiftedByShape[1]}/{c.wallPreShiftedByShape[2]}) · " +
                              $"전 도형 버림 {c.wallPreDropped} · 일부 도형만 버림 {c.wallPrePartial} | 벽 인접 시작 겹침(목표 0) {ShapeNames[0]} {c.startOverlapKind[0, KWall]} · " +
                              $"{ShapeNames[1]} {c.startOverlapKind[1, KWall]} · {ShapeNames[2]} {c.startOverlapKind[2, KWall]} | " +
                              $"소요 {(c.seconds >= 0f ? F(c.seconds) + "s" : "미완료")}");
                // [M2R1] 버림 비율(후보 대비) — 전 도형·도형별.
                sb.AppendLine($"{c.label} 벽 인접 버림 비율(후보 {c.wallCandidates} 대비): 전 도형 {c.wallPreDropped}({Pct(c.wallPreDropped, c.wallCandidates)}) · " +
                              $"{ShapeNames[0]} {c.wallPreDroppedByShape[0]}({Pct(c.wallPreDroppedByShape[0], c.wallCandidates)}) · " +
                              $"{ShapeNames[1]} {c.wallPreDroppedByShape[1]}({Pct(c.wallPreDroppedByShape[1], c.wallCandidates)}) · " +
                              $"{ShapeNames[2]} {c.wallPreDroppedByShape[2]}({Pct(c.wallPreDroppedByShape[2], c.wallCandidates)}) · " +
                              $"버린 도형 중 천장 없음(수평 겹침) {c.lowHeadUnknownWallDrop}");
                if (c.wallPreDropExamples.Count > 0)
                    sb.AppendLine($"{c.label} 사전 검사로 버린 벽 인접 후보(앞 {c.wallPreDropExamples.Count}건, 섹터 로컬 첫 위치 ← 벽): " +
                                  string.Join("; ", c.wallPreDropExamples));
                // [M2R1] 저머리(스폰 불가) 구역 — 천장 콜라이더별. 여기 든 자리는 어떤 표본으로도 그 도형의 낙하 판정이 안 된다(한계).
                if (c.lowHead.Count == 0)
                    sb.AppendLine($"{c.label} 저머리(스폰 불가) 구역: 없음");
                else
                {
                    List<LowHeadZone> zl = new List<LowHeadZone>(c.lowHead.Values);
                    zl.Sort((a, b) => string.CompareOrdinal(a.ceiling, b.ceiling));
                    sb.AppendLine($"{c.label} 저머리(스폰 불가) 구역 {zl.Count}곳(천장 콜라이더별, 섹터 로컬 윗면 범위, 윗면→천장 거리, " +
                                  $"벽인접 버림/시작겹침 도형별 {ShapeNames[0]}/{ShapeNames[1]}/{ShapeNames[2]} — 이 자리는 그 도형 낙하 판정 불가):");
                    foreach (LowHeadZone z in zl)
                        sb.AppendLine($"  LOW_HEADROOM {z.ceiling} 범위={V(z.min)}~{V(z.max)} 머리여유={F(z.minHead)}~{F(z.maxHead)} " +
                                      $"벽인접버림={z.wallDrop[0]}/{z.wallDrop[1]}/{z.wallDrop[2]} 시작겹침={z.startOv[0]}/{z.startOv[1]}/{z.startOv[2]}");
                }
                if (c.buriedBy.Count > 0)
                {
                    List<KeyValuePair<string, int>> bl = new List<KeyValuePair<string, int>>(c.buriedBy);
                    bl.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
                    StringBuilder bs = new StringBuilder();
                    for (int i = 0; i < bl.Count && i < 15; i++) bs.Append((i > 0 ? ", " : "") + bl[i].Key + "×" + bl[i].Value);
                    sb.AppendLine($"{c.label} 매몰 원인(덮은 콜라이더 상위 15): {bs}");
                }
                int shown = 0;
                foreach (Surface sf in c.surfaces)
                {
                    if (sf.kind != KWall) continue;
                    if (shown == 0) sb.Append($"{c.label} 벽 인접 표본 예시(앞 5건, 섹터 로컬 윗면 ← 벽):");
                    sb.Append($" {V(c.ToLocal(sf.world))}←{sf.wallName}" + (sf.shapeMask != 7 ? $"[{MaskNames(sf.shapeMask)}만]" : "") + ";");
                    if (++shown >= 5) break;
                }
                if (shown > 0) sb.AppendLine();
            }
            sb.AppendLine();
            foreach (SweepCtx c in ctxs)
            {
                // [R2] 시작 겹침 표본 지점(끝 분류 무관).
                if (c.startFindings.Count > 0)
                {
                    sb.AppendLine($"── {c.label} {c.name} 시작 겹침(>{F(PenetrationFail)}) 표본(섹터 로컬, 도형별 최대 {MaxFindingsPerBucket}건 — " +
                                  "스폰 순간 정적 콜라이더와 겹쳐 PhysX 밀어내기 뒤 판정된 표본. exit 미반영)");
                    foreach (Finding f in c.startFindings)
                        sb.AppendLine($"  START_OVERLAP   {f.shape,-11} 종류={KindNames[Mathf.Clamp(f.kind, 0, 2)]} 윗면={V(f.surfLocal)} " +
                                      $"시작깊이={F(f.startDepth)} 시작상대={f.startCollider ?? "(없음)"} → 끝={f.cls}(기저 {f.underlying}) " +
                                      $"끝깊이={F(f.depth)} 정지={V(f.restLocal)} 윗면콜라이더={f.surfCollider}" +
                                      (f.wallName != null ? $" 벽={f.wallName}" : ""));
                    for (int s = 0; s < 3; s++)
                        if (c.startFindingsCount[s] > MaxFindingsPerBucket)
                            sb.AppendLine($"  (생략) {ShapeNames[s]}|START_OVERLAP: 전체 {c.startFindingsCount[s]}건 중 {MaxFindingsPerBucket}건만 기록");
                }
            }
            sb.AppendLine();
            foreach (SweepCtx c in ctxs)
            {
                if (c.movingNames.Count > 0)
                    sb.AppendLine($"{c.label} 움직이는 물체(접촉): {string.Join(", ", c.movingNames)}");
                if (c.findings.Count == 0) continue;
                sb.AppendLine($"── {c.label} {c.name} 지점(섹터 로컬, 도형·분류별 최대 {MaxFindingsPerBucket}건 — 초과분은 개수만)");
                foreach (Cls k in new[] { Cls.FELL_THROUGH, Cls.PENETRATING, Cls.WEDGED, Cls.MOVING_CONTACT, Cls.FELL_OFF_EDGE })
                    foreach (Finding f in c.findings)
                    {
                        if (f.cls != k) continue;
                        sb.AppendLine($"  {f.cls,-15} {f.shape,-11} 윗면={V(f.surfLocal)} 정지={V(f.restLocal)} 깊이={F(f.depth)} " +
                                      $"낙하={F(f.drop)} 밀림={F(f.maxMove)} 발밑ny={F(f.footNy)} 기저={f.underlying} " +
                                      $"윗면콜라이더={f.surfCollider} 상대=[{string.Join(", ", f.names)}] 종류={KindNames[Mathf.Clamp(f.kind, 0, 2)]}" +
                                      (f.startCollider != null ? $" 시작겹침={F(f.startDepth)}@{f.startCollider}" : ""));
                    }
                foreach (KeyValuePair<string, int> kv in c.bucketCount)
                    if (kv.Value > MaxFindingsPerBucket)
                        sb.AppendLine($"  (생략) {kv.Key}: 전체 {kv.Value}건 중 {MaxFindingsPerBucket}건만 기록");
            }
            return sb.ToString();
        }

        private string BuildJson(int exitCode, int ft, int pen, int wed, int mov, int off)
        {
            StringBuilder j = new StringBuilder();
            j.Append("{\n");
            j.Append($"  \"tool\": \"Map4StuckSweep\",\n  \"task\": \"INF2\",\n  \"time\": \"{DateTime.Now:yyyy-MM-ddTHH:mm:ss}\",\n");
            j.Append($"  \"exit\": {exitCode},\n  \"watchdog\": {B(watchdogHit)},\n");
            j.Append($"  \"totals\": {{\"FELL_THROUGH\": {ft}, \"PENETRATING\": {pen}, \"WEDGED\": {wed}, \"MOVING_CONTACT\": {mov}, \"FELL_OFF_EDGE\": {off}, " +
                     $"\"MC_FAIL\": {mcFail}}},\n");
            // [INF2-2] 벽 인접 시작 겹침(도형별, 목표 0)·소요 시간.
            j.Append($"  \"wallStartOverlap\": {{\"total\": {WallStartOverlapTotal()}, {S(ShapeNames[0])}: {WallStartOverlapTotal(0)}, " +
                     $"{S(ShapeNames[1])}: {WallStartOverlapTotal(1)}, {S(ShapeNames[2])}: {WallStartOverlapTotal(2)}}},\n");
            j.Append($"  \"elapsedSeconds\": {F(runStartRealtime >= 0f ? Time.realtimeSinceStartup - runStartRealtime : -1f)},\n");
            j.Append($"  \"detailedSectors\": [{string.Join(", ", DetailedSectors)}],\n");
            if (args != null)
                j.Append($"  \"args\": {{\"sectors\": [{string.Join(", ", args.sectors)}], \"spacing\": {F(args.spacing)}, " +
                         $"\"coarseSpacing\": {F(args.coarseSpacing)}, \"timeScale\": {F(args.timeScale)}, \"wave\": {args.wave}, " +
                         $"\"fixedDeltaTime\": {F(Time.fixedDeltaTime)}, \"dropSteps\": {dropSteps}, \"pushSteps\": {pushSteps}}},\n");
            j.Append("  \"thresholds\": {" +
                     $"\"surfaceMinNormalY\": {F(SurfaceMinNormalY)}, \"dropHeight\": {F(DropHeight)}, \"simSeconds\": {F(SimSeconds)}, " +
                     $"\"fellDrop\": {F(FellDrop)}, \"fellSupportRange\": {F(FellSupportRange)}, \"penetrationFail\": {F(PenetrationFail)}, " +
                     $"\"pushSpeed\": {F(PushSpeed)}, \"pushSeconds\": {F(PushSeconds)}, \"pushMinMove\": {F(PushMinMove)}, " +
                     $"\"flatFloorNormalY\": {F(FlatFloorNormalY)}, \"negativeGap\": {F(NegativeGap)}}},\n");
            j.Append("  \"fatal\": [");
            for (int i = 0; i < fatal.Count; i++) j.Append((i > 0 ? ", " : "") + S(fatal[i]));
            j.Append("],\n");
            j.Append("  \"probes\": [");
            for (int s = 0; s < 3; s++)
            {
                ShapeTemplate t = templates[s];
                if (s > 0) j.Append(", ");
                if (t == null) { j.Append("null"); continue; }
                j.Append($"{{\"shape\": {S(t.shape)}, \"source\": {S(t.describe)}, \"bottomOffset\": {F(t.bottomOffset)}, " +
                         $"\"spawnOffset\": {F(t.spawnOffset)}, \"boundsSize\": {VJ(t.size)}, \"centerLocal\": {VJ(t.centerLocal)}}}");
            }
            j.Append("],\n");
            j.Append($"  \"negativeControl\": {{\"pass\": {B(negPass)}, " +
                     $"\"wedge\": {{\"innerWidth\": {F(wedgeWidthUsed)}, \"slopeDeg\": {F(WedgeSlopeDeg)}, \"clearance\": {F(WedgeClearance)}, " +
                     $"\"wallTop\": {F(WedgeWallTop)}}}, \"items\": [");
            if (neg != null)
                for (int i = 0; i < neg.allNeg.Count; i++)
                    j.Append((i > 0 ? ", " : "") + FindingJson(neg.allNeg[i]));
            j.Append("]},\n");
            j.Append("  \"sectors\": [\n");
            for (int ci = 0; ci < ctxs.Count; ci++)
            {
                SweepCtx c = ctxs[ci];
                j.Append($"    {{\"id\": {c.id}, \"name\": {S(c.name ?? "")}, \"spacing\": {F(c.spacing)}, \"columns\": {c.columns}, " +
                         $"\"staggerColumns\": {c.staggerColumns}, \"wallClear\": {F(wallClear)}, \"wallDedup\": {c.wallDedup}, " +
                         $"\"surfaces\": {c.surfaces.Count}, \"surfacesByKind\": {KindJson(c.surfKind)}, " +
                         $"\"buried\": {c.buried}, \"buriedByKind\": {KindJson(c.buriedKind)}, \"buriedBy\": {{");
                {
                    List<KeyValuePair<string, int>> bl = new List<KeyValuePair<string, int>>(c.buriedBy);
                    bl.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
                    for (int i = 0; i < bl.Count; i++) j.Append((i > 0 ? ", " : "") + $"{S(bl[i].Key)}: {bl[i].Value}");
                }
                j.Append($"}}, \"error\": {(c.error == null ? "null" : S(c.error))}, " +
                         $"\"done\": {B(c.done)}, \"origin\": {VJ(c.origin)}, \"seconds\": {F(c.seconds)},\n");
                // [INF2-2] 벽 인접 사전 겹침 검사.
                j.Append($"      \"wallPrecheck\": {{\"candidates\": {c.wallCandidates}, \"shifted\": {c.wallPreShifted}, " +
                         $"\"dropped\": {c.wallPreDropped}, \"firstOverlapByShape\": {{{S(ShapeNames[0])}: {c.wallPreFirstOverlap[0]}, " +
                         $"{S(ShapeNames[1])}: {c.wallPreFirstOverlap[1]}, {S(ShapeNames[2])}: {c.wallPreFirstOverlap[2]}}}, " +
                         $"\"backoffStep\": {F(WallBackoffStep)}, \"backoffMax\": {F(WallBackoffMax)}, " +
                         // [M2R1] dropped = 세 도형 모두 버린 후보, partial = 일부 도형만 버린 후보, 도형별 물림·버림.
                         $"\"partial\": {c.wallPrePartial}, \"shiftedByShape\": {ShapeJson(c.wallPreShiftedByShape)}, " +
                         $"\"droppedByShape\": {ShapeJson(c.wallPreDroppedByShape)}, \"droppedNoCeiling\": {c.lowHeadUnknownWallDrop}, " +
                         $"\"dropExamples\": [");
                for (int i = 0; i < c.wallPreDropExamples.Count; i++) j.Append((i > 0 ? ", " : "") + S(c.wallPreDropExamples[i]));
                j.Append("]},\n");
                // [컨트롤타워 FIN2 판정 2026-09-29] 격자·반칸 표본 사전 겹침 이동 집계(도형별 moved/stay, 격자·반칸 구분 + 합).
                j.Append($"      \"gridPrecheck\": {GridPreJson(c)},\n");
                j.Append("      \"lowHeadroom\": [");
                {
                    List<LowHeadZone> zl = new List<LowHeadZone>(c.lowHead.Values);
                    zl.Sort((a, b) => string.CompareOrdinal(a.ceiling, b.ceiling));
                    for (int i = 0; i < zl.Count; i++)
                        j.Append((i > 0 ? ", " : "") + $"{{\"ceiling\": {S(zl[i].ceiling)}, \"min\": {VJ(zl[i].min)}, \"max\": {VJ(zl[i].max)}, " +
                                 $"\"headroomMin\": {F(zl[i].minHead)}, \"headroomMax\": {F(zl[i].maxHead)}, " +
                                 $"\"wallDropByShape\": {ShapeJson(zl[i].wallDrop)}, \"startOverlapByShape\": {ShapeJson(zl[i].startOv)}}}");
                }
                j.Append("],\n      \"shapes\": {");
                for (int s = 0; s < 3; s++)
                {
                    if (s > 0) j.Append(", ");
                    j.Append($"{S(ShapeNames[s])}: {{\"samples\": {c.samples[s]}, \"startOverlap\": {c.startOverlap[s]}, " +
                             $"\"startOverlapByKind\": {{\"grid\": {c.startOverlapKind[s, 0]}, \"stagger\": {c.startOverlapKind[s, 1]}, \"wall\": {c.startOverlapKind[s, 2]}}}");
                    foreach (Cls k in AllCls) j.Append($", {S(k.ToString())}: {c.counts[s, (int)k]}");
                    j.Append(", \"movingContactUnderlying\": {");
                    for (int ki = 0; ki < AllCls.Length; ki++)
                        j.Append((ki > 0 ? ", " : "") + $"{S(AllCls[ki].ToString())}: {c.movingUnder[s, (int)AllCls[ki]]}");
                    j.Append("}}");
                }
                j.Append("},\n      \"movingObjects\": [");
                int mi = 0;
                foreach (string nm in c.movingNames) j.Append((mi++ > 0 ? ", " : "") + S(nm));
                j.Append("],\n      \"startOverlaps\": [");
                for (int i = 0; i < c.startFindings.Count; i++)
                    j.Append((i > 0 ? ",\n        " : "\n        ") + FindingJson(c.startFindings[i]));
                j.Append(c.startFindings.Count > 0 ? "\n      ]" : "]");
                j.Append(",\n      \"findings\": [");
                for (int i = 0; i < c.findings.Count; i++)
                    j.Append((i > 0 ? ",\n        " : "\n        ") + FindingJson(c.findings[i]));
                j.Append(c.findings.Count > 0 ? "\n      ]}" : "]}");
                j.Append(ci < ctxs.Count - 1 ? ",\n" : "\n");
            }
            j.Append("  ]\n}\n");
            return j.ToString();
        }

        private static string FindingJson(Finding f)
        {
            StringBuilder n = new StringBuilder("[");
            for (int i = 0; i < f.names.Count; i++) n.Append((i > 0 ? ", " : "") + S(f.names[i]));
            n.Append("]");
            return $"{{\"shape\": {S(f.shape)}, \"class\": {S(f.cls.ToString())}, \"underlying\": {S(f.underlying.ToString())}, " +
                   $"\"surfaceLocal\": {VJ(f.surfLocal)}, \"restLocal\": {VJ(f.restLocal)}, \"depth\": {F(f.depth)}, " +
                   $"\"startDepth\": {F(f.startDepth)}, \"drop\": {F(f.drop)}, \"maxMove\": {F(f.maxMove)}, \"footNormalY\": {F(f.footNy)}, " +
                   $"\"surfaceCollider\": {S(f.surfCollider ?? "")}, \"colliders\": {n}, " +
                   $"\"kind\": {S(KindNames[Mathf.Clamp(f.kind, 0, 2)])}, " +
                   $"\"startCollider\": {(f.startCollider == null ? "null" : S(f.startCollider))}, " +
                   $"\"wallCollider\": {(f.wallName == null ? "null" : S(f.wallName))}" +
                   (f.tag != null ? $", \"tag\": {S(f.tag)}" : "") + "}";
        }

        /// <summary>[컨트롤타워 FIN2 판정 2026-09-29] 격자·반칸 사전 겹침 이동 집계 → {"step":..,"stepMax":..,"heightTol":..,"Sphere":{"moved":{"grid","stagger","total"},"stay":{...}},...}.</summary>
        private static string GridPreJson(SweepCtx c)
        {
            StringBuilder g = new StringBuilder();
            g.Append($"{{\"step\": {F(WallBackoffStep)}, \"stepMax\": {F(WallBackoffMax)}, \"heightTol\": {F(GridShiftHeightTol)}");
            for (int s = 0; s < 3; s++)
            {
                int mg = c.gridPreMoved[0, s], ms = c.gridPreMoved[1, s], sg = c.gridPreStay[0, s], ss = c.gridPreStay[1, s];
                g.Append($", {S(ShapeNames[s])}: {{\"moved\": {{\"grid\": {mg}, \"stagger\": {ms}, \"total\": {mg + ms}}}, " +
                         $"\"stay\": {{\"grid\": {sg}, \"stagger\": {ss}, \"total\": {sg + ss}}}}}");
            }
            g.Append("}");
            return g.ToString();
        }

        private static string KindJson(int[] a) =>
            $"{{\"grid\": {a[0]}, \"stagger\": {a[1]}, \"wall\": {a[2]}}}";

        /// <summary>[M2R1] 도형별 int[3] → {"Sphere": a0, "Cube": a1, "Tetrahedron": a2}.</summary>
        private static string ShapeJson(int[] a) =>
            $"{{{S(ShapeNames[0])}: {a[0]}, {S(ShapeNames[1])}: {a[1]}, {S(ShapeNames[2])}: {a[2]}}}";

        private static string ResolveOutDir()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string mapRoot = Directory.GetParent(projectRoot).FullName;
            return Path.Combine(mapRoot, "검증");
        }
    }

    // ── 서식 ────────────────────────────────────────────────────────────────────────
    private static string F(float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) return "null";
        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }
    private static string V(Vector3 v) => $"({F(v.x)},{F(v.y)},{F(v.z)})";
    private static string VJ(Vector3 v) => $"[{F(v.x)}, {F(v.y)}, {F(v.z)}]";
    private static string B(bool b) => b ? "true" : "false";
    private static string S(string s)
    {
        StringBuilder sb = new StringBuilder("\"");
        foreach (char ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
#endif
