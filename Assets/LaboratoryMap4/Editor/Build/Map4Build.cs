#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F1-3 겹침·끼임 방지 빌더 헬퍼. 모든 좌표는 섹터 로컬(parent 기준), 상자는 min/max로 받는다.
/// 이름 접두사는 팀 Map3 관례(CP_, DOOR_, TRG_, RESET_, GEO_, VIS_)를 따른다.
/// </summary>
public static class Map4Build
{
    /// <summary>[F1 재작업 판정 결정5] LD-01 원문(PlayerSystem_Requirement.md:66, map-reviewer
    /// 캐시 #40 직독)은 "필수 통과 경로의 틈 2.0U 이하, 턱 1.1U 이하(네모 한계 2.08U/1.20U에서
    /// 여유 차감)"다 — 이건 **단일 턱 점프 한계**(절대 상한, 이걸 넘는 단은 통과 자체가
    /// 불가하므로 무조건 생성 거부)이지 계단 규격이 아니다. 계단 챌면의 "정상" 목표는 공통규칙
    /// 필수 경로 단차 1.0U — 1.0~1.1U 사이는 "허용되지만 권장 아님"(경고), 1.1U 초과는 거부.
    /// 공통규칙의 0.8U(맵2 잠금 🔒N4)는 맵2 한정으로 본다(결정5 채택).</summary>
    public const float LD01_MaxLedgeRise = 1.1f;
    /// <summary>계단 챌면(단높이) 권장 상한 — 공통규칙 "필수 경로 단차 1.0U". 이 값~LD01_MaxLedgeRise
    /// 사이는 경고만(거부 아님).</summary>
    public const float LedgeRecommendedMaxRise = 1.0f;
    /// <summary>계단 디딤 깊이(챌면 사이 수평 발판) 최소값 — 결정5 "디딤 깊이 ≥ 1.5U".</summary>
    public const float LedgeMinTreadDepth = 1.5f;
    /// <summary>[F1 재작업 판정 결정5] 걷는 경사로(Ramp)의 최대 경사각 — 팀 PlayerMover.
    /// uphillLimitAngle(30°, PlayerMover.cs:174 — 이 각도부터 유지 속도가 0/음수가 돼 밀려
    /// 내려온다)에서 여유 5°를 뺀 값.</summary>
    public const float RampMaxAngleDeg = 25f;
    /// <summary>도형 최소 폭(끼임 틈·통로 폭 판정 공용) — 팀 콜라이더 실측: Cube BoxCollider
    /// size=Vector3.one(1U), Sphere SphereCollider radius=0.5(지름 1U), Tetrahedron 챔퍼드
    /// 콜라이더 반경 0.5 기준(1U 안팎) — 전부 약 1U. [실좌표] PlayerObjectMenuItem.cs:238-284.</summary>
    public const float ShapeMinPassageWidth = 1.0f;

    private struct BoxRecord { public Vector3 min, max; public string name; }
    private static readonly List<BoxRecord> floorRecords = new List<BoxRecord>();

    /// <summary>섹터 하나를 새로 짓기 전에 호출 — 바닥 겹침 검사 범위를 그 섹터로 한정한다.</summary>
    public static void BeginSection() => floorRecords.Clear();

    // ── 재질 ─────────────────────────────────────────────────────────────
    private static Map4Palette paletteCache;

    private static Map4Palette GetPalette()
    {
        if (paletteCache != null) return paletteCache;
        const string path = "Assets/LaboratoryMap4/Materials/Map4Palette.asset";
        paletteCache = AssetDatabase.LoadAssetAtPath<Map4Palette>(path);
        if (paletteCache == null)
        {
            EnsureDir("Assets/LaboratoryMap4/Materials");
            paletteCache = ScriptableObject.CreateInstance<Map4Palette>();
            AssetDatabase.CreateAsset(paletteCache, path);
        }
        return paletteCache;
    }

