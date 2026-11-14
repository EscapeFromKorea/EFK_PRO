#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F1-1 도형 플레이어 생성기. 팀 생성기(Tools/PlayerSystem/Create Player/*, PlayerObjectMenuItem)를
/// 임시 씬에서 그대로 호출해 Base 프리팹을 만들고, 그 위에 우리 Variant 프리팹 + Lab_ShapeProfile을
/// 얹는다. Variant는 처음 없을 때만 만들고, 이미 있으면 오버라이드를 건드리지 않는다("Base
/// 재생성 후에도 변형의 오버라이드가 유지되어야 한다", F1-1 명세).
///
/// [F1 재작업 판정 R5 — "멱등" 단정 철회, 실측으로 교체] 이전 버전은 이 문단에서 "Base 재생성은
/// 몇 번을 다시 돌려도 안전하다(멱등)"이라고 **증거 없이 단정**했다(반려 사유: Variant가 참조하는
/// Base 루트 GameObject의 로컬 fileID가 재생성 후에도 보존되는지 실측하지 않았다). 실측 결과와
/// 결론은 협업/작업캐시/map-builder.md의 이번 회차 블록과 검증/F1_regenerate_idempotency.txt에
/// 남긴다 — 이 문단은 그 실측이 나온 뒤에도 고쳐 쓰지 않고 원문을 보존한 채 실측 파일을 근거로
/// 인용한다(재타이핑 금지 원칙과 같은 이유 — 결론은 파일에, 여기는 "무엇을 봤는지"만 안내).
/// </summary>
public static class Lab_PlayerBuilder
{
    private const string Root = "Assets/LaboratoryMap4/Players";
    private const string BaseDir = Root + "/Base";
    private const string GeneratedDir = Root + "/Generated";
    private const string ProfilesDir = "Assets/LaboratoryMap4/Profiles";

    private static readonly string[] ShapeNames = { "Sphere", "Cube", "Tetrahedron" };

    [MenuItem("Tools/Laboratory Map4/Players/Regenerate Base Prefabs")]
    public static void RegenerateAll()
    {
        // [F1 재작업 판정 R3, 2차 반려 C-e] 진입 시점에 딱 한 번만 확인한다 — 사람이 메뉴로 직접
        // 실행할 때 지금 열려 있는(우리와 무관한) 씬을 저장 확인 없이 갈아치우지 않기 위함이다.
        // 사용자가 여기서 취소하면 **즉시 중단**하고(아래로 진행하지 않음) "완료" 로그를 남기지
        // 않는다.
        if (!Map4SceneGuard.CanReplaceCurrentScenes())
        {
            Debug.LogWarning("[Lab_PlayerBuilder] 사용자가 저장을 취소해 Regenerate를 중단한다 — " +
                              "아무 것도 만들지 않았다(완료 로그는 남기지 않는다).");
            return;
        }

        // [2차 반려 C-e] 스크래치 씬을 도형마다 새로 만들지 않는다 — 이전 버전은 도형 3번 반복마다
        // NewSceneMode.Single로 씬을 "교체"했는데, 그때마다 직전 스크래치 씬(우리가 방금 만든
        // Player_* 오브젝트가 있는, 저장한 적 없는 씬)이 "수정됨" 상태라 인터랙티브 실행에서는
        // Unity가 **또** 저장 여부를 묻는 대화상자를 띄울 수 있었다(위에서 이미 한 번 확인했는데도
        // 스크래치 자신의 내용 때문에 다시 물어보는 것). 그 대화상자에서 사용자가 취소하면
        // NewScene 호출 자체가 어떻게 되는지 이 프로젝트에서 확인된 바 없이 그냥 계속 진행해
        // "완료" 로그까지 찍혔을 위험이 있었다. 이제 스크래치 씬은 **하나만** 만들고, 도형마다
        // 그 씬의 루트 오브젝트만 지워 재사용한다 — 씬 자체를 교체하는 API를 다시 부르지 않으므로
        // 그 경로의 대화상자 자체가 발생할 수 없다("스크래치 씬이 가드를 발동하지 않게").
        Scene scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        EnsureFolders();
        int successCount = 0;
        foreach (string shape in ShapeNames)
        {
            ClearSceneRoots(scratch);

            GameObject basePrefab = RegenerateBase(shape);
            if (basePrefab == null)
            {
                Debug.LogError($"[Lab_PlayerBuilder] '{shape}' Base 생성 실패 — 이 도형은 건너뛴다.");
                continue;
            }
            EnsureProfile(shape);
            EnsureVariant(shape, basePrefab);
            successCount++;
        }
        // [3차 반려 C2] 마지막 반복(Tetrahedron)이 만든 오브젝트가 scratch 씬에 그대로 남은 채
        // 이 메서드가 반환하면, 이 메뉴를 단독 실행한 사람은 "수정된 무제 씬"이 열린 상태로
        // 남게 된다 — NewScene을 다시 불러 갈아치우면 C-e가 막으려던 저장 대화상자 위험이
        // 되살아나므로, 같은 씬을 그대로 두고 내용만 마지막으로 한 번 더 비운다.
        ClearSceneRoots(scratch);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (successCount != ShapeNames.Length)
        {
            // [2차 반려 C-e] 일부만 성공해도 예전엔 "3종 x 3도형 생성 완료"를 그대로 찍었다 —
            // 이제 부분 실패는 예외로 던져 호출자(Map4SceneBuilder.GenerateAll → Map4Batch)가
            // 실패로 집계하게 한다.
            throw new System.Exception($"[Lab_PlayerBuilder] 부분 실패 — {successCount}/{ShapeNames.Length}개 " +
                                        "도형만 성공했다. 위 [실패] 로그에서 어느 도형인지 확인해라.");
        }
        Debug.Log("[Lab_PlayerBuilder] Base/Variant/Profile 3종 x 3도형 생성 완료.");
    }

