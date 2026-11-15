using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the DayDream environment art pass around Map1's existing greybox.
/// Model bottoms are aligned from their real renderer bounds instead of guessed pivots.
/// </summary>
public static class Map1EnvironmentDecorator
{
    private const string ScenePath = "Assets/Scenes/Map1.unity";
    private const string RootName = "Environment_Decoration";
    private const int ExpectedCount = 120;

    private sealed class Placement
    {
        public string zone;
        public string model;
        public string name;
        public Vector3 position;
        public Vector3 scale;
        public float yaw;
        public bool grounded;
    }

    [MenuItem("Tools/Map1/Build Environment Decoration")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject existing = scene.GetRootGameObjects().FirstOrDefault(go => go.name == RootName);
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing);

        var root = new GameObject(RootName);
        var zones = new Dictionary<string, Transform>();
        foreach (Placement spec in CreatePlacements())
        {
            if (!zones.TryGetValue(spec.zone, out Transform zone))
            {
                zone = new GameObject(spec.zone).transform;
                zone.SetParent(root.transform, false);
                zones.Add(spec.zone, zone);
            }
            Place(zone, spec);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Validate();
        Debug.Log($"Map1 DayDream environment built: {ExpectedCount} aligned model instances in {zones.Count} zones.");
    }

    [MenuItem("Tools/Map1/Validate Environment Decoration")]
    public static void Validate()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject root = scene.GetRootGameObjects().FirstOrDefault(go => go.name == RootName);
        if (root == null)
            throw new InvalidOperationException($"Map1 is missing {RootName}.");

        Placement[] specs = CreatePlacements().ToArray();
        Dictionary<string, Placement> byName = specs.ToDictionary(p => p.name);
        Transform[] instances = root.transform.Cast<Transform>().SelectMany(z => z.Cast<Transform>()).ToArray();
        if (root.transform.childCount != 9)
            throw new InvalidOperationException($"Expected 9 environment zones, found {root.transform.childCount}.");
        if (instances.Length != ExpectedCount)
            throw new InvalidOperationException($"Expected {ExpectedCount} decorations, found {instances.Length}.");

        foreach (Transform instance in instances)
        {
            if (!byName.TryGetValue(instance.name, out Placement spec))
                throw new InvalidOperationException($"Unexpected environment instance: {instance.name}.");
            Vector3 scale = instance.localScale;
            if (scale.x < 3f || scale.y < 3f || scale.z < 3f)
                throw new InvalidOperationException($"{instance.name} scale is below (3,3,3): {scale}.");
            if (PrefabUtility.GetCorrespondingObjectFromSource(instance.gameObject) == null)
                throw new InvalidOperationException($"{instance.name} lost its FBX prefab link.");
            if (instance.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException($"{instance.name} must remain visual-only but contains a collider.");

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException($"{instance.name} has no renderer.");
            Bounds bounds = CombinedBounds(renderers);
            if (spec.grounded && Mathf.Abs(bounds.min.y - spec.position.y) > 0.025f)
                throw new InvalidOperationException($"{instance.name} is not seated on y={spec.position.y:F2}; bottom={bounds.min.y:F3}.");
        }
        Debug.Log($"Map1 environment validation passed: 9 zones, {ExpectedCount} linked models, all scales >= 3, grounded bottoms aligned, no colliders.");
    }

