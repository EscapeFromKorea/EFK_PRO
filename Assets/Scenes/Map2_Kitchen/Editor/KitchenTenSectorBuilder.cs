#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Map2 새 기획서의 10섹터 저폴리 플레이 씬 생성기.
/// 기존 TeamKitchen.unity는 보존하고 별도 씬에 한 번만 굽는다.
/// 좌표계는 Unity 월드(x, 높이 y, 깊이 z), 1U = 1m이다.
/// </summary>
public static class KitchenTenSectorBuilder
{
    public const string ScenePath = "Assets/Scene/Map2_Kitchen/Map2_Kitchen_Scene.unity";
    private const string MatDir = "Assets/Scenes/Map2_Kitchen/Materials/TenSector";
    private static readonly Vector3[] Stops = {
        new Vector3(12, 0, 12), new Vector3(34, 0, 12),
        new Vector3(56, 2, 12), new Vector3(78, 4, 12),
        new Vector3(96, 8, 30), new Vector3(78, 10, 50),
        new Vector3(56, 10, 50), new Vector3(34, 14, 50),
        new Vector3(12, 18, 68), new Vector3(40, 22, 78),
        new Vector3(55, 22, 94)
    };
    private static readonly string[] Names = {
        "레시피와 부스러기", "세제 타일", "서랍 승강장", "싱크볼 거품섬",
        "수도꼭지 실다리", "도마 공사장", "태엽 프라이팬", "팬트리 낙하물",
        "냉장고 투석기", "환기 후드와 창문"
    };

