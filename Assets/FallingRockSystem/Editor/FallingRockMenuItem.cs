// 이 파일은 반드시 "Editor" 폴더 안에 위치해야 한다.

using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools &gt; FallingRock 메뉴. 낙석 커튼(스포너 + 스폰 지점)을 씬에 한 번에 생성하고,
/// 배치가 사양대로 맞물렸는지 검증한다.
///
/// ★ 감속 구역(SlowZone)은 여기서 만들지 않는다. ★ 생성 코드를 복제하면 HourglassSystem과
/// 기본값이 갈라져 "낙석은 감속되는데 모래시계 쪽 값과 다르다"류 사고가 난다. 씬에 이미 있는
/// SlowZone을 찾아 참조만 연결하고, 없으면 Tools &gt; Hourglass를 먼저 실행하라고 안내한다.
///
/// 개수·사이즈를 조정해 만들려면 커스텀 창(FallingRockCurtainWindow)을 쓴다. 창도 아래
/// CreateCurtain을 호출한다 — 생성 절차(Undo 등록, SlowZone 자동 연결, 줄 배치, 검증)를
/// 복제하면 한쪽만 고쳐지는 사고가 나므로 진입점은 둘이지만 본체는 하나다.
/// </summary>
public static class FallingRockMenuItem
{
    [MenuItem("Tools/FallingRock/Create Rock Curtain (4 lanes)")]
    private static void CreateCurtainPreset()
    {
        // 회귀 방지용 기본값 프리셋 — 스포너 컴포넌트 기본값을 하나도 덮지 않는다(configure = null).
        CreateCurtain(4, null);
    }

    /// <summary>커튼 생성 본체. laneCount = 줄 수, configure = 스포너 기본값을 덮을 설정
    /// (커스텀 창이 넘긴 사이즈·주기 등. null이면 컴포넌트 기본값 그대로).
    /// 줄 간격·통로 길이는 configure가 반영된 스포너 값에서 다시 계산한다.</summary>
    internal static GameObject CreateCurtain(int laneCount, System.Action<FallingRockSpawner> configure)
    {
        laneCount = Mathf.Max(1, laneCount);

        GameObject root = new GameObject("FallingRock_Curtain");
        Undo.RegisterCreatedObjectUndo(root, "Create Rock Curtain");
        FallingRockSpawner spawner = root.AddComponent<FallingRockSpawner>();
        configure?.Invoke(spawner);

        float spacing = spawner.rockSize + spawner.gapWidth; // 기본 3 U.
                                                             // 통로 길이 = (줄-1)x간격 + 진입/이탈 2 U.

        SlowZone zone = Object.FindObjectOfType<SlowZone>();
        Collider zoneCol = zone != null ? zone.GetComponent<Collider>() : null;

        if (zoneCol != null)
        {
            spawner.referenceSlowZone = zone;
            Bounds b = zoneCol.bounds;
            // 낙석 몸통이 처음부터 구역 안에 들어가게 상단에서 반 칸 내려 스폰한다(경계에 걸치면
            // 감속이 첫 프레임에 안 걸리거나 검증이 경계 오차로 흔들린다).
            root.transform.position = new Vector3(b.center.x, b.max.y - spawner.rockSize * 0.5f, b.center.z);
        }
        else
        {
            root.transform.position = SceneView.lastActiveSceneView != null
                ? SceneView.lastActiveSceneView.pivot
                : Vector3.zero;
        }

        // 전진 축(Z)으로 줄지어 배치한다 - §0.1의 옆모습 2D 횡스크롤 기준.
        Transform[] lanes = new Transform[laneCount];
        float first = -(laneCount - 1) * spacing * 0.5f;
        for (int i = 0; i < laneCount; i++)
        {
            GameObject lane = new GameObject($"Lane_{i + 1}");
            lane.transform.SetParent(root.transform, false);
            lane.transform.localPosition = new Vector3(0f, 0f, first + i * spacing);
            lanes[i] = lane.transform;
        }
        spawner.spawnPositions = lanes;

        EditorUtility.SetDirty(spawner);
        Selection.activeGameObject = root;

        // 통로 길이 식은 커스텀 창의 통과 창 계산과 반드시 같아야 한다(창에서 본 판정과 실제
        // 생성물이 어긋나면 아무 의미가 없다) — 식은 FallingRockPassWindow 한 곳에만 둔다.
        float corridor = FallingRockPassWindow.CorridorLength(laneCount, spacing);
        Debug.Log($"[FallingRock] 낙석 커튼 생성 완료. 통로 길이 {corridor} U → 구(7 U/s)가 " +
                  $"{corridor / FallingRockPassWindow.SphereSpeedDefault:F2}초에 통과한다. 투하 주기는 평소 {spawner.spawnInterval}초 / " +
                  $"감속 중 {spawner.spawnIntervalWhileSlowed}초 — 낙하만 늦추면 통로를 막는 시간이 " +
                  "길어져 오히려 통과가 어려워지므로 스폰 주기도 함께 늘어난다(폴더 CLAUDE.md 참고).", root);

        if (zoneCol == null)
        {
            Debug.LogWarning("[FallingRock] 씬에 SlowZone이 없어 감속 구역을 연결하지 못했다. " +
                             "Tools > Hourglass로 모래시계 세트를 먼저 만든 뒤, 이 커튼을 구역 상단으로 " +
                             "옮기고 스포너의 referenceSlowZone에 그 SlowZone을 연결해라. 감속 구역 " +
                             "안에서 떨어지지 않으면 낙석이 느려지지 않아 퍼즐이 성립하지 않는다.");
            return root;
        }

        // 구역 높이는 늘리지 않아도 된다(기본 6 U가 낙하 6 U = 타이밍 계산의 기준 그대로다).
        // 오히려 despawnFallDistance보다 높아지면 낙석이 구역 안에서 소멸하므로, 그 경우는
        // Validate()가 정확한 수치로 잡아준다. 여기서 보는 것은 전진 축(Z) 길이뿐이다.
        // 검사 문구는 커스텀 창이 생성 전 미리보기로도 띄우므로 한 곳(ZoneLengthProblem)에 둔다.
        string zoneProblem = FallingRockPassWindow.ZoneLengthProblem(zoneCol.bounds, corridor, zone.name);
        if (zoneProblem != null) Debug.LogWarning($"[FallingRock] {zoneProblem}", zone);

        Validate();
        return root;
    }

    [MenuItem("Tools/FallingRock/Validate Scene Setup")]
    private static void Validate()
    {
        FallingRockSpawner[] spawners = Object.FindObjectsOfType<FallingRockSpawner>();
        if (spawners.Length == 0)
        {
            Debug.LogWarning("[FallingRock] 씬에 FallingRockSpawner가 없다.");
            return;
        }

        foreach (FallingRockSpawner spawner in spawners)
        {
            string problem = spawner.Validate();
            if (problem == null)
                Debug.Log($"[FallingRock] '{spawner.name}' 배치 정상.", spawner);
            else
                Debug.LogWarning($"[FallingRock] '{spawner.name}' — {problem}", spawner);
        }
    }
}
