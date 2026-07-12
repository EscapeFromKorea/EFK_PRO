// 이 파일은 반드시 프로젝트의 "Editor" 폴더 안에 위치해야 합니다.

using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > PlayerSystem > Create Player 메뉴로 구/정육면체/정사면체 플레이어 오브젝트를 생성한다.
/// Assets/Scenes/SampleScene.unity에서 실제로 쓰이는 Player_Root -> Player_Mesh / Player_Collider
/// 3단 계층을 그대로 따르되, Player_Mesh 아래에 순수 시각용 자식(Player_MeshVisual)을 하나 더 두어
/// 패드 감지용 트리거 콜라이더와 분리한다. Rigidbody 회전은 자유롭게 둬서(FreezeRotation 없음)
/// 실제 물리로 모서리에 걸려 통통 튀며 구르게 하며, Player_Mesh/Player_MeshVisual/Player_Collider는
/// 모두 로컬 회전이 identity라 Root의 실제 회전을 그대로 물려받는다(별도 회전 스크립트 불필요).
/// </summary>
public static class PlayerObjectMenuItem
{
    private enum PlayerShapeType
    {
        Sphere,
        Cube,
        Tetrahedron
    }

    [MenuItem("Tools/PlayerSystem/Create Player/Sphere")]
    private static void CreateSpherePlayer() => CreatePlayer(PlayerShapeType.Sphere);

    [MenuItem("Tools/PlayerSystem/Create Player/Cube")]
    private static void CreateCubePlayer() => CreatePlayer(PlayerShapeType.Cube);

    [MenuItem("Tools/PlayerSystem/Create Player/Tetrahedron")]
    private static void CreateTetrahedronPlayer() => CreatePlayer(PlayerShapeType.Tetrahedron);

    private static void CreatePlayer(PlayerShapeType shape)
    {
        EnsureSwitcher();
        EnsureFollowCamera();

        Vector3 spawnPos = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            spawnPos = SceneView.lastActiveSceneView.pivot;

        // ── Player_Root ────────────────────────────────────────
        GameObject root = new GameObject($"Player_{shape}");
        Undo.RegisterCreatedObjectUndo(root, "Create Player");
        root.transform.position = spawnPos;
        root.tag = "Player";

        Rigidbody rb = root.AddComponent<Rigidbody>();
        // 회전을 자유롭게 둬서 실제 물리로 모서리에 걸려 통통 튀며 구르게 한다.
        // 이동 입력(PlayerMover)은 월드 축 velocity를 직접 지정하므로 회전과 무관하게
        // 항상 의도한 방향으로 이동한다 — 방향 조작감에는 영향이 없다.
        rb.constraints = RigidbodyConstraints.None;
        rb.angularDrag = 0.5f;
        ConfigureContinuousRollPhysics(rb, shape);
        // 토크+마찰 물리 구르기에 맞춘 오버라이드. 정육면체/정사면체 공통으로 적용하고 구에는 적용하지 않는다.
        if (shape != PlayerShapeType.Sphere)
            ConfigureTorqueRollPhysics(rb);

        PlayerMover mover = root.AddComponent<PlayerMover>();
        // 정육면체/정사면체는 토크+마찰 물리 구르기로 통일한다. 구(Sphere)만 기존 velocity/각속도
        // 대입 방식(useTorqueRolling=false 기본값)을 그대로 유지해 동작이 변하지 않는다.
        if (shape != PlayerShapeType.Sphere)
            mover.useTorqueRolling = true;
        root.AddComponent<PlayerJump>();
        root.AddComponent<PlayerAccelReceiver>();
        PlayerShapeController shapeController = root.AddComponent<PlayerShapeController>();
        // SphereCollider는 비균일(X/Y) 스케일에서 반지름이 두 축 중 큰 쪽 기준으로만 커지므로,
        // 구 모양일 때만 PlayerShapeController가 X/Y 평균으로 world scale을 보정하게 한다.
        shapeController.useAverageColliderScale = shape == PlayerShapeType.Sphere;

        Mesh tetraMesh = shape == PlayerShapeType.Tetrahedron ? TetrahedronMeshGenerator.Create(0.5f) : null;

        // ── Player_Mesh (트리거 전용, 회전시키지 않음) ──────────
        GameObject meshHolder = new GameObject("Player_Mesh");
        Undo.RegisterCreatedObjectUndo(meshHolder, "Create Player");
        meshHolder.transform.SetParent(root.transform, false);
        meshHolder.tag = "Player";
        AddCollider(meshHolder, shape, tetraMesh, isTrigger: true);

        // ── Player_MeshVisual (실제로 구르며 회전하는 순수 시각 메쉬) ──
        GameObject visual = CreateVisual(shape, tetraMesh);
        Undo.RegisterCreatedObjectUndo(visual, "Create Player");
        visual.transform.SetParent(meshHolder.transform, false);

        // ── Player_Collider (지면/기믹과의 실제 물리 충돌 전담) ──
        GameObject colliderHolder = new GameObject("Player_Collider");
        Undo.RegisterCreatedObjectUndo(colliderHolder, "Create Player");
        colliderHolder.transform.SetParent(root.transform, false);
        colliderHolder.tag = "Player";
        AddCollider(colliderHolder, shape, tetraMesh, isTrigger: false);
        shapeController.groundContact = colliderHolder.AddComponent<PlayerGroundContact>();

        shapeController.meshTransform = meshHolder.transform;
        shapeController.colliderTransform = colliderHolder.transform;

        Selection.activeGameObject = root;
        Debug.Log($"[PlayerSystem] Player_{shape} 생성 완료");
    }

