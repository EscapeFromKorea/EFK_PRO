// 이 파일은 반드시 "Editor" 폴더 안에 위치해야 한다.

using UnityEditor;
using UnityEngine;

/// <summary>
/// 낙석 커튼 커스텀 생성 창 — 낙석 개수/사이즈/주기를 조정해 생성한다.
///
/// ★ 이 창의 존재 이유는 "값을 받는 것"이 아니라 "받은 값으로 통과 창을 다시 계산해 보여주는 것"이다. ★
/// 개수나 사이즈를 건드리면 통로 길이 → 구의 통과 시간 → 필요한 빈 시간이 전부 따라 변한다.
/// 값만 받아 생성하면 사양 §2("감속 중에는 구가 통과 가능, 감속 없이는 불가")가 <b>에러 없이 조용히</b>
/// 깨진 커튼이 씬에 박힌다. 그래서 입력을 바꿀 때마다 통과 창을 실시간으로 재계산해 판정까지 띄운다.
///
/// 계산식은 새로 발명하지 않고 FallingRockSpawner 클래스 주석 (1)~(6)의 유도 과정을 그대로 옮겼다
/// (FallingRockPassWindow). 그 주석이 이 기믹 파라미터의 단일 근거이므로, 식을 고칠 일이 생기면
/// 주석과 이 계산을 함께 고쳐야 한다.
///
/// 생성 자체는 기존 메뉴(FallingRockMenuItem.CreateCurtain)에 위임한다 — Undo 등록, SlowZone
/// 자동 연결, 줄 배치 절차를 복제하지 않기 위함이다. 감속 구역은 여기서도 만들지 않는다.
/// </summary>
public class FallingRockCurtainWindow : EditorWindow
{
    // 기본값은 전부 FallingRockSpawner 컴포넌트 기본값과 같다(= Create Rock Curtain (4 lanes)
    // 프리셋과 같은 결과가 나오는 상태에서 시작해, 기획자가 바꾼 것만 달라지게 한다).
    private int laneCount = 4;
    private float rockSize = 1f;
    private float gapWidth = 2f;
    private float spawnInterval = 2f;
    private float spawnIntervalWhileSlowed = 5f;
    private float initialFallSpeed = 0f;
    private float despawnFallDistance = 8f;
    private float sphereSpeed = FallingRockPassWindow.SphereSpeedDefault;

