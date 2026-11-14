using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Map2KitchenDraft.Editor
{
    /// <summary>
    /// Measures the saved rebuild scene through its physics colliders.  The route
    /// graph is sampled from the scene and never consults the builder's route data.
    /// It checks ordinary walking/jumping reachability; it cannot prove that a
    /// skilled player cannot exploit shape stacking or a future gimmick.
    /// </summary>
    public static class KitchenRebuildVerification
    {
        [Serializable] private sealed class Check
        {
            public string name;
            public bool passed;
            public string detail;
        }

        [Serializable] private sealed class Report
        {
            public string scene;
            public string unityVersion;
            public bool success;
            public string scope = "Saved-scene colliders, raycasts and sampled walk graph. Manual keyboard play and advanced cooperative exploits are outside this audit.";
            public Check[] checks;
        }

        private const string ScenePath = "Assets/Scenes/Map2_Kitchen/Rebuild/Scenes/Map2KitchenDraft.unity";
        private const string BeltPrefix = "Map2 V3 Belt Segment";
        private static readonly List<Check> Checks = new List<Check>();
        private static readonly RaycastHit[] HitBuffer = new RaycastHit[128];
        private static readonly Collider[] OverlapBuffer = new Collider[128];

        [MenuItem("Tools/Map2 Kitchen Rebuild/2 Verify Saved Scene")]
        public static void Run()
        {
            RunForScene(ScenePath, "rebuild_acceptance.json");
        }

        internal static void RunForScene(string scenePath, string reportName)
        {
            Checks.Clear();
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            try
            {
                Physics.SyncTransforms();
                VerifyNativeTeam();
                VerifyReviewSpawnClearance();
                VerifyExtent();
                VerifyBeltCoverage();
                VerifySinkCutout();
                VerifyWalkGraph();
                VerifyKeyAndDrop();
            }
            catch (Exception e) { Add("Verification completed without exception", false, e.ToString()); }
            finally { EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single); }
            var report = new Report
            {
                scene = scenePath,
                unityVersion = Application.unityVersion,
                success = Checks.Count > 0 && Checks.All(c => c.passed),
                checks = Checks.ToArray()
            };
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "검증", reportName));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            if (!report.success) throw new InvalidOperationException("Map2 rebuild scene audit failed: " + output);
        }

        private static void VerifyNativeTeam()
        {
            var movers = UnityEngine.Object.FindObjectsByType<PlayerMover>(FindObjectsSortMode.None);
            var cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            var follow = UnityEngine.Object.FindObjectsByType<PlayerFollowCamera>(FindObjectsSortMode.None);
            var controller = UnityEngine.Object.FindFirstObjectByType<KitchenDraftController>();
            bool pass = movers.Length == 3 && cams.Length == 1 && follow.Length == 1 &&
                cams[0].GetComponent<PlayerFollowCamera>() == follow[0] &&
                !cams[0].orthographic && follow[0].enableMouseOrbit &&
                movers.All(p => p.cameraRelativeInput &&
                    (p.GetComponent<Rigidbody>().constraints & RigidbodyConstraints.FreezePositionX) == 0) &&
                controller != null && controller.chapters != null && controller.chapters.Length == 7 &&
                controller.chapterStarts != null && controller.chapterStarts.Length == 7;
            Add("Native three-shape 3D camera and movement", pass,
                "movers=" + movers.Length + ", cameras=" + cams.Length + ", orbit=" + follow.Length +
                ", seven regions=" + (controller != null && controller.chapters != null ? controller.chapters.Length : 0));
        }

        private static void VerifyReviewSpawnClearance()
        {
            // Chapter warps move one selected native shape to each saved marker.
            // ComputePenetration tests the exact collider pose at that destination
            // without modifying the saved scene or the other two shapes.
            var controller = UnityEngine.Object.FindFirstObjectByType<KitchenDraftController>();
            var movers = UnityEngine.Object.FindObjectsByType<PlayerMover>(FindObjectsSortMode.None);
            if (controller == null || controller.chapterStarts == null ||
                controller.chapterStarts.Length != 7 || movers.Length != 3)
            {
                Add("Review spawn clearance prerequisites", false,
                    "Expected seven markers and all three native movers");
                return;
            }
            var world = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                .Where(c => c.enabled && !c.isTrigger && c.GetComponentInParent<PlayerMover>() == null)
                .ToArray();
            for (int chapter = 0; chapter < 7; chapter++)
            {
                Transform marker = controller.chapterStarts[chapter];
                var problems = new List<string>();
                int comparisons = 0;
                if (marker == null)
                {
                    Add("CH" + (chapter + 1) + " native shape spawn clearance", false,
                        "Saved review marker is missing");
                    continue;
                }
                foreach (PlayerMover mover in movers)
                {
                    Collider[] bodyColliders = mover.GetComponentsInChildren<Collider>(true)
                        .Where(c => c.enabled && !c.isTrigger).ToArray();
                    if (bodyColliders.Length == 0)
                    {
                        problems.Add(mover.name + " has no enabled solid collider");
                        continue;
                    }
                    Vector3 displacement = marker.position - mover.transform.position;
                    foreach (Collider body in bodyColliders)
                    {
                        Vector3 bodyAtMarker = body.transform.position + displacement;
                        Bounds candidate = body.bounds;
                        candidate.center += displacement;
                        candidate.Expand(.20f);
                        foreach (Collider solid in world)
                        {
                            if (!candidate.Intersects(solid.bounds)) continue;
                            comparisons++;
                            if (!Physics.ComputePenetration(body, bodyAtMarker, body.transform.rotation,
                                    solid, solid.transform.position, solid.transform.rotation,
                                    out Vector3 direction, out float depth)) continue;
                            if (depth > .08f && problems.Count < 8)
                                problems.Add(mover.name + "/" + body.name + " intersects " + solid.name +
                                    " by " + depth.ToString("F3") + " m toward " + direction.ToString("F2"));
                        }
                    }
                }
                Add("CH" + (chapter + 1) + " native shape spawn clearance", problems.Count == 0,
                    "3 shapes, candidate intersections=" + comparisons +
                    (problems.Count == 0 ? ", clear" : ", " + string.Join("; ", problems)));
            }
        }

        private static void VerifyExtent()
        {
            // These are actual walkable top surfaces, not the enlarged room walls.
            Vector3[] points = {
                new Vector3(-118, 0.5f, -112), new Vector3(-118, 8, 100),
                new Vector3(-48, 1, 52), new Vector3(128, 8, 100),
                new Vector3(128, 8, -120), new Vector3(-48, 8, -120)
            };
            var found = new List<Vector3>();
            foreach (var p in points)
            {
                Surface s = FloorAt(p.x, p.z, p.y, 1.8f, false);
                if (s.valid) found.Add(new Vector3(p.x, s.y, p.z));
            }
            // The elevated exit must have its own walkable deck, not only a tall prop.
            Surface upper = FloorAt(-44.5f, -120, 16f, 1.3f, false);
            if (upper.valid) found.Add(new Vector3(-44.5f, upper.y, -120));
            float spanX = found.Count > 0 ? found.Max(p => p.x) - found.Min(p => p.x) : 0;
            float spanZ = found.Count > 0 ? found.Max(p => p.z) - found.Min(p => p.z) : 0;
            float spanY = found.Count > 0 ? found.Max(p => p.y) - found.Min(p => p.y) : 0;
            Add("Playable floor footprint grows at least 3x on X and Z", found.Count >= 6 &&
                spanX >= 237f && spanZ >= 216f,
                "supported=" + found.Count + "/" + points.Length + "+upper, X=" + spanX.ToString("F1") +
                " m, Z=" + spanZ.ToString("F1") + " m");
            Add("Playable floor height range grows at least 2x", upper.valid &&
                upper.y >= 14.8f && spanY >= 12.8f,
                "upper=" + (upper.valid ? upper.y.ToString("F1") : "missing") +
                " m, Y span=" + spanY.ToString("F1") + " m");
        }

        private static void VerifyBeltCoverage()
        {
            Collider[] belts = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                .Where(IsBeltBase).ToArray();
            bool credible = belts.Length >= 9 && belts.All(c => !c.isTrigger &&
                (c is MeshCollider || c.bounds.size.y <= 2.2f));
            Add("Many distinct solid restaurant belt segments", credible,
                "physical belt bases=" + belts.Length);
            // Sample the white drawing's complete line, including its bends.
            BeltLine("Entry climb", belts, V(-118,-112), V(-118,-75));
            BeltLine("Left collection run", belts, V(-118,-75), V(-118,100));
            BeltLine("Upper-left turn", belts, V(-118,100), V(-48,100));
            BeltLine("Sink approach", belts, V(-48,100), V(-48,82));
            BeltLine("Sink lower leg", belts, V(-48,50), V(-48,26));
            BeltLine("Lower inner elbow", belts, V(-48,26), V(12,26));
            BeltLine("Inner rising run", belts, V(12,26), V(12,100));
            BeltLine("Dish storage approach", belts, V(12,100), V(42,100));
            BeltLine("Dish storage return", belts, V(70,100), V(128,100));
            BeltLine("Right long return", belts, V(128,100), V(128,-120));
            BeltLine("Cooking return", belts, V(128,-120), V(-48,-120));
            bool sinkGap = new[] { 76f, 70f, 64f, 58f, 52f }.All(z => !BeltAt(-48, z, belts));
            bool dishGap = new[] { 46f, 50f, 54f, 58f, 62f, 66f }.All(x => !BeltAt(x, 100, belts));
            Add("Only drawn sink and dish-storage breaks lack belt", sinkGap && dishGap,
                "sink gap clear=" + sinkGap + ", dish gap clear=" + dishGap);
        }

        private static void BeltLine(string name, Collider[] belts, Vector2 a, Vector2 b)
        {
            int total = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(a, b) / 2.5f));
            int hits = 0;
            for (int i = 0; i <= total; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)total);
                if (BeltAt(p.x, p.y, belts)) hits++;
            }
            float ratio = hits / (float)(total + 1);
            Add(name + " belt covers the sketch line", hits == total + 1,
                hits + "/" + (total + 1) + " samples (" + (ratio * 100f).ToString("F1") + "%)");
        }

        private static bool BeltAt(float x, float z, Collider[] belts)
        {
            var ray = new Ray(new Vector3(x, 22, z), Vector3.down);
            foreach (var belt in belts)
                if (belt.Raycast(ray, out RaycastHit hit, 35f) && hit.normal.y > 0.45f) return true;
            return false;
        }

        private static bool IsBeltBase(Collider candidate)
        {
            if (!candidate.enabled) return false;
            if (candidate is MeshCollider &&
                (candidate.name == KitchenRebuildBuilder.EntrySurfaceName ||
                 candidate.name == KitchenRebuildBuilder.SinkSurfaceName))
                return true;
            if (!(candidate is BoxCollider)) return false;
            for (Transform t = candidate.transform; t != null; t = t.parent)
                if (t.name.StartsWith(BeltPrefix, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void VerifySinkCutout()
        {
            var floor = GameObject.Find("Map2 Sink Basin Floor");
            var entry = GameObject.Find("Map2 Sink North Entry Ramp");
            var exit = GameObject.Find("Map2 Sink South Exit");
            Surface middle = FloorAt(-48, 52, 1, 1.2f, false);
            Surface north = FloorAt(-48, 75, 4f, 4.5f, false);
            Surface westRim = FloorAt(-62, 52, 8f, 8f, false);
            Surface eastRim = FloorAt(-34, 52, 8f, 8f, false);
            bool trulyOpen = floor != null && entry != null && exit != null && middle.valid &&
                middle.y < 2f && (!north.valid || north.y < 8.8f) &&
                (!westRim.valid || westRim.y > 6f) && (!eastRim.valid || eastRim.y > 6f);
            Add("Sink is an actual recessed open basin", trulyOpen,
                "floor=" + (middle.valid ? middle.y.ToString("F2") : "none") +
                ", north=" + (north.valid ? north.y.ToString("F2") : "none") +
                ", rims W/E=" + (westRim.valid ? westRim.y.ToString("F2") : "none") +
                "/" + (eastRim.valid ? eastRim.y.ToString("F2") : "none"));

            Collider[] walls = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                .Where(c => c.name.StartsWith("Map2 Sink Wall", StringComparison.Ordinal) && !c.isTrigger).ToArray();
            // Probe beyond the descent ramp. At z52 its own side guards are
            // correctly closer than the basin walls, so a first-hit wall check
            // there would reject a well-protected sink.
            bool left = Physics.Raycast(new Vector3(-48,3,40), Vector3.left, out RaycastHit leftHit,
                17f, ~0, QueryTriggerInteraction.Ignore) && leftHit.collider.name.StartsWith("Map2 Sink Wall");
            bool right = Physics.Raycast(new Vector3(-48,3,40), Vector3.right, out RaycastHit rightHit,
                17f, ~0, QueryTriggerInteraction.Ignore) && rightHit.collider.name.StartsWith("Map2 Sink Wall");
            bool high = walls.Count(c => c.bounds.max.y > 10.2f) >= 2;
            Add("Both sink side walls block floor bypass and upper-rim hop", walls.Length >= 2 &&
                left && right && high,
                "walls=" + walls.Length + ", left=" + left + ", right=" + right +
                ", walls higher than approach=" + walls.Count(c => c.bounds.max.y > 10.2f));
        }

        private static void VerifyWalkGraph()
        {
            // Full map, 2 m cells. A directed edge allows the cube's <=1.2 m
            // jump up (the lowest of the three native shape jump heights)
            // and <=8 m down; cardinal links follow the drawn belt route.
            const float step = 2f;
            const int nx = 139;
            const int nz = 125;
            const float x0 = -138f;
            const float z0 = -128f;
            var nodes = new GraphNode[nx * nz];
            int usable = 0;
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    float x = x0 + ix * step;
                    float z = z0 + iz * step;
                    Surface s = HighestClearSurface(x, z);
                    if (!s.valid) continue;
                    nodes[iz * nx + ix] = new GraphNode { valid = true, y = s.y };
                    usable++;
                }

            int start = NearestNode(nodes, nx, nz, x0, z0, step, -48, 88, 8);
            int finish = NearestNode(nodes, nx, nz, x0, z0, step, 12, 72, 8);
            var edgeCache = new Dictionary<long, bool>();
            var full = Reach(nodes, nx, nz, x0, z0, step, start, finish, false, edgeCache);
            var blockedSink = Reach(nodes, nx, nz, x0, z0, step, start, finish, true, edgeCache);
            bool connected = start >= 0 && finish >= 0 && full.reached && full.visitedSink;
            bool mandatory = connected && !blockedSink.reached;
            Add("Physical walk graph connects CH3 to CH6 through recessed sink", connected,
                "walk cells=" + usable + ", start=" + start + ", finish=" + finish +
                ", visited sink=" + full.visitedSink + ", reached=" + full.reached);
            Add("Removing sink interior disconnects CH3 from CH6", mandatory,
                "other route reached=" + blockedSink.reached + ", cells explored=" + blockedSink.explored);

            int beforeDishes = NearestNode(nodes, nx, nz, x0, z0, step, 38, 100, 8);
            int afterDishes = NearestNode(nodes, nx, nz, x0, z0, step, 74, 100, 8);
            var dishCrossing = Reach(nodes, nx, nz, x0, z0, step,
                beforeDishes, afterDishes, false, edgeCache);
            Add("Dish-storage belt gap has a physical plate crossing", dishCrossing.reached,
                "from=" + beforeDishes + ", to=" + afterDishes +
                ", reached=" + dishCrossing.reached + ", explored=" + dishCrossing.explored);

            int entry = NearestNode(nodes, nx, nz, x0, z0, step, -118, -107, .5f);
            int cooking = NearestNode(nodes, nx, nz, x0, z0, step, 0, -120, 8);
            var wholeLine = Reach(nodes, nx, nz, x0, z0, step, entry, cooking, false, edgeCache);
            Add("Ordinary walking path connects return hatch to cooking route",
                wholeLine.reached && wholeLine.visitedSink,
                "from=" + entry + ", to=" + cooking + ", reached=" + wholeLine.reached +
                ", traversed sink=" + wholeLine.visitedSink +
                ", explored=" + wholeLine.explored);

            int keyApproach = NearestNode(nodes, nx, nz, x0, z0, step, -8, -120, 8);
            int upperDoor = NearestNode(nodes, nx, nz, x0, z0, step, -44, -120, 16);
            var upperAscent = Reach(nodes, nx, nz, x0, z0, step,
                keyApproach, upperDoor, false, edgeCache);
            Add("Cube-height route reaches upper key-only aperture", upperAscent.reached &&
                keyApproach >= 0 && upperDoor >= 0 && nodes[upperDoor].y >= 15.2f,
                "from=" + keyApproach + ", to=" + upperDoor + ", upper floor=" +
                (upperDoor >= 0 ? nodes[upperDoor].y.ToString("F2") : "none") +
                ", reached=" + upperAscent.reached);
        }

        private struct Surface { public bool valid; public float y; public Collider collider; }
        private struct GraphNode { public bool valid; public float y; }
        private struct ReachResult { public bool reached; public bool visitedSink; public int explored; }

        private static Surface FloorAt(float x, float z, float expectedY, float tolerance, bool requireHeadroom)
        {
            int count = Physics.RaycastNonAlloc(new Vector3(x, 25, z), Vector3.down, HitBuffer,
                48f, ~0, QueryTriggerInteraction.Ignore);
            Surface best = default;
            float bestError = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = HitBuffer[i];
                if (hit.collider.GetComponentInParent<PlayerMover>() != null || hit.normal.y < .6f) continue;
                float error = Mathf.Abs(hit.point.y - expectedY);
                if (error > tolerance || error >= bestError) continue;
                if (requireHeadroom && !HeadClear(x, hit.point.y, z, hit.collider)) continue;
                best = new Surface { valid = true, y = hit.point.y, collider = hit.collider };
                bestError = error;
            }
            return best;
        }

        private static Surface HighestClearSurface(float x, float z)
        {
            int count = Physics.RaycastNonAlloc(new Vector3(x, 24, z), Vector3.down, HitBuffer,
                25f, ~0, QueryTriggerInteraction.Ignore);
            Surface best = default;
            float top = -1f;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = HitBuffer[i];
                if (hit.collider.GetComponentInParent<PlayerMover>() != null || hit.normal.y < .65f) continue;
                float y = hit.point.y;
                if (y < 0 || y > 19f || y <= top) continue;
                if (!HeadClear(x, y, z, hit.collider)) continue;
                best = new Surface { valid = true, y = y, collider = hit.collider };
                top = y;
            }
            return best;
        }

        private static bool HeadClear(float x, float floor, float z, Collider ground)
        {
            Vector3 feet = new Vector3(x, floor + .48f, z);
            Vector3 head = new Vector3(x, floor + 1.32f, z);
            int count = Physics.OverlapCapsuleNonAlloc(feet, head, .39f, OverlapBuffer,
                ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = OverlapBuffer[i];
                if (c == ground || c.GetComponentInParent<PlayerMover>() != null || c.bounds.max.y <= floor + .40f) continue;
                return false;
            }
            return true;
        }

        private static int NearestNode(GraphNode[] nodes, int nx, int nz, float x0, float z0,
            float step, float x, float z, float y)
        {
            float best = float.PositiveInfinity;
            int winner = -1;
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    int key = iz * nx + ix;
                    if (!nodes[key].valid) continue;
                    float dx = x0 + ix * step - x, dz = z0 + iz * step - z;
                    float distance = dx * dx + dz * dz + Mathf.Pow((nodes[key].y - y) * 1.4f, 2);
                    if (distance < best) { best = distance; winner = key; }
                }
            return best <= 36f ? winner : -1;
        }

        private static ReachResult Reach(GraphNode[] nodes, int nx, int nz, float x0, float z0,
            float step, int start, int finish, bool removeBasin, Dictionary<long, bool> edgeCache)
        {
            var result = new ReachResult();
            if (start < 0 || finish < 0) return result;
            var seen = new bool[nodes.Length];
            var sinkState = new bool[nodes.Length];
            var queue = new Queue<int>();
            seen[start] = true; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                result.explored++;
                int ix = index % nx, iz = index / nx;
                float x = x0 + ix * step, z = z0 + iz * step;
                bool inBasin = IsBasin(x, z, nodes[index].y);
                bool visitedSink = sinkState[index] || inBasin;
                if (index == finish) { result.reached = true; result.visitedSink = visitedSink; return result; }
                for (int direction = 0; direction < 4; direction++)
                    {
                        int dx = direction == 0 ? 1 : direction == 1 ? -1 : 0;
                        int dz = direction == 2 ? 1 : direction == 3 ? -1 : 0;
                        int jx = ix + dx, jz = iz + dz;
                        if (jx < 0 || jx >= nx || jz < 0 || jz >= nz) continue;
                        int next = jz * nx + jx;
                        if (seen[next] || !nodes[next].valid) continue;
                        float nxWorld = x0 + jx * step, nzWorld = z0 + jz * step;
                        bool nextInBasin = IsBasin(nxWorld, nzWorld, nodes[next].y);
                        if (removeBasin && nextInBasin) continue;
                        float rise = nodes[next].y - nodes[index].y;
                        if (rise > 1.2f || rise < -8f) continue;
                        long edgeKey = ((long)index << 32) | (uint)next;
                        if (!edgeCache.TryGetValue(edgeKey, out bool clear))
                        {
                            clear = TransitionClear(new Vector3(x, nodes[index].y, z),
                                new Vector3(nxWorld, nodes[next].y, nzWorld));
                            edgeCache[edgeKey] = clear;
                        }
                        if (!clear) continue;
                        seen[next] = true;
                        sinkState[next] = visitedSink || nextInBasin;
                        queue.Enqueue(next);
                    }
            }
            return result;
        }

        // Each edge follows a short body-sized arc with substeps. This prevents
        // a 2 m grid hop from tunnelling across thin sink walls or guard rails.
        private static bool TransitionClear(Vector3 a, Vector3 b)
        {
            float horizontal = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            int slices = Mathf.Max(5, Mathf.CeilToInt(horizontal / .28f));
            float lift = b.y > a.y + .5f ? 1.1f : .15f;
            for (int i = 1; i < slices; i++)
            {
                float t = i / (float)slices;
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y += lift * 4f * t * (1f - t);
                int count = Physics.OverlapCapsuleNonAlloc(
                    p + Vector3.up * .47f, p + Vector3.up * 1.25f,
                    .35f, OverlapBuffer, ~0, QueryTriggerInteraction.Ignore);
                for (int j = 0; j < count; j++)
                {
                    Collider c = OverlapBuffer[j];
                    if (c.GetComponentInParent<PlayerMover>() != null || c.bounds.max.y <= p.y + .38f) continue;
                    if (c.ClosestPoint(p + Vector3.up * .8f).y <= p.y + .4f) continue;
                    return false;
                }
            }
            // A narrow, full-height partition may fall between sample points.
            // Casting the torso centre catches it without accepting a route
            // simply because neighbouring floor cells exist.
            if (Mathf.Abs(b.y - a.y) > .5f) return true;
            Vector3 from = a + Vector3.up * .85f;
            Vector3 to = b + Vector3.up * .85f;
            Vector3 delta = to - from;
            int hitCount = Physics.RaycastNonAlloc(from, delta.normalized,
                HitBuffer, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Collider c = HitBuffer[i].collider;
                if (c.GetComponentInParent<PlayerMover>() != null || c.bounds.max.y <= Mathf.Min(a.y, b.y) + .4f) continue;
                if (HitBuffer[i].normal.y > .55f) continue;
                return false;
            }
            return true;
        }

        private static bool IsBasin(float x, float z, float y)
            => x >= -60f && x <= -36f && z >= 22f && z <= 82f && y < 4.1f;

        private static void VerifyKeyAndDrop()
        {
            var c = UnityEngine.Object.FindFirstObjectByType<KitchenDraftController>();
            var trigger = UnityEngine.Object.FindFirstObjectByType<DraftExitTrigger>();
            var handoff = UnityEngine.Object.FindFirstObjectByType<KitchenMapHandoff>();
            Transform landing = handoff != null ? handoff.transform : trigger != null ? trigger.transform : null;
            bool wired = handoff != null ? handoff.GetComponent<Collider>().isTrigger :
                trigger != null && trigger.controller == c;
            bool good = c != null && c.key != null && c.doorBlocker != null &&
                c.finalDropPoint != null && landing != null && wired &&
                c.doorBlocker.enabled && landing.position.y < c.doorBlocker.bounds.min.y - 9f;
            Add("Key-only upper aperture ends in a real lower drop", good,
                "key=" + (c != null && c.key != null) + ", upper door=" +
                (c != null && c.doorBlocker != null ? c.doorBlocker.bounds.center.y.ToString("F1") : "none") +
                ", trigger=" + (landing != null ? landing.position.y.ToString("F1") : "none"));
        }

        private static Vector2 V(float x, float z) => new Vector2(x, z);
        private static void Add(string name, bool passed, string detail)
            => Checks.Add(new Check { name = name, passed = passed, detail = detail });
    }
}
