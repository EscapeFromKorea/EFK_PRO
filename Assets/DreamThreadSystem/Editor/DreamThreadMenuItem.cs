// 이 파일은 반드시 "Editor" 폴더 안에 위치해야 한다.
//    Editor 폴더 밖에 두면 런타임 빌드 시 UnityEditor 참조로 컴파일 에러가 난다.

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > DreamThread 메뉴로 실타래 테스트 세팅을 만든다.
/// - Create Anchor: ThreadAnchor 하나(빛나는 작은 구 마커, 콜라이더 없음)를 SceneView 중앙에 생성한다.
///   씬에 DreamThreadController가 없으면 함께 만들어(LineRenderer 포함) 바로 테스트할 수 있게 보장한다.
/// - Create Pin Placer (Phase 2): 씬에 ThreadPinPlacer가 없으면 하나 만든다. 세모가 G로 벽에 핀을
///   박아 런타임 앵커를 만드는 컴포넌트(레벨 C용). 핀은 일반 ThreadAnchor라 컨트롤러가 자동 인식한다.
///
/// 앵커는 물리 마커라 콜라이더를 붙이지 않는다(연결은 거리 판정 — ThreadAnchor 주석 참고).
/// 기존 에디터 세팅 패턴(CloudTrampoline/RainbowBridge)을 따른다: SceneView 중앙 스폰, Undo 등록,
/// Selection 설정, 렌더 파이프라인 대응 셰이더.
/// </summary>
public static class DreamThreadMenuItem
{
    private const string SystemFolder = "Assets/DreamThreadSystem";
    private const string MaterialSavePath = SystemFolder + "/Materials";

    [MenuItem("Tools/DreamThread/Create Anchor")]
    private static void CreateAnchor()
    {
        EnsureMaterialFolder();
        EnsureController();

        Vector3 origin = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            origin = SceneView.lastActiveSceneView.pivot;

        GameObject anchorObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        anchorObj.name = "DreamThread_Anchor";
        Object.DestroyImmediate(anchorObj.GetComponent<Collider>()); // 마커일 뿐 — 물리 접촉 없음
        anchorObj.transform.position = origin;
        anchorObj.transform.localScale = Vector3.one * 0.35f;
        anchorObj.GetComponent<Renderer>().sharedMaterial =
            LoadOrCreateMaterial("Anchor", new Color(0.5f, 0.85f, 1f, 1f));
        anchorObj.AddComponent<ThreadAnchor>();

        Undo.RegisterCreatedObjectUndo(anchorObj, "Create DreamThread Anchor");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = anchorObj;
        Debug.Log("[DreamThread] 앵커 생성 완료. 구/세모 플레이어로 앵커 근처(connectRange 안)에서 F를 눌러 매달리고, " +
                  "좌우로 흔들어 진폭을 키운 뒤 F로 놓으세요. 마우스 휠로 실 길이를 조절합니다.");
    }

    [MenuItem("Tools/DreamThread/Create Pin Placer")]
    private static void CreatePinPlacer()
    {
        if (Object.FindObjectOfType<ThreadPinPlacer>() != null)
        {
            Debug.Log("[DreamThread] 씬에 이미 ThreadPinPlacer가 있습니다.");
            return;
        }

        GameObject obj = new GameObject("DreamThreadPinPlacer");
        obj.AddComponent<ThreadPinPlacer>();
        Undo.RegisterCreatedObjectUndo(obj, "Create DreamThread Pin Placer");
        Selection.activeGameObject = obj;
        Debug.Log("[DreamThread] ThreadPinPlacer 생성 완료. 세모를 조작하며 벽을 향해 이동한 뒤 G로 핀을 박으세요. " +
                  "G는 고리 생성 + 세모 벽부착(그 자리에 완전 고정)을 함께 합니다 — 점프로 위로 도약하며 탈착 후 공중 이동, " +
                  "더 높은 벽에서 G 재입력으로 재부착(클라이밍 루프). 고리는 동시 2개까지(3번째는 가장 오래된 것 자동 회수), " +
                  "수동 회수는 전용 키 T(박은 고리 전부 제거). 박은 고리엔 구·세모가 F로 매달립니다.");
    }

    // 씬에 컨트롤러가 없으면 LineRenderer를 포함해 하나 만든다. 이미 있으면 아무 것도 하지 않는다.
    private static void EnsureController()
    {
        if (Object.FindObjectOfType<DreamThreadController>() != null) return;

        GameObject ctrlObj = new GameObject("DreamThreadController");
        DreamThreadController ctrl = ctrlObj.AddComponent<DreamThreadController>();

        LineRenderer line = ctrlObj.GetComponent<LineRenderer>(); // RequireComponent가 이미 추가함
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.widthMultiplier = ctrl.lineWidth;
        line.numCapVertices = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.sharedMaterial = LoadOrCreateLineMaterial();
        line.enabled = false;

        Undo.RegisterCreatedObjectUndo(ctrlObj, "Create DreamThreadController");
        Debug.Log("[DreamThread] 씬에 DreamThreadController를 생성했습니다.");
    }

    private static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder(SystemFolder))
            AssetDatabase.CreateFolder("Assets", "DreamThreadSystem");
        if (!AssetDatabase.IsValidFolder(MaterialSavePath))
            AssetDatabase.CreateFolder(SystemFolder, "Materials");
    }

    private static Material LoadOrCreateMaterial(string name, Color color)
    {
        string path = $"{MaterialSavePath}/DreamThread_{name}_Mat.mat";
        Shader shader = ResolveShader();

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null)
        {
            mat.shader = shader;
            mat.color = color;
        }
        else
        {
            mat = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(mat, path);
        }
        EditorUtility.SetDirty(mat);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    // 실(LineRenderer)용 머티리얼. LineRenderer는 언릿 계열 셰이더가 적합해 파이프라인 무관하게
    // Sprites/Default를 우선 쓴다(없으면 Unlit/Color, 최후엔 Standard).
    private static Material LoadOrCreateLineMaterial()
    {
        string path = $"{MaterialSavePath}/DreamThread_Line_Mat.mat";
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null)
        {
            if (shader != null) mat.shader = shader;
            mat.color = new Color(0.75f, 0.9f, 1f, 1f);
        }
        else
        {
            mat = new Material(shader) { color = new Color(0.75f, 0.9f, 1f, 1f) };
            AssetDatabase.CreateAsset(mat, path);
        }
        EditorUtility.SetDirty(mat);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    /// <summary>현재 렌더 파이프라인에 맞는 셰이더를 반환한다(RainbowBridge/CloudTrampoline 세팅과 동일 방식).</summary>
    private static Shader ResolveShader()
    {
        var pipeline = GraphicsSettings.defaultRenderPipeline;
        if (pipeline == null)
        {
            Shader s = Shader.Find("Standard");
            if (s != null) return s;
        }
        else
        {
            string n = pipeline.GetType().Name;
            if (n.Contains("Universal") || n.Contains("URP"))
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit");
                if (s != null) return s;
            }
            else if (n.Contains("HighDefinition") || n.Contains("HDRP"))
            {
                Shader s = Shader.Find("HDRP/Lit");
                if (s != null) return s;
            }
        }

        Debug.LogWarning("[DreamThread] 렌더 파이프라인에 맞는 셰이더를 찾지 못해 기본 셰이더로 대체합니다.");
        return Shader.Find("Diffuse") ?? Shader.Find("Hidden/InternalErrorShader");
    }
}