    [MenuItem("Tools/FallingRock/Create Rock Curtain (Custom)...")]
    private static void Open()
    {
        FallingRockCurtainWindow win = GetWindow<FallingRockCurtainWindow>(false, "Falling Rock Curtain");
        win.minSize = new Vector2(430f, 560f);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("낙석 커튼 설정", EditorStyles.boldLabel);
        laneCount = Mathf.Max(1, EditorGUILayout.IntField(
            new GUIContent("낙석 줄 수", "전진 축(Z)으로 늘어놓을 스폰 지점 수. 줄이 늘어날수록 통로가 " +
                                        "길어져 구가 통과하는 데 걸리는 시간이 늘어난다."), laneCount));
        rockSize = Mathf.Max(0.05f, EditorGUILayout.FloatField(
            new GUIContent("낙석 크기 (U)", "낙석 한 변. 줄 간격과 '통로를 막는 시간' 양쪽에 들어간다."), rockSize));
        gapWidth = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("틈 폭 (U)", "구가 지나갈 틈. 줄 간격 = 낙석 크기 + 이 값."), gapWidth));
        spawnInterval = Mathf.Max(0.05f, EditorGUILayout.FloatField(
            new GUIContent("투하 주기 (초)", "감속이 없을 때. 이 값이 '감속 없이는 통과 불가'를 만든다."), spawnInterval));
        spawnIntervalWhileSlowed = Mathf.Max(0.05f, EditorGUILayout.FloatField(
            new GUIContent("감속 중 투하 주기 (초)", "이 값이 '감속 중에는 통과 가능'을 만든다."), spawnIntervalWhileSlowed));
        initialFallSpeed = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("초기 하강 속도 (U/s)", "스폰 순간 1회만 주는 속도. 올리면 감속 없는 낙하가 빨라진다."), initialFallSpeed));
        despawnFallDistance = Mathf.Max(0.1f, EditorGUILayout.FloatField(
            new GUIContent("소멸 낙하 거리 (U)", "감속 구역 바닥보다 아래여야 한다(구역 안에서 소멸하면 " +
                                                "SlowZone에 죽은 참조가 누적된다)."), despawnFallDistance));
        sphereSpeed = Mathf.Max(0.1f, EditorGUILayout.FloatField(
            new GUIContent("구 이동속도 (U/s)", "사양 §0 기준 7. 실제 도형 스탯(PlayerShapeStats)이 " +
                                                "조정되면 여기도 맞춰야 판정이 유효하다."), sphereSpeed));

        SlowZone zone = Object.FindObjectOfType<SlowZone>();
        Collider zoneCol = zone != null ? zone.GetComponent<Collider>() : null;

        // 낙하 높이 = 감속 구역 높이. 스폰은 구역 상단, 통과 통로는 구역 하단이라는 전제
        // (FallingRockSpawner 주석 (1))를 그대로 쓴다. 구역이 없으면 그 주석의 기준값 6 U로 가정한다.
        float slowMultiplier = zone != null ? zone.slowMultiplier : 0.5f;
        float maxFallSpeed = zone != null ? zone.maxFallSpeed : 2f;
        float fallHeight = zoneCol != null ? zoneCol.bounds.size.y : 6f;

        FallingRockPassWindow w = FallingRockPassWindow.Compute(
            laneCount, rockSize, gapWidth, spawnInterval, spawnIntervalWhileSlowed,
            initialFallSpeed, fallHeight, slowMultiplier, maxFallSpeed, sphereSpeed);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("통과 창 (실시간 계산)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"줄 간격 = {rockSize:F2} + {gapWidth:F2} = {w.spacing:F2} U\n" +
            $"통로 길이 = ({laneCount}-1) x {w.spacing:F2} + 진입/이탈 2 = {w.corridor:F2} U\n" +
            $"구 통과 시간 = {w.corridor:F2} / {sphereSpeed:F1} = {w.passTime:F2}초\n\n" +
            $"[감속 없음] 낙하 {fallHeight:F1} U에 {w.fallTimeNormal:F2}초, 도달 속도 {w.descentNormal:F2} U/s\n" +
            $"  낙석이 구의 몸 높이대를 막는 시간 = ({rockSize:F2}+{FallingRockPassWindow.SphereDiameter:F2}) / " +
            $"{w.descentNormal:F2} = {w.blockedNormal:F2}초\n" +
            $"  통로가 비는 시간 = {spawnInterval:F2} − {w.blockedNormal:F2} = {w.emptyNormal:F2}초\n\n" +
            $"[감속 중] 중력 x{slowMultiplier:F2}, 하강 상한 {maxFallSpeed:F1} U/s → 낙하 {w.fallTimeSlowed:F2}초, " +
            $"도달 속도 {w.descentSlowed:F2} U/s\n" +
            $"  막는 시간 = ({rockSize:F2}+{FallingRockPassWindow.SphereDiameter:F2}) / {w.descentSlowed:F2} = " +
            $"{w.blockedSlowed:F2}초\n" +
            $"  통로가 비는 시간 = {spawnIntervalWhileSlowed:F2} − {w.blockedSlowed:F2} = {w.emptySlowed:F2}초",
            MessageType.None);

        // 사양 §2 판정. 두 조건을 각각 보여줘야 "어느 쪽이 깨졌는지"를 알 수 있다 —
        // 성립/불성립 한 줄만 띄우면 기획자가 어느 값을 만져야 하는지 모른다.
        string verdict =
            (w.ImpossibleWithoutSlow
                ? $"O 감속 없이는 통과 불가 — 빈 시간 {w.emptyNormal:F2}초 < 통과 {w.passTime:F2}초"
                : $"X 감속 없이도 통과 가능 — 빈 시간 {w.emptyNormal:F2}초 ≥ 통과 {w.passTime:F2}초 " +
                  $"(여유 {w.emptyNormal - w.passTime:F2}초). 투하 주기를 {w.MaxIntervalForImpossible:F2}초 " +
                  "미만으로 낮추거나 초기 하강 속도를 올려라.") +
            "\n" +
            (w.PossibleWhileSlowed
                ? $"O 감속 중에는 통과 가능 — 빈 시간 {w.emptySlowed:F2}초 ≥ 통과 {w.passTime:F2}초 " +
                  $"(여유 {w.emptySlowed / Mathf.Max(0.01f, w.passTime):F1}배)"
                : $"X 감속 중에도 통과 불가 — 빈 시간 {w.emptySlowed:F2}초 < 통과 {w.passTime:F2}초. " +
                  $"감속 중 투하 주기를 {w.MinSlowedIntervalForPossible:F2}초 이상으로 올려라(낙하만 " +
                  "늦추면 막는 시간이 늘어 오히려 불리해진다 — 폴더 CLAUDE.md).");

        bool specHolds = w.ImpossibleWithoutSlow && w.PossibleWhileSlowed;
        EditorGUILayout.HelpBox(
            (specHolds ? "사양 §2 성립\n" : "사양 §2 불성립\n") + verdict,
            specHolds ? MessageType.Info : MessageType.Warning);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("씬 감속 구역", EditorStyles.boldLabel);
        if (zoneCol == null)
        {
            EditorGUILayout.HelpBox(
                "씬에 SlowZone이 없다. Tools > Hourglass로 모래시계 세트를 먼저 만들어라. " +
                "지금 계산은 SlowZone 기본값(중력 x0.5, 하강 상한 2 U/s, 높이 6 U)을 가정한 값이고, " +
                "생성해도 referenceSlowZone이 비어 감속 중 주기 전환이 동작하지 않는다.",
                MessageType.Warning);
        }
        else
        {
            // 아래 두 검사는 각각 기존 코드와 같은 판정을 재사용한다(문구가 갈라지면 창에서 정상으로
            // 본 세팅이 런타임 Validate에서 경고를 뱉는다).
            string zoneProblem = FallingRockPassWindow.ZoneLengthProblem(zoneCol.bounds, w.corridor, zone.name);
            float spawnY = zoneCol.bounds.max.y - rockSize * 0.5f; // 생성 코드와 같은 스폰 높이.
            string despawnProblem = FallingRockSpawner.DespawnInsideZoneProblem(
                spawnY, rockSize, despawnFallDistance, zoneCol.bounds);

            if (zoneProblem == null && despawnProblem == null)
                EditorGUILayout.HelpBox($"'{zone.name}' 정상 — Z {zoneCol.bounds.size.z:F1} U / 통로 " +
                                        $"{w.corridor:F1} U, 소멸 지점은 구역 바닥 아래.", MessageType.Info);
            if (zoneProblem != null) EditorGUILayout.HelpBox(zoneProblem, MessageType.Warning);
            if (despawnProblem != null) EditorGUILayout.HelpBox(despawnProblem, MessageType.Warning);
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Create Rock Curtain", GUILayout.Height(28f)))
            Create();
    }

    // 계산이 FallingRockSpawner 클래스 주석 (1)~(6)의 검산 숫자를 그대로 재현하는지 확인한다.
    // 식을 손대면 여기서 먼저 깨지므로, 주석과 코드가 조용히 갈라지는 것을 막는 유일한 안전장치다.
    [MenuItem("Tools/FallingRock/Self-Check Pass Window Math")]
    private static void SelfCheck()
    {
        FallingRockPassWindow w = FallingRockPassWindow.Compute(4, 1f, 2f, 2f, 5f, 0f, 6f, 0.5f, 2f,
                                                               FallingRockPassWindow.SphereSpeedDefault);
        string result =
            $"통로 {w.corridor:F2} (기대 11.00) / 통과 {w.passTime:F2} (1.57) / " +
            $"낙하 {w.fallTimeNormal:F2}·{w.fallTimeSlowed:F2} (1.11·3.20) / " +
            $"막는 시간 {w.blockedNormal:F2}·{w.blockedSlowed:F2} (0.18·1.00) / " +
            $"빈 시간 {w.emptyNormal:F2}·{w.emptySlowed:F2} (1.82·4.00)";

        bool ok = Mathf.Abs(w.corridor - 11f) < 0.01f
                  && Mathf.Abs(w.passTime - 1.57f) < 0.01f
                  && Mathf.Abs(w.fallTimeNormal - 1.11f) < 0.01f
                  && Mathf.Abs(w.fallTimeSlowed - 3.20f) < 0.01f
                  && Mathf.Abs(w.blockedNormal - 0.18f) < 0.01f
                  && Mathf.Abs(w.blockedSlowed - 1.00f) < 0.01f
                  && Mathf.Abs(w.emptyNormal - 1.82f) < 0.01f
                  && Mathf.Abs(w.emptySlowed - 4.00f) < 0.01f;

        if (ok) Debug.Log($"[FallingRock] 통과 창 계산 정상 — {result}");
        else Debug.LogError($"[FallingRock] 통과 창 계산이 FallingRockSpawner 주석 검산과 어긋난다 — {result}. " +
                            "식을 고쳤다면 주석도 함께 고쳐라(중력 설정이 -9.81이 아닐 때도 여기서 걸린다).");
    }

    private void Create()
    {
        FallingRockMenuItem.CreateCurtain(laneCount, spawner =>
        {
            spawner.rockSize = rockSize;
            spawner.gapWidth = gapWidth;
            spawner.spawnInterval = spawnInterval;
            spawner.spawnIntervalWhileSlowed = spawnIntervalWhileSlowed;
            spawner.initialFallSpeed = initialFallSpeed;
            spawner.despawnFallDistance = despawnFallDistance;
        });
    }
}

