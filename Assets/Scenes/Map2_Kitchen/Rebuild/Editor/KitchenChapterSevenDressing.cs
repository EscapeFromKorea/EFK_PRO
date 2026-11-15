using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Map2KitchenDraft.Editor
{
    /// <summary>Visual-only finishes for the existing chapter seven cooking and serving line.</summary>
    internal static class KitchenChapterSevenDressing
    {
        private const string RootName = "CH7 cafeteria serving details";
        private const string MaterialFolder = "Assets/Scenes/Map2_Kitchen/Rebuild/Materials/V3/";

        [MenuItem("Tools/Map2 Kitchen Rebuild/Apply Chapter 7 Serving Finish")]
        public static void ApplyToSavedScenes()
        {
            foreach (string path in new[] { KitchenDetailScene.ScenePath, KitchenConfirmedScene.ScenePath })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var draft = UnityEngine.Object.FindFirstObjectByType<KitchenDraftController>();
                Add(draft.chapters[6].transform.parent);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new IOException("Could not save " + path);
            }
        }

        [MenuItem("Tools/Map2 Kitchen Rebuild/Capture Chapter 7 Serving Views")]
        public static void CaptureServingViews()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before capturing serving views.");
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != KitchenConfirmedScene.ScenePath)
                throw new InvalidOperationException("Open the playable kitchen scene first.");

            var camera = Camera.main;
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "미리보기", "디테일"));
            try
            {
                camera.fieldOfView = 64f;
                Capture("CH7_조리라인", new Vector3(127f, 10.25f, -20f), new Vector3(132.3f, 8.8f, -34f));
                Capture("CH7_배식대", new Vector3(118f, 10.9f, -119f), new Vector3(103.5f, 8.8f, -128f));
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
            }

            void Capture(string name, Vector3 eye, Vector3 target)
            {
                camera.transform.position = eye;
                camera.transform.LookAt(target);
                KitchenRebuildBuilder.RenderPng(camera, Path.Combine(folder, name + ".png"));
            }
        }

        internal static void Add(Transform chapter)
        {
            Transform previous = chapter.Find(RootName);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);

            var steel = LoadMaterial("Steel");
            var darkSteel = LoadMaterial("SteelDark");
            var inset = LoadMaterial("Drain");
            var ivory = LoadMaterial("Plate");
            var group = new GameObject(RootName).transform;
            group.SetParent(chapter, false);

            // Dress the counter visible across the belt at the chapter entrance.
            // Its worktop is much lower than the belt and stays background scenery.
            AddTrays(group, new Vector3(92f, 4.2f, -73f), ivory);
            AddPan(group, new Vector3(87.2f, 4.2f, -72.6f), steel, darkSteel);
            AddPan(group, new Vector3(83.8f, 4.2f, -72.6f), steel, darkSteel);
            AddLadle(group, new Vector3(92.5f, 6.18f, -63.55f), steel, darkSteel);

            // Keep cooking equipment on its existing counters and dress their end strips.
            foreach (int station in new[] { 0, 1, 3, 5 })
            {
                float z = -24f - station * 18f;
                for (int door = 0; door < 3; door++)
                {
                    var center = new Vector3(132.32f, 6.17f, z + (door - 1) * 3.45f);
                    FinishDoor(group, center, Quaternion.Euler(0, -90, 0), 3.23f, 2.52f, steel, darkSteel, inset);
                }

                float end = z - 4.88f;
                AddPan(group, new Vector3(134.9f, 7.9f, end), steel, darkSteel);
                if (station == 0 || station == 3)
                    AddTrays(group, new Vector3(140f, 7.9f, end), ivory);
                else
                    AddPan(group, new Vector3(139.4f, 7.9f, end), steel, darkSteel);

                Box("Brushed steel splashback", group, new Vector3(151.60f, 9.29f, z),
                    new Vector3(.12f, 2.78f, 11.8f), steel);
            }

            foreach (float x in new[] { 108f, 92f })
            {
                const float front = -125.99f;
                for (int door = 0; door < 3; door++)
                {
                    var center = new Vector3(x + (door - 1) * 4.05f, 5.72f, front);
                    FinishDoor(group, center, Quaternion.identity, 3.83f, 3.18f, steel, darkSteel, inset);
                }
                AddTrays(group, new Vector3(x - 3.85f, 7.75f, -127.52f), ivory);
                AddPan(group, new Vector3(x, 7.75f, -127.52f), steel, darkSteel);
                AddPan(group, new Vector3(x + 3.48f, 7.75f, -127.52f), steel, darkSteel);
                Box("Sanitary backsplash facing", group, new Vector3(x, 9.13f, -133.94f),
                    new Vector3(13.14f, 2.78f, .08f), steel);
                AddLadle(group, new Vector3(x + 4.8f, 9.55f, -133.5f), steel, darkSteel);
                AddServingLight(group, new Vector3(x, 10.65f, -128.84f));
            }
            AddServingLight(group, new Vector3(137.15f, 10.72f, -28.44f));
            AddServingLight(group, new Vector3(137.15f, 10.72f, -46.44f));

            DressBeltSides(" - CH7 east white-line to cooking", 132f);
            DressBeltSides(" - CH7 bottom white-line cooking route", 176f);

            if (group.GetComponentInChildren<Collider>(true) != null)
                throw new InvalidOperationException("Chapter 7 dressing must have no colliders.");

            void DressBeltSides(string suffix, float length)
            {
                Transform belt = chapter.Cast<Transform>()
                    .Single(item => item.name.EndsWith(suffix, StringComparison.Ordinal));
                foreach (float side in new[] { -1f, 1f })
                {
                    float edge = side * 3.49f;
                    Vector3 World(float x, float y, float z) => belt.TransformPoint(new Vector3(x, y, z));

                    Box("Conveyor folded stainless side casing", group, World(edge, -.47f, 0f),
                        new Vector3(.16f, .68f, length - 4f), steel, belt.rotation);
                    Box("Conveyor folded bottom lip", group, World(edge, -.81f, 0f),
                        new Vector3(.22f, .10f, length - 4f), darkSteel, belt.rotation);
                    for (float z = 12f - length / 2f; z < length / 2f - 6f; z += 24f)
                    {
                        Box("Conveyor inspection panel seam", group, World(edge + side * .09f, -.48f, z),
                            new Vector3(.04f, .44f, 2.75f), darkSteel, belt.rotation);
                        Box("Conveyor removable inspection cover", group, World(edge + side * .113f, -.48f, z),
                            new Vector3(.04f, .35f, 2.61f), steel, belt.rotation);
                        Box("Conveyor inspection cover pull", group, World(edge + side * .14f, -.47f, z),
                            new Vector3(.04f, .09f, .44f), inset, belt.rotation);
                    }
                }
            }
        }

        private static void FinishDoor(Transform group, Vector3 center, Quaternion facing,
            float width, float height, Material steel, Material darkSteel, Material inset)
        {
            Vector3 At(float y, float depth) => center + facing * new Vector3(0f, y, depth);
            Box("Inset seam around service cabinet door", group, center,
                new Vector3(width, height, .15f), darkSteel, facing);
            Box("Brushed stainless service cabinet door", group, At(0f, .083f),
                new Vector3(width - .14f, height - .13f, .085f), steel, facing);
            Box("Recessed cabinet pull rim", group, At(height * .21f, .144f),
                new Vector3(1.05f, .24f, .045f), darkSteel, facing);
            Box("Recessed cabinet pull well", group, At(height * .21f, .17f),
                new Vector3(.81f, .12f, .028f), inset, facing);
        }

        private static void AddPan(Transform group, Vector3 at, Material steel, Material darkSteel)
        {
            Box("Stainless serving pan body", group, at + Vector3.up * .18f,
                new Vector3(2.52f, .36f, 1.15f), darkSteel);
            Box("Stainless serving pan rim", group, at + Vector3.up * .37f,
                new Vector3(2.68f, .10f, 1.27f), steel);
            Box("Fitted serving pan lid", group, at + Vector3.up * .45f,
                new Vector3(2.37f, .08f, 1.02f), steel);
            Box("Serving pan handle", group, at + Vector3.up * .56f,
                new Vector3(.62f, .14f, .15f), darkSteel);
        }

        private static void AddTrays(Transform group, Vector3 at, Material ivory)
        {
            const int trayCount = 5;
            const float spacing = .105f;
            for (int i = 0; i < trayCount; i++)
            {
                var offset = new Vector3(i * .025f, .06f + i * spacing, i * -.015f);
                Box("Stacked compartment cafeteria tray", group, at + offset,
                    new Vector3(2.18f, .082f, 1.23f), ivory);
            }
            Vector3 rim = at + new Vector3(.10f, .54f, -.06f);
            foreach (float x in new[] { -1.04f, 1.04f, -.24f, .36f })
                Box("Food tray molded rim", group, rim + Vector3.right * x,
                    new Vector3(.09f, .10f, 1.18f), ivory);
            foreach (float z in new[] { -.57f, .57f })
                Box("Food tray outer lip", group, rim + Vector3.forward * z,
                    new Vector3(2.16f, .10f, .09f), ivory);
        }

        private static void AddLadle(Transform group, Vector3 at, Material steel, Material darkSteel)
        {
            Box("Serving ladle wall clip", group, at + new Vector3(0f, .60f, -.23f),
                new Vector3(.30f, .23f, .40f), darkSteel);
            Box("Hanging serving ladle handle", group, at, new Vector3(.13f, 1.34f, .13f), steel);
            Shape(PrimitiveType.Sphere, "Serving ladle bowl", group, at + Vector3.down * .84f,
                new Vector3(.59f, .53f, .21f), steel, Quaternion.identity);
        }

        private static void AddServingLight(Transform group, Vector3 position)
        {
            var pool = new GameObject("Warm countertop light").transform;
            pool.SetParent(group, false);
            pool.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            var light = pool.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = 5.1f;
            light.spotAngle = 97f;
            light.intensity = 1.7f;
            light.color = new Color(1f, .83f, .63f);
        }

        private static Material LoadMaterial(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + name + ".mat")
            ?? throw new InvalidOperationException("Missing serving material: " + name);

        private static void Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material,
            Quaternion? rotation = null) =>
            Shape(PrimitiveType.Cube, name, parent, position, scale, material, rotation ?? Quaternion.identity);

        private static void Shape(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale,
            Material material, Quaternion rotation)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.SetPositionAndRotation(position, rotation);
            item.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            item.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(item, StaticEditorFlags.BatchingStatic);
        }
    }
}