    /// <summary>
    /// 세 도형 모두 PlayerMover의 v=ωr 연속 회전 공식 하나를 공유한다(별도 모드 분기 없음).
    /// 대신 "완전히 평평한 면으로 접지한 채 순간 각속도를 강제하면 다중 접촉점이 솔버와
    /// 충돌한다"는 구조적 문제를 완화하기 위해 Rigidbody 물리 설정을 여기서 도형별로 조정한다:
    /// - maxAngularVelocity: 기본 캡이 7 rad/s라 moveSpeed/rollRadius로 나오는 각속도(기본값
    ///   기준 5/0.5=10 rad/s)가 쉽게 잘린다. 세 도형 모두 넉넉하게 올려둔다.
    /// - interpolation: 물리 스텝 사이 시각적 끊김/떨림을 보간해 부드럽게 보이게 한다.
    /// - solverIterations/solverVelocityIterations, collisionDetectionMode: 구는 접촉점이
    ///   항상 1개뿐이라 문제가 없지만, 정육면체/정사면체는 접지 중 여러 접촉점(코너/컴파운드
    ///   구)이 동시에 상충하는 방향으로 움직이려 한다 — solver iteration을 늘려 이 다중 접촉
    ///   제약을 더 정확히(덜 튀게) 풀고, ContinuousDynamic으로 회전 중 얇은 지오메트리를
    ///   파고드는 것도 방지한다.
    /// </summary>
    private static void ConfigureContinuousRollPhysics(Rigidbody rb, PlayerShapeType shape)
    {
        rb.maxAngularVelocity = 30f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (shape != PlayerShapeType.Sphere)
        {
            rb.solverIterations = 12;
            rb.solverVelocityIterations = 4;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }

    /// <summary>
    /// 토크+마찰 구르기 경로(정육면체/정사면체 공통) Rigidbody 오버라이드.
    /// - centerOfMass를 도형 기하 중심(0,0.5,0)에 고정 → 텀블링이 좌우 대칭이 되고, 자식 콜라이더
    ///   배치(컴파운드 구/박스)에 따라 자동 계산되는 무게중심이 치우쳐 한쪽으로 쏠리는 걸 막는다.
    /// - angularDrag를 낮춰(0.1) 토크로 시작된 구르기가 과하게 죽지 않게 한다(기본 0.5는 무겁다).
    /// - maxAngularVelocity를 20으로 → 토크 구동에서는 각속도를 강제하지 않으므로 현실적 범위면 충분.
    /// </summary>
    private static void ConfigureTorqueRollPhysics(Rigidbody rb)
    {
        rb.centerOfMass = new Vector3(0f, 0.5f, 0f);
        rb.angularDrag = 0.1f;
        rb.maxAngularVelocity = 20f;
    }

    private static GameObject CreateVisual(PlayerShapeType shape, Mesh tetraMesh)
    {
        GameObject go;
        switch (shape)
        {
            case PlayerShapeType.Sphere:
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                break;
            case PlayerShapeType.Cube:
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                break;
            default:
                go = new GameObject();
                go.AddComponent<MeshFilter>().sharedMesh = tetraMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = CreateDefaultMaterial();
                break;
        }

        go.name = "Player_MeshVisual";

        // 시각 전용 오브젝트이므로 프리미티브가 기본으로 붙여준 콜라이더는 제거한다.
        Collider existingCollider = go.GetComponent<Collider>();
        if (existingCollider != null)
            Object.DestroyImmediate(existingCollider);

        return go;
    }

    private static void AddCollider(GameObject go, PlayerShapeType shape, Mesh tetraMesh, bool isTrigger)
    {
        if (shape == PlayerShapeType.Sphere)
        {
            // 시각 메쉬와 동일한 SphereCollider를 써서 실제로 둥글게 부딪히고 구르는 느낌을
            // 살린다. 비균일(X/Y 개별) 스케일 시의 반지름 왜곡은 솔리드 콜라이더 쪽에서
            // PlayerShapeController.useAverageColliderScale 보정으로 상쇄한다.
            SphereCollider sphere = go.AddComponent<SphereCollider>();
            sphere.radius = 0.5f;
            sphere.isTrigger = isTrigger;
            return;
        }

        if (shape == PlayerShapeType.Tetrahedron)
        {
            if (isTrigger)
            {
                // 트리거는 침투 방지가 필요 없어 실제 메쉬 모양을 그대로 써도 안전하다.
                MeshCollider mesh = go.AddComponent<MeshCollider>();
                mesh.sharedMesh = tetraMesh;
                mesh.convex = true;
                mesh.isTrigger = true;
                return;
            }

            // Unity에는 정사면체에 대응하는 기본 Primitive Collider가 없다. Convex MeshCollider를
            // 솔리드에도 그대로 쓰면 실제 뾰족한 꼭짓점/모서리로 접지하는데, 회전 중 그 뾰족한
            // 점이 바닥을 파고들려 하는 정도가 매 순간 불연속적으로 바뀌어(각/모서리가 바뀔
            // 때마다 접촉 형태 자체가 급변) 솔버가 가장 격하게 반응한다. 대신 실제 꼭짓점
            // 4개 각각에 작은 SphereCollider를 배치한 컴파운드로 근사한다 — 구는 어느 방향으로
            // 회전해도 중심에서 표면까지 거리가 항상 반지름으로 일정하므로, 회전하며 바닥에
            // 파고드는 정도가 완만하고 연속적으로 바뀐다(뾰족한 점보다 솔버가 다루기 쉽다).
            // 각 구의 중심은 실제 꼭짓점에서 무게중심 방향으로 반지름만큼 당겨서, 구 표면이
            // 원본 메쉬의 꼭짓점 위치와 거의 겹치도록 했다(모양 왜곡을 최소화).
            foreach (Vector3 vertex in TetrahedronMeshGenerator.GetVertices(0.5f))
            {
                SphereCollider vertexSphere = go.AddComponent<SphereCollider>();
                vertexSphere.radius = TetrahedronVertexColliderRadius;
                Vector3 inwardDir = vertex.normalized; // 무게중심(원점) -> 꼭짓점 방향
                vertexSphere.center = vertex - inwardDir * TetrahedronVertexColliderRadius;
                // 토크+마찰 구르기: 접촉 모서리가 미끄러지지 않고 피벗 역할을 하도록 그립 마찰.
                // (기존 저마찰은 '각속도 강제' 방식의 접촉 충돌 완화용이라 여기선 정반대로 부적합.)
                vertexSphere.material = GetTetrahedronGripMaterial();
            }
            return;
        }

        // 정육면체 솔리드/트리거 콜라이더. BoxCollider가 곧 실제 정육면체 모양 그 자체이므로
        // 형태를 바꿀 이유는 없다. 정육면체도 토크+마찰 물리 구르기로 통일했으므로, 솔리드
        // 콜라이더에는 고마찰 그립 재질(GetCubeGripMaterial)을 씌워 넓은 접촉면이 미끄러지지
        // 않고 앞모서리를 피벗 삼아 넘어가게 한다.
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = Vector3.one;
        box.isTrigger = isTrigger;
        if (!isTrigger)
            // 정육면체도 토크+마찰 구르기로 통일. 넓은 평면으로 접지하므로 정사면체보다 강한
            // 그립(마찰)을 줘야 앞모서리를 피벗 삼아 미끄러지지 않고 넘어간다.
            box.material = GetCubeGripMaterial();
    }

    /// <summary>정사면체 컴파운드 콜라이더의 꼭짓점 구 반지름. 너무 크면 형태가 뭉툭해지고,
    /// 너무 작으면 뾰족한 점에 가까워져 접촉이 다시 불안정해진다.</summary>
    private const float TetrahedronVertexColliderRadius = 0.12f;

    private static PhysicMaterial tetrahedronGripMaterial;
    private static PhysicMaterial cubeGripMaterial;

    /// <summary>
    /// 정사면체 토크+마찰 구르기 그립 재질. 꼭짓점 컴파운드 구가 접촉 모서리를 붙잡아 토크를 실제
    /// 구르기로 변환하게 한다. 씬 테스트 결과 그립이 과해 마찰을 낮췄다(static 1.0→0.7, dynamic
    /// 0.8→0.55). 정육면체보다 접촉 면적이 작아 더 낮은 마찰로도 충분히 굴러간다.
    /// </summary>
    private static PhysicMaterial GetTetrahedronGripMaterial()
    {
        if (tetrahedronGripMaterial == null)
            tetrahedronGripMaterial = CreateGripMaterial("PlayerTetrahedron_RollGrip", 0.7f, 0.55f);
        return tetrahedronGripMaterial;
    }

    /// <summary>
    /// 정육면체 토크+마찰 구르기 그립 재질. 넓은 평면으로 접지해 미끄러지기 쉬우므로 정사면체보다
    /// 강한 그립(static 1.0 / dynamic 0.8)을 줘 앞모서리 피벗이 안정적으로 걸리게 한다.
    /// </summary>
    private static PhysicMaterial GetCubeGripMaterial()
    {
        if (cubeGripMaterial == null)
            cubeGripMaterial = CreateGripMaterial("PlayerCube_RollGrip", 1.0f, 0.8f);
        return cubeGripMaterial;
    }

    private static PhysicMaterial CreateGripMaterial(string name, float staticFriction, float dynamicFriction)
    {
        return new PhysicMaterial(name)
        {
            staticFriction = staticFriction,
            dynamicFriction = dynamicFriction,
            frictionCombine = PhysicMaterialCombine.Maximum,
            bounciness = 0f,
            bounceCombine = PhysicMaterialCombine.Minimum
        };
    }

    private static Material CreateDefaultMaterial()
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Diffuse");
        if (shader == null) shader = Shader.Find("Hidden/InternalErrorShader");
        return new Material(shader);
    }

