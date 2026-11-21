using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ShapeKind = PlayerShapeStats.ShapeKind;

internal static partial class Map1SectorUpgrade
{
    private const string ScenePath = "Assets/Scenes/Map1_DayDream/Map1_DayDream.unity";
    private const string MaterialFolder = "Assets/LevelDesignSystem/Materials/Map1Coop";

    [MenuItem("Tools/Map1 Codex/Apply Sector Co-op Upgrade")]
    public static void Apply()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform p05 = Find(scene, "P05_Thread_Swing_Canyon");
        Transform p06 = Find(scene, "P06_Sleepwalker_Wall");
        Transform p07 = Find(scene, "P07_Wake_The_Giant");
        Transform p09 = Find(scene, "P09_Thread_Tug_Bridge");
        if (p05.Find("P05_Cargo_Shuttle") != null)
        {
            bool changed = RefineP07(p07);
            changed |= UpgradeP09(p09);
            if (changed)
                SaveUpgrade(scene);
            else
                Debug.Log("Map 1 sector co-op upgrade already applied.");
            return;
        }
        UpgradeP05(p05);
        UpgradeP06(p06);
        UpgradeP07(p07);
        UpgradeP09(p09);
        foreach (Transform sector in new[] { p05, p06, p07, p09 })
            ConfigureCheckpoint(sector);
        RespawnZone previousTeamCheckpoint = null;
        for (int i = 5; i <= 12; i++)
        {
            RespawnZone checkpoint = Find(scene, $"Checkpoint_P{i:00}").GetComponent<RespawnZone>();
            checkpoint.requiredCheckpoint = previousTeamCheckpoint;
            if (checkpoint.TryGetComponent<Map1TeamCheckpoint>(out _)) previousTeamCheckpoint = checkpoint;
        }
        SaveUpgrade(scene);
    }

    private static void SaveUpgrade(Scene scene)
    {
        EditorSceneManager.MarkSceneDirty(scene);
        const string saveOverride = "EFK_ALLOW_TEAM_SAVE";
        string prior = Environment.GetEnvironmentVariable(saveOverride);
        try
        {
            Environment.SetEnvironmentVariable(saveOverride, "1");
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Map 1 sector scene save failed.");
        }
        finally { Environment.SetEnvironmentVariable(saveOverride, prior); }
        Debug.Log("Map 1 sector co-op upgrade saved.");
    }

    private static Transform Find(Scene scene, string name) => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
        .Single(t => t.name == name);

    private static void ConfigureCheckpoint(Transform sector)
    {
        string number = sector.name.Substring(0, 3);
        Transform marker = sector.Find("Checkpoint_" + number);
        if (marker == null) throw new InvalidOperationException("Checkpoint missing in " + sector.name);
        var team = marker.gameObject.AddComponent<Map1TeamCheckpoint>();
        team.checkpoint = marker.GetComponent<RespawnZone>();
        team.checkpointTrigger = marker.GetComponent<BoxCollider>();
        team.checkpointTrigger.enabled = false;
        team.arrivalSize = number == "P09" ? P09ArrivalSize : new Vector3(13f, 4f, 6f);
        team.requires = sector.GetComponentsInChildren<Map1CoopSwitch>(true);
    }

    private static GameObject Box(Transform parent, string name, Vector3 worldCenter, Vector3 size, Color color)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = worldCenter;
        root.AddComponent<BoxCollider>().size = size;
        var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mesh.name = "Visual";
        UnityEngine.Object.DestroyImmediate(mesh.GetComponent<Collider>());
        mesh.transform.SetParent(root.transform, false);
        mesh.transform.localScale = size;
        mesh.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
        return root;
    }

    private static Map1CoopSwitch Switch(Transform parent, string name, Vector3 worldCenter,
        Vector3 size, ShapeKind kind)
    {
        Color color = kind == ShapeKind.Sphere ? new Color(.27f, .63f, .94f) :
            kind == ShapeKind.Cube ? new Color(.88f, .55f, .24f) : new Color(.64f, .42f, .88f);
        var mark = new GameObject(name);
        mark.transform.SetParent(parent, false);
        mark.transform.position = worldCenter;
        var trigger = mark.AddComponent<BoxCollider>();
        trigger.size = size;
        trigger.isTrigger = true;
        var visual = Box(mark.transform, "Plate", worldCenter - Vector3.up * (size.y * .5f - .015f),
            new Vector3(size.x, .06f, size.z), color);
        UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
        var control = mark.AddComponent<Map1CoopSwitch>();
        control.kind = kind;
        control.indicator = visual.GetComponentInChildren<Renderer>();
        control.readyMaterial = MaterialFor(new Color(.35f, .87f, .58f));
        return control;
    }

    private static Material MaterialFor(Color color)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/LevelDesignSystem/Materials", "Map1Coop");
        string path = MaterialFolder + "/" + ColorUtility.ToHtmlStringRGB(color) + ".mat";
        Material asset = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (asset != null) return asset;
        asset = new Material(Shader.Find("Standard")) { color = color };
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }
}
