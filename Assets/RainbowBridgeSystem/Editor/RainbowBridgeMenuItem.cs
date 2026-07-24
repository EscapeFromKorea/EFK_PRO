// 이 파일은 반드시 "Editor" 폴더 안에 위치해야 한다.
//    Editor 폴더 밖에 두면 런타임 빌드 시 UnityEditor 참조로 컴파일 에러가 난다.

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 무지개 다리 한 세트를 씬에 생성한다. 발판(RainbowBridgeSwitch) 1개 + 다리 세그먼트 N개
/// (초기 비활성)를 만들고, 발판의 targetObjects 배열에 세그먼트들을 자동 연결한다.
/// 세그먼트 머티리얼은 파이프라인별로 Transparent로 설정해, 런타임 알파 페이드가 화면에 반영되게 한다.
///
/// - Tools > RainbowBridge > Create Bridge Setup : 기본 3개.
/// - Tools > RainbowBridge > Create Bridge Setup (Custom count)... : 개수를 입력받는 작은 창.
///
/// 기존 에디터 세팅 패턴(AccelSystem/ScalingSystem)을 따른다: SceneView 중앙 스폰,
/// Undo 등록, Selection 설정, 렌더 파이프라인 대응 셰이더.
/// </summary>
public static class RainbowBridgeMenuItem
{
    private const string MaterialSavePath = "Assets/RainbowBridgeSystem/Materials";
    private const int DefaultSegmentCount = 3;

    [MenuItem("Tools/RainbowBridge/Create Bridge Setup")]
    private static void CreateDefault() => CreateSetup(DefaultSegmentCount);

    /// <summary>발판 1개 + 세그먼트 segmentCount개를 생성한다. 개수 지정 창에서도 이 메서드를 호출한다.</summary>
    public static void CreateSetup(int segmentCount)
    {
        segmentCount = Mathf.Max(1, segmentCount);
        EnsureMaterialFolder();

        Vector3 origin = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            origin = SceneView.lastActiveSceneView.pivot;

        GameObject root = new GameObject("RainbowBridge_Setup");
        root.transform.position = origin;
        Undo.RegisterCreatedObjectUndo(root, "Create RainbowBridge Setup");

        // 발판(스위치) — 불투명 유지
        GameObject padObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        padObj.name = "RainbowBridge_Switch";
        padObj.transform.SetParent(root.transform);
        padObj.transform.localPosition = Vector3.zero;
        padObj.transform.localScale = new Vector3(1.5f, 0.1f, 1.5f);
        padObj.GetComponent<BoxCollider>().isTrigger = true;
        padObj.GetComponent<Renderer>().sharedMaterial =
            LoadOrCreateMaterial("Switch", new Color(0.9f, 0.9f, 0.2f), transparent: false);

        RainbowBridgeSwitch sw = padObj.AddComponent<RainbowBridgeSwitch>();

        // 다리 세그먼트들 — Transparent 머티리얼(알파 페이드 대응), 초기 collider/renderer 비활성
        Material segMat = LoadOrCreateMaterial("Segment", new Color(0.4f, 0.7f, 1f, 1f), transparent: true);
        GameObject[] segments = new GameObject[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = $"RainbowBridge_Segment_{i}";
            seg.transform.SetParent(root.transform);
            seg.transform.localPosition = new Vector3(0f, 0f, 2f + i * 2f);
            seg.transform.localScale = new Vector3(2f, 0.2f, 2f);
            seg.GetComponent<Renderer>().sharedMaterial = segMat;

            // 실체화 전이므로 꺼둔다(런타임 Start도 다시 보장하지만 씬에서도 통일).
            seg.GetComponent<Collider>().enabled = false;
            seg.GetComponent<Renderer>().enabled = false;

            segments[i] = seg;
        }
        sw.targetObjects = segments;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = padObj;
        Debug.Log($"[RainbowBridge] 발판 1개 + 다리 세그먼트 {segmentCount}개 생성 완료. Player 태그 오브젝트로 발판을 밟아 테스트하세요.");
    }

    private static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/RainbowBridgeSystem"))
            AssetDatabase.CreateFolder("Assets", "RainbowBridgeSystem");
        if (!AssetDatabase.IsValidFolder(MaterialSavePath))
            AssetDatabase.CreateFolder("Assets/RainbowBridgeSystem", "Materials");
    }

    private static Material LoadOrCreateMaterial(string name, Color color, bool transparent)
    {
        string path = $"{MaterialSavePath}/RainbowBridge_{name}_Mat.mat";
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

        if (transparent) ConfigureTransparent(mat);
        EditorUtility.SetDirty(mat);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    /// <summary>
    /// 머티리얼을 알파 블렌딩(Transparent surface)으로 설정한다. 파이프라인마다 프로퍼티/키워드가
    /// 달라 셰이더 이름으로 분기한다. 이 설정이 없으면 알파를 낮춰도 반투명으로 그려지지 않는다.
    /// </summary>
    private static void ConfigureTransparent(Material mat)
    {
        string n = mat.shader != null ? mat.shader.name : "";

        if (n.Contains("Universal Render Pipeline")) // URP Lit
        {
            mat.SetFloat("_Surface", 1f);   // 0 Opaque / 1 Transparent
            mat.SetFloat("_Blend", 0f);     // 0 Alpha
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        else if (n.Contains("HDRP") || n.Contains("HDRenderPipeline") || n.Contains("HighDefinition"))
        {
            mat.SetFloat("_SurfaceType", 1f);
            mat.SetFloat("_BlendMode", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        else // Built-in Standard
        {
            mat.SetFloat("_Mode", 3f);      // Transparent
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
    }

    /// <summary>현재 렌더 파이프라인에 맞는 셰이더를 반환한다(ScalingSystem 세팅과 동일 방식).</summary>
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

        Debug.LogWarning("[RainbowBridge] 렌더 파이프라인에 맞는 셰이더를 찾지 못해 기본 셰이더로 대체합니다.");
        return Shader.Find("Diffuse") ?? Shader.Find("Hidden/InternalErrorShader");
    }
}

/// <summary>세그먼트 개수를 입력받아 무지개 다리 세트를 생성하는 작은 창.</summary>
public class RainbowBridgeCreatorWindow : EditorWindow
{
    private int segmentCount = 3;

    [MenuItem("Tools/RainbowBridge/Create Bridge Setup (Custom count)...")]
    private static void Open()
    {
        RainbowBridgeCreatorWindow win = GetWindow<RainbowBridgeCreatorWindow>(true, "Rainbow Bridge Creator");
        win.minSize = new Vector2(280, 90);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("생성할 다리 세그먼트 개수를 지정하세요.", EditorStyles.wordWrappedLabel);
        segmentCount = Mathf.Max(1, EditorGUILayout.IntField("Segment Count", segmentCount));

        EditorGUILayout.Space();
        if (GUILayout.Button("Create"))
        {
            RainbowBridgeMenuItem.CreateSetup(segmentCount);
            Close();
        }
    }
}
