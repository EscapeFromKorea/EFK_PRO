// 이 파일은 반드시 "Editor" 폴더 안에 위치해야 한다.

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > Hourglass 메뉴로 씬에 몽환의 모래시계 테스트 세트 하나를 생성한다.
/// SlowZone(감속 구역, 반투명 시각 포함) + FallingRockFlip이 붙은 낙석(모래시계 대역) 하나를
/// SceneView 중앙에 배치한다. 세모/네모가 낙석에 몸으로 부딪히면 뒤집히고 감속 구역이 켜진다
/// (구는 FallingRockFlip의 도형 게이트가 막는다 - 사양 2장의 도형 분담). includePlayer는
/// 요구사항 기본값(false, 낙석만 감속)을 그대로 따른다 - SlowZone 컴포넌트 자체 기본값을
/// 건드리지 않는다.
///
/// 기존 에디터 세팅 패턴(CloudTrampolineSystem/ZeroGravityBubbleSystem)을 따른다.
/// </summary>
public static class HourglassMenuItem
{
    private const string MaterialSavePath = "Assets/HourglassSystem/Materials";

    [MenuItem("Tools/Hourglass")]
    private static void CreateHourglass()
    {
        EnsureMaterialFolder();

        Vector3 origin = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            origin = SceneView.lastActiveSceneView.pivot;

        // 감속 구역
        GameObject zone = new GameObject("SlowZone_Hourglass", typeof(BoxCollider), typeof(SlowZone));
        zone.transform.position = origin;
        Undo.RegisterCreatedObjectUndo(zone, "Create Hourglass");

        BoxCollider zoneCol = zone.GetComponent<BoxCollider>();
        zoneCol.isTrigger = true;
        zoneCol.size = new Vector3(6f, 6f, 6f);

        SlowZone slowZone = zone.GetComponent<SlowZone>();

        // 감속 구역 시각화 - 반투명 주황 박스 (충돌 없음, 순수 연출).
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual_SlowZone";
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(zone.transform, false);
        visual.transform.localScale = zoneCol.size;
        visual.GetComponent<Renderer>().sharedMaterial = LoadOrCreateZoneMaterial();

        // 검증용 - 계속 떨어지는 파편. 부딪혀서 멈춰있는 Hourglass_Rock은 이미 정지 상태라
        // 감속 효과가 눈에 안 보인다(속도/중력을 줄여도 줄일 게 없음) - 낙하 중인 대상이 있어야
        // 구역 켜졌을 때 실제로 느려지는 게 보인다. Zone의 자식으로 둬서 Zone을 옮기면 같이 따라간다.
        GameObject debris = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        debris.name = "FallingDebris";
        Undo.RegisterCreatedObjectUndo(debris, "Create Hourglass");
        debris.transform.SetParent(zone.transform, false);
        debris.transform.localScale = Vector3.one * 0.6f;
        float debrisTopLocalY = zoneCol.size.y / 2f + 2f;
        // 낙석과 같은 X/Z(구역 정중앙)에 둔다 - 낙석에 부딪히려고 보는 그 시야에 파편도 같이
        // 떨어지는 게 보여야, 카메라를 딴 데로 돌리지 않고 부딪힘+낙하를 한 화면에서 확인 가능하다.
        debris.transform.localPosition = new Vector3(0f, debrisTopLocalY, 0f);

        Rigidbody debrisRb = debris.AddComponent<Rigidbody>();
        debrisRb.mass = 1f;
        // drag는 0(Unity 기본) 그대로 둔다. 감속의 낙하 속도 제한은 SlowZone.maxFallSpeed가 직접
        // 자르므로 대상의 authored drag에 의존하지 않는다 - 씬에 이미 놓인 파편(drag 0)에서도
        // 그대로 동작해야 하기 때문에 일부러 배율 방식을 쓰지 않는다.

        RespawningFallingDebris debrisFall = debris.AddComponent<RespawningFallingDebris>();
        // 구역 상단에서 바닥 살짝 위까지의 낙하 거리. 절대 높이가 아니라 거리로 주므로
        // 나중에 구역을 옮겨도 어긋나지 않는다(시작 위치는 컴포넌트가 Awake에 스스로 읽는다).
        debrisFall.fallDistance = debrisTopLocalY + zoneCol.size.y / 2f - 0.5f;

        // 낙석(모래시계 대역) - 부딪히면 뒤집히고 위 감속 구역을 켠다.
        GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rock.name = "Hourglass_Rock";
        rock.transform.position = origin + new Vector3(0f, 0.5f, 3f);
        Undo.RegisterCreatedObjectUndo(rock, "Create Hourglass");

        Rigidbody rockRb = rock.AddComponent<Rigidbody>();
        rockRb.mass = 2f;
        rockRb.drag = 1f; // 살짝 무거운 느낌 - 부딪혀도 너무 쉽게 날아가지 않게.

        FallingRockFlip flip = rock.AddComponent<FallingRockFlip>();
        var flipSo = new SerializedObject(flip);
        flipSo.FindProperty("targetSlowZone").objectReferenceValue = slowZone;
        flipSo.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = rock;
        Debug.Log("[Hourglass] 생성 완료. 세모/네모로 'Hourglass_Rock'에 몸으로 부딪혀보세요 - 구(Sphere)는 " +
                  "도형 게이트에 막혀 발동되지 않습니다(사양 2장: 세모/네모가 시간을 늦추고 그 틈으로 구가 통과). " +
                  "운동량(질량x상대속도)이 3.0 이상이면 뒤집히고 감속 구역이 켜집니다. 효과 확인은 " +
                  "'FallingDebris'(계속 떨어지는 파편)의 낙하 속도로 - 구역 안에서는 중력 0.5배 + 하강 속도 " +
                  "상한 2 U/s로 일정한 속도로 천천히 내려옵니다(구역 밖 자유낙하는 약 10 U/s). 멈춰있는 " +
                  "낙석은 감속 여부가 눈에 안 보입니다.");
    }

    private static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/HourglassSystem"))
            AssetDatabase.CreateFolder("Assets", "HourglassSystem");
        if (!AssetDatabase.IsValidFolder(MaterialSavePath))
            AssetDatabase.CreateFolder("Assets/HourglassSystem", "Materials");
    }

    private static Material LoadOrCreateZoneMaterial()
    {
        string path = $"{MaterialSavePath}/SlowZone_Mat.mat";
        Material cached = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (cached != null) return cached;

        Shader shader = ResolveShader();
        Material mat = new Material(shader) { name = "SlowZone_Mat" };
        ConfigureTransparent(mat, new Color(1f, 0.5f, 0.15f, 0.18f));

        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>머티리얼을 알파 블렌딩(Transparent surface)으로 설정한다. 파이프라인마다
    /// 프로퍼티/키워드가 달라 셰이더 이름으로 분기한다(CloudTrampoline 세팅과 동일 방식).</summary>
    private static void ConfigureTransparent(Material mat, Color color)
    {
        string n = mat.shader != null ? mat.shader.name : "";
        mat.color = color;

        if (n.Contains("Universal Render Pipeline"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        else if (n.Contains("HDRP") || n.Contains("HighDefinition"))
        {
            mat.SetFloat("_SurfaceType", 1f);
            mat.SetFloat("_BlendMode", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        else // Built-in Standard
        {
            mat.SetFloat("_Mode", 3f);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
    }

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

        Debug.LogWarning("[Hourglass] 렌더 파이프라인에 맞는 셰이더를 찾지 못해 기본 셰이더로 대체합니다.");
        return Shader.Find("Diffuse") ?? Shader.Find("Hidden/InternalErrorShader");
    }
}
