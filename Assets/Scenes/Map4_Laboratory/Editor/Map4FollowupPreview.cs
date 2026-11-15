#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>수정 사본의 정적 외관 자료. 게임 카메라·실제 조작 검증을 대신하지 않는다.</summary>
public static class Map4FollowupPreview
{
    [MenuItem("Tools/Laboratory Map4/Followup/Capture review views")]
    public static void Run()
    {
        int failures = 0;
        try
        {
            if (!Map4SceneGuard.CanReplaceCurrentScenes()) throw new InvalidOperationException("Scene replacement cancelled");
            EditorSceneManager.OpenScene("Assets/Scenes/Map4_Laboratory/Scenes/Map4_Master.unity", OpenSceneMode.Single);
            Map4SceneBuilder.OpenAllSectors();
            string root = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
            string folder = Path.Combine(root, "검증", "N03_preview_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(folder);
            var report = new StringBuilder("Static editor renders; inactive traps intentionally absent. S7/S8 roofs hidden only for review cutaways; production scenes are not saved. Not a gameplay completion test. Runtime-created line materials are audited separately by Map4RuntimeMaterialChecks.\n");
            int runtimePending = 0;
            foreach (SectorController sector in UnityEngine.Object.FindObjectsOfType<SectorController>())
            {
                Renderer[] renderers = sector.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                    {
                        if (material == null && renderer is LineRenderer)
                        {
                            runtimePending++;
                            report.AppendLine("RUNTIME PENDING (not a material pass): S" + sector.sectorId + "/" + renderer.name);
                            continue;
                        }
                        report.AppendLine("MISSING MATERIAL/SHADER: S" + sector.sectorId + "/" + renderer.name);
                        failures++;
                    }
                }
                if (!new[] { 1, 2, 5, 7, 8 }.Contains(sector.sectorId)) continue;
                Renderer[] visible = renderers.Where(r => !(r is LineRenderer) && r.enabled && r.gameObject.activeInHierarchy).ToArray();
                if (visible.Length == 0) throw new InvalidOperationException("No visible renderers in S" + sector.sectorId);
                Bounds bounds = visible[0].bounds;
                foreach (Renderer renderer in visible.Skip(1)) bounds.Encapsulate(renderer.bounds);
                Capture(folder, "S" + sector.sectorId + "_overview", bounds, new Vector3(1.0f, 1.5f, -1.1f), sector);
                if (sector.sectorId == 1)
                {
                    Transform generated = sector.transform.Find("Generated");
                    if (generated != null)
                        Capture(folder, "S1_bridge", new Bounds(generated.TransformPoint(new Vector3(0f, 0f, 154f)), new Vector3(23f, 10f, 36f)), new Vector3(1.3f, 1.7f, -.7f), sector);
                }
                if (sector.sectorId == 5)
                {
                    Renderer[] ceilings = visible.Where(r => r.name.IndexOf("Canopy", StringComparison.OrdinalIgnoreCase) >= 0 || r.transform.parent != null && r.transform.parent.name.IndexOf("Canopy", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                    if (ceilings.Length > 0)
                    {
                        Bounds detail = ceilings[0].bounds;
                        detail.Expand(new Vector3(12f, 8f, 12f));
                        Capture(folder, "S5_first_ceiling", detail, new Vector3(1.4f, .7f, -.8f), sector);
                    }
                }
                report.AppendLine("S" + sector.sectorId + " visible renderers=" + visible.Length + " bounds=" + bounds);
            }
            report.AppendLine("Missing material/shader count=" + failures);
            report.AppendLine("Runtime-pending line material slots=" + runtimePending);
            File.WriteAllText(Path.Combine(folder, "render_report.txt"), report.ToString(), new UTF8Encoding(true));
            Debug.Log("[Map4FollowupPreview] " + folder + " missing=" + failures);
        }
        catch (Exception exception) { Debug.LogException(exception); failures++; }
        if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void Capture(string folder, string name, Bounds bounds, Vector3 direction, SectorController sector)
    {
        const int width = 1600, height = 1000;
        var go = new GameObject("ReviewCamera_TEMP");
        var camera = go.AddComponent<Camera>();
        RenderTexture target = null;
        Texture2D image = null;
        RenderTexture previous = RenderTexture.active;
        // 다른 섹터의 벽·천장이 정사영 앞을 가리지 않게 캡처 중에만 렌더러를 숨긴다. 씬은 저장하지 않는다.
        Renderer[] hidden = UnityEngine.Object.FindObjectsOfType<Renderer>().Where(r => r.enabled &&
            (!r.transform.IsChildOf(sector.transform) || (sector.sectorId == 7 || sector.sectorId == 8) &&
             r.GetComponentsInParent<Transform>().Any(t => t.name.StartsWith("GEO_S7_Ceiling", StringComparison.Ordinal) || t.name.StartsWith("GEO_S8_Ceil_", StringComparison.Ordinal)))).ToArray();
        try
        {
            foreach (Renderer renderer in hidden) renderer.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .15f, .19f);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 5000f;
            float radius = Mathf.Max(5f, bounds.extents.magnitude);
            camera.orthographicSize = radius * .82f;
            camera.transform.position = bounds.center + direction.normalized * radius * 3f;
            camera.transform.LookAt(bounds.center);
            target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            UnityEngine.Object.DestroyImmediate(go);
            foreach (Renderer renderer in hidden) if (renderer != null) renderer.enabled = true;
        }
    }
}
#endif