    private static Transform root;
    private static Transform[] sectors;
    private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    [MenuItem("Tools/Kitchen Map2/Build 10 Sector Low Poly Scene")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("플레이 모드에서 씬을 생성할 수 없습니다.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EnsureMaterials();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        root = new GameObject("Kitchen_10Sector_Level").transform;
        sectors = new Transform[10];

        BuildRoom();
        for (int i = 0; i < 10; i++) BuildSectorBase(i);
        for (int i = 0; i < 10; i++)
        {
            if (i == 4) BuildAnchorCrossing();
            else Link(Stops[i], Stops[i + 1], "Route_" + (i + 1).ToString("00") + "_" + (i + 2).ToString("00"));
            BuildGatherGate(i);
        }

        BuildS1(); BuildS2(); BuildS3(); BuildS4(); BuildS5();
        BuildS6(); BuildS7(); BuildS8(); BuildS9(); BuildS10();
        BuildGoal();
        BuildPlayersAndRecovery();
        BuildLighting();

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException("10섹터 씬 저장에 실패했습니다.");
        AssetDatabase.SaveAssets();
        if (!Validate()) throw new InvalidOperationException("10섹터 정적 검증에 실패했습니다.");
        Debug.Log("[Kitchen10] 10섹터 저폴리 씬 저장 완료: " + ScenePath);
    }

    [MenuItem("Tools/Kitchen Map2/Validate 10 Sector Scene")]
    public static bool Validate()
    {
        var errors = new List<string>();
        Transform level = GameObject.Find("Kitchen_10Sector_Level")?.transform;
        if (level == null) { Debug.LogError("[Kitchen10] 레벨 루트 없음"); return false; }
        Transform[] all = level.GetComponentsInChildren<Transform>(true);
        for (int i = 1; i <= 10; i++)
        {
            if (Array.Find(all, t => t.name.StartsWith("Sector_" + i.ToString("00") + "_")) == null)
                errors.Add("섹터 " + i + " 누락");
            if (Array.Find(all, t => t.name == "Gate_" + i.ToString("00") + "_AllThree") == null)
                errors.Add("합류 관문 " + i + " 누락");
        }
        if (level.GetComponentsInChildren<ExitWeightPlate>(true).Length != 10) errors.Add("3인 합류판은 10개여야 합니다.");
        if (level.GetComponentsInChildren<ThreadAnchor>(true).Length != 6) errors.Add("실 앵커는 6개여야 합니다.");
        if (level.GetComponentsInChildren<ThreadBridge>(true).Length != 5) errors.Add("연속 줄다리는 5경간이어야 합니다.");
        if (level.GetComponentsInChildren<RailCart>(true).Length != 0) errors.Add("레일카가 있습니다.");
        if (level.GetComponentsInChildren<RespawnZone>(true).Length != 10) errors.Add("체크포인트는 10개여야 합니다.");
        if (level.GetComponentsInChildren<KitchenTeamGoal>(true).Length != 1) errors.Add("전원 목표 트리거가 없습니다.");
        for (int i = 0; i < 10; i++)
        {
            float grade = Mathf.Abs(Stops[i + 1].y - Stops[i].y) /
                Vector2.Distance(new Vector2(Stops[i].x, Stops[i].z), new Vector2(Stops[i + 1].x, Stops[i + 1].z));
            if (grade > 0.58f) errors.Add("연결 경사 초과: " + (i + 1) + "→" + (i + 2));
        }
        foreach (ExitWeightPlate plate in level.GetComponentsInChildren<ExitWeightPlate>(true))
            if (plate.exitBarriers == null || plate.exitBarriers.Length != 1 || plate.exitBarriers[0] == null)
                errors.Add("무게판 배선 누락: " + plate.name);
        foreach (Transform t in all)
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                errors.Add("Missing Script: " + t.name);
        // 편집 모드에서도 고정 지형의 중심선을 1U 간격으로 레이캐스트해 실질적인 바닥 연속성을 확인한다.
        Physics.SyncTransforms();
        for (int i = 0; i < 10; i++)
        {
            if (i == 4)
            {
                ThreadBridge[] spans = sectors != null && sectors[4] != null
                    ? sectors[4].GetComponentsInChildren<ThreadBridge>()
                    : level.GetComponentsInChildren<ThreadBridge>();
                foreach (ThreadBridge span in spans)
                    if (span.anchorA == null || span.anchorB == null ||
                        Vector3.Distance(span.anchorA.transform.position, span.anchorB.transform.position) > span.maxSpan)
                        errors.Add("S5 줄다리 앵커 거리/배선 오류: " + span.name);
                continue;
            }
            Vector3 from = Stops[i], to = Stops[i + 1];
            int samples = Mathf.CeilToInt(Vector3.Distance(from, to));
            float horizontalLength = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
            for (int n = 0; n <= samples; n++)
            {
                float progress = (float)n / samples;
                Vector3 expected = Vector3.Lerp(from, to, progress);
                float rampProgress = Mathf.Clamp01((horizontalLength * progress - 6.4f) / (horizontalLength - 12.8f));
                expected.y = Mathf.Lerp(from.y, to.y, rampProgress);
                RaycastHit[] hits = Physics.RaycastAll(expected + Vector3.up * 5f, Vector3.down, 6.5f,
                    ~0, QueryTriggerInteraction.Ignore);
                bool supported = false;
                foreach (RaycastHit hit in hits)
                {
                    if (!hit.collider.name.StartsWith("Walkable_Deck_") && hit.collider.name != "Continuous_Ramp" &&
                        hit.collider.name != "Window_Safe_Floor") continue;
                    if (Mathf.Abs(hit.point.y - expected.y) <= .6f) { supported = true; break; }
                }
                if (!supported) { errors.Add("지형 단절: " + (i + 1) + "→" + (i + 2) + " t=" + n + "/" + samples); break; }
            }
        }
        if (errors.Count > 0) { Debug.LogError("[Kitchen10] " + string.Join(" | ", errors)); return false; }
        Debug.Log("[Kitchen10] 정적 검증 통과: 10섹터, 합류판 10, CP 10, 앵커 6, 줄다리 5, 목표 1, 레일카 0. 실제 입력 완주는 별도 검증 필요.");
        return true;
    }

    [MenuItem("Tools/Kitchen Map2/Render Overview Preview")]
    public static void RenderPreview() => RenderFrom(new Vector3(55, 0, 48), 55f, "Kitchen10_overview.png");

    [MenuItem("Tools/Kitchen Map2/Render S5 Preview")]
    public static void RenderS5Preview() => RenderFrom(new Vector3(87, 0, 40), 18f, "Kitchen10_S5.png");

    private static void RenderFrom(Vector3 focus, float size, string fileName)
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject cameraObject = new GameObject("Kitchen10_Preview_Camera", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = size;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.67f, .76f, .76f);
        camera.transform.position = focus + Vector3.up * 120f;
        camera.transform.LookAt(focus, Vector3.forward);
        RenderTexture target = new RenderTexture(1600, 1000, 24);
        RenderTexture previous = RenderTexture.active;
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        Texture2D capture = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        capture.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
        capture.Apply();
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, fileName);
        File.WriteAllBytes(path, capture.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(capture);
        UnityEngine.Object.DestroyImmediate(cameraObject);
        Debug.Log("[Kitchen10] 조감 렌더 저장: " + path);
    }

