using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Map2KitchenDraft
{
    /// <summary>
    /// Opt-in Editor Play Mode integration checks for confirmed Map 2 behavior.
    /// Actors are staged synthetically; the actual scene components, Rigidbody and
    /// scene transition run in Play Mode. This is not a manual traversal test.
    /// </summary>
    public sealed class KitchenConfirmedProbe : MonoBehaviour
    {
        [Serializable] private sealed class Check
        {
            public string name;
            public bool passed;
            public string detail;
            public int startFrame;
            public int endFrame;
            public float elapsedSeconds;
        }

        [Serializable] private sealed class Report
        {
            public string unityVersion;
            public string initialScene;
            public string finalScene;
            public string executionEnvironment;
            public string scope;
            public int totalFrames;
            public float totalSeconds;
            public bool success;
            public Check[] checks;
        }

        private struct Mark
        {
            public int frame;
            public float time;
        }

        private const string Map2Scene = "Map2KitchenPlayable";
        private const string Map3Scene = "Map3_ToyWorld";
        private const string Map3ScenePath = "Assets/Scenes/Map3_ToyWorld/Map3_ToyWorld.unity";
        private readonly List<Check> checks = new List<Check>();
        private KitchenDraftController draft;
        private KitchenMapHandoff handoff;
        private PlayerMover actor;
        private Rigidbody body;
        private PlayerMover alternate;
        private KitchenPursuit[] pursuits;
        private KitchenWashHazards laser;
        private string root;
        private int startFrame;
        private float startTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Environment.GetCommandLineArgs().Contains("-map2confirmedverify")) return;
            if (SceneManager.GetActiveScene().name != Map2Scene) return;
            var runner = new GameObject("Map2 Confirmed Play Verification");
            DontDestroyOnLoad(runner);
            runner.AddComponent<KitchenConfirmedProbe>();
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            startFrame = Time.frameCount;
            startTime = Time.realtimeSinceStartup;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            yield return null;
            root = Directory.GetParent(Application.dataPath).Parent.FullName;
            draft = FindFirstObjectByType<KitchenDraftController>();
            handoff = FindFirstObjectByType<KitchenMapHandoff>();
            laser = FindFirstObjectByType<KitchenWashHazards>();
            pursuits = FindObjectsByType<KitchenPursuit>(FindObjectsSortMode.None);
            actor = FindObjectsByType<PlayerMover>(FindObjectsSortMode.None)
                .FirstOrDefault(m => m.transform == PlayerControlSwitcher.ActiveTarget);
            alternate = FindObjectsByType<PlayerMover>(FindObjectsSortMode.None)
                .FirstOrDefault(m => m != actor);
            body = actor != null ? actor.GetComponent<Rigidbody>() : null;

            Mark setup = Now();
            bool ready = draft != null && handoff != null && laser != null && body != null &&
                pursuits != null && pursuits.Length >= 4 &&
                SceneManager.GetActiveScene().name == Map2Scene;
            Record("Playable scene wires confirmed components", ready,
                "draft=" + (draft != null) + ", handoff=" + (handoff != null) +
                ", laser=" + (laser != null) + ", active Rigidbody=" + (body != null) +
                ", pursuers=" + (pursuits != null ? pursuits.Length : 0), setup);
            if (!ready) { Finish(); yield break; }

            bool externalBefore = actor.ExternallyDriven;
            actor.ExternallyDriven = true;
            draft.ResetDraft();
            yield return new WaitForFixedUpdate();

            yield return StartCoroutine(CheckPursuers());
            yield return StartCoroutine(CheckWater());
            yield return StartCoroutine(CheckLaser());
            yield return StartCoroutine(CheckKeyAndHandoff());

            if (actor != null) actor.ExternallyDriven = externalBefore;
            Finish();
        }

        private IEnumerator CheckPursuers()
        {
            for (int chapter = 0; chapter < 6; chapter++)
            {
                if (chapter != 0 && chapter != 2 && chapter != 4 && chapter != 5) continue;
                CreatureKind kind = chapter == 4 ? CreatureKind.Cockroach : CreatureKind.Rat;
                KitchenPursuit pursuit = pursuits.FirstOrDefault(p => p != null && p.Kind == kind &&
                    p.ChapterIndex == chapter);
                Mark mark = Now();
                if (pursuit == null || pursuit.route == null || pursuit.route.Length < 2)
                {
                    Record("CH" + (chapter + 1) + " creature follows authored route", false,
                        "Missing " + kind + " route", mark);
                    continue;
                }

                draft.PreviewChapter(chapter);
                pursuit.ResetPursuit();
                if (chapter == 0)
                {
                    mark = Now();
                    Vector3 idleStart = pursuit.transform.position;
                    int idleTicks = Mathf.CeilToInt((pursuit.reactionDelay + .3f) /
                        Time.fixedDeltaTime);
                    for (int i = 0; i < idleTicks; i++) yield return new WaitForFixedUpdate();
                    yield return null;
                    Record("CH1 rat waits while player is at the entry",
                        !pursuit.IsChasing && pursuit.CurrentPathDistance < .02f &&
                        Vector3.Distance(idleStart, pursuit.transform.position) < .02f,
                        "chasing=" + pursuit.IsChasing + ", path=" +
                        pursuit.CurrentPathDistance.ToString("F3") + "m, displacement=" +
                        Vector3.Distance(idleStart, pursuit.transform.position).ToString("F3") +
                        "m", mark);
                }
                // Use the flat leg before the sink corner for the basin chase.
                float targetDistance = kind == CreatureKind.Cockroach ? 17f :
                    Mathf.Min(pursuit.PathLength - .4f,
                        Mathf.Max(pursuit.spawnGap + 2.5f, pursuit.PathLength * .72f));
                Vector3 target = PointOnRoute(pursuit.route, Mathf.Max(0f, targetDistance));
                Teleport(actor, target + Vector3.up * (kind == CreatureKind.Cockroach ? .55f : .15f));
                yield return new WaitForFixedUpdate();
                yield return null; // Pursuit arming is in Update.
                float armedDistance = pursuit.CurrentPathDistance;
                float observedQuietSeconds = 0f;
                if (chapter == 0 || kind == CreatureKind.Cockroach)
                {
                    mark = Now();
                    int quietTicks = Mathf.FloorToInt(1f / Time.fixedDeltaTime);
                    for (int i = 0; i < quietTicks; i++) yield return new WaitForFixedUpdate();
                    observedQuietSeconds = quietTicks * Time.fixedDeltaTime;
                    float earlyMovement = Mathf.Abs(pursuit.CurrentPathDistance - armedDistance);
                    bool visible = pursuit.GetComponentsInChildren<Renderer>(true)
                        .Any(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy);
                    bool startsBehind = chapter == 0 ? targetDistance - armedDistance >= 8f :
                        armedDistance < .1f;
                    Record((chapter == 0 ? "CH1 rat" : "CH5 cockroach") +
                        " gives a visible one-second escape window",
                        earlyMovement < .02f && !pursuit.IsChasing && visible && startsBehind,
                        "distance moved=" + earlyMovement.ToString("F3") + "m in " +
                        observedQuietSeconds.ToString("F2") + "s, chasing=" +
                        pursuit.IsChasing + ", visible=" + visible + ", starts behind=" +
                        startsBehind + ", player=" + actor.transform.position.ToString("F2"), mark);
                }

                // Measure movement over fixed steps after the reaction interval.
                // A configured speed alone does not prove the creature moves slowly.
                int settleTicks = Mathf.CeilToInt((Mathf.Max(0f,
                    pursuit.reactionDelay - observedQuietSeconds) + .2f) / Time.fixedDeltaTime);
                for (int i = 0; i < settleTicks; i++) yield return new WaitForFixedUpdate();
                float startDistance = pursuit.CurrentPathDistance;
                const int sampleTicks = 30;
                for (int i = 0; i < sampleTicks; i++) yield return new WaitForFixedUpdate();
                float travelled = pursuit.CurrentPathDistance - startDistance;
                float measuredSpeed = travelled / (sampleTicks * Time.fixedDeltaTime);
                bool bounded = pursuit.CurrentPathDistance <= pursuit.PathLength + .01f &&
                    (kind != CreatureKind.Cockroach ||
                     pursuit.marker.activityBounds.bounds.Contains(pursuit.transform.position));
                Record("CH" + (chapter + 1) + " " + kind + " moves on its route",
                    travelled > .25f && measuredSpeed < 3.5f && pursuit.IsChasing && bounded,
                    "start=" + startDistance.ToString("F2") + "m, end=" +
                    pursuit.CurrentPathDistance.ToString("F2") + "m, route=" +
                    pursuit.PathLength.ToString("F2") + "m, observed speed=" +
                    measuredSpeed.ToString("F2") + "m/s, chasing=" + pursuit.IsChasing +
                    ", inside activity=" + bounded, mark);

                if (kind == CreatureKind.Cockroach)
                {
                    mark = Now();
                    Bounds bounds = pursuit.marker.activityBounds.bounds;
                    Teleport(actor, bounds.center + Vector3.right * (bounds.extents.x + 12f));
                    for (int i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
                    Record("Cockroach stops outside sink activity area",
                        !pursuit.IsChasing && bounds.Contains(pursuit.transform.position),
                        "chasing=" + pursuit.IsChasing + ", creature=" +
                        pursuit.transform.position.ToString("F2"), mark);
                }
                if (chapter == 5)
                {
                    mark = Now();
                    Teleport(actor, new Vector3(43.5f, 9.25f, 100f));
                    for (int i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
                    bool ended = pursuit.HasReachedEnd && !pursuit.IsChasing &&
                        pursuit.transform.position.x <= 42.05f;
                    Teleport(actor, target + Vector3.up * .15f);
                    for (int i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
                    Record("CH6 rat stops at sorting and stays stopped", ended &&
                        pursuit.HasReachedEnd && !pursuit.IsChasing,
                        "ended at sorting=" + ended + ", still stopped=" + !pursuit.IsChasing, mark);
                }
            }

            Mark switchMark = Now();
            bool switched = TrySwitchTo(alternate);
            bool controlCorrect = switched && PlayerControlSwitcher.ActiveTarget == alternate.transform &&
                alternate.IsControlled && !actor.IsControlled &&
                FindObjectsByType<PlayerMover>(FindObjectsSortMode.None).Count(p => p.IsControlled) == 1;
            bool restored = TrySwitchTo(actor);
            Record("Native shape switching keeps one active target",
                controlCorrect && restored && PlayerControlSwitcher.ActiveTarget == actor.transform,
                "other controlled=" + controlCorrect + ", restored=" + restored, switchMark);
        }

        private IEnumerator CheckWater()
        {
            Mark mark = Now();
            var sterilizer = FindFirstObjectByType<KitchenSterilizer>();
            if (sterilizer == null || sterilizer.JetCount != 12)
            {
                Record("CH2 has 12 timed jets", false, "Missing or incorrectly configured sterilizer", mark);
                yield break;
            }
            draft.PreviewChapter(1);
            yield return new WaitForFixedUpdate();
            var jets = Enumerable.Range(0, sterilizer.JetCount).Select(sterilizer.GetJet).ToArray();
            Vector3[] starts = jets.Select(jet => jet.spray.WorldStart).ToArray();
            bool timed = jets.Select(jet => jet.phase).Distinct().Count() == 12;
            var travel = new float[jets.Length];
            for (int tick = 0; tick < 40; tick++)
            {
                yield return new WaitForFixedUpdate();
                for (int i = 0; i < jets.Length; i++)
                {
                    Vector3 delta = jets[i].spray.WorldStart - starts[i];
                    travel[i] = Mathf.Max(travel[i], Mathf.Abs(delta.x));
                    timed &= Mathf.Abs(delta.y) < .001f && Mathf.Abs(delta.z) < .001f;
                }
            }
            for (int i = 0; i < jets.Length; i++)
                timed &= travel[i] > .05f && !jets[i].spray.TracksPlayer;
            Record("CH2 has 12 staggered non-tracking jets that sweep sideways", timed,
                "jets=" + jets.Length + ", distinct phases=" + jets.Select(jet => jet.phase).Distinct().Count(), mark);

            mark = Now();
            KitchenSterilizer.Jet target = jets[7];
            int before = sterilizer.HitCount;
            Teleport(actor, new Vector3(target.spray.WorldStart.x, 8.9f, target.center.z));
            for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate();
            Vector3 recovered = actor.transform.position - target.recovery.position;
            Record("CH2 contact returns the controlled shape to its local safe point",
                sterilizer.HitCount == before + 1 && new Vector2(recovered.x, recovered.z).magnitude < .15f,
                "hits=" + (sterilizer.HitCount - before) + ", recovery=" + actor.transform.position.ToString("F2"), mark);
        }

        private IEnumerator CheckLaser()
        {
            Mark mark = Now();
            draft.PreviewChapter(3);
            bool externalBefore = actor.ExternallyDriven;
            actor.ExternallyDriven = false; // Sensor rejects externally driven review actors.
            // Sample the actual ramp collider, below the upper cabinet, where a player walks.
            RaycastHit floor = Physics.RaycastAll(new Vector3(-48f, 25f, 72f), Vector3.down,
                    25f, ~0, QueryTriggerInteraction.Ignore)
                .FirstOrDefault(hit => hit.collider.name == "Map2 Sink North Entry Ramp");
            bool found = floor.collider != null;
            if (found)
            {
                Teleport(actor, floor.point + Vector3.up * .6f);
                body.velocity = Vector3.right * 2.5f;
                yield return null; // Let the real LateUpdate aim the beam, including in batch mode.
            }
            bool tracks = found && laser.IsTracking && laser.CurrentTarget == actor.transform &&
                Vector3.Distance(laser.BeamEnd, laser.transform.position) > .1f;
            Record("CH4 moving selected shape activates aiming beam", tracks,
                "ramp sample=" + found + ", tracking=" + laser.IsTracking +
                ", beam end=" + laser.BeamEnd.ToString("F2"), mark);

            mark = Now();
            body.velocity = Vector3.zero;
            yield return null;
            bool stopped = !laser.TryGetMovingVisibleTarget(actor.transform, out _) && !laser.IsTracking;
            Record("CH4 beam ignores a stationary shape", stopped,
                "tracking=" + laser.IsTracking, mark);
            actor.ExternallyDriven = externalBefore;
        }

        private IEnumerator CheckKeyAndHandoff()
        {
            Mark mark = Now();
            draft.ResetDraft();
            Teleport(actor, new Vector3(-62f, 5.3f, -120f));
            for (int i = 0; i < 90; i++) yield return new WaitForFixedUpdate();
            bool noKeyStays = SceneManager.GetActiveScene().name == Map2Scene &&
                !draft.ExitReached && !handoff.IsLoading && !handoff.HasCrossedUpperExit;
            Record("Lower fall without key stays in Map2", noKeyStays,
                "scene=" + SceneManager.GetActiveScene().name + ", exit=" + draft.ExitReached +
                ", upper crossed=" + handoff.HasCrossedUpperExit, mark);

            mark = Now();
            draft.ResetDraft();
            bool closed = !draft.HasKey && draft.doorBlocker != null && draft.doorBlocker.enabled;
            Teleport(actor, draft.key.position + new Vector3(.2f, 0f, .1f));
            bool collected = draft.TryCollectKey(actor.transform);
            bool opened = collected && draft.HasKey && !draft.doorBlocker.enabled &&
                !draft.key.gameObject.activeSelf;
            Record("Key interaction opens upper door", closed && opened,
                "closed before=" + closed + ", collected=" + collected +
                ", open after=" + opened, mark);

            mark = Now();
            KitchenPursuit[] rats = pursuits.Where(p => p != null && p.Kind == CreatureKind.Rat).ToArray();
            for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            bool ratsStopped = rats.Length >= 3 && rats.All(p => !p.IsChasing && p.HasReachedEnd);
            Record("Key ends all rat pursuit", ratsStopped,
                "rats=" + rats.Length + ", stopped=" +
                rats.Count(p => !p.IsChasing && p.HasReachedEnd), mark);

            mark = Now();
            Teleport(actor, new Vector3(-62f, 5.3f, -120f));
            for (int i = 0; i < 90; i++) yield return new WaitForFixedUpdate();
            bool keyAloneStays = SceneManager.GetActiveScene().name == Map2Scene &&
                !draft.ExitReached && !handoff.IsLoading && !handoff.HasCrossedUpperExit;
            Record("Key alone cannot skip upper exit route", keyAloneStays,
                "scene=" + SceneManager.GetActiveScene().name +
                ", upper crossed=" + handoff.HasCrossedUpperExit, mark);

            mark = Now();
            Teleport(actor, new Vector3(-47.1f, 16.75f, -120f));
            bool startedBeforeUpperExit = !handoff.HasCrossedUpperExit;
            for (int i = 0; i < 130 && body.position.x > -54f; i++)
            {
                Vector3 velocity = body.velocity;
                velocity.x = -5f;
                body.velocity = velocity;
                yield return new WaitForFixedUpdate();
            }
            body.velocity = Vector3.zero;
            bool armed = startedBeforeUpperExit && handoff.HasCrossedUpperExit && body.position.x < -53f;
            Record("Active shape crosses open upper exit", armed,
                "upper crossed=" + armed + ", position=" + body.position.ToString("F2"), mark);

            mark = Now();
            yield return null;
            bool dispatchShowsChapter7 = armed && draft.CurrentChapter == 6;
            Record("Food dispatch keeps CH7 until Map3 handoff", dispatchShowsChapter7,
                "chapter=" + (draft.CurrentChapter + 1) + ", position=" + body.position.ToString("F2"), mark);

            if (alternate != null && armed)
            {
                mark = Now();
                Teleport(alternate, new Vector3(-62f, 5.3f, -120f));
                for (int i = 0; i < 90; i++) yield return new WaitForFixedUpdate();
                bool inactiveStays = SceneManager.GetActiveScene().name == Map2Scene &&
                    !draft.ExitReached && !handoff.IsLoading;
                Record("Inactive shape cannot trigger Map3 transition", inactiveStays,
                    "scene=" + SceneManager.GetActiveScene().name +
                    ", loading=" + handoff.IsLoading, mark);
            }

            mark = Now();
            int waited = 0;
            Vector3 startOfDrop = body.position;
            while (SceneManager.GetActiveScene().name == Map2Scene && waited++ < 240)
            {
                if (body != null && body.position.x > -61.3f)
                {
                    Vector3 velocity = body.velocity;
                    velocity.x = -5f;
                    body.velocity = velocity;
                }
                else if (body != null)
                {
                    Vector3 velocity = body.velocity;
                    velocity.x = 0f;
                    body.velocity = velocity;
                }
                yield return new WaitForFixedUpdate();
            }
            Scene destination = SceneManager.GetActiveScene();
            bool loaded = destination.path == Map3ScenePath;
            Record("Rigidbody fall loads real Map3 scene", loaded,
                "scene=" + destination.path + ", departed ledge=" + startOfDrop.ToString("F2") +
                ", waited fixed frames=" + waited, mark);

            mark = Now();
            if (loaded)
            {
                for (int i = 0; i < 35; i++) yield return new WaitForFixedUpdate();
                PlayerMover[] map3Movers = FindObjectsByType<PlayerMover>(FindObjectsSortMode.None);
                PlayerFollowCamera[] follows = FindObjectsByType<PlayerFollowCamera>(FindObjectsSortMode.None);
                Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                bool ownTeam = map3Movers.Length == 3 && follows.Length == 1 && cameras.Length == 1 &&
                    map3Movers.All(m => m.gameObject.scene == SceneManager.GetActiveScene()) &&
                    PlayerControlSwitcher.ActiveTarget != null &&
                    PlayerControlSwitcher.ActiveTarget.gameObject.scene == SceneManager.GetActiveScene() &&
                    map3Movers.Count(m => m.IsControlled) == 1 &&
                    follows[0].target == PlayerControlSwitcher.ActiveTarget;
                Record("Map3 starts with its own three shapes and one camera", ownTeam,
                    "movers=" + map3Movers.Length + ", follows=" + follows.Length +
                    ", cameras=" + cameras.Length + ", active=" +
                    (PlayerControlSwitcher.ActiveTarget != null ? PlayerControlSwitcher.ActiveTarget.name : "none"), mark);
            }
            else Record("Map3 starts with its own three shapes and one camera", false,
                "Map3 did not load", mark);
        }

        private void Teleport(PlayerMover mover, Vector3 destination)
        {
            Rigidbody rigidbody = mover.GetComponent<Rigidbody>();
            rigidbody.velocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            rigidbody.position = destination;
            mover.transform.position = destination;
            Physics.SyncTransforms();
            if (mover == actor) PlayerFollowCamera.SnapToTarget(mover.transform);
        }

        private static Vector3 PointOnRoute(Vector3[] route, float distance)
        {
            for (int i = 1; i < route.Length; i++)
            {
                float span = Vector3.Distance(route[i - 1], route[i]);
                if (distance <= span) return Vector3.Lerp(route[i - 1], route[i],
                    span > .001f ? distance / span : 0f);
                distance -= span;
            }
            return route[route.Length - 1];
        }

        private static bool TrySwitchTo(PlayerMover mover)
        {
            PlayerControlSwitcher switcher = FindFirstObjectByType<PlayerControlSwitcher>();
            FieldInfo field = typeof(PlayerControlSwitcher).GetField("activePlayer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo apply = typeof(PlayerControlSwitcher).GetMethod("ApplyActive",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (switcher == null || mover == null || field == null || apply == null) return false;
            field.SetValue(switcher, mover);
            apply.Invoke(switcher, null);
            return PlayerControlSwitcher.ActiveTarget == mover.transform;
        }

        private static Mark Now() => new Mark { frame = Time.frameCount, time = Time.realtimeSinceStartup };

        private void Record(string name, bool passed, string detail, Mark mark)
        {
            checks.Add(new Check
            {
                name = name, passed = passed, detail = detail,
                startFrame = mark.frame, endFrame = Time.frameCount,
                elapsedSeconds = Time.realtimeSinceStartup - mark.time
            });
            Debug.Log("[Map2 Confirmed Probe] " + (passed ? "PASS " : "FAIL ") + name + ": " + detail);
        }

        private void Finish()
        {
            bool success = checks.Count > 0 && checks.All(c => c.passed);
            var report = new Report
            {
                unityVersion = Application.unityVersion,
                initialScene = Map2Scene,
                finalScene = SceneManager.GetActiveScene().name,
                executionEnvironment = Application.isEditor ? "Unity Editor Play Mode" : "Built player",
                scope = "Editor probe using staged actor positions, Rigidbody motion, the upper and lower triggers, and actual scene loading.",
                totalFrames = Time.frameCount - startFrame,
                totalSeconds = Time.realtimeSinceStartup - startTime,
                success = success,
                checks = checks.ToArray()
            };
            try
            {
                string path = Path.Combine(root, "검증", "confirmed_playmode.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                Debug.Log("[Map2 Confirmed Probe] " + (success ? "PASS" : "FAIL") + " report=" + path);
            }
            catch (Exception e) { success = false; Debug.LogError("Report write failed: " + e); }
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
    }
}