    /// <summary>Standard 셰이더 머티리얼을 팔레트 항목 이름으로 찍어낸다(F1-3 — "머티리얼은
    /// Standard 셰이더로 코드 생성해 Materials/Generated/에 저장"). 손으로 만든 머티리얼 아님.</summary>
    public static Material GetMaterial(string paletteName)
    {
        Map4Palette palette = GetPalette();
        Map4Palette.Entry entry = palette.Get(paletteName);
        if (entry == null && palette.entries.Length > 0) entry = palette.entries[0];
        if (entry == null)
        {
            Debug.LogError("[Map4Build] 팔레트가 비어 있다 — 기본 흰색 머티리얼로 대체한다.");
            return new Material(Shader.Find("Standard"));
        }

        const string dir = "Assets/LaboratoryMap4/Materials/Generated";
        EnsureDir(dir);
        string path = $"{dir}/Map4_{entry.paletteName}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = entry.color;
        mat.SetFloat("_Glossiness", entry.smoothness);
        mat.SetFloat("_Metallic", entry.metallic);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ── 바닥/벽 ──────────────────────────────────────────────────────────

    /// <summary>연속 구간당 콜라이더 1개. 같은 높이(Y 범위 겹침) + 같은 XZ 영역이 이미 있으면
    /// 생성을 거부하고 에러를 남긴다(F1-3 — "같은 높이 바닥이 겹치면 생성 거부").</summary>
    public static Transform Floor(Transform parent, Vector3 min, Vector3 max, string paletteName = "Floor")
    {
        foreach (BoxRecord r in floorRecords)
        {
            bool yOverlap = min.y < r.max.y && max.y > r.min.y;
            bool xOverlap = min.x < r.max.x && max.x > r.min.x;
            bool zOverlap = min.z < r.max.z && max.z > r.min.z;
            if (yOverlap && xOverlap && zOverlap)
            {
                Debug.LogError($"[Map4Build] Floor 생성 거부 — 같은 높이 바닥이 겹친다: 새 박스 " +
                                $"{min}~{max} vs 기존 '{r.name}' {r.min}~{r.max}");
                return null;
            }
        }
        Transform t = CreateSolidBox(parent, "GEO_Floor", min, max, GetMaterial(paletteName));
        if (t != null) floorRecords.Add(new BoxRecord { min = min, max = max, name = t.name });
        return t;
    }

    /// <summary>한 벽면 = 콜라이더 1개(F1-3).</summary>
    public static Transform Wall(Transform parent, Vector3 min, Vector3 max, string paletteName = "Wall")
        => CreateSolidBox(parent, "GEO_Wall", min, max, GetMaterial(paletteName));

    /// <summary>문/통로 구멍이 있는 벽 — 구멍 목록으로 잘라서 조각끼리 정확히 맞닿게 한다(틈 0,
    /// 겹침 0, F1-3). openings는 벽의 "긴 축"(X 또는 Z, 더 긴 쪽) 위 (start,end) 절대좌표
    /// 목록이다. 구멍 범위가 벽을 벗어나거나 서로 겹치면 아무것도 만들지 않고 에러만 남긴다.</summary>
    public static Transform[] WallWithOpenings(Transform parent, Vector3 min, Vector3 max,
        List<Vector2> openings, string paletteName = "Wall")
    {
        bool alongX = (max.x - min.x) >= (max.z - min.z);
        float lo = alongX ? min.x : min.z;
        float hi = alongX ? max.x : max.z;

        List<Vector2> sorted = new List<Vector2>(openings);
        sorted.Sort((a, b) => a.x.CompareTo(b.x));
        for (int i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].x < lo || sorted[i].y > hi || sorted[i].x >= sorted[i].y)
            {
                Debug.LogError($"[Map4Build] WallWithOpenings — 구멍 범위가 벽 범위({lo}~{hi})를 " +
                                $"벗어나거나 역전됐다: {sorted[i]}");
                return null;
            }
            if (i > 0 && sorted[i].x < sorted[i - 1].y)
            {
                Debug.LogError($"[Map4Build] WallWithOpenings — 구멍끼리 겹친다: {sorted[i - 1]}, {sorted[i]}");
                return null;
            }
            // [F1 재작업 판정 C7] 구멍 폭이 도형 최소 폭보다 좁으면 만들 수는 있지만(문/틈새는
            // 디자인 의도일 수 있다) 통과 불가 또는 끼임 위험이라 경고한다 — CheckGap과 같은
            // 기준(ShapeMinPassageWidth)을 구멍에도 적용.
            float openingWidth = sorted[i].y - sorted[i].x;
            if (openingWidth < ShapeMinPassageWidth)
                Debug.LogWarning($"[Map4Build] WallWithOpenings — 구멍 폭 {openingWidth:F2}U가 도형 " +
                                  $"최소 폭 {ShapeMinPassageWidth}U보다 좁다({sorted[i]}) — 통과 불가/끼임 위험.");
        }

