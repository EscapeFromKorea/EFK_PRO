using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a complete alternate DayDream level from the P1-P12 plan.
/// Exact colliders define gameplay; low-poly FBX models provide the visual shell.
/// </summary>
public static class Map1CodexBuilder
{
    private const string ScenePath = "Assets/Scenes/map1_codex.unity";
    private const string MaterialFolder = "Assets/LevelDesignSystem/Materials/Codex";
    private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
    private static Transform worldRoot, systemsRoot, playersRoot, environmentRoot;
    private static Material grassMat, stoneMat, accentMat, dangerMat, cloudMat, threadMat, glassMat;
    private static Material[] rainbowBridgeMats;

    [MenuItem("Tools/Map1 Codex/Build Complete Level")]
    public static void Build()
    {
        EnsureSceneView();
        EnsureMaterials();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        scene.name = "map1_codex";
        EditorSceneManager.SetActiveScene(scene);

        ConfigureSceneDefaults();
        systemsRoot = NewRoot("00_Systems");
        playersRoot = NewRoot("01_Players");
        worldRoot = NewRoot("02_Level_P1-P12");
        environmentRoot = NewRoot("03_Environment_Art");

        BuildCoreSystems();
        BuildPlayers();
        BuildP01();
        BuildP02();
        BuildP03();
        BuildP04();
        BuildP05();
        BuildP06();
        BuildP07();
        BuildP08();
        BuildP09();
        BuildP10();
        BuildP11();
        BuildP12();
        BuildOutOfBounds();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Validate();
        Debug.Log($"Map1 Codex complete level built → {ScenePath}");
    }