    private static void EnsureSwitcher()
    {
        if (Object.FindObjectOfType<PlayerControlSwitcher>() != null) return;

        GameObject switcherObj = new GameObject("PlayerControlSwitcher");
        Undo.RegisterCreatedObjectUndo(switcherObj, "Create PlayerControlSwitcher");
        switcherObj.AddComponent<PlayerControlSwitcher>();
    }

    /// <summary>
    /// 씬의 기존 카메라에 PlayerFollowCamera를 붙여 활성 플레이어를 따라가게 한다. 카메라 오브젝트는
    /// PlayerSystem 밖의 씬 자산이므로 새로 만들지 않고, 이미 있는 Main Camera(없으면 아무 Camera)에
    /// 우리 컴포넌트만 부착한다. 이미 붙어 있거나 씬에 카메라가 없으면 아무 것도 하지 않는다
    /// (그 경우 씬의 카메라에 직접 이 스크립트를 추가하면 된다).
    /// </summary>
    private static void EnsureFollowCamera()
    {
        if (Object.FindObjectOfType<PlayerFollowCamera>() != null) return;

        Camera cam = Camera.main;
        if (cam == null) cam = Object.FindObjectOfType<Camera>();
        if (cam == null) return;

        Undo.AddComponent<PlayerFollowCamera>(cam.gameObject);
        Debug.Log($"[PlayerSystem] '{cam.name}'에 PlayerFollowCamera를 자동 부착했습니다.");
    }
}