    private static void EnsureMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Scenes/Map2_Kitchen/Materials", "TenSector");
        AddMat("Cream", new Color(.89f, .81f, .65f));
        AddMat("Tile", new Color(.69f, .78f, .77f));
        AddMat("Wood", new Color(.53f, .32f, .17f));
        AddMat("WoodLight", new Color(.71f, .50f, .29f));
        AddMat("Metal", new Color(.48f, .56f, .61f));
        AddMat("Dark", new Color(.13f, .20f, .24f));
        AddMat("Glass", new Color(.30f, .70f, .79f));
        AddMat("Foam", new Color(.85f, .95f, .92f));
        AddMat("Sugar", new Color(.94f, .85f, .65f));
        AddMat("Tomato", new Color(.84f, .26f, .19f));
        AddMat("Orange", new Color(1f, .55f, .15f));
        AddMat("Guide", new Color(.08f, .91f, .78f));
        AddMat("Goal", new Color(1f, .83f, .23f));
        AddMat("Soap", new Color(.33f, .70f, .91f));
    }

    private static void AddMat(string name, Color color)
    {
        string path = MatDir + "/Kitchen10_" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = "Kitchen10_" + name, color = color };
            AssetDatabase.CreateAsset(mat, path);
        }
        mats[name] = mat;
    }

    private static Transform Group(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static GameObject Primitive(Transform parent, PrimitiveType shape, string name,
        Vector3 position, Vector3 scale, string material, bool solid = true)
    {
        GameObject go = GameObject.CreatePrimitive(shape);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mats[material];
        if (!solid) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale,
        string material, bool solid = true) => Primitive(parent, PrimitiveType.Cube, name, position, scale, material, solid);

    private static void Text(Transform parent, string value, Vector3 position, float size, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(90, 0, 0);
        TextMesh text = go.AddComponent<TextMesh>();
        text.text = value;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.characterSize = size;
        text.fontSize = 64;
        text.color = new Color(.05f, .22f, .25f);
    }

    private static void BuildRoom()
    {
        Transform room = Group(root, "Kitchen_Room");
        Box(room, "Porcelain_Tile_Floor", new Vector3(55, -5.2f, 48), new Vector3(112, .5f, 98), "Tile");
        Box(room, "West_Wall", new Vector3(-1, 13, 48), new Vector3(1, 36, 98), "Cream");
        Box(room, "East_Wall", new Vector3(111, 13, 48), new Vector3(1, 36, 98), "Cream");
        Box(room, "South_Wall", new Vector3(55, 13, -1), new Vector3(112, 36, 1), "Cream");
        Box(room, "North_Wall_L", new Vector3(18, 13, 96), new Vector3(38, 36, 1), "Cream");
        Box(room, "North_Wall_R", new Vector3(90, 13, 96), new Vector3(42, 36, 1), "Cream");
        Box(room, "Window_Lower", new Vector3(55, 8, 96), new Vector3(32, 14, 1), "Cream");
        Box(room, "Window_Upper", new Vector3(55, 32, 96), new Vector3(32, 6, 1), "Cream");
        // 조리대와 가구는 이동 통로보다 아래 또는 옆에 놓아 카메라와 콜라이더를 막지 않는다.
        Box(room, "South_Cabinet_Run", new Vector3(55, -2.4f, 26), new Vector3(76, 5, 7), "Wood");
        Box(room, "West_Cabinet_Run", new Vector3(7, 3, 46), new Vector3(8, 16, 36), "Wood");
        for (int x = 8; x <= 104; x += 8)
            for (int z = 7; z <= 88; z += 8)
                Box(room, "Tile_Grout_" + x + "_" + z, new Vector3(x, -4.93f, z),
                    new Vector3(.08f, .015f, 7.7f), "Cream", false);
    }

    private static void BuildSectorBase(int i)
    {
        Vector3 p = Stops[i];
        Transform s = Group(root, "Sector_" + (i + 1).ToString("00") + "_" + Names[i]);
        sectors[i] = s;
        string floor = i == 1 || i == 3 ? "Tile" : i == 4 || i == 6 || i == 8 ? "Metal" : "WoodLight";
        Box(s, "Walkable_Deck_" + (i + 1).ToString("00"), p - Vector3.up * .3f,
            new Vector3(14, .6f, 14), floor);
        // 섹터 번호는 플레이어 시점에서도 보이는 바닥 인쇄와 입구 측 세로 표지 두 군데에 둔다.
        Text(s, (i + 1).ToString("00"), p + new Vector3(-4.9f, .05f, -4.8f), .8f, "Sector_Number_Floor");
        Box(s, "Sector_Sign_Pole", p + new Vector3(-6, 2, -6), new Vector3(.2f, 4, .2f), "Dark", false);
        Box(s, "Sector_Sign", p + new Vector3(-6, 3.6f, -6), new Vector3(2.6f, 1.6f, .3f), "Orange", false);
        Checkpoint(s, i, p);
    }

    private static void Link(Vector3 from, Vector3 to, string name)
    {
        Transform route = Group(root, name);
        Vector3 flat = new Vector3(to.x - from.x, 0, to.z - from.z).normalized;
        Vector3 a = from + flat * 6.4f;
        Vector3 b = to - flat * 6.4f;
        Vector3 d = b - a;
        Quaternion q = Quaternion.LookRotation(d.normalized, Vector3.up);
        GameObject deck = Box(route, "Continuous_Ramp", (a + b) * .5f - q * Vector3.up * .3f,
            new Vector3(5.6f, .6f, d.magnitude + .8f), "WoodLight");
        deck.transform.rotation = q;
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject rail = Box(route, side < 0 ? "Rail_L" : "Rail_R",
                (a + b) * .5f + q * new Vector3(side * 2.9f, .45f, 0),
                new Vector3(.18f, .9f, d.magnitude), "Metal");
            rail.transform.rotation = q;
        }
        int dots = Mathf.Max(2, Mathf.CeilToInt(d.magnitude / 2f));
        for (int n = 0; n <= dots; n++)
        {
            Vector3 pos = Vector3.Lerp(a, b, (float)n / dots) + Vector3.up * .09f;
            Primitive(route, PrimitiveType.Cylinder, "Route_Dot", pos,
                new Vector3(.28f, .025f, .28f), "Guide", false);
        }
    }

    private static void BuildGatherGate(int i)
    {
        Transform s = sectors[i];
        Vector3 p = Stops[i], q = Stops[i + 1];
        Vector3 dir = new Vector3(q.x - p.x, 0, q.z - p.z).normalized;
        Quaternion facing = Quaternion.LookRotation(dir, Vector3.up);
        GameObject barrier = Box(s, "Gate_" + (i + 1).ToString("00") + "_AllThree",
            p + dir * 5.8f + Vector3.up * 1.65f,
            new Vector3(5.8f, 3.3f, .45f), "Dark");
        barrier.transform.rotation = facing;
        GameObject plateObj = Box(s, "AllThree_Plate_" + (i + 1).ToString("00"),
            p + dir * 2.0f + Vector3.up * .11f, new Vector3(4.8f, .18f, 4.4f), "Goal");
        plateObj.transform.rotation = facing;
        plateObj.GetComponent<BoxCollider>().isTrigger = true;
        ExitWeightPlate plate = plateObj.AddComponent<ExitWeightPlate>();
        plate.requiredWeight = 5.2f; // 구1.5 + 세모1 + 네모3 = 5.5; 세 명 모두 정상 크기여야 열린다.
        plate.latchOpen = true;
        plate.exitBarriers = new[] { barrier };
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject funnel = Box(s, "Gate_Funnel_" + side,
                p + facing * new Vector3(side * 3.0f, 1.2f, 4.1f),
                new Vector3(.25f, 2.4f, 6f), "Metal");
            funnel.transform.rotation = facing;
        }
        Text(s, "3", p + dir * 2.0f + Vector3.up * .23f, .55f, "Team_Plate_Mark");
    }

    private static void Checkpoint(Transform parent, int index, Vector3 p)
    {
        GameObject zone = new GameObject("CP_" + (index + 1).ToString("00"), typeof(BoxCollider), typeof(RespawnZone));
        zone.transform.SetParent(parent, false);
        zone.transform.position = p + new Vector3(-2.7f, .02f, 2.6f);
        BoxCollider box = zone.GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(3.3f, 2.8f, 3.3f);
        box.center = new Vector3(0, 1.4f, 0);
        Primitive(zone.transform, PrimitiveType.Cylinder, "Checkpoint_Light", zone.transform.position + Vector3.up * .05f,
            new Vector3(.6f, .05f, .6f), "Guide", false);
        Box(zone.transform, "Checkpoint_Flag", zone.transform.position + new Vector3(-1.5f, 1.7f, 0),
            new Vector3(.8f, .4f, .08f), "Goal", false);
    }

    private static void BuildS1()
    {
        Transform s = sectors[0]; Vector3 p = Stops[0];
        for (int n = 0; n < 3; n++)
            Box(s, "BreadCrumb_Step_" + (n + 1), p + new Vector3(-3 + n * 2.8f, (.4f + n * .3f) * .5f, 3.3f),
                new Vector3(2.3f, .4f + n * .3f, 2.1f), "Sugar");
        Pad<AccelPad>(s, "S1_AccelPad", p + new Vector3(0, .13f, -2.3f), "Guide").boostSpeed = 6f;
        Pad<JumpPad>(s, "S1_JumpPad", p + new Vector3(3, .13f, 3.2f), "Orange").jumpHeight = 1.7f;
        Box(s, "Recipe_Paper", p + new Vector3(-3, .06f, -3), new Vector3(3, .08f, 3.5f), "Cream", false);
    }

    private static void BuildS2()
    {
        Transform s = sectors[1]; Vector3 p = Stops[1];
        GameObject spill = Box(s, "Soap_Spill_StickerSurface", p + new Vector3(0, .025f, -3.2f),
            new Vector3(8, .05f, 2), "Soap");
        spill.AddComponent<StickerSurface>();
        Pad<AccelPad>(s, "S2_AccelPad", p + new Vector3(-3, .13f, -2.8f), "Guide").boostSpeed = 5f;
        for (int n = 0; n < 4; n++)
            Primitive(s, PrimitiveType.Sphere, "Soap_Bubble", p + new Vector3(-4 + n * 2.5f, 1.2f, -5.8f),
                Vector3.one * (.6f + .15f * n), "Foam", false);
        Primitive(s, PrimitiveType.Cylinder, "Dish_Soap_Bottle", p + new Vector3(4.8f, 1.8f, 3.7f),
            new Vector3(1, 1.8f, 1), "Soap");
    }

    private static void BuildS3()
    {
        Transform s = sectors[2]; Vector3 p = Stops[2];
        for (int n = 0; n < 3; n++)
        {
            Box(s, "Drawer_" + n, p + new Vector3(-4 + n * 3.6f, .7f + n * .3f, 4.5f),
                new Vector3(3, 1.4f + n * .6f, 2.7f), "Wood");
            Box(s, "Drawer_Handle_" + n, p + new Vector3(-4 + n * 3.6f, 1.0f + n * .6f, 3.08f),
                new Vector3(1.5f, .2f, .2f), "Metal", false);
        }
        GameObject liftObj = Box(s, "S3_LiftPlatform", p + new Vector3(3.6f, .1f, -3.4f),
            new Vector3(4.6f, .25f, 4.6f), "Metal");
        Rigidbody body = liftObj.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        BoxCollider sensor = liftObj.AddComponent<BoxCollider>(); sensor.isTrigger = true;
        sensor.size = new Vector3(1, 2, 1); sensor.center = new Vector3(0, 1.1f, 0);
        LiftPlatform lift = liftObj.AddComponent<LiftPlatform>(); lift.riseHeight = 2f; lift.riderSensor = sensor;
        GameObject padObj = Box(s, "S3_LightPad", p + new Vector3(-3.2f, .1f, -2.9f),
            new Vector3(2.4f, .17f, 2.4f), "Goal");
        padObj.GetComponent<BoxCollider>().isTrigger = true;
        padObj.AddComponent<LiftPad>().targetLift = lift;
        Box(s, "Lift_Upper_Landing", p + new Vector3(3.6f, 1.8f, 4.9f), new Vector3(5, .4f, 4), "WoodLight");
    }

    private static void BuildS4()
    {
        Transform s = sectors[3]; Vector3 p = Stops[3];
        Primitive(s, PrimitiveType.Cylinder, "Sink_Basin_Rim", p + new Vector3(0, -.55f, 4.8f),
            new Vector3(5.6f, .25f, 5.6f), "Metal", false);
        Primitive(s, PrimitiveType.Cylinder, "Sink_Water", p + new Vector3(0, -.42f, 4.8f),
            new Vector3(5.1f, .04f, 5.1f), "Soap", false);
        GameObject shrink = Box(s, "S4_ShrinkPad", p + new Vector3(-4, .11f, -2.8f),
            new Vector3(1.6f, .18f, 1.6f), "Tomato");
        shrink.GetComponent<BoxCollider>().isTrigger = true;
        shrink.AddComponent<ScalePad>().padType = ScalePad.EPadType.Shrink;
        GameObject restore = Box(s, "S4_RestorePad", p + new Vector3(3.4f, .11f, -2.8f),
            new Vector3(1.6f, .18f, 1.6f), "Guide");
        restore.GetComponent<BoxCollider>().isTrigger = true;
        // 같은 Shrink 패드를 다시 밟으면 Normal로 돌아간다. Grow는 Shrunk→Grown으로 바뀐다.
        restore.AddComponent<ScalePad>().padType = ScalePad.EPadType.Shrink;
        GameObject cloud = Box(s, "S4_CloudTrampoline", p + new Vector3(-3, .11f, 3.8f),
            new Vector3(3.4f, .25f, 2.6f), "Foam");
        cloud.AddComponent<CloudTrampoline>().baseBounceHeight = 2.2f;
        GameObject bubble = new GameObject("S4_ZeroGravityBubble", typeof(BoxCollider), typeof(ZeroGravityBubble));
        // 합류판과 겹치면 중력 배율이 낮아져 세 명의 무게가 판정값에 못 미친다.
        bubble.transform.SetParent(s, false); bubble.transform.position = p + new Vector3(-3.5f, 2.5f, 4f);
        BoxCollider bubbleCol = bubble.GetComponent<BoxCollider>(); bubbleCol.isTrigger = true;
        bubbleCol.size = new Vector3(4, 5, 4);
        Pad<JumpPad>(s, "S4_JumpPad", p + new Vector3(4.8f, .13f, 4.6f), "Orange").jumpHeight = 2.4f;
    }

    private static void BuildAnchorCrossing()
    {
        Transform s = sectors[4];
        Vector3 from = Stops[4], to = Stops[5];
        Vector3 flat = new Vector3(to.x - from.x, 0, to.z - from.z).normalized;
        Vector3 a = from + flat * 6.4f;
        Vector3 b = to - flat * 6.4f;
        ThreadAnchor[] anchors = new ThreadAnchor[6];
        GameObject controllerObj = new GameObject("DreamThreadController", typeof(DreamThreadController));
        controllerObj.transform.SetParent(root, false);
        LineRenderer controllerLine = controllerObj.GetComponent<LineRenderer>();
        controllerLine.positionCount = 2; controllerLine.useWorldSpace = true;
        controllerLine.sharedMaterial = mats["Guide"]; controllerLine.enabled = false;
        for (int n = 0; n < 6; n++)
        {
            Vector3 pos = Vector3.Lerp(a, b, n / 5f);
            // ThreadBridge는 앵커 높이 자체를 보행면으로 쓰므로 데크와 같은 높이에 둔다.
            GameObject ring = Primitive(s, PrimitiveType.Sphere, "Existing_Anchor_" + (n + 1).ToString("00"),
                pos + Vector3.up * .12f, Vector3.one * .5f, "Orange", false);
            anchors[n] = ring.AddComponent<ThreadAnchor>();
            anchors[n].connectRange = 4f;
            GameObject bridgeObj = null;
            if (n == 0) continue;
            bridgeObj = new GameObject("ThreadBridge_" + n.ToString("00"), typeof(ThreadBridge));
            bridgeObj.transform.SetParent(s, false);
            ThreadBridge bridge = bridgeObj.GetComponent<ThreadBridge>();
            bridge.anchorA = anchors[n - 1]; bridge.anchorB = anchors[n];
            bridge.segmentWidth = 4.6f; bridge.baseSag = .12f; bridge.maxSag = .65f;
            bridge.maxSpans = 1;
            LineRenderer line = bridgeObj.GetComponent<LineRenderer>();
            line.sharedMaterial = mats["Guide"]; line.widthMultiplier = .08f;
            line.useWorldSpace = true;
        }
        // 줄이 생성되기 전 에디터에서도 건널 위치를 읽을 수 있는 비충돌 장식.
        Vector3 delta = b - a;
        GameObject visual = Box(s, "Thread_Path_Visual_Only", (a + b) * .5f - Vector3.up * .7f,
            new Vector3(4.5f, .08f, delta.magnitude), "WoodLight", false);
        visual.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
    }

    private static void BuildS5()
    {
        Transform s = sectors[4]; Vector3 p = Stops[4];
        Primitive(s, PrimitiveType.Cylinder, "Faucet_Base", p + new Vector3(-5, 1.5f, 4.5f),
            new Vector3(.8f, 1.5f, .8f), "Metal");
        Primitive(s, PrimitiveType.Cylinder, "Faucet_Spout", p + new Vector3(-4, 3.2f, 4.5f),
            new Vector3(2.5f, .25f, .4f), "Metal", false);
        for (int n = 0; n < 5; n++)
            Box(s, "Hanging_Towel_Fold_" + n, p + new Vector3(-5 + n * .8f, .5f, 6.5f),
                new Vector3(.72f, 1.1f, .07f), n % 2 == 0 ? "Soap" : "Foam", false);
    }

    private static void BuildS6()
    {
        Transform s = sectors[5]; Vector3 p = Stops[5];
        Box(s, "Cutting_Board", p + new Vector3(0, .06f, 3.8f), new Vector3(10, .1f, 4), "Wood");
        for (int n = 0; n < 6; n++)
        {
            GameObject sugar = Box(s, "SnapSugar_" + n, p + new Vector3(-4 + (n % 3) * 2.1f, .65f, -3.2f + (n / 3) * 2.2f),
                Vector3.one * 1.2f, "Sugar");
            Rigidbody body = sugar.AddComponent<Rigidbody>(); body.mass = 1f;
            sugar.AddComponent<SnapBlock>();
        }
        // 운반·결합·도킹 컨트롤러는 각 시스템의 자동 생성 진입점이 플레이 모드에서 보장한다.
    }

    private static void BuildS7()
    {
        Transform s = sectors[6]; Vector3 p = Stops[6];
        Primitive(s, PrimitiveType.Cylinder, "Induction_Hob", p + new Vector3(0, .07f, 3.5f),
            new Vector3(4.8f, .05f, 4.8f), "Dark", false);
        Primitive(s, PrimitiveType.Cylinder, "Pan", p + new Vector3(0, .12f, 3.5f),
            new Vector3(3.1f, .12f, 3.1f), "Metal", false);
        GameObject axleObj = WindupAxleMenuItem.CreateWindupAxleAt(p + new Vector3(-4.2f, .2f, -3.4f));
        axleObj.name = "S7_WindupAxle"; axleObj.transform.SetParent(s, true);
        GameObject plate = Box(s, "S7_RotatingPanHandle", p + new Vector3(3.3f, .2f, 3.5f),
            new Vector3(5.5f, .3f, 1.5f), "Wood");
        RotatingPlatform rotor = plate.AddComponent<RotatingPlatform>();
        rotor.axle = axleObj.GetComponent<WindupAxle>();
        rotor.activationMode = WindupActivationMode.HoldPad;
        GameObject activation = Box(s, "S7_HoldPad", p + new Vector3(-3.6f, .11f, 1.7f),
            new Vector3(2, .17f, 2), "Goal");
        activation.GetComponent<BoxCollider>().isTrigger = true;
        rotor.activationPad = activation.AddComponent<WindupActivationPad>();
    }

    private static void BuildS8()
    {
        Transform s = sectors[7]; Vector3 p = Stops[7];
        Box(s, "Pantry_Back", p + new Vector3(0, 4, 6.4f), new Vector3(12, 8, .5f), "Wood");
        for (int n = 0; n < 3; n++)
        {
            Box(s, "Pantry_Shelf_" + n, p + new Vector3(-3.8f + 3.8f * n, 1.7f, 5.5f),
                new Vector3(3.3f, .25f, 2), "WoodLight", false);
            Primitive(s, PrimitiveType.Cylinder, "Pantry_Can_" + n, p + new Vector3(-3.8f + 3.8f * n, 2.6f, 5.4f),
                new Vector3(.65f, .8f, .65f), n == 1 ? "Tomato" : "Metal", false);
        }
        GameObject slowObj = new GameObject("S8_SlowZone", typeof(BoxCollider), typeof(SlowZone));
        slowObj.transform.SetParent(s, false); slowObj.transform.position = p + new Vector3(0, 2.8f, -3.2f);
        BoxCollider slowBox = slowObj.GetComponent<BoxCollider>(); slowBox.isTrigger = true;
        slowBox.size = new Vector3(8, 5.5f, 5);
        GameObject hourglass = Box(s, "S8_Hourglass", p + new Vector3(-4.4f, .6f, -3.3f),
            new Vector3(1.2f, 1.2f, 1.2f), "Orange");
        Rigidbody body = hourglass.AddComponent<Rigidbody>(); body.mass = 2f; body.constraints = RigidbodyConstraints.FreezeAll;
        GameObject visual = Box(hourglass.transform, "Hourglass_RockVisual", hourglass.transform.position,
            Vector3.one * 1.01f, "Orange", false);
        hourglass.GetComponent<MeshRenderer>().enabled = false;
        FallingRockFlip flip = hourglass.AddComponent<FallingRockFlip>();
        flip.targetSlowZone = slowObj.GetComponent<SlowZone>(); flip.visualRoot = visual.transform;
        GameObject curtain = new GameObject("S8_FallingRockSpawner", typeof(FallingRockSpawner));
        curtain.transform.SetParent(s, false);
        FallingRockSpawner spawner = curtain.GetComponent<FallingRockSpawner>();
        spawner.referenceSlowZone = slowObj.GetComponent<SlowZone>();
        spawner.spawnInterval = 3f; spawner.spawnIntervalWhileSlowed = 6f;
        spawner.despawnFallDistance = 9f;
        var points = new Transform[3];
        for (int n = 0; n < 3; n++)
        {
            GameObject point = new GameObject("Rock_Lane_" + n);
            point.transform.SetParent(curtain.transform, false);
            point.transform.position = p + new Vector3(-3 + n * 3, 6.2f, -3.2f);
            points[n] = point.transform;
        }
        spawner.spawnPositions = points;
        GameObject wind = new GameObject("S8_WindZone", typeof(BoxCollider), typeof(WindZone));
        wind.transform.SetParent(s, false); wind.transform.position = p + new Vector3(0, 1.8f, 2.5f);
        wind.transform.rotation = Quaternion.Euler(0, 90, 0);
        BoxCollider wc = wind.GetComponent<BoxCollider>(); wc.isTrigger = true;
        wc.size = new Vector3(4, 3, 2);
        wind.GetComponent<WindZone>().windSpeed = 2.5f;
    }

    private static void BuildS9()
    {
        Transform s = sectors[8]; Vector3 p = Stops[8];
        Box(s, "Fridge_Body", p + new Vector3(-5, -4.3f, 4.5f), new Vector3(4, 8, 3), "Metal");
        Box(s, "Fridge_Door", p + new Vector3(-5, -3.8f, 2.95f), new Vector3(3.5f, 6.5f, .18f), "Foam", false);
        for (int n = 0; n < 3; n++)
            Primitive(s, PrimitiveType.Cylinder, "Fridge_Magnet_" + n,
                p + new Vector3(-6 + n, -.9f, 2.8f), new Vector3(.35f, .05f, .35f),
                n == 0 ? "Tomato" : "Goal", false);
        GameObject catapult = SlingCatapultMenuItem.BuildSlingCatapult(p + new Vector3(-3.5f, .1f, -3.2f), 3f, "S9_SlingCatapult");
        catapult.transform.SetParent(s, true);
        GameObject biscuit = Box(s, "S9_BreakableBiscuit", p + new Vector3(-1.3f, 1.25f, 5.1f),
            new Vector3(2.2f, 2.5f, .5f), "Sugar");
        biscuit.AddComponent<BreakableObject>();
    }

    private static void BuildS10()
    {
        Transform s = sectors[9]; Vector3 p = Stops[9];
        Box(s, "Range_Hood_Duct", p + new Vector3(0, 3.5f, 5.5f), new Vector3(10, 1, 3), "Metal", false);
        Pad<AccelPad>(s, "S10_AccelPad", p + new Vector3(-3.4f, .13f, -1.9f), "Guide").boostSpeed = 6f;
        Pad<JumpPad>(s, "S10_JumpPad", p + new Vector3(3.6f, .13f, -2.3f), "Orange").jumpHeight = 2f;
        GameObject cloud = Box(s, "S10_CloudTrampoline", p + new Vector3(0, .11f, 4.5f),
            new Vector3(2.8f, .2f, 2.8f), "Foam");
        cloud.AddComponent<CloudTrampoline>().baseBounceHeight = 2f;
        GameObject bubble = new GameObject("S10_ZeroGravityBubble", typeof(BoxCollider), typeof(ZeroGravityBubble));
        bubble.transform.SetParent(s, false); bubble.transform.position = p + new Vector3(3.7f, 2.1f, -2.4f);
        BoxCollider bc = bubble.GetComponent<BoxCollider>(); bc.isTrigger = true;
        bc.size = new Vector3(3.2f, 4, 3.2f);
        GameObject wind = new GameObject("S10_WindZone", typeof(BoxCollider), typeof(WindZone));
        wind.transform.SetParent(s, false); wind.transform.position = p + new Vector3(-3.2f, 1.3f, 3.4f);
        wind.transform.rotation = Quaternion.Euler(0, 90, 0);
        BoxCollider wc = wind.GetComponent<BoxCollider>(); wc.isTrigger = true; wc.size = new Vector3(3, 2.5f, 3);
        wind.GetComponent<WindZone>().windSpeed = 2f;
    }

    private static T Pad<T>(Transform parent, string name, Vector3 pos, string material) where T : Component
    {
        GameObject go = Box(parent, name, pos, new Vector3(2.4f, .25f, 2.4f), material);
        return go.AddComponent<T>();
    }

    private static void BuildGoal()
    {
        Transform goal = Group(root, "Goal_Window_SafeLanding");
        Vector3 p = Stops[10];
        Box(goal, "Window_Safe_Floor", p - Vector3.up * .3f, new Vector3(16, .6f, 14), "WoodLight");
        Box(goal, "Window_Left_Rail", p + new Vector3(-7.8f, .7f, 0), new Vector3(.3f, 1.4f, 14), "Metal");
        Box(goal, "Window_Right_Rail", p + new Vector3(7.8f, .7f, 0), new Vector3(.3f, 1.4f, 14), "Metal");
        Box(goal, "Window_End_Rail", p + new Vector3(0, .7f, 6.8f), new Vector3(16, 1.4f, .3f), "Metal");
        GameObject trigger = new GameObject("Goal_AllThree_Trigger", typeof(BoxCollider), typeof(KitchenTeamGoal));
        trigger.transform.SetParent(goal, false); trigger.transform.position = p + Vector3.forward * 4f;
        BoxCollider col = trigger.GetComponent<BoxCollider>(); col.isTrigger = true;
        col.size = new Vector3(11, 3.5f, 9); col.center = new Vector3(0, 1.75f, 0);
        Box(goal, "Open_Window_Frame", p + new Vector3(0, 3.5f, 2f), new Vector3(14, .35f, .4f), "Cream", false);
        Text(goal, "EXIT", p + new Vector3(0, .06f, -2.8f), 1.0f, "Exit_Floor_Sign");
    }

    private static void BuildPlayersAndRecovery()
    {
        Transform systems = Group(root, "Players_And_Recovery");
        GameObject camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camera.tag = "MainCamera"; camera.transform.SetParent(systems, false);
        camera.transform.position = new Vector3(12, 10, 0);
        string[] kinds = { "Sphere", "Cube", "Tetrahedron" };
        float[] x = { 10f, 12f, 14f };
        for (int i = 0; i < 3; i++)
        {
            PlayerMover player = V3Gimmicks.Create<PlayerMover>("Tools/PlayerSystem/Create Player/" + kinds[i], systems);
            player.name = "Player_" + kinds[i];
            player.transform.position = new Vector3(x[i], 1.2f, 9f);
        }
        GameObject respawn = new GameObject("RespawnController", typeof(RespawnController));
        respawn.transform.SetParent(systems, false);
        respawn.GetComponent<RespawnController>().killY = -18f;
        if (camera.GetComponent<PlayerFollowCamera>() == null) camera.AddComponent<PlayerFollowCamera>();
    }

    private static void BuildLighting()
    {
        Transform lights = Group(root, "LowPoly_Lighting");
        GameObject key = new GameObject("Warm_Kitchen_Light", typeof(Light));
        key.transform.SetParent(lights, false);
        key.transform.rotation = Quaternion.Euler(48, -28, 0);
        Light lamp = key.GetComponent<Light>(); lamp.type = LightType.Directional; lamp.intensity = 1.3f;
        lamp.color = new Color(1f, .92f, .79f);
        RenderSettings.ambientLight = new Color(.44f, .52f, .55f);
        RenderSettings.fog = true; RenderSettings.fogColor = new Color(.67f, .76f, .76f);
        RenderSettings.fogDensity = .006f;
    }
}
#endif