        List<Transform> pieces = new List<Transform>();
        float cursor = lo;
        foreach (Vector2 op in sorted)
        {
            if (op.x > cursor) pieces.Add(MakeWallSegment(parent, min, max, alongX, cursor, op.x, paletteName));
            cursor = op.y;
        }
        if (cursor < hi) pieces.Add(MakeWallSegment(parent, min, max, alongX, cursor, hi, paletteName));

        return pieces.ToArray();
    }

    private static Transform MakeWallSegment(Transform parent, Vector3 min, Vector3 max, bool alongX,
        float segLo, float segHi, string paletteName)
    {
        Vector3 segMin = min, segMax = max;
        if (alongX) { segMin.x = segLo; segMax.x = segHi; }
        else { segMin.z = segLo; segMax.z = segHi; }
        return CreateSolidBox(parent, "GEO_Wall", segMin, segMax, GetMaterial(paletteName));
    }

    // ── 문 ──────────────────────────────────────────────────────────────

    /// <summary>팀 doorPhysics + kinematic Rigidbody(09-22 결함: RB 없으면 doorPhysics.Awake가
    /// doorRigidbody를 null로 남겨 매 FixedUpdate NullReferenceException — F1-3 명세) + 문틀.
    ///
    /// [검토 요청, 2차 반려 A 항목 — 주석 정리] 팀 doorPhysics.OnTriggerEnter/Exit(isBlocked
    /// 판정)는 트리거 콜라이더가 있어야 발화하는데, 물리적으로 막아야 하는 문 콜라이더는
    /// isTrigger=false다. 같은 GameObject에 트리거 콜라이더를 하나 더 얹어(팀 코드 무수정)
    /// isBlocked 신호를 받게 했다 — "콜라이더 하나 추가"라는 구조적 개입이라 map-reviewer 확인이
    /// 필요하다(트리거 크기 자체는 C6 대로 팀 비율로 보정, 아래 참고 — "같은 크기"가 아니다).</summary>
    public static doorPhysics Door(Transform parent, Vector3 min, Vector3 max, float openHeight,
        string paletteName = "Door")
    {
        Transform doorT = CreateSolidBox(parent, "DOOR_Door", min, max, GetMaterial(paletteName));
        if (doorT == null) return null;
        GameObject go = doorT.gameObject;
        Vector3 size = max - min;
        Vector3 center = (min + max) * 0.5f;

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        doorPhysics dp = go.AddComponent<doorPhysics>();
        dp.doorTargetYOffset = openHeight;

        // [F1 재작업 판정 C6] 팀 원본 구성은 "솔리드 + 살짝 큰 트리거"다 — 트리거가 솔리드와 정확히
        // 같은 크기면 플레이어 콜라이더 중심이 딱 경계에 걸칠 때만 겹침이 잡혀 isBlocked가
        // 불안정하다. 팀 DoorSystemMenuItem.cs:28(DoorBlockTriggerSize)·145-149(주석) 그대로의
        // 비율(가로 1.05×·세로 1.02×·두께 1.6×, 문틀 두께 축에 가장 큰 여유를 준다)을 우리 문
        // 크기에 곱한다 — 팀 원본은 고정 스케일 문 하나에 대한 절대값이라, 크기가 다른 우리 문에는
        // 비율로 이식하는 것이 맞는 번역이라고 판단했다(검토 요청 — 비율 적용이 맞는지 확인 필요).
        BoxCollider trigger = go.AddComponent<BoxCollider>();
        trigger.size = new Vector3(size.x * 1.05f, size.y * 1.02f, size.z * 1.6f);
        trigger.isTrigger = true;

        // [F1 재작업 판정 C7] 문틀(VIS_NoCollide_ — 콜라이더 없는 큰 시각물, F1-3 명명 규칙)은
        // 움직이는 문(doorT, Rigidbody.MovePosition으로 올라감)이 아니라 **고정된 parent**에
        // 매단다 — 이전 버전은 문의 자식이라 문이 열릴 때 문틀까지 같이 떠올랐다(구조적 오류).
        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.name = "VIS_NoCollide_DoorFrame";
        Object.DestroyImmediate(frame.GetComponent<Collider>()); // 문틀은 순수 시각(콜라이더 없음).
        frame.transform.SetParent(parent, false);
        frame.transform.localPosition = center + new Vector3(0f, size.y / 2f + 0.1f, 0f);
        frame.transform.localScale = new Vector3(size.x + 0.2f, 0.2f, size.z + 0.2f);
        frame.GetComponent<MeshRenderer>().sharedMaterial = GetMaterial(paletteName);

        // [F1 재작업 판정 C7, 2차 반려 A 항목 — 문 방향 고려] "열린 상태에서 통로 폭이 도형 통과
        // 규격 이상인지 검사"(F1-3 명세) — 이전 버전은 항상 size.x만 봐서, 문이 X축이 아니라
        // Z축을 따라 넓은 방향으로 배치되면(예: 남북 방향 복도의 동서벽에 낸 문) 잘못된 축을
        // 검사했다. 문틀은 두께 축(얇은 쪽)과 폭 축(넓은 쪽)이 있고 통로 폭은 항상 더 넓은
        // 수평 축이므로 Mathf.Max(size.x,size.z)를 쓴다.
        CheckPassageWidth(Mathf.Max(size.x, size.z), ShapeMinPassageWidth, $"Door({go.name}) 열린 폭");

        return dp;
    }

    /// <summary>열린 상태 통로 폭이 도형 통과 규격 이상인지 검사(F1-3 — "열린 상태에서 통로 폭이
    /// 도형 통과 규격 이상인지 검사"). 미달이면 에러만 남기고 문 자체는 이미 만들어진 대로 둔다
    /// (호출자가 치수를 다시 정해야 함을 알리는 용도).</summary>
    public static void CheckPassageWidth(float availableWidth, float requiredWidth, string context)
    {
        if (availableWidth < requiredWidth)
            Debug.LogError($"[Map4Build] 통로 폭 부족 — {context}: 열린 폭 {availableWidth:F2}U < " +
                            $"필요 {requiredWidth:F2}U.");
    }

    // ── 계단(점프 턱/Ledge)/경사로(Ramp) ──────────────────────────────────
    // [F1 재작업 판정 결정5] 두 헬퍼로 나뉜다 — Stairs는 "점프 턱"(챌면 각각을 점프로 넘는 단),
    // Ramp는 "걷는 경사로"(연속 경사, 걸어서 오른다). 용도가 다르므로 규격도 다르다.

    /// <summary>점프 턱(Ledge/Stairs). 단높이가 LD01_MaxLedgeRise(1.1U, 하드 상한)를 넘으면 에러
    /// 로그 후 생성 중단. LedgeRecommendedMaxRise(1.0U)를 넘지만 하드 상한 이내면 경고만(허용).
    /// 디딤 깊이(stepRun)가 LedgeMinTreadDepth(1.5U) 미만이면 생성 중단(결정5). baseMin은 parent
    /// 기준 로컬 좌표(계단 그룹의 원점 = 첫 단 시작 모서리).</summary>
    public static Transform Stairs(Transform parent, Vector3 baseMin, float totalRise, float totalRun,
        int stepCount, float widthZ, string paletteName = "Floor")
    {
        if (stepCount <= 0)
        {
            Debug.LogError("[Map4Build] Stairs 생성 중단 — stepCount가 0 이하다.");
            return null;
        }

        float stepRise = totalRise / stepCount;
        if (stepRise > LD01_MaxLedgeRise)
        {
            Debug.LogError($"[Map4Build] Stairs 생성 중단 — 단높이 {stepRise:F3}U가 LD-01 하드 상한 " +
                            $"{LD01_MaxLedgeRise}U를 초과한다(요청: 총상승 {totalRise}U ÷ {stepCount}단).");
            return null;
        }
        if (stepRise > LedgeRecommendedMaxRise)
            Debug.LogWarning($"[Map4Build] Stairs — 단높이 {stepRise:F3}U가 권장 상한 " +
                              $"{LedgeRecommendedMaxRise}U를 넘는다(LD-01 하드 상한 {LD01_MaxLedgeRise}U " +
                              "이내라 생성은 허용).");

        float stepRun = totalRun / stepCount;
        if (stepRun < LedgeMinTreadDepth)
        {
            Debug.LogError($"[Map4Build] Stairs 생성 중단 — 디딤 깊이 {stepRun:F3}U가 최소" +
                            $"{LedgeMinTreadDepth}U 미만이다(요청: 총수평 {totalRun}U ÷ {stepCount}단).");
            return null;
        }

        float angleDeg = Mathf.Atan2(stepRise, stepRun) * Mathf.Rad2Deg;

        GameObject group = new GameObject("GEO_Stairs");
        group.transform.SetParent(parent, false);
        group.transform.localPosition = baseMin;

        for (int i = 0; i < stepCount; i++)
        {
            Vector3 stepMin = new Vector3(i * stepRun, 0f, 0f);
            Vector3 stepMax = new Vector3((i + 1) * stepRun, (i + 1) * stepRise, widthZ);
            CreateSolidBox(group.transform, $"GEO_Step_{i}", stepMin, stepMax, GetMaterial(paletteName));
        }

        Debug.Log($"[Map4Build] Stairs 생성 — 단수 {stepCount}, 단높이 {stepRise:F3}U, 디딤 깊이 " +
                  $"{stepRun:F3}U, 경사각 {angleDeg:F1}도.");
        return group.transform;
    }

    /// <summary>경사로 슬래브 두께(Unit) — Ramp()와 그 단위 검사가 공유한다.</summary>
    public const float RampThickness = 0.6f;

    /// <summary>걷는 경사로(Ramp). 경사각이 RampMaxAngleDeg(25°)를 넘으면 에러 로그 후 생성 중단
    /// (결정5). baseMin은 parent 기준 로컬 좌표 — 경사로 바닥(오르기 전) 쪽 왼쪽 모서리
    /// (x=baseMin.x, y=baseMin.y, z=baseMin.z가 시작점). width는 X축 폭, rise는 상승 높이(Y),
    /// run은 수평 진행 거리(Z) — 경사로는 로컬 +Z 방향으로 올라간다.
    /// startSurfaceY·endSurfaceY로 실제 걸어 다니는 윗면의 시작·끝 높이(parent 로컬 Y)를
    /// 돌려준다 — 호출자가 앞뒤로 이어지는 바닥·통로와 턱 0으로 맞았는지 검사할 수 있게 한다
    /// (2차 반려 N1 대응).</summary>
    public static Transform Ramp(Transform parent, Vector3 baseMin, float width, float rise, float run,
        out float startSurfaceY, out float endSurfaceY, string paletteName = "Floor")
    {
        startSurfaceY = baseMin.y;
        endSurfaceY = baseMin.y + rise;

        if (run <= 0f || Mathf.Abs(rise) < 0.001f)
        {
            Debug.LogError($"[Map4Build] Ramp 생성 중단 — run({run})은 0보다 커야 하고 rise({rise})는 " +
                            "0이 아니어야 한다.");
            return null;
        }

        float angleDeg = Mathf.Atan2(Mathf.Abs(rise), run) * Mathf.Rad2Deg;
        if (angleDeg > RampMaxAngleDeg)
        {
            Debug.LogError($"[Map4Build] Ramp 생성 중단 — 경사각 {angleDeg:F1}도가 한도 " +
                            $"{RampMaxAngleDeg}도를 초과한다(상승 {rise}U / 수평거리 {run}U). 더 완만하게 " +
                            "만들려면 run을 늘려라.");
            return null;
        }

        // [2차 반려 N1 재수정 — 근본 원인] 이전 버전(1차 수정)은 폭×두께×slopeLength 크기의
        // BoxCollider를 (bottom,top) 방향으로 회전시켜 윗면만 선분에 맞췄다. 단위 검사
        // (TestRampEndHeights, 고립된 Ramp 하나만 봄)와 Generate 로그는 윗면 높이가 정확하다고
        // 확인했지만, 실제 섹터 감사(Map4Batch.Audit)는 S4·S6에서 TEMP_Ramp↔GEO_Floor(통로) 관통
        // 0.114U/0.116U를 그대로 재현했다 — 회전된 두꺼운 박스는 기울기 때문에 "아랫면"이 시작·끝
        // seam을 필연적으로 두께×sinθ(S4 실측 0.227U)만큼 넘어서 튀어나오고, 그 자리에 이미 앞뒤
        // 바닥/통로 박스가 있어 겹친다 — 임의의 두께·각도를 가진 회전 박스로는 "윗면 turn0"과
        // "이웃과 겹침0"을 동시에 만족시킬 수 없다(기하학적으로 불가능, 단위 검사가 놓친 이유는
        // 이웃 바닥/통로와의 상호작용을 안 보기 때문). 진짜 해결책: 회전 대신 앞뒤 끝면이 월드
        // Y축에 평행한 수직면인 "쐐기"(wedge, 볼록 6면체)를 직접 만든다 — Z 범위가 정확히
        // [baseMin.z, baseMin.z+run]으로 고정되어 오버행이 0이 되고, 윗면은 여전히 (bottom,top)
        // 선분을 정확히 지난다. Physics.ComputePenetration(Map4Audit이 쓴다)이 볼록 형상만
        // 지원하므로 MeshCollider.convex=true로 만든다(PhysX가 정점 집합의 볼록 껍질을 계산해
        // 삼각형 감김 방향은 충돌 계산에 영향 없음 — 렌더링에만 영향).
        float x0 = baseMin.x, x1 = baseMin.x + width;
        float z0 = baseMin.z, z1 = baseMin.z + run;
        float y0 = baseMin.y, y1 = baseMin.y + rise;
        float yb0 = y0 - RampThickness, yb1 = y1 - RampThickness;

        // 코너 8개는 parent 로컬 좌표로 직접 계산한다 — GameObject 자체는 회전·이동 없이(로컬
        // position=0, rotation=identity) parent 바로 아래 둔다. 그러면 go.transform == parent
        // 좌표계와 동일해, TestRampEndHeights 등에서 go.transform.TransformPoint(정점)이 그대로
        // 올바른 월드 좌표를 준다.
        Vector3 c0 = new Vector3(x0, y0, z0);   // 윗면-시작-좌
        Vector3 c1 = new Vector3(x1, y0, z0);   // 윗면-시작-우
        Vector3 c2 = new Vector3(x1, y1, z1);   // 윗면-끝-우
        Vector3 c3 = new Vector3(x0, y1, z1);   // 윗면-끝-좌
        Vector3 c4 = new Vector3(x0, yb0, z0);  // 아랫면-시작-좌
        Vector3 c5 = new Vector3(x1, yb0, z0);  // 아랫면-시작-우
        Vector3 c6 = new Vector3(x1, yb1, z1);  // 아랫면-끝-우
        Vector3 c7 = new Vector3(x0, yb1, z1);  // 아랫면-끝-좌

        // [3차 반려 A — 면별 정점 분리] 코너 8개를 6면이 공유하면 RecalculateNormals가 인접면의
        // 법선을 정점마다 평균 내(스무스 셰이딩) 쐐기의 평평한 면들이 경사면처럼 뭉개져 보인다.
        // 면마다 정점을 4개씩 새로 만들어(합계 24개) 어느 정점도 두 면에서 공유되지 않게 하면
        // RecalculateNormals가 면마다 독립된 평평한 법선을 낸다(플랫 셰이딩, 물리에는 영향 없음 —
        // MeshCollider convex=true는 정점 위치들의 볼록 껍질만 본다). 각 면은 이전 버전과 정확히
        // 같은 코너 순서(0,1,2,3 상대 인덱스로 삼각형 0,2,1 + 0,3,2)를 유지해 감김 방향이
        // 그대로다. 앞 4개(윗면)는 이전 8-정점판의 verts[0]·verts[2]와 여전히 같은 코너
        // (top-start-left·top-end-right)를 가리켜, TestRampEndHeights·Map4SceneBuilder의
        // 결정11 검사가 그대로 유효하다.
        Vector3[] verts =
        {
            c0, c1, c2, c3, // 0~3   윗면(걷는 면)
            c4, c5, c6, c7, // 4~7   아랫면
            c0, c1, c5, c4, // 8~11  시작 끝면(수직, z=z0 평면 — 앞뒤 바닥과 맞물린다)
            c2, c3, c7, c6, // 12~15 끝 끝면(수직, z=z1 평면 — 통로와 맞물린다)
            c3, c0, c4, c7, // 16~19 왼쪽 옆면(x=x0)
            c1, c2, c6, c5, // 20~23 오른쪽 옆면(x=x1)
        };

        int[] tris =
        {
            0,2,1, 0,3,2,       // 윗면
            4,5,6, 4,6,7,       // 아랫면
            8,9,10, 8,10,11,    // 시작 끝면
            12,13,14, 12,14,15, // 끝 끝면
            16,17,18, 16,18,19, // 왼쪽 옆면
            20,21,22, 20,22,23, // 오른쪽 옆면
        };

        Mesh mesh = new Mesh { name = "GEO_Ramp_Wedge" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject go = new GameObject("GEO_Ramp");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        MeshCollider col = go.AddComponent<MeshCollider>();
        col.sharedMesh = mesh;
        col.convex = true;

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = GetMaterial(paletteName);

        startSurfaceY = y0;
        endSurfaceY = y1;
        Debug.Log($"[Map4Build] Ramp 생성(쐐기 메시, N1 재수정) — 상승 {rise:F2}U / 수평 {run:F2}U, " +
                  $"경사각 {angleDeg:F1}도, 윗면 시작Y={startSurfaceY:F4} 끝Y={endSurfaceY:F4}, " +
                  $"Z범위 [{z0:F3},{z1:F3}](오버행 0, 볼록 메시 콜라이더).");
        return go.transform;
    }

    /// <summary>startSurfaceY/endSurfaceY가 필요 없는 호출부용 편의 오버로드.</summary>
    public static Transform Ramp(Transform parent, Vector3 baseMin, float width, float rise, float run,
        string paletteName = "Floor")
        => Ramp(parent, baseMin, width, rise, run, out _, out _, paletteName);

    // ── 끼임 틈 검사 ─────────────────────────────────────────────────────

    /// <summary>두 상자 사이 틈이 0보다 크고 도형 최소 폭(1.0U) 미만이면 경고(F1-3 — "끼임 틈
    /// 금지: 0 < 틈 < 1.0U를 만들면 경고").</summary>
    public static void CheckGap(Vector3 minA, Vector3 maxA, Vector3 minB, Vector3 maxB, string labelA, string labelB)
    {
        float gapX = Mathf.Max(minB.x - maxA.x, minA.x - maxB.x);
        float gapY = Mathf.Max(minB.y - maxA.y, minA.y - maxB.y);
        float gapZ = Mathf.Max(minB.z - maxA.z, minA.z - maxB.z);
        float gap = Mathf.Max(gapX, gapY, gapZ);
        if (gap > 0f && gap < 1.0f)
            Debug.LogError($"[Map4Build] 끼임 틈 위험 — '{labelA}'와 '{labelB}' 사이 {gap:F3}U " +
                            "(0 초과 1.0U 미만은 도형 최소 폭보다 좁아 끼일 수 있다).");
    }

    // ── 공통 ────────────────────────────────────────────────────────────

    private static Transform CreateSolidBox(Transform parent, string namePrefix, Vector3 min, Vector3 max, Material mat)
    {
        Vector3 size = max - min;
        Vector3 center = (min + max) * 0.5f;
        if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
        {
            Debug.LogError($"[Map4Build] {namePrefix} 크기가 0 이하다: min={min} max={max}");
            return null;
        }

        GameObject go = new GameObject(namePrefix);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;

        BoxCollider col = go.AddComponent<BoxCollider>();
        col.size = size;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual";
        Object.DestroyImmediate(visual.GetComponent<Collider>()); // "보이는 결 1개 = 콜라이더 1개".
        visual.transform.SetParent(go.transform, false);
        visual.transform.localScale = size;
        visual.GetComponent<MeshRenderer>().sharedMaterial = mat;

        return go.transform;
    }

    private static void EnsureDir(string path)
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
#endif
