using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Map2KitchenDraft.Editor
{
    /// <summary>Rebuilds the whole three-dimensional restaurant route on an isolated project copy.</summary>
    public static class KitchenRebuildBuilder
    {
        private const string Root = "Assets/Scenes/Map2_Kitchen/Rebuild";
        private const string ScenePath = Root + "/Scenes/Map2KitchenDraft.unity";
        private const string MaterialRoot = Root + "/Materials/V3";
        private const string MeshRoot = Root + "/Art/Meshes/V3";
        private const string EntryClimb = "CH1 temporary dish-return climb";
        private const string SinkClimb = "CH6 temporary basin exit";
        private const string CookingClimb = "TEMP cooking upper exit approach";
        internal const string EntrySurfaceName = "CH1 continuous belt collision surface";
        internal const string SinkSurfaceName = "CH6 continuous belt collision surface";
        private const string CookingSurfaceName = "CH7 continuous exit collision surface";
        private static readonly Vector3 EntryStart = new Vector3(-118, .5f, -112);
        private static readonly Vector3 EntryEnd = new Vector3(-118, 8, -75);
        private static readonly Vector3 SinkStart = new Vector3(12, 1, 26);
        private static readonly Vector3 SinkEnd = new Vector3(12, 8, 65);
        private static readonly Vector3 CookingStart = new Vector3(-10, 8, -120);
        private static readonly Vector3 CookingEnd = new Vector3(-44, 16, -120);
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static Transform world;
        private static KitchenDraftController controller;
        private static Camera sceneCamera;
        private static int beltSerial;

        private static readonly string[] ChapterNames =
        {
            "퇴식구와 쥐", "접시 살균기", "접시 통로", "설거지 상부", "싱크대 내부", "그릇 정리", "조리대와 배출구"
        };
        private static readonly string[] ChapterObjectives =
        {
            "퇴식구의 긴 벨트를 따라 세척구로 이동", "고정 물줄기 사이의 통로를 통과", "접시 사이로 설거지 구역에 접근",
            "벨트가 끊긴 곳에서 싱크대 안으로 진입", "세척조 바닥을 지나 반대편으로 이동", "그릇을 건너 다음 벨트로 이동", "열쇠를 찾고 상단 출구로 이동"
        };
        private static readonly Vector3[] ChapterStarts =
        {
            new Vector3(-118f, 1.4f, -114f), new Vector3(-118f, 8.9f, -20f),
            new Vector3(-108f, 8.9f, 100f), new Vector3(-48f, 8.9f, 81f),
            new Vector3(-48f, 1.9f, 45f), new Vector3(12f, 8.9f, 76f),
            new Vector3(128f, 8.9f, -20f)
        };
        private static readonly Vector3[] ChapterEnds =
        {
            new Vector3(-118f, 8.9f, -24f), new Vector3(-118f, 8.9f, 100f),
            new Vector3(-48f, 8.9f, 82f), new Vector3(-48f, 1.9f, 50f),
            new Vector3(12f, 1.9f, 26f), new Vector3(128f, 8.9f, 12f),
            new Vector3(-62f, 1f, -120f)
        };
        private static readonly Vector3[] ChapterCenters =
        {
            new Vector3(-118, 5, -66), new Vector3(-118, 10, 40), new Vector3(-83, 10, 95),
            new Vector3(-48, 6, 74), new Vector3(-38, 3, 38), new Vector3(64, 10, 66),
            new Vector3(45, 10, -54)
        };
        private static readonly Vector3[] ChapterSizes =
        {
            new Vector3(21, 17, 99), new Vector3(21, 15, 130), new Vector3(83, 16, 28),
            new Vector3(30, 17, 38), new Vector3(102, 10, 52), new Vector3(146, 19, 135),
            new Vector3(198, 23, 140)
        };

        [Serializable] private sealed class BuildInfo
        {
            public string result;
            public string output;
            public string unityVersion;
            public ulong totalSize;
            public string[] messages;
        }

        [MenuItem("Tools/Map2 Kitchen Rebuild/1 Rebuild Scene")]
        public static void BuildAndValidate()
        {
            if (Application.unityVersion != "6000.3.28f1")
                throw new InvalidOperationException("This draft must be built with exact Unity 6000.3.28f1; current=" + Application.unityVersion);
            EnsureFolder(MaterialRoot);
            EnsureFolder(MeshRoot);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            controller = UnityEngine.Object.FindFirstObjectByType<KitchenDraftController>();
            sceneCamera = Camera.main;
            var players = UnityEngine.Object.FindObjectsByType<PlayerMover>(FindObjectsSortMode.None);
            if (controller == null || sceneCamera == null || sceneCamera.GetComponent<PlayerFollowCamera>() == null || players.Length != 3)
                throw new InvalidOperationException("Saved team camera, three players and review controller are required.");
            foreach (var player in players) player.transform.SetParent(null, true);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == "MAP 2 - LAB RESTAURANT KITCHEN" || root.name == "MAP 2 - RESTAURANT REBUILD")
                    UnityEngine.Object.DestroyImmediate(root);
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.gameObject.name == "Soft Kitchen Daylight") UnityEngine.Object.DestroyImmediate(light.gameObject);

            world = new GameObject("MAP 2 - RESTAURANT REBUILD").transform;
            beltSerial = 0;
            SetupMaterials();
            SetupAtmosphere();
            var chapterParents = new Transform[7];
            var starts = new Transform[7];
            var regions = new DraftChapterRegion[7];
            for (int i = 0; i < 7; i++)
            {
                chapterParents[i] = new GameObject("CH" + (i + 1) + " - " + ChapterNames[i]).transform;
                chapterParents[i].SetParent(world);
                starts[i] = new GameObject("Review Start " + (i + 1)).transform;
                starts[i].SetParent(chapterParents[i]);
                starts[i].position = ChapterStarts[i];
                var end = new GameObject("Review End " + (i + 1)).transform;
                end.SetParent(chapterParents[i]);
                end.position = ChapterEnds[i];
                var region = new GameObject("CH" + (i + 1) + " Region Trigger");
                region.transform.SetParent(chapterParents[i]);
                region.transform.position = ChapterCenters[i];
                var volume = region.AddComponent<BoxCollider>();
                volume.isTrigger = true;
                volume.size = ChapterSizes[i];
                regions[i] = region.AddComponent<DraftChapterRegion>();
                regions[i].index = i;
                regions[i].chapterName = ChapterNames[i];
                regions[i].objective = ChapterObjectives[i];
                regions[i].start = starts[i];
                regions[i].end = end;
                regions[i].volume = volume;
            }

            BuildRoom();
            BuildEntry(chapterParents[0]);
            BuildSterilizer(chapterParents[1]);
            BuildPlates(chapterParents[2]);
            BuildSink(chapterParents[3], chapterParents[4]);
            BuildSorting(chapterParents[5]);
            BuildCooking(chapterParents[6]);
            KitchenRebuildSurfaceQuality.Apply(world.gameObject);

            var visit = new GameObject("Map2 Sink Interior Visit Volume");
            visit.transform.SetParent(chapterParents[4]);
            visit.transform.position = new Vector3(-48, 2.4f, 39);
            var visitVolume = visit.AddComponent<BoxCollider>();
            visitVolume.isTrigger = true;
            visitVolume.size = new Vector3(20, 3, 24);
            controller.chapterStarts = starts;
            controller.chapterNames = ChapterNames;
            controller.chapters = regions;
            controller.chapterBoundaries = null;
            controller.sinkInteriorVolume = visitVolume;
            controller.previewCamera = sceneCamera;
            controller.interactionRadius = 2.5f;
            controller.key = GameObject.Find("Map2 Exit Key").transform;
            controller.doorVisual = GameObject.Find("Key-Only Upper Exit Door").transform;
            controller.doorBlocker = controller.doorVisual.GetComponent<Collider>();
            controller.doorOpenOffset = new Vector3(0, 4, 0);
            controller.finalDropPoint = GameObject.Find("Map3 Connection - Drop Point").transform;
            GameObject.Find("Exit Trigger - Key Required").GetComponent<DraftExitTrigger>().controller = controller;
            foreach (var player in players)
            {
                // Keep each shape at the scene root so ground contact only filters its own colliders.
                int index = Array.IndexOf(players, player);
                player.transform.position = ChapterStarts[0] + new Vector3((index - 1) * 1.25f, .2f, index * 1.2f);
                player.cameraRelativeInput = true;
            }
            var orbit = sceneCamera.GetComponent<PlayerFollowCamera>();
            sceneCamera.farClipPlane = 650f;
            sceneCamera.transform.position = ChapterStarts[0] + new Vector3(0, 4, -10);
            sceneCamera.transform.LookAt(ChapterStarts[0] + Vector3.up);
            orbit.enableMouseOrbit = true;
            Physics.SyncTransforms();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Map2 Rebuild] Rebuilt full restaurant route: " + ScenePath);
        }

        private static void SetupMaterials()
        {
            Materials.Clear();
            Material("Floor", new Color(.19f, .23f, .26f), .15f, .35f);
            Material("Wall", new Color(.32f, .39f, .43f), .32f, .53f);
            Material("Steel", new Color(.72f, .78f, .79f), .66f, .77f);
            Material("SteelDark", new Color(.26f, .34f, .36f), .57f, .61f);
            Material("Trim", new Color(.90f, .84f, .64f), .58f, .69f);
            Material("Belt", new Color(.43f, .29f, .22f), .05f, .28f);
            Material("BeltAlternate", new Color(.35f, .22f, .18f), .05f, .23f);
            Material("BeltBed", new Color(.22f, .15f, .13f), .20f, .38f);
            Material("Plate", new Color(.96f, .94f, .88f), .06f, .83f);
            Material("Water", new Color(.35f, .79f, .93f), .20f, .75f, .25f);
            Material("Lamp", new Color(.99f, .78f, .43f), .12f, .62f, .75f);
            Material("Warning", new Color(.90f, .37f, .23f), .18f, .41f, .25f);
            Material("Rat", new Color(.31f, .28f, .26f), .08f, .28f);
            Material("Roach", new Color(.23f, .13f, .10f), .06f, .32f);
            Material("Drain", new Color(.07f, .11f, .12f), .59f, .41f);
            Material("Key", new Color(.99f, .77f, .25f), .70f, .73f, .56f);
            var waterJet = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/WaterJet.mat");
            if (waterJet == null) throw new InvalidOperationException("Original water jet material missing");
            Materials["WaterJet"] = waterJet;
        }

        private static Material Material(string name, Color color, float metallic, float gloss, float emission = 0f)
        {
            string path = MaterialRoot + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Standard");
                if (shader == null) throw new InvalidOperationException("Standard shader missing");
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = color;
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Glossiness", gloss);
            if (emission > 0)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * emission);
            }
            EditorUtility.SetDirty(m);
            Materials[name] = m;
            return m;
        }

        private static void SetupAtmosphere()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.64f, .73f, .78f);
            RenderSettings.ambientEquatorColor = new Color(.48f, .52f, .54f);
            RenderSettings.ambientGroundColor = new Color(.32f, .3f, .32f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(.14f, .19f, .22f);
            RenderSettings.fogDensity = .0017f;
            var daylight = new GameObject("Restaurant skylight").AddComponent<Light>();
            daylight.transform.SetParent(world);
            daylight.type = LightType.Directional;
            daylight.intensity = 1.18f;
            daylight.color = new Color(1f, .94f, .82f);
            daylight.transform.rotation = Quaternion.Euler(51, -34, 0);
            daylight.shadows = LightShadows.Soft;
            daylight.shadowStrength = .65f;
            daylight.shadowBias = .035f;
            sceneCamera.backgroundColor = RenderSettings.fogColor;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }

        private static GameObject Shape(PrimitiveType primitive, string name, Transform parent, Vector3 center, Vector3 scale,
            string material, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(primitive);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = center;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Materials[material];
            if (!solid) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            else
            {
                int cameraBlocker = LayerMask.NameToLayer("CameraBlocker");
                if (cameraBlocker >= 0) go.layer = cameraBlocker;
            }
            return go;
        }

        private static GameObject Box(string name, Transform parent, Vector3 center, Vector3 scale, string material, bool solid = false)
        { return Shape(PrimitiveType.Cube, name, parent, center, scale, material, solid); }

        private static GameObject Cylinder(string name, Transform parent, Vector3 center, Vector3 scale, string material, bool solid = false)
        { return Shape(PrimitiveType.Cylinder, name, parent, center, scale, material, solid); }

        private static GameObject Sphere(string name, Transform parent, Vector3 center, Vector3 scale, string material)
        { return Shape(PrimitiveType.Sphere, name, parent, center, scale, material); }

        private static GameObject Beam(string name, Transform parent, Vector3 a, Vector3 b, float width, float height,
            string material, bool solid = false)
        {
            var beam = Box(name, parent, (a + b) * .5f, new Vector3(width, height, Vector3.Distance(a, b)), material, solid);
            beam.transform.rotation = Quaternion.LookRotation(b - a, Vector3.up);
            return beam;
        }

        private static void Tube(string name, Transform parent, Vector3 a, Vector3 b, float diameter, string material)
        {
            var go = Cylinder(name, parent, (a + b) * .5f, new Vector3(diameter, Vector3.Distance(a, b) * .5f, diameter), material);
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, b - a);
        }

        private static void Junction(Transform p, string name, Vector3 at, float top)
        {
            Box(name + " stainless mitred junction", p, new Vector3(at.x, top - .24f, at.z), new Vector3(7.5f, .45f, 7.5f), "BeltBed", true);
            Box(name + " brown tiled corner", p, new Vector3(at.x, top + .01f, at.z), new Vector3(6.9f, .08f, 6.9f), "Belt");
            for (int i = 0; i < 4; i++)
                Box(name + " rivet " + i, p, new Vector3(at.x + (i % 2 == 0 ? -3.55f : 3.55f), top + .025f,
                    at.z + (i < 2 ? -3.55f : 3.55f)), new Vector3(.13f, .06f, .13f), "Trim");
        }

        private static void Belt(Transform p, string description, Vector3 start, Vector3 end, float width = 7f)
        {
            int id = ++beltSerial;
            var root = new GameObject("Map2 V3 Belt Segment " + id.ToString("D2") + " - " + description);
            root.transform.SetParent(p);
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length < .45f) throw new InvalidOperationException("Belt too short: " + description);
            root.transform.position = (start + end) * .5f;
            root.transform.rotation = Quaternion.LookRotation(delta, Vector3.up);
            var baseMesh = Box("Restaurant brown conveyor walkable bed", root.transform,
                root.transform.position + Vector3.down * .2f, new Vector3(width, .38f, length + .20f), "BeltBed", true);
            baseMesh.transform.rotation = root.transform.rotation;
            var bedTop = Box("Belt continuous collision surface", root.transform, root.transform.position,
                new Vector3(width - .32f, .075f, length + .08f), "Belt", true);
            bedTop.transform.rotation = root.transform.rotation;
            // The slat surface is one mesh and the collision surface is one box per straight segment.
            AddSlatMesh(root.transform, id, length, width);
            foreach (float side in new[] { -1f, 1f })
            {
                var edge = Box("Brushed stainless side frame", root.transform, root.transform.position,
                    new Vector3(.28f, .22f, length + .18f), "Steel");
                edge.transform.localPosition = new Vector3(side * (width * .5f - .12f), .10f, 0);
                edge.transform.localRotation = Quaternion.identity;
                if (length > 18f)
                {
                    for (float d = -length * .5f + 8; d < length * .5f - 4; d += 15)
                    {
                        var bolt = Cylinder("Chrome side bolt", root.transform, root.transform.position,
                            new Vector3(.17f, .04f, .17f), "Trim");
                        bolt.transform.localPosition = new Vector3(side * (width * .5f - .12f), .24f, d);
                    }
                }
            }
            if (length >= 40)
                for (float d = -length * .5f + 18; d < length * .5f - 15; d += 28)
                {
                    var arrow = Box("Subtle belt direction arrow", root.transform, root.transform.position,
                        new Vector3(.12f, .015f, 1.15f), "Trim");
                    arrow.transform.localPosition = new Vector3(0, .09f, d);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var tip = Box("Belt direction chevron", root.transform, root.transform.position,
                            new Vector3(.09f, .014f, .7f), "Trim");
                        tip.transform.localPosition = new Vector3(side * .25f, .09f, d + .37f);
                        tip.transform.localRotation = Quaternion.Euler(0, side * 45, 0);
                    }
                }
        }

        private static void AddSlatMesh(Transform parent, int id, float length, float width)
        {
            string assetPath = MeshRoot + "/BeltSlats_" + id.ToString("D2") + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Map2 Brown Conveyor Slats " + id.ToString("D2") };
                AssetDatabase.CreateAsset(mesh, assetPath);
            }
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var uv = new List<Vector2>();
            int count = Mathf.CeilToInt(length / .64f);
            float step = length / count;
            for (int i = 0; i < count; i++)
            {
                float z = -length * .5f + (i + .5f) * step;
                float half = step * .44f;
                float x = width * .5f - .28f;
                int n = verts.Count;
                verts.Add(new Vector3(-x, .065f, z - half));
                verts.Add(new Vector3(x, .065f, z - half));
                verts.Add(new Vector3(-x, .065f, z + half));
                verts.Add(new Vector3(x, .065f, z + half));
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
                tris.Add(n); tris.Add(n + 2); tris.Add(n + 1);
                tris.Add(n + 1); tris.Add(n + 2); tris.Add(n + 3);
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            var slats = new GameObject("Photo reference: brown interlocking modular slats");
            slats.transform.SetParent(parent);
            slats.transform.localPosition = Vector3.zero;
            slats.transform.localRotation = Quaternion.identity;
            slats.AddComponent<MeshFilter>().sharedMesh = mesh;
            slats.AddComponent<MeshRenderer>().sharedMaterial = Materials["BeltAlternate"];
        }

        private static void StagedBeltAscent(Transform p, string name, Vector3 from, Vector3 to, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                float t0 = (float)i / steps;
                float t1 = (i + .84f) / steps;
                float t2 = (float)(i + 1) / steps;
                Vector3 a = Vector3.Lerp(from, to, t0);
                Vector3 b = Vector3.Lerp(from, to, t1);
                Vector3 c = Vector3.Lerp(from, to, t2);
                a.y = Mathf.Lerp(from.y, to.y, t0);
                b.y = Mathf.Lerp(from.y, to.y, t2);
                c.y = b.y;
                Belt(p, name + " rise " + (i + 1), a, b);
                Belt(p, name + " rest landing " + (i + 1), b, c);
            }
        }

        private static void StagedSteelAscent(Transform p, string name, Vector3 from, Vector3 to, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                float t0 = (float)i / steps;
                float t1 = (i + .84f) / steps;
                float t2 = (float)(i + 1) / steps;
                Vector3 a = Vector3.Lerp(from, to, t0);
                Vector3 b = Vector3.Lerp(from, to, t1);
                Vector3 c = Vector3.Lerp(from, to, t2);
                a.y = Mathf.Lerp(from.y, to.y, t0);
                b.y = Mathf.Lerp(from.y, to.y, t2);
                c.y = b.y;
                Beam(name + " rise " + (i + 1), p, a - Vector3.up * .17f, b - Vector3.up * .17f, 6.7f, .34f, "Steel", true);
                Beam(name + " rest landing " + (i + 1), p, b - Vector3.up * .17f, c - Vector3.up * .17f, 6.7f, .34f, "Steel", true);
            }
        }

        private static void BuildRoom()
        {
            // The real kitchen floor is twenty metres below the raised equipment. It is scenery,
            // not a short route around the sink or the dish-storage break.
            Box("Restaurant foundation far below route", world, new Vector3(5, -18.45f, -8),
                new Vector3(295, .9f, 284), "Floor", true);
            Box("Back north kitchen wall", world, new Vector3(5, -1f, 140), new Vector3(296, 35, .6f), "Wall", true);
            Box("South service wall", world, new Vector3(5, -1f, -145), new Vector3(296, 35, .6f), "Wall", true);
            Box("West restaurant wall", world, new Vector3(-142, -1f, -3), new Vector3(.6f, 35, 284), "Wall", true);
            Box("East restaurant wall", world, new Vector3(152, -1f, -3), new Vector3(.6f, 35, 284), "Wall", true);
            for (int i = 0; i < 17; i++)
            {
                float z = -132 + i * 16;
                Box("West wall enamel tile seam " + i, world, new Vector3(-141.63f, 1.5f, z),
                    new Vector3(.03f, 14f, .055f), "SteelDark");
                Box("East wall enamel tile seam " + i, world, new Vector3(151.63f, 1.5f, z),
                    new Vector3(.03f, 14f, .055f), "SteelDark");
            }
            for (int row = 0; row < 3; row++)
            {
                float y = -6.5f + row * 6.5f;
                Box("North tiled wall horizontal grout " + row, world, new Vector3(5, y, 139.64f),
                    new Vector3(293, .08f, .03f), "SteelDark");
                Box("West tiled wall horizontal grout " + row, world, new Vector3(-141.64f, y, -3),
                    new Vector3(.03f, .08f, 280), "SteelDark");
            }
            for (int i = 0; i < 7; i++)
            {
                float z = -104 + i * 37;
                if (z < -75f) continue; // Entry climb is below the raised belt: do not draw a cabinet through it.
                Box("West under-belt structural cabinet " + i, world, new Vector3(-118, -5.2f, z),
                    new Vector3(9.2f, 24.6f, 9.6f), "SteelDark");
                Box("West cabinet folded door " + i, world, new Vector3(-112.95f, -4.2f, z),
                    new Vector3(.1f, 11f, 8.8f), "Steel");
                Box("West cabinet handle " + i, world, new Vector3(-112.87f, -3.6f, z),
                    new Vector3(.2f, .25f, 2f), "Trim");
            }
            for (int i = 0; i < 7; i++)
            {
                float z = 87 - i * 36;
                Box("East under-belt drive housing " + i, world, new Vector3(128, -5.2f, z),
                    new Vector3(9.4f, 24.6f, 9.4f), "SteelDark");
                Cylinder("East conveyor drive wheel " + i, world, new Vector3(133.3f, 3.2f, z),
                    new Vector3(1.7f, .28f, 1.7f), "Trim");
            }
            for (int i = 0; i < 8; i++)
            {
                float x = -28 + i * 20;
                Box("Foodline under-counter support " + i, world, new Vector3(x, -5.2f, -120),
                    new Vector3(10f, 24.6f, 9f), "SteelDark");
                Box("Service cabinet vent " + i, world, new Vector3(x, 2.5f, -114.9f),
                    new Vector3(4.6f, 1f, .12f), "Drain");
                for (int j = 0; j < 4; j++)
                    Box("Vent louver " + i + "/" + j, world,
                        new Vector3(x, 2.17f + j * .22f, -114.82f), new Vector3(4.1f, .035f, .05f), "Steel");
            }
            float[] islandX = { -76f, -21f, 51f, 85f };
            float[] islandZ = { -72f, -22f, 40f };
            for (int row = 0; row < islandZ.Length; row++)
            for (int col = 0; col < islandX.Length; col++)
            {
                // These sit below the raised conveyor and cannot connect its route.
                // They give the large room the density and scale of a working kitchen.
                ServiceIsland(world, islandX[col], islandZ[row], row, col);
            }
            for (int i = 0; i < 8; i++)
            {
                float z = -105 + i * 31;
                if (z > 75) break;
                HangingLamp(world, new Vector3(-118, 20, z));
                HangingLamp(world, new Vector3(128, 20, z));
            }
            for (int i = 0; i < 4; i++)
                HangingLamp(world, new Vector3(-70 + i * 58, 20, 100));
            Sign(world, "DISH RETURN", new Vector3(-135, 13.5f, -108), Quaternion.Euler(0, 90, 0));
            Sign(world, "WASH / STERILIZE", new Vector3(-135, 14, -24), Quaternion.Euler(0, 90, 0));
            Sign(world, "SORT / DRY", new Vector3(46, 14, 124), Quaternion.identity);
            Sign(world, "HOT LINE", new Vector3(145, 13.5f, -58), Quaternion.Euler(0, 90, 0));
        }

        private static void ServiceIsland(Transform p, float x, float z, int row, int col)
        {
            string label = "Central service island " + row + "/" + col;
            Box(label + " cabinet body", p, new Vector3(x, -7.15f, z),
                new Vector3(24f, 21.7f, 16f), "SteelDark", true);
            Box(label + " stainless prep counter", p, new Vector3(x, 3.95f, z),
                new Vector3(25f, .5f, 17f), "Steel", true);
            Box(label + " rear splashguard", p, new Vector3(x, 5.5f, z + 8.2f),
                new Vector3(25f, 2.6f, .36f), "Steel", true);
            for (int panel = 0; panel < 4; panel++)
            {
                float px = x - 8.7f + panel * 5.8f;
                Box(label + " front folded cabinet door " + panel, p,
                    new Vector3(px, -2.7f, z - 8.06f), new Vector3(5.2f, 11.0f, .16f), "Steel");
                Box(label + " front recessed handle " + panel, p,
                    new Vector3(px, -2.5f, z - 8.19f), new Vector3(1.8f, .18f, .07f), "Trim");
            }
            for (int tray = 0; tray < 4; tray++)
            {
                float tx = x - 8.3f + tray * 5.4f;
                Tray(p, new Vector3(tx, 4.25f, z - 2.8f), 2.1f);
            }
            for (int side = -1; side <= 1; side += 2)
                Tube(label + " hood support pipe " + side, p,
                    new Vector3(x + side * 10.5f, 4.25f, z + 5.5f),
                    new Vector3(x + side * 10.5f, 13.4f, z + 5.5f), .25f, "Steel");
            Box(label + " extraction hood", p, new Vector3(x, 13.8f, z + 1.2f),
                new Vector3(25.2f, 1.2f, 17.5f), "SteelDark");
            Box(label + " task light", p, new Vector3(x, 13.15f, z + 1.2f),
                new Vector3(19f, .11f, 9.8f), "Lamp");
            Box(label + " raised food-safe canopy", p, new Vector3(x, 17.3f, z + 1.2f),
                new Vector3(9.1f, 5.9f, 8.5f), "Steel");
        }

        private static void HangingLamp(Transform p, Vector3 center)
        {
            Tube("Industrial lamp suspension", p, center, center + Vector3.down * 3.6f, .09f, "SteelDark");
            Box("Pendant light body", p, center + Vector3.down * 3.8f, new Vector3(4.8f, .55f, 1.6f), "Steel");
            Box("Warm fluorescent diffuser", p, center + Vector3.down * 4.1f,
                new Vector3(4.3f, .12f, 1.3f), "Lamp");
            // The broad ambient and one directional source illuminate most geometry;
            // a handful of these local lights gives scale without flooding the scene.
            if (Mathf.Abs(center.z) < 35 || center.z > 90)
            {
                var point = new GameObject("Warm industrial task light");
                point.transform.SetParent(p);
                point.transform.position = center + Vector3.down * 4.35f;
                var light = point.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 25f;
                light.intensity = 1.1f;
                light.color = new Color(1f, .83f, .63f);
            }
        }

        private static void Sign(Transform p, string text, Vector3 at, Quaternion rotation)
        {
            var panel = Box("Wayfinding: " + text, p, at, new Vector3(13.5f, 2.2f, .18f), "SteelDark");
            panel.transform.rotation = rotation;
            Box("Wayfinding amber upper trim", p, at + rotation * new Vector3(0, 1.16f, -.11f),
                new Vector3(13.7f, .09f, .12f), "Trim").transform.rotation = rotation;
            // One caption faces the route; a second backface previously drew through
            // the panel in Unity's transparent text queue and made the sign unreadable.
            bool frontPositive = at.x < -130f || at.z < -130f;
            float facing = frontPositive ? 1f : -1f;
            var label = new GameObject("Route label " + text);
            label.transform.SetParent(p);
            label.transform.position = at + rotation * new Vector3(0, 0, facing * .14f);
            label.transform.rotation = rotation * (frontPositive ? Quaternion.Euler(0, 180, 0) : Quaternion.identity);
            var letters = label.AddComponent<TextMesh>();
            letters.text = text;
            letters.fontSize = 64;
            letters.characterSize = .16f;
            letters.anchor = TextAnchor.MiddleCenter;
            letters.alignment = TextAlignment.Center;
            letters.color = new Color(1f, .9f, .68f);
        }

        private static void BuildEntry(Transform p)
        {
            Box("Cafeteria return landing", p, new Vector3(-118, .25f, -112),
                new Vector3(13, .5f, 11), "Steel", true);
            StagedBeltAscent(p, EntryClimb, EntryStart, EntryEnd, 8);
            ReplaceEntryClimbColliders(p);
            Belt(p, "CH1 long white-line return", new Vector3(-118, 8, -75), new Vector3(-118, 8, -24));
            Box("Dish return hatch frame left", p, new Vector3(-123.4f, 4.7f, -112),
                new Vector3(.55f, 8.7f, 9.5f), "SteelDark", true);
            Box("Dish return hatch frame right", p, new Vector3(-112.6f, 4.7f, -112),
                new Vector3(.55f, 8.7f, 9.5f), "SteelDark", true);
            Box("Dish return lintel", p, new Vector3(-118, 9.2f, -112),
                new Vector3(11.4f, .55f, .7f), "Steel", true);
            for (int i = 0; i < 4; i++)
            {
                float z = -68 + i * 12;
                Tray(p, new Vector3(-116.8f + (i % 2) * 1.5f, 8.1f, z), 2.4f);
                Tube("Wall utility conduit", p, new Vector3(-137, 11, z - 3),
                    new Vector3(-137, 11, z + 4), .20f, "Steel");
            }
            Rat(p, new Vector3(-109.5f, 1.0f, -105), 0, null);
            Box("Distant rat entry grille", p, new Vector3(-109, 2, -107), new Vector3(.2f, 2.5f, 4), "Drain");
        }

        internal static void ReplaceEntryClimbColliders(Transform chapter) =>
            ReplaceClimbColliders(chapter, EntryClimb, EntrySurfaceName,
                "EntryBeltCollision", EntryStart, EntryEnd, .0375f);

        internal static void ReplaceSinkClimbColliders(Transform chapter) =>
            ReplaceClimbColliders(chapter, SinkClimb, SinkSurfaceName,
                "SinkBeltCollision", SinkStart, SinkEnd, .0375f);

        internal static void ReplaceCookingClimbColliders(Transform chapter) =>
            ReplaceClimbColliders(chapter, CookingClimb, CookingSurfaceName,
                "CookingExitCollision", CookingStart, CookingEnd, 0f);

        private static void ReplaceClimbColliders(Transform chapter, string segmentName, string surfaceName,
            string meshName, Vector3 start, Vector3 end, float topOffset)
        {
            const float halfWidth = 3.34f;
            const float overlap = .45f;
            const float landingOverlap = 1.1f;
            string meshPath = MeshRoot + "/" + meshName + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = surfaceName };
                AssetDatabase.CreateAsset(mesh, meshPath);
            }

            // Keep a single incline under the segmented belt art. The slope
            // starts below the approach and meets the upper belt at its surface.
            Vector3 climb = end - start;
            Vector3 horizontal = new Vector3(climb.x, 0f, climb.z);
            Vector3 forward = horizontal.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward) * halfWidth;
            float slope = (climb.y + topOffset) / horizontal.magnitude;
            var profile = new[]
            {
                -forward * overlap - Vector3.up * (overlap * slope),
                climb + Vector3.up * (topOffset + .01f) - forward * .65f,
                climb + Vector3.up * (topOffset + .01f) + forward * landingOverlap,
                climb + Vector3.up * (topOffset - .05f) + forward * (landingOverlap + overlap)
            };

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (Vector3 point in profile)
            {
                vertices.Add(point - right);
                vertices.Add(point + right);
            }
            for (int i = 0; i < vertices.Count - 2; i += 2)
                triangles.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            foreach (Transform segment in chapter)
            {
                if (!segment.name.Contains(segmentName, StringComparison.Ordinal)) continue;
                foreach (var box in segment.GetComponentsInChildren<BoxCollider>())
                    UnityEngine.Object.DestroyImmediate(box);
            }
            Transform surface = chapter.Find(surfaceName);
            if (surface == null)
            {
                var go = new GameObject(surfaceName);
                surface = go.transform;
                surface.SetParent(chapter, false);
                go.AddComponent<MeshCollider>();
            }
            surface.position = start;
            int layer = LayerMask.NameToLayer("CameraBlocker");
            if (layer >= 0) surface.gameObject.layer = layer;
            surface.GetComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static void BuildSterilizer(Transform p)
        {
            Belt(p, "CH2 long white-line sterilizer", new Vector3(-118, 8, -24), new Vector3(-118, 8, 100));
            for (int bay = 0; bay < 4; bay++)
            {
                float z = -13 + bay * 30;
                Box("Sterilizer stainless portal left " + bay, p,
                    new Vector3(-122.6f, 12.3f, z), new Vector3(.38f, 8.4f, .6f), "Steel", true);
                Box("Sterilizer stainless portal right " + bay, p,
                    new Vector3(-113.4f, 12.3f, z), new Vector3(.38f, 8.4f, .6f), "Steel", true);
                Box("Sterilizer overhead arm " + bay, p,
                    new Vector3(-118, 16.55f, z), new Vector3(9.6f, .42f, .7f), "Steel", true);
                Box("Sterilizer blue inspection panel " + bay, p,
                    new Vector3(-122.32f, 13f, z + 4), new Vector3(.12f, 1.3f, 2.2f), "Water");
            }
            for (int i = 0; i < 8; i++)
            {
                float z = -10 + i * 13;
                float x = -118f + (i % 2 == 0 ? -1.45f : 1.45f);
                Cylinder("Fixed sterilizer nozzle " + i, p, new Vector3(x, 15.7f, z),
                    new Vector3(.58f, .22f, .58f), "SteelDark");
                var source = new GameObject("Fixed water jet " + i);
                source.transform.SetParent(p);
                source.transform.position = new Vector3(x, 15.4f, z);
                var jet = source.AddComponent<DraftWaterJet>();
                jet.localEnd = new Vector3(0, -7f, 0);
                jet.width = .5f;
                jet.waterMaterial = Materials["WaterJet"];
                jet.enabled = false;
                jet.enabled = true;
            }
            for (int i = 0; i < 9; i++)
                Tray(p, new Vector3(-118 + (i % 2 == 0 ? 2 : -2), 8.1f, -3 + i * 10), 1.55f);
            Junction(p, "Sterilizer to plate route", new Vector3(-118, 8, 100), 8);
        }

        private static void BuildPlates(Transform p)
        {
            Belt(p, "CH3 top white-line plate route", new Vector3(-118, 8, 100), new Vector3(-48, 8, 100));
            Junction(p, "Plate route to sink descent", new Vector3(-48, 8, 100), 8);
            Belt(p, "CH3 approach to sink white-line", new Vector3(-48, 8, 100), new Vector3(-48, 8, 82));
            for (int i = 0; i < 14; i++)
            {
                float x = -111 + i * 4.6f;
                float z = 99f + (i % 3 - 1) * 1.35f;
                Dish(p, new Vector3(x, 8.08f, z), 1.15f + (i % 3) * .16f, .22f + (i % 4) * .12f, true);
            }
            Box("Plate washer transfer tunnel", p, new Vector3(-81, 14.7f, 106.8f),
                new Vector3(41, 7.5f, 2.3f), "SteelDark", true);
            for (int i = 0; i < 6; i++)
            {
                float x = -101 + i * 8;
                Box("Transfer tunnel louver " + i, p, new Vector3(x, 15.5f, 105.5f),
                    new Vector3(4, .45f, .15f), "Steel");
            }
            Rat(p, new Vector3(-70, 9f, 110.5f), 2, null);
        }

        private static void BuildSink(Transform upper, Transform lower)
        {
            // No upper floor spans the vessel. Its interior is a true seven-metre-deep open well.
            Box("Map2 Sink Basin Floor", lower, new Vector3(-48, .8f, 52),
                new Vector3(24, .4f, 60), "SteelDark", true);
            Box("Map2 Sink Wall West", upper, new Vector3(-60.1f, 7, 52),
                new Vector3(.45f, 12f, 61), "Steel", true);
            Box("Map2 Sink Wall East - upper", upper, new Vector3(-35.9f, 7, 58),
                new Vector3(.45f, 12f, 49), "Steel", true);
            Box("Map2 Sink Wall South", lower, new Vector3(-48, 7, 21.8f),
                new Vector3(25, 12f, .45f), "Steel", true);
            Box("Map2 Sink North mouth cabinet left", upper, new Vector3(-56.6f, 7, 84),
                new Vector3(7.2f, 12f, 3.6f), "Steel", true);
            Box("Map2 Sink North mouth cabinet right", upper, new Vector3(-39.4f, 7, 84),
                new Vector3(7.2f, 12f, 3.6f), "Steel", true);
            Box("Map2 Sink back high backsplash", upper, new Vector3(-48, 12.8f, 86.4f),
                new Vector3(24.8f, 2.2f, .4f), "SteelDark", true);
            // The adjoining cabinets have high solid sides and no walkable top near Y8.
            Box("Sink outer cabinet west", upper, new Vector3(-71, 5.5f, 51),
                new Vector3(20, 11, 67), "SteelDark", true);
            Box("Sink outer cabinet east north", upper, new Vector3(-23, 5.5f, 58),
                new Vector3(23, 11, 50), "SteelDark", true);
            Box("Sink cabinet vertical steel lip west", upper, new Vector3(-59.85f, 12.8f, 52),
                new Vector3(.3f, .6f, 61), "Trim");
            Box("Sink cabinet vertical steel lip east", upper, new Vector3(-36.15f, 12.8f, 58),
                new Vector3(.3f, .6f, 49), "Trim");
            Box("Sink exposed north rim left", upper, new Vector3(-57.4f, 8.3f, 81.9f),
                new Vector3(5.6f, .2f, .8f), "Steel");
            Box("Sink exposed north rim right", upper, new Vector3(-38.6f, 8.3f, 81.9f),
                new Vector3(5.6f, .2f, .8f), "Steel");

            // The white PDF line is interrupted here; the steel descent is the mandatory
            // passage through the unfilled opening, with guards all the way to the well floor.
            Vector3 inlet = new Vector3(-48, 8, 82);
            Vector3 basin = new Vector3(-48, 1, 50);
            Beam("Map2 Sink North Entry Ramp", upper,
                inlet - Vector3.up * .19f, basin - Vector3.up * .19f, 6.7f, .38f, "Steel", true);
            foreach (float side in new[] { -1f, 1f })
                Beam("Sink descent continuous full-height side guard", upper,
                    inlet + new Vector3(side * 3.67f, 2.45f, 0),
                    basin + new Vector3(side * 3.67f, 2.45f, 0), .34f, 4.9f, "SteelDark", true);
            Box("Sink descent landing and overshoot stop", lower,
                new Vector3(-48, 1.1f, 48.2f), new Vector3(7.5f, .22f, 3.3f), "Steel", true);
            Box("Sink descent speed control edge bump", lower,
                new Vector3(-50.7f, 1.23f, 45.9f), new Vector3(1.6f, .40f, .42f), "SteelDark", true);

            Belt(lower, "CH5 bottom sink white-line", new Vector3(-48, 1, 50), new Vector3(-48, 1, 26));
            Junction(lower, "Sink bottom corner", new Vector3(-48, 1, 26), 1);
            Belt(lower, "CH5 bottom east white-line", new Vector3(-48, 1, 26), new Vector3(12, 1, 26));
            var sinkSouthExit = new GameObject("Map2 Sink South Exit");
            sinkSouthExit.transform.SetParent(lower);
            sinkSouthExit.transform.position = new Vector3(-34, 1.8f, 26);
            // Through the basin's only side opening. The wall above x=-36 ends before z32.
            Box("Sink exit narrowing south guide", lower, new Vector3(-34, 4.2f, 21.8f),
                new Vector3(16, 6.4f, .45f), "Steel", true);
            Box("Sink exit narrowing north guide", lower, new Vector3(-27, 4.2f, 30.2f),
                new Vector3(29, 6.4f, .45f), "Steel", true);

            for (int i = 0; i < 5; i++)
            {
                float z = 39 + i * 8.2f;
                Tube("Sink high arched faucet riser " + i, upper,
                    new Vector3(-59.3f, 8.4f, z), new Vector3(-59.3f, 15.1f, z), .29f, "Steel");
                Tube("Sink high arched faucet reach " + i, upper,
                    new Vector3(-59.3f, 15.1f, z), new Vector3(-50f, 15.1f, z), .29f, "Steel");
                Tube("Sink faucet downturned nozzle " + i, upper,
                    new Vector3(-50f, 15.1f, z), new Vector3(-50f, 14.2f, z), .38f, "SteelDark");
                Cylinder("Faucet temperature dial " + i, upper,
                    new Vector3(-59.2f, 9.15f, z + 1), new Vector3(.42f, .16f, .42f), "Warning");
            }
            for (int i = 0; i < 3; i++)
            {
                float z = 34 + i * 16;
                Cylinder("Sink floor drain perforated outer ring " + i, lower,
                    new Vector3(-54.7f, 1.025f, z), new Vector3(1.7f, .035f, 1.7f), "Steel");
                Cylinder("Sink floor drain dark recessed center " + i, lower,
                    new Vector3(-54.7f, 1.065f, z), new Vector3(1.2f, .018f, 1.2f), "Drain");
                for (int j = 0; j < 5; j++)
                    Box("Drain stainless grille " + i + "/" + j, lower,
                        new Vector3(-54.7f - .72f + j * .36f, 1.10f, z),
                        new Vector3(.07f, .025f, 1.6f), "Steel");
            }
            for (int i = 0; i < 3; i++)
                Dish(lower, new Vector3(-56.3f, 1.02f, 45 + i * 6), 1.2f, .08f, false);
            var roachBounds = new GameObject("Cockroach bounds - sink only");
            roachBounds.transform.SetParent(lower);
            roachBounds.transform.position = new Vector3(-48, 3.1f, 52);
            var activityBounds = roachBounds.AddComponent<BoxCollider>();
            activityBounds.isTrigger = true;
            activityBounds.size = new Vector3(21, 4.1f, 56);
            var roach = Sphere("Cockroach visual marker - basin only", lower,
                new Vector3(-55, 1.47f, 35), new Vector3(1.35f, .58f, 1.7f), "Roach");
            var marker = roach.AddComponent<DraftCreatureMarker>();
            marker.kind = CreatureKind.Cockroach;
            marker.chapterIndex = 4;
            marker.activityBounds = activityBounds;
            marker.visualOnly = true;
            for (int i = 0; i < 6; i++)
                Tube("Cockroach leg " + i, roach.transform,
                    new Vector3(-55, 1.43f, 35) + new Vector3(i < 3 ? -.4f : .4f, 0, (i % 3 - 1) * .3f),
                    new Vector3(-55, 1.1f, 35) + new Vector3(i < 3 ? -1.2f : 1.2f, 0, (i % 3 - 1) * .65f),
                    .05f, "Roach");
            // Laser emitter is a placement marker, not an active tracking or damage system.
            Box("Pest-laser fixture location only", upper, new Vector3(-67, 14.6f, 55),
                new Vector3(2.3f, 1.2f, 2.3f), "SteelDark");
            Sphere("Pest fixture lens - color undecided", upper, new Vector3(-66.0f, 14.55f, 55),
                new Vector3(.5f, .5f, .5f), "Water");
            Sign(upper, "SINK / WASH", new Vector3(-48, 18, 119), Quaternion.identity);
        }

        private static void BuildSorting(Transform p)
        {
            StagedBeltAscent(p, SinkClimb, SinkStart, SinkEnd, 8);
            ReplaceSinkClimbColliders(p);
            Belt(p, "CH6 center north white-line", new Vector3(12, 8, 65), new Vector3(12, 8, 100));
            Junction(p, "Center turning toward dish sorting", new Vector3(12, 8, 100), 8);
            Belt(p, "CH6 dish sorting approach white-line", new Vector3(12, 8, 100),
                new Vector3(42, 8, 100));
            // The second intentional belt break. It is bridged by shallow dish tops,
            // with inter-plate intervals smaller than the team's cube jump range.
            Box("Dish sorting gap deep catch-free void marker", p, new Vector3(56, -14.4f, 100),
                new Vector3(30, .2f, 10), "Drain");
            for (int i = 0; i < 11; i++)
            {
                float x = 43.5f + i * 2.45f;
                float top = 8.16f + ((i % 3 == 1) ? .26f : (i % 3 == 2 ? .38f : 0));
                var basePlatform = Box("Sorting gap dish landing " + i, p,
                    new Vector3(x, top - .10f, 100), new Vector3(2.78f, .2f, 5.5f), "Plate", true);
                Dish(p, new Vector3(x, top, 100), 1.3f, .07f, false);
                Box("Dish underside rib " + i, p, new Vector3(x, top - .34f, 100),
                    new Vector3(2.1f, .3f, 4.2f), "SteelDark");
            }
            Belt(p, "CH6 sorting outfeed white-line", new Vector3(70, 8, 100), new Vector3(128, 8, 100));
            Junction(p, "Outfeed turns south", new Vector3(128, 8, 100), 8);
            Belt(p, "CH6 long east return white-line", new Vector3(128, 8, 100), new Vector3(128, 8, 12));
            for (int i = 0; i < 7; i++)
            {
                float x = 16 + i * 16;
                Box("Dish storage rack long leg " + i, p, new Vector3(x, 9.7f, 111),
                    new Vector3(11.6f, 4.5f, 2.3f), "SteelDark");
                for (int j = 0; j < 3; j++)
                    Box("Dish rack shelf " + i + "/" + j, p,
                        new Vector3(x, 8.5f + j * 1.35f, 109.8f), new Vector3(11.6f, .18f, 2.1f), "Steel");
                if (x < 42 || x > 70)
                    for (int j = 0; j < 4; j++)
                        Dish(p, new Vector3(x - 3.4f + j * 2.1f, 9.65f, 109.7f), .75f, .10f, false);
            }
            for (int i = 0; i < 6; i++)
            {
                float z = 84 - i * 15;
                Tray(p, new Vector3(127.3f + (i % 2 == 0 ? 1.1f : -1.1f), 8.08f, z), 1.5f);
            }
            Rat(p, new Vector3(7.4f, 8.7f, 106), 5, null);
            Box("Rat pursuit ends at dish sorting boundary", p, new Vector3(42, 13.5f, 108),
                new Vector3(.32f, 1.0f, 3.5f), "Warning");
        }

        private static void BuildCooking(Transform p)
        {
            Belt(p, "CH7 east white-line to cooking", new Vector3(128, 8, 12), new Vector3(128, 8, -120));
            Junction(p, "Cooking return U-turn", new Vector3(128, 8, -120), 8);
            Belt(p, "CH7 bottom white-line cooking route", new Vector3(128, 8, -120),
                new Vector3(-48, 8, -120));
            for (int i = 0; i < 6; i++)
            {
                float z = -24 - i * 18;
                Box("Cooking range stainless counter " + i, p,
                    new Vector3(138.1f, 6.3f, z), new Vector3(11.5f, 3.2f, 11.4f), "Steel", true);
                Box("Cooking range black induction top " + i, p,
                    new Vector3(137.8f, 7.98f, z), new Vector3(9.7f, .13f, 8.2f), "Drain");
                for (int ring = 0; ring < 2; ring++)
                {
                    float rz = z + (ring == 0 ? -2.1f : 2.1f);
                    Cylinder("Cooktop brushed hot plate " + i + "/" + ring, p,
                        new Vector3(137.7f, 8.08f, rz), new Vector3(1.7f, .04f, 1.7f), "SteelDark");
                    Cylinder("Cooking pan " + i + "/" + ring, p,
                        new Vector3(137.7f, 8.2f, rz), new Vector3(1.45f, .14f, 1.45f), "Drain");
                    Beam("Pan handle " + i + "/" + ring, p,
                        new Vector3(139.1f, 8.32f, rz), new Vector3(141f, 8.32f, rz), .24f, .13f, "SteelDark");
                }
                Box("Range exhaust hood canopy " + i, p,
                    new Vector3(137.8f, 16.8f, z), new Vector3(11.7f, 1.25f, 10.3f), "SteelDark");
                Box("Warm hood light " + i, p,
                    new Vector3(137.6f, 16.12f, z), new Vector3(7.3f, .10f, 6.8f), "Lamp");
                Tube("Hood exhaust riser " + i, p,
                    new Vector3(138, 17.42f, z), new Vector3(138, 23.5f, z), 1.8f, "Steel");
            }
            for (int i = 0; i < 8; i++)
            {
                float x = 108 - i * 16;
                Box("Foodline boxed prep counter " + i, p,
                    new Vector3(x, 5.6f, -130.2f), new Vector3(13.4f, 4.3f, 8.3f), "Steel", true);
                Box("Foodline backsplash " + i, p,
                    new Vector3(x, 9.1f, -134.2f), new Vector3(13.4f, 3.1f, .42f), "SteelDark");
                for (int j = 0; j < 3; j++)
                    Cylinder("Prepped ingredient bowl " + i + "/" + j, p,
                        new Vector3(x - 3.5f + j * 3.4f, 7.85f, -129.4f),
                        new Vector3(.9f, .35f, .9f), j % 2 == 0 ? "Plate" : "SteelDark");
            }

            var key = Cylinder("Map2 Exit Key", p, new Vector3(-8, 8.67f, -120),
                new Vector3(.74f, .12f, .74f), "Key");
            Box("Key brass stem", key.transform, new Vector3(-8, 8.7f, -119.25f),
                new Vector3(.15f, .16f, 1.25f), "Key");
            Box("Key brass tooth A", key.transform, new Vector3(-7.76f, 8.7f, -118.8f),
                new Vector3(.55f, .16f, .16f), "Key");
            Box("Key brass tooth B", key.transform, new Vector3(-7.76f, 8.7f, -119.08f),
                new Vector3(.40f, .16f, .16f), "Key");
            Box("Key pedestal foot", p, new Vector3(-8, 8.25f, -123.8f),
                new Vector3(3.2f, .42f, 2.0f), "SteelDark");
            Box("Key pedestal luminous indicator", p, new Vector3(-8, 8.49f, -123.8f),
                new Vector3(2.5f, .04f, 1.2f), "Key");

            StagedSteelAscent(p, CookingClimb, CookingStart, CookingEnd, 8);
            ReplaceCookingClimbColliders(p);
            Box("Map2 Upper Exit Approach Landing", p, new Vector3(-45.6f, 15.79f, -120),
                new Vector3(7.2f, .42f, 7f), "Steel", true);
            foreach (float side in new[] { -1f, 1f })
                Beam("Upper exit ascent guard", p,
                    new Vector3(-10, 10.6f, -120 + side * 3.65f),
                    new Vector3(-44, 18.6f, -120 + side * 3.65f), .32f, 5.2f, "SteelDark", true);
            // Complete key-only aperture at Y16. The normal food belt stays at Y8 below it.
            Box("Exit full lower sealed wall", p, new Vector3(-49.6f, 7.2f, -120),
                new Vector3(.55f, 14.4f, 30f), "SteelDark", true);
            Box("Exit upper sealed wall", p, new Vector3(-49.6f, 19.3f, -120),
                new Vector3(.55f, 3.5f, 30f), "SteelDark", true);
            Box("Exit wall south of aperture", p, new Vector3(-49.6f, 16f, -131.1f),
                new Vector3(.55f, 3.2f, 17.8f), "SteelDark", true);
            Box("Exit wall north of aperture", p, new Vector3(-49.6f, 16f, -113.9f),
                new Vector3(.55f, 3.2f, 7.8f), "SteelDark", true);
            Box("Key-Only Upper Exit Door", p, new Vector3(-49.6f, 16f, -120),
                new Vector3(.6f, 3.2f, 4.4f), "Warning", true);
            for (int i = 0; i < 4; i++)
                Box("Key door amber frame stripe " + i, p,
                    new Vector3(-49.2f, i < 2 ? 14.35f : 17.65f, -120 + (i % 2 == 0 ? -2.4f : 2.4f)),
                    new Vector3(.12f, .16f, 4.9f), "Trim");
            Box("Food dispatch upper exit ledge", p, new Vector3(-53.3f, 15.80f, -120),
                new Vector3(8.1f, .4f, 8.2f), "Steel", true);
            Box("Food dispatch lower landing", p, new Vector3(-62, .22f, -120),
                new Vector3(8f, .44f, 8f), "Steel", true);
            Box("Dispatch shaft west crash barrier", p, new Vector3(-66.4f, 8.0f, -120),
                new Vector3(.45f, 16.0f, 9f), "SteelDark", true);
            Box("Dispatch shaft north side", p, new Vector3(-62, 7.0f, -115.6f),
                new Vector3(8.5f, 14.0f, .42f), "SteelDark", true);
            Box("Dispatch shaft south side", p, new Vector3(-62, 7.0f, -124.4f),
                new Vector3(8.5f, 14.0f, .42f), "SteelDark", true);
            Box("Dispatch landing safety stripe", p, new Vector3(-62, .47f, -120),
                new Vector3(6.7f, .04f, .32f), "Warning");
            var triggerObject = new GameObject("Exit Trigger - Key Required");
            triggerObject.transform.SetParent(p);
            triggerObject.transform.position = new Vector3(-62, 1.2f, -120);
            var trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(7.2f, 2.1f, 7.2f);
            triggerObject.AddComponent<DraftExitTrigger>();
            var drop = new GameObject("Map3 Connection - Drop Point");
            drop.transform.SetParent(p);
            drop.transform.position = new Vector3(-62, .85f, -120);
            Sign(p, "KEY / DISPATCH", new Vector3(-8, 14, -138), Quaternion.identity);
        }

        private static void Tray(Transform p, Vector3 at, float width)
        {
            Box("Restaurant stainless dish tray", p, at + Vector3.up * .045f,
                new Vector3(width, .10f, width * .72f), "SteelDark");
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Folded steel tray lip", p,
                    at + new Vector3(side * (width * .5f - .09f), .16f, 0),
                    new Vector3(.16f, .21f, width * .72f), "Steel");
            }
            Dish(p, at + new Vector3(0, .14f, 0), width * .23f, .06f, false);
        }

        private static void Dish(Transform p, Vector3 at, float radius, float thickness, bool walkable)
        {
            Cylinder("Stacked white ceramic dish", p,
                at + Vector3.up * (thickness * .5f),
                new Vector3(radius, thickness * .5f, radius), "Plate", walkable);
            Cylinder("Ceramic raised rim", p,
                at + Vector3.up * (thickness + .025f),
                new Vector3(radius * .86f, .025f, radius * .86f), "Plate");
            Cylinder("Subtle ceramic inset", p,
                at + Vector3.up * (thickness + .052f),
                new Vector3(radius * .65f, .009f, radius * .65f), "Steel");
        }

        private static void Rat(Transform p, Vector3 at, int chapter, BoxCollider bounds)
        {
            var body = Sphere("Rat silhouette - visual only", p, at,
                new Vector3(1.8f, .85f, 2.3f), "Rat");
            Sphere("Rat face", p, at + new Vector3(0, .2f, 1.1f),
                new Vector3(.9f, .55f, .8f), "Rat");
            Sphere("Rat ear left", p, at + new Vector3(-.68f, .72f, .5f),
                new Vector3(.49f, .55f, .26f), "Rat");
            Sphere("Rat ear right", p, at + new Vector3(.68f, .72f, .5f),
                new Vector3(.49f, .55f, .26f), "Rat");
            Tube("Rat curled tail", p,
                at + new Vector3(0, -.3f, -1.0f), at + new Vector3(1.9f, -.5f, -2.9f), .11f, "Rat");
            var activity = new GameObject("Rat visual activity bounds CH" + (chapter + 1));
            activity.transform.SetParent(p);
            activity.transform.position = at;
            var area = activity.AddComponent<BoxCollider>();
            area.isTrigger = true;
            area.size = new Vector3(18, 5, 20);
            var marker = body.AddComponent<DraftCreatureMarker>();
            marker.kind = CreatureKind.Rat;
            marker.chapterIndex = chapter;
            marker.activityBounds = bounds != null ? bounds : area;
            marker.visualOnly = true;
        }

        [MenuItem("Tools/Map2 Kitchen Rebuild/2 Capture Views")]
        public static void CapturePreviews()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var controllerInScene = UnityEngine.Object.FindFirstObjectByType<KitchenDraftController>();
            var cam = controllerInScene != null ? controllerInScene.previewCamera : null;
            if (cam == null) throw new InvalidOperationException("Saved preview camera missing");
            Vector3 beforePosition = cam.transform.position;
            Quaternion beforeRotation = cam.transform.rotation;
            float beforeField = cam.fieldOfView;
            try
            {
                for (int i = 0; i < ChapterStarts.Length; i++)
                {
                    Vector3 target = ChapterStarts[i];
                    cam.transform.position = target + new Vector3(-12, 8, -18);
                    cam.transform.LookAt(target + Vector3.up * 1.5f);
                    cam.fieldOfView = 63f;
                    RenderPng(cam, OutputPath("미리보기/CH" + (i + 1) + ".png"));
                }
                cam.transform.position = new Vector3(5, 272, -79);
                cam.transform.LookAt(new Vector3(2, 0, -6));
                cam.fieldOfView = 61f;
                RenderPng(cam, OutputPath("미리보기/Map2_Overview.png"));
                cam.transform.position = new Vector3(-79, 35, 44);
                cam.transform.LookAt(new Vector3(-48, 5, 52));
                cam.fieldOfView = 57f;
                RenderPng(cam, OutputPath("미리보기/Sink_Section.png"));
                cam.transform.position = new Vector3(-104, 23, -35);
                cam.transform.LookAt(new Vector3(-118, 8, -13));
                cam.fieldOfView = 58f;
                RenderPng(cam, OutputPath("미리보기/Conveyor_Detail.png"));
            }
            finally
            {
                cam.transform.position = beforePosition;
                cam.transform.rotation = beforeRotation;
                cam.fieldOfView = beforeField;
            }
            Debug.Log("[Map2 Rebuild] Captured overview, seven zones, sink section and conveyor details.");
        }

        internal static void RenderPng(Camera cam, string path)
        {
            var render = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            render.antiAliasing = 4;
            var oldActive = RenderTexture.active;
            var oldTarget = cam.targetTexture;
            try
            {
                cam.targetTexture = render;
                RenderTexture.active = render;
                cam.Render();
                var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                texture.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }
            finally
            {
                cam.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(render);
            }
        }

        [MenuItem("Tools/Map2 Kitchen Rebuild/3 Build Windows Preview")]
        public static void BuildWindows()
        {
            string output = OutputPath("실행본/Map2KitchenRebuild.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };
            string originalName = PlayerSettings.productName;
            BuildReport report;
            try
            {
                PlayerSettings.productName = "The Axiom - Map2 Kitchen Rebuild V3";
                report = BuildPipeline.BuildPlayer(options);
            }
            finally { PlayerSettings.productName = originalName; }
            var messages = new List<string>();
            if (report.steps != null)
                foreach (var step in report.steps)
                    foreach (var message in step.messages)
                        if (message.type == LogType.Warning || message.type == LogType.Error || message.type == LogType.Exception)
                            messages.Add(message.type + ": " + message.content);
            var info = new BuildInfo
            {
                result = report.summary.result.ToString(), output = output,
                unityVersion = Application.unityVersion, totalSize = report.summary.totalSize,
                messages = messages.ToArray()
            };
            string reportPath = OutputPath("검증/build_report.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, JsonUtility.ToJson(info, true), System.Text.Encoding.UTF8);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Map2 rebuild Windows preview failed: " + report.summary.result);
        }

        private static string OutputPath(string relative)
        { return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, relative)); }
    }
}
