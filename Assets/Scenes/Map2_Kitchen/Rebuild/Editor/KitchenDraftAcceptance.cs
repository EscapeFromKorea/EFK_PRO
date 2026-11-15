using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Map2KitchenDraft.Editor
{
    /// <summary>Checks the saved draft scene's confirmed interactions in Edit mode.</summary>
    public static class KitchenDraftAcceptance
    {
        private const string ScenePath = "Assets/Scenes/Map2_Kitchen/Rebuild/Scenes/Map2KitchenDraft.unity";

        [Serializable]
        private sealed class CheckResult
        {
            public string name;
            public bool passed;
            public string detail;
        }

        [Serializable]
        private sealed class AcceptanceReport
        {
            public string scenePath;
            public string unityVersion;
            public bool success;
            public CheckResult[] checks;
            public string scope;
        }

        [MenuItem("Tools/Map2 Kitchen Draft/4 Run Acceptance")]
        public static void Run()
        {
            var results = new List<CheckResult>();
            bool sceneOpened = false;
            try
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                    throw new InvalidOperationException("Saved draft scene is missing: " + ScenePath);

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                sceneOpened = true;
                var controller = UnityEngine.Object.FindFirstObjectByType<KitchenDraftController>();
                var players = UnityEngine.Object.FindObjectsByType<PlayerMover>(FindObjectsSortMode.None);
                Add(results, "Saved scene and three team players", controller != null && players.Length == 3,
                    "Controller=" + (controller != null) + ", PlayerMover count=" + players.Length);
                if (controller == null || players.Length != 3)
                    throw new InvalidOperationException("Controller or team player count invalid; dependent checks cannot run.");

                var actor = players[0].transform;
                var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
                var nativeCameras = UnityEngine.Object.FindObjectsByType<PlayerFollowCamera>(FindObjectsSortMode.None);
                var sceneCamera = controller.previewCamera;
                Add(results, "Single native perspective orbit camera",
                    cameras.Length == 1 && nativeCameras.Length == 1 && sceneCamera != null &&
                    sceneCamera.GetComponent<PlayerFollowCamera>() == nativeCameras[0] &&
                    !sceneCamera.orthographic && Mathf.Abs(sceneCamera.fieldOfView - 60f) <= 1f &&
                    nativeCameras[0].enableMouseOrbit &&
                    sceneCamera.GetComponents<MonoBehaviour>().All(c => c == null || c.GetType().Name != "KitchenDraftCamera"),
                    "camera count=" + cameras.Length + ", native count=" + nativeCameras.Length +
                    ", FOV=" + (sceneCamera != null ? sceneCamera.fieldOfView.ToString("F1") : "missing"));
                bool free3D = players.All(p => p.cameraRelativeInput &&
                    (p.GetComponent<Rigidbody>().constraints & RigidbodyConstraints.FreezePositionX) == 0);
                Add(results, "All team players free on X with camera-relative movement", free3D,
                    string.Join("; ", players.Select(p => p.name + ":" + p.GetComponent<Rigidbody>().constraints)));
                var sphere = players.FirstOrDefault(p => p.name.ToLowerInvariant().Contains("sphere"));
                var cube = players.FirstOrDefault(p => p.name.ToLowerInvariant().Contains("cube"));
                var tetra = players.FirstOrDefault(p => p.name.ToLowerInvariant().Contains("tetra"));
                bool shapeDefaults = sphere != null && cube != null && tetra != null &&
                    !HasRotationFreeze(sphere) && HasRotationFreeze(cube) && HasRotationFreeze(tetra);
                Add(results, "Shape-specific rotation defaults", shapeDefaults,
                    "sphere=" + (sphere != null ? sphere.GetComponent<Rigidbody>().constraints.ToString() : "missing") +
                    ", cube=" + (cube != null ? cube.GetComponent<Rigidbody>().constraints.ToString() : "missing") +
                    ", tetra=" + (tetra != null ? tetra.GetComponent<Rigidbody>().constraints.ToString() : "missing"));
                var regions = controller.chapters;
                bool seven = regions != null && regions.Length == 7 && controller.chapterStarts != null &&
                    controller.chapterStarts.Length == 7;
                if (seven) for (int i = 0; i < 7; i++)
                    seven &= regions[i] != null && regions[i].index == i && regions[i].volume != null &&
                        regions[i].start != null && regions[i].end != null &&
                        regions[i].start == controller.chapterStarts[i] &&
                        !string.IsNullOrWhiteSpace(regions[i].chapterName) &&
                        !string.IsNullOrWhiteSpace(regions[i].objective);
                Add(results, "Seven saved spatial chapter regions", seven,
                    "regions=" + (regions != null ? regions.Length : 0));
                if (!seven) throw new InvalidOperationException("Seven complete chapter regions required.");
                var starts = regions.Select(r => r.start.position).ToArray();
                float spanX = starts.Max(p => p.x) - starts.Min(p => p.x);
                float spanY = starts.Max(p => p.y) - starts.Min(p => p.y);
                float spanZ = starts.Max(p => p.z) - starts.Min(p => p.z);
                Add(results, "Chapter route spans X Y Z", spanX > 15f && spanY > 2f && spanZ > 15f,
                    "X=" + spanX.ToString("F1") + " Y=" + spanY.ToString("F1") + " Z=" + spanZ.ToString("F1"));
                for (int i = 0; i < 7; i++)
                {
                    bool inside = regions[i].volume.bounds.Contains(regions[i].start.position);
                    bool ground = GroundUnder(regions[i].start.position);
                    Add(results, "CH" + (i + 1) + " start inside region with floor", inside && ground,
                        "start=" + regions[i].start.position.ToString("F2") + ", inside=" + inside + ", floor=" + ground);
                }
                var exits = UnityEngine.Object.FindObjectsByType<DraftExitTrigger>(FindObjectsSortMode.None);
                bool lowerExit = exits.Length == 1 && controller.finalDropPoint != null &&
                    controller.doorBlocker != null &&
                    exits[0].controller == controller &&
                    exits[0].transform.position.y < controller.doorBlocker.bounds.min.y - 1f &&
                    Vector3.Distance(exits[0].transform.position, controller.finalDropPoint.position) < 3f;
                Add(results, "Exit on lower drop landing", lowerExit,
                    "exit count=" + exits.Length +
                    ", physical gate bottom Y=" + (controller.doorBlocker != null ? controller.doorBlocker.bounds.min.y.ToString("F1") : "missing") +
                    ", exit Y=" + (exits.Length > 0 ? exits[0].transform.position.y.ToString("F1") : "missing") +
                    ", landing distance=" + (exits.Length > 0 && controller.finalDropPoint != null ?
                        Vector3.Distance(exits[0].transform.position, controller.finalDropPoint.position).ToString("F1") : "missing"));
                var key = controller.key;
                var door = controller.doorVisual;
                var blocker = controller.doorBlocker;
                Add(results, "Key and door wiring", key != null && door != null && blocker != null,
                    "Key=" + (key != null) + ", visual=" + (door != null) + ", blocker=" + (blocker != null));
                if (key == null || door == null || blocker == null)
                    throw new InvalidOperationException("Key gate references missing; dependent checks cannot run.");

                controller.ResetDraft(); // Awake is not called in Edit mode: captures the closed-door baseline.
                Vector3 closedDoorPosition = door.localPosition;
                Vector3 closedDoorWorldCenter = blocker.bounds.center;
                Physics.SyncTransforms();

                controller.NotifyExit(actor);
                Add(results, "Exit rejects actor without key", !controller.ExitReached && !controller.HasKey,
                    "ExitReached=" + controller.ExitReached + ", HasKey=" + controller.HasKey);

                var nonPlayer = new GameObject("Acceptance non-player probe");
                nonPlayer.transform.position = key.position;
                bool nonPlayerAccepted;
                try { nonPlayerAccepted = controller.TryCollectKey(nonPlayer.transform); }
                finally { UnityEngine.Object.DestroyImmediate(nonPlayer); }

                actor.position = key.position + Vector3.up * (controller.interactionRadius + 5f);
                Physics.SyncTransforms();
                bool farPlayerAccepted = controller.TryCollectKey(actor);
                actor.position = key.position + Vector3.right * (controller.interactionRadius + 5f);
                Physics.SyncTransforms();
                bool farXAccepted = controller.TryCollectKey(actor);
                Add(results, "Key rejects non-player and distant player",
                    !nonPlayerAccepted && !farPlayerAccepted && !farXAccepted && !controller.HasKey,
                    "Non-player=" + nonPlayerAccepted + ", far Y=" + farPlayerAccepted + ", far X=" + farXAccepted);

                bool closedRayHitsDoor = RayHitsDoor(blocker, closedDoorWorldCenter);
                actor.position = key.position + Vector3.forward * Mathf.Min(0.3f, controller.interactionRadius * 0.25f);
                Physics.SyncTransforms();
                bool nearAccepted = controller.TryCollectKey(actor);
                bool doorMoved = Vector3.Distance(door.localPosition,
                    closedDoorPosition + controller.doorOpenOffset) < 0.001f;
                Add(results, "Nearby player collects key and opens gate",
                    nearAccepted && controller.HasKey && !blocker.enabled && doorMoved && !key.gameObject.activeSelf,
                    "Accepted=" + nearAccepted + ", HasKey=" + controller.HasKey +
                    ", blocker enabled=" + blocker.enabled + ", visual moved=" + doorMoved);

                bool openedRayHitsDoor = RayHitsDoor(blocker, closedDoorWorldCenter);
                Add(results, "Door blocks physical ray only while closed",
                    closedRayHitsDoor && !openedRayHitsDoor,
                    "Closed hit=" + closedRayHitsDoor + ", opened hit=" + openedRayHitsDoor);

                bool duplicateAccepted = controller.TryCollectKey(actor);
                Add(results, "Duplicate key collection rejected", !duplicateAccepted && controller.HasKey,
                    "Duplicate accepted=" + duplicateAccepted + ", HasKey=" + controller.HasKey);

                controller.NotifyExit(actor);
                bool acceptedExitWithKey = controller.ExitReached;
                controller.ResetDraft();
                bool resetClosed = !controller.HasKey && !controller.ExitReached && blocker.enabled &&
                    key.gameObject.activeSelf && Vector3.Distance(door.localPosition, closedDoorPosition) < 0.001f;
                controller.NotifyExit(actor);
                Add(results, "Exit requires key and reset closes gate",
                    acceptedExitWithKey && resetClosed && !controller.ExitReached,
                    "Exit with key=" + acceptedExitWithKey + ", reset closed=" + resetClosed +
                    ", exit after reset=" + controller.ExitReached);

                var jets = UnityEngine.Object.FindObjectsByType<DraftWaterJet>(FindObjectsSortMode.None);
                bool waterFixed = jets.Length > 0;
                var worldStarts = new Vector3[jets.Length];
                var worldEnds = new Vector3[jets.Length];
                var lineStarts = new Vector3[jets.Length];
                var lineEnds = new Vector3[jets.Length];
                for (int i = 0; i < jets.Length; i++)
                {
                    var line = jets[i].GetComponent<LineRenderer>();
                    waterFixed &= !jets[i].TracksPlayer && line != null && line.positionCount == 2;
                    worldStarts[i] = jets[i].WorldStart;
                    worldEnds[i] = jets[i].WorldEnd;
                    if (line != null && line.positionCount == 2)
                    {
                        lineStarts[i] = line.GetPosition(0);
                        lineEnds[i] = line.GetPosition(1);
                        waterFixed &= Close(lineStarts[i], worldStarts[i]) && Close(lineEnds[i], worldEnds[i]);
                    }
                }
                actor.position += new Vector3(2f, 1f, 3f);
                Physics.SyncTransforms();
                for (int i = 0; i < jets.Length; i++)
                {
                    var line = jets[i].GetComponent<LineRenderer>();
                    waterFixed &= Close(jets[i].WorldStart, worldStarts[i]) &&
                        Close(jets[i].WorldEnd, worldEnds[i]) &&
                        line != null && line.positionCount == 2 &&
                        Close(line.GetPosition(0), lineStarts[i]) && Close(line.GetPosition(1), lineEnds[i]);
                }
                Add(results, "Water LineRenderer endpoints remain fixed when player moves", waterFixed,
                    "Jet count=" + jets.Length + ", all endpoints fixed=" + waterFixed);

                var markers = UnityEngine.Object.FindObjectsByType<DraftCreatureMarker>(FindObjectsSortMode.None);
                int roachCount = 0;
                bool allRoachesInside = true;
                foreach (var marker in markers)
                {
                    if (marker.kind != CreatureKind.Cockroach) continue;
                    roachCount++;
                    allRoachesInside &= marker.chapterIndex == 4 && marker.activityBounds != null &&
                        marker.IsInsideActivityBounds(marker.transform.position);
                }
                Add(results, "Cockroach remains inside sink activity bounds", roachCount > 0 && allRoachesInside,
                    "Cockroach count=" + roachCount + ", all inside CH5 bounds=" + allRoachesInside);
            }
            catch (Exception exception)
            {
                Add(results, "Acceptance execution", false, exception.ToString());
            }
            finally
            {
                // Reopen the saved asset instead of saving the mutated Edit-mode scene.
                if (sceneOpened)
                {
                    try { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); }
                    catch (Exception exception)
                    { Add(results, "Restore saved scene", false, exception.ToString()); }
                }
            }

            bool success = results.Count > 0;
            foreach (var result in results) success &= result.passed;
            var report = new AcceptanceReport
            {
                scenePath = ScenePath,
                unityVersion = Application.unityVersion,
                success = success,
                checks = results.ToArray(),
                scope = "Saved-scene EditMode structure, 3D placement and direct physics/API checks. No actual keyboard or mouse input, runtime pursuit AI or full manual traversal verified. Scene mutations discarded by reopening; no scene save."
            };
            string reportPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "검증", "acceptance_report.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            Debug.Log("[Map2 Kitchen] Acceptance " + (success ? "passed" : "failed") + ": " + reportPath);
            if (!success) throw new InvalidOperationException("Map2 kitchen acceptance failed; see " + reportPath);
        }

        private static bool RayHitsDoor(Collider door, Vector3 closedCenter)
        {
            Physics.SyncTransforms();
            RaycastHit hit;
            return Physics.RaycastAll(closedCenter + Vector3.right * 2f, Vector3.left, 4f)
                .Any(rayHit => rayHit.collider == door);
        }

        private static bool GroundUnder(Vector3 point)
        {
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * 0.2f, Vector3.down, 6f))
                if (hit.collider != null && !hit.collider.isTrigger &&
                    hit.collider.GetComponentInParent<PlayerMover>() == null)
                    return true;
            return false;
        }

        private static bool HasRotationFreeze(PlayerMover mover)
        {
            var constraints = mover.GetComponent<Rigidbody>().constraints;
            return (constraints & RigidbodyConstraints.FreezeRotationX) != 0 &&
                (constraints & RigidbodyConstraints.FreezeRotationY) != 0 &&
                (constraints & RigidbodyConstraints.FreezeRotationZ) != 0;
        }

        private static bool Close(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.001f;

        private static void Add(List<CheckResult> results, string name, bool passed, string detail)
        {
            results.Add(new CheckResult { name = name, passed = passed, detail = detail });
        }
    }
}
