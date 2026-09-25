using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 경로 추격자(CH1) 그레이박스 배치 도구. 실제 CH1 레벨(투석기 → 벽 → 카트 3대 → 출구)이 아직 씬에
/// 연결된 경로로 존재하지 않아, 웨이포인트/안전점은 서로 떨어진 열린 공간에 놓는 자체 그레이박스
/// 자리표시자다 — 씬에 이미 있는 DestructionSystem Breakable, RailCartSystem RailCart는 있는 만큼만
/// 참조로 건다(레벨이 갖춰지기 전까지 임의로 연결하지 않는다는 원칙, 상세는 배치 로그 참고).
/// 몇 번이고 다시 실행할 수 있다(이미 있으면 건너뛴다).
/// </summary>
public static class PathChaserMenuItem
{
    private static readonly Vector3 BasePos = new Vector3(0f, 50f, 0f); // 실제 레벨과 겹치지 않는 열린 공간

    [MenuItem("Tools/PathChaserSystem/Create Path Chaser (CH1)")]
    public static void CreatePathChaser() => BuildPathChaser();

    // 배치모드 -executeMethod 전용 진입점 — 씬을 직접 열고 배치 후 한 번만 저장한다(배치모드 저장은
    // 전체 재직렬화를 일으키므로 저장은 반드시 한 번으로 끝낸다).
    public static void CreateFromCommandLine()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Lab_CH1.unity");
        BuildPathChaser();
        EditorSceneManager.SaveScene(scene);
    }

    private static void BuildPathChaser()
    {
        if (Object.FindObjectOfType<PathChaserController>() != null)
        {
            Debug.LogWarning("[PathChaser] 씬에 이미 PathChaserController가 있다 — 하나만 둔다.");
            return;
        }

        RespawnController respawn = Object.FindObjectOfType<RespawnController>();
        if (respawn == null)
        {
            Debug.LogWarning("[PathChaser] 씬에 RespawnController가 없어 구간 안전점을 배선할 수 없다 " +
                             "(Tools > Respawn > Create Respawn Controller를 먼저 실행해라).");
            return;
        }

        // 1. 웨이포인트 — 그레이박스 자리표시자 3개(시작 → 벽 앞 정지 한계 → 종점)
        Transform wp0 = CreateWaypoint("PathChaser_WP0_Start", BasePos);
        Transform wp1 = CreateWaypoint("PathChaser_WP1_WallStop", BasePos + new Vector3(6f, 0f, 0f));
        Transform wp2 = CreateWaypoint("PathChaser_WP2_End", BasePos + new Vector3(12f, 0f, 0f));

        // 2. 추격자 — kinematic Rigidbody + 잡힘 판정 트리거(같은 GameObject, 클래스 요약 참고)
        GameObject agentGo = new GameObject("PathChaserAgent_CH1");
        agentGo.transform.position = wp0.position;

        Rigidbody rb = agentGo.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        PathChaserAgent agent = agentGo.AddComponent<PathChaserAgent>();
        agent.waypoints = new[] { wp0, wp1, wp2 };
        agent.speed = 3f;

        SphereCollider catchCollider = agentGo.AddComponent<SphereCollider>();
        catchCollider.isTrigger = true;
        catchCollider.radius = 1f;
        PathChaserCatchZone catchZone = agentGo.AddComponent<PathChaserCatchZone>();

        // 3. 안전점 2개 — Tools/Respawn/Create Section Safe Point 메뉴로 만들고(CH4/CH5와 같은
        //    패턴) SectionHitCounter+트리거를 붙인다.
        SectionHitCounter safePre = CreateSectionSafePoint(
            "SectionSafePoint_CH1_SAFE_PRE", "CH1_SAFE_PRE", BasePos + new Vector3(-3f, 0f, 3f));
        SectionHitCounter safePost = CreateSectionSafePoint(
            "SectionSafePoint_CH1_SAFE_POST", "CH1_SAFE_POST", BasePos + new Vector3(15f, 0f, 3f));

        // 4. 컨트롤러
        GameObject controllerGo = new GameObject("PathChaserController_CH1");
        PathChaserController controller = controllerGo.AddComponent<PathChaserController>();
        controller.agent = agent;
        controller.wallStopWaypointIndex = 1;
        controller.safePreCounter = safePre;
        controller.safePostCounter = safePost;
        catchZone.controller = controller;

        // 씬에 이미 있는 것만 있는 만큼 참조로 건다 — 없거나 부족해도 새로 만들지 않는다(레벨 배치
        // 전까지 억지로 끼워 맞추지 않는다는 원칙). arrivalPoint/teamExitZone은 카트가 실제로
        // 건너야 할 종점이 레벨에 없어 비워둔다 — Start()가 그 사실을 로그로 알리고 시작을 거부한다.
        BreakableObject wall = Object.FindObjectOfType<BreakableObject>();
        controller.wall = wall;
        controller.carts = Object.FindObjectsOfType<RailCart>();

        // 구간 카운터 → RespawnController.RespawnPlayer(GameObject, SectionSafePoint) 배선.
        // RespawnWiringMenuItem.WireSectionHits와 완전히 같은 동적 배선을 여기서 직접 건다(그 메뉴는
        // 씬의 모든 SectionHitCounter를 훑으므로 굳이 별도 실행할 필요 없이 이 자리에서 끝낸다).
        UnityEditor.Events.UnityEventTools.AddPersistentListener<GameObject, SectionSafePoint>(
            safePre.OnThresholdReached, respawn.RespawnPlayer);
        UnityEditor.Events.UnityEventTools.AddPersistentListener<GameObject, SectionSafePoint>(
            safePost.OnThresholdReached, respawn.RespawnPlayer);

        Selection.activeGameObject = controllerGo;

        string wallMsg = wall != null ? wall.name : "(없음)";
        string cartsMsg = controller.carts.Length.ToString();
        Debug.Log($"[PathChaser] CH1 그레이박스 배치 완료 — wall={wallMsg}, carts={cartsMsg}대. " +
                  "웨이포인트는 실제 레벨과 무관한 자리표시자이고, arrivalPoint/teamExitZone은 비워둬 " +
                  "컨트롤러가 시작을 거부한다 — 실제 CH1 레벨(투석기·벽·카트 3대·출구) 배치 후 " +
                  "재배치/재연결이 필요하다.", controllerGo);
    }

    private static Transform CreateWaypoint(string name, Vector3 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.position = pos;
        return go.transform;
    }

    private static SectionHitCounter CreateSectionSafePoint(string name, string sectionId, Vector3 pos)
    {
        RespawnMenuItem.CreateSectionSafePoint();
        GameObject go = Selection.activeGameObject;
        go.name = name;
        go.transform.position = pos;

        SphereCollider col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.6f;

        SectionSafePoint point = go.GetComponent<SectionSafePoint>();
        point.sectionId = sectionId;
        point.occupancyRadius = 0.6f;

        SectionHitCounter counter = go.AddComponent<SectionHitCounter>();
        counter.destination = point;
        counter.hitsBeforeRespawn = 1;
        point.counter = counter;

        return counter;
    }
}
