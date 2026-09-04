using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ToyWorldPrototypeBuilder
{
    public const string ScenePath = "Assets/Map3ToyWorld/Scenes/Map3_ToyWorld.unity";
    private const string MaterialFolder = "Assets/Map3ToyWorld/Materials";

    private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

    private sealed class PlazaRefs
    {
        public Transform returnPoint;
        public HubProgressDisplay progress;
    }

    private sealed class BranchRefs
    {
        public ToyWorldRepairItem item;
    }

    [MenuItem("Tools/The Axiom/Build Map3 ToyWorld Prototype")]
    public static void BuildMenu() => BuildPrototype();

    public static void BuildFromCommandLine()
    {
        try
        {
            BuildPrototype();
            int errors = ToyWorldPrototypeValidator.ValidateScene(false);
            if (errors > 0) throw new InvalidOperationException($"Map3 validation failed with {errors} error(s).");
            Debug.Log("[ToyWorldBuilder] Command-line build and validation passed.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
    }

    public static void BuildPrototype()
    {
        EnsureFolders();
        LoadMaterials();

        Scene scene = System.IO.File.Exists(ScenePath)
            ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject root = GameObject.Find("Map3_ToyWorld_Root");
        if (root == null) root = new GameObject("Map3_ToyWorld_Root");

        Transform existingGenerated = root.transform.Find("Generated");
        if (existingGenerated != null) UnityEngine.Object.DestroyImmediate(existingGenerated.gameObject);

        Transform generated = Node("Generated", root.transform);
        if (root.transform.Find("Manual") == null) Node("Manual", root.transform);

        Transform managers = Node("Managers", generated);
        Transform players = Node("Players", generated);
        Transform areas = Node("Areas", generated);
        Transform shared = Node("SharedPuzzleObjects", generated);
        Transform safety = Node("CheckpointsAndResetVolumes", generated);
        Transform lighting = Node("GrayboxLighting", generated);
        Transform debug = Node("Debug_RouteMarkers", generated);

        Camera camera = BuildLightingAndCamera(lighting);
        BuildPlayers(players, camera);

        RespawnController respawn = managers.gameObject.AddComponent<RespawnController>();
        respawn.killY = -12f;
        respawn.outOfBoundsSeconds = 0.8f;
        respawn.dropExtraHeight = 5f;

        PuzzleResetManager resetManager = managers.gameObject.AddComponent<PuzzleResetManager>();
        ToyWorldLevelDirector director = managers.gameObject.AddComponent<ToyWorldLevelDirector>();
        HubProgressDisplay progress = managers.gameObject.AddComponent<HubProgressDisplay>();
        ToyWorldDebugHUD hud = managers.gameObject.AddComponent<ToyWorldDebugHUD>();
        hud.director = director;
        hud.resetManager = resetManager;

        SnapBlockController snapController = managers.gameObject.AddComponent<SnapBlockController>();
        snapController.snapDistance = 0.45f;
        snapController.snapAngleToleranceDeg = 15f;
        snapController.maxBlocksPerStructure = 14;

        FrictionStickerController stickerController = managers.gameObject.AddComponent<FrictionStickerController>();
        stickerController.aimRange = 4f;
        stickerController.slipCount = 2;
        stickerController.velcroCount = 2;

        BuildDreamThreadManager(managers);

        PlazaRefs plaza = BuildPlaza(areas, safety, progress);
        BuildToyBox(areas, shared, safety);
        BranchRefs fort = BuildBlockFort(areas, shared, safety);
        BranchRefs train = BuildTrainYard(areas, shared, safety);
        BranchRefs doll = BuildDollHouse(areas, shared, safety);
        MusicBoxRepairController musicBox;
        doorPhysics finalGate;
        doorPhysics installationGate;
        BuildFinalRoom(areas, shared, safety, director, out musicBox, out finalGate, out installationGate);

        director.finalGate = finalGate;
        director.installationGate = installationGate;
        director.musicBox = musicBox;
        director.hubProgress = progress;
        musicBox.director = director;

        fort.item.director = director;
        train.item.director = director;
        doll.item.director = director;
        BuildWorldSafety(safety);
        BuildRouteMarkers(debug);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = root;
        Debug.Log($"[ToyWorldBuilder] Built functional graybox at {ScenePath}. Manual subtree was preserved.");
    }

    private static Camera BuildLightingAndCamera(Transform parent)
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(parent, false);
        cameraObject.transform.position = new Vector3(0f, 8f, -58f);
        cameraObject.transform.rotation = Quaternion.Euler(20f, 0f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.farClipPlane = 250f;
        cameraObject.AddComponent<AudioListener>();
        PlayerFollowCamera follow = cameraObject.AddComponent<PlayerFollowCamera>();
        follow.offset = new Vector3(0f, 7f, -11f);
        follow.lookHeightOffset = 1f;
        follow.enableMouseOrbit = true;

        GameObject sun = new GameObject("Directional Light");
        sun.transform.SetParent(parent, false);
        sun.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        Light light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.45f, 0.5f, 0.62f);
        RenderSettings.ambientEquatorColor = new Color(0.28f, 0.3f, 0.34f);
        RenderSettings.ambientGroundColor = new Color(0.12f, 0.13f, 0.16f);
        return camera;
    }

    private static void BuildPlayers(Transform parent, Camera camera)
    {
        string[] menuItems =
        {
            "Tools/PlayerSystem/Create Player/Sphere",
            "Tools/PlayerSystem/Create Player/Cube",
            "Tools/PlayerSystem/Create Player/Tetrahedron"
        };
        string[] objectNames = { "Player_Sphere", "Player_Cube", "Player_Tetrahedron" };
        float[] xPositions = { -1.5f, 0f, 1.5f };

        for (int i = 0; i < menuItems.Length; i++)
        {
            if (!EditorApplication.ExecuteMenuItem(menuItems[i]))
                throw new InvalidOperationException("Could not invoke existing player builder: " + menuItems[i]);
            GameObject player = GameObject.Find(objectNames[i]);
            if (player == null) throw new InvalidOperationException("Existing PlayerSystem builder did not create " + objectNames[i]);
            player.transform.SetParent(parent, true);
            player.transform.position = new Vector3(xPositions[i], 0.05f, -50f);
        }

        PlayerFollowCamera follow = camera.GetComponent<PlayerFollowCamera>();
        follow.target = GameObject.Find("Player_Sphere").transform;
        PlayerControlSwitcher switcher = UnityEngine.Object.FindObjectOfType<PlayerControlSwitcher>();
        if (switcher != null) switcher.transform.SetParent(parent, true);
    }

    private static void BuildDreamThreadManager(Transform parent)
    {
        GameObject controllerObject = new GameObject("DreamThreadController_Map3");
        controllerObject.transform.SetParent(parent, false);
        DreamThreadController controller = controllerObject.AddComponent<DreamThreadController>();
        controller.maxLength = 10f;
        controller.minLength = 1.5f;
        LineRenderer line = controllerObject.GetComponent<LineRenderer>();
        line.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/DreamThreadSystem/Materials/DreamThread_Line_Mat.mat");
        line.widthMultiplier = controller.lineWidth;

        GameObject pinPlacer = new GameObject("DreamThreadPinPlacer_Map3");
        pinPlacer.transform.SetParent(parent, false);
        pinPlacer.AddComponent<ThreadPinPlacer>();
    }

    private static PlazaRefs BuildPlaza(Transform areas, Transform safety, HubProgressDisplay progress)
    {
        Transform area = Node("ToyPlaza_Hub", areas);
        Box("GEO_PlazaFloor", area, new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f), Mat("Ground"));
        Box("GEO_MusicBoxSilhouette", area, new Vector3(0f, 4f, 13f), new Vector3(12f, 8f, 2f), Mat("MusicBoxDark"));

        Transform returnPoint = Node("HubReturnPoint", area);
        returnPoint.position = new Vector3(0f, 0f, 6f);

        Renderer[] slots = new Renderer[3];
        Renderer[] beacons = new Renderer[3];
        Color[] colors = { new Color(1f, 0.4f, 0.08f), new Color(0.1f, 0.9f, 0.85f), new Color(0.9f, 0.2f, 0.8f) };
        Vector3[] slotPositions = { new Vector3(-3f, 0.35f, 2f), new Vector3(0f, 0.35f, 2f), new Vector3(3f, 0.35f, 2f) };
        Vector3[] beaconPositions = { new Vector3(-13f, 2f, 6f), new Vector3(13f, 2f, 6f), new Vector3(10f, 2f, -12f) };
        for (int i = 0; i < 3; i++)
        {
            GameObject slot = Box("VIS_HubSlot_" + ((ToyWorldRepairItemType)i), area, slotPositions[i], new Vector3(2f, 0.7f, 2f), Mat("Inactive"), false);
            slots[i] = slot.GetComponentInChildren<Renderer>();
            GameObject beacon = Box("VIS_BranchBeacon_" + i, area, beaconPositions[i], new Vector3(1f, 4f, 1f), MaterialForColor("Route" + i, colors[i]), false);
            beacons[i] = beacon.GetComponentInChildren<Renderer>();
        }
        progress.itemSlots = slots;
        progress.branchBeacons = beacons;

        Walkway("GEO_Path_ToyBox_To_Plaza", areas, new Vector3(0f, 0f, -31f), new Vector3(0f, 0f, -15f), 6f);
        Walkway("GEO_Path_Plaza_To_BlockFort", areas, new Vector3(-14f, 0f, 5f), new Vector3(-27f, 0f, 12f), 5f);
        Walkway("GEO_Path_Plaza_To_TrainYard", areas, new Vector3(14f, 0f, 5f), new Vector3(27f, 0f, 12f), 5f);
        Walkway("GEO_Path_Plaza_To_DollHouse", areas, new Vector3(10f, 0f, -12f), new Vector3(24f, 0f, -25f), 5f);
        Walkway("GEO_Path_Plaza_To_Final", areas, new Vector3(0f, 0f, 15f), new Vector3(0f, 0f, 27f), 6f);

        Checkpoint("CP_ToyPlaza", safety, new Vector3(0f, 5.5f, -10f));
        return new PlazaRefs { returnPoint = returnPoint, progress = progress };
    }

    private static void BuildToyBox(Transform areas, Transform shared, Transform safety)
    {
        Transform area = Node("ToyBox_Entrance", areas);
        Box("GEO_ToyBoxFloor", area, new Vector3(0f, -0.5f, -42f), new Vector3(28f, 1f, 22f), Mat("Ground"));
        Box("GEO_ToyBoxBackWall", area, new Vector3(0f, 4f, -53f), new Vector3(28f, 8f, 1f), Mat("Wall"));
        Box("GEO_ToyBoxLeftWall", area, new Vector3(-14f, 3f, -42f), new Vector3(1f, 6f, 22f), Mat("Wall"));
        Box("GEO_ToyBoxRightWall", area, new Vector3(14f, 3f, -42f), new Vector3(1f, 6f, 22f), Mat("Wall"));
        Box("GEO_BrokenShelf_Left", area, new Vector3(-9f, 2f, -35f), new Vector3(12f, 4f, 2f), Mat("Wall"));
        Box("GEO_BrokenShelf_Right", area, new Vector3(9f, 2f, -35f), new Vector3(12f, 4f, 2f), Mat("Wall"));

        LiftPad shelfPad;
        LiftPlatform shelfLift = LiftSet("ExistingLift_ToyBoxShelf", area,
            new Vector3(0f, 0.25f, -37f), new Vector3(-7f, 0.15f, -42f), 5f, out shelfPad);
        shelfLift.moveSpeed = 1.4f;
        shelfPad.lightWeightGate = 2f;

        Portal enable = PortalObject("TRG_ToyBox_RollModeEnable", area, new Vector3(-7f, 2f, -46f), Portal.PortalAction.Enable);
        PortalObject("TRG_ToyBox_RollModeDisable", area, new Vector3(0f, 2f, -31.5f), Portal.PortalAction.Disable);

        for (int i = 0; i < 8; i++)
        {
            float x = -5.5f + (i % 4) * 1.7f;
            float z = -49f + (i / 4) * 1.8f;
            SnapBlockObject("DYN_ToyBox_SnapBlock_" + (i + 1), shared, new Vector3(x, 0.55f, z), new Vector3(1.4f, 1.1f, 1.4f));
        }

        Seesaw("DYN_ToyBox_Seesaw_Bypass", area, new Vector3(7f, 0.7f, -43f), Quaternion.Euler(0f, 90f, 0f), true);
        Checkpoint("CP_ToyBox_Start", safety, new Vector3(0f, 5.5f, -50f));
        enable.name = "TRG_ToyBox_RollModeEnable_ExistingPortal";
    }

    private static BranchRefs BuildBlockFort(Transform areas, Transform shared, Transform safety)
    {
        Transform area = Node("Branch_BlockFort", areas);
        Box("GEO_BlockFortFloor", area, new Vector3(-40f, -0.5f, 18f), new Vector3(30f, 1f, 28f), Mat("Ground"));
        Box("GEO_BlockFortWall", area, new Vector3(-37f, 2.5f, 18f), new Vector3(2f, 5f, 22f), Mat("Wall"));
        Box("GEO_BlockFortBattlement", area, new Vector3(-37f, 5.25f, 18f), new Vector3(3f, 0.5f, 24f), Mat("Accent"));

        Vector3[] stairPositions =
        {
            new Vector3(-30.7f, 0.5f, 17f), new Vector3(-32f, 1.5f, 17f),
            new Vector3(-33.3f, 2.5f, 17f), new Vector3(-34.7f, 3.5f, 17f)
        };
        for (int i = 0; i < stairPositions.Length; i++)
            SnapBlockObject("DYN_BlockFort_StairBlock_" + (i + 1), shared, stairPositions[i], new Vector3(2.4f, 1f, 3f));
        for (int i = 0; i < 4; i++)
            SnapBlockObject("DYN_BlockFort_LooseBlock_" + (i + 1), shared, new Vector3(-30f + i * 1.7f, 0.55f, 23f), new Vector3(1.4f, 1.1f, 1.4f));

        GameObject slipRamp = Ramp("GEO_BlockFort_SlipRamp_Bypass", area,
            new Vector3(-31f, 0.2f, 27f), new Vector3(-38f, 3.9f, 27f), 3.2f, Mat("Slip"));
        slipRamp.AddComponent<StickerSurface>();
        Seesaw("DYN_BlockFort_Seesaw_Bypass", area, new Vector3(-31f, 0.7f, 10f), Quaternion.identity, true);

        Checkpoint("CP_BlockFort", safety, new Vector3(-47f, 5.5f, 18f));
        ToyWorldRepairItem item = RepairItem("GOAL_WindUpSpring", area, ToyWorldRepairItemType.WindUpSpring,
            new Vector3(-47f, 1.2f, 18f), Mat("Spring"));
        return new BranchRefs { item = item };
    }

    private static BranchRefs BuildTrainYard(Transform areas, Transform shared, Transform safety, Transform hubReturn)
    {
        Transform area = Node("Branch_TrainYard", areas);
        Box("GEO_TrainWestBank", area, new Vector3(31f, -0.5f, 18f), new Vector3(14f, 1f, 26f), Mat("Ground"));
        Box("GEO_TrainEastBank", area, new Vector3(53f, -0.5f, 18f), new Vector3(14f, 1f, 26f), Mat("Ground"));
        Box("VIS_TrainCanyonDanger", area, new Vector3(42f, -5.5f, 18f), new Vector3(9f, 0.4f, 26f), Mat("Danger"), false);

        CreateRailSegment(area, 35f, 40.5f, 18f);
        CreateRailSegment(area, 44f, 57f, 18f);
        for (float x = 35f; x <= 57f; x += 2f)
            if (x < 40.5f || x > 44f)
                Box("GEO_RailTie_" + x.ToString("0"), area, new Vector3(x, 0.15f, 18f), new Vector3(0.35f, 0.3f, 4f), Mat("Rail"));

        Transform railReference = Node("RailReference_Forward", area);
        railReference.position = new Vector3(35f, 0f, 18f);
        railReference.rotation = Quaternion.Euler(0f, 90f, 0f);
        GameObject switchObject = Box("DYN_RailBranchSwitch", area, new Vector3(39f, 0.65f, 18f),
            new Vector3(4f, 0.3f, 1.6f), Mat("Gold"));
        Rigidbody switchBody = switchObject.AddComponent<Rigidbody>();
        switchBody.isKinematic = true;
        ToyRailSwitch railSwitch = switchObject.AddComponent<ToyRailSwitch>();
        switchObject.AddComponent<PuzzleResettable>().autoResetBelowY = -7f;
        ToyRailCart cart = RailCart("DYN_ToyRailCart", area, new Vector3(35f, 1.05f, 18f), railReference);
        cart.railSwitch = railSwitch;

        GameObject switchPadObject = Box("TRG_RailBranchSwitchPad_NormalRoute", area,
            new Vector3(31f, 0.15f, 23f), new Vector3(3f, 0.3f, 3f), Mat("Gold"), true, true);
        ToyRailSwitchPad switchPad = switchPadObject.AddComponent<ToyRailSwitchPad>();
        switchPad.railSwitch = railSwitch;
        switchPad.latch = true;

        Portal("TRG_Train_TorqueEnable", area, new Vector3(30f, 2f, 13f), TorquePortalMode.Enable);
        WindUpAxis axis = Axis("DYN_Train_WindUpAxis", area, new Vector3(34f, 1.5f, 13f));
        axis.receivers = new MonoBehaviour[] { cart };
        Portal("TRG_Train_TorqueDisable", area, new Vector3(53f, 2f, 13f), TorquePortalMode.Disable);

        GameObject derailPadObject = Box("TRG_IntentionalDerailPad_Bypass", area, new Vector3(38f, 0.15f, 23f), new Vector3(3f, 0.3f, 3f), Mat("Danger"), true, true);
        ToyRailDerailPad derailPad = derailPadObject.AddComponent<ToyRailDerailPad>();
        derailPad.cart = cart;
        derailPad.lateralImpulse = new Vector3(0f, 1f, 5f);

        ThreadAnchorObject("WIRE_TrainGapAnchor", area, new Vector3(42f, 7f, 18f), 9f);
        AccelJumpBypass(area, new Vector3(35f, 0.35f, 24f), Vector3.right, 7f);

        Checkpoint("CP_TrainYard", safety, new Vector3(54f, 5.5f, 18f));
        ToyWorldReturnPortal shortcut = ReturnPortal("TRG_Train_ReturnShortcut", area, new Vector3(55f, 2f, 24f), hubReturn);
        ToyWorldRepairItem item = RepairItem("GOAL_PowerGear", area, ToyWorldRepairItemType.PowerGear,
            new Vector3(55f, 1.2f, 18f), Mat("Gear"));
        return new BranchRefs { item = item, shortcut = shortcut };
    }

    private static BranchRefs BuildDollHouse(Transform areas, Transform shared, Transform safety, Transform hubReturn)
    {
        Transform area = Node("Branch_DollHouse", areas);
        Box("GEO_DollHouseBase", area, new Vector3(34f, -0.5f, -34f), new Vector3(24f, 1f, 24f), Mat("Ground"));
        Box("GEO_DollHouseBackWall", area, new Vector3(34f, 10f, -46f), new Vector3(24f, 20f, 1f), Mat("Wall"));
        Box("GEO_DollHouseLeftWall", area, new Vector3(22f, 10f, -34f), new Vector3(1f, 20f, 24f), Mat("Wall"));
        Box("GEO_DollHouseRightWall", area, new Vector3(46f, 10f, -34f), new Vector3(1f, 20f, 24f), Mat("Wall"));
        Box("GEO_DollLevel1", area, new Vector3(34f, 5.5f, -41f), new Vector3(22f, 1f, 9f), Mat("Accent"));
        Box("GEO_DollLevel2", area, new Vector3(34f, 11.5f, -28f), new Vector3(22f, 1f, 9f), Mat("Accent"));
        Box("GEO_DollAttic", area, new Vector3(34f, 17.5f, -41f), new Vector3(22f, 1f, 9f), Mat("Accent"));

        RotatingBoard("DYN_DollBed_Ramp", area, new Vector3(34f, 0.3f, -28f), new Vector3(34f, 5.8f, -38f), 4f);
        RotatingBoard("DYN_DollLivingBoard_Ramp", area, new Vector3(30f, 6.3f, -38f), new Vector3(30f, 11.8f, -31f), 4f);
        RotatingBoard("DYN_DollAtticBoard_Ramp", area, new Vector3(38f, 12.3f, -31f), new Vector3(38f, 17.8f, -39f), 4f);
        Seesaw("DYN_DollShelf_Seesaw", area, new Vector3(39f, 12.8f, -38f), Quaternion.identity, false);

        ThreadAnchorObject("WIRE_DollMobile_Low", area, new Vector3(34f, 10f, -34f), 8f);
        ThreadAnchorObject("WIRE_DollMobile_High", area, new Vector3(34f, 17f, -34f), 8f);
        AccelJumpBypass(area, new Vector3(27f, 0.35f, -28f), Vector3.up, 7f);

        Checkpoint("CP_DollHouse_Level1", safety, new Vector3(34f, 11.5f, -41f));
        Checkpoint("CP_DollHouse_Level2", safety, new Vector3(34f, 17.5f, -28f));
        Checkpoint("CP_DollHouse_Attic", safety, new Vector3(34f, 23.5f, -41f));

        ToyWorldReturnPortal shortcut = ReturnPortal("TRG_Doll_ReturnShortcut", area, new Vector3(41f, 20f, -41f), hubReturn);
        ToyWorldRepairItem item = RepairItem("GOAL_MelodyCylinder", area, ToyWorldRepairItemType.MelodyCylinder,
            new Vector3(34f, 19.2f, -41f), Mat("Cylinder"));
        return new BranchRefs { item = item, shortcut = shortcut };
    }

    private static void BuildFinalRoom(Transform areas, Transform shared, Transform safety,
        ToyWorldLevelDirector director, out MusicBoxRepairController musicBox, out ToyWorldGate finalGate)
    {
        Transform area = Node("Final_BrokenMusicBox", areas);
        Box("GEO_FinalFloor", area, new Vector3(0f, -0.5f, 40f), new Vector3(30f, 1f, 28f), Mat("MusicBoxDark"));
        Box("GEO_FinalLeftWall", area, new Vector3(-15f, 5f, 40f), new Vector3(1f, 10f, 28f), Mat("Wall"));
        Box("GEO_FinalRightWall", area, new Vector3(15f, 5f, 40f), new Vector3(1f, 10f, 28f), Mat("Wall"));

        finalGate = MovingBox<ToyWorldGate>("DOOR_FinalGate_Requires3Parts", area,
            new Vector3(0f, 2.5f, 27.5f), new Vector3(7f, 5f, 1f), Mat("Locked"), true);
        finalGate.openOffset = new Vector3(0f, 6f, 0f);
        finalGate.moveSpeed = 3f;
        finalGate.statusRenderer = finalGate.GetComponentInChildren<Renderer>();
        Box("GEO_FinalGateFrameLeft", area, new Vector3(-5f, 3f, 27.5f), new Vector3(3f, 6f, 1.5f), Mat("Wall"));
        Box("GEO_FinalGateFrameRight", area, new Vector3(5f, 3f, 27.5f), new Vector3(3f, 6f, 1.5f), Mat("Wall"));
        Box("GEO_FinalGateBarrierLeft", area, new Vector3(-10.75f, 3f, 27.5f), new Vector3(8.5f, 6f, 1.5f), Mat("Wall"));
        Box("GEO_FinalGateBarrierRight", area, new Vector3(10.75f, 3f, 27.5f), new Vector3(8.5f, 6f, 1.5f), Mat("Wall"));

        musicBox = area.gameObject.AddComponent<MusicBoxRepairController>();
        musicBox.director = director;
        ToyWorldInstallSocket[] sockets = new ToyWorldInstallSocket[3];
        for (int i = 0; i < 3; i++)
        {
            sockets[i] = InstallSocket("TRG_Install_" + ((ToyWorldRepairItemType)i), area,
                (ToyWorldRepairItemType)i, new Vector3(-3f + i * 3f, 0.3f, 33f), director);
        }
        musicBox.installSockets = sockets;

        Portal("TRG_Final_TorqueEnable", area, new Vector3(0f, 2f, 37f), TorquePortalMode.Enable);
        WindUpAxis axis = Axis("DYN_Final_MusicBox_WindUpAxis", area, new Vector3(0f, 1.5f, 41f));
        axis.receivers = new MonoBehaviour[] { musicBox };

        Rigidbody[] steps = new Rigidbody[6];
        for (int i = 0; i < steps.Length; i++)
        {
            GameObject step = Box("PLATFORM_DeployableStep_" + (i + 1), area,
                new Vector3(0f, 0.45f + i * 1.25f, 42f + i * 1.55f), new Vector3(5f, 0.8f, 2f), Mat("Dynamic"));
            Rigidbody body = step.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            steps[i] = body;
        }
        musicBox.deployableSteps = steps;
        musicBox.activationChargeRequired = 1.75f;

        GameObject spinner = CylinderVisual("VIS_MusicBoxSpinner", area, new Vector3(0f, 2f, 41f), new Vector3(4f, 0.3f, 4f), Mat("Gold"), false);
        musicBox.activatedSpinner = spinner.transform;

        Box("GEO_FinalExitPlatform", area, new Vector3(0f, 7.5f, 52f), new Vector3(10f, 1f, 7f), Mat("Accent"));
        GameObject exit = Box("GOAL_FinalExitTrigger", area, new Vector3(0f, 9.5f, 53f), new Vector3(4f, 4f, 2f), Mat("Goal"), true, true);
        ToyWorldExitTrigger exitTrigger = exit.AddComponent<ToyWorldExitTrigger>();
        exitTrigger.director = director;

        AccelJumpBypass(area, new Vector3(7f, 0.35f, 44f), Vector3.up, 9f);
        RotatingBoard("DYN_Final_FreeBoard_Bypass", area, new Vector3(-9f, 0.3f, 42f), new Vector3(-4f, 7.8f, 50f), 3f);
        ThreadAnchorObject("WIRE_FinalExitAnchor", area, new Vector3(-2f, 12f, 49f), 10f);
        for (int i = 0; i < 4; i++)
            SnapBlockObject("DYN_Final_BypassBlock_" + (i + 1), shared, new Vector3(8f + (i % 2) * 1.6f, 0.55f, 37f + (i / 2) * 1.6f), new Vector3(1.4f, 1.1f, 1.4f));

        Portal("TRG_Final_TorqueDisable", area, new Vector3(0f, 9.5f, 50f), TorquePortalMode.Disable);
        Checkpoint("CP_FinalRoom", safety, new Vector3(0f, 5.5f, 30f));
    }

    private static void BuildWorldSafety(Transform safety)
    {
        Vector3[] centers =
        {
            new Vector3(0f, -6f, -42f), new Vector3(0f, -6f, 0f), new Vector3(-40f, -6f, 18f),
            new Vector3(42f, -6f, 18f), new Vector3(34f, -6f, -34f), new Vector3(0f, -6f, 40f)
        };
        Vector3[] sizes =
        {
            new Vector3(34f, 2f, 28f), new Vector3(36f, 2f, 36f), new Vector3(36f, 2f, 34f),
            new Vector3(42f, 2f, 34f), new Vector3(30f, 2f, 30f), new Vector3(36f, 2f, 34f)
        };
        for (int i = 0; i < centers.Length; i++)
        {
            GameObject reset = Box("RESET_ObjectVolume_" + i, safety, centers[i], sizes[i], Mat("Danger"), true, true);
            reset.AddComponent<ToyWorldObjectResetVolume>();
            reset.GetComponentInChildren<Renderer>().enabled = false;
        }

        GameObject playerBounds = Box("RESET_PlayerOutOfBounds", safety, new Vector3(0f, -8.5f, 0f),
            new Vector3(170f, 3f, 170f), Mat("Danger"), true, true);
        playerBounds.AddComponent<OutOfBoundsVolume>();
        playerBounds.GetComponentInChildren<Renderer>().enabled = false;
    }

    private static void BuildRouteMarkers(Transform parent)
    {
        string[] names =
        {
            "ROUTE_ToyBox_Normal_TorqueAxisLift", "ROUTE_ToyBox_Bypass_SnapBlockStairs",
            "ROUTE_BlockFort_Normal_SnapBlockStairs", "ROUTE_BlockFort_Bypass_SeesawOrSlipRamp",
            "ROUTE_Train_Normal_WindUpRailCart", "ROUTE_Train_Bypass_WireOrAccelJumpOrDerail",
            "ROUTE_Doll_Normal_RotatingFurniture", "ROUTE_Doll_Bypass_WireOrJump",
            "ROUTE_Final_Normal_DeployableStairs", "ROUTE_Final_Bypass_BlocksOrBoardOrJump"
        };
        for (int i = 0; i < names.Length; i++) Node(names[i], parent);
    }

    private static void CreateRailSegment(Transform parent, float fromX, float toX, float z)
    {
        float length = toX - fromX;
        float center = (fromX + toX) * 0.5f;
        Box("RAIL_Left_" + fromX, parent, new Vector3(center, 0.25f, z - 1.2f), new Vector3(length, 0.5f, 0.35f), Mat("Rail"));
        Box("RAIL_Right_" + fromX, parent, new Vector3(center, 0.25f, z + 1.2f), new Vector3(length, 0.5f, 0.35f), Mat("Rail"));
    }

    private static ToyRailCart RailCart(string name, Transform parent, Vector3 position, Transform railReference)
    {
        GameObject cartObject = Box(name, parent, position, new Vector3(3.5f, 1.1f, 3.2f), Mat("Dynamic"));
        Rigidbody body = cartObject.AddComponent<Rigidbody>();
        body.mass = 5f;
        ToyRailCart cart = cartObject.AddComponent<ToyRailCart>();
        cart.railReference = railReference;
        cart.driveAcceleration = 3f;
        cart.maxRailSpeed = 12f;
        cartObject.AddComponent<PuzzleResettable>().autoResetBelowY = -7f;
        cartObject.AddComponent<StickerSurface>();
        ThreadAnchorObject("WIRE_CartAnchor", cartObject.transform, position + Vector3.up * 2f, 5f, true);
        return cart;
    }

    private static WindUpAxis Axis(string name, Transform parent, Vector3 position)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = position;
        CapsuleCollider solid = root.AddComponent<CapsuleCollider>();
        solid.direction = 1;
        solid.radius = 0.8f;
        solid.height = 3f;
        SphereCollider interaction = root.AddComponent<SphereCollider>();
        interaction.radius = 3f;
        interaction.isTrigger = true;
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.angularDrag = 1.5f;
        HingeJoint hinge = root.AddComponent<HingeJoint>();
        hinge.axis = Vector3.up;
        WindUpAxis axis = root.AddComponent<WindUpAxis>();
        axis.localAxis = Vector3.up;
        axis.maxTurns = 1.5f;
        axis.outputScale = 8f;
        GameObject visual = CylinderVisual("VisualMesh", root.transform, position, new Vector3(1.6f, 1.5f, 1.6f), Mat("Gold"), true);
        axis.energyRenderer = visual.GetComponent<Renderer>();
        GameObject handle = Box("VIS_WindHandle", root.transform, position + new Vector3(1.4f, 0.8f, 0f), new Vector3(2.8f, 0.25f, 0.3f), Mat("Gold"), false, false, true);
        handle.transform.SetParent(root.transform, true);
        root.AddComponent<PuzzleResettable>();
        return axis;
    }

    private static TorquePortal Portal(string name, Transform parent, Vector3 position, TorquePortalMode mode)
    {
        GameObject root = Box(name, parent, position, new Vector3(5f, 4f, 1f), mode == TorquePortalMode.Disable ? Mat("Disable") : Mat("Torque"), true, true);
        Renderer main = root.GetComponentInChildren<Renderer>();
        main.enabled = false;
        Box("VIS_PortalLeft", root.transform, position + new Vector3(-2.2f, 0f, 0f), new Vector3(0.4f, 4f, 0.5f), mode == TorquePortalMode.Disable ? Mat("Disable") : Mat("Torque"), false, false, true);
        Box("VIS_PortalRight", root.transform, position + new Vector3(2.2f, 0f, 0f), new Vector3(0.4f, 4f, 0.5f), mode == TorquePortalMode.Disable ? Mat("Disable") : Mat("Torque"), false, false, true);
        GameObject top = Box("VIS_PortalTop", root.transform, position + new Vector3(0f, 2f, 0f), new Vector3(4.8f, 0.4f, 0.5f), mode == TorquePortalMode.Disable ? Mat("Disable") : Mat("Torque"), false, false, true);
        TorquePortal portal = root.AddComponent<TorquePortal>();
        portal.mode = mode;
        portal.statusRenderer = top.GetComponentInChildren<Renderer>();
        return portal;
    }

    private static ToyWorldReturnPortal ReturnPortal(string name, Transform parent, Vector3 position, Transform destination)
    {
        GameObject root = Box(name, parent, position, new Vector3(4f, 4f, 1f), Mat("Inactive"), true, true);
        Renderer renderer = root.GetComponentInChildren<Renderer>();
        ToyWorldReturnPortal portal = root.AddComponent<ToyWorldReturnPortal>();
        portal.destination = destination;
        portal.statusRenderer = renderer;
        return portal;
    }

    private static ToyWorldRepairItem RepairItem(string name, Transform parent, ToyWorldRepairItemType type, Vector3 position, Material material)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = position;
        SphereCollider trigger = root.AddComponent<SphereCollider>();
        trigger.radius = 1.2f;
        trigger.isTrigger = true;
        GameObject visualRoot = new GameObject("VisualMesh");
        visualRoot.transform.SetParent(root.transform, false);
        if (type == ToyWorldRepairItemType.PowerGear)
        {
            CylinderVisual("GearCore", visualRoot.transform, position, new Vector3(1.5f, 0.35f, 1.5f), material, true);
            for (int i = 0; i < 6; i++)
            {
                GameObject spoke = Box("GearTooth_" + i, visualRoot.transform, position, new Vector3(0.35f, 0.45f, 2.1f), material, false, false, true);
                spoke.transform.localRotation = Quaternion.Euler(0f, i * 60f, 0f);
            }
        }
        else if (type == ToyWorldRepairItemType.MelodyCylinder)
        {
            GameObject cylinder = CylinderVisual("MelodyCylinder", visualRoot.transform, position, new Vector3(1.3f, 1.8f, 1.3f), material, true);
            cylinder.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                GameObject coil = CylinderVisual("SpringSegment_" + i, visualRoot.transform, position, new Vector3(1.4f, 0.12f, 1.4f), material, true);
                coil.transform.localPosition = new Vector3(0f, -0.55f + i * 0.38f, 0f);
            }
        }
        ToyWorldRepairItem item = root.AddComponent<ToyWorldRepairItem>();
        item.itemType = type;
        item.visualRoot = visualRoot;
        return item;
    }

    private static ToyWorldInstallSocket InstallSocket(string name, Transform parent, ToyWorldRepairItemType type, Vector3 position, ToyWorldLevelDirector director)
    {
        GameObject root = Box(name, parent, position, new Vector3(2.2f, 0.6f, 2.2f), Mat("Inactive"), true, true);
        ToyWorldInstallSocket socket = root.AddComponent<ToyWorldInstallSocket>();
        socket.itemType = type;
        socket.director = director;
        socket.socketRenderer = root.GetComponentInChildren<Renderer>();
        return socket;
    }

    private static GameObject SnapBlockObject(string name, Transform parent, Vector3 position, Vector3 size)
    {
        GameObject root = Box(name, parent, position, size, Mat("Dynamic"));
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = 0.9f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        root.AddComponent<SnapBlock>();
        root.AddComponent<PuzzleResettable>().autoResetBelowY = -7f;
        return root;
    }

    private static RotatingPlate Seesaw(string name, Transform parent, Vector3 position, Quaternion rotation, bool launchPad)
    {
        GameObject root = Box(name, parent, position, new Vector3(8f, 0.4f, 3f), Mat("Dynamic"));
        root.transform.rotation = rotation;
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = 4f;
        RotatingPlate plate = root.AddComponent<RotatingPlate>();
        plate.rotationAxis = Vector3.forward;
        plate.useLimits = true;
        plate.minAngle = -30f;
        plate.maxAngle = 30f;
        plate.angularResistance = 1.2f;
        plate.plateUseGravity = true;
        root.AddComponent<PuzzleResettable>().autoResetBelowY = -7f;

        if (launchPad)
        {
            GameObject pad = Box("GEO_SeesawLaunchEnd", root.transform, position + root.transform.right * 3f + Vector3.up * 0.3f,
                new Vector3(1.8f, 0.2f, 2.4f), Mat("Jump"), true, false, true);
            JumpPad jump = pad.AddComponent<JumpPad>();
            jump.jumpHeight = 6f;
        }
        return plate;
    }

    private static RotatingPlate RotatingBoard(string name, Transform parent, Vector3 start, Vector3 end, float width)
    {
        Vector3 delta = end - start;
        GameObject root = Box(name, parent, (start + end) * 0.5f, new Vector3(width, 0.4f, delta.magnitude), Mat("Dynamic"));
        root.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = 18f;
        RotatingPlate board = root.AddComponent<RotatingPlate>();
        board.rotationAxis = Vector3.right;
        board.useLimits = true;
        board.minAngle = -55f;
        board.maxAngle = 55f;
        board.angularResistance = 3f;
        board.plateUseGravity = false;
        root.AddComponent<PuzzleResettable>().autoResetBelowY = -7f;
        root.AddComponent<StickerSurface>();
        ThreadAnchorObject("WIRE_" + name, root.transform, end + Vector3.up * 0.5f, 6f, true);
        return board;
    }

    private static void AccelJumpBypass(Transform parent, Vector3 position, Vector3 direction, float jumpHeight)
    {
        Quaternion rotation = direction == Vector3.up ? Quaternion.identity : Quaternion.LookRotation(direction, Vector3.up);
        GameObject accelObject = Box("TRG_AccelBypass", parent, position, new Vector3(3f, 0.3f, 3f), Mat("Accel"), true, true);
        accelObject.transform.rotation = rotation;
        AccelPad accel = accelObject.AddComponent<AccelPad>();
        accel.boostSpeed = 12f;
        accel.holdDuration = 0.8f;
        GameObject jumpObject = Box("GEO_JumpBypass", parent, position + (direction == Vector3.up ? Vector3.zero : direction * 2.5f),
            new Vector3(3f, 0.35f, 3f), Mat("Jump"));
        JumpPad jump = jumpObject.AddComponent<JumpPad>();
        jump.jumpHeight = jumpHeight;
    }

    private static ThreadAnchor ThreadAnchorObject(string name, Transform parent, Vector3 worldPosition, float range, bool keepWorld = false)
    {
        GameObject anchor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        anchor.name = name;
        UnityEngine.Object.DestroyImmediate(anchor.GetComponent<Collider>());
        anchor.GetComponent<Renderer>().sharedMaterial = Mat("Wire");
        anchor.transform.localScale = Vector3.one * 0.45f;
        anchor.transform.SetParent(parent, keepWorld);
        anchor.transform.position = worldPosition;
        ThreadAnchor component = anchor.AddComponent<ThreadAnchor>();
        component.connectRange = range;
        return component;
    }

    private static RespawnZone Checkpoint(string name, Transform parent, Vector3 position)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = position;
        BoxCollider trigger = root.AddComponent<BoxCollider>();
        trigger.size = new Vector3(4f, 11f, 4f);
        trigger.isTrigger = true;
        RespawnZone zone = root.AddComponent<RespawnZone>();
        CylinderVisual("VIS_CheckpointPole", root.transform, position + new Vector3(-1.3f, -3.5f, 0f), new Vector3(0.15f, 2f, 0.15f), Mat("Checkpoint"), true);
        GameObject flag = Box("VIS_CheckpointFlag", root.transform, position + new Vector3(-0.65f, -2f, 0f), new Vector3(1.3f, 0.7f, 0.1f), Mat("Checkpoint"), false, false, true);
        zone.flagRenderer = flag.GetComponentInChildren<Renderer>();
        zone.raiseFromLocalY = -5f;
        return zone;
    }

    private static GameObject Ramp(string name, Transform parent, Vector3 start, Vector3 end, float width, Material material)
    {
        Vector3 delta = end - start;
        GameObject root = Box(name, parent, (start + end) * 0.5f, new Vector3(width, 0.35f, delta.magnitude), material);
        root.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        return root;
    }

    private static void Walkway(string name, Transform parent, Vector3 start, Vector3 end, float width)
    {
        Vector3 delta = end - start;
        Vector3 center = (start + end) * 0.5f + Vector3.down * 0.3f;
        Quaternion rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        GameObject floor = Box(name, parent, center, new Vector3(width, 0.6f, delta.magnitude), Mat("Ground"));
        floor.transform.rotation = rotation;

        Vector3 side = Vector3.Cross(Vector3.up, delta.normalized) * (width * 0.5f);
        GameObject railA = Box(name + "_VisibleRailA", parent, center + side + Vector3.up * 0.65f,
            new Vector3(0.25f, 0.7f, delta.magnitude), Mat("Accent"));
        GameObject railB = Box(name + "_VisibleRailB", parent, center - side + Vector3.up * 0.65f,
            new Vector3(0.25f, 0.7f, delta.magnitude), Mat("Accent"));
        railA.transform.rotation = rotation;
        railB.transform.rotation = rotation;
    }

    private static T MovingBox<T>(string name, Transform parent, Vector3 position, Vector3 size, Material material, bool kinematic)
        where T : Component
    {
        GameObject root = Box(name, parent, position, size, material);
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.isKinematic = kinematic;
        body.useGravity = !kinematic;
        return root.AddComponent<T>();
    }

    private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material,
        bool collider = true, bool trigger = false, bool worldPositionForChild = false)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, worldPositionForChild);
        root.transform.position = position;
        if (collider)
        {
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = trigger;
        }

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "VisualMesh";
        UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.GetComponent<Renderer>().sharedMaterial = material;
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = size;
        return root;
    }

    private static GameObject CylinderVisual(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool worldPosition)
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = name;
        UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.GetComponent<Renderer>().sharedMaterial = material;
        visual.transform.SetParent(parent, worldPosition);
        visual.transform.position = position;
        visual.transform.localScale = scale;
        return visual;
    }

    private static Transform Node(string name, Transform parent)
    {
        GameObject node = new GameObject(name);
        node.transform.SetParent(parent, false);
        return node.transform;
    }

    private static Material Mat(string key) => Materials[key];

    private static Material MaterialForColor(string name, Color color)
    {
        Material material;
        if (Materials.TryGetValue(name, out material)) return material;
        material = LoadOrCreateMaterial(name, color);
        Materials[name] = material;
        return material;
    }

    private static void LoadMaterials()
    {
        Materials.Clear();
        AddMaterial("Ground", new Color(0.42f, 0.44f, 0.48f));
        AddMaterial("Wall", new Color(0.3f, 0.32f, 0.37f));
        AddMaterial("Accent", new Color(0.65f, 0.67f, 0.72f));
        AddMaterial("Dynamic", new Color(0.1f, 0.85f, 0.78f));
        AddMaterial("Torque", new Color(1f, 0.42f, 0.06f));
        AddMaterial("Disable", new Color(0.15f, 0.55f, 1f));
        AddMaterial("Gold", new Color(1f, 0.78f, 0.08f));
        AddMaterial("Slip", new Color(0.1f, 0.48f, 1f));
        AddMaterial("Jump", new Color(0.8f, 0.35f, 1f));
        AddMaterial("Accel", new Color(0.1f, 0.9f, 1f));
        AddMaterial("Danger", new Color(0.8f, 0.08f, 0.08f));
        AddMaterial("Spring", new Color(1f, 0.35f, 0.05f));
        AddMaterial("Gear", new Color(0.08f, 0.9f, 0.82f));
        AddMaterial("Cylinder", new Color(0.95f, 0.15f, 0.78f));
        AddMaterial("Goal", new Color(0.18f, 1f, 0.35f));
        AddMaterial("Inactive", new Color(0.12f, 0.12f, 0.16f));
        AddMaterial("Locked", new Color(0.35f, 0.06f, 0.06f));
        AddMaterial("MusicBoxDark", new Color(0.08f, 0.09f, 0.18f));
        AddMaterial("Rail", new Color(0.22f, 0.24f, 0.27f));
        AddMaterial("Wire", new Color(0.75f, 0.92f, 1f));
        AddMaterial("Checkpoint", new Color(0.2f, 0.95f, 0.5f));
    }

    private static void AddMaterial(string name, Color color)
    {
        Materials[name] = LoadOrCreateMaterial(name, color);
    }

    private static Material LoadOrCreateMaterial(string name, Color color)
    {
        string path = MaterialFolder + "/TW_" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Diffuse") ?? Shader.Find("Hidden/InternalErrorShader");
            material = new Material(shader) { name = "TW_" + name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "Map3ToyWorld");
        EnsureFolder("Assets/Map3ToyWorld", "Scenes");
        EnsureFolder("Assets/Map3ToyWorld", "Materials");
        EnsureFolder("Assets/Map3ToyWorld", "Validation");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string full = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(full)) AssetDatabase.CreateFolder(parent, child);
    }
}
