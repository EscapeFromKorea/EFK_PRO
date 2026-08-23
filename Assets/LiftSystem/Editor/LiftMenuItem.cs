using UnityEditor;
using UnityEngine;

/// <summary>
/// `Tools > LiftSystem > ...` — 구-정사면체 협동 리프트의 패드·리프트를 SceneView 중앙에 생성한다.
/// 확정 사양은 `docs/PRD/Lift.md`, 폴더 설계 배경은 `LiftSystem/CLAUDE.md` 참고.
///
/// [왜 콜라이더 치수를 여기서 또 정의하나 — 값은 하나뿐이다]
/// `LiftPlatform.Reset()`이 이미 지지/센서 콜라이더를 만든다. 여기서 같은 값(2×0.4×2 / 2×0.6×2)을
/// 그대로 반복하는 이유는 `Reset()`이 에디터에서 실제로 도는지 여부와 무관하게 최종 결과가 같아지게
/// 하려는 것이다 — 돌면 그대로 덮어써지고, 안 돌아도 이미 이 값이 들어가 있다. `AccelPadMenuItem`이
/// `AccelPad.Reset()`과 같은 값을 다시 명시하는 것과 같은 이유(저장소 관례).
///
/// [리프트 발판 치수·이동 범위는 전부 TBD]
/// `riseHeight`·발판 크기는 PRD §9·§10이 "레벨 배치 시 결정"이라 명시한 자리값이다. 이 메뉴는
/// 스크립트 기본값을 그대로 쓰고 따로 강제하지 않는다 — 배치 후 인스펙터에서 조정하는 게 맞다.
/// </summary>
public static class LiftMenuItem
{
    private const string SystemFolder = "Assets/LiftSystem";
    private const string MaterialSavePath = SystemFolder + "/Materials";

    // LiftPlatform.Reset()과 동일한 값(단일 정본은 LiftPlatform.cs). 여기서 다시 쓰는 이유는 클래스
    // 주석 참고.
    private static readonly Vector3 SupportSize = new Vector3(2f, 0.4f, 2f);
    private static readonly Vector3 SensorSize = new Vector3(2f, 0.6f, 2f);

    private static readonly Vector3 PadSize = new Vector3(2f, 0.3f, 2f);

    // 세트로 만들 때 리프트 기준 상대 배치 — "같은 공간"(PRD §8)이라 몇 U 옆에 붙인다.
    private static readonly Vector3 SetPadOffset = new Vector3(3f, 0f, 0f);

    [MenuItem("Tools/LiftSystem/Create Lift Platform")]
    private static void CreateLiftPlatformMenu()
    {
        GameObject platform = BuildPlatform(SceneOrigin());
        Undo.RegisterCreatedObjectUndo(platform, "Create Lift Platform");
        Finish(platform, "[LiftSystem] 리프트 생성 완료. 패드를 만들어 LiftPad > Target Lift에 이 " +
                          "LiftPlatform을 꽂으면 홀드하는 동안 위로 오릅니다(riseHeight는 TBD — 레벨에 맞게 조정).");
    }

    [MenuItem("Tools/LiftSystem/Create Lift Pad")]
    private static void CreateLiftPadMenu()
    {
        Vector3 origin = SceneOrigin();
        LiftPlatform lift = FindNearestLift(origin);

        GameObject pad = BuildPad(origin, lift);
        Undo.RegisterCreatedObjectUndo(pad, "Create Lift Pad");

        Finish(pad, lift != null
            ? $"[LiftSystem] 패드 생성 완료 — 리프트 '{lift.name}'에 연결했습니다. 구·정사면체가 밟고 " +
              "있는 동안 리프트가 오르고, 정육면체는 무게 게이트(기본 2.0)에 걸러집니다."
            : "[LiftSystem] 패드 생성 완료. 씬에 리프트가 없어 연결하지 못했습니다 — Create Lift Platform으로 " +
              "리프트를 만든 뒤 패드의 LiftPad > Target Lift에 꽂으세요.");
    }