    [MenuItem("Tools/Map1 Codex/Validate Complete Level")]
    public static void Validate()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject level = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "02_Level_P1-P12");
        GameObject environment = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "03_Environment_Art");
        if (level == null || level.transform.childCount != 12)
            throw new InvalidOperationException("Map1 Codex must contain exactly P01-P12 phase roots.");
        if (environment == null)
            throw new InvalidOperationException("Map1 Codex is missing its environment-art root.");
        if (UnityEngine.Object.FindObjectsOfType<PlayerMover>(true).Length != 3)
            throw new InvalidOperationException("Map1 Codex must contain exactly three player shapes.");
        if (UnityEngine.Object.FindObjectsOfType<JumpPad>(true).Length < 7)
            throw new InvalidOperationException("Map1 Codex is missing planned jump pads.");
        if (UnityEngine.Object.FindObjectsOfType<AccelPad>(true).Length < 2)
            throw new InvalidOperationException("Map1 Codex is missing acceleration sections.");
        if (UnityEngine.Object.FindObjectsOfType<CloudTrampoline>(true).Length < 8)
            throw new InvalidOperationException("Map1 Codex is missing the P3 cloud sequence.");
        if (UnityEngine.Object.FindObjectsOfType<ThreadAnchor>(true).Length < 10)
            throw new InvalidOperationException("Map1 Codex is missing thread anchors.");
        if (UnityEngine.Object.FindObjectsOfType<doorPhysics>(true).Length < 3)
            throw new InvalidOperationException("Map1 Codex is missing cooperation gates.");
        if (UnityEngine.Object.FindObjectsOfType<ZeroGravityBubble>(true).Length < 3)
            throw new InvalidOperationException("Map1 Codex is missing the P11 bubble current.");
        if (UnityEngine.Object.FindObjectsOfType<RainbowBridgeSwitch>(true).Length < 3)
            throw new InvalidOperationException("Map1 Codex is missing rainbow bridge routes.");
        if (UnityEngine.Object.FindObjectsOfType<RespawnZone>(true).Length < 8)
            throw new InvalidOperationException("Map1 Codex needs at least eight recovery checkpoints.");

        Transform[] decorations = environment.GetComponentsInChildren<Transform>(true)
            .Where(t => t != environment.transform && PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) != null)
            .Where(t => t.parent != null && t.parent.parent == environment.transform)
            .ToArray();
        if (decorations.Length < 110)
            throw new InvalidOperationException($"Map1 Codex environment is too sparse: {decorations.Length} model instances.");
        foreach (Transform decoration in decorations)
        {
            Vector3 scale = decoration.localScale;
            if (scale.x < 3f || scale.y < 3f || scale.z < 3f)
                throw new InvalidOperationException($"Decoration {decoration.name} is below minimum scale (3,3,3).");
            if (decoration.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException($"Visual decoration {decoration.name} contains a collider.");
        }

        int solidTerrain = level.GetComponentsInChildren<Collider>(true).Count(c => !c.isTrigger);
        if (solidTerrain < 55)
            throw new InvalidOperationException($"Map1 Codex terrain is incomplete: {solidTerrain} solid colliders.");
        Debug.Log($"Map1 Codex validation passed: 12 phases, 3 players, {solidTerrain} solid colliders, " +
                  $"{decorations.Length} scaled environment models, planned gimmick counts present.");
    }

    [MenuItem("Tools/Map1 Codex/Replace Greybox Gameplay Visuals")]
    public static void ReplaceGreyboxGameplayVisuals()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var replaced = new HashSet<GameObject>();
        ReplaceVisualsForComponents(UnityEngine.Object.FindObjectsOfType<AccelPad>(true), "Gimmick_SpeedPad", replaced);
        ReplaceVisualsForComponents(UnityEngine.Object.FindObjectsOfType<JumpPad>(true), "Gimmick_JumpPad", replaced);

        foreach (ScalePad pad in UnityEngine.Object.FindObjectsOfType<ScalePad>(true))
        {
            string model = pad.padType == ScalePad.EPadType.Grow ? "Gimmick_GrowPad" : "Gimmick_ShrinkPad";
            ReplaceGameplayVisual(pad.gameObject, model);
            replaced.Add(pad.gameObject);
        }

        ReplaceVisualsForComponents(UnityEngine.Object.FindObjectsOfType<PadTrigger>(true), "Gimmick_DoorSwitchPad", replaced);
        ReplaceVisualsForComponents(UnityEngine.Object.FindObjectsOfType<ExitWeightPlate>(true), "Gimmick_DoorSwitchPad", replaced);
        ReplaceVisualsForComponents(UnityEngine.Object.FindObjectsOfType<RainbowBridgeSwitch>(true), "Gimmick_BridgeSustainPad", replaced);

        foreach (Transform candidate in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)))
        {
            if (candidate.name.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (candidate.GetComponent<BoxCollider>() == null || candidate.GetComponent<MeshRenderer>() == null) continue;
            ReplaceGameplayVisual(candidate.gameObject, "Wall_LevelBoundary");
            replaced.Add(candidate.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        ValidateGameplayVisuals(replaced);
        Debug.Log($"Map1 Codex gameplay visuals replaced: {replaced.Count} greybox objects now use linked FBX models.");
    }

    public static void CaptureReview()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Capture("01_Overview", new Vector3(150f, 270f, -115f), new Vector3(0f, 4f, 170f), 38f);
        Capture("02_P01-P04", new Vector3(75f, 72f, -45f), new Vector3(0f, 3f, 62f), 43f);
        Capture("03_P05-P08", new Vector3(82f, 75f, 105f), new Vector3(0f, 8f, 215f), 44f);
        Capture("04_P09-P12", new Vector3(85f, 78f, 235f), new Vector3(0f, 8f, 325f), 44f);
        Capture("05_Player_Start", new Vector3(0f, 7f, -14f), new Vector3(0f, 2f, 35f), 55f);
        Capture("06_Player_Finale", new Vector3(0f, 14f, 304f), new Vector3(0f, 12f, 350f), 55f);
        Debug.Log("Map1 Codex review renders captured.");
    }

    // ── P1-P12 ─────────────────────────────────────────────────────────────
    private static void BuildP01()
    {
        Transform p = Phase(1, "Cloudstep_Tutorial");
        Platform(p, "P01_Start_Island", new Vector3(0, -1, 0), new Vector3(16, 2, 12));
        for (int i = 0; i < 4; i++)
        {
            float top = i * 0.65f;
            Platform(p, $"P01_Step_{i + 1}", new Vector3(0, top - 0.5f, 9f + i * 4.2f), new Vector3(4.2f, 1, 3.0f), "Platform_FloatingHex");
            JumpPadAt(p, $"P01_JumpPad_{i + 1}", new Vector3(0, top + 0.1f, 9f + i * 4.2f), 3f);
        }
        Platform(p, "P01_Landing_Island", new Vector3(0, 1.6f, 29f), new Vector3(14, 2, 10));
        Checkpoint(p, "Checkpoint_P01", new Vector3(0, 2.6f, 29f));
        Dress(p, new Vector3(0, 0, 0), new Vector2(16, 12), 0f, 10, 101);
        Dress(p, new Vector3(0, 0, 29), new Vector2(14, 10), 2.6f, 8, 102);
    }

    private static void BuildP02()
    {
        Transform p = Phase(2, "Sleepy_Runway");
        Platform(p, "P02_Runway", new Vector3(0, 1.6f, 43f), new Vector3(12, 2, 18));
        AccelPadAt(p, "P02_Accel", new Vector3(0, 2.7f, 39f), Vector3.forward, 18f);
        JumpPadAt(p, "P02_Launch", new Vector3(0, 2.7f, 49f), 3f);
        Platform(p, "P02_Cube_Landing", new Vector3(-4.5f, 1.6f, 57f), new Vector3(7, 2, 5));
        Platform(p, "P02_Tetra_Landing", new Vector3(0, 2.3f, 63f), new Vector3(6, 2, 5));
        Platform(p, "P02_Sphere_Landing", new Vector3(4.5f, 3.0f, 69f), new Vector3(7, 2, 5));
        Platform(p, "P02_Reunion", new Vector3(0, 2.0f, 76f), new Vector3(16, 2, 8));
        JumpPadAt(p, "P02_ReunionAssist", new Vector3(0, 3.1f, 74f), 2.5f);
        Checkpoint(p, "Checkpoint_P02", new Vector3(0, 3.0f, 76f));
        Dress(p, new Vector3(0, 0, 43), new Vector2(12, 18), 2.6f, 10, 201);
        Dress(p, new Vector3(0, 0, 76), new Vector2(16, 8), 3f, 8, 202);
        FloatingClouds(p, 53f, 72f, 8, 2f);
    }

    private static void BuildP03()
    {
        Transform p = Phase(3, "Weightless_Cloud_River");
        Platform(p, "P03_Entry", new Vector3(0, 2f, 83f), new Vector3(14, 2, 7));
        for (int i = 0; i < 8; i++)
            CloudAt(p, $"P03_Cloud_{i + 1}", new Vector3(i % 2 == 0 ? -1.2f : 1.2f, 2.6f, 89f + i * 3.7f));
        for (int i = 0; i < 9; i++)
            Platform(p, $"P03_CubeStone_{i + 1}", new Vector3(5.2f, 2.25f, 88f + i * 3.15f), new Vector3(2.2f, 0.7f, 2.0f), "Platform_SteppingStone");
        Platform(p, "P03_Exit", new Vector3(0, 2f, 121f), new Vector3(16, 2, 9));
        Checkpoint(p, "Checkpoint_P03", new Vector3(0, 3f, 121f));
        Dress(p, new Vector3(0, 0, 83), new Vector2(14, 7), 3f, 6, 301);
        Dress(p, new Vector3(0, 0, 121), new Vector2(16, 9), 3f, 8, 302);
        FloatingClouds(p, 86f, 118f, 14, 1f);
    }

    private static void BuildP04()
    {
        Transform p = Phase(4, "First_Handshake_Gate");
        Platform(p, "P04_Courtyard", new Vector3(0, 2f, 132f), new Vector3(16, 2, 19));
        DoorSet(p, "P04", new Vector3(0, 5.5f, 139f), true);
        Platform(p, "P04_Exit", new Vector3(0, 2f, 146f), new Vector3(14, 2, 7));
        Checkpoint(p, "Checkpoint_P04", new Vector3(0, 3f, 146f));
        Dress(p, new Vector3(0, 0, 132), new Vector2(16, 17), 3f, 12, 401);
    }

    private static void BuildP05()
    {
        Transform p = Phase(5, "Thread_Swing_Canyon");
        Platform(p, "P05_Entry", new Vector3(0, 2f, 154f), new Vector3(14, 2, 8));
        for (int i = 0; i < 5; i++)
            ThreadAnchorAt(p, $"P05_SwingAnchor_{i + 1}", new Vector3(i % 2 == 0 ? -1.5f : 1.5f, 8f + (i % 2), 159f + i * 5f), 4.4f);
        for (int i = 0; i < 9; i++)
            Platform(p, $"P05_Cube_Bypass_{i + 1}", new Vector3(6.3f, 2.3f + i * 0.18f, 158f + i * 3.15f), new Vector3(2.4f, 0.8f, 2.0f), "Platform_SteppingStone");
        Platform(p, "P05_Reunion", new Vector3(0, 2.8f, 184f), new Vector3(16, 2, 9));
        Checkpoint(p, "Checkpoint_P05", new Vector3(0, 3.8f, 184f));
        Dress(p, new Vector3(0, 0, 154), new Vector2(14, 8), 3f, 7, 501);
        Dress(p, new Vector3(0, 0, 184), new Vector2(16, 9), 3.8f, 8, 502);
        FloatingClouds(p, 157f, 181f, 10, -1f);
    }

    private static void BuildP06()
    {
        Transform p = Phase(6, "Sleepwalker_Wall");
        Platform(p, "P06_Base", new Vector3(0, 2.8f, 193f), new Vector3(18, 2, 10));
        VisualBox(p, "P06_ClimbWall", new Vector3(0, 9f, 199f), new Vector3(12, 12, 1.2f), stoneMat, "Wall_LevelBoundary");
        ThreadAnchorAt(p, "P06_WallMarker_Low", new Vector3(-2.5f, 7f, 198.2f), 3.2f);
        ThreadAnchorAt(p, "P06_WallMarker_High", new Vector3(2.5f, 12f, 198.2f), 3.2f);
        Platform(p, "P06_Top", new Vector3(0, 13f, 204f), new Vector3(16, 2, 9));
        doorPhysics liftGate = SimpleDoor(p, "P06_TeamLiftGate", new Vector3(6.2f, 7.3f, 199f), new Vector3(4.2f, 7f, 1f));
        PressurePad(p, "P06_Tetra_TopSwitch", new Vector3(0, 14.1f, 204f), liftGate);
        for (int i = 0; i < 4; i++)
            Platform(p, $"P06_Descent_{i + 1}", new Vector3(-6f, 11.5f - i * 2.3f, 210f + i * 3.2f), new Vector3(5f, 1.2f, 3f));
        Checkpoint(p, "Checkpoint_P06", new Vector3(0, 14f, 204f));
        Dress(p, new Vector3(0, 0, 193), new Vector2(18, 10), 3.8f, 10, 601);
        Dress(p, new Vector3(0, 0, 204), new Vector2(16, 9), 14f, 8, 602);
    }

    private static void BuildP07()
    {
        Transform p = Phase(7, "Wake_The_Giant");
        Platform(p, "P07_Entry", new Vector3(0, 4.8f, 221f), new Vector3(16, 2, 10));
        Platform(p, "P07_Cube_Pedestal", new Vector3(0, 6.2f, 228f), new Vector3(3.2f, 1, 3.2f), "Platform_FloatingHex");
        ThreadAnchorAt(p, "P07_CatchAnchor", new Vector3(0, 11f, 231f), 4.5f);
        Platform(p, "P07_Landing", new Vector3(0, 4.8f, 239f), new Vector3(16, 2, 9));
        RainbowBridge(p, "P07_Cube_ReturnBridge", new Vector3(5f, 5.9f, 225f), 6, true, 0f);
        Checkpoint(p, "Checkpoint_P07", new Vector3(0, 5.8f, 239f));
        Dress(p, new Vector3(0, 0, 221), new Vector2(16, 10), 5.8f, 8, 701);
        Dress(p, new Vector3(0, 0, 239), new Vector2(16, 9), 5.8f, 8, 702);
    }

    private static void BuildP08()
    {
        Transform p = Phase(8, "Dream_On_The_Scale");
        Platform(p, "P08_Gate_Island", new Vector3(0, 4.8f, 249f), new Vector3(18, 2, 12));
        WeightGate(p, "P08", new Vector3(0, 8.3f, 255f), false);
        for (int i = 0; i < 6; i++)
            Platform(p, $"P08_Cube_Bypass_{i + 1}", new Vector3(7f, 5.15f, 252f + i * 3.1f), new Vector3(2.4f, 0.7f, 2f), "Platform_SteppingStone");
        Platform(p, "P08_Reunion", new Vector3(0, 4.8f, 268f), new Vector3(18, 2, 9));
        Checkpoint(p, "Checkpoint_P08", new Vector3(0, 5.8f, 268f));
        Dress(p, new Vector3(0, 0, 249), new Vector2(18, 12), 5.8f, 10, 801);
        Dress(p, new Vector3(0, 0, 268), new Vector2(18, 9), 5.8f, 8, 802);
    }

    private static void BuildP09()
    {
        Transform p = Phase(9, "Thread_Tug_Bridge");
        Platform(p, "P09_Entry", new Vector3(0, 4.8f, 277f), new Vector3(16, 2, 8));
        Platform(p, "P09_Exit", new Vector3(0, 4.8f, 292f), new Vector3(16, 2, 8));
        ThreadBridgeAt(p, "P09_WeightedThreadBridge", new Vector3(0, 6.1f, 281f), new Vector3(0, 6.1f, 288f));
        Checkpoint(p, "Checkpoint_P09", new Vector3(0, 5.8f, 292f));
        Dress(p, new Vector3(0, 0, 277), new Vector2(16, 8), 5.8f, 8, 901);
        Dress(p, new Vector3(0, 0, 292), new Vector2(16, 8), 5.8f, 8, 902);
        FloatingClouds(p, 280f, 290f, 6, 1f);
    }

    private static void BuildP10()
    {
        Transform p = Phase(10, "Only_In_Dreams_Bridges");
        Platform(p, "P10_Fork", new Vector3(0, 4.8f, 301f), new Vector3(20, 2, 9));
        Platform(p, "P10_Reunion", new Vector3(0, 4.8f, 320f), new Vector3(20, 2, 9));
        RainbowBridge(p, "P10_CoopBridge", new Vector3(-4.5f, 5.9f, 305f), 6, true, 0f);
        RainbowBridge(p, "P10_TimedBridge", new Vector3(4.5f, 5.9f, 305f), 6, false, 3.5f);
        Checkpoint(p, "Checkpoint_P10", new Vector3(0, 5.8f, 320f));
        Dress(p, new Vector3(0, 0, 301), new Vector2(20, 9), 5.8f, 10, 1001);
        Dress(p, new Vector3(0, 0, 320), new Vector2(20, 9), 5.8f, 10, 1002);
    }

    private static void BuildP11()
    {
        Transform p = Phase(11, "Misty_ZeroG_Current");
        Platform(p, "P11_Entry", new Vector3(0, 4.8f, 329f), new Vector3(16, 2, 8));
        Platform(p, "P11_FloorSafety", new Vector3(0, 2.8f, 343f), new Vector3(5, 1, 20));
        for (int i = 0; i < 3; i++)
        {
            BubbleAt(p, $"P11_Bubble_{i + 1}", new Vector3(0, 8f, 336f + i * 7f), 4.5f);
            ThreadAnchorAt(p, $"P11_BubbleAnchor_{i + 1}", new Vector3(i % 2 == 0 ? -2f : 2f, 11f, 338f + i * 7f), 4.8f);
        }
        Platform(p, "P11_Exit", new Vector3(0, 4.8f, 357f), new Vector3(16, 2, 8));
        Checkpoint(p, "Checkpoint_P11", new Vector3(0, 5.8f, 357f));
        Dress(p, new Vector3(0, 0, 329), new Vector2(16, 8), 5.8f, 8, 1101);
        Dress(p, new Vector3(0, 0, 357), new Vector2(16, 8), 5.8f, 8, 1102);
        FloatingClouds(p, 333f, 354f, 12, 2f);
    }

    private static void BuildP12()
    {
        Transform p = Phase(12, "Dream_Summit_Finale");
        Platform(p, "P12_Staging", new Vector3(0, 4.8f, 367f), new Vector3(20, 2, 12));
        ScalePadAt(p, "P12_ShrinkPad", new Vector3(0, 5.9f, 364f), ScalePad.EPadType.Shrink, dangerMat);
        VisualBox(p, "P12_TunnelWall_L", new Vector3(-3.1f, 8f, 375f), new Vector3(5.45f, 6f, 12f), stoneMat, "Wall_LevelBoundary");
        VisualBox(p, "P12_TunnelWall_R", new Vector3(3.1f, 8f, 375f), new Vector3(5.45f, 6f, 12f), stoneMat, "Wall_LevelBoundary");
        Platform(p, "P12_Mid", new Vector3(0, 4.8f, 385f), new Vector3(18, 2, 9));
        VisualBox(p, "P12_FinalClimbWall", new Vector3(0, 10f, 391f), new Vector3(12, 10, 1f), stoneMat, "Wall_LevelBoundary");
        ThreadAnchorAt(p, "P12_ClimbMarker_Low", new Vector3(-2f, 8f, 390.4f), 3.5f);
        ThreadAnchorAt(p, "P12_ClimbMarker_High", new Vector3(2f, 13f, 390.4f), 3.5f);
        Platform(p, "P12_UpperDeck", new Vector3(0, 14f, 397f), new Vector3(18, 2, 10));
        WeightGate(p, "P12", new Vector3(-4f, 17.5f, 399f), true);
        AccelPadAt(p, "P12_FinalAccel", new Vector3(0, 15.1f, 400f), Vector3.forward, 20f);
        JumpPadAt(p, "P12_FinalJump", new Vector3(0, 15.1f, 403f), 5f);
        Platform(p, "P12_Summit", new Vector3(0, 19f, 415f), new Vector3(20, 3, 16));
        Checkpoint(p, "Checkpoint_P12", new Vector3(0, 20.5f, 415f));
        Dress(p, new Vector3(0, 0, 367), new Vector2(20, 12), 5.8f, 12, 1201);
        Dress(p, new Vector3(0, 0, 397), new Vector2(18, 10), 15f, 10, 1202);
        Dress(p, new Vector3(0, 0, 415), new Vector2(20, 16), 20.5f, 18, 1203);
        PlaceDeco(p, "Deco_RainbowArch", "P12_Summit_Rainbow", new Vector3(0, 20.5f, 419f), 6f, 0f, true);
        PlaceDeco(p, "Deco_MoonStarPillars", "P12_Summit_Crown", new Vector3(0, 20.5f, 424f), 6f, 0f, true);
    }

    // ── systems and gameplay helpers ───────────────────────────────────────
    private static void BuildCoreSystems()
    {
        GameObject switcher = new GameObject("PlayerControlSwitcher");
        switcher.transform.SetParent(systemsRoot);
        switcher.AddComponent<PlayerControlSwitcher>();
        GameObject respawn = new GameObject("RespawnController");
        respawn.transform.SetParent(systemsRoot);
        RespawnController rc = respawn.AddComponent<RespawnController>();
        rc.killY = -24f;
        GameObject thread = new GameObject("DreamThreadController");
        thread.transform.SetParent(systemsRoot);
        DreamThreadController controller = thread.AddComponent<DreamThreadController>();
        LineRenderer line = thread.GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.widthMultiplier = controller.lineWidth;
        line.sharedMaterial = threadMat;
        line.enabled = false;
        new GameObject("DreamThreadPinPlacer", typeof(ThreadPinPlacer)).transform.SetParent(systemsRoot);
        new GameObject("DreamThreadCubeAnchor", typeof(ThreadCubeAnchor)).transform.SetParent(systemsRoot);
    }

    private static void BuildPlayers()
    {
        Vector3[] positions = { new Vector3(-1.8f, 1f, -2f), new Vector3(0f, 1f, -2f), new Vector3(1.8f, 1f, -2f) };
        string[] methods = { "CreateCubePlayer", "CreateSpherePlayer", "CreateTetrahedronPlayer" };
        Color[] colors = { new Color(0.95f, 0.65f, 0.25f), new Color(0.3f, 0.8f, 1f), new Color(0.9f, 0.35f, 0.8f) };
        for (int i = 0; i < methods.Length; i++)
        {
            SetPivot(positions[i]);
            HashSet<GameObject> before = SceneManager.GetActiveScene().GetRootGameObjects().ToHashSet();
            MethodInfo method = typeof(PlayerObjectMenuItem).GetMethod(methods[i], BindingFlags.Static | BindingFlags.NonPublic);
            method.Invoke(null, null);
            GameObject player = SceneManager.GetActiveScene().GetRootGameObjects().First(g => !before.Contains(g) && g.name.StartsWith("Player_"));
            player.transform.position = positions[i];
            player.transform.SetParent(playersRoot, true);
            // This level runs along world +Z and the camera stays behind it at -Z.
            // PlayerMover's project default is +90 degrees for older side-view scenes;
            // leaving that value here makes A move forward and W move sideways.
            PlayerMover mover = player.GetComponent<PlayerMover>();
            if (mover != null) mover.inputYawOffset = 0f;
            Renderer visual = player.GetComponentsInChildren<Renderer>(true).FirstOrDefault();
            if (visual != null) visual.sharedMaterial = MaterialAsset($"Player_{i}", colors[i]);
        }
        Camera camera = Camera.main;
        PlayerFollowCamera follow = camera.GetComponent<PlayerFollowCamera>() ?? camera.gameObject.AddComponent<PlayerFollowCamera>();
        follow.offset = new Vector3(0f, 8f, -13f);
        follow.cameraYawOffset = 0f;
        camera.transform.position = new Vector3(0f, 8f, -14f);
        camera.farClipPlane = 600f;
    }

    private static void BuildOutOfBounds()
    {
        GameObject volume = new GameObject("Codex_OutOfBoundsVolume");
        volume.transform.SetParent(systemsRoot);
        volume.transform.position = new Vector3(0, -12f, 205f);
        BoxCollider collider = volume.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(120, 12, 470);
        volume.AddComponent<OutOfBoundsVolume>();
    }

    private static Transform Phase(int number, string name)
    {
        GameObject go = new GameObject($"P{number:00}_{name}");
        go.transform.SetParent(worldRoot, false);
        GameObject art = new GameObject($"P{number:00}_Art");
        art.transform.SetParent(environmentRoot, false);
        return go.transform;
    }

    private static GameObject Platform(Transform parent, string name, Vector3 center, Vector3 size, string model = "Platform_GrassBlock")
    {
        GameObject wrapper = new GameObject(name);
        wrapper.transform.SetParent(parent, false);
        GameObject visual = InstantiateModel(model, name + "_Visual", wrapper.transform);
        StripColliders(visual);
        FitRendererBounds(visual, center, size);
        GameObject collision = new GameObject(name + "_Collision");
        collision.transform.SetParent(wrapper.transform, false);
        collision.transform.position = center;
        BoxCollider box = collision.AddComponent<BoxCollider>();
        box.size = size;
        return wrapper;
    }

    private static GameObject SolidBox(Transform parent, string name, Vector3 center, Vector3 size, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    private static GameObject VisualBox(Transform parent, string name, Vector3 center, Vector3 size, Material material, string model)
    {
        GameObject box = SolidBox(parent, name, center, size, material);
        ReplaceGameplayVisual(box, model);
        return box;
    }

    private static void JumpPadAt(Transform parent, string name, Vector3 center, float height)
    {
        GameObject pad = SolidBox(parent, name, center, new Vector3(2.4f, 0.2f, 2.4f), accentMat);
        JumpPad jump = pad.AddComponent<JumpPad>();
        jump.jumpHeight = height;
        ReplaceGameplayVisual(pad, "Gimmick_JumpPad");
    }

    private static void AccelPadAt(Transform parent, string name, Vector3 center, Vector3 direction, float speed)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = center;
        root.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(2.8f, 0.2f, 3.2f);
        AccelPad accel = root.AddComponent<AccelPad>();
        accel.boostSpeed = speed;
        ReplaceGameplayVisual(root, "Gimmick_SpeedPad");
    }

    private static void CloudAt(Transform parent, string name, Vector3 center)
    {
        GameObject cloud = new GameObject(name);
        cloud.transform.SetParent(parent, false);
        cloud.transform.position = center;
        BoxCollider support = cloud.AddComponent<BoxCollider>();
        support.size = new Vector3(3.2f, 0.8f, 2.8f);
        cloud.AddComponent<CloudTrampoline>();
        Vector3[] puffs = { new Vector3(-0.8f, 0, 0), new Vector3(0, 0.2f, 0.25f), new Vector3(0.8f, 0, 0), new Vector3(-0.25f, 0.1f, -0.5f) };
        foreach (Vector3 pos in puffs)
        {
            GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            puff.name = "Cloud_Puff";
            puff.transform.SetParent(cloud.transform, false);
            puff.transform.localPosition = pos;
            puff.transform.localScale = new Vector3(1.5f, 0.75f, 1.25f);
            puff.GetComponent<Renderer>().sharedMaterial = cloudMat;
            UnityEngine.Object.DestroyImmediate(puff.GetComponent<Collider>());
        }
    }

    private static ThreadAnchor ThreadAnchorAt(Transform parent, string name, Vector3 position, float range)
    {
        GameObject anchor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        anchor.name = name;
        anchor.transform.SetParent(parent, false);
        anchor.transform.position = position;
        anchor.transform.localScale = Vector3.one * 0.42f;
        anchor.GetComponent<Renderer>().sharedMaterial = threadMat;
        UnityEngine.Object.DestroyImmediate(anchor.GetComponent<Collider>());
        ThreadAnchor component = anchor.AddComponent<ThreadAnchor>();
        component.connectRange = range;
        return component;
    }

    private static void ThreadBridgeAt(Transform parent, string name, Vector3 a, Vector3 b)
    {
        ThreadAnchor anchorA = ThreadAnchorAt(parent, name + "_A", a, 4f);
        ThreadAnchor anchorB = ThreadAnchorAt(parent, name + "_B", b, 4f);
        GameObject bridgeObject = new GameObject(name);
        bridgeObject.transform.SetParent(parent, false);
        ThreadBridge bridge = bridgeObject.AddComponent<ThreadBridge>();
        bridge.anchorA = anchorA;
        bridge.anchorB = anchorB;
        bridge.maxSpan = Vector3.Distance(a, b) + 1f;
        bridge.segmentWidth = 0.8f;
        LineRenderer line = bridgeObject.GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.sharedMaterial = threadMat;
        line.widthMultiplier = 0.08f;
    }

    private static doorPhysics SimpleDoor(Transform parent, string name, Vector3 center, Vector3 size)
    {
        GameObject door = SolidBox(parent, name, center, size, dangerMat);
        BoxCollider trigger = door.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = Vector3.one * 1.08f;
        Rigidbody rb = door.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        doorPhysics physics = door.AddComponent<doorPhysics>();
        physics.doorTargetYOffset = size.y + 0.5f;
        return physics;
    }

    private static GameObject PressurePad(Transform parent, string name, Vector3 center, doorPhysics target)
    {
        GameObject pad = SolidBox(parent, name, center, new Vector3(2.4f, 0.18f, 2.4f), accentMat);
        pad.GetComponent<Collider>().isTrigger = true;
        PadTrigger trigger = pad.AddComponent<PadTrigger>();
        trigger.doorPhysicsScript = target;
        ReplaceGameplayVisual(pad, "Gimmick_DoorSwitchPad");
        return pad;
    }

    private static void DoorSet(Transform parent, string prefix, Vector3 doorCenter, bool includeLever)
    {
        doorPhysics door = SimpleDoor(parent, prefix + "_Door", doorCenter, new Vector3(8f, 5f, 0.7f));
        float floorY = doorCenter.y - 2.5f;
        PressurePad(parent, prefix + "_HoldPad", new Vector3(3.5f, floorY + 0.09f, doorCenter.z - 4f), door);
        if (!includeLever) return;
        GameObject pivot = SolidBox(parent, prefix + "_LeverPivot", new Vector3(-3.5f, floorY + 1.25f, doorCenter.z - 4f), new Vector3(0.3f, 0.3f, 0.3f), stoneMat);
        GameObject head = SolidBox(pivot.transform, prefix + "_LeverHead", pivot.transform.position + new Vector3(1.1f, 1f, 0), new Vector3(2.2f, 0.35f, 0.35f), accentMat);
        LeverHead lever = head.AddComponent<LeverHead>();
        lever.leverPivot = pivot.transform;
        door.leverHead = lever;
        SolidBox(parent, prefix + "_LeverBase", pivot.transform.position + Vector3.down * 1.1f, new Vector3(1.2f, 2.2f, 1.2f), stoneMat);
    }

    private static void WeightGate(Transform parent, string prefix, Vector3 doorCenter, bool latch)
    {
        doorPhysics door = SimpleDoor(parent, prefix + "_WeightDoor", doorCenter, new Vector3(8f, 5f, 0.7f));
        float floorY = doorCenter.y - 2.5f;
        GameObject plate = SolidBox(parent, prefix + "_WeightPlate", new Vector3(doorCenter.x, floorY + 0.09f, doorCenter.z - 4f), new Vector3(3f, 0.18f, 3f), accentMat);
        plate.GetComponent<Collider>().isTrigger = true;
        ExitWeightPlate weight = plate.AddComponent<ExitWeightPlate>();
        weight.targetDoor = door;
        weight.requiredWeight = 2.75f;
        weight.latchOpen = latch;
        ReplaceGameplayVisual(plate, "Gimmick_DoorSwitchPad");
    }

    private static void RainbowBridge(Transform parent, string name, Vector3 switchCenter, int segments, bool hold, float duration)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        GameObject pad = SolidBox(root.transform, "Switch", switchCenter, new Vector3(2f, 0.18f, 2f), accentMat);
        pad.GetComponent<Collider>().isTrigger = true;
        RainbowBridgeSwitch sw = pad.AddComponent<RainbowBridgeSwitch>();
        sw.activatorRequiresHold = hold;
        if (!hold) sw.activeDurationSec = duration;
        ReplaceGameplayVisual(pad, "Gimmick_BridgeSustainPad");
        var targets = new GameObject[segments];
        for (int i = 0; i < segments; i++)
        {
            Material segmentMaterial = rainbowBridgeMats[i % rainbowBridgeMats.Length];
            GameObject segment = SolidBox(root.transform, $"Segment_{i + 1}", switchCenter + new Vector3(0, 0, 2.6f + i * 2.1f), new Vector3(2.5f, 0.25f, 2.2f), segmentMaterial);
            segment.GetComponent<Collider>().enabled = false;
            segment.GetComponent<Renderer>().enabled = false;
            targets[i] = segment;
        }
        sw.targetObjects = targets;
    }

    private static void BubbleAt(Transform parent, string name, Vector3 center, float radius)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = center;
        SphereCollider trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = radius;
        root.AddComponent<ZeroGravityBubble>();
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Boundary";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = Vector3.one * radius * 2f;
        visual.GetComponent<Renderer>().sharedMaterial = glassMat;
        UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
    }

    private static void ScalePadAt(Transform parent, string name, Vector3 center, ScalePad.EPadType type, Material mat)
    {
        GameObject pad = SolidBox(parent, name, center, new Vector3(2f, 0.18f, 2f), mat);
        pad.GetComponent<Collider>().isTrigger = true;
        ScalePad scale = pad.AddComponent<ScalePad>();
        scale.padType = type;
        scale.defaultColor = mat.color;
        ReplaceGameplayVisual(pad, type == ScalePad.EPadType.Grow ? "Gimmick_GrowPad" : "Gimmick_ShrinkPad");
    }

    private static void Checkpoint(Transform parent, string name, Vector3 floorPoint)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = floorPoint;
        BoxCollider zone = root.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.size = new Vector3(6, 18, 6);
        zone.center = new Vector3(0, 9, 0);
        RespawnZone respawn = root.AddComponent<RespawnZone>();
        GameObject pole = SolidBox(root.transform, "Pole", floorPoint + new Vector3(-2.4f, 2f, 0), new Vector3(0.15f, 4, 0.15f), stoneMat);
        UnityEngine.Object.DestroyImmediate(pole.GetComponent<Collider>());
        GameObject flag = SolidBox(root.transform, "Flag", floorPoint + new Vector3(-1.8f, 3.5f, 0), new Vector3(1.2f, 0.7f, 0.08f), accentMat);
        UnityEngine.Object.DestroyImmediate(flag.GetComponent<Collider>());
        respawn.flagRenderer = flag.GetComponent<Renderer>();
    }

    // ── environment helpers ────────────────────────────────────────────────
    private static void Dress(Transform phase, Vector3 center, Vector2 footprint, float topY, int count, int seed)
    {
        Transform art = environmentRoot.Find(phase.name.Substring(0, 3) + "_Art");
        string[] models = { "Deco_DreamTree", "Deco_MushroomTree", "Deco_CrystalCluster", "Deco_LanternPost", "Deco_MoonPedestal", "Deco_BannerPost_Moon", "Deco_BannerPost_Star", "Deco_Signpost" };
        for (int i = 0; i < count; i++)
        {
            bool left = i % 2 == 0;
            float t = (i / 2f + 0.7f) / (Mathf.Ceil(count / 2f) + 0.4f);
            float x = center.x + (left ? -1 : 1) * Mathf.Max(footprint.x * 0.5f - 1.0f, 1.5f);
            float z = center.z - footprint.y * 0.42f + footprint.y * 0.84f * t;
            float jitter = ((seed * 31 + i * 17) % 11 - 5) * 0.08f;
            float scale = 3f + ((seed + i * 7) % 5) * 0.35f;
            PlaceDeco(art, models[(seed + i) % models.Length], $"{phase.name}_Deco_{i:00}", new Vector3(x + jitter, topY, z), scale, left ? 35f : -35f, true);
        }
    }

    private static void FloatingClouds(Transform phase, float zStart, float zEnd, int count, float baseY)
    {
        Transform art = environmentRoot.Find(phase.name.Substring(0, 3) + "_Art");
        string[] models = { "Deco_Cloud_A", "Deco_Cloud_B", "Deco_Cloud_C", "Deco_Cloud_D", "Deco_FloatingRock" };
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : i / (float)(count - 1);
            float x = (i % 2 == 0 ? -1 : 1) * (9f + (i % 3) * 2.5f);
            float y = baseY + (i % 4) * 1.6f;
            PlaceDeco(art, models[i % models.Length], $"{phase.name}_Vista_{i:00}", new Vector3(x, y, Mathf.Lerp(zStart, zEnd, t)), 4.5f + (i % 3) * 0.7f, i * 29f, false);
        }
    }

    private static void PlaceDeco(Transform parent, string model, string name, Vector3 position, float scale, float yaw, bool grounded)
    {
        GameObject go = InstantiateModel(model, name, parent);
        StripColliders(go);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0, yaw, 0);
        go.transform.localScale = Vector3.one * Mathf.Max(3f, scale);
        if (grounded)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = CombinedBounds(renderers);
            go.transform.position += Vector3.up * (position.y - bounds.min.y);
        }
    }

    private static GameObject InstantiateModel(string model, string name, Transform parent)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/{model}.fbx");
        if (asset == null) throw new InvalidOperationException($"Missing model Assets/Models/{model}.fbx");
        GameObject go = PrefabUtility.InstantiatePrefab(asset) as GameObject;
        go.name = name;
        go.transform.SetParent(parent, true);
        return go;
    }

    private static void ReplaceVisualsForComponents<T>(IEnumerable<T> components, string model, HashSet<GameObject> replaced)
        where T : Component
    {
        foreach (T component in components)
        {
            if (component == null || !replaced.Add(component.gameObject)) continue;
            ReplaceGameplayVisual(component.gameObject, model);
        }
    }

    private static void ReplaceGameplayVisual(GameObject host, string model)
    {
        BoxCollider gameplayCollider = host.GetComponent<BoxCollider>();
        if (gameplayCollider == null)
            throw new InvalidOperationException($"{host.name} needs a BoxCollider before its visual can be replaced.");

        Transform oldVisual = host.transform.Find("GameplayVisual");
        if (oldVisual != null)
            UnityEngine.Object.DestroyImmediate(oldVisual.gameObject);

        foreach (Renderer renderer in host.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        GameObject visual = InstantiateModel(model, "GameplayVisual", host.transform);
        StripColliders(visual);
        FitVisualToCollider(visual, gameplayCollider);
    }

    private static void FitVisualToCollider(GameObject visual, BoxCollider target)
    {
        Transform host = target.transform;
        visual.transform.SetParent(host, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        Bounds source = RendererBoundsInLocalSpace(visual.GetComponentsInChildren<Renderer>(true), host);
        visual.transform.localScale = new Vector3(
            target.size.x / Mathf.Max(source.size.x, 0.0001f),
            target.size.y / Mathf.Max(source.size.y, 0.0001f),
            target.size.z / Mathf.Max(source.size.z, 0.0001f));

        Bounds fitted = RendererBoundsInLocalSpace(visual.GetComponentsInChildren<Renderer>(true), host);
        visual.transform.localPosition += target.center - fitted.center;
    }

    private static Bounds RendererBoundsInLocalSpace(Renderer[] renderers, Transform space)
    {
        if (renderers.Length == 0) throw new InvalidOperationException("Model has no renderer.");
        bool initialized = false;
        Bounds result = default;
        foreach (Renderer renderer in renderers)
        {
            Bounds world = renderer.bounds;
            Vector3 min = world.min;
            Vector3 max = world.max;
            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                Vector3 corner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                Vector3 local = space.InverseTransformPoint(corner);
                if (!initialized) { result = new Bounds(local, Vector3.zero); initialized = true; }
                else result.Encapsulate(local);
            }
        }
        return result;
    }

    private static void ValidateGameplayVisuals(IEnumerable<GameObject> hosts)
    {
        foreach (GameObject host in hosts)
        {
            if (host == null || host.GetComponent<BoxCollider>() == null)
                throw new InvalidOperationException("Gameplay visual replacement removed a required collider.");
            Transform visual = host.transform.Find("GameplayVisual");
            if (visual == null || PrefabUtility.GetCorrespondingObjectFromSource(visual.gameObject) == null)
                throw new InvalidOperationException($"{host.name} is missing its linked FBX GameplayVisual.");
            if (visual.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException($"{host.name} GameplayVisual must remain visual-only.");
            if (visual.GetComponentsInChildren<Renderer>(true).Length == 0)
                throw new InvalidOperationException($"{host.name} GameplayVisual has no renderer.");
        }
    }

    private static void FitRendererBounds(GameObject visual, Vector3 targetCenter, Vector3 targetSize)
    {
        visual.transform.position = Vector3.zero;
        visual.transform.rotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        Bounds original = CombinedBounds(visual.GetComponentsInChildren<Renderer>(true));
        visual.transform.localScale = new Vector3(targetSize.x / original.size.x, targetSize.y / original.size.y, targetSize.z / original.size.z);
        Bounds scaled = CombinedBounds(visual.GetComponentsInChildren<Renderer>(true));
        visual.transform.position += targetCenter - scaled.center;
    }

    private static Bounds CombinedBounds(Renderer[] renderers)
    {
        if (renderers.Length == 0) throw new InvalidOperationException("Model has no renderer.");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void StripColliders(GameObject root)
    {
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.DestroyImmediate(collider);
    }

    // ── setup, materials, capture ──────────────────────────────────────────
    private static void ConfigureSceneDefaults()
    {
        Camera camera = Camera.main;
        camera.transform.position = new Vector3(0, 8, -14);
        camera.transform.rotation = Quaternion.Euler(18f, 0, 0);
        camera.farClipPlane = 600f;
        Light light = UnityEngine.Object.FindObjectOfType<Light>();
        if (light != null)
        {
            light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.92f, 0.82f);
        }
        RenderSettings.ambientLight = new Color(0.34f, 0.38f, 0.5f);
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.62f, 0.74f, 0.9f);
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 90f;
        RenderSettings.fogEndDistance = 260f;
    }

    private static void EnsureMaterials()
    {
        EnsureFolder("Assets/LevelDesignSystem", "Materials");
        EnsureFolder("Assets/LevelDesignSystem/Materials", "Codex");
        grassMat = MaterialAsset("Grass", new Color(0.32f, 0.72f, 0.3f));
        stoneMat = MaterialAsset("Stone", new Color(0.43f, 0.38f, 0.54f));
        accentMat = MaterialAsset("Accent", new Color(0.95f, 0.72f, 0.22f));
        dangerMat = MaterialAsset("Danger", new Color(0.8f, 0.28f, 0.38f));
        cloudMat = MaterialAsset("Cloud", new Color(0.92f, 0.96f, 1f));
        threadMat = MaterialAsset("Thread", new Color(0.4f, 0.9f, 1f));
        glassMat = MaterialAsset("DreamGlass", new Color(0.45f, 0.78f, 1f, 0.28f), true);
        rainbowBridgeMats = new[]
        {
            MaterialAsset("DreamGlass_Red", new Color(1f, 0.18f, 0.22f, 1f), true),
            MaterialAsset("DreamGlass_Orange", new Color(1f, 0.48f, 0.08f, 1f), true),
            MaterialAsset("DreamGlass_Yellow", new Color(1f, 0.88f, 0.1f, 1f), true),
            MaterialAsset("DreamGlass_Green", new Color(0.12f, 0.82f, 0.3f, 1f), true),
            MaterialAsset("DreamGlass_Blue", new Color(0.12f, 0.5f, 1f, 1f), true),
            MaterialAsset("DreamGlass_Violet", new Color(0.62f, 0.2f, 1f, 1f), true)
        };
    }

    private static Material MaterialAsset(string name, Color color, bool transparent = false)
    {
        string key = name + transparent;
        if (Materials.TryGetValue(key, out Material cached)) return cached;
        string path = $"{MaterialFolder}/Codex_{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        if (transparent)
        {
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = 3000;
        }
        EditorUtility.SetDirty(material);
        Materials[key] = material;
        return material;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
    }

    private static Transform NewRoot(string name)
    {
        GameObject go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
        return go.transform;
    }

    private static void EnsureSceneView()
    {
        if (SceneView.lastActiveSceneView == null) EditorWindow.GetWindow<SceneView>();
    }

    private static void SetPivot(Vector3 position)
    {
        EnsureSceneView();
        SceneView.lastActiveSceneView.pivot = position;
    }

    private static void Capture(string label, Vector3 position, Vector3 target, float fov)
    {
        GameObject go = new GameObject("Codex_Review_Camera");
        Camera camera = go.AddComponent<Camera>();
        camera.transform.position = position;
        camera.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
        camera.fieldOfView = fov;
        camera.farClipPlane = 700f;
        camera.clearFlags = CameraClearFlags.Skybox;
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        image.Apply();
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string output = Path.Combine(projectRoot, "Temp", "Map1CodexReview");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, $"Map1Codex_{label}.png"), image.EncodeToPNG());
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(go);
    }
}
