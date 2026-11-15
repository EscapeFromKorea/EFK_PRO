using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Map2KitchenDraft.Editor
{
    public static class KitchenConfirmedScene
    {
        private const string Source = "Assets/Scenes/Map2_Kitchen/Rebuild/Scenes/Map2KitchenDetail.unity";
        public const string ScenePath = "Assets/Scenes/Map2_Kitchen/Rebuild/Scenes/Map2KitchenPlayable.unity";
        public const string Map3Path = "Assets/Scenes/Map3_ToyWorld/Map3_ToyWorld.unity";

        [MenuItem("Tools/Map2 Kitchen Rebuild/Build Playable Scene")]
        public static void BuildAndVerify()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Map3Path) == null)
                throw new InvalidOperationException("Map 3 scene is unavailable: " + Map3Path);

            var scene = EditorSceneManager.OpenScene(Source, OpenSceneMode.Single);
            var draft = Object.FindFirstObjectByType<KitchenDraftController>();
            if (draft == null) throw new InvalidOperationException("Missing Map 2 controller.");

            foreach (var marker in Object.FindObjectsByType<DraftCreatureMarker>(FindObjectsSortMode.None))
                AddPursuit(draft, marker);
            KitchenSterilizerScene.Apply(draft);
            AddMotionLaser(draft);
            AddMapHandoff(draft);
            draft.chapters[0].objective = "쥐를 피해 세척구로 올라가세요";
            draft.chapters[2].objective = "쥐를 피해 접시 통로를 지나세요";
            draft.chapters[3].objective = "움직임을 감지하는 레이저를 피해 싱크 안으로 내려가세요";
            draft.chapters[4].objective = "바퀴벌레를 피해 싱크대에서 나오세요";
            draft.chapters[5].objective = "그릇 정리 구역으로 이동해 쥐 추격에서 벗어나세요";
            draft.chapters[6].objective = "열쇠로 위쪽 출구를 열고 음식 배출구로 이동하세요";

            if (!EditorSceneManager.SaveScene(scene, ScenePath, saveAsCopy: true))
                throw new IOException("Could not save " + ScenePath);

            var enabledScenes = EditorBuildSettings.scenes.ToList();
            RegisterScene(enabledScenes, ScenePath);
            RegisterScene(enabledScenes, Map3Path);
            EditorBuildSettings.scenes = enabledScenes.ToArray();
            AssetDatabase.SaveAssets();
            KitchenRebuildVerification.RunForScene(ScenePath, "confirmed_acceptance.json");
        }

        private static void RegisterScene(List<EditorBuildSettingsScene> scenes, string path)
        {
            int index = scenes.FindIndex(scene => scene.path == path);
            var enabledScene = new EditorBuildSettingsScene(path, true);
            if (index < 0) scenes.Add(enabledScene);
            else scenes[index] = enabledScene;
        }

        private static void AddPursuit(KitchenDraftController draft, DraftCreatureMarker marker)
        {
            Transform originalParent = marker.transform.parent;
            var root = new GameObject(marker.kind == CreatureKind.Rat ? "Rat Pursuit" : "Sink Cockroach Pursuit");
            root.transform.SetParent(originalParent, false);
            var pivot = new GameObject("Creature Visual").transform;
            pivot.SetParent(root.transform, false);
            root.transform.position = marker.transform.position;

            // The rat's draft body and details were separate scene siblings.
            if (marker.kind == CreatureKind.Rat)
            {
                var siblings = originalParent.Cast<Transform>()
                    .Where(t => t.name == "Rat face" || t.name.StartsWith("Rat ear", StringComparison.Ordinal) ||
                                t.name == "Rat curled tail")
                    .ToArray();
                foreach (Transform part in siblings) part.SetParent(pivot, true);
            }
            marker.transform.SetParent(pivot, true);
            marker.visualOnly = false;
            marker.gameObject.name = marker.kind == CreatureKind.Rat ? "Rat Body" : "Cockroach Body";
            var pursuit = root.AddComponent<KitchenPursuit>();
            pursuit.facingVisual = pivot;
            Vector3[] route = RouteFor(marker);
            bool isRat = marker.kind == CreatureKind.Rat;
            pursuit.Configure(draft, marker, route, isRat ? 2.6f : 1.9f,
                isRat ? 11f : 0f, isRat ? 3.7f : 9f);
        }

        private static Vector3[] RouteFor(DraftCreatureMarker marker)
        {
            if (marker.kind == CreatureKind.Cockroach)
                return new[]
                {
                    new Vector3(-54.7f, 1.5f, 50), new Vector3(-48, 1.5f, 49),
                    new Vector3(-48, 1.5f, 26), new Vector3(-39, 1.5f, 26)
                };

            switch (marker.chapterIndex)
            {
                case 0:
                    var entry = new List<Vector3> { new Vector3(-118, 1f, -116) };
                    for (int i = 0; i < 8; i++)
                    {
                        float nextHeight = Mathf.Lerp(1f, 8.5f, (i + 1f) / 8f);
                        entry.Add(new Vector3(-118, Mathf.Lerp(1f, 8.5f, i / 8f),
                            Mathf.Lerp(-112f, -75f, i / 8f)));
                        entry.Add(new Vector3(-118, nextHeight, Mathf.Lerp(-112f, -75f, (i + .84f) / 8f)));
                        entry.Add(new Vector3(-118, nextHeight, Mathf.Lerp(-112f, -75f, (i + 1f) / 8f)));
                    }
                    entry.Add(new Vector3(-118, 8.5f, -24));
                    return entry.ToArray();
                case 2:
                    return new[]
                    {
                        new Vector3(-118, 8.56f, 100), new Vector3(-48, 8.56f, 100),
                        new Vector3(-48, 8.56f, 83)
                    };
                case 5:
                    return new[]
                    {
                        new Vector3(12, 8.5f, 65), new Vector3(12, 8.5f, 100),
                        new Vector3(42, 8.5f, 100)
                    };
                default:
                    throw new InvalidOperationException("Unexpected rat marker for chapter " + marker.chapterIndex);
            }
        }

        private static void AddMotionLaser(KitchenDraftController draft)
        {
            Transform parent = draft.chapters[3].transform.parent;
            Transform lens = parent.Find("Pest fixture lens - color undecided");
            Transform fixture = parent.Find("Pest-laser fixture location only");
            if (lens == null || fixture == null) throw new InvalidOperationException("Missing sink laser fixture.");
            lens.position = new Vector3(-52f, 14.55f, 73f);
            fixture.position = lens.position + Vector3.up * .64f;
            var detection = AddVolume(parent, "Wash laser activity area",
                new Vector3(-48, 9f, 69.5f), new Vector3(16, 9f, 27f));
            var laser = lens.gameObject.AddComponent<KitchenWashHazards>();
            laser.activityBounds = detection;
            // Keep the original light neutral for review, using an existing bundled shader.
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Map2_Kitchen/Rebuild/Materials/WaterJet.mat");
            if (material != null) lens.GetComponent<LineRenderer>().sharedMaterial = material;
        }

        private static void AddMapHandoff(KitchenDraftController draft)
        {
            var oldExit = Object.FindFirstObjectByType<DraftExitTrigger>();
            if (oldExit == null) throw new InvalidOperationException("Missing food dispatch landing trigger.");
            var upper = AddVolume(oldExit.transform.parent, "Upper food dispatch ledge",
                new Vector3(-54f, 16.4f, -120f), new Vector3(5f, 4f, 6.6f));
            var shaft = AddVolume(oldExit.transform.parent, "Food dispatch exit and shaft",
                new Vector3(-58.3f, 9f, -120f), new Vector3(16.6f, 20f, 8.5f));
            GameObject landing = oldExit.gameObject;
            Object.DestroyImmediate(oldExit);
            var handoff = landing.AddComponent<KitchenMapHandoff>();
            handoff.Configure(draft, upper, shaft, Map3Path);
        }

        private static BoxCollider AddVolume(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var volume = new GameObject(name);
            volume.transform.SetParent(parent, false);
            volume.transform.position = center;
            var box = volume.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = true;
            return box;
        }
    }
}