/// <summary>
/// 통과 창(pass window) 계산. 식은 전부 FallingRockSpawner 클래스 주석 (1)~(6)에서 그대로 가져왔다 —
/// 그 주석이 이 기믹 파라미터의 단일 근거이므로 여기서 식을 새로 만들지 않는다.
/// 에디터 전용이지만 값 자체는 런타임 물리와 같은 중력(Physics.gravity)을 쓴다.
/// </summary>
internal struct FallingRockPassWindow
{
    /// <summary>사양 §0 구 이동속도(U/s).</summary>
    public const float SphereSpeedDefault = 7f;

    /// <summary>사양 §0 구 지름(U). 낙석이 "구의 몸 높이대"를 막는 시간 계산에 들어간다(주석 (3)).</summary>
    public const float SphereDiameter = 1f;

    /// <summary>통로 진입 1 U + 이탈 1 U (주석 (2)).</summary>
    public const float EntryExitMargin = 2f;

    public float spacing;
    public float corridor;
    public float passTime;

    public float fallTimeNormal;
    public float descentNormal;
    public float blockedNormal;
    public float emptyNormal;

    public float fallTimeSlowed;
    public float descentSlowed;
    public float blockedSlowed;
    public float emptySlowed;

    /// <summary>감속 없이는 통과 불가(사양 §2 전반).</summary>
    public bool ImpossibleWithoutSlow => emptyNormal < passTime;

