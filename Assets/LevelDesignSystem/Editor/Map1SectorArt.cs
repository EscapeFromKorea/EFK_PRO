using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Shape = PlayerShapeStats.ShapeKind;
using Object = UnityEngine.Object;

internal static partial class Map1SectorUpgrade
{
    private const string ArtRoot = "Assets/Scenes/Map1_DayDream/DayDreamCoopArt";

    [MenuItem("Tools/Map1 Codex/Apply Sector Art")]
    public static void ApplySectorArt()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        RemoveWorldText(scene);
        var shapes = Enum.GetValues(typeof(Shape)).Cast<Shape>().ToDictionary(
            kind => kind, kind => Find(scene, "Player_" + kind).Find("Player_Mesh/Player_MeshVisual")
                .GetComponent<MeshFilter>().sharedMesh);
        DressSectors(shapes, Find(scene, "P05_Thread_Swing_Canyon"), Find(scene, "P06_Sleepwalker_Wall"),
            Find(scene, "P07_Wake_The_Giant"), Find(scene, "P09_Thread_Tug_Bridge"));
        SaveUpgrade(scene);
    }

    private static void RemoveWorldText(Scene scene)
    {
        var texts = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TextMesh>(true)).ToArray();
        var labels = texts.Select(text => text.gameObject).ToHashSet();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var control in root.GetComponentsInChildren<Map1CoopSwitch>(true))
                control.reveal = control.reveal.Where(target => target != null && !labels.Contains(target)).ToArray();
        foreach (GameObject label in labels)
            Object.DestroyImmediate(label);
    }

    private static void DressSectors(System.Collections.Generic.IReadOnlyDictionary<Shape, Mesh> shapes,
        Transform p05, Transform p06, Transform p07, Transform p09)
    {
        if (!AssetDatabase.IsValidFolder(ArtRoot))
            AssetDatabase.CreateFolder("Assets/Scenes/Map1_DayDream", "DayDreamCoopArt");

        Dress(p05, "Meadow", "P05_Pickup_Pier", "P05_Destination_Pier");
        Dress(p05, "StonePerch", "P05_Anchor_Approach", "P05_Transfer_Shelf", "P05_Swing_Rest",
            "P05_Sphere_Exit_Shelf", "P05_Tetra_High_Shelf");
        Dress(p05, "WoodDeck", "P05_Reunion_Ramp");
        Dress(p05, "CloudBarge", "P05_Cargo_Shuttle");

        Dress(p06, "Meadow", "P06_PowerIsland");
        Dress(p06, "TimberRail", "P06_PowerIsland_Backstop", "P06_PowerIsland_NorthRail",
            "P06_Catwalk_Edge", "P06_Catwalk_End");
        Dress(p06, "Speed", "P06_Sphere_RunUp");
        Dress(p06, "WoodDeck", "P06_Power_ReturnBridge", "P06_UpperCatwalk");
        Dress(p06, "MoonLift", "P06_TeamLift");

        Dress(p07, "StonePerch", "P07_AnchorApproach", "P07_Sphere_SwingShelf", "P07_Sphere_Runup",
            "P07_Tetra_Approach", "P07_Tetra_HighLedge", "P07_Tetra_LowLedge");
        Dress(p07, "PinStone", "P07_Tetra_PinWall");

        Dress(p09, "WoodDeck", "P09_TunnelDeck", "P09_FanReturnWalk/ToExitCorner", "P09_FanReturnWalk/ToReunion");
        // Trim the wooden faces where the two return pieces meet and where the path touches the grass.
        FitBoard(p09.Find("P09_TunnelDeck"), Vector3.zero, new Vector3(4.48f, .54f, 6.94f));
        FitBoard(p09.Find("P09_FanReturnWalk/ToExitCorner"), new Vector3(0, 0, -.71f), new Vector3(3.03f, .46f, 2.86f));
        FitBoard(p09.Find("P09_FanReturnWalk/ToReunion"), new Vector3(1.34f, 0, 0), new Vector3(4.96f, .46f, 2.15f));
        Dress(p09, "TimberRail", "P09_TunnelEastRail", "P09_TunnelWestRail", "P09_IslandBackstop",
            "P09_HighVentBackstop", "P09_HighVentOuterRail");
        Dress(p09, "Meadow", "P09_FanIsland", "P09_HighVentLanding");
        Dress(p09, "Speed", "P09_Sphere_Launch");
        Dress(p09, "StoneGate", "P09_SideShutter", "P09_ExitGate", "P09_IslandReturnGate");
        Dress(p09, "PinStone", "P09_IslandWestWall");

        Transform farRail = p09.Find("P09_HighVentBackstop");
        RemoveChild(farRail, "VentFan");
        Attach(farRail, "WindCrest", "WindShrine", new Vector3(0, .61f, .25f), new Vector3(1.55f, 1.4f, 1.08f));
        Transform fanControl = p09.Find("P09_Sphere_FanSwitch");
        RemoveChild(fanControl, "FanStation");
        RemoveChild(fanControl, "FanMount");
        Attach(fanControl, "WindCrest", "WindShrine", new Vector3(0, .02f, 1.12f), new Vector3(1.65f, 1.42f, .75f));

        Physics.SyncTransforms();
        foreach (Transform sector in new[] { p05, p06, p07, p09 })
            foreach (var control in sector.GetComponentsInChildren<Map1CoopSwitch>(true))
                DressControl(control, shapes[Shape.Cube], shapes[control.kind]);
    }

    private static void FitBoard(Transform host, Vector3 center, Vector3 size) =>
        Attach(host, "DayDreamVisual", "WoodDeck", center - Vector3.up * .024f, size);

    private static void Dress(Transform sector, string style, params string[] paths)
    {
        foreach (string path in paths)
        {
            Transform host = sector.Find(path);
            var collider = host != null ? host.GetComponent<BoxCollider>() : null;
            if (collider == null) throw new InvalidOperationException($"Missing P05–P09 art target: {path}");
            RemoveChild(host, "Visual");
            Vector3 size = collider.size;
            Vector3 center = collider.center;
            if (style == "StonePerch" || style == "Meadow" || style == "WoodDeck")
            {
                size.x -= Mathf.Min(.14f, size.x * .08f);
                size.z -= Mathf.Min(.14f, size.z * .08f);
                center.y -= .024f;
            }
            bool alongZ = style == "TimberRail" && size.z > size.x;
            Attach(host, "DayDreamVisual", style, center,
                alongZ ? new Vector3(size.z, size.y, size.x) : size, alongZ ? 90f : 0f);
        }
    }

    private static void DressControl(Map1CoopSwitch control, Mesh cube, Mesh mark)
    {
        Transform target = control.indicator.transform;
        Transform plate = target.parent;
        Vector3 size = control.GetComponent<BoxCollider>().size;
        RemoveChild(control.transform, "ControlHousing");
        RemoveChild(control.transform, "ShapeGlyph");
        RemoveChild(control.transform, "DayDreamSwitchBase");
        Transform tile = control.transform.Find("SwitchTile");
        if (tile == null)
        {
            tile = new GameObject("SwitchTile", typeof(MeshFilter), typeof(MeshRenderer)).transform;
            tile.SetParent(control.transform, false);
        }
        tile.GetComponent<MeshFilter>().sharedMesh = cube;
        tile.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(new Color(.22f, .22f, .20f));
        Vector3 area = control.GetComponent<BoxCollider>().center;
        Vector3 origin = control.transform.TransformPoint(area) + Vector3.up * .22f;
        Vector3 floor = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1.8f, ~0, QueryTriggerInteraction.Ignore)
            ? control.transform.InverseTransformPoint(hit.point) : plate.localPosition;
        float width = Mathf.Min(size.x, size.z) * .76f;
        tile.localScale = new Vector3(width, .075f, width);
        tile.localPosition = new Vector3(area.x, floor.y + .062f, area.z);

        // Use the player model's existing mesh as a flat inlay, separated from the tile's top.
        target.GetComponent<MeshFilter>().sharedMesh = mark;
        target.localScale = new Vector3(width * .55f, .027f, width * .55f);
        target.localPosition = tile.localPosition - plate.localPosition + Vector3.up * .069f;
    }

    private static void RemoveChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) Object.DestroyImmediate(child.gameObject);
    }

    private static void Attach(Transform parent, string name, string style, Vector3 center, Vector3 size, float yaw = 0f)
    {
        Transform current = parent.Find(name);
        GameObject prefab = ArtPrefab(style);
        GameObject art = current != null ? current.gameObject : null;
        if (art == null || PrefabUtility.GetCorrespondingObjectFromSource(art) != prefab)
        {
            RemoveChild(parent, name);
            art = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        }
        art.name = name;
        art.transform.localPosition = center;
        art.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        art.transform.localScale = size;
    }

    private static GameObject ArtPrefab(string style)
    {
        string path = $"{ArtRoot}/{style}.prefab";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset != null) return asset;
        var root = new GameObject(style);
        try
        {
            switch (style)
            {
                case "Meadow": Part("Platform_GrassBlock", Vector3.zero, Vector3.one); break;
                case "StonePerch": Part("Platform_SteppingStone", Vector3.zero, Vector3.one); break;
                case "WoodDeck": Part("Bridge_Wood", Vector3.zero, Vector3.one); break;
                case "TimberRail": Part("Deco_WoodFence", Vector3.zero, Vector3.one); break;
                case "Speed": Part("Gimmick_SpeedPad", Vector3.zero, Vector3.one); break;
                case "CloudBarge":
                    Part("Gimmick_MovingPlatform", Vector3.zero, Vector3.one);
                    Part("Deco_Cloud_A", new Vector3(0, -.7f, -.21f), new Vector3(.78f, .7f, .54f));
                    Part("Deco_Cloud_C", new Vector3(.14f, -.71f, .27f), new Vector3(.62f, .61f, .44f));
                    break;
                case "MoonLift":
                    Part("Gimmick_MovingPlatform", Vector3.zero, Vector3.one);
                    Part("Deco_MoonPedestal", new Vector3(0, -.81f, 0), new Vector3(.69f, .93f, .68f));
                    Part("Deco_Cloud_D", new Vector3(0, -.95f, .04f), new Vector3(.96f, .77f, .82f));
                    break;
                case "PinStone": Part("Wall_LevelBoundary", Vector3.zero, Vector3.one); break;
                case "StoneGate":
                    foreach (float x in new[] { -.333f, 0f, .333f })
                        Part("Wall_LevelBoundary", new Vector3(x, 0f, 0f), new Vector3(.327f, 1f, .86f));
                    Part("Deco_CrystalCluster", new Vector3(0f, .16f, -.45f), new Vector3(.14f, .32f, .3f));
                    break;
                case "WindShrine":
                    Part("Deco_MoonPedestal", new Vector3(0, -.3f, 0), new Vector3(.7f, .39f, .74f));
                    Part("Deco_Cloud_B", new Vector3(-.14f, -.04f, .03f), new Vector3(.73f, .45f, .86f));
                    Part("Deco_CrystalCluster", new Vector3(.07f, .11f, -.08f), new Vector3(.52f, .73f, .76f));
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown DayDream art style");
            }
            const string saveOverride = "EFK_ALLOW_TEAM_SAVE";
            string prior = Environment.GetEnvironmentVariable(saveOverride);
            try
            {
                Environment.SetEnvironmentVariable(saveOverride, "1");
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                if (prefab == null) throw new InvalidOperationException($"Could not save {path}");
                return prefab;
            }
            finally { Environment.SetEnvironmentVariable(saveOverride, prior); }
        }
        finally { Object.DestroyImmediate(root); }

        void Part(string model, Vector3 center, Vector3 size)
        {
            string sourcePath = $"Assets/Models/{model}.fbx";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new InvalidOperationException($"Missing {sourcePath}");
            var part = Object.Instantiate(source, root.transform);
            part.name = model;
            part.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            part.transform.localScale = Vector3.one;
            foreach (var collider in part.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
            foreach (var visual in part.GetComponentsInChildren<MeshRenderer>(true))
                if (visual.name.StartsWith("UCX_", StringComparison.Ordinal) ||
                    (style == "WoodDeck" && !visual.name.StartsWith("Bridge_Wood_Log_", StringComparison.Ordinal)))
                    visual.enabled = false;

            Bounds bounds = LocalModelBounds(part.transform);
            var scale = new Vector3(size.x / Mathf.Max(bounds.size.x, .001f),
                size.y / Mathf.Max(bounds.size.y, .001f), size.z / Mathf.Max(bounds.size.z, .001f));
            part.transform.localScale = scale;
            part.transform.localPosition = center - Vector3.Scale(bounds.center, scale);
        }
    }

    private static Bounds LocalModelBounds(Transform model)
    {
        Bounds result = default;
        bool first = true;
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!renderer.enabled) continue;
            Bounds box = renderer.localBounds;
            Matrix4x4 toModel = model.worldToLocalMatrix * renderer.localToWorldMatrix;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        var corner = box.center + Vector3.Scale(box.extents, new Vector3(x, y, z));
                        Vector3 point = toModel.MultiplyPoint3x4(corner);
                        if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                        else result.Encapsulate(point);
                    }
        }
        if (first) throw new InvalidOperationException($"{model.name} has no mesh renderer");
        return result;
    }

}
