using UnityEditor;
using UnityEngine;

/// <summary>
/// `Tools > DoorSystem > ...` — 문/레버/패드/출구 무게판을 SceneView 중앙에 생성한다.
///
/// [왜 값을 스크립트 기본값이 아니라 씬에서 떠왔나]
/// 실제로 플레이테스트를 통과한 건 씬에 배치된 그 세팅이지 스크립트 기본값이 아니다. 실제로
/// `LeverHead`의 기본값은 returnDelay 5 / returnSpeed 1.5인데 씬은 1 / 0.1로 훨씬 빠르게 되돌아온다.
/// 기본값으로 만들면 "메뉴로 만든 레버가 씬 레버와 다르게 논다"가 되므로, 아래 상수는 전부
/// SampleScene에 배치된 door / lever_pivot / lever_head / lever_body / pad에서 그대로 읽어 왔다.
///
/// [연결은 자동으로]
/// 레버·패드는 문이 없으면 의미가 없다. 개별 생성 시 씬에서 **가장 가까운 문**을 찾아 참조를 꽂아
/// 준다(문이 없으면 안내만 하고 만들지는 않는다 — 어디에 놓을지는 레벨 디자이너의 판단이라
/// 임의로 문을 만들어 두면 정리할 쓰레기가 된다).
/// </summary>
public static class DoorSystemMenuItem
{
    private const string SystemFolder = "Assets/DoorSystem";
    private const string MaterialSavePath = SystemFolder + "/Materials";

    // ── 씬(SampleScene)에서 그대로 떠 온 규격 ───────────────────────────────────────────
    private static readonly Vector3 DoorScale = new Vector3(15.45f, 7.8715f, 1f);
    private const float DoorTargetYOffset = 3f;
    private const float DoorSpeed = 2f;
    // 문은 솔리드 콜라이더(밀 수 있는 판)와 살짝 큰 트리거(플레이어가 끼면 멈추는 판정)를 함께 쓴다.
    private static readonly Vector3 DoorBlockTriggerSize = new Vector3(1.05f, 1.02f, 1.6f);

    private static readonly Vector3 LeverPivotScale = new Vector3(0.21565f, 0.110621125f, 0.22799f);
    // 레버는 -45도(LeverHead가 되돌아가는 기준각)에서 닫힘이다. 씬 인스턴스는 -50도로 남아 있지만
    // 그건 손으로 돌려 둔 잔여값이고, 처음 복귀 때 -45로 스냅한다 — 새로 만드는 건 -45에서 시작한다.
    private const float LeverClosedAngleY = -45f;
    private static readonly Vector3 LeverHeadLocalPos = new Vector3(-8.9449f, 1.1f, -0.69802f);
    // 씬 원본은 z가 -1.209(반전 스케일)인데, 음수 스케일은 렌더 노멀이 뒤집혀 조명이 이상해진다.
    // 크기는 같으므로 양수로 만든다.
    private static readonly Vector3 LeverHeadLocalScale = new Vector3(17.54709f, 2.839943f, 1.2091851f);
    private const float LeverHeadLocalYaw = -3.12f;
    private static readonly Vector3 LeverBodyOffset = new Vector3(0.2f, -0.5835f, 0.4338f);
    private static readonly Vector3 LeverBodyScale = new Vector3(1.3924f, 2.149f, 2.4055f);
    private const float LeverRotateSpeed = 3f;
    private const float LeverMaxAngle = 45f;
    private const float LeverNormalSmoothSpeed = 5f;
    private const float LeverReturnDelay = 1f;
    private const float LeverReturnSpeed = 0.1f;
    private const float LeverPushExitGrace = 0.3f;

    private static readonly Vector3 PadScale = new Vector3(2.628f, 0.58452946f, 2.4965f);
    private const float PadPressDepth = 0.1f;
    private const float PadSpeed = 5f;

    private static readonly Vector3 ExitPlateScale = new Vector3(2f, 0.15f, 2f);

    // 세트로 만들 때 문 기준 상대 배치. 레버와 패드가 문 앞 양옆에 서도록.
    private static readonly Vector3 SetLeverOffset = new Vector3(-4f, -2.5f, -4f);
    private static readonly Vector3 SetPadOffset = new Vector3(4f, -3.6f, -4f);
    private static readonly Vector3 SetPlateOffset = new Vector3(0f, -3.6f, -5f);

    // ── 메뉴 ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/DoorSystem/Create Door")]
    private static void CreateDoorMenu()
    {
        GameObject door = SpawnDoor(SceneOrigin());
        Undo.RegisterCreatedObjectUndo(door, "Create Door");
        Finish(door, "[DoorSystem] 문 생성 완료. 레버나 패드를 만들면 이 문에 자동으로 연결됩니다.");
    }

