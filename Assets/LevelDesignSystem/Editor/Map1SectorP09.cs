using UnityEditor;
using UnityEngine;
using Shape = PlayerShapeStats.ShapeKind;

internal static partial class Map1SectorUpgrade
{
    private static readonly Vector3 P09ArrivalSize = new Vector3(13f, 1.6f, 6f);

    private static bool UpgradeP09(Transform sector)
    {
        if (sector.Find("P09_Headwind") != null) return false;

        // Remove the previous prototype's props and switches from a scene already upgraded once.
        foreach (string name in new[]
        {
            "P09_WeightedThreadBridge", "P09_WeightedThreadBridge_A", "P09_WeightedThreadBridge_B",
            "P09_MidIsland", "P09_IslandBackstop", "P09_Sphere_Runup", "P09_Sphere_UncoverAnchor",
            "P09_PinWall_Entry", "P09_PinWall_Island", "P09_IslandAnchor", "P09_ExitAnchor",
            "P09_PinSpan_1", "P09_PinSpan_2", "P09_Cube_Brace", "P09_Brace_1", "P09_Brace_2",
            "SPHERE / OPEN THE FAR ANCHOR", "TETRA / E: PIN", "CUBE / BRACE BOTH SPANS"
        })
        {
            for (int i = sector.childCount - 1; i >= 0; i--)
            {
                Transform child = sector.GetChild(i);
                if (child.name == name) Object.DestroyImmediate(child.gameObject);
            }
        }

        var stone = new Color(.32f, .49f, .65f);
        var amber = new Color(.88f, .55f, .24f);
        var blue = new Color(.27f, .63f, .94f);
        var violet = new Color(.64f, .42f, .88f);

        // The heavy cube can walk against the headwind to the first control.
        Box(sector, "P09_TunnelDeck", new Vector3(0f, 5.53f, 284.5f), new Vector3(4.6f, .54f, 7.2f), stone);
        Box(sector, "P09_TunnelEastRail", new Vector3(2.24f, 6.12f, 284.5f), new Vector3(.22f, .64f, 7.2f), stone);
        Box(sector, "P09_TunnelWestRail", new Vector3(-2.24f, 6.12f, 282.45f), new Vector3(.22f, .64f, 3.1f), stone);
        GameObject headwind = P09Wind(sector, "P09_Headwind", new Vector3(0f, 7.55f, 284.45f),
            new Vector3(4.8f, 3.5f, 7.65f), Vector3.back, 16f, 1.15f, new Color(1f, .81f, .45f, .75f));

        var weight = Switch(sector, "P09_Cube_UnlockShutter", new Vector3(0f, 6.19f, 287.15f),
            new Vector3(2.7f, .8f, 1.24f), Shape.Cube);

        // The island is a control station. Its high walls stop the direct hop to the exit.
        Box(sector, "P09_FanIsland", new Vector3(11.35f, 5.52f, 284.65f), new Vector3(4.6f, .56f, 4.2f), stone);
        Box(sector, "P09_IslandBackstop", new Vector3(13.56f, 6.48f, 284.65f), new Vector3(.22f, 1.35f, 4.2f), stone);
        Box(sector, "P09_IslandWestWall", new Vector3(9.1f, 7.24f, 285.83f), new Vector3(.26f, 2.88f, 1.84f), stone);
        GameObject islandGate = Box(sector, "P09_IslandReturnGate", new Vector3(11.34f, 7.26f, 286.66f),
            new Vector3(4.68f, 2.93f, .3f), violet);
        GameObject shutter = Box(sector, "P09_SideShutter", new Vector3(6.55f, 7.14f, 279.78f),
            new Vector3(3.1f, 2.68f, .3f), amber);
        shutter.transform.rotation = Quaternion.LookRotation(new Vector3(.66f, 0f, .75f));

        var runup = Box(sector, "P09_Sphere_Launch", new Vector3(6.58f, 5.93f, 279.96f),
            new Vector3(1.55f, .32f, 1.28f), blue);
        runup.transform.rotation = shutter.transform.rotation;
        runup.GetComponent<BoxCollider>().isTrigger = true;
        var boost = runup.AddComponent<AccelPad>();
        boost.boostSpeed = 10f;
        boost.holdDuration = .54f;
        boost.decelDuration = .25f;
        runup.SetActive(false);
        weight.conceal = new[] { shutter };
        weight.reveal = new[] { runup };
        var fan = Switch(sector, "P09_Sphere_FanSwitch", new Vector3(11.46f, 6.19f, 285.37f),
            new Vector3(1.95f, .8f, 1.45f), Shape.Sphere);
        fan.requires = new[] { weight };

        // A separate airflow runs diagonally up to the far vent. The vent is above a normal jump.
        Vector3 updraftDirection = new Vector3(-.58f, .60f, .55f);
        GameObject updraft = P09Wind(sector, "P09_VentUpdraft", new Vector3(-2.84f, 7.64f, 286.22f),
            new Vector3(3.25f, 3.15f, 6.2f), updraftDirection, 4.15f, 1.18f, new Color(.70f, .88f, 1f, .9f));
        updraft.SetActive(false);
        fan.conceal = new[] { headwind };
        fan.reveal = new[] { updraft };

        Box(sector, "P09_HighVentLanding", new Vector3(-5.1f, 8.31f, 289.44f), new Vector3(4.7f, .58f, 3.8f), violet);
        Box(sector, "P09_HighVentBackstop", new Vector3(-5.1f, 9.26f, 291.34f), new Vector3(4.7f, 1.31f, .2f), violet);
        Box(sector, "P09_HighVentOuterRail", new Vector3(-7.45f, 9.16f, 289.44f), new Vector3(.2f, 1.11f, 3.8f), violet);
        var vent = Switch(sector, "P09_Tetra_OpenExit", new Vector3(-5.08f, 8.99f, 290.03f),
            new Vector3(2.5f, .82f, 1.53f), Shape.Tetrahedron);
        vent.requires = new[] { fan };
        // Leave the high vent lane clear of the exit gate and prevent a low bypass beneath it.
        GameObject exitGate = Box(sector, "P09_ExitGate", new Vector3(0f, 6.56f, 288.73f),
            new Vector3(16.45f, 3.38f, .34f), violet);

        var returnWalk = new GameObject("P09_FanReturnWalk");
        returnWalk.transform.SetParent(sector, false);
        Box(returnWalk.transform, "ToExitCorner", new Vector3(11.47f, 5.57f, 288.84f),
            new Vector3(3.15f, .46f, 4.32f), blue);
        Box(returnWalk.transform, "ToReunion", new Vector3(9.2f, 5.57f, 290.67f),
            new Vector3(4.8f, .46f, 2.15f), blue);
        returnWalk.SetActive(false);
        vent.conceal = new[] { exitGate, islandGate };
        vent.reveal = new[] { returnWalk };

        if (sector.Find("Checkpoint_P09").TryGetComponent<Map1TeamCheckpoint>(out var checkpoint))
            checkpoint.arrivalSize = P09ArrivalSize;
        return true;
    }

    private static GameObject P09Wind(Transform sector, string name, Vector3 center, Vector3 size,
        Vector3 direction, float speed, float airMultiplier, Color streamColor)
    {
        var zone = new GameObject(name);
        zone.transform.SetParent(sector, false);
        zone.transform.SetPositionAndRotation(center, Quaternion.LookRotation(direction));
        var volume = zone.AddComponent<BoxCollider>();
        volume.size = size;
        volume.isTrigger = true;
        var wind = zone.AddComponent<WindZone>();
        wind.windSpeed = speed;
        wind.airMultiplier = airMultiplier;

        var visual = new GameObject("WindStreaks");
        visual.transform.SetParent(zone.transform, false);
        var particles = visual.AddComponent<ParticleSystem>();
        string path = MaterialFolder + "/WindStreaks.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = WindZoneMenuItem.CreateWindMaterial();
            AssetDatabase.CreateAsset(material, path);
        }
        WindZoneMenuItem.ConfigureWindParticles(particles, size, material);
        var main = particles.main;
        main.startColor = streamColor;
        main.startLifetime = .42f;
        main.startSize = .055f;
        wind.windVisual = particles;
        return zone;
    }
}