    [MenuItem("Tools/LiftSystem/Create Lift Set (Pad + Platform)")]
    private static void CreateLiftSetMenu()
    {
        Vector3 origin = SceneOrigin();

        GameObject platform = BuildPlatform(origin);
        GameObject pad = BuildPad(origin + SetPadOffset, platform.GetComponent<LiftPlatform>());

        Undo.RegisterCreatedObjectUndo(platform, "Create Lift Set");
        Undo.RegisterCreatedObjectUndo(pad, "Create Lift Set");

        Finish(platform, "[LiftSystem] 리프트 세트 생성 완료(패드 + 리프트, 서로 연결됨). 구·정사면체가 " +
                         "패드를 밟는 동안 리프트가 오르고, 떼면 원위치로 돌아갑니다. riseHeight·발판 크기는 " +
                         "TBD 자리값이니 레벨에 맞게 인스펙터에서 조정하세요.");
    }

    // ── 생성 헬퍼 ─────────────────────────────────────────────────────────────────────

    private static GameObject BuildPlatform(Vector3 position)
    {
        GameObject platform = new GameObject("LiftPlatform");
        platform.transform.position = position;

        // 솔리드 콜라이더(비-트리거) — 라이더를 실제로 떠받쳐 PlayerGroundContact의 SphereCast가
        // 접지로 잡게 한다.
        BoxCollider support = platform.AddComponent<BoxCollider>();
        support.isTrigger = false;
        support.size = SupportSize;
        support.center = Vector3.zero;

        // 라이더 감지 전용 트리거 — kinematic-kinematic 쌍에서 OnCollision*이 발화하지 않아
        // 반드시 트리거로 감지해야 한다(LiftPlatform.cs 클래스 주석 참고).
        BoxCollider sensor = platform.AddComponent<BoxCollider>();
        sensor.isTrigger = true;
        sensor.size = SensorSize;
        sensor.center = new Vector3(0f, SupportSize.y * 0.5f + SensorSize.y * 0.5f, 0f);

        Rigidbody rb = platform.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate; // 계속 움직이는 판이라 보간(CloudTrampoline 왕복판과 동일 이유)

        LiftPlatform lift = platform.AddComponent<LiftPlatform>();
        lift.riderSensor = sensor;

        // 시각 — 지지 콜라이더 크기와 맞춘 순수 렌더용 큐브(콜라이더 없음).
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "LiftPlatform_Visual";
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(platform.transform, false);
        visual.transform.localScale = SupportSize;
        visual.GetComponent<Renderer>().sharedMaterial =
            LoadOrCreateMaterial("Platform", new Color(0.55f, 0.85f, 0.6f, 1f));

        return platform;
    }

    private static GameObject BuildPad(Vector3 position, LiftPlatform lift)
    {
        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = "LiftPad";
        pad.transform.position = position;
        pad.transform.localScale = PadSize;
        pad.GetComponent<Collider>().isTrigger = true; // 밟힘은 트리거 겹침으로 센다(PadTrigger와 같은 방식)
        pad.GetComponent<Renderer>().sharedMaterial =
            LoadOrCreateMaterial("Pad", new Color(0.3f, 0.8f, 0.9f, 1f));

        LiftPad lp = pad.AddComponent<LiftPad>();
        lp.targetLift = lift;
        return pad;
    }

    // ── 공통 ─────────────────────────────────────────────────────────────────────────

    private static Vector3 SceneOrigin() =>
        SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

    /// <summary>씬에서 가장 가까운 리프트를 찾는다(`DoorSystemMenuItem.FindNearestDoor`와 같은 방식).</summary>
    private static LiftPlatform FindNearestLift(Vector3 from)
    {
        LiftPlatform best = null;
        float bestSqr = float.PositiveInfinity;
        foreach (LiftPlatform l in Object.FindObjectsOfType<LiftPlatform>())
        {
            float sqr = (l.transform.position - from).sqrMagnitude;
            if (sqr >= bestSqr) continue;
            bestSqr = sqr;
            best = l;
        }
        return best;
    }

    private static void Finish(GameObject select, string message)
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = select;
        Debug.Log(message);
    }

    // 파이프라인 무관하게 색이 나오도록 Lit → Standard로 폴백한다(DoorSystemMenuItem과 같은 방식).
    private static Material LoadOrCreateMaterial(string key, Color color)
    {
        string path = $"{MaterialSavePath}/Lift_{key}_Mat.mat";
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