    /// <summary>감속 중에는 통과 가능(사양 §2 후반).</summary>
    public bool PossibleWhileSlowed => emptySlowed >= passTime;

    /// <summary>"감속 없이는 불가"가 성립하려면 투하 주기가 이 값 미만이어야 한다.</summary>
    public float MaxIntervalForImpossible => passTime + blockedNormal;

    /// <summary>"감속 중에는 가능"이 성립하려면 감속 중 주기가 이 값 이상이어야 한다.</summary>
    public float MinSlowedIntervalForPossible => passTime + blockedSlowed;

    public static float CorridorLength(int laneCount, float spacing)
    {
        return (Mathf.Max(1, laneCount) - 1) * spacing + EntryExitMargin;
    }

    public static FallingRockPassWindow Compute(int laneCount, float rockSize, float gapWidth,
                                                float spawnInterval, float spawnIntervalWhileSlowed,
                                                float initialFallSpeed, float fallHeight,
                                                float slowMultiplier, float maxFallSpeed,
                                                float sphereSpeed)
    {
        float g = Mathf.Abs(Physics.gravity.y); // 9.81 — 주석 (1)의 계산도 이 값을 썼다.

        FallingRockPassWindow w = new FallingRockPassWindow();
        w.spacing = rockSize + gapWidth;                                  // (2)
        w.corridor = CorridorLength(laneCount, w.spacing);                // (2)
        w.passTime = w.corridor / Mathf.Max(0.01f, sphereSpeed);          // (2)

        // (1) 낙하 시간과 통로 도달 속도. 감속 없음 = 그냥 자유낙하, 감속 중 = 중력 배율 + 하강 상한.
        Fall(fallHeight, g, initialFallSpeed, 0f, out w.fallTimeNormal, out w.descentNormal);
        Fall(fallHeight, g * Mathf.Max(0.01f, slowMultiplier), initialFallSpeed, maxFallSpeed,
             out w.fallTimeSlowed, out w.descentSlowed);

        // (3) 낙석 하나가 구의 몸 높이대를 막는 시간 = (낙석 + 구 지름) / 하강 속도.
        w.blockedNormal = (rockSize + SphereDiameter) / Mathf.Max(0.01f, w.descentNormal);
        w.blockedSlowed = (rockSize + SphereDiameter) / Mathf.Max(0.01f, w.descentSlowed);

        // (4)(6) 통로가 비어 있는 시간 = 투하 주기 − 막는 시간. 유량 보존 때문에 주기가 그대로면
        // 낙하를 늦출수록 이 값이 줄어든다 — 그래서 감속 중 주기를 따로 둔다((5)).
        w.emptyNormal = spawnInterval - w.blockedNormal;
        w.emptySlowed = spawnIntervalWhileSlowed - w.blockedSlowed;
        return w;
    }

