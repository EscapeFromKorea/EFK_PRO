using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lab_CH1 레벨 배치 — 각 생성 메뉴로 만들어 둔 오브젝트(플레이어 3, 투석기, 파괴벽, 카트 3+레일,
/// 태엽 축, 체크포인트, PathChaser 일체)를 CH1 경로로 재배치하고 빠진 것(가림벽, 도착 플랫폼,
/// 카트 전용 트리거 교량, TeamExitZone, arrivalPoint)을 만들어 배선한다. Undo 한 번으로 되돌릴 수 있고,
/// 다시 실행해도 같은 결과가 나온다(새로 만드는 것은 이름으로 찾아 재사용).
///
/// 경로(바닥 큐브 z 50 → -80, 진행 방향 -Z):
///   시작(플레이어·SAFE_PRE·체크포인트) z≈40 → 투석기 z=12 → 가림벽+파괴벽(높이 12) z=-10 → SAFE_POST z=-20
///   → 카트 승차장 z=-64 → 절벽(바닥 끝 z≈-80.7 ~ 플랫폼 -89) → 도착 플랫폼(TeamExitZone) z -89~-111
/// 높이는 모두 바닥 콜라이더에 레이를 쏴서 구한다 — 메인 바닥은 기울어져 있어 pos.y+scale.y/2가 틀린다.
/// </summary>
public static class Ch1LevelLayoutMenuItem
{
    private const float WallZ = -10f;
    private const float WallHalfWidth = 3f;
    private const float WallHeight = 12f;
    private const float CartStartZ = -64f;
    private const float EdgeZ = -80.6f;
    private const float PlatformNearZ = -89f;
    private const float PlatformFarZ = -111f;
    private const float RailEndZ = -96f;
    private static readonly float[] RailX = { -4f, 0f, 4f };

    // 배치모드 -executeMethod 전용 진입점 — 씬을 열고 배치 후 한 번만 저장한다.
    public static void LayoutFromCommandLine()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Lab_CH1.unity");
        Layout();
        EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("Tools/PathChaserSystem/Layout CH1 Level (Lab_CH1)")]
    public static void Layout()
    {
        Undo.SetCurrentGroupName("Layout CH1 Level");
        int undoGroup = Undo.GetCurrentGroup();

        GameObject floorGo = GameObject.Find("Cube");
        Collider floor = floorGo != null ? floorGo.GetComponent<BoxCollider>() : null;
        RespawnController respawn = Object.FindObjectOfType<RespawnController>();
        PathChaserController controller = Object.FindObjectOfType<PathChaserController>();
        BreakableObject wall = Object.FindObjectOfType<BreakableObject>();
        CatapultLoadController catapult = Object.FindObjectOfType<CatapultLoadController>();
        WindupAxle axle = Object.FindObjectOfType<WindupAxle>();
        RailCart[] carts = Object.FindObjectsOfType<RailCart>().OrderBy(c => c.name + c.GetInstanceID()).ToArray();
        if (floor == null || respawn == null || controller == null || controller.agent == null || wall == null
            || catapult == null || axle == null || carts.Length != 3 || controller.safePreCounter == null
            || controller.safePostCounter == null)
        {
            Debug.LogError("[CH1 Layout] 필요한 오브젝트가 없다 — 바닥 'Cube', RespawnController, PathChaser " +
                           "(Create Path Chaser 메뉴), BreakableObject, 투석기, WindupAxle, RailCart 정확히 3대.");
            return;
        }
        Physics.SyncTransforms();

        // ── 1. 도착 플랫폼(절벽 건너편) — 윗면을 절벽 끝 바닥 높이와 맞춘다 ──
        float edgeY = GroundY(floor, 0f, EdgeZ);
        GameObject platform = FindOrCreatePrimitive("CH1_ArrivalPlatform");
        SetTransform(platform.transform, new Vector3(0f, edgeY - 0.5f, (PlatformNearZ + PlatformFarZ) / 2f),
            Quaternion.identity, new Vector3(30f, 1f, PlatformNearZ - PlatformFarZ));
        Physics.SyncTransforms();
        Collider platformCol = platform.GetComponent<Collider>();

        // ── 2. 플레이어·체크포인트·SAFE_PRE (시작 구역) ──
        string[] shapes = { "Player_Sphere", "Player_Cube", "Player_Tetrahedron" };
        for (int i = 0; i < shapes.Length; i++)
        {
            GameObject p = GameObject.Find(shapes[i]);
            if (p != null) PlaceOnGround(p, floor, -6f + 3f * i, 40f, 0.05f);
        }
        RespawnZone checkpoint = Object.FindObjectOfType<RespawnZone>();
        if (checkpoint != null) PlaceOnGround(checkpoint.gameObject, floor, -12f, 44f, 0f);
        SetPosition(controller.safePreCounter.transform, OnGround(floor, -10f, 38f));

        // ── 3. 투석기 — 발사 방향은 -root.forward(CatapultArm). 벽 구멍 중앙을 향하게 돌린다 ──
        Vector3 catapultPos = new Vector3(-5f, 0f, 12f); // 벽까지 약 22U — 사거리(속도 10~18, 피치 50°) 안
        Vector3 toWall = new Vector3(0f, 0f, WallZ) - catapultPos;
        Undo.RecordObject(catapult.transform, "Layout CH1 Level");
        catapult.transform.rotation = Quaternion.LookRotation(-toWall.normalized, Vector3.up);
        PlaceOnGround(catapult.gameObject, floor, catapultPos.x, catapultPos.z, 0.02f);

        // ── 4. 파괴벽 + 양옆 가림벽(바닥 전폭을 막아 벽을 부숴야만 지나가게) ──
        float wallY = GroundY(floor, 0f, WallZ);
        SetTransform(wall.transform, new Vector3(0f, wallY + WallHeight / 2f - 0.05f, WallZ),
            Quaternion.identity, new Vector3(WallHalfWidth * 2f, WallHeight, 1f));
        Bounds fb = floor.bounds;
        CreateBarrier("CH1_Barrier_L", fb.min.x - 0.5f, -WallHalfWidth, wallY);
        CreateBarrier("CH1_Barrier_R", WallHalfWidth, fb.max.x + 0.5f, wallY);
        SetPosition(controller.safePostCounter.transform, OnGround(floor, -10f, -20f));

        // ── 5. 카트 3대 + 레일(승차장 → 절벽 → 플랫폼) + 태엽 축 하나에 3대 연결 ──
        for (int i = 0; i < carts.Length; i++)
        {
            RailCart cart = carts[i];
            RailPath path = cart.path;
            float x = RailX[i];
            Vector3 a = OnGround(floor, x, CartStartZ);
            Vector3 mid = OnGround(floor, x, EdgeZ);
            Vector3 b = OnGround(platformCol, x, RailEndZ);
            SetPosition(path.transform, a);
            SetPosition(path.waypoints[0], a);
            SetPosition(path.waypoints[1], mid);
            SetPosition(path.waypoints[2], b);
            Undo.RecordObject(path, "Layout CH1 Level");
            path.curveControlPoints = new Transform[0]; // 직선 레일 — 생성기의 곡선 제어점 해제
            Transform curve = path.transform.Find("CurvePoint_AMid");
            if (curve != null) Undo.DestroyObjectImmediate(curve.gameObject);

            Undo.RecordObject(cart.transform, "Layout CH1 Level");
            cart.transform.SetPositionAndRotation(a, Quaternion.LookRotation(mid - a, Vector3.up));
            Undo.RecordObject(cart, "Layout CH1 Level");
            cart.axle = axle;
            path.GetComponent<RailTrackVisual>()?.ForceRebuild();
        }
        PlaceOnGround(axle.gameObject, floor, -9f, CartStartZ + 2f, 0f);

        // ── 6. 카트 전용 트리거 교량(절벽 구간만 카트 중력 상쇄) ──
        float gapCenterZ = (EdgeZ + PlatformNearZ) / 2f;
        GameObject support = FindOrCreate("CH1_RailCartSupportZone");
        SetTransform(support.transform, new Vector3(0f, edgeY + 1f, gapCenterZ), Quaternion.identity, Vector3.one);
        BoxCollider supportCol = GetOrAdd<BoxCollider>(support);
        supportCol.isTrigger = true;
        supportCol.size = new Vector3(14f, 4f, EdgeZ - PlatformNearZ + 1f);
        GetOrAdd<RailCartSupportZone>(support);

        // 절벽에 떨어진 플레이어를 킬 라인(y -30)까지 기다리지 않고 바로 복귀시킨다.
        OutOfBoundsVolume oob = Object.FindObjectOfType<OutOfBoundsVolume>();
        if (oob != null)
        {
            SetTransform(oob.transform, new Vector3(0f, edgeY - 4f, gapCenterZ), Quaternion.identity,
                new Vector3(fb.size.x + 4f, 6f, EdgeZ - PlatformNearZ));
            if (oob.GetComponent<Collider>() is BoxCollider oobCol)
            {
                Undo.RecordObject(oobCol, "Layout CH1 Level");
                oobCol.center = Vector3.zero;
                oobCol.size = Vector3.one;
            }
        }

        // ── 7. 도착 구역: arrivalPoint + TeamExitZone ──
        float platTop = GroundY(platformCol, 0f, RailEndZ);
        GameObject arrival = FindOrCreate("CH1_ArrivalPoint");
        SetPosition(arrival.transform, new Vector3(0f, platTop, RailEndZ));
        GameObject exitGo = FindOrCreate("CH1_TeamExitZone");
        SetTransform(exitGo.transform, new Vector3(0f, platTop + 2f, (PlatformNearZ + PlatformFarZ) / 2f),
            Quaternion.identity, Vector3.one);
        BoxCollider exitCol = GetOrAdd<BoxCollider>(exitGo);
        exitCol.isTrigger = true;
        exitCol.size = new Vector3(30f, 4f, PlatformNearZ - PlatformFarZ);
        TeamExitZone exitZone = GetOrAdd<TeamExitZone>(exitGo);

        // ── 8. PathChaser — 시작(z 49) → 벽 구멍 앞 정지 → 절벽 끝 종점 ──
        PathChaserAgent agent = controller.agent;
        Transform[] wps = agent.waypoints;
        SetPosition(wps[0], OnGround(floor, 5f, 49f) + Vector3.up);
        SetPosition(wps[1], OnGround(floor, 1.5f, WallZ + 3f) + Vector3.up);
        SetPosition(wps[2], OnGround(floor, 0f, EdgeZ + 0.6f) + Vector3.up);
        SetPosition(agent.transform, wps[0].position);
        EnsureChaserVisual(agent.transform);

        Undo.RecordObject(controller, "Layout CH1 Level");
        controller.wall = wall;
        controller.carts = carts;
        controller.arrivalPoint = arrival.transform;
        controller.teamExitZone = exitZone;
        controller.arrivalRadius = 5f; // 레일 3줄이 x ±4로 벌어져 있어 한 점 기준 반경을 넓힌다
        controller.wallStopWaypointIndex = 1;

        // 구간 카운터 → RespawnController.RespawnPlayer(player, safePoint). 비어 있을 때만 건다(중복 방지).
        WireIfEmpty(controller.safePreCounter, respawn);
        WireIfEmpty(controller.safePostCounter, respawn);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Debug.Log($"[CH1 Layout] 배치 완료 — 절벽 끝 높이 {edgeY:F3}, 플랫폼 윗면 {platTop:F3}. Ctrl+S로 저장하세요.");
    }

    private static void WireIfEmpty(SectionHitCounter counter, RespawnController respawn)
    {
        if (counter.OnThresholdReached.GetPersistentEventCount() > 0) return;
        Undo.RecordObject(counter, "Layout CH1 Level");
        UnityEventTools.AddPersistentListener<GameObject, SectionSafePoint>(counter.OnThresholdReached, respawn.RespawnPlayer);
    }

    private static void CreateBarrier(string name, float minX, float maxX, float groundY)
    {
        GameObject go = FindOrCreatePrimitive(name);
        SetTransform(go.transform, new Vector3((minX + maxX) / 2f, groundY + WallHeight / 2f - 0.05f, WallZ),
            Quaternion.identity, new Vector3(maxX - minX, WallHeight, 1f));
        go.isStatic = true;
    }

    private static void EnsureChaserVisual(Transform agent)
    {
        if (agent.Find("PathChaser_Visual") != null) return;
        GameObject v = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Undo.RegisterCreatedObjectUndo(v, "Layout CH1 Level");
        v.name = "PathChaser_Visual";
        Object.DestroyImmediate(v.GetComponent<Collider>()); // 잡힘은 부모의 트리거(반경 1)가 판정한다
        v.transform.SetParent(agent, false);
        v.transform.localScale = Vector3.one * 2f;
        v.GetComponent<MeshRenderer>().sharedMaterial = LoadOrCreateRedMaterial();
    }

    private static Material LoadOrCreateRedMaterial()
    {
        const string path = "Assets/PathChaserSystem/PathChaserRed.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        Shader shader = GraphicsSettings.currentRenderPipeline == null
            ? Shader.Find("Standard") : Shader.Find("Universal Render Pipeline/Lit");
        mat = new Material(shader) { color = new Color(0.85f, 0.15f, 0.15f) };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ── 공용 헬퍼 ──

    private static float GroundY(Collider ground, float x, float z)
    {
        Ray ray = new Ray(new Vector3(x, 500f, z), Vector3.down);
        if (ground.Raycast(ray, out RaycastHit hit, 1000f)) return hit.point.y;
        Debug.LogWarning($"[CH1 Layout] ({x}, {z})에서 {ground.name} 윗면을 못 찾았다 — bounds 상단으로 대체.");
        return ground.bounds.max.y;
    }

    private static Vector3 OnGround(Collider ground, float x, float z) => new Vector3(x, GroundY(ground, x, z), z);

    /// <summary>xz로 옮긴 뒤, 보이는 메쉬의 가장 낮은 점이 바닥 윗면 + clearance에 오도록 Y를 맞춘다.</summary>
    private static void PlaceOnGround(GameObject go, Collider ground, float x, float z, float clearance)
    {
        Undo.RecordObject(go.transform, "Layout CH1 Level");
        go.transform.position = new Vector3(x, go.transform.position.y, z);
        Renderer[] rs = go.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) { go.transform.position = OnGround(ground, x, z); return; }
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        go.transform.position += Vector3.up * (GroundY(ground, x, z) + clearance - b.min.y);
    }

    private static void SetPosition(Transform t, Vector3 pos)
    {
        Undo.RecordObject(t, "Layout CH1 Level");
        t.position = pos;
    }

    private static void SetTransform(Transform t, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        Undo.RecordObject(t, "Layout CH1 Level");
        t.SetPositionAndRotation(pos, rot);
        t.localScale = scale;
    }

    private static GameObject FindOrCreate(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go != null) return go;
        go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Layout CH1 Level");
        return go;
    }

    private static GameObject FindOrCreatePrimitive(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go != null) return go;
        go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, "Layout CH1 Level");
        return go;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        if (c != null) { Undo.RecordObject(c, "Layout CH1 Level"); return c; }
        return Undo.AddComponent<T>(go);
    }
}
