// 이 파일은 반드시 "Editor" 폴더 안에 위치해야 한다.
//    Editor 폴더 밖에 두면 런타임 빌드 시 UnityEditor 참조로 컴파일 에러가 난다.

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > CloudTrampoline 메뉴로 씬에 구름 트램펄린 하나를 생성한다.
/// 솔리드 BoxCollider(도약/지지 판) + CloudTrampoline 컴포넌트 + 뭉게구름 시각(흰 구 여러 개)을 만든다.
/// 시각용 구는 콜라이더를 제거해 순수 연출이며, 실제 충돌/도약/지지는 루트 BoxCollider 하나로만 판정한다.
///
/// 구름 머티리얼은 파이프라인별로 Transparent(알파 블렌드)로 설정한다 — 과부하 붕괴 시 CloudTrampoline이
/// 알파를 페이드하는데, 대상 머티리얼이 Transparent가 아니면 반투명 보간이 화면에 안 보이고 팝되기 때문이다.
/// 단, RainbowBridge와 달리 ZWrite는 켜둔다(_ZWrite=1): 구름은 겹쳐 놓은 puff 구들이라 깊이를 안 쓰면
/// 카메라 이동 시 앞뒤 정렬이 오브젝트 단위로 뒤바뀌며 내부 면이 팝된다. 깊이를 쓰면 평상시(알파=1,
/// 사실상 불투명)엔 픽셀 단위로 정확히 가려지고, 페이드 중에도 정렬이 결정적이라 깜빡임이 없다.
///
/// 기존 에디터 세팅 패턴(AccelSystem/RainbowBridgeSystem)을 따른다: SceneView 중앙 스폰,
/// Undo 등록, Selection 설정, 렌더 파이프라인 대응 셰이더.
/// </summary>
public static class CloudTrampolineMenuItem
{
    private const string MaterialSavePath = "Assets/CloudTrampolineSystem/Materials";

    // 뭉게구름을 이루는 흰 구들의 (로컬 위치, 비균일 스케일). 모든 puff의 수평 외곽(중심±반지름)이
    // 루트 BoxCollider footprint(반경 x±1.5, z±1.0, 윗면 y=+0.5) 안에 들어오도록 배치한다 — 시각
    // 경계가 충돌 경계를 넘으면 "구름을 밟았는데 안 튀는" 지점이 생긴다. 구 프리미티브의 월드
    // 반지름은 scale*0.5다.
    // 윗면: 넓고 납작한 타원체 판(크레스트 y≈0.45)으로 콜라이더 윗면과 맞춰 평평하게.
    // 옆·둘레: 윗면 판보다 낮게(top ≤ 0.33) 크기가 제각각인 둥근 혹을 여러 겹 둘러 몽글몽글한 실루엣을
    //          낸다. 낮게 두는 이유는 윗면을 평평하게 유지해 착지면(콜라이더 윗면)과 어긋나지 않게 하기 위함.
    private static readonly (Vector3 pos, Vector3 scale)[] Puffs =
    {
        // 평평한 윗면 판 (납작하게 눌린 타원체, 크레스트가 콜라이더 윗면 근처)
        (new Vector3(0f, 0.05f, 0f), new Vector3(2.6f, 0.8f, 1.7f)),
        (new Vector3(-0.7f, 0.0f, 0.35f), new Vector3(1.4f, 0.7f, 1.2f)),
        (new Vector3(0.7f, 0.0f, -0.35f), new Vector3(1.4f, 0.7f, 1.2f)),
        // 둘레 큰 혹 (네 변)
        (new Vector3(-1.05f, -0.12f, 0f), new Vector3(0.9f, 0.9f, 0.9f)),
        (new Vector3(1.05f, -0.12f, 0f), new Vector3(0.9f, 0.9f, 0.9f)),
        (new Vector3(0f, -0.12f, 0.62f), new Vector3(0.75f, 0.75f, 0.75f)),
        (new Vector3(0f, -0.12f, -0.62f), new Vector3(0.75f, 0.75f, 0.75f)),
        // 모서리 혹 (크기 제각각 → 비대칭 실루엣)
        (new Vector3(-0.95f, -0.15f, 0.55f), new Vector3(0.72f, 0.72f, 0.72f)),
        (new Vector3(0.9f, -0.15f, 0.58f), new Vector3(0.66f, 0.66f, 0.66f)),
        (new Vector3(-0.9f, -0.18f, -0.55f), new Vector3(0.6f, 0.6f, 0.6f)),
        (new Vector3(0.95f, -0.15f, -0.55f), new Vector3(0.7f, 0.7f, 0.7f)),
        // 자잘한 스커트 혹 (가장자리 뭉게뭉게)
        (new Vector3(-1.28f, -0.22f, 0.25f), new Vector3(0.44f, 0.44f, 0.44f)),
        (new Vector3(1.28f, -0.22f, -0.25f), new Vector3(0.44f, 0.44f, 0.44f)),
        (new Vector3(-0.35f, -0.2f, 0.72f), new Vector3(0.52f, 0.52f, 0.52f)),
        (new Vector3(0.4f, -0.2f, -0.72f), new Vector3(0.52f, 0.52f, 0.52f)),
        (new Vector3(0.5f, -0.22f, 0.66f), new Vector3(0.5f, 0.5f, 0.5f)),
        (new Vector3(-0.5f, -0.22f, -0.66f), new Vector3(0.5f, 0.5f, 0.5f)),
    };