    public static void CaptureReview()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Capture("Overall", new Vector3(175f, 155f, -85f), new Vector3(54f, 3f, 62f), 42f);
        Capture("Opening", new Vector3(78f, 54f, -63f), new Vector3(22f, 0f, 17f), 43f);
        Capture("Midgame", new Vector3(112f, 70f, 16f), new Vector3(54f, 5f, 70f), 45f);
        Capture("Finale", new Vector3(155f, 65f, 75f), new Vector3(86f, 6f, 120f), 42f);
        Capture("Player_Opening", new Vector3(-7f, 8f, -18f), new Vector3(25f, 0f, 2f), 55f);
        Capture("Player_Clouds", new Vector3(52f, 12f, 23f), new Vector3(52f, 7f, 62f), 55f);
        Capture("Player_Current", new Vector3(52f, 11f, 64f), new Vector3(60f, 8f, 104f), 55f);
        Capture("Player_Finale", new Vector3(88f, 14f, 108f), new Vector3(118f, 7f, 120f), 55f);
        Debug.Log("Map1 environment review renders captured.");
    }

    private static IEnumerable<Placement> CreatePlacements()
    {
        var p = new List<Placement>();
        void G(string zone, string model, string name, float x, float y, float z, float s, float yaw = 0f) => p.Add(new Placement { zone = zone, model = model, name = name, position = new Vector3(x, y, z), scale = Vector3.one * s, yaw = yaw, grounded = true });
        void F(string zone, string model, string name, float x, float y, float z, float s, float yaw = 0f) => p.Add(new Placement { zone = zone, model = model, name = name, position = new Vector3(x, y, z), scale = Vector3.one * s, yaw = yaw, grounded = false });
        const float low = -1.46f;
        const float high = 5.99f;

        // Z00 — soft, readable spawn island; the central play lane stays open.
        G("Z00_Sleepy_Grove", "Deco_DreamTree", "Start_DreamTree_W", -9.7f, low, -6.7f, 5.2f, 20f);
        G("Z00_Sleepy_Grove", "Deco_DreamTree", "Start_DreamTree_E", 4.8f, low, 6.8f, 4.4f, -28f);
        G("Z00_Sleepy_Grove", "Deco_MushroomTree", "Start_Mushroom_NW", -9.8f, low, 6.5f, 3.8f, 12f);
        G("Z00_Sleepy_Grove", "Deco_MushroomTree", "Start_Mushroom_SE", 5.0f, low, -6.9f, 3.4f, -16f);
        G("Z00_Sleepy_Grove", "Deco_CrystalCluster", "Start_Crystal_N", -5.8f, low, 8.4f, 3.2f, 30f);
        G("Z00_Sleepy_Grove", "Deco_CrystalCluster", "Start_Crystal_S", 1.9f, low, -8.3f, 3.0f, -25f);
        G("Z00_Sleepy_Grove", "Deco_LanternPost", "Start_Lantern_N", 5.9f, low, 3.1f, 3.4f, -90f);
        G("Z00_Sleepy_Grove", "Deco_LanternPost", "Start_Lantern_S", 5.9f, low, -3.1f, 3.4f, -90f);
        G("Z00_Sleepy_Grove", "Deco_Signpost", "Start_Signpost", 3.8f, low, 1.8f, 3.2f, 70f);
        G("Z00_Sleepy_Grove", "Deco_RopeFence", "Start_RopeFence_W", -11.3f, low, 0f, 3.2f, 0f);
        F("Z00_Sleepy_Grove", "Deco_Cloud_A", "Start_Cloud_N", -3f, -4.8f, 13f, 6.5f, 15f);
        F("Z00_Sleepy_Grove", "Deco_Cloud_B", "Start_Cloud_S", -1f, -5.8f, -14f, 7.0f, -10f);
        F("Z00_Sleepy_Grove", "Deco_FloatingRock", "Start_Underside_W", -13.8f, -7.5f, 3f, 5.5f, 25f);
        F("Z00_Sleepy_Grove", "Deco_FloatingRock", "Start_Underside_E", 8.5f, -8.5f, -2f, 4.8f, -18f);

        // Z01 — first threshold island, framed but never blocking the central runway.
        G("Z01_First_Flight", "Deco_ArchGate", "FirstFlight_Arch", 21.2f, low, 0f, 4.2f, 90f);
        G("Z01_First_Flight", "Deco_DreamTree", "FirstFlight_Tree_N", 31.8f, low, 5.7f, 4.2f, -20f);
        G("Z01_First_Flight", "Deco_MushroomTree", "FirstFlight_Tree_S", 31.8f, low, -5.7f, 3.7f, 25f);
        G("Z01_First_Flight", "Deco_CrystalCluster", "FirstFlight_Crystal_N", 25.6f, low, 6.1f, 3.0f, 10f);
        G("Z01_First_Flight", "Deco_CrystalCluster", "FirstFlight_Crystal_S", 25.6f, low, -6.1f, 3.0f, -10f);
        G("Z01_First_Flight", "Deco_LanternPost", "FirstFlight_Lantern_N", 22.0f, low, 4.5f, 3.2f, 90f);
        G("Z01_First_Flight", "Deco_LanternPost", "FirstFlight_Lantern_S", 22.0f, low, -4.5f, 3.2f, 90f);
        G("Z01_First_Flight", "Deco_RopeFence", "FirstFlight_Fence_N", 28.2f, low, 6.7f, 3.3f, 90f);
        G("Z01_First_Flight", "Deco_RopeFence", "FirstFlight_Fence_S", 28.2f, low, -6.7f, 3.3f, 90f);
        F("Z01_First_Flight", "Deco_Cloud_C", "FirstGap_Cloud_N", 14f, -4.2f, 7.5f, 6.0f, 20f);
        F("Z01_First_Flight", "Deco_Cloud_D", "FirstGap_Cloud_S", 14f, -5.0f, -8.0f, 5.5f, -20f);
        F("Z01_First_Flight", "Deco_FloatingRock", "FirstFlight_Underside", 36.5f, -7f, 5f, 5.0f, 10f);

        // Z02 — P4 cooperation room. Decorations hug the side walls and clarify entry/exit.
        G("Z02_Coop_Court", "Deco_LanternPost", "Coop_Entry_L", 46.0f, high, 7.3f, 3.2f, 0f);
        G("Z02_Coop_Court", "Deco_LanternPost", "Coop_Entry_R", 58.1f, high, 7.3f, 3.2f, 0f);
        G("Z02_Coop_Court", "Deco_BannerPost_Moon", "Coop_Banner_Moon", 45.8f, high, 17.8f, 3.1f, 0f);
        G("Z02_Coop_Court", "Deco_BannerPost_Star", "Coop_Banner_Star", 58.3f, high, 17.8f, 3.1f, 0f);
        G("Z02_Coop_Court", "Deco_StoneWall_Window", "Coop_Wall_W0", 45.3f, high, 10.5f, 3.0f, 0f);
        G("Z02_Coop_Court", "Deco_StoneWall_Gem", "Coop_Wall_W1", 45.3f, high, 14.2f, 3.0f, 0f);
        G("Z02_Coop_Court", "Deco_StoneWall_Window", "Coop_Wall_E0", 58.8f, high, 10.5f, 3.0f, 180f);
        G("Z02_Coop_Court", "Deco_StoneWall_Gem", "Coop_Wall_E1", 58.8f, high, 14.2f, 3.0f, 180f);
        G("Z02_Coop_Court", "Deco_CrystalCluster", "Coop_Crystal_W", 46.3f, high, 19.1f, 3.0f, 15f);
        G("Z02_Coop_Court", "Deco_CrystalCluster", "Coop_Crystal_E", 57.8f, high, 19.1f, 3.0f, -15f);

        // Z03 — calmer lever garden before the moving-cloud sequence.
        G("Z03_Lever_Garden", "Deco_DreamTree", "Lever_Tree_NW", 46.0f, high, 39.7f, 4.2f, 18f);
        G("Z03_Lever_Garden", "Deco_MushroomTree", "Lever_Mushroom_NE", 57.9f, high, 39.5f, 3.5f, -25f);
        G("Z03_Lever_Garden", "Deco_CrystalCluster", "Lever_Crystal_W", 45.8f, high, 31.5f, 3.0f, 15f);
        G("Z03_Lever_Garden", "Deco_CrystalCluster", "Lever_Crystal_E", 58.2f, high, 30.5f, 3.0f, -15f);
        G("Z03_Lever_Garden", "Deco_MoonPedestal", "Lever_Moon_W", 46.0f, high, 27.7f, 3.2f, 0f);
        G("Z03_Lever_Garden", "Deco_MoonPedestal", "Lever_Moon_E", 58.0f, high, 27.7f, 3.2f, 180f);
        G("Z03_Lever_Garden", "Deco_LanternPost", "Lever_Lantern_W", 46.2f, high, 35.3f, 3.1f, 90f);
        G("Z03_Lever_Garden", "Deco_LanternPost", "Lever_Lantern_E", 58.0f, high, 35.3f, 3.1f, -90f);
        G("Z03_Lever_Garden", "Deco_Fountain", "Lever_Fountain", 47.3f, high, 27.8f, 3.0f, 0f);
        G("Z03_Lever_Garden", "Deco_RopeFence", "Lever_Fence_N", 52.0f, high, 40.6f, 3.4f, 90f);

        // Z04 — layered cloud banks make the narrow moving-cloud path feel suspended.
        string[] cloudModels = { "Deco_Cloud_A", "Deco_Cloud_B", "Deco_Cloud_C", "Deco_Cloud_D" };
        for (int i = 0; i < 6; i++)
        {
            float z = 42f + i * 3.7f;
            float side = i % 2 == 0 ? -1f : 1f;
            F("Z04_Cloud_Runway", cloudModels[i % 4], $"Runway_Cloud_{i:00}", 52.6f + side * (6.3f + (i % 3)), 2.0f + (i % 2) * 1.2f, z, 5.0f + (i % 3) * 0.5f, i * 23f);
        }
        for (int i = 0; i < 4; i++)
            F("Z04_Cloud_Runway", "Deco_FloatingRock", $"Runway_Rock_{i:00}", 44f + i * 5.8f, -2.5f - i, 44f + i * 5.2f, 4.2f + i * 0.3f, i * 31f);
        for (int i = 0; i < 4; i++)
            F("Z04_Cloud_Runway", "Deco_SparkleStar", $"Runway_Star_{i:00}", 47f + i * 3.8f, 13f + (i % 2) * 3f, 45f + i * 5.2f, 3.2f + (i % 2) * 0.4f, i * 35f);

        // Z05 — ceremonial threshold into the zero-gravity dream current.
        G("Z05_Bubble_Threshold", "Deco_RainbowArch", "Bubble_RainbowArch", 52.1f, high, 76.2f, 4.0f, 0f);
        G("Z05_Bubble_Threshold", "Deco_LanternPost", "Bubble_Lantern_SW", 46.0f, high, 68.5f, 3.2f, 0f);
        G("Z05_Bubble_Threshold", "Deco_LanternPost", "Bubble_Lantern_SE", 58.1f, high, 68.5f, 3.2f, 0f);
        G("Z05_Bubble_Threshold", "Deco_LanternPost", "Bubble_Lantern_NW", 46.0f, high, 75.6f, 3.2f, 180f);
        G("Z05_Bubble_Threshold", "Deco_LanternPost", "Bubble_Lantern_NE", 58.1f, high, 75.6f, 3.2f, 180f);
        G("Z05_Bubble_Threshold", "Deco_CrystalCluster", "Bubble_Crystal_W", 46.2f, high, 72.0f, 3.2f, 20f);
        G("Z05_Bubble_Threshold", "Deco_CrystalCluster", "Bubble_Crystal_E", 57.9f, high, 72.0f, 3.2f, -20f);
        G("Z05_Bubble_Threshold", "Deco_BannerPost_Moon", "Bubble_MoonBanner", 48.0f, high, 76.3f, 3.0f, 0f);
        G("Z05_Bubble_Threshold", "Deco_BannerPost_Star", "Bubble_StarBanner", 56.1f, high, 76.3f, 3.0f, 0f);

        // Z06 — depth layers around, above and below the playable bubble corridor.
        for (int i = 0; i < 8; i++)
        {
            float z = 79f + i * 3.4f;
            bool left = i % 2 == 0;
            F("Z06_Dream_Current", cloudModels[(i + 1) % 4], $"Current_Cloud_{i:00}", left ? 48f - (i % 3) : 68f + (i % 3), 4f + (i % 3) * 2.2f, z, 5.2f + (i % 2) * 0.7f, i * 27f);
        }
        for (int i = 0; i < 4; i++)
            F("Z06_Dream_Current", "Deco_FloatingRock", $"Current_Rock_{i:00}", i % 2 == 0 ? 47f : 69f, -1f - i * 1.4f, 81f + i * 7f, 4.5f + i * 0.35f, i * 40f);
        for (int i = 0; i < 4; i++)
            F("Z06_Dream_Current", "Deco_SparkleStar", $"Current_Star_{i:00}", 51f + i * 4.5f, 14f + (i % 2) * 4f, 82f + i * 6.2f, 3.3f + (i % 2) * 0.5f, i * 35f);

        // Z07 — perimeter dressing preserves the catapult's launch footprint.
        G("Z07_Catapult_Isle", "Deco_DreamTree", "Catapult_Tree_SW", 52.5f, high, 106.0f, 5.0f, 20f);
        G("Z07_Catapult_Isle", "Deco_DreamTree", "Catapult_Tree_NW", 52.5f, high, 129.4f, 4.6f, -15f);
        G("Z07_Catapult_Isle", "Deco_MushroomTree", "Catapult_Mushroom_W", 51.8f, high, 117.0f, 3.8f, 20f);
        G("Z07_Catapult_Isle", "Deco_MushroomTree", "Catapult_Mushroom_NE", 78.7f, high, 105.5f, 3.6f, -20f);
        G("Z07_Catapult_Isle", "Deco_CrystalCluster", "Catapult_Crystal_SW", 57.0f, high, 105.2f, 3.2f, 20f);
        G("Z07_Catapult_Isle", "Deco_CrystalCluster", "Catapult_Crystal_NW", 57.0f, high, 130.5f, 3.2f, -20f);
        G("Z07_Catapult_Isle", "Deco_CrystalCluster", "Catapult_Crystal_SE", 77.4f, high, 106.0f, 3.2f, -20f);
        G("Z07_Catapult_Isle", "Deco_LanternPost", "Catapult_Lantern_SW", 55.0f, high, 109.0f, 3.2f, 0f);
        G("Z07_Catapult_Isle", "Deco_LanternPost", "Catapult_Lantern_NW", 55.0f, high, 127.5f, 3.2f, 180f);
        G("Z07_Catapult_Isle", "Deco_LanternPost", "Catapult_Lantern_SE", 77.5f, high, 109.0f, 3.2f, 0f);
        G("Z07_Catapult_Isle", "Deco_StoneWall_Window", "Catapult_Wall_W0", 50.7f, high, 112.0f, 3.1f, 0f);
        G("Z07_Catapult_Isle", "Deco_StoneWall_Gem", "Catapult_Wall_W1", 50.7f, high, 124.0f, 3.1f, 0f);
        G("Z07_Catapult_Isle", "Deco_BannerPost_Moon", "Catapult_Banner_Moon", 61.0f, high, 105.0f, 3.0f, 0f);
        G("Z07_Catapult_Isle", "Deco_BannerPost_Star", "Catapult_Banner_Star", 73.5f, high, 105.0f, 3.0f, 0f);
        G("Z07_Catapult_Isle", "Deco_Signpost", "Catapult_Signpost", 61.5f, high, 109.0f, 3.0f, 25f);
        G("Z07_Catapult_Isle", "Deco_Waterfall", "Catapult_Waterfall_W", 50.3f, high, 120.0f, 4.0f, 90f);
        F("Z07_Catapult_Isle", "Deco_Cloud_A", "Catapult_Cloud_N", 66f, 1.0f, 137f, 7.0f, 15f);
        F("Z07_Catapult_Isle", "Deco_Cloud_C", "Catapult_Cloud_S", 67f, 0.0f, 98f, 6.5f, -20f);

        // Z08 — strongest landmark rhythm and a clear central arrival aisle.
        G("Z08_Starlit_Finale", "Deco_ArchGate", "Finale_EntryArch", 99.2f, high, 119.9f, 4.5f, 90f);
        G("Z08_Starlit_Finale", "Deco_LanternPost", "Finale_Lantern_SW", 101.0f, high, 113.0f, 3.3f, 90f);
        G("Z08_Starlit_Finale", "Deco_LanternPost", "Finale_Lantern_NW", 101.0f, high, 127.0f, 3.3f, 90f);
        G("Z08_Starlit_Finale", "Deco_LanternPost", "Finale_Lantern_SE", 121.5f, high, 113.0f, 3.3f, -90f);
        G("Z08_Starlit_Finale", "Deco_LanternPost", "Finale_Lantern_NE", 121.5f, high, 127.0f, 3.3f, -90f);
        G("Z08_Starlit_Finale", "Deco_MoonStarPillars", "Finale_MoonStarPillars", 118.0f, high, 120.0f, 4.2f, 90f);
        G("Z08_Starlit_Finale", "Deco_MoonPedestal", "Finale_MoonPedestal_S", 111.0f, high, 110.0f, 3.4f, 0f);
        G("Z08_Starlit_Finale", "Deco_MoonPedestal", "Finale_MoonPedestal_N", 111.0f, high, 129.0f, 3.4f, 180f);
        G("Z08_Starlit_Finale", "Deco_DreamTree", "Finale_Tree_SE", 122.5f, high, 110.5f, 4.8f, -20f);
        G("Z08_Starlit_Finale", "Deco_DreamTree", "Finale_Tree_NE", 122.5f, high, 129.0f, 4.8f, 20f);
        G("Z08_Starlit_Finale", "Deco_CrystalCluster", "Finale_Crystal_SW", 105.0f, high, 110.0f, 3.2f, 20f);
        G("Z08_Starlit_Finale", "Deco_CrystalCluster", "Finale_Crystal_NW", 105.0f, high, 129.0f, 3.2f, -20f);
        G("Z08_Starlit_Finale", "Deco_CrystalCluster", "Finale_Crystal_SE", 119.0f, high, 110.0f, 3.2f, -20f);
        G("Z08_Starlit_Finale", "Deco_CrystalCluster", "Finale_Crystal_NE", 119.0f, high, 129.0f, 3.2f, 20f);
        G("Z08_Starlit_Finale", "Deco_BannerPost_Moon", "Finale_Banner_Moon", 107.0f, high, 113.0f, 3.1f, 90f);
        G("Z08_Starlit_Finale", "Deco_BannerPost_Star", "Finale_Banner_Star", 107.0f, high, 127.0f, 3.1f, 90f);
        F("Z08_Starlit_Finale", "Deco_SparkleStar", "Finale_CrownStar", 118.0f, 18.0f, 120.0f, 5.0f, 0f);

        if (p.Count != ExpectedCount)
            throw new InvalidOperationException($"Placement table contains {p.Count}, expected {ExpectedCount}.");
        return p;
    }

    private static void Place(Transform parent, Placement spec)
    {
        string path = $"Assets/Models/{spec.model}.fbx";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
            throw new InvalidOperationException($"Missing environment model: {path}");
        GameObject instance = PrefabUtility.InstantiatePrefab(asset) as GameObject;
        if (instance == null)
            throw new InvalidOperationException($"Could not instantiate environment model: {path}");
        instance.name = spec.name;
        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = spec.position;
        instance.transform.localRotation = Quaternion.Euler(0f, spec.yaw, 0f);
        instance.transform.localScale = spec.scale;
        if (spec.grounded)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException($"{spec.name} has no renderer for ground alignment.");
            Bounds bounds = CombinedBounds(renderers);
            instance.transform.position += Vector3.up * (spec.position.y - bounds.min.y);
        }
    }

    private static Bounds CombinedBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void Capture(string label, Vector3 position, Vector3 target, float fieldOfView)
    {
        var cameraObject = new GameObject("Map1_Review_Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = position;
        camera.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);
        camera.fieldOfView = fieldOfView;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 500f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.allowHDR = false;
        var texture = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        camera.targetTexture = texture;
        camera.Render();
        RenderTexture.active = texture;
        image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        image.Apply();
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputDirectory = Path.Combine(projectRoot, "Temp", "Map1Review");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllBytes(Path.Combine(outputDirectory, $"Map1_{label}.png"), image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(cameraObject);
    }
}
