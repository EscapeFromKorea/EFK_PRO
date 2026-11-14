using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Map2KitchenDraft.Editor
{
    /// <summary>Creates a self-contained, reviewable kitchen scene. All dimensions are provisional.</summary>
    public static class KitchenDraftBuilder
    {
        private const string Root = "Assets/Scenes/Map2_Kitchen/Rebuild";
        private const string ScenePath = Root + "/Scenes/Map2KitchenDraft.unity";
        private static readonly float[] Starts = { 2f, 27f, 52f, 78f, 104f, 130f, 158f }; // Legacy unused V1 helpers below.
        private static readonly Vector3[] ReviewStarts = {
            new Vector3(-4f, .8f, -7f), new Vector3(0f, 3.8f, 17f),
            new Vector3(0f, 3.8f, 34f), new Vector3(32f, 4.9f, 30f),
            new Vector3(36f, 1.3f, 10f), new Vector3(35f, 4.3f, -10f),
            new Vector3(-4f, 4.3f, -10f) };
        private static readonly Vector3[] ReviewEnds = {
            new Vector3(0f, 3.8f, 17f), new Vector3(0f, 3.8f, 31f),
            new Vector3(29f, 4.3f, 35f), new Vector3(37f, 1.3f, 16f),
            new Vector3(35f, 4.3f, -9f), new Vector3(-1f, 4.3f, -10f),
            new Vector3(-32f, .9f, -10f) };
        private static readonly Vector3[] RegionCenters = {
            new Vector3(0, 2, 0), new Vector3(0, 5, 23), new Vector3(13, 5, 36),
            new Vector3(36, 5, 30), new Vector3(36, 3, 8), new Vector3(16, 5, -5),
            new Vector3(-17, 5, -10) };
        private static readonly Vector3[] RegionSizes = {
            new Vector3(18, 8, 18), new Vector3(14, 8, 16), new Vector3(30, 8, 14),
            new Vector3(16, 9, 18), new Vector3(16, 7, 18), new Vector3(46, 8, 22),
            new Vector3(38, 12, 20) };
        private static readonly string[] Names = { "주방 진입과 쥐", "고정 물줄기 살균기", "접시 경로와 쥐 재등장", "설거지 상부", "싱크대 내부", "음식 컨베이어와 그릇", "조리대·열쇠·출구" };
        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        private static Transform world;
        private static KitchenDraftController controller;
        private static Camera camera;

        [Serializable] private sealed class Audit
        {
            public bool success;
            public string scenePath;
            public int chapterCount;
            public int spatialRegionCount;
            public bool perspectiveOrbitCamera;
            public bool exitOnLowerLanding;
            public int fixedWaterJets;
            public int creatureMarkers;
            public bool cockroachWithinSink;
            public bool keyGateWired;
            public string[] issues;
        }

        [Serializable] private sealed class BuildInfo
        {
            public string result;
            public string output;
            public string unityVersion;
            public ulong totalSize;
            public string[] messages;
        }

        [MenuItem("Tools/Map2 Kitchen Draft/1 Build And Validate")]
        public static void BuildAndValidate()
        {
            EnsureFolder(Root);
            EnsureFolder(Root + "/Editor");
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Art/Meshes");
            Mats.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            world = new GameObject("MAP 2 - LAB RESTAURANT KITCHEN").transform;
            SetupRender();
            SetupMaterials();

            var starts = new Transform[7];
            var regions = new DraftChapterRegion[7];
            for (int i = 0; i < starts.Length; i++)
            {
                var section = new GameObject("CH" + (i + 1) + " - " + Names[i]);
                section.transform.SetParent(world);
                starts[i] = new GameObject("Review Start " + (i + 1)).transform;
                starts[i].SetParent(section.transform);
                starts[i].position = ReviewStarts[i];
                var end = new GameObject("Review End " + (i + 1)).transform;
                end.SetParent(section.transform);
                end.position = ReviewEnds[i];
                var regionObject = new GameObject("CH" + (i + 1) + " Region Trigger");
                regionObject.transform.SetParent(section.transform);
                regionObject.transform.position = RegionCenters[i];
                var volume = regionObject.AddComponent<BoxCollider>();
                volume.isTrigger = true;
                volume.size = RegionSizes[i];
                regions[i] = regionObject.AddComponent<DraftChapterRegion>();
                regions[i].index = i;
                regions[i].chapterName = Names[i];
                regions[i].objective = i == 6 ? "열쇠를 찾아 상단 출구로 이동" : "주방 경로를 따라 다음 구역으로 이동";
                regions[i].volume = volume;
                regions[i].start = starts[i];
                regions[i].end = end;
            }

            Build3DEntry(starts[0].parent);
            Build3DSterilizer(starts[1].parent);
            Build3DPlates(starts[2].parent);
            Build3DSinkTop(starts[3].parent);
            Build3DSinkInside(starts[4].parent);
            Build3DConveyor(starts[5].parent);
            Build3DCooking(starts[6].parent);
            // The first five zones are one northern loop; moving their common section
            // roots also moves geometry, review markers and region trigger volumes.
            for (int i = 0; i < 5; i++)
                starts[i].parent.position += new Vector3(0, 0, 14f);

            var controlObject = new GameObject("Map2 Review Controller");
            controller = controlObject.AddComponent<KitchenDraftController>();
            controller.chapterStarts = starts;
            controller.chapterNames = Names;
            controller.chapters = regions;
            controller.chapterBoundaries = null; // Spatial 3D regions replace one-dimensional Z boundaries.
            controller.interactionRadius = 2.5f;
            var key = GameObject.Find("Map2 Exit Key");
            var door = GameObject.Find("Key-Only Upper Exit Door");
            var drop = GameObject.Find("Map3 Connection - Drop Point");
            controller.key = key.transform;
            controller.doorVisual = door.transform;
            controller.doorBlocker = door.GetComponent<Collider>();
            controller.doorOpenOffset = new Vector3(0, 3.0f, 0);
            controller.finalDropPoint = drop.transform;

            CreatePlayers();
            SetupCamera();
            controller.previewCamera = camera;
            var trigger = GameObject.Find("Exit Trigger - Key Required").GetComponent<DraftExitTrigger>();
            trigger.controller = controller;

            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            WriteAudit();
            Debug.Log("[Map2 Kitchen] Scene created and audited: " + ScenePath);
        }

        private static void SetupRender()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Custom;
            RenderSettings.ambientLight = new Color(.76f, .81f, .82f);
            var ambient = new UnityEngine.Rendering.SphericalHarmonicsL2();
            ambient.AddAmbientLight(new Color(.78f, .84f, .86f));
            RenderSettings.ambientProbe = ambient;
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(.12f, .18f, .22f);
            RenderSettings.fogDensity = .003f;
            var sun = new GameObject("Soft Kitchen Daylight").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = new Color(.86f, .94f, 1f);
            sun.transform.rotation = Quaternion.Euler(47, -29, 0);
        }

        private static void SetupMaterials()
        {
            Mat("Floor", .21f, .28f, .31f, .12f, .42f);
            Mat("Steel", .72f, .78f, .78f, .48f, .62f);
            Mat("SteelDark", .32f, .42f, .44f, .38f, .48f);
            Mat("Wall", .30f, .43f, .46f, .10f, .48f);
            Mat("Belt", .38f, .24f, .19f, .08f, .27f);
            Mat("BeltEdge", .25f, .14f, .11f, .04f, .22f);
            Mat("Plate", .93f, .93f, .83f, .02f, .72f);
            Mat("Water", .24f, .76f, .90f, .10f, .72f, true);
            string jetPath = Root + "/Materials/WaterJet.mat";
            var jetMaterial = AssetDatabase.LoadAssetAtPath<Material>(jetPath);
            var jetShader = Shader.Find("Sprites/Default");
            if (jetShader == null) throw new InvalidOperationException("Sprites/Default shader missing for water jets");
            if (jetMaterial == null)
            {
                jetMaterial = new Material(jetShader);
                jetMaterial.name = "WaterJet";
                AssetDatabase.CreateAsset(jetMaterial, jetPath);
            }
            else jetMaterial.shader = jetShader;
            jetMaterial.color = Color.white;
            EditorUtility.SetDirty(jetMaterial);
            Mats["WaterJet"] = jetMaterial;
            Mat("Warm", .96f, .69f, .30f, .04f, .44f, true);
            Mat("Warning", .91f, .31f, .21f, .02f, .45f, true);
            Mat("Rat", .36f, .31f, .29f, .01f, .35f);
            Mat("Roach", .24f, .11f, .08f, .03f, .38f);
            Mat("Drain", .09f, .16f, .18f, .60f, .38f);
            Mat("Key", .99f, .78f, .20f, .68f, .75f, true);
        }

        private static Material Mat(string name, float r, float g, float b, float metallic, float smooth, bool glow = false)
        {
            var path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Standard"));
                m.name = name;
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = new Color(r, g, b);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Glossiness", smooth);
            if (glow)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(r, g, b) * .48f);
            }
            EditorUtility.SetDirty(m);
            Mats[name] = m;
            return m;
        }

        private static GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, string mat, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mats[mat];
            if (!solid) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            else
            {
                int cameraBlocker = LayerMask.NameToLayer("CameraBlocker");
                if (cameraBlocker >= 0) go.layer = cameraBlocker;
            }
            return go;
        }

        private static GameObject Box(string name, Transform parent, float x, float y, float z, float sx, float sy, float sz, string mat, bool solid = false)
        { return Shape(PrimitiveType.Cube, name, parent, new Vector3(x, y, z), new Vector3(sx, sy, sz), mat, solid); }

        private static GameObject Cylinder(string name, Transform parent, float x, float y, float z, float sx, float sy, float sz, string mat, bool solid = false)
        { return Shape(PrimitiveType.Cylinder, name, parent, new Vector3(x, y, z), new Vector3(sx, sy, sz), mat, solid); }

        private static void BuildSectionShell(int i, Transform p)
        {
            float z = Starts[i] + 10f;
            float length = i == 6 ? 26f : 24f;
            float floorY = i == 4 ? -0.18f : (i == 0 ? -0.18f : 1.82f);
            Box("Walkable slab", p, 0, floorY, z, 10, .35f, length, "Floor", true);
            Box("Rear tiled wall", p, -5.25f, 4.5f, z, .35f, 9f, length, "Wall");
            Box("Rear wall stainless trim", p, -4.99f, 1.2f, z, .08f, .24f, length, "Steel");
            Box("Overhead service rail", p, -3.7f, 8.2f, z, .18f, .22f, length, "SteelDark");
            for (int j = 0; j < 2; j++)
            {
                float lz = z - 5f + j * 10f;
                Box("Long ceiling luminaire", p, -3f, 8.05f, lz, 1.2f, .16f, 4.5f, "Plate");
            }
            Box("Chapter color band", p, -4.98f, 5.5f, Starts[i] + 1f, .08f, .34f, 3f, i == 1 ? "Water" : (i == 6 ? "Key" : "Warm"));
        }

        private static void BuildEntry(Transform p)
        {
            Box("Cafeteria tray return", p, -2.8f, 1.25f, 8, 3.4f, .28f, 10f, "Steel", true);
            for (int i = 0; i < 6; i++) Plate(p, -2.8f, 1.48f, 4.5f + i * 1.1f, .95f);
            Rat(p, 2.4f, .55f, 1.1f, 0);
            Box("Wash entrance arch left", p, -2.8f, 3.35f, 22.5f, .5f, 4.2f, .5f, "SteelDark");
            Box("Wash entrance arch right", p, 2.8f, 3.35f, 22.5f, .5f, 4.2f, .5f, "SteelDark");
            Box("Wash entrance arch crown", p, 0, 5.4f, 22.5f, 6.1f, .5f, .5f, "Steel");
            var ramp = Box("TEMP REVIEW RAMP - ascent undecided", p, 0, .96f, 19.5f, 3.2f, .24f, 7f, "Steel", true);
            ramp.transform.rotation = Quaternion.Euler(-16f, 0, 0);
        }

        private static void BuildSterilizer(Transform p)
        {
            Box("Sterilizer roof", p, 0, 6.4f, 36.4f, 7.4f, .35f, 17.2f, "Steel");
            Box("Sterilizer back", p, -3.5f, 4.15f, 36.4f, .25f, 4.5f, 17.2f, "SteelDark");
            Box("Sterilizer front lower lip", p, 3.45f, 2.5f, 36.4f, .20f, .65f, 17.2f, "Steel");
            for (int i = 0; i < 4; i++)
            {
                float z = 30.2f + i * 4.0f;
                Cylinder("Fixed water nozzle " + (i + 1), p, 0, 6.04f, z, .42f, .22f, .42f, "SteelDark");
                var jetObject = new GameObject("Fixed water jet " + (i + 1));
                jetObject.transform.SetParent(p);
                jetObject.transform.position = new Vector3(0, 5.9f, z);
                var jet = jetObject.AddComponent<DraftWaterJet>();
                jet.localEnd = new Vector3(0, -3.75f, 0);
                jet.width = .25f;
                jet.waterMaterial = Mats["Water"];
                jet.enabled = false;
                jet.enabled = true;
            }
            Box("Sterilizer outlet hood", p, 0, 5.2f, 47.5f, 7.4f, 2.4f, .45f, "SteelDark");
        }

        private static void BuildPlates(Transform p)
        {
            Box("Dish service counter", p, -3.0f, 2.4f, 61.5f, 3.5f, .35f, 17f, "Steel", true);
            for (int i = 0; i < 7; i++)
            {
                float z = 53f + i * 2.3f;
                Plate(p, i % 2 == 0 ? -2.9f : -1.65f, 2.65f + .08f * (i % 3), z, 1.2f);
            }
            for (int i = 0; i < 4; i++)
                Box("Dish stepping pad " + (i + 1), p, 1.0f, 2.25f + .28f * (i % 2), 56f + i * 3f, 2.1f, .26f, 1.8f, "Plate", true);
            Rat(p, -3.0f, 2.7f, 70.5f, 2);
            Box("Side rat entry opening", p, -4.93f, 2.9f, 70.5f, .15f, 2.2f, 2.8f, "Drain");
        }

        private static void BuildSinkTop(Transform p)
        {
            Box("Sink worktop left", p, -2.8f, 2.35f, 84.7f, 3.5f, .3f, 13.5f, "Steel", true);
            Box("Sink worktop right", p, 2.8f, 2.35f, 84.7f, 3.5f, .3f, 13.5f, "Steel", true);
            Box("Sink basin backdrop", p, 0, .7f, 84.7f, 3.4f, 1.4f, 13.5f, "SteelDark");
            Box("Faucet upright", p, -1.5f, 4.1f, 81f, .3f, 3.3f, .3f, "Steel");
            Box("Faucet horizontal", p, -.8f, 5.6f, 81f, 1.7f, .27f, .28f, "Steel");
            Box("Pest light mount - tracking undecided", p, -3.8f, 6.1f, 89f, .9f, .9f, 1.5f, "SteelDark");
            Cylinder("Pest light lens", p, -3.14f, 5.8f, 89f, .45f, .15f, .45f, "Water");
            Box("Open sink descent lip", p, 0, 2.15f, 95.5f, 3.4f, .15f, .5f, "Steel");
        }

        private static void BuildSinkInside(Transform p)
        {
            Box("Sink basin floor", p, 0, .08f, 113f, 7.2f, .28f, 21f, "SteelDark", true);
            Box("Sink basin back rim", p, -3.5f, 1.4f, 113f, .24f, 2.4f, 21f, "Steel");
            Box("Sink basin front rim (low)", p, 3.5f, .65f, 113f, .24f, .9f, 21f, "Steel");
            for (int i = 0; i < 4; i++) Cylinder("Drain ring " + i, p, 0, .26f, 111.4f, 2.5f - .38f * i, .035f, 2.5f - .38f * i, i % 2 == 0 ? "Drain" : "Steel");
            var bounds = new GameObject("Cockroach activity bounds - sink only");
            bounds.transform.SetParent(p);
            bounds.transform.position = new Vector3(0, .9f, 113f);
            var box = bounds.AddComponent<BoxCollider>();
            box.size = new Vector3(6.4f, 1.7f, 19.5f);
            box.isTrigger = true;
            var roach = Shape(PrimitiveType.Sphere, "Cockroach visual marker - sink only", p, new Vector3(1.1f, .48f, 112.2f), new Vector3(1.1f, .42f, 1.5f), "Roach");
            var marker = roach.AddComponent<DraftCreatureMarker>();
            marker.kind = CreatureKind.Cockroach;
            marker.chapterIndex = 4;
            marker.activityBounds = box;
            marker.visualOnly = true;
            for (int i = 0; i < 6; i++)
                Box("Cockroach leg " + i, roach.transform, 1.1f + (i % 2 == 0 ? -.55f : .55f), .35f, 111.55f + i * .28f, .75f, .08f, .08f, "Roach");
            var ramp = Box("TEMP REVIEW RAMP - sink ascent undecided", p, 0, 1.03f, 123f, 3f, .26f, 7f, "Steel", true);
            ramp.transform.rotation = Quaternion.Euler(-15f, 0, 0);
        }

        private static void BuildConveyor(Transform p)
        {
            float center = 139.8f;
            Box("TEMP REVIEW WALKWAY - CH6 to CH7", p, 0, 1.82f, 153.5f, 3.5f, .35f, 5.0f, "Steel", true);
            Box("Conveyor walkable core", p, 0, 2.5f, center, 3.5f, .18f, 23.5f, "BeltEdge", true);
            Box("Stainless left counter", p, -3.45f, 2.38f, center, 3.3f, .25f, 24f, "Steel", true);
            Box("Stainless right counter", p, 3.45f, 2.38f, center, 3.3f, .25f, 24f, "Steel", true);
            Box("Left belt guide", p, -1.89f, 2.85f, center, .17f, .42f, 24f, "Steel");
            Box("Right belt guide", p, 1.89f, 2.85f, center, .17f, .42f, 24f, "Steel");
            for (int i = 0; i < 54; i++)
            {
                float z = 128.2f + i * .43f;
                Box("Brown segmented food belt slat " + i, p, 0, 2.67f, z, 3.54f, .13f, .38f, i % 9 == 0 ? "BeltEdge" : "Belt");
            }
            for (int i = 0; i < 5; i++)
            {
                float z = 130f + i * 4.6f;
                Box("Lower service cabinet " + i, p, -3.25f, 1.25f, z, 2.8f, 1.8f, 4.2f, "SteelDark");
                Box("Cabinet pull " + i, p, -1.76f, 1.25f, z, .12f, .14f, 1.05f, "Steel");
                Box("Conveyor support " + i, p, 2.9f, 1.15f, z, .25f, 2.0f, .35f, "SteelDark");
            }
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3 - i; j++) Plate(p, -3.3f, 2.68f + i * .17f, 136.5f + j * 1.6f, .82f);
            Rat(p, -3.4f, 2.85f, 149f, 5);
            Box("Rat pursuit ends here - marker", p, -4.95f, 4.8f, 152f, .12f, .35f, 2.5f, "Warning");
        }

        private static void BuildCooking(Transform p)
        {
            Box("Cooking prep table", p, -3.0f, 3.4f, 167f, 3.2f, .3f, 15f, "Steel", true);
            for (int i = 0; i < 3; i++)
            {
                Cylinder("Cooking pan " + i, p, -3f, 3.6f, 162f + i * 4.5f, 1.55f, .11f, 1.55f, "Drain");
                Box("Pan handle " + i, p, -1.75f, 3.6f, 162f + i * 4.5f, 1.3f, .12f, .23f, "SteelDark");
            }
            Box("Cooking cabinet base", p, -3.0f, 2.5f, 167f, 3.0f, 1.5f, 15f, "SteelDark");
            var key = Cylinder("Map2 Exit Key", p, 0, 2.8f, 170.0f, .7f, .08f, .7f, "Key");
            Cylinder("Key stem", key.transform, 0, 3.2f, 170f, .11f, .43f, .11f, "Key");
            var ramp = Box("TEMP REVIEW RAMP - upper opening access", p, 0, 3.0f, 173.8f, 2.8f, .24f, 7f, "Steel", true);
            ramp.transform.rotation = Quaternion.Euler(-17f, 0, 0);
            Box("Upper exit catwalk", p, 0, 3.85f, 178.6f, 3.4f, .3f, 6.4f, "Steel", true);
            Box("Only upper exit frame left", p, -1.75f, 5.0f, 178.5f, .35f, 2.4f, .45f, "SteelDark");
            Box("Only upper exit frame right", p, 1.75f, 5.0f, 178.5f, .35f, 2.4f, .45f, "SteelDark");
            Box("Only upper exit frame crown", p, 0, 6.2f, 178.5f, 3.8f, .35f, .45f, "Steel");
            var door = Box("Key-Only Upper Exit Door", p, 0, 4.95f, 178.5f, 3.15f, 2.15f, .28f, "Warning", true);
            var exit = new GameObject("Exit Trigger - Key Required");
            exit.transform.SetParent(p);
            exit.transform.position = new Vector3(0, 4.7f, 180.2f);
            var triggerBox = exit.AddComponent<BoxCollider>();
            triggerBox.isTrigger = true;
            triggerBox.size = new Vector3(3.5f, 2.6f, 1f);
            exit.AddComponent<DraftExitTrigger>();
            Box("Food dispatch service window", p, -3.1f, 4.0f, 182.5f, 3.5f, 2.4f, .25f, "Steel");
            Box("Map3 transfer warning stripe", p, 0, 2.25f, 182.2f, 5f, .08f, .65f, "Warm");
            var point = new GameObject("Map3 Connection - Drop Point");
            point.transform.SetParent(p);
            point.transform.position = new Vector3(0, -2.0f, 184f);
            Cylinder("Open drop shaft", p, 0, -3f, 184f, 3.5f, 2.8f, 3.5f, "Drain");
            Box("Dispatch end wall", p, -4.8f, 5.0f, 185.8f, .35f, 7f, .5f, "SteelDark");
        }

        // V2 is a true XZ route. The old linear builders above remain unused so that
        // the first draft stays easy to compare while this separate scene is reviewed.
        private static GameObject Floor3D(string name, Transform p, float x, float top, float z, float sx, float sz, string material = "Floor")
        { return Box(name, p, x, top - .18f, z, sx, .36f, sz, material, true); }

        private static void RampZ(string name, Transform p, float x, float z0, float y0, float z1, float y1, float width)
        {
            float run = z1 - z0;
            float angle = -Mathf.Atan2(y1 - y0, run) * Mathf.Rad2Deg;
            var ramp = Box(name, p, x, (y0 + y1) * .5f - .15f, (z0 + z1) * .5f,
                width, .3f, Mathf.Sqrt(run * run + (y1 - y0) * (y1 - y0)) + .5f, "Steel", true);
            ramp.transform.rotation = Quaternion.Euler(angle, 0, 0);
        }

        private static void RampX(string name, Transform p, float x0, float y0, float x1, float y1, float z, float width)
        {
            float run = x1 - x0;
            float angle = Mathf.Atan2(y1 - y0, run) * Mathf.Rad2Deg;
            // atan2 wraps at 180 for a westbound ramp; slope depends on dy/dx.
            angle = Mathf.Atan((y1 - y0) / run) * Mathf.Rad2Deg;
            var ramp = Box(name, p, (x0 + x1) * .5f, (y0 + y1) * .5f - .15f, z,
                Mathf.Sqrt(run * run + (y1 - y0) * (y1 - y0)) + .5f, .3f, width, "Steel", true);
            ramp.transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        private static void Build3DEntry(Transform p)
        {
            Floor3D("CH1 - researcher cafeteria entry floor", p, 0, 0, 0, 18, 18);
            Box("Cafeteria stainless service wall", p, -8.8f, 2.2f, 0, .3f, 4.4f, 17.5f, "Wall", true);
            Box("Cafeteria low counter", p, -5f, 1.2f, -1f, 2.4f, .3f, 9f, "Steel", true);
            for (int i = 0; i < 5; i++) Plate(p, -5f, 1.4f, -4.5f + i * 1.6f, .85f);
            Rat(p, 3.2f, .55f, -6f, 0);
            RampZ("TEMP CH1 wash entrance ramp", p, 0, 5, 0, 15, 3, 5f);
            Box("Wash entrance left post", p, -3f, 3.1f, 15f, .4f, 5.5f, .45f, "SteelDark");
            Box("Wash entrance right post", p, 3f, 3.1f, 15f, .4f, 5.5f, .45f, "SteelDark");
            Box("Wash entrance header", p, 0, 5.9f, 15f, 6.4f, .35f, .5f, "Steel");
        }

        private static void Build3DSterilizer(Transform p)
        {
            Floor3D("CH2 - sterilizer walkable floor", p, 0, 3, 23, 14, 16, "SteelDark");
            Box("Sterilizer left stainless side", p, -6.7f, 5.1f, 23, .3f, 3.6f, 15.5f, "Steel", true);
            Box("Sterilizer right low guide", p, 6.7f, 3.8f, 23, .22f, .8f, 15.5f, "Steel");
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -2f : 2f);
                float z = 18.5f + i * 3.1f;
                Box("Sterilizer overhead beam " + i, p, 0, 7.05f, z, 13f, .25f, .4f, "Steel", true);
                Cylinder("Fixed jet nozzle " + i, p, x, 6.65f, z, .5f, .2f, .5f, "SteelDark");
                var source = new GameObject("Fixed water jet " + i);
                source.transform.SetParent(p);
                source.transform.position = new Vector3(x, 6.5f, z);
                var jet = source.AddComponent<DraftWaterJet>();
                jet.localEnd = new Vector3(0, -3.15f, 0);
                jet.width = .42f;
                jet.waterMaterial = Mats["WaterJet"];
                jet.enabled = false;
                jet.enabled = true;
            }
            Box("Sterilizer service panel", p, -6.45f, 5.5f, 24f, .12f, 1.2f, 2.3f, "Water");
        }

        private static void DishStep(Transform p, int index, float x, float top, float z)
        {
            var dish = Cylinder("CH3 walkable dish " + index, p, x, top - .13f, z, 4.2f, .13f, 4.2f, "Plate", true);
            UnityEngine.Object.DestroyImmediate(dish.GetComponent<Collider>());
            var meshCollider = dish.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = dish.GetComponent<MeshFilter>().sharedMesh;
            meshCollider.convex = false;
            Cylinder("Dish rim " + index, p, x, top + .025f, z, 3.8f, .025f, 3.8f, "Plate");
            Cylinder("Dish inset " + index, p, x, top + .055f, z, 2.7f, .01f, 2.7f, "Steel");
        }

        private static void Build3DPlates(Transform p)
        {
            float[,] nodes = { { 0, 3, 34 }, { 4, 3.2f, 35 }, { 7.5f, 3.3f, 38 },
                { 11, 3.1f, 40 }, { 14.5f, 3.4f, 38 }, { 18, 3.25f, 35 },
                { 22, 3.4f, 35 }, { 25.5f, 3.25f, 36 } };
            for (int i = 0; i < nodes.GetLength(0); i++)
                DishStep(p, i + 1, nodes[i, 0], nodes[i, 1], nodes[i, 2]);
            Rat(p, 15f, 3.75f, 41.5f, 2);
            Box("Rat re-entry hatch", p, 15f, 4.6f, 43f, 2.8f, 2.5f, .25f, "Drain");
        }

        private static void Build3DSinkTop(Transform p)
        {
            Floor3D("CH4 - sink upper walkway", p, 36, 3.5f, 30.5f, 16, 17);
            Box("Stainless sink worktop", p, 36, 4.0f, 32, 12, .28f, 9f, "Steel", true);
            Box("Large sink basin dark inset", p, 36, 4.18f, 32, 7.3f, .1f, 5.5f, "SteelDark");
            Box("Faucet riser", p, 32.3f, 5.4f, 32f, .22f, 2.5f, .22f, "Steel");
            Box("Faucet spout", p, 34f, 6.5f, 32f, 3.4f, .2f, .22f, "Steel");
            Box("Pest laser emitter place only", p, 42f, 6.4f, 34, .8f, .9f, 1.2f, "SteelDark");
            Cylinder("Pest lamp lens - final color undecided", p, 42f, 6.1f, 33.1f, .5f, .12f, .5f, "Water");
            RampZ("TEMP CH4 sink descent", p, 37, 16, .5f, 22, 3.5f, 4f);
        }

        private static void Build3DSinkInside(Transform p)
        {
            Floor3D("CH5 - sink basin floor", p, 36, .5f, 8, 16, 16, "SteelDark");
            Box("Sink basin left wall", p, 28f, 1.7f, 8, .3f, 2.4f, 15.5f, "Steel", true);
            Box("Sink basin right wall", p, 44f, 1.7f, 8, .3f, 2.4f, 15.5f, "Steel", true);
            for (int i = 0; i < 4; i++)
                Cylinder("Sink drain ring " + i, p, 35, .55f + i * .02f, 8, 3.3f - i * .45f, .025f, 3.3f - i * .45f, i % 2 == 0 ? "Drain" : "Steel");
            var activity = new GameObject("Cockroach bounds - sink only");
            activity.transform.SetParent(p);
            activity.transform.position = new Vector3(36, 1.45f, 8);
            var bounds = activity.AddComponent<BoxCollider>();
            bounds.isTrigger = true;
            bounds.size = new Vector3(14.5f, 2.4f, 14.5f);
            var roach = Shape(PrimitiveType.Sphere, "Cockroach visual marker - basin only", p,
                new Vector3(38, .8f, 7), new Vector3(1.2f, .45f, 1.6f), "Roach");
            var marker = roach.AddComponent<DraftCreatureMarker>();
            marker.kind = CreatureKind.Cockroach;
            marker.chapterIndex = 4;
            marker.activityBounds = bounds;
            marker.visualOnly = true;
            for (int i = 0; i < 6; i++)
                Box("Cockroach leg " + i, p, 38 + (i % 2 == 0 ? -.7f : .7f), .74f, 6.35f + .26f * i,
                    1.1f, .08f, .08f, "Roach");
            RampZ("TEMP CH5 sink ascent", p, 35, -9, 3.5f, 1, .5f, 5f);
        }

        private static void Build3DConveyor(Transform p)
        {
            Floor3D("CH6 - conveyor and dish sorting floor", p, 14, 3.5f, -10, 38, 12);
            Floor3D("CH6 ramp arrival lip", p, 35, 3.5f, -10, 4, 2);
            Floor3D("TEMP CH5 to CH6 connector", p, 35, 3.5f, -2, 4, 14.2f, "Steel");
            Box("Straight brown belt support", p, 14, 3.72f, -10, 37.4f, .22f, 3.5f, "BeltEdge", true);
            Box("Stainless north counter", p, 14, 3.72f, -13.7f, 38, .28f, 3.7f, "Steel", true);
            Box("Stainless south counter", p, 14, 3.72f, -6.3f, 38, .28f, 3.7f, "Steel", true);
            Box("Brown belt north guide", p, 14, 4.02f, -11.84f, 38, .32f, .16f, "Steel");
            Box("Brown belt south guide", p, 14, 4.02f, -8.16f, 38, .32f, .16f, "Steel");
            for (int i = 0; i < 82; i++)
                Box("Restaurant belt brown slat " + i, p, -4.45f + .45f * i, 3.91f, -10,
                    .41f, .10f, 3.36f, i % 12 == 0 ? "BeltEdge" : "Belt");
            for (int i = 0; i < 8; i++)
            {
                float x = -4f + i * 5f;
                Box("Conveyor lower cabinet " + i, p, x, 2.4f, -13.8f, 4.2f, 2.2f, 3f, "SteelDark");
                Box("Cabinet handle " + i, p, x, 2.5f, -12.27f, 1.2f, .13f, .1f, "Steel");
            }
            for (int i = 0; i < 6; i++) Plate(p, 11f + i * 1.4f, 4f + .15f * (i % 2), -13.2f, .77f);
            Rat(p, 4f, 4.12f, -14.2f, 5);
            Box("Rat pursuit ends at sorting boundary", p, 0, 5.4f, -15.8f, .25f, .4f, 2.5f, "Warning");
        }

        private static void Build3DCooking(Transform p)
        {
            Floor3D("CH7 - cooking work area", p, -14, 3.5f, -10, 28, 18);
            Box("Cooking prep counter", p, -13, 4.2f, -5f, 13f, .35f, 3.5f, "Steel", true);
            for (int i = 0; i < 3; i++)
            {
                Cylinder("Cooking pan " + i, p, -18f + i * 4f, 4.42f, -5f, 1.8f, .12f, 1.8f, "Drain");
                Box("Pan handle " + i, p, -16.8f + i * 4f, 4.42f, -5f, 1.35f, .12f, .2f, "SteelDark");
            }
            var key = Cylinder("Map2 Exit Key", p, -13f, 4.05f, -7f, .75f, .11f, .75f, "Key");
            Box("Gold key stem", key.transform, -13f, 4.05f, -6.3f, .13f, .13f, 1.2f, "Key");
            Box("Gold key tooth A", key.transform, -12.85f, 4.05f, -5.8f, .42f, .13f, .13f, "Key");
            Box("Gold key tooth B", key.transform, -12.85f, 4.05f, -6.05f, .32f, .13f, .13f, "Key");
            RampX("TEMP CH7 upper exit approach", p, -15, 3.5f, -21.25f, 6.5f, -14, 3.5f);
            Floor3D("Upper key-only exit walk", p, -23, 6.5f, -11.5f, 3.5f, 7.0f, "Steel");
            // The wall is physically complete except for this one key-locked high opening.
            Box("Exit wall lower sealed span", p, -25, 5.0f, -10, .35f, 3.0f, 18f, "SteelDark", true);
            Box("Exit wall upper sealed span", p, -25, 9.5f, -10, .35f, 1.2f, 18f, "SteelDark", true);
            Box("Exit wall side south", p, -25, 7.7f, -15.375f, .35f, 2.4f, 7.25f, "SteelDark", true);
            Box("Exit wall side north", p, -25, 7.7f, -4.625f, .35f, 2.4f, 7.25f, "SteelDark", true);
            Box("Key-Only Upper Exit Door", p, -25, 7.7f, -10, .35f, 2.4f, 3.5f, "Warning", true);
            Floor3D("Food dispatch upper exit ledge", p, -27.2f, 6.5f, -10, 4.4f, 4.0f, "Steel");
            Box("Food dispatch service hatch", p, -27.2f, 8.4f, -12.3f, 3.4f, 2.1f, .25f, "SteelDark");
            Floor3D("Map3 transition lower landing", p, -32, .1f, -10, 6.0f, 6.0f, "Steel");
            Box("Dispatch shaft east barrier", p, -29.15f, 3.25f, -10, .3f, 6.5f, 6.2f, "SteelDark", true);
            Box("Dispatch shaft west barrier", p, -35.15f, 4.5f, -10, .3f, 9f, 6.2f, "SteelDark", true);
            Box("Dispatch shaft south barrier", p, -32.15f, 4.5f, -13.15f, 6.3f, 9f, .3f, "SteelDark", true);
            Box("Dispatch shaft north barrier", p, -32.15f, 4.5f, -6.85f, 6.3f, 9f, .3f, "SteelDark", true);
            Box("Map3 drop landing warning stripe", p, -32, .33f, -10, 4.6f, .06f, .32f, "Warm");
            var exit = new GameObject("Exit Trigger - Key Required");
            exit.transform.SetParent(p);
            exit.transform.position = new Vector3(-32, 1.2f, -10);
            var trigger = exit.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(5.5f, 2f, 5.5f);
            exit.AddComponent<DraftExitTrigger>();
            var drop = new GameObject("Map3 Connection - Drop Point");
            drop.transform.SetParent(p);
            drop.transform.position = new Vector3(-32, .9f, -10);
        }

        private static void Plate(Transform p, float x, float y, float z, float radius)
        {
            Cylinder("White dish base", p, x, y, z, radius * 2, .07f, radius * 2, "Plate");
            Cylinder("White dish raised rim", p, x, y + .065f, z, radius * 1.72f, .035f, radius * 1.72f, "Plate");
        }

        private static void Rat(Transform p, float x, float y, float z, int chapter)
        {
            var body = Shape(PrimitiveType.Sphere, "Rat silhouette - visual only", p, new Vector3(x, y, z), new Vector3(1.65f, .8f, 2.0f), "Rat");
            var marker = body.AddComponent<DraftCreatureMarker>();
            marker.kind = CreatureKind.Rat;
            marker.chapterIndex = chapter;
            marker.visualOnly = true;
            Shape(PrimitiveType.Sphere, "Rat head", body.transform, new Vector3(x, y + .05f, z + .9f), new Vector3(.74f, .63f, .75f), "Rat");
            for (int i = -1; i <= 1; i += 2)
                Shape(PrimitiveType.Sphere, "Rat ear", body.transform, new Vector3(x + i * .31f, y + .4f, z + 1.0f), new Vector3(.36f, .44f, .19f), "Rat");
            var tail = Box("Rat tail", body.transform, x, y, z - 1.4f, .14f, .14f, 1.6f, "Rat");
            tail.transform.rotation = Quaternion.Euler(0, 22, 0);
        }

        private static void CreatePlayers()
        {
            string[] menu = { "Sphere", "Cube", "Tetrahedron" };
            for (int i = 0; i < menu.Length; i++)
            {
                Selection.activeGameObject = null;
                if (!EditorApplication.ExecuteMenuItem("Tools/PlayerSystem/Create Player/" + menu[i]))
                    throw new InvalidOperationException("PlayerSystem menu missing: " + menu[i]);
                var root = Selection.activeGameObject;
                if (root == null || root.GetComponent<PlayerMover>() == null)
                    throw new InvalidOperationException("PlayerSystem failed to create " + menu[i]);
                root.transform.position = new Vector3(-4f + i * 1.4f, .8f, 7f + i * 1.0f);
                var mover = root.GetComponent<PlayerMover>();
                mover.cameraRelativeInput = true;
                mover.inputYawOffset = 0f;
                PersistDynamicPlayerMeshes(root, menu[i]);
                var visual = root.transform.Find("Player_Mesh/Player_MeshVisual");
                if (visual == null) throw new InvalidOperationException("Player visual missing: " + menu[i]);
                int materialIndex = i == 0 ? 1 : i == 1 ? 0 : 2;
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/LevelDesignSystem/Materials/Codex/Codex_Player_" + materialIndex + ".mat");
                if (material == null) throw new InvalidOperationException("Player material missing: " + menu[i]);
                visual.GetComponent<Renderer>().sharedMaterial = material;
            }
        }

        private static void PersistDynamicPlayerMeshes(GameObject root, string suffix)
        {
            int n = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh))) continue;
                string path = Root + "/Art/Meshes/Player_" + suffix + "_Visual_" + n++ + ".asset";
                var copy = UnityEngine.Object.Instantiate(mesh);
                AssetDatabase.CreateAsset(copy, path);
                filter.sharedMesh = copy;
            }
            foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true))
            {
                var mesh = collider.sharedMesh;
                if (mesh == null || !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh))) continue;
                string path = Root + "/Art/Meshes/Player_" + suffix + "_Collider_" + n++ + ".asset";
                var copy = UnityEngine.Object.Instantiate(mesh);
                AssetDatabase.CreateAsset(copy, path);
                collider.sharedMesh = copy;
            }
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var material = renderer.sharedMaterial;
                if (material == null || !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material))) continue;
                renderer.sharedMaterial = Mats[suffix == "Sphere" ? "Water" : (suffix == "Cube" ? "Warm" : "Key")];
            }
        }

        private static void SetupCamera()
        {
            foreach (var c in UnityEngine.Object.FindObjectsOfType<Camera>()) UnityEngine.Object.DestroyImmediate(c.gameObject);
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.10f, .16f, .20f);
            camera.orthographic = false;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 350f;
            var follow = go.AddComponent<PlayerFollowCamera>();
            follow.target = null; // Native switcher chooses the initially active shape.
            follow.offset = new Vector3(0f, 4f, -10f);
            follow.cameraYawOffset = 0f;
            follow.enableMouseOrbit = true;
            follow.enableTerrainAvoidance = true;
            follow.blockerLayerName = "CameraBlocker";
            Vector3 actualEntry = ReviewStarts[0] + new Vector3(0, 0, 14f);
            go.transform.position = actualEntry + follow.offset;
            go.transform.LookAt(actualEntry + Vector3.up);
            go.AddComponent<AudioListener>();
        }

        private static void WriteAudit()
        {
            Physics.SyncTransforms();
            var issues = new List<string>();
            var c = UnityEngine.Object.FindObjectOfType<KitchenDraftController>();
            if (c == null) issues.Add("KitchenDraftController missing");
            int chapters = c != null && c.chapterStarts != null ? c.chapterStarts.Length : 0;
            if (chapters != 7) issues.Add("Expected 7 review sections, found " + chapters);
            int regionCount = c != null && c.chapters != null ? c.chapters.Length : 0;
            if (regionCount != 7) issues.Add("Expected seven 3D chapter volumes");
            if (c != null && c.chapters != null)
                for (int i = 0; i < c.chapters.Length; i++)
                {
                    var r = c.chapters[i];
                    if (r == null || r.index != i || r.volume == null || !r.volume.isTrigger || r.start == null || r.end == null)
                        issues.Add("Incomplete spatial chapter " + i);
                }
            var cameraRig = c != null && c.previewCamera != null ? c.previewCamera.GetComponent<PlayerFollowCamera>() : null;
            bool orbitCamera = c != null && c.previewCamera != null && !c.previewCamera.orthographic &&
                cameraRig != null && cameraRig.enableMouseOrbit && cameraRig.enableTerrainAvoidance;
            if (!orbitCamera) issues.Add("Native perspective orbit camera missing");
            if (c != null && c.chapterStarts != null)
                for (int i = 0; i < c.chapterStarts.Length; i++) if (c.chapterStarts[i] == null) issues.Add("Null section start " + i);
            var jets = UnityEngine.Object.FindObjectsOfType<DraftWaterJet>();
            if (jets.Length < 3) issues.Add("Expected at least 3 fixed water jets");
            foreach (var j in jets)
                if (j.TracksPlayer || j.waterMaterial == null || j.localEnd.y >= 0 || Mathf.Abs(j.localEnd.x) > .001f || Mathf.Abs(j.localEnd.z) > .001f)
                    issues.Add("Water jet is not fixed, downward, or material-wired: " + j.name);
            var creatures = UnityEngine.Object.FindObjectsOfType<DraftCreatureMarker>();
            bool roachInSink = false;
            foreach (var m in creatures)
            {
                if (!m.visualOnly) issues.Add("Creature marker unexpectedly active: " + m.name);
                if (m.kind == CreatureKind.Cockroach)
                {
                    if (m.chapterIndex != 4 || m.activityBounds == null || !m.activityBounds.bounds.Contains(m.transform.position)) issues.Add("Cockroach outside sink bounds");
                    else roachInSink = true;
                }
            }
            if (!roachInSink) issues.Add("Sink cockroach marker missing");
            bool gate = c != null && c.key != null && c.doorVisual != null && c.doorBlocker != null && c.finalDropPoint != null && c.previewCamera != null;
            var exits = UnityEngine.Object.FindObjectsOfType<DraftExitTrigger>();
            gate &= exits.Length == 1 && exits[0].controller == c && exits[0].GetComponent<Collider>() != null && exits[0].GetComponent<Collider>().isTrigger;
            bool lowerLanding = gate && exits[0].transform.position.y < 2f && c.finalDropPoint.position.y < 2f;
            if (!lowerLanding) issues.Add("Exit trigger must be on lower Map3 connection landing");
            if (!gate) issues.Add("Key-only gate or final connection is incompletely wired");
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    foreach (var component in t.GetComponents<Component>()) if (component == null) issues.Add("Missing script on " + t.name);
            var audit = new Audit { success = issues.Count == 0, scenePath = ScenePath, chapterCount = chapters,
                spatialRegionCount = regionCount, perspectiveOrbitCamera = orbitCamera, exitOnLowerLanding = lowerLanding,
                fixedWaterJets = jets.Length, creatureMarkers = creatures.Length, cockroachWithinSink = roachInSink,
                keyGateWired = gate, issues = issues.ToArray() };
            WriteJson("검증/scene_audit.json", JsonUtility.ToJson(audit, true));
            if (!audit.success) throw new InvalidOperationException("Map2 scene audit failed: " + string.Join("; ", audit.issues));
        }

        [MenuItem("Tools/Map2 Kitchen Draft/2 Capture Previews")]
        public static void CapturePreviews()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var c = UnityEngine.Object.FindObjectOfType<KitchenDraftController>();
            if (c == null || c.chapterStarts == null || c.chapterStarts.Length != 7) throw new InvalidOperationException("Build scene before previews");
            var cam = c.previewCamera;
            if (cam == null) throw new InvalidOperationException("Preview camera missing");
            var follow = cam.GetComponent<PlayerFollowCamera>();
            if (follow != null) follow.enabled = false;
            var player = UnityEngine.Object.FindObjectOfType<PlayerMover>();
            if (player == null) throw new InvalidOperationException("Review player missing");
            Vector3 originalPlayerPosition = player.transform.position;
            Vector3[] playerSpots = { new Vector3(-3, .8f, -2), new Vector3(0, 3.8f, 23),
                new Vector3(13, 4.2f, 38), new Vector3(36, 4.9f, 30), new Vector3(36, 1.3f, 8),
                new Vector3(18, 4.5f, -10), new Vector3(-14, 4.3f, -10) };
            Vector3[] targets = { new Vector3(0, 1, 0), new Vector3(0, 4, 23),
                new Vector3(13, 3.5f, 37), new Vector3(36, 4, 30), new Vector3(36, 1, 8),
                new Vector3(18, 4, -10), new Vector3(-15, 5, -10) };
            Vector3[] offsets = { new Vector3(11, 9, -15), new Vector3(9, 7, -15),
                new Vector3(0, 14, -25), new Vector3(17, 10, -15), new Vector3(16, 11, -15),
                new Vector3(15, 13, -20), new Vector3(14, 12, -20) };
            for (int i = 0; i < 5; i++)
            {
                playerSpots[i] += new Vector3(0, 0, 14f);
                targets[i] += new Vector3(0, 0, 14f);
            }
            Directory.CreateDirectory(OutputPath("미리보기"));
            for (int i = 0; i < 7; i++)
            {
                player.transform.position = playerSpots[i];
                Physics.SyncTransforms();
                PositionPerspectiveCamera(cam, targets[i], offsets[i], 60f);
                RenderPng(cam, OutputPath("미리보기/CH" + (i + 1) + ".png"));
            }
            player.transform.position = playerSpots[5];
            PositionPerspectiveCamera(cam, new Vector3(18, 4, -10), new Vector3(16, 9, -13), 53f);
            RenderPng(cam, OutputPath("미리보기/Conveyor_Detail.png"));
            player.transform.position = playerSpots[1];
            PositionPerspectiveCamera(cam, new Vector3(0, 4, 37), new Vector3(8, 8, -12), 56f);
            RenderPng(cam, OutputPath("미리보기/Sterilizer_Detail.png"));
            player.transform.position = originalPlayerPosition;
            PositionPerspectiveCamera(cam, new Vector3(5, 3, 17), new Vector3(5, 100, -65), 60f);
            RenderPng(cam, OutputPath("미리보기/Map2_Overview.png"));
            Debug.Log("[Map2 Kitchen] 10 actual scene camera previews captured; scene state not saved");
        }

        private static void PositionPerspectiveCamera(Camera cam, Vector3 target, Vector3 offset, float fov)
        {
            cam.orthographic = false;
            cam.fieldOfView = fov;
            cam.transform.position = target + offset;
            cam.transform.LookAt(target);
        }

        private static void RenderPng(Camera cam, string path)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            try
            {
                cam.targetTexture = rt;
                RenderTexture.active = rt;
                cam.Render();
                texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        [MenuItem("Tools/Map2 Kitchen Draft/3 Build Windows Preview")]
        public static void BuildWindows()
        {
            string projectScenePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ScenePath);
            if (!File.Exists(projectScenePath)) throw new FileNotFoundException("Build scene first", projectScenePath);
            string output = OutputPath("실행본/Map2KitchenDraft.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var options = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development };
            string previousProductName = PlayerSettings.productName;
            BuildReport report;
            try
            {
                PlayerSettings.productName = "The Axiom - Map2 Kitchen 3D Draft";
                report = BuildPipeline.BuildPlayer(options);
            }
            finally { PlayerSettings.productName = previousProductName; }
            var messages = new List<string>();
            if (report.steps != null)
                foreach (var step in report.steps)
                    foreach (var message in step.messages)
                        if (message.type == LogType.Warning || message.type == LogType.Error || message.type == LogType.Exception)
                            messages.Add(message.type + ": " + message.content);
            var result = new BuildInfo { result = report.summary.result.ToString(), output = output,
                unityVersion = Application.unityVersion, totalSize = report.summary.totalSize, messages = messages.ToArray() };
            WriteJson("검증/build_report.json", JsonUtility.ToJson(result, true));
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Map2 Windows preview build failed: " + report.summary.result);
        }

        private static string OutputPath(string relative)
        { return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, relative)); }

        private static void WriteJson(string path, string json)
        {
            string full = OutputPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, json, System.Text.Encoding.UTF8);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash <= 0) return;
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