    /// <summary>감속 구역의 전진 축(Z) 길이가 통로보다 짧으면 문제 문구, 정상이면 null.
    /// 커스텀 창(생성 전 미리보기)과 메뉴 생성 로그가 같은 문구를 쓴다.</summary>
    public static string ZoneLengthProblem(Bounds zoneBounds, float corridor, string zoneName)
    {
        if (zoneBounds.size.z + 0.01f >= corridor) return null;

        return $"감속 구역의 전진 축(Z) 길이가 {zoneBounds.size.z:F1} U로 낙석 통로 {corridor:F1} U보다 " +
               $"{corridor - zoneBounds.size.z:F1} U 짧다. Tools > Hourglass의 기본 6x6x6은 테스트 리그 " +
               $"규격이다 — '{zoneName}'의 BoxCollider size Z를 {corridor:F1} 이상으로 늘리고 반투명 시각" +
               "(Visual_SlowZone) 스케일도 같이 맞춰라. 여기서 자동으로 늘리지 않는 이유는 다른 기믹의 " +
               "오브젝트라서다. 구역 밖에서 떨어지는 줄은 감속되지 않아 통과 창이 열리지 않는다.";
    }

    // 등가속 낙하 + 속도 상한(cap, 0이면 없음)에서 낙하 시간과 도달 속도를 구한다.
    // 상한이 걸리는 구간은 등속이므로 두 구간으로 나눠 더한다(SlowZone이 매 스텝 clamp하는 동작과 같다).
    private static void Fall(float height, float g, float v0, float cap, out float time, out float endSpeed)
    {
        if (height <= 0f || g <= 0f)
        {
            time = 0f;
            endSpeed = Mathf.Max(v0, 0.01f);
            return;
        }

        if (cap > 0f && v0 >= cap) // 진입 순간 이미 상한 이상 → 전 구간 등속.
        {
            endSpeed = cap;
            time = height / cap;
            return;
        }

        float free = Mathf.Sqrt(v0 * v0 + 2f * g * height); // 상한이 없을 때의 도달 속도.
        if (cap <= 0f || free <= cap)
        {
            endSpeed = free;
            time = (free - v0) / g;
            return;
        }

        float distanceToCap = (cap * cap - v0 * v0) / (2f * g);
        endSpeed = cap;
        time = (cap - v0) / g + (height - distanceToCap) / cap;
    }
}
