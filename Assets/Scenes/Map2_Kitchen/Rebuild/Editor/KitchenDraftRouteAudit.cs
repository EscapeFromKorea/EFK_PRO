using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Map2KitchenDraft.Editor
{
    // Measures the saved scene's surfaces and clearances. This does not simulate keyboard traversal.
    public static class KitchenDraftRouteAudit
    {
        [Serializable] private class Check { public string name; public bool passed; public string detail; }
        [Serializable] private class Report
        {
            public string unityVersion;
            public bool success;
            public string scope = "Static physical surface/clearance samples and gap measurements; not a full player walkthrough.";
            public Check[] checks;
        }
        private const string Scene = "Assets/Scenes/Map2_Kitchen/Rebuild/Scenes/Map2KitchenDraft.unity";
        private static readonly List<Check> Checks = new List<Check>();

        public static void Run()
        {
            Checks.Clear();
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            try
            {
                Physics.SyncTransforms();
                Walk("Entry and wash approach", N(-4,0,-7), N(0,0,-7), N(0,0,5));
                Walk("CH1 ascent to sterilizer", N(0,0,5), N(0,3,15), N(0,3,30.5f));
                Walk("Sink upper perimeter", N(28.6f,3.5f,36), N(29,3.5f,23), N(37,3.5f,22.5f));
                Walk("Sink descent and basin", N(37,3.5f,22), N(37,.5f,16), N(36,.5f,8), N(35,.5f,1));
                Walk("Sink ascent and conveyor arrival", N(35,.5f,1), N(35,3.5f,-9), V(35,3.5f,-9), V(35,3.5f,-10), V(33.1f,3.5f,-10));
                Walk("Conveyor and cooking approach", V(32,3.83f,-10), V(-4,3.83f,-10), V(-13,3.5f,-8));
                Walk("Upper opening ramp", V(-15,3.5f,-14), V(-21.25f,6.5f,-14));

                var plates = UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)
                    .Where(c => c.name.StartsWith("CH3 walkable dish "))
                    .OrderBy(c => int.Parse(c.name.Substring("CH3 walkable dish ".Length))).ToArray();
                bool platePass = plates.Length == 8;
                float maxGap = 0, maxRise = 0;
                for (int i = 1; i < plates.Length; i++)
                {
                    var a = plates[i-1].bounds;
                    var b = plates[i].bounds;
                    float distance = Vector2.Distance(new Vector2(a.center.x,a.center.z), new Vector2(b.center.x,b.center.z));
                    maxGap = Mathf.Max(maxGap, distance - a.extents.x - b.extents.x);
                    maxRise = Mathf.Max(maxRise, b.max.y - a.max.y);
                }
                platePass &= maxGap <= 1f && maxRise <= .6f;
                Add("Flat mesh dishes and small jump gaps", platePass,
                    "Mesh dishes="+plates.Length+", max edge gap="+maxGap.ToString("F3")+", max rise="+maxRise.ToString("F3"));

                var controller = UnityEngine.Object.FindFirstObjectByType<KitchenDraftController>();
                var actor = UnityEngine.Object.FindFirstObjectByType<PlayerMover>().transform;
                controller.ResetDraft();
                actor.position = controller.key.position + Vector3.right * .5f;
                bool opened = controller.TryCollectKey(actor);
                Add("Key opens gate before route clearance sampling", opened && !controller.doorBlocker.enabled, "Key accepted="+opened);
                Walk("Upper walk through open key hole", V(-21.6f,6.5f,-14), V(-23.5f,6.5f,-10), V(-28.8f,6.5f,-10));
                var landing = controller.finalDropPoint.position;
                RaycastHit hit;
                Vector3 origin = V(-27.5f,4.6f,-10);
                Vector3 delta = landing - origin;
                bool sealedLowerApproach = Physics.Raycast(origin, delta.normalized, out hit, delta.magnitude,
                    ~0, QueryTriggerInteraction.Ignore) && hit.collider.name.StartsWith("Dispatch shaft");
                Add("Lower landing blocks direct low approach", sealedLowerApproach,
                    "First barrier="+(hit.collider != null ? hit.collider.name : "none"));
            }
            catch (Exception e) { Add("Route audit execution", false, e.ToString()); }
            finally { EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single); }
            bool success = Checks.Count > 0 && Checks.All(c => c.passed);
            var report = new Report { unityVersion=Application.unityVersion, success=success, checks=Checks.ToArray() };
            string path = Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","검증","route_report.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report,true));
            if (!success) throw new InvalidOperationException("Kitchen route audit failed: "+path);
        }

        private static void Walk(string label, params Vector3[] points)
        {
            var problems = new List<string>();
            int samples = 0;
            for (int leg=1; leg<points.Length; leg++)
            {
                int n=Mathf.CeilToInt(Vector3.Distance(points[leg-1],points[leg])/.3f);
                for(int j=0;j<=n;j++)
                {
                    Vector3 expected=Vector3.Lerp(points[leg-1],points[leg],j/(float)n);
                    var hits=Physics.RaycastAll(expected+Vector3.up*2,Vector3.down,4,~0,QueryTriggerInteraction.Ignore)
                        .Where(h=>h.collider.GetComponentInParent<PlayerMover>()==null)
                        .OrderBy(h=>h.distance).ToArray();
                    samples++;
                    if(hits.Length==0 || Mathf.Abs(hits[0].point.y-expected.y)>.45f)
                    {
                        if(problems.Count<4) problems.Add("surface at "+expected.ToString("F2")+" -> "+(hits.Length==0?"none":hits[0].point.y.ToString("F2")+"/"+hits[0].collider.name));
                        continue;
                    }
                    float floor=hits[0].point.y;
                    foreach(var c in Physics.OverlapCapsule(new Vector3(expected.x,floor+.50f,expected.z),
                        new Vector3(expected.x,floor+.75f,expected.z),.45f,~0,QueryTriggerInteraction.Ignore))
                    {
                        if(c==hits[0].collider || c.GetComponentInParent<PlayerMover>()!=null || c.bounds.max.y<=floor+.4f) continue;
                        if(c.ClosestPoint(new Vector3(expected.x,floor+.5f,expected.z)).y<=floor+.4f) continue;
                        if(problems.Count<4) problems.Add("clearance at "+expected.ToString("F2")+" -> "+c.name);
                    }
                }
            }
            Add(label,problems.Count==0,"Samples="+samples+(problems.Count==0?", clear":", "+string.Join("; ",problems)));
        }
        private static Vector3 V(float x,float y,float z) { return new Vector3(x,y,z); }
        private static Vector3 N(float x,float y,float z) { return new Vector3(x,y,z+14f); }
        private static void Add(string name,bool passed,string detail) { Checks.Add(new Check{name=name,passed=passed,detail=detail}); }
    }
}
