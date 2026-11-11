using UnityEditor;
using UnityEngine;

/// <summary>
/// `Tools > ProjectileTrapSystem > Create Projectile Launcher` — 발사구 + 예고 표시판을 SceneView
/// 중앙에 그레이박스로 생성한다. 확정 사양은 `docs/PRD/ProjectileTrap.md`, 폴더 설계 배경은
/// `ProjectileTrapSystem/CLAUDE.md` 참고.
///
/// [모든 수치가 그레이박스] W/R/V/L/반경은 PRD §5가 전부 미정으로 남긴 값이다. 스크립트 기본값을
/// 그대로 쓰고 강제하지 않는다 — 배치 후 인스펙터에서 실측 조정한다(PeriodicTrapMenuItem과 같은 방침).
/// </summary>
public static class ProjectileTrapMenuItem
{
    private const string SystemFolder = "Assets/ProjectileTrapSystem";
    private const string MaterialSavePath = SystemFolder + "/Materials";

    [MenuItem("Tools/ProjectileTrapSystem/Create Projectile Launcher")]
    private static void CreateLauncher()
    {
        GameObject go = new GameObject("ProjectileLauncher");
        go.transform.position = SceneOrigin();

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Launcher_Visual";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = new Vector3(0.6f, 0.6f, 0.4f);
        body.GetComponent<Renderer>().sharedMaterial =
            LoadOrCreateMaterial("Body", new Color(0.35f, 0.35f, 0.4f));

        GameObject warningPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
        warningPlane.name = "Warning_Visual";
        Object.DestroyImmediate(warningPlane.GetComponent<Collider>());
        warningPlane.transform.SetParent(go.transform, false);
        warningPlane.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        warningPlane.transform.localScale = Vector3.one * 0.4f;
        warningPlane.GetComponent<Renderer>().sharedMaterial =
            LoadOrCreateMaterial("Warning", Color.white);

        ProjectileWarning warning = go.AddComponent<ProjectileWarning>();
        warning.target = warningPlane.GetComponent<Renderer>();

        ProjectileLauncher launcher = go.AddComponent<ProjectileLauncher>();
        launcher.warning = warning;

        Undo.RegisterCreatedObjectUndo(go, "Create Projectile Launcher");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = go;
        Debug.Log("[ProjectileTrapSystem] 발사구 생성 완료. 씬에서 이 오브젝트를 돌려 조준하세요 " +
                  "(발사 방향 = transform.forward). W/R/V/L/반경은 그레이박스 기본값 — 배치 후 " +
                  "실측해 조정하세요. OnHazardHit을 RespawnController.RespawnPlayer에 배선하세요 " +
                  "(Tools > ProjectileTrapSystem > Wire Hits To Respawn).");
    }

    private static Vector3 SceneOrigin() =>
        SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

    // 파이프라인 무관하게 색이 나오도록 Lit → Standard로 폴백한다(PeriodicTrapMenuItem과 같은 방식).
    private static Material LoadOrCreateMaterial(string key, Color color)
    {
        string path = $"{MaterialSavePath}/ProjectileTrap_{key}_Mat.mat";
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
