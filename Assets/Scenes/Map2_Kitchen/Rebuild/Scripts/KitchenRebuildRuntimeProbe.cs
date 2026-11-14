using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Map2KitchenDraft
{
    /// <summary>Opt-in physical checks of the built Windows scene.</summary>
    public sealed class KitchenRebuildRuntimeProbe : MonoBehaviour
    {
        [Serializable] private sealed class Check
        {
            public string name;
            public bool passed;
            public string detail;
        }

        [Serializable] private sealed class Report
        {
            public string unityVersion;
            public string scene;
            public string executionEnvironment;
            public bool success;
            public string scope;
            public Check[] checks;
        }

        private readonly List<Check> checks = new List<Check>();
        private KitchenDraftController controller;
        private PlayerMover actor;
        private Rigidbody body;
        private PlayerFollowCamera follow;
        private Camera sceneCamera;
        private string root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Environment.GetCommandLineArgs().Any(a => a == "-map2rebuildverify")) return;
            new GameObject("Map2 Rebuild Physical Verification").AddComponent<KitchenRebuildRuntimeProbe>();
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            yield return new WaitForSecondsRealtime(.7f);
            bool ready = false;
            try
            {
                root = Directory.GetParent(Application.dataPath).Parent.FullName;
                controller = FindFirstObjectByType<KitchenDraftController>();
                follow = FindFirstObjectByType<PlayerFollowCamera>();
                sceneCamera = Camera.main;
                actor = FindObjectsByType<PlayerMover>(FindObjectsSortMode.None)
                    .FirstOrDefault(m => m.transform == PlayerControlSwitcher.ActiveTarget);
                body = actor != null ? actor.GetComponent<Rigidbody>() : null;
                ready = controller != null && follow != null && sceneCamera != null && actor != null &&
                    body != null && controller.chapters != null && controller.chapters.Length == 7 &&
                    !sceneCamera.orthographic && follow.enableMouseOrbit;
                Add("Built scene has native 3D team setup", ready,
                    "controller=" + (controller != null) + ", actor=" + (actor != null) +
                    ", native follow=" + (follow != null));
            }
            catch (Exception e) { Add("Built scene setup", false, e.ToString()); }
            if (!ready) { Finish(); yield break; }
            VerifyNativeControls();

            bool externalBefore = actor.ExternallyDriven;
            actor.ExternallyDriven = true;
            controller.ResetDraft();

            // Real Rigidbody movement, with gameplay input suppressed. These
            // checks intentionally do not claim manual keyboard traversal.
            Teleport(new Vector3(-118, 9.1f, -36));
            body.velocity = new Vector3(3, 0, 0);
            float x0 = body.position.x;
            yield return new WaitForSecondsRealtime(.35f);
            float dx = body.position.x - x0;
            Teleport(new Vector3(-118, 9.1f, -36));
            body.velocity = new Vector3(0, 0, 3);
            float z0 = body.position.z;
            yield return new WaitForSecondsRealtime(.35f);
            float dz = body.position.z - z0;
            Add("Real Rigidbody moves independently on X and Z", dx > .35f && dz > .35f,
                "X=" + dx.ToString("F2") + " m, Z=" + dz.ToString("F2") + " m");

            string imageDir = Path.Combine(root, "미리보기");
            if (IsDetailScene()) imageDir = Path.Combine(imageDir, "디테일");
            try { Directory.CreateDirectory(imageDir); }
            catch (Exception e) { Add("Actual-camera preview directory", false, e.ToString()); }
            for (int i = 0; i < 7; i++)
            {
                bool previewStarted = true;
                try { controller.PreviewChapter(i); }
                catch (Exception e)
                {
                    previewStarted = false;
                    Add("CH" + (i + 1) + " review warp", false, e.ToString());
                }
                if (!previewStarted) continue;
                yield return null;
                yield return null;
                try
                {
                    bool region = controller.chapters[i].volume.bounds.Contains(actor.transform.position);
                    bool ground = GroundUnder(actor.transform.position);
                    bool cameraFollows = PlayerControlSwitcher.ActiveTarget == actor.transform &&
                        follow.target == actor.transform;
                    Add("CH" + (i + 1) + " review position has physical ground and native camera",
                        region && ground && cameraFollows,
                        "position=" + actor.transform.position.ToString("F2") +
                        ", region=" + region + ", ground=" + ground + ", follow=" + cameraFollows);
                    string imagePath = Path.Combine(imageDir, "Rebuild_Runtime_CH" + (i + 1) + ".png");
                    Capture(imagePath);
                    Add("CH" + (i + 1) + " actual-camera image", File.Exists(imagePath) &&
                        new FileInfo(imagePath).Length > 3000,
                        "bytes=" + (File.Exists(imagePath) ? new FileInfo(imagePath).Length : 0));
                }
                catch (Exception e) { Add("CH" + (i + 1) + " runtime sample", false, e.ToString()); }
            }

            // Test the user-reported bypass as a physical descent into the open basin.
            bool sinkReady = false;
            float firstY = 0;
            try
            {
                controller.ResetDraft();
                Teleport(new Vector3(-48, 8.85f, 79));
                firstY = body.position.y;
                sinkReady = true;
            }
            catch (Exception e) { Add("Physical sink descent setup", false, e.ToString()); }
            if (sinkReady)
            {
                for (int i = 0; i < 360; i++)
                {
                    body.velocity = new Vector3(0, body.velocity.y, -5.4f);
                    yield return new WaitForFixedUpdate();
                    if (controller.HasVisitedSink) break;
                }
                try
                {
                Add("Rigidbody descends through the actual sink into its visit volume",
                    controller.HasVisitedSink && body.position.y < 4.1f &&
                    body.position.z < 52f && firstY - body.position.y > 4f,
                    "start Y=" + firstY.ToString("F2") + ", end=" + body.position.ToString("F2") +
                    ", visited=" + controller.HasVisitedSink);
                string sinkImage = Path.Combine(imageDir, "Rebuild_Runtime_Sink.png");
                Capture(sinkImage);
                Add("Runtime sink camera image", File.Exists(sinkImage) &&
                    new FileInfo(sinkImage).Length > 3000,
                    "bytes=" + (File.Exists(sinkImage) ? new FileInfo(sinkImage).Length : 0));
                }
                catch (Exception e) { Add("Physical sink descent results", false, e.ToString()); }
            }

            bool collected = false;
            Vector3 landing = default;
            try
            {
                controller.ResetDraft();
                bool closed = controller.doorBlocker != null && controller.doorBlocker.enabled &&
                    !controller.HasKey && !controller.ExitReached && !controller.HasVisitedSink;
                Vector3 keyAt = controller.key.position;
                Teleport(keyAt + new Vector3(.2f, 0, .1f));
                collected = controller.TryCollectKey(actor.transform);
                Add("Key opens only the upper exit door", closed && collected && controller.HasKey &&
                    !controller.doorBlocker.enabled,
                    "closed=" + closed + ", collected=" + collected +
                    ", opened=" + !controller.doorBlocker.enabled);

                landing = controller.finalDropPoint.position;
                Teleport(new Vector3(landing.x, 17.8f, landing.z));
            }
            catch (Exception e) { Add("Key and gravity setup", false, e.ToString()); }
            if (collected)
            {
                for (int i = 0; i < 180 && !controller.ExitReached; i++)
                    yield return new WaitForFixedUpdate();
                try
                {
                Add("Gravity fall reaches actual lower handoff trigger", controller.ExitReached &&
                    body.position.y < 3f,
                    "actor=" + body.position.ToString("F2") + ", trigger=" +
                    controller.ExitReached + ", lower marker=" + landing.ToString("F2"));
                controller.ResetDraft();
                Add("Review reset restores closed door and unused sink visit", !controller.HasKey &&
                    !controller.ExitReached && !controller.HasVisitedSink &&
                    controller.doorBlocker.enabled && controller.key.gameObject.activeSelf,
                    "door=" + controller.doorBlocker.enabled + ", sink=" + controller.HasVisitedSink);
                }
                catch (Exception e) { Add("Key, drop and reset results", false, e.ToString()); }
            }
            actor.ExternallyDriven = externalBefore;
            Finish();
        }

        private void Teleport(Vector3 destination)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = destination;
            actor.transform.position = destination;
            Physics.SyncTransforms();
            PlayerFollowCamera.SnapToTarget(actor.transform);
        }

        private void VerifyNativeControls()
        {
            try
            {
                var yaw = typeof(PlayerFollowCamera).GetField("orbitYaw", BindingFlags.Instance | BindingFlags.NonPublic);
                var pitch = typeof(PlayerFollowCamera).GetField("orbitPitch", BindingFlags.Instance | BindingFlags.NonPublic);
                bool available = yaw != null && pitch != null && follow.enableMouseOrbit;
                float changed = 0;
                if (available)
                {
                    float oldYaw = (float)yaw.GetValue(follow), oldPitch = (float)pitch.GetValue(follow);
                    yaw.SetValue(follow, 0f); pitch.SetValue(follow, 0f);
                    PlayerFollowCamera.SnapToTarget(actor.transform);
                    Vector3 before = sceneCamera.transform.forward;
                    yaw.SetValue(follow, 55f); pitch.SetValue(follow, 18f);
                    PlayerFollowCamera.SnapToTarget(actor.transform);
                    changed = Vector3.Angle(before, sceneCamera.transform.forward);
                    yaw.SetValue(follow, oldYaw); pitch.SetValue(follow, oldPitch);
                    PlayerFollowCamera.SnapToTarget(actor.transform);
                }
                Add("Native mouse orbit changes actual perspective view", available && changed > 12f,
                    "camera forward changed " + changed.ToString("F1") + " degrees");
            }
            catch (Exception e) { Add("Native mouse orbit", false, e.ToString()); }

            try
            {
                var switcher = FindFirstObjectByType<PlayerControlSwitcher>();
                var activeField = typeof(PlayerControlSwitcher).GetField("activePlayer",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var apply = typeof(PlayerControlSwitcher).GetMethod("ApplyActive",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var other = FindObjectsByType<PlayerMover>(FindObjectsSortMode.None).FirstOrDefault(p => p != actor);
                bool available = switcher != null && activeField != null && apply != null && other != null;
                bool switched = false;
                if (available)
                {
                    activeField.SetValue(switcher, other); apply.Invoke(switcher, null);
                    switched = PlayerControlSwitcher.ActiveTarget == other.transform &&
                        follow.target == other.transform && other.IsControlled &&
                        FindObjectsByType<PlayerMover>(FindObjectsSortMode.None).Count(p => p.IsControlled) == 1;
                    activeField.SetValue(switcher, actor); apply.Invoke(switcher, null);
                }
                Add("Native shape switcher hands camera and control to another shape",
                    available && switched && PlayerControlSwitcher.ActiveTarget == actor.transform &&
                    follow.target == actor.transform,
                    "switch API=" + available + ", other shape controlled=" + switched);
            }
            catch (Exception e) { Add("Native shape switching", false, e.ToString()); }
        }

        private bool GroundUnder(Vector3 position)
        {
            foreach (RaycastHit hit in Physics.RaycastAll(position + Vector3.up * .2f,
                Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider != null && hit.collider.GetComponentInParent<PlayerMover>() == null)
                    return true;
            return false;
        }

        private void Capture(string path)
        {
            var previous = sceneCamera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(960, 540, 24);
            Texture2D texture = null;
            try
            {
                sceneCamera.targetTexture = target;
                sceneCamera.Render();
                RenderTexture.active = target;
                texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                sceneCamera.targetTexture = previous;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                if (texture != null) Destroy(texture);
            }
        }

        private void Add(string name, bool passed, string detail)
        {
            checks.Add(new Check { name = name, passed = passed, detail = detail });
            Debug.Log("[Map2 Rebuild Probe] " + (passed ? "PASS " : "FAIL ") + name + ": " + detail);
        }

        private void Finish()
        {
            bool success = checks.Count > 0 && checks.All(c => c.passed);
            var report = new Report
            {
                unityVersion = Application.unityVersion,
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                executionEnvironment = Application.isEditor ? "Unity Editor Play Mode" : "Windows built player",
                success = success,
                scope = "Native 3D camera, physical Rigidbody X/Z motion and sink descent, seven review warps with real camera PNGs, key API, gravity drop and lower trigger. Movement input is bypassed; this does not establish a manual full walkthrough or unfinished pursuit mechanics. Execution environment is recorded separately.",
                checks = checks.ToArray()
            };
            try
            {
                if (string.IsNullOrEmpty(root)) root = Directory.GetParent(Application.dataPath).Parent.FullName;
                string prefix = IsDetailScene() ? "detail" : "rebuild";
                string path = Path.Combine(root, "검증", prefix +
                    (Application.isEditor ? "_playmode.json" : "_runtime.json"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                Debug.Log("[Map2 Rebuild Probe] " + (success ? "PASS" : "FAIL") + " report=" + path);
            }
            catch (Exception e) { success = false; Debug.LogError("[Map2 Rebuild Probe] Report write failed: " + e); }
#if UNITY_EDITOR
            if (Application.isEditor)
            {
                UnityEditor.EditorApplication.ExitPlaymode();
                if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(success ? 0 : 1);
                return;
            }
#endif
            Application.Quit(success ? 0 : 1);
        }

        private static bool IsDetailScene()
        {
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Map2KitchenDetail";
        }
    }
}