    [MenuItem("Tools/DoorSystem/Create Lever")]
    private static void CreateLeverMenu()
    {
        Vector3 origin = SceneOrigin();
        doorPhysics door = FindNearestDoor(origin);

        GameObject pivot = SpawnLever(origin, door);
        Undo.RegisterCreatedObjectUndo(pivot, "Create Lever");

        Finish(pivot, door != null
            ? $"[DoorSystem] 레버 생성 완료 — 문 '{door.name}'에 연결했습니다. 플레이어로 레버 머리를 밀면 " +
              "미는 각도에 비례해 문이 올라갑니다."
            : "[DoorSystem] 레버 생성 완료. 씬에 문이 없어 연결하지 못했습니다 — Create Door로 문을 만든 뒤 " +
              "문의 doorPhysics > leverHead에 이 레버의 LeverHead를 꽂으세요.");
    }

    [MenuItem("Tools/DoorSystem/Create Pad")]
    private static void CreatePadMenu()
    {
        Vector3 origin = SceneOrigin();
        doorPhysics door = FindNearestDoor(origin);

        GameObject pad = SpawnPad(origin, door);
        Undo.RegisterCreatedObjectUndo(pad, "Create Pad");

        Finish(pad, door != null
            ? $"[DoorSystem] 패드 생성 완료 — 문 '{door.name}'에 연결했습니다. 밟고 있는 동안 문이 완전히 열립니다."
            : "[DoorSystem] 패드 생성 완료. 씬에 문이 없어 연결하지 못했습니다 — Create Door로 문을 만든 뒤 " +
              "패드의 PadTrigger > doorPhysicsScript에 그 문을 꽂으세요.");
    }

    [MenuItem("Tools/DoorSystem/Create Door Set (Door + Lever + Pad)")]
    private static void CreateDoorSetMenu()
    {
        Vector3 origin = SceneOrigin();

        GameObject door = SpawnDoor(origin);
        doorPhysics dp = door.GetComponent<doorPhysics>();
        GameObject lever = SpawnLever(origin + SetLeverOffset, dp);
        GameObject pad = SpawnPad(origin + SetPadOffset, dp);

        Undo.RegisterCreatedObjectUndo(door, "Create Door Set");
        Undo.RegisterCreatedObjectUndo(lever, "Create Door Set");
        Undo.RegisterCreatedObjectUndo(pad, "Create Door Set");

        Finish(door, "[DoorSystem] 문 세트 생성 완료(문 + 레버 + 패드, 서로 연결됨). 레버는 미는 각도만큼 " +
                     "문을 올리고, 패드는 밟는 동안 완전히 엽니다.");
    }

    [MenuItem("Tools/DoorSystem/Create Exit Weight Plate (+ Door)")]
    private static void CreateExitPlateMenu()
    {
        Vector3 origin = SceneOrigin();

        GameObject door = SpawnDoor(origin);
        GameObject plate = SpawnExitPlate(origin + SetPlateOffset, door.GetComponent<doorPhysics>());

        Undo.RegisterCreatedObjectUndo(door, "Create Exit Weight Plate");
        Undo.RegisterCreatedObjectUndo(plate, "Create Exit Weight Plate");

        Finish(plate, "[DoorSystem] 출구 무게판 + 전용 문 생성 완료(연결됨). 네모(무게 3.0)가 올라서면 " +
                      "레버·패드와 똑같은 방식으로 문이 올라갑니다. 구+세모(2.5)로는 안 열립니다(임계 2.75). " +
                      "한 번 열리면 계속 열려 있습니다(latchOpen) — 판을 누른 네모 자신이 지나가야 하므로. " +
                      "무중력 버블 안에 두면 네모도 1.8이라 안 열리니 주의하세요.");
    }

    // ── 생성 헬퍼 ─────────────────────────────────────────────────────────────────────

    private static GameObject SpawnDoor(Vector3 position)
    {
        GameObject door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = "door";
        door.transform.position = position;
        door.transform.localScale = DoorScale;
        door.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMaterial("Door", new Color(0.55f, 0.4f, 0.3f, 1f));

        // 프리미티브가 달아 준 BoxCollider가 솔리드 판. 그 위에 살짝 큰 트리거를 하나 더 얹어
        // "플레이어가 문틈에 끼면 문이 멈춘다"(doorPhysics.isBlocked) 판정을 한다 — 씬 원본과 같은 구성.
        BoxCollider block = door.AddComponent<BoxCollider>();
        block.isTrigger = true;
        block.size = DoorBlockTriggerSize;

        // doorPhysics.Awake가 isKinematic/useGravity를 강제하지만, 에디터에서 봐도 명확하도록 미리 맞춘다.
        Rigidbody rb = door.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        doorPhysics dp = door.AddComponent<doorPhysics>();
        dp.doorTargetYOffset = DoorTargetYOffset;
        dp.doorSpeed = DoorSpeed;
        return door;
    }

