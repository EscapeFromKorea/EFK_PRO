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
    /// <summary>Opt-in built-player smoke test. Runs only with -map2verify.</summary>
    public sealed class KitchenDraftRuntimeProbe : MonoBehaviour
    {
        [Serializable] private sealed class CheckResult
        {
            public string name;
            public bool passed;
            public string detail;
        }

        [Serializable] private sealed class ProbeReport
        {
            public string unityVersion;
            public string scene;
            public bool success;
            public CheckResult[] checks;
            public string scope;
        }

        private readonly List<CheckResult> checks = new List<CheckResult>();
        private KitchenDraftController controller;
        private PlayerFollowCamera follow;
        private Camera actualCamera;
        private PlayerMover actor;
        private Rigidbody body;
        private string basePath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Environment.GetCommandLineArgs().Any(a => a == "-map2verify")) return;
            new GameObject("Map2 Runtime 3D Verification").AddComponent<KitchenDraftRuntimeProbe>();
        }

        private void Start() { StartCoroutine(Run()); }

        private IEnumerator Run()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            yield return new WaitForSecondsRealtime(1f);
            bool ready = false;
            try
            {
                // The data directory's grandparent is the versioned draft root in a standalone build.
                basePath = Directory.GetParent(Application.dataPath).Parent.FullName;
                controller = FindFirstObjectByType<KitchenDraftController>();
                follow = FindFirstObjectByType<PlayerFollowCamera>();
                actualCamera = Camera.main;
                var movers = FindObjectsByType<PlayerMover>(FindObjectsSortMode.None);
                actor = movers.FirstOrDefault(m => m.transform == PlayerControlSwitcher.ActiveTarget);
                body = actor != null ? actor.GetComponent<Rigidbody>() : null;
                ready = controller != null && follow != null && actualCamera != null &&
                    actualCamera.GetComponent<PlayerFollowCamera>() == follow && movers.Length == 3 &&
                    actor != null && body != null && controller.chapters != null &&
                    controller.chapters.Length == 7 && controller.chapterStarts != null &&
                    controller.chapterStarts.Length == 7;
                Add("Loaded team scene with seven regions", ready,
                    "controller=" + (controller != null) + ", follow=" + (follow != null) +
                    ", Camera.main=" + (actualCamera != null) + ", movers=" + movers.Length +
                    ", active=" + (actor != null ? actor.name : "missing"));
                if (ready)
                    Add("Initial active player and native camera match",
                        PlayerControlSwitcher.ActiveTarget == actor.transform && follow.target == actor.transform,
                        "switcher=" + Name(PlayerControlSwitcher.ActiveTarget) + ", camera=" + Name(follow.target));
            }
            catch (Exception e) { Add("Runtime setup", false, e.ToString()); }
            if (!ready) { Finish(); yield break; }

            // Actual Rigidbody simulation in both horizontal axes, with gameplay input bypassed.
            bool originalExternal = actor.ExternallyDriven;
            Vector3 originalPosition = body.position;
            Vector3 originalVelocity = body.velocity;
            actor.ExternallyDriven = true;
            body.position = controller.chapterStarts[0].position + Vector3.up;
            body.velocity = Vector3.right * 3f;
            Physics.SyncTransforms();
            Vector3 xStart = body.position;
            yield return new WaitForSecondsRealtime(0.32f);
            float dx = body.position.x - xStart.x;
            Add("Rigidbody physically travels X", dx > 0.4f, "X displacement=" + dx.ToString("F3") + " m");
            body.position = controller.chapterStarts[0].position + Vector3.up;
            body.velocity = Vector3.forward * 3f;
            Physics.SyncTransforms();
            Vector3 zStart = body.position;
            yield return new WaitForSecondsRealtime(0.32f);
            float dz = body.position.z - zStart.z;
            Add("Rigidbody physically travels Z", dz > 0.4f, "Z displacement=" + dz.ToString("F3") + " m");
            body.position = originalPosition;
            body.velocity = originalVelocity;
            actor.ExternallyDriven = originalExternal;
            Physics.SyncTransforms();
            PlayerFollowCamera.SnapToTarget(actor.transform);

            try
            {
                var yaw = typeof(PlayerFollowCamera).GetField("orbitYaw", BindingFlags.Instance | BindingFlags.NonPublic);
                var pitch = typeof(PlayerFollowCamera).GetField("orbitPitch", BindingFlags.Instance | BindingFlags.NonPublic);
                bool fields = yaw != null && pitch != null && follow.enableMouseOrbit;
                if (fields)
                {
                    float oldYaw = (float)yaw.GetValue(follow);
                    float oldPitch = (float)pitch.GetValue(follow);
                    yaw.SetValue(follow, 0f); pitch.SetValue(follow, 0f);
                    PlayerFollowCamera.SnapToTarget(actor.transform);
                    Vector3 viewA = actualCamera.transform.forward;
                    yaw.SetValue(follow, 60f); pitch.SetValue(follow, 20f);
                    PlayerFollowCamera.SnapToTarget(actor.transform);
                    Vector3 viewB = actualCamera.transform.forward;
                    float changed = Vector3.Angle(viewA, viewB);
                    Add("Native camera yaw and pitch alter real view", changed > 15f,
                        "forward angle=" + changed.ToString("F1") + " degrees");
                    yaw.SetValue(follow, oldYaw); pitch.SetValue(follow, oldPitch);
                    PlayerFollowCamera.SnapToTarget(actor.transform);
                }
                else Add("Native camera yaw and pitch alter real view", false, "Private orbit fields or orbit mode missing");
            }
            catch (Exception e) { Add("Native camera yaw and pitch alter real view", false, e.ToString()); }

            try
            {
                var switcher = FindFirstObjectByType<PlayerControlSwitcher>();
                var field = typeof(PlayerControlSwitcher).GetField("activePlayer", BindingFlags.Instance | BindingFlags.NonPublic);
                var apply = typeof(PlayerControlSwitcher).GetMethod("ApplyActive", BindingFlags.Instance | BindingFlags.NonPublic);
                var next = FindObjectsByType<PlayerMover>(FindObjectsSortMode.None).FirstOrDefault(m => m != actor);
                bool methodReady = switcher != null && field != null && apply != null && next != null;
                bool switched = false;
                if (methodReady)
                {
                    field.SetValue(switcher, next);
                    apply.Invoke(switcher, null);
                    switched = PlayerControlSwitcher.ActiveTarget == next.transform && follow.target == next.transform &&
                        FindObjectsByType<PlayerMover>(FindObjectsSortMode.None).Count(m => m.IsControlled) == 1 &&
                        next.IsControlled;
                    field.SetValue(switcher, actor);
                    apply.Invoke(switcher, null);
                }
                Add("Native switcher changes control and camera target", methodReady && switched &&
                    PlayerControlSwitcher.ActiveTarget == actor.transform && follow.target == actor.transform,
                    "reflection API present=" + methodReady + ", switched=" + switched);
            }
            catch (Exception e) { Add("Native switcher changes control and camera target", false, e.ToString()); }

            string imageDirectory = Path.Combine(basePath, "미리보기");
            try { Directory.CreateDirectory(imageDirectory); }
            catch (Exception e) { Add("Preview output directory", false, e.ToString()); }
            for (int i = 0; i < 7; i++)
            {
                bool previewCalled = true;
                try { controller.PreviewChapter(i); }
                catch (Exception e) { previewCalled = false; Add("CH" + (i + 1) + " warp", false, e.ToString()); }
                yield return null;
                yield return null;
                if (!previewCalled) continue;
                try
                {
                    bool ground = GroundUnder(actor.transform.position);
                    bool region = controller.chapters[i] != null && controller.chapters[i].volume != null &&
                        controller.chapters[i].volume.bounds.Contains(actor.transform.position);
                    bool state = controller.CurrentChapter == i && follow.target == actor.transform &&
                        PlayerControlSwitcher.ActiveTarget == actor.transform && ground && region;
                    Add("CH" + (i + 1) + " runtime location and floor", state,
                        "CurrentChapter=" + controller.CurrentChapter + ", position=" + actor.transform.position.ToString("F2") +
                        ", ground=" + ground + ", region=" + region);
                    string path = Path.Combine(imageDirectory, "Runtime_CH" + (i + 1) + ".png");
                    Capture(actualCamera, path);
                    Add("CH" + (i + 1) + " actual-camera PNG", File.Exists(path) && new FileInfo(path).Length > 10000,
                        "path=" + path + ", bytes=" + (File.Exists(path) ? new FileInfo(path).Length : 0));
                }
                catch (Exception e) { Add("CH" + (i + 1) + " runtime preview", false, e.ToString()); }
            }

            bool collectedForDrop = false;
            bool externalBeforeDrop = actor.ExternallyDriven;
            try
            {
                controller.ResetDraft();
                bool reset = !controller.HasKey && !controller.ExitReached && controller.doorBlocker.enabled;
                Vector3 keyAt = controller.key.position;
                body.position = keyAt + new Vector3(0.2f, 0f, 0.2f);
                actor.transform.position = body.position;
                body.velocity = Vector3.zero;
                Physics.SyncTransforms();
                float actualDistance = Vector3.Distance(actor.transform.position, keyAt);
                bool enabledBefore = actor.isActiveAndEnabled;
                bool hasKeyBefore = controller.HasKey;
                collectedForDrop = controller.TryCollectKey(actor.transform);
                Add("Runtime near key opens gate", reset && collectedForDrop && controller.HasKey &&
                    !controller.doorBlocker.enabled,
                    "reset=" + reset + ", collected=" + collectedForDrop +
                    ", actualDistance=" + actualDistance.ToString("F3") +
                    ", actorEnabled=" + enabledBefore + ", HasKeyBefore=" + hasKeyBefore);
                if (collectedForDrop)
                {
                    actor.ExternallyDriven = true;
                    Vector3 dropStart = new Vector3(-30.2f, 7.4f, -10f);
                    body.position = dropStart;
                    actor.transform.position = dropStart;
                    body.velocity = Vector3.zero;
                    Physics.SyncTransforms();
                }
            }
            catch (Exception e) { Add("Runtime key and drop setup", false, e.ToString()); }
            if (collectedForDrop)
            {
                yield return new WaitForSecondsRealtime(1.6f);
                try
                {
                    Add("Real gravity drop reaches lower exit trigger", controller.HasKey &&
                        controller.ExitReached && body.position.y < 2.5f,
                        "HasKey=" + controller.HasKey + ", ExitReached=" + controller.ExitReached +
                        ", landingY=" + body.position.y.ToString("F2") +
                        ", start=(-30.2,7.4,-10); no NotifyExit call");
                }
                catch (Exception e) { Add("Real gravity drop reaches lower exit trigger", false, e.ToString()); }
            }
            actor.ExternallyDriven = externalBeforeDrop;
            try
            {
                controller.ResetDraft();
                Add("Runtime reset closes gate", !controller.HasKey && !controller.ExitReached &&
                    controller.doorBlocker.enabled && controller.key.gameObject.activeSelf,
                    "HasKey=" + controller.HasKey + ", blocker=" + controller.doorBlocker.enabled);
            }
            catch (Exception e) { Add("Runtime reset after drop", false, e.ToString()); }
            Finish();
        }

        private static bool GroundUnder(Vector3 at)
        {
            foreach (var hit in Physics.RaycastAll(at + Vector3.up * 0.2f, Vector3.down, 6f))
                if (hit.collider != null && !hit.collider.isTrigger &&
                    hit.collider.GetComponentInParent<PlayerMover>() == null) return true;
            return false;
        }

        private static void Capture(Camera camera, string path)
        {
            RenderTexture previous = camera.targetTexture;
            RenderTexture oldActive = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(960, 540, 24);
            Texture2D image = null;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image = new Texture2D(960, 540, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(target);
                if (image != null) Destroy(image);
            }
        }

        private void Add(string name, bool passed, string detail)
        {
            checks.Add(new CheckResult { name = name, passed = passed, detail = detail });
            Debug.Log("[Map2 3D Probe] " + (passed ? "PASS " : "FAIL ") + name + ": " + detail);
        }

        private static string Name(Transform target) => target != null ? target.name : "null";

        private void Finish()
        {
            bool success = checks.Count > 0 && checks.All(c => c.passed);
            var report = new ProbeReport
            {
                unityVersion = Application.unityVersion,
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                success = success,
                checks = checks.ToArray(),
                scope = "Built-player PlayMode geometry, native camera state, switcher method, 3D Rigidbody motion with PlayerMover.ExternallyDriven=true, chapter warps, rendered camera frames, key API and real gravity fall into the lower exit trigger. Rigidbody tests bypass gameplay input. Actual keyboard, mouse motion, Tab key, pursuit AI and complete manual traversal remain unverified. Runs only under -map2verify."
            };
            try
            {
                if (string.IsNullOrEmpty(basePath)) basePath = Directory.GetParent(Application.dataPath).Parent.FullName;
                string dir = Path.Combine(basePath, "검증");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "runtime_3d_report.json");
                File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                Debug.Log("[Map2 3D Probe] " + (success ? "PASS" : "FAIL") + " report=" + path);
            }
            catch (Exception e) { success = false; Debug.LogError("[Map2 3D Probe] Could not write report: " + e); }
            Application.Quit(success ? 0 : 1);
        }
    }
}
