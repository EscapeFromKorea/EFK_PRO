using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Map2KitchenDraft.Editor
{
    public static class KitchenDetailScene
    {
        private const string BaseScene = "Assets/Scenes/Map2_Kitchen/Rebuild/Scenes/Map2KitchenDraft.unity";
        internal const string ScenePath = "Assets/Scenes/Map2_Kitchen/Rebuild/Scenes/Map2KitchenDetail.unity";
        private const string RouteMeshPath = "Assets/Scenes/Map2_Kitchen/Rebuild/Art/Meshes/V3/KitchenDetailRoute.asset";
        private const string MaterialsPath = "Assets/Scenes/Map2_Kitchen/Rebuild/Materials/V3/";

        [MenuItem("Tools/Map2 Kitchen Rebuild/Build Detail Review Scene")]
        public static void BuildAndReview()
        {
            var scene = EditorSceneManager.OpenScene(BaseScene, OpenSceneMode.Single);
            var camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("The base scene needs its review camera.");
            var detail = new GameObject("Kitchen equipment and route markings");
            KitchenDetailFixtures.Add(detail.transform);
            KitchenDetailCooking.Add(detail.transform);
            var draft = UnityEngine.Object.FindFirstObjectByType<KitchenDraftController>();
            KitchenChapterSevenDressing.Add(draft.chapters[6].transform.parent);
            Physics.SyncTransforms();
            AddRouteMarkings(detail.transform);
            if (detail.GetComponentInChildren<Collider>(true) != null)
                throw new InvalidOperationException("Kitchen detail added a collider to the existing route.");
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, ScenePath, saveAsCopy: true))
                throw new IOException("Could not save " + ScenePath);

            KitchenRebuildVerification.RunForScene(ScenePath, "detail_acceptance.json");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            CapturePreviews(Camera.main);
        }

        private static void AddRouteMarkings(Transform parent)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var edgeTriangles = new List<int>();
            // Mark approach, turn and arrival separately. Nothing spans a belt gap.
            Arrow(-118, -77, Vector3.forward);
            Arrow(-118, 95, Vector3.forward);
            Arrow(-114, 100, Vector3.right);
            Arrow(-52, 100, Vector3.right);
            Arrow(-48, 94, Vector3.back);
            Edge(-48, 83, Vector3.back);
            Arrow(-48, 78, Vector3.back);
            Arrow(-48, 62, Vector3.back);
            Arrow(-48, 30, Vector3.back);
            Arrow(-43, 26, Vector3.right);
            Arrow(7, 26, Vector3.right);
            Arrow(12, 70, Vector3.forward);
            Arrow(16, 100, Vector3.right);
            Arrow(38, 100, Vector3.right);
            Edge(41.1f, 100, Vector3.right);
            Arrow(73, 100, Vector3.right);
            Arrow(123, 100, Vector3.right);
            Arrow(128, 96, Vector3.back);
            Arrow(128, -115, Vector3.back);
            Arrow(123, -120, Vector3.left);
            Arrow(12, -120, Vector3.left);

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RouteMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Kitchen route paint" };
                AssetDatabase.CreateAsset(mesh, RouteMeshPath);
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(triangles, 0);
            mesh.SetTriangles(edgeTriangles, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            var markings = new GameObject("Directional paint and edge stripes");
            markings.transform.SetParent(parent, false);
            markings.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = markings.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { LoadMaterial("Trim"), LoadMaterial("Warning") };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            void Arrow(float x, float z, Vector3 heading)
            {
                var hit = Ground(x, z);
                var forward = Vector3.ProjectOnPlane(heading, hit.normal).normalized;
                var right = Vector3.Cross(hit.normal, forward).normalized;
                // Leave clearance above the existing visible slats on pitched belts.
                var center = hit.point + hit.normal * .075f;
                for (int line = 0; line < 2; line++)
                {
                    var tip = center + forward * (.6f - line * .74f);
                    var back = tip - forward * .59f;
                    Quad(back - right * .85f, tip, tip - forward * .23f,
                        back - right * .60f - forward * .18f, triangles);
                    Quad(tip, back + right * .85f, back + right * .60f - forward * .18f,
                        tip - forward * .23f, triangles);
                }
            }

            void Edge(float x, float z, Vector3 heading)
            {
                var hit = Ground(x, z);
                var forward = Vector3.ProjectOnPlane(heading, hit.normal).normalized;
                var right = Vector3.Cross(hit.normal, forward).normalized;
                var center = hit.point + hit.normal * .075f;
                for (int stripe = -4; stripe <= 4; stripe++)
                {
                    var p = center + right * (stripe * .57f);
                    Quad(p - right * .2f - forward * .18f, p - right * .05f + forward * .18f,
                        p + right * .26f + forward * .18f, p + right * .11f - forward * .18f, edgeTriangles);
                }
            }

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, List<int> indices)
            {
                int i = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                // Winding is upward for all route markings.
                if (Vector3.Cross(b - a, c - a).y > 0f)
                    indices.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                else
                    indices.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
        }

        private static RaycastHit Ground(float x, float z)
        {
            var hit = Physics.RaycastAll(new Vector3(x, 25f, z), Vector3.down, 25f, ~0,
                    QueryTriggerInteraction.Ignore)
                .Where(h => h.collider.name == "Belt continuous collision surface" ||
                            h.collider.name == "Map2 Sink North Entry Ramp")
                .OrderBy(h => h.distance)
                .FirstOrDefault();
            if (hit.collider == null)
                throw new InvalidOperationException($"No route surface at X{x}, Z{z}.");
            return hit;
        }

        private static Material LoadMaterial(string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + name + ".mat");
            if (material == null) throw new InvalidOperationException("Missing material " + name);
            return material;
        }

        private static void CapturePreviews(Camera camera)
        {
            var savedPosition = camera.transform.position;
            var savedRotation = camera.transform.rotation;
            float savedFov = camera.fieldOfView;
            try
            {
                Capture("01_살균기", new Vector3(-110, 14, -22), new Vector3(-119, 12, -7), 63);
                Capture("02_싱크진입", new Vector3(-48, 10.85f, 86), new Vector3(-48, 5, 65), 60);
                Capture("03_그릇정리", new Vector3(95, 14, 94), new Vector3(98, 11.5f, 109), 60);
                Capture("04_조리대", new Vector3(127, 13, -70), new Vector3(138, 11, -72), 60);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
                camera.fieldOfView = savedFov;
            }

            void Capture(string name, Vector3 position, Vector3 target, float fov)
            {
                camera.transform.position = position;
                camera.transform.LookAt(target);
                camera.fieldOfView = fov;
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "미리보기", "디테일"));
                KitchenRebuildBuilder.RenderPng(camera, Path.Combine(folder, name + ".png"));
            }
        }
    }
}