    /// <summary>스크래치 씬의 모든 루트 GameObject를 지운다 — 씬 자체를 교체하지 않고(그러면
    /// C-e가 막으려는 대화상자 위험이 되살아난다) 내용만 비운다.</summary>
    private static void ClearSceneRoots(Scene scene)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
            Object.DestroyImmediate(go);
    }

    private static void EnsureFolders()
    {
        CreateFolderRecursive(BaseDir);
        CreateFolderRecursive(GeneratedDir);
        CreateFolderRecursive(ProfilesDir);
    }

    private static void CreateFolderRecursive(string path)
    {
        string[] parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    /// <summary>이미 활성화된 스크래치 씬(RegenerateAll이 한 번만 만들어 재사용) 위에서 팀 메뉴로
    /// Player_{shape}를 만들고, 정사면체의 런타임 전용 메시/머티리얼을 영구 에셋으로 뽑아낸 뒤
    /// Base 프리팹으로 저장한다. 스크래치 씬은 저장하지 않는다(철칙 6과 같은 원칙 — 임시
    /// 작업물을 씬 파일에 남기지 않는다).
    ///
    /// [2차 반려 C-e] 이전엔 이 메서드가 매번 NewScene(Single)로 씬 자체를 교체했다 — 도형 3번
    /// 반복마다 "직전 스크래치의 미저장 변경"을 갈아치우는 셈이라, 인터랙티브 실행에서 Unity가
    /// (우리 Map4SceneGuard와 별개로) 자체적으로 저장 여부를 물을 여지가 있었고, 그 대화상자
    /// 결과를 우리가 확인하지 않아 취소돼도 그냥 계속 진행할 위험이 있었다. 이제 씬은 호출자가
    /// 이미 만들어 넘겨주고, 이 메서드는 그 씬의 "내용"만 다루므로 씬 교체 API 자체를 다시
    /// 부르지 않는다 — 그 경로의 대화상자가 원천적으로 발생하지 않는다.</summary>
    private static GameObject RegenerateBase(string shape)
    {
        GameObject basePrefab = null;

        Selection.activeGameObject = null;
        bool ok = EditorApplication.ExecuteMenuItem($"Tools/PlayerSystem/Create Player/{shape}");
        if (!ok)
        {
            Debug.LogError($"[Lab_PlayerBuilder] 팀 메뉴 'Tools/PlayerSystem/Create Player/{shape}' 실행 실패.");
            return null;
        }

        GameObject created = GameObject.Find($"Player_{shape}");
        if (created == null)
        {
            Debug.LogError($"[Lab_PlayerBuilder] 'Player_{shape}' 오브젝트를 찾지 못했다(팀 메뉴 결과 이름 불일치?).");
            return null;
        }

        PersistRuntimeMeshesAndMaterials(created, shape);

        string path = $"{BaseDir}/Player_{shape}_Base.prefab";
        basePrefab = PrefabUtility.SaveAsPrefabAsset(created, path, out bool success);
        if (!success)
        {
            Debug.LogError($"[Lab_PlayerBuilder] '{path}' 프리팹 저장 실패.");
            return null;
        }
        Debug.Log($"[Lab_PlayerBuilder] Base 프리팹 갱신: {path}");

        // scratch 씬은 저장하지 않는다(위 주석) — 다음 도형 반복이나 이후 Map4SceneBuilder의
        // NewScene(Single) 호출이 이 씬을 그대로 교체하므로 별도 Close/복원이 필요 없다.
        return basePrefab;
    }

    /// <summary>PlayerObjectMenuItem이 정사면체에 한해 런타임에 만드는 비영속 Mesh 2개(시각용
    /// TetrahedronMeshGenerator.Create, 콜라이더용 CreateChamferedColliderMesh)와 기본 Material 1개
    /// (PlayerObjectMenuItem.CreateDefaultMaterial)를 Players/Generated/ 아래 영구 에셋으로 옮겨
    /// 프리팹 저장 후에도 참조가 끊기지 않게 한다(F1-1 명세). 구/정육면체는 내장 Primitive
    /// 메시(이미 영구 에셋)라 해당 없음.</summary>
    private static void PersistRuntimeMeshesAndMaterials(GameObject root, string shape)
    {
        if (shape != "Tetrahedron") return;

        MeshFilter visualFilter = root.GetComponentInChildren<MeshFilter>();
        if (visualFilter != null && visualFilter.sharedMesh != null &&
            string.IsNullOrEmpty(AssetDatabase.GetAssetPath(visualFilter.sharedMesh)))
        {
            visualFilter.sharedMesh = PersistMesh(visualFilter.sharedMesh, "Tetrahedron_VisualMesh");
        }

        MeshRenderer visualRenderer = root.GetComponentInChildren<MeshRenderer>();
        if (visualRenderer != null && visualRenderer.sharedMaterial != null &&
            string.IsNullOrEmpty(AssetDatabase.GetAssetPath(visualRenderer.sharedMaterial)))
        {
            string matPath = $"{GeneratedDir}/Tetrahedron_DefaultMat.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null) AssetDatabase.DeleteAsset(matPath);
            AssetDatabase.CreateAsset(Object.Instantiate(visualRenderer.sharedMaterial), matPath);
            visualRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        foreach (MeshCollider mc in root.GetComponentsInChildren<MeshCollider>())
        {
            if (mc.sharedMesh == null || !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mc.sharedMesh))) continue;
            string suffix = mc.isTrigger ? "TriggerMesh" : "ColliderMesh";
            mc.sharedMesh = PersistMesh(mc.sharedMesh, $"Tetrahedron_{suffix}");
        }
    }

    private static Mesh PersistMesh(Mesh source, string assetName)
    {
        string meshPath = $"{GeneratedDir}/{assetName}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(Object.Instantiate(source), meshPath);
        return AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
    }

    private static void EnsureProfile(string shape)
    {
        string profilePath = $"{ProfilesDir}/Lab_{shape}Profile.asset";
        if (AssetDatabase.LoadAssetAtPath<Lab_ShapeProfile>(profilePath) != null) return; // 기존 값 보존.

        string statsPath = $"{ProfilesDir}/Lab_{shape}Stats.asset";
        PlayerShapeStats teamStats = AssetDatabase.LoadAssetAtPath<PlayerShapeStats>(
            $"Assets/PlayerSystem/ShapeStats/{shape}Stats.asset");
        PlayerShapeStats clone = ScriptableObject.CreateInstance<PlayerShapeStats>();
        if (teamStats != null)
        {
            clone.kind = teamStats.kind;
            clone.moveSpeed = teamStats.moveSpeed;
            clone.jumpHeight = teamStats.jumpHeight;
            clone.mass = teamStats.mass;
            clone.friction = teamStats.friction;
            clone.bounciness = teamStats.bounciness;
        }
        else
        {
            Debug.LogWarning($"[Lab_PlayerBuilder] 팀 원본 {shape}Stats.asset을 못 찾아 PlayerShapeStats " +
                              "기본값(전부 0)으로 생성한다 — Base 프리팹 생성(팀 메뉴가 자동 생성)이 먼저 " +
                              "실행됐는지 확인해라.");
        }
        AssetDatabase.CreateAsset(clone, statsPath);

        // [F1 재작업 판정 C1] 예전엔 구만 Discrete였다 — 명세(F1-1 "Rigidbody
        // collisionDetectionMode(구도 ContinuousDynamic)")와 어긋났다("질문 없이 확정·미보고"로
        // 지적받음). 이제 전 도형 ContinuousDynamic·solver 12/4로 통일한다 — 어차피 팀
        // PlayerMover.Awake가 solverVelocityIterations<4면 12/4로 강제하므로(PlayerMover.cs:
        // 287-291) 구만 다르게 둘 이유가 없다.
        Lab_ShapeProfile profile = ScriptableObject.CreateInstance<Lab_ShapeProfile>();
        profile.statsAsset = clone;
        profile.shapeKind = clone.kind;
        profile.constraints = shape == "Sphere" ? RigidbodyConstraints.None : RigidbodyConstraints.FreezeRotation;
        profile.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        profile.solverIterations = 12;
        profile.solverVelocityIterations = 4;
        AssetDatabase.CreateAsset(profile, profilePath);

        Debug.Log($"[Lab_PlayerBuilder] 신규 프로필 생성: {profilePath} (+ {statsPath})");
    }

    private static void EnsureVariant(string shape, GameObject basePrefab)
    {
        string variantPath = $"{Root}/Player_{shape}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) != null) return; // 기존 오버라이드 보존.

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        try
        {
            Lab_ShapeProfileApplier applier = instance.AddComponent<Lab_ShapeProfileApplier>();
            applier.profile = AssetDatabase.LoadAssetAtPath<Lab_ShapeProfile>($"{ProfilesDir}/Lab_{shape}Profile.asset");
            Lab_AntiStuck antiStuck = instance.AddComponent<Lab_AntiStuck>();
            antiStuck.profile = applier.profile;

            GameObject variant = PrefabUtility.SaveAsPrefabAsset(instance, variantPath, out bool success);
            if (!success)
                Debug.LogError($"[Lab_PlayerBuilder] Variant 저장 실패: {variantPath}");
            else
                Debug.Log($"[Lab_PlayerBuilder] 신규 Variant 생성: {variantPath}");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }
}
#endif
