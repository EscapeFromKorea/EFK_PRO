// 이 파일은 반드시 프로젝트의 "Editor" 폴더 안에 위치해야 합니다.

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > DestructionSystem > Create Breakable 메뉴로 부서지는 장애물을 즉석 생성한다.
/// PlayerObjectMenuItem/CatapultMenuItem과 같은 패턴(PrimitiveType 즉석 생성, SceneView 중앙
/// 스폰, Undo 등록)을 따른다 — 아트 에셋이 아직 없어(docs/art-pipeline/ 비어 있음) 프리팹 프리셋
/// 대신 PrimitiveType으로 만든다.
///
/// fragmentPrefabs는 항상 비운 채로 만든다 — 레벨 디자이너가 직접 채운다(docs/PRD/Destruction.md
/// §4 참고). "Breakable" 태그는 이 저장소에 아직 없는 커스텀 태그라, 없으면 여기서 새로 만든다.
///
/// [균열 텍스처 — 일반 오브젝트와 시각적으로 구분]
/// 아트 에셋이 없어 크랙 텍스처를 코드로 절차 생성한다 — 밝은 돌 톤 베이스에 어두운 갈라짐 선을
/// 여러 갈래 그려 넣는다. `CatapultMenuItem.LoadOrCreateMaterial`과 같은 idempotent
/// load-or-create 패턴(있으면 그대로 로드, 없으면 한 번만 생성해 에셋으로 저장)이라 매번 다시
/// 그리지 않는다 — 시드를 고정해 재생성해도 같은 패턴이 나온다.
/// </summary>
public static class DestructionMenuItem
{
    private const string BreakableTag = "Breakable";
    private const string SystemFolder = "Assets/DestructionSystem";
    private const string MaterialSavePath = SystemFolder + "/Materials";
    private const string CrackTexturePath = MaterialSavePath + "/Breakable_CrackTexture.png";
    private const string CrackMaterialPath = MaterialSavePath + "/Breakable_Mat.mat";
    private const int CrackTextureSize = 256;

    [MenuItem("Tools/DestructionSystem/Create Breakable/Cube")]
    private static void CreateBreakableCube() => CreateBreakable(PrimitiveType.Cube);

    [MenuItem("Tools/DestructionSystem/Create Breakable/Sphere")]
    private static void CreateBreakableSphere() => CreateBreakable(PrimitiveType.Sphere);

    private static void CreateBreakable(PrimitiveType shape)
    {
        EnsureTagExists(BreakableTag);
        EnsureMaterialFolder();

        Vector3 spawnPos = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            spawnPos = SceneView.lastActiveSceneView.pivot;

        GameObject go = GameObject.CreatePrimitive(shape);
        Undo.RegisterCreatedObjectUndo(go, "Create Breakable");
        go.name = $"Breakable_{shape}";
        go.transform.position = spawnPos;
        go.tag = BreakableTag;

        // CreatePrimitive가 이미 알맞은 콜라이더(BoxCollider/SphereCollider, non-trigger)를
        // 붙여 준다 — 새로 붙일 필요가 없다.
        go.AddComponent<BreakableObject>();

        // 균열 텍스처 머티리얼 — 일반 오브젝트와 한눈에 구분되도록(사용자 요청).
        go.GetComponent<Renderer>().sharedMaterial = LoadOrCreateCrackedMaterial();

        Selection.activeGameObject = go;
        Debug.Log($"[DestructionSystem] '{go.name}' 생성 완료 (fragmentPrefabs 비어 있음 — 레벨 " +
                   "디자이너가 직접 채워야 합니다).");
    }

    /// <summary>
    /// "Breakable"은 Unity 기본 태그가 아니라 이 기믹 전용 커스텀 태그다. TagManager.asset을
    /// SerializedObject로 직접 편집하는 표준 Unity 에디터 패턴을 쓴다 — 이 저장소에 기존 태그
    /// 생성 선례가 없어(다른 기믹은 전부 이미 있는 "Player" 태그만 읽는다) 새로 작성했다.
    /// </summary>
    private static void EnsureTagExists(string tag)
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tagsProp = tagManager.FindProperty("tags");

        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag)
                return;
        }

        tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
        tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
        tagManager.ApplyModifiedProperties();
    }

    private static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder(MaterialSavePath))
            AssetDatabase.CreateFolder(SystemFolder, "Materials");
    }

    /// <summary>있으면 그대로 로드하고, 없을 때만 균열 텍스처를 새로 생성해 머티리얼과 함께
    /// 에셋으로 저장한다(CatapultMenuItem.LoadOrCreateMaterial과 같은 idempotent 패턴).</summary>
    private static Material LoadOrCreateCrackedMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(CrackMaterialPath);
        if (existing != null) return existing;

        Material mat = new Material(ResolveShader()) { mainTexture = LoadOrCreateCrackTexture() };
        AssetDatabase.CreateAsset(mat, CrackMaterialPath);
        return AssetDatabase.LoadAssetAtPath<Material>(CrackMaterialPath);
    }

    private static Texture2D LoadOrCreateCrackTexture()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(CrackTexturePath);
        if (existing != null) return existing;

        Texture2D tex = GenerateCrackTexture(CrackTextureSize);
        File.WriteAllBytes(CrackTexturePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(CrackTexturePath);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(CrackTexturePath);
    }

    /// <summary>밝은 돌 톤 바탕에 어두운 갈라짐 선을 절차적으로 그려 넣는다 — 실제 크랙 아트가
    /// 생기기 전까지 "이건 부술 수 있다"를 한눈에 알려주는 용도라 정교할 필요는 없다. 고정 시드를
    /// 써서 에셋을 지우고 다시 생성해도 매번 같은 패턴이 나온다.</summary>
    private static Texture2D GenerateCrackTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
        var baseColor = new Color(0.72f, 0.70f, 0.65f);
        var crackColor = new Color(0.16f, 0.14f, 0.12f);

        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = baseColor;
        tex.SetPixels(pixels);

        Random.InitState(12345);
        const int crackCount = 5;
        for (int c = 0; c < crackCount; c++)
        {
            Vector2 pos = new Vector2(Random.Range(0, size), Random.Range(0, size));
            float angle = Random.Range(0f, 360f);
            int segments = Random.Range(6, 12);
            for (int s = 0; s < segments; s++)
            {
                angle += Random.Range(-35f, 35f);
                float len = Random.Range(size * 0.05f, size * 0.12f);
                Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                Vector2 next = pos + dir * len;
                DrawLine(tex, pos, next, crackColor);
                pos = next;
            }
        }

        tex.Apply();
        return tex;
    }

    private static void DrawLine(Texture2D tex, Vector2 a, Vector2 b, Color color)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
        for (int i = 0; i <= steps; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, (float)i / steps);
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = Mathf.Clamp((int)p.x + dx, 0, tex.width - 1);
                int y = Mathf.Clamp((int)p.y, 0, tex.height - 1);
                tex.SetPixel(x, y, color);
            }
        }
    }

    /// <summary>현재 렌더 파이프라인에 맞는 셰이더를 반환한다(CatapultMenuItem.ResolveShader과
    /// 같은 방식 — Built-in/URP/HDRP 대응). 이 저장소 관례상 작은 패턴은 컴포넌트마다 각자
    /// 복제한다(CatapultLoadController.cs 상단 주석 참고).</summary>
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

        Debug.LogWarning("[DestructionSystem] 렌더 파이프라인에 맞는 셰이더를 찾지 못해 기본 셰이더로 대체합니다.");
        return Shader.Find("Diffuse") ?? Shader.Find("Hidden/InternalErrorShader");
    }
}
