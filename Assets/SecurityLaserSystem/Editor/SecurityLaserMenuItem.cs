using UnityEditor;
using UnityEngine;

/// <summary>
/// `Tools > SecurityLaserSystem` — 보안 레이저 발사구를 SceneView 중앙에 그레이박스로 생성한다.
/// CH4는 <see cref="CharacterLockedLaser.targetKinds"/> 필드가 있어 메뉴를 캐릭터별로 나누지 않는다 —
/// 배치 후 인스펙터에서 지정한다(YAGNI). 확정 사양은 `docs/PRD/SecurityLaser.md`, 폴더 설계 배경은
/// `SecurityLaserSystem/CLAUDE.md` 참고.
/// </summary>
public static class SecurityLaserMenuItem
{
    private const string SystemFolder = "Assets/SecurityLaserSystem";
    private const string MaterialSavePath = SystemFolder + "/Materials";

    [MenuItem("Tools/SecurityLaserSystem/Create Character Locked Laser (CH4)")]
    private static void CreateCharacterLockedLaser()
    {
        GameObject go = CreateBase("SecurityLaser_CharacterLocked", out LaserBeam beam);
        CharacterLockedLaser locked = go.AddComponent<CharacterLockedLaser>();
        locked.beam = beam;

        Undo.RegisterCreatedObjectUndo(go, "Create Character Locked Laser");
        Finish(go, "캐릭터별 조준 고정 레이저 생성 완료. targetKinds로 대상 도형 종류(1개 이상)를 " +
                   "지정하세요(현재 조작 캐릭터 참조 금지 — 조준 시점마다 그 중 가장 가까운 인스턴스를 " +
                   "스냅샷 조준합니다).");
    }

    [MenuItem("Tools/SecurityLaserSystem/Create Fixed Periodic Laser (CH5)")]
    private static void CreateFixedPeriodicLaser()
    {
        GameObject go = CreateBase("SecurityLaser_FixedPeriodic", out LaserBeam beam);
        go.AddComponent<FixedPeriodicLaser>().beam = beam;

        Undo.RegisterCreatedObjectUndo(go, "Create Fixed Periodic Laser");
        Finish(go, "고정 방향 주기 반복 레이저 생성 완료. 씬에서 이 오브젝트를 돌려 조준하세요 " +
                   "(발사 방향 = transform.forward).");
    }

    private static GameObject CreateBase(string name, out LaserBeam beam)
    {
        GameObject go = new GameObject(name);
        go.transform.position = SceneOrigin();

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Emitter_Visual";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = new Vector3(0.4f, 0.4f, 0.3f);
        body.GetComponent<Renderer>().sharedMaterial =
            LoadOrCreateMaterial("Body", new Color(0.35f, 0.3f, 0.32f));

        beam = go.AddComponent<LaserBeam>(); // [RequireComponent(LineRenderer)]가 자동으로 딸려온다.
        return go;
    }

    private static void Finish(GameObject go, string message)
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = go;
        Debug.Log($"[SecurityLaserSystem] {message} OnHazardHit을 RespawnController.RespawnPlayer에 " +
                  "배선하세요(Tools > SecurityLaserSystem > Wire Hits To Respawn).");
    }

    private static Vector3 SceneOrigin() =>
        SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

    // 파이프라인 무관하게 색이 나오도록 Lit → Standard로 폴백한다(ProjectileTrapMenuItem과 같은 방식).
    private static Material LoadOrCreateMaterial(string key, Color color)
    {
        string path = $"{MaterialSavePath}/SecurityLaser_{key}_Mat.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(MaterialSavePath))
            AssetDatabase.CreateFolder(SystemFolder, "Materials");

        Shader s = Shader.Find("Universal Render Pipeline/Lit")
                   ?? Shader.Find("HDRP/Lit")
                   ?? Shader.Find("Standard");
        Material mat = new Material(s);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
