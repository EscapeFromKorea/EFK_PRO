// 이 파일은 반드시 "Editor" 폴더 안에 위치해야 한다.

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > Hourglass 메뉴로 씬에 몽환의 모래시계 테스트 세트 하나를 생성한다.
/// SlowZone(감속 구역, 반투명 시각 포함) + FallingRockFlip이 붙은 낙석(모래시계 대역) 하나를
/// SceneView 중앙에 배치한다. 세모가 낙석에 몸으로 부딪히면 뒤집히고 감속 구역이 켜지고, 네모는
/// 발동 대신 낙석을 밀어 옮기며, 구는 열린 창으로 통과만 한다(FallingRockFlip의 도형 3역 분담 -
/// 2026-07-30 확정). 그래서 여기서 만드는 낙석은 Rigidbody constraints가 FreezeAll(잠김)로
/// 시작한다 - 네모가 미는 동안만 풀린다. 낙석은 Root(물리 전담) + Hourglass_RockVisual(시각 전담,
/// 콜라이더 없음) 2단으로 만든다 - 뒤집기가 콜라이더까지 회전시켜 플레이어를 튕겨내던 문제 때문이며,
/// 플레이어의 Player_Mesh / Player_MeshVisual 분리와 같은 구조다. includePlayer는
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
        // Root는 물리 전담(Rigidbody + BoxCollider + FallingRockFlip)이고 메쉬는 갖지 않는다.
        // 뒤집기 연출은 아래 시각 자식만 돌리기 때문이다 - 플레이어 계층의
        // Player_Mesh / Player_MeshVisual 분리와 같은 구조(Assets/CLAUDE.md 참고).
        GameObject rock = new GameObject("Hourglass_Rock", typeof(BoxCollider));
        rock.transform.position = origin + new Vector3(0f, 0.5f, 3f);
        Undo.RegisterCreatedObjectUndo(rock, "Create Hourglass");

        BoxCollider rockCol = rock.GetComponent<BoxCollider>();
        rockCol.size = Vector3.one; // 시각 자식(단위 정육면체)과 같은 크기.

        // 시각 자식 - MeshFilter/MeshRenderer만. 콜라이더가 붙으면 뒤집는 동안 그 콜라이더가
        // 플레이어를 훑어 PhysX가 튕겨내므로, 프리미티브가 붙여준 콜라이더는 반드시 제거한다.
        GameObject rockVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rockVisual.name = "Hourglass_RockVisual";
        Object.DestroyImmediate(rockVisual.GetComponent<Collider>());
        rockVisual.transform.SetParent(rock.transform, false); // 로컬 회전 identity에서 시작.

        Rigidbody rockRb = rock.AddComponent<Rigidbody>();
        rockRb.mass = 2f;
        rockRb.drag = 1f; // 살짝 무거운 느낌 - 네모가 밀 때 미끄러지듯 날아가지 않게.
        // 밀림 정책(FallingRockFlip.lockUnlessPushedByCube)의 기본 상태 = 잠김. 런타임에는 Awake가
        // 이 값을 다시 세팅하지만, 씬 파일에도 잠긴 상태로 직렬화해두면 (1) Play 없이 인스펙터를
        // 봐도 정책이 드러나고 (2) 에디터에서 이 오브젝트가 바닥 없는 공중에 놓여도 흘러내리지 않는다.
        // ※ 네모가 밀 때는 위치 3축이 모두 풀린다(레일 없는 자유 이동) - 아래에 바닥이 없으면 밀린
        //    순간 떨어진다. 테스트할 자리에는 바닥을 두어라.
        rockRb.constraints = RigidbodyConstraints.FreezeAll;

        FallingRockFlip flip = rock.AddComponent<FallingRockFlip>();
        var flipSo = new SerializedObject(flip);
        flipSo.FindProperty("targetSlowZone").objectReferenceValue = slowZone;
        flipSo.FindProperty("visualRoot").objectReferenceValue = rockVisual.transform;
        flipSo.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = rock;
        Debug.Log("[Hourglass] 생성 완료. 도형 3역 분담입니다 - 세모(Tetrahedron)로 'Hourglass_Rock'에 몸으로 " +
                  "부딪히면(운동량 = 질량x상대속도 3.0 이상) 뒤집히며 감속 구역이 켜지고, 그 세모에게 " +
                  "모래시계는 밀려나지 않습니다. 네모(Cube)는 발동시키지 못하는 대신 접촉해서 미는 동안 " +
                  "모래시계를 원하는 자리로 옮길 수 있고(손을 떼면 그 자리에서 멈춤), 구(Sphere)는 발동도 " +
                  "밀기도 못하고 열린 창으로 통과만 합니다. 효과 확인은 'FallingDebris'(계속 떨어지는 파편)의 " +
                  "낙하 속도로 - 구역에 들어서면 약 10 U/s에서 0.5초에 걸쳐 눈에 보이게 느려져 하강 상한 " +
                  "2 U/s로 천천히 내려옵니다(중력도 0.5배). 멈춰있는 낙석은 감속 여부가 눈에 안 보입니다. " +
                  "뒤집기는 시각 자식('Hourglass_RockVisual')만 회전하는 연출이라 콜라이더는 제자리입니다 - " +
                  "부딪힌 플레이어가 튕겨나가지 않습니다. 애니메이션으로 대체할 경우 Animator도 이 시각 " +
                  "자식에 붙이세요(Root에 붙이면 콜라이더가 같이 돌아 튕김이 재발합니다).");
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
            // 2 = Fade. Unity가 프로젝트를 열 때 아래 블렌드 값들을 _Mode대로 다시 유도하므로,
            // _Mode와 어긋나는 조합(3 = Transparent는 _ALPHAPREMULTIPLY_ON + SrcBlend One)을 쓰면
            // 에셋이 조용히 덮어써진다. 자세한 근거는 RainbowBridgeMenuItem의 같은 지점 주석 참고.
            mat.SetFloat("_Mode", 2f);      // Fade
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