    [MenuItem("Tools/CloudTrampoline")]
    private static void CreateCloudTrampoline()
    {
        EnsureMaterialFolder();

        Vector3 origin = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            origin = SceneView.lastActiveSceneView.pivot;

        GameObject cloud = new GameObject("CloudTrampoline", typeof(BoxCollider), typeof(CloudTrampoline));
        cloud.transform.position = origin;
        Undo.RegisterCreatedObjectUndo(cloud, "Create CloudTrampoline");

        // 도약/지지 판 — 솔리드 콜라이더. 윗면이 착지/도약/눌러앉는 면이 된다.
        BoxCollider col = cloud.GetComponent<BoxCollider>();
        col.isTrigger = false;
        col.size = new Vector3(3f, 1f, 2f);

        // 뭉게구름 시각 — 흰 구 여러 개(콜라이더 없음, 순수 연출). Transparent 머티리얼(붕괴 페이드 대응).
        Material mat = LoadOrCreateMaterial(new Color(0.95f, 0.97f, 1f, 1f));
        foreach (var puff in Puffs)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Cloud_Puff";
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(cloud.transform, false);
            sphere.transform.localPosition = puff.pos;
            sphere.transform.localScale = puff.scale;
            sphere.GetComponent<Renderer>().sharedMaterial = mat;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = cloud;
        Debug.Log("[CloudTrampoline] 구름 트램펄린 생성 완료. Player 태그 오브젝트로 구름 위에 착지/점프해 " +
                  "도약을, 무거운 조합(네모+구 등)으로 과부하 붕괴를 테스트하세요.");
    }

    private static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/CloudTrampolineSystem"))
            AssetDatabase.CreateFolder("Assets", "CloudTrampolineSystem");
        if (!AssetDatabase.IsValidFolder(MaterialSavePath))
            AssetDatabase.CreateFolder("Assets/CloudTrampolineSystem", "Materials");
    }

    private static Material LoadOrCreateMaterial(Color color)
    {
        string path = $"{MaterialSavePath}/Cloud_Mat.mat";
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

        ConfigureTransparent(mat);
        EditorUtility.SetDirty(mat);
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    /// <summary>머티리얼을 알파 블렌딩(Transparent surface)으로 설정하되 ZWrite는 켜둔다(_ZWrite=1).
    /// 파이프라인마다 프로퍼티/키워드가 달라 셰이더 이름으로 분기한다. Transparent가 아니면 붕괴 시 알파를
    /// 낮춰도 반투명으로 그려지지 않는다. ZWrite를 켜는 이유(RainbowBridge와의 차이): 구름은 겹친 puff
    /// 구들이라 깊이를 안 쓰면(ZWrite off) 앞뒤 정렬이 오브젝트 단위로만 이뤄져 카메라 이동 시 내부 면이
    /// 깜빡인다. 깊이를 쓰면 알파=1(평상시)엔 완전 불투명하게 픽셀 단위로 가려지고 페이드 중에도 정렬이
    /// 결정적이라 깜빡임이 없다.</summary>
    private static void ConfigureTransparent(Material mat)
    {
        string n = mat.shader != null ? mat.shader.name : "";

        if (n.Contains("Universal Render Pipeline")) // URP Lit
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 1f); // 겹친 puff 깊이 정렬 → 카메라 이동 시 내부 면 팝 방지
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        else if (n.Contains("HDRP") || n.Contains("HDRenderPipeline") || n.Contains("HighDefinition"))
        {
            mat.SetFloat("_SurfaceType", 1f);
            mat.SetFloat("_BlendMode", 0f);
            mat.SetFloat("_ZWrite", 1f); // 겹친 puff 깊이 정렬 → 카메라 이동 시 내부 면 팝 방지
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        else // Built-in Standard
        {
            mat.SetFloat("_Mode", 3f);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 1); // 겹친 puff 깊이 정렬 → 카메라 이동 시 내부 면 팝 방지
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
    }

    /// <summary>현재 렌더 파이프라인에 맞는 셰이더를 반환한다(RainbowBridge/Scaling 세팅과 동일 방식).</summary>
    private static Shader ResolveShader()
    {
        var pipeline = GraphicsSettings.defaultRenderPipeline;
        if (pipeline != null)
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
        else
        {
            Shader s = Shader.Find("Standard");
            if (s != null) return s;
        }

        Debug.LogWarning("[CloudTrampoline] 렌더 파이프라인에 맞는 셰이더를 찾지 못해 기본 셰이더로 대체합니다.");
        return Shader.Find("Diffuse") ?? Shader.Find("Hidden/InternalErrorShader");
    }
}