    private static GameObject SpawnLever(Vector3 position, doorPhysics door)
    {
        GameObject pivot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pivot.name = "lever_pivot";
        pivot.transform.position = position;
        pivot.transform.rotation = Quaternion.Euler(0f, LeverClosedAngleY, 0f);
        pivot.transform.localScale = LeverPivotScale;
        pivot.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMaterial("Lever", new Color(0.45f, 0.45f, 0.5f, 1f));

        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.name = "lever_head";
        head.transform.SetParent(pivot.transform, false);
        head.transform.localPosition = LeverHeadLocalPos;
        head.transform.localRotation = Quaternion.Euler(0f, LeverHeadLocalYaw, 0f);
        head.transform.localScale = LeverHeadLocalScale;
        head.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMaterial("LeverHead", new Color(0.8f, 0.35f, 0.3f, 1f));

        LeverHead lh = head.AddComponent<LeverHead>();
        lh.leverPivot = pivot.transform;
        lh.rotateSpeed = LeverRotateSpeed;
        lh.maxAngle = LeverMaxAngle;
        lh.normalSmoothSpeed = LeverNormalSmoothSpeed;
        lh.returnDelay = LeverReturnDelay;
        lh.returnSpeed = LeverReturnSpeed;
        lh.pushExitGrace = LeverPushExitGrace;

        // 받침대. 씬에서도 pivot의 자식이 아니라 옆에 놓인 별개 오브젝트라 그대로 따른다
        // (pivot이 회전하므로 자식으로 붙이면 받침대까지 같이 돌아 버린다).
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "lever_body";
        body.transform.position = position + LeverBodyOffset;
        body.transform.localScale = LeverBodyScale;
        body.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMaterial("Lever", new Color(0.45f, 0.45f, 0.5f, 1f));

        if (door != null) door.leverHead = lh;
        return pivot;
    }

    private static GameObject SpawnPad(Vector3 position, doorPhysics door)
    {
        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = "pad";
        pad.transform.position = position;
        pad.transform.localScale = PadScale;
        pad.GetComponent<Collider>().isTrigger = true; // 밟힘은 트리거 겹침으로 센다
        pad.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMaterial("Pad", new Color(0.35f, 0.7f, 0.9f, 1f));

        PadTrigger pt = pad.AddComponent<PadTrigger>();
        pt.padPressDepth = PadPressDepth;
        pt.padSpeed = PadSpeed;
        pt.doorPhysicsScript = door;
        return pad;
    }

    private static GameObject SpawnExitPlate(Vector3 position, doorPhysics door)
    {
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "ExitWeightPlate";
        plate.transform.position = position;
        plate.transform.localScale = ExitPlateScale;
        plate.GetComponent<Collider>().isTrigger = true;
        plate.GetComponent<Renderer>().sharedMaterial =
            LoadOrCreateMaterial("ExitWeightPlate", new Color(1f, 0.85f, 0.35f, 1f));

        ExitWeightPlate p = plate.AddComponent<ExitWeightPlate>();
        p.targetDoor = door;
        return plate;
    }

    // ── 공통 ─────────────────────────────────────────────────────────────────────────

    private static Vector3 SceneOrigin() =>
        SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

    /// <summary>씬에서 가장 가까운 문을 찾는다. 문이 하나뿐인 씬이 대부분이라 사실상 "그 문"이고,
    /// 여러 개면 방금 만든 위치에서 가까운 쪽을 고른다(로그로 어느 문에 붙었는지 알린다).</summary>
    private static doorPhysics FindNearestDoor(Vector3 from)
    {
        doorPhysics best = null;
        float bestSqr = float.PositiveInfinity;
        foreach (doorPhysics d in Object.FindObjectsOfType<doorPhysics>())
        {
            float sqr = (d.transform.position - from).sqrMagnitude;
            if (sqr >= bestSqr) continue;
            bestSqr = sqr;
            best = d;
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

    // 파이프라인 무관하게 색이 나오도록 Lit → Standard로 폴백한다(다른 기믹 로더와 같은 방식).
    private static Material LoadOrCreateMaterial(string key, Color color)
    {
        string path = $"{MaterialSavePath}/Door_{key}_Mat.mat";
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
