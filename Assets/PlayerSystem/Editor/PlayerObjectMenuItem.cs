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
        // 정육면체/정사면체는 물리 회전을 고정한다 — 콜라이더/무게중심 축이 고정돼 점프 도달
        // 궤적과 착지가 결정론적으로 유지되고(레벨 도달 규격표·LD-01의 전제), "평평한 면 접촉 +
        // 각속도/토크"가 PhysX 접촉 솔버와 충돌하던 구조적 문제 자체가 사라진다. 구르는 연출은
        // PlayerVisualRoll이 시각 메쉬만 굴려서 낸다. 구(Sphere)는 회전이 자유로워도 도달에
        // 영향이 없고(무게중심 포물선은 스핀과 무관) 실제 물리 구르기가 자연스러워 자유회전으로 둔다.
        rb.constraints = shape == PlayerShapeType.Sphere
            ? RigidbodyConstraints.None
            : RigidbodyConstraints.FreezeRotation;
        rb.angularDrag = 0.5f;
        ConfigureContinuousRollPhysics(rb, shape);
        // 토크+마찰 물리 구르기(ConfigureTorqueRollPhysics / useTorqueRolling)는 더 이상 켜지 않는다.
        // 코드는 PlayerMover에 플래그(useTorqueRolling=false 기본) 뒤로 남겨둬, 추후 "착지까지 실제
        // 물리로 구르는" 방식으로 되돌리고 싶을 때 플래그만 켜면 복원할 수 있게 한다.

        root.AddComponent<PlayerMover>();
        root.AddComponent<PlayerJump>();
        root.AddComponent<PlayerAccelReceiver>();
        root.AddComponent<PlayerWindReceiver>();
        // 이속/점프높이/질량/마찰은 PlayerShapeIdentity가 도형 스탯 에셋(PlayerShapeStats)에서 읽어
        // 아래(콜라이더 생성 후) Start 시점에 적용한다 — 값은 코드가 아니라 데이터 에셋에 있다(#2).
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

        // 정육면체/정사면체는 물리 회전을 고정했으므로, 시각 메쉬(Player_MeshVisual)만 굴려
        // 구르는 연출을 낸다. 공중에서만 이동 방향으로 굴리고 착지 시 똑바로 복귀한다.
        if (shape != PlayerShapeType.Sphere)
        {
            PlayerVisualRoll visualRoll = root.AddComponent<PlayerVisualRoll>();
            visualRoll.visual = visual.transform;
            visualRoll.groundContact = shapeController.groundContact;
            // 경사면 침하 보정 기준값. 정육면체는 반높이 0.5가 기하적으로 정확하지만(기본값),
            // 정사면체는 밑면 3회 대칭이라 정육면체용 4각 대칭 식과 정확히 맞는 값이 없다
            // (비탈 방향에 따라 이론값 0.29~0.57). 파고드는 것보다 살짝 뜨는 편이 덜 어색해
            // 중앙보다 낮은 값에서 출발시키고, 최종 조정은 씬에서 눈으로 한다.
            if (shape == PlayerShapeType.Tetrahedron) visualRoll.groundSinkFactor = 0.45f;
        }

        // 도형 스탯 에셋을 로드(없으면 요구사항 기본값으로 생성)해 부착한다. 실제 적용은 Start에서.
        PlayerShapeIdentity identity = root.AddComponent<PlayerShapeIdentity>();
        identity.stats = LoadOrCreateStats(shape);
        identity.solidCollider = colliderHolder.GetComponent<Collider>();

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
    ///   항상 1개뿐이라 문제가 없지만, 정육면체(평평한 면 전체로 접지)는 여러 접촉점이 동시에
    ///   상충하는 방향으로 움직이려 한다 — solver iteration을 늘려 이 다중 접촉 제약을 더
    ///   정확히(덜 튀게) 풀고, ContinuousDynamic으로 회전 중 얇은 지오메트리를 파고드는 것도
    ///   방지한다. 정사면체는 예전에 꼭짓점/변마다 겹치는 SphereCollider 컴파운드를 썼을 때
    ///   바로 이 다중 접촉 충돌 때문에 버벅임+파고듦이 함께 났었는데, 지금은 살짝 깎인
    ///   단일 Convex MeshCollider라 콜라이더 자체는 구와 마찬가지로 접촉이 한 벌뿐이지만,
    ///   그래도 회전 중 접촉 형태가 자주 바뀌므로 안전하게 정육면체와 같은 설정을 공유한다.
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

    /// <summary>
    /// 도형 스탯 에셋(PlayerShapeStats)을 로드한다. 없으면 요구사항 #2 기본값으로 생성해
    /// Assets/PlayerSystem/ShapeStats/에 저장한다. 이후 디자이너가 이 에셋을 직접 수정하면
    /// 재컴파일 없이 반영된다(값이 코드가 아니라 데이터에 있음).
    /// </summary>
    private static PlayerShapeStats LoadOrCreateStats(PlayerShapeType shape)
    {
        const string dir = "Assets/PlayerSystem/ShapeStats";
        string path = $"{dir}/{shape}Stats.asset";

        PlayerShapeStats stats = AssetDatabase.LoadAssetAtPath<PlayerShapeStats>(path);
        if (stats != null) return stats;

        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets/PlayerSystem", "ShapeStats");

        stats = ScriptableObject.CreateInstance<PlayerShapeStats>();
        switch (shape)
        {
            case PlayerShapeType.Sphere:
                stats.kind = PlayerShapeStats.ShapeKind.Sphere;
                stats.moveSpeed = 7.0f; stats.jumpHeight = 1.6f; stats.mass = 1.5f; stats.friction = 0.1f;
                break;
            case PlayerShapeType.Tetrahedron:
                stats.kind = PlayerShapeStats.ShapeKind.Tetrahedron;
                stats.moveSpeed = 5.0f; stats.jumpHeight = 2.0f; stats.mass = 1.0f; stats.friction = 0.8f;
                break;
            case PlayerShapeType.Cube:
                stats.kind = PlayerShapeStats.ShapeKind.Cube;
                stats.moveSpeed = 3.5f; stats.jumpHeight = 1.2f; stats.mass = 3.0f; stats.friction = 0.5f;
                break;
        }
        stats.bounciness = 0f;

        AssetDatabase.CreateAsset(stats, path);
        AssetDatabase.SaveAssets();
        return stats;
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

            // Unity에는 정사면체에 대응하는 기본 Primitive Collider가 없다.
            //
            // [시행착오] 처음엔 꼭짓점 4개에 작은 SphereCollider를 배치한 컴파운드로 근사했었다.
            // 그런데 변 길이(약 1.41)에 비해 구 반지름(0.12)이 작아 변/면 중간이 뚫려 있어 그
            // 틈으로 메쉬가 파고드는 문제가 있었고, 변 중점에도 구를 추가해 틈을 메우자 이번엔
            // 하나의 강체에 딸린 여러 개의 독립된 볼록 프리미티브가 동시에 접촉하면서(예: 변
            // 하나에 꼭짓점 2개 + 중점 1개가 거의 동시에 닿음) PhysX 솔버가 서로 다른 분리 방향을
            // 요구하는 접촉점들 사이에서 버벅였다 — 틈을 메우는 것과 접촉 개수를 줄이는 것이
            // 서로 상충하는 목표였다.
            //
            // 그래서 컴파운드를 걷어내고, 각 꼭짓점을 이웃 꼭짓점 쪽으로 살짝(truncateFactor) 깎아
            // 뾰족한 점 대신 작은 평평한 면으로 만든 뒤 그 점들을 "단 하나의" Convex MeshCollider로
            // 쿠킹한다. 콜라이더가 하나뿐이라 PhysX가 표준적인 단일 볼록체 접촉으로 다뤄 컴파운드
            // 방식의 접촉 충돌이 근본적으로 사라지고, 깎인 평평한 면 덕에 완전히 뾰족한 점으로
            // 접지할 때 생기던 접촉 불연속(매 순간 접촉 형태 급변)도 함께 줄어든다. 빈틈도 없다 —
            // 하나의 통짜 볼록 껍질이라 SphereCollider 사이 같은 미접촉 구간 자체가 없다.
            MeshCollider solidCollider = go.AddComponent<MeshCollider>();
            solidCollider.sharedMesh = TetrahedronMeshGenerator.CreateChamferedColliderMesh(0.5f);
            solidCollider.convex = true;
            // 물리 머티리얼(마찰/반발)은 PlayerShapeIdentity가 도형 스탯에서 읽어 Start에 적용한다(#6).
            return;
        }

        // 정육면체 솔리드/트리거 콜라이더. BoxCollider가 곧 실제 정육면체 모양 그 자체이므로 형태를
        // 바꿀 이유가 없다. 물리 머티리얼(마찰/반발)은 PlayerShapeIdentity가 도형 스탯에서 읽어
        // Start에 적용한다(#6).
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = Vector3.one;
        box.isTrigger = isTrigger;
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
