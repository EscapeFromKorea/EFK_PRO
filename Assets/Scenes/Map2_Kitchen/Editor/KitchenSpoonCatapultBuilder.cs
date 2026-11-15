#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>기존 투석기의 기능·충돌체를 보존하고 주방용 숟가락 외형을 베이크한다.</summary>
public static class KitchenSpoonCatapultBuilder
{
    public const string ScenePath = "Assets/Scenes/Map2_Kitchen/Scenes/Map2_Kitchen.unity";
    public const string ArtPath = "Assets/Scenes/Map2_Kitchen/Art/SpoonCatapult";
    public const string PrefabPath = ArtPath + "/KitchenSpoonCatapult.prefab";
    private const string FixedVisual = "KitchenSpoon_Stand";
    private const string MovingVisual = "KitchenSpoon_Utensil";
    private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    [MenuItem("Tools/Kitchen Map2/Apply Spoon Skin To Selected Catapult")]
    public static void ApplySelected()
    {
        CatapultLoadController load = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<CatapultLoadController>() : null;
        if (load == null) throw new InvalidOperationException("투석기 루트 또는 자식을 선택하십시오.");
        if (Application.isPlaying) throw new InvalidOperationException("편집 모드에서 적용하십시오.");
        PrepareAssets();
        Undo.RegisterFullObjectHierarchyUndo(load.gameObject, "Apply kitchen spoon catapult");
        Apply(load);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(load.gameObject.scene);
    }

    public static void BuildMap2()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CatapultLoadController[] loads = UnityEngine.Object.FindObjectsOfType<CatapultLoadController>();
        if (loads.Length != 1) throw new InvalidOperationException("예상한 맵2 투석기 1대와 다릅니다: " + loads.Length);
        PrepareAssets();
        Apply(loads[0]);
        PrefabUtility.SaveAsPrefabAsset(loads[0].gameObject, PrefabPath);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("맵2 씬 저장 실패");
        AssetDatabase.SaveAssets();
        Render(loads[0], false);
        Render(loads[0], true);
        Debug.Log("[KitchenSpoon] 씬 교체 및 재사용 프리팹 저장 완료: " + PrefabPath);
    }

    public static void RenderMap2Context()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CatapultLoadController load = UnityEngine.Object.FindObjectOfType<CatapultLoadController>();
        var cameraObject = new GameObject("Temporary spoon context camera", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.scene = scene;
        camera.transform.position = load.transform.position + new Vector3(14, 12, 19);
        camera.transform.LookAt(load.transform.position + new Vector3(-1, 4f, 2));
        camera.fieldOfView = 48f; camera.nearClipPlane = .1f; camera.farClipPlane = 250;
        RenderTexture rt = new RenderTexture(1440, 1000, 24);
        RenderTexture old = RenderTexture.active;
        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
        Texture2D texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply();
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "KitchenSpoon_InMap.png");
        File.WriteAllBytes(path, texture.EncodeToPNG());
        RenderTexture.active = old; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(cameraObject);
        Debug.Log("[KitchenSpoon] 맵 배치 미리보기 저장: " + path);
    }

    private static void Apply(CatapultLoadController load)
    {
        if (load.arm == null || load.arm.armPivot == null || load.arm.bucket == null || load.anchor == null)
            throw new InvalidOperationException("팔·버킷·당김 앵커 참조가 필요합니다.");
        Transform root = load.transform;
        Transform pivot = load.arm.armPivot;
        Transform baseObject = root.Find("Catapult_Base");
        CatapultSteerHandle steer = root.GetComponent<CatapultSteerHandle>();
        if (baseObject == null || steer == null || steer.dockAnchor == null)
            throw new InvalidOperationException("받침과 조향 손잡이가 필요합니다.");

        // 바퀴는 외형뿐 아니라 충돌도 제거한다. 도마 바닥과 원래 받침 콜라이더가 본체를 지지한다.
        foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
            if (part.name == "Catapult_Wheel")
                foreach (Collider wheel in part.GetComponentsInChildren<Collider>(true)) wheel.enabled = false;

        // 시각 전용 자식만 교체하며 기존 기믹/충돌체/리지드바디의 직렬화 값은 끝에서 비교한다.
        var mechanics = root.GetComponentsInChildren<Component>(true)
            .Where(c => c is Collider || c is Rigidbody || c is CatapultArm || c is CatapultBucket ||
                        c is CatapultLoadController || c is CatapultSteerHandle || c is CatapultPullAnchor)
            .ToDictionary(c => c, c => EditorJsonUtility.ToJson(c));
        var poses = root.GetComponentsInChildren<Transform>(true)
            .Where(t => !IsNewVisual(t)).ToDictionary(t => t,
                t => (t.localPosition, t.localRotation, t.localScale, t.parent));
        RemoveVisual(root.Find(FixedVisual));
        RemoveVisual(pivot.Find(MovingVisual));
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true)) renderer.enabled = false;

        Transform stand = Group(root, FixedVisual);
        stand.localPosition = baseObject.localPosition - baseObject.localRotation * Vector3.up * baseObject.localScale.y * .5f;
        stand.localRotation = baseObject.localRotation;
        float width = baseObject.localScale.x, depth = baseObject.localScale.z, height = baseObject.localScale.y;
        Mesh board = BeveledBox(new Vector3(width, height, depth), .25f);
        MeshObject(stand, "Maple_Chopping_Board", SaveMesh("ChoppingBoard", board), materials["Maple"], Vector3.up * height * .5f);
        // 도마의 나뭇결은 얇은 조각으로 베이크하며 충돌체를 추가하지 않는다.
        for (int i = 0; i < 9; i++)
        {
            float x = (i - 4) * width / 11f;
            MeshObject(stand, "Maple_Grain_" + i, SaveMesh("Grain_" + i,
                BeveledBox(new Vector3(.035f, .009f, depth * (.65f + .025f * (i % 3))), .003f)),
                materials["MapleGrain"], new Vector3(x, height + .008f, (i % 2 == 0 ? -.2f : .2f)));
        }
        MeshObject(stand, "Board_Juice_Groove", SaveMesh("BoardGroove", RectRing(width - .38f, depth - .38f, .045f)),
            materials["MapleGrain"], new Vector3(0, height + .012f, 0));

        // 넓은 차축/원형 캡 대신 접은 행주와 작은 클립만 두어 숟가락 실루엣을 드러낸다.
        MeshObject(stand, "Folded_Dishcloth", SaveMesh("Dishcloth", BeveledBox(new Vector3(1.2f, .5f, 1.3f), .18f)),
            materials["Linen"], new Vector3(1.6f, height + .25f, 0));
        for (int fold = 0; fold < 3; fold++)
            MeshObject(stand, "Dishcloth_Fold_" + fold, SaveMesh("ClothFold",
                BeveledBox(new Vector3(1.24f, .025f, 1.34f), .18f)), materials["LinenDark"],
                new Vector3(1.6f, height + .1f + fold * .14f, 0));
        for (int side = -1; side <= 1; side += 2)
        {
            MeshObject(stand, "Spoon_Pivot_Clip", SaveMesh("PivotClip", BeveledBox(new Vector3(.16f, 1.3f, .5f), .06f)),
                materials["Steel"], new Vector3(side * .62f, height + .65f, 0));
        }

        Transform utensil = Group(pivot, MovingVisual);
        // 숟가락의 긴 축은 원래 팔의 +Y, 오목한 면은 원래 버킷의 +Y 방향(-팔 Z)이다.
        Vector3 bowlCenter = pivot.InverseTransformPoint(load.arm.bucket.transform.position);
        MeshObject(utensil, "Faceted_Stainless_Spoon", SaveMesh("SpoonBowl", SpoonBowl(bowlCenter)), materials["Steel"], Vector3.zero);
        Vector2[] handleOutline = {
            new Vector2(-.42f, -1.8f), new Vector2(.42f, -1.8f), new Vector2(.5f, -1.55f),
            new Vector2(.35f, 0), new Vector2(.29f, bowlCenter.y - 1.75f), new Vector2(.56f, bowlCenter.y - 1.4f),
            new Vector2(-.56f, bowlCenter.y - 1.4f), new Vector2(-.29f, bowlCenter.y - 1.75f),
            new Vector2(-.35f, 0), new Vector2(-.5f, -1.55f)
        };
        MeshObject(utensil, "Stainless_Spoon_Handle", SaveMesh("SpoonHandle", Slab(handleOutline, .18f)), materials["Steel"], Vector3.zero);
        MeshObject(utensil, "Silicone_Handle_Grip", SaveMesh("SpoonGrip", BeveledBox(new Vector3(1.05f, 2.1f, .75f), .2f)),
            materials["Grip"], new Vector3(0, -.98f, 0));
        for (int i = 0; i < 4; i++)
            MeshObject(utensil, "Grip_Ridge_" + i, SaveMesh("GripRidge", BeveledBox(new Vector3(1.08f, .04f, .78f), .2f)),
                materials["GripDark"], new Vector3(0, -1.65f + i * .42f, 0));

        // 발사 때 팔에 달린 앵커가 움직이면 두 고무줄의 길이도 함께 바뀐다.
        KitchenSpoonBands bands = stand.gameObject.AddComponent<KitchenSpoonBands>();
        bands.movingAnchor = load.anchor.transform;
        bands.leftTie = Group(stand, "Elastic_Tie_Left"); bands.leftTie.localPosition = new Vector3(-1.5f, height + .16f, -2.8f);
        bands.rightTie = Group(stand, "Elastic_Tie_Right"); bands.rightTie.localPosition = new Vector3(1.5f, height + .16f, -2.8f);
        bands.leftBand = Band(stand, "Elastic_Left"); bands.rightBand = Band(stand, "Elastic_Right");
        foreach (Transform tie in new[] { bands.leftTie, bands.rightTie })
            Cylinder(tie, "Elastic_Pin", Vector3.zero, .16f, .4f, Quaternion.identity, "Steel");

        // 실제 C 도킹/연결 위치를 가리키는 고리도 시각만 교체한다.
        Transform ring = Group(stand, "Steering_Silicone_Loop");
        ring.position = steer.dockAnchor.position; ring.rotation = steer.dockAnchor.rotation;
        MeshObject(ring, "Steering_Loop", SaveMesh("SteerLoop", Torus(.8f, .1f, 12, 5)), materials["Grip"], Vector3.zero);
        Transform pullRing = Group(utensil, "Pull_Anchor_Loop");
        pullRing.position = load.anchor.transform.position; pullRing.rotation = load.anchor.transform.rotation;
        MeshObject(pullRing, "Pull_Loop", SaveMesh("PullLoop", Torus(.53f, .09f, 12, 5)), materials["Elastic"], Vector3.zero);

        foreach (var entry in mechanics)
            if (EditorJsonUtility.ToJson(entry.Key) != entry.Value)
                throw new InvalidOperationException("기존 기믹/물리 설정이 변경되었습니다: " + entry.Key.name);
        foreach (var entry in poses)
        {
            var p = entry.Value;
            if (entry.Key.localPosition != p.Item1 || entry.Key.localRotation != p.Item2 ||
                entry.Key.localScale != p.Item3 || entry.Key.parent != p.Item4)
                throw new InvalidOperationException("기존 위치/회전/크기가 변경되었습니다: " + entry.Key.name);
        }
        if (stand.GetComponentsInChildren<Collider>(true).Length != 0 || utensil.GetComponentsInChildren<Collider>(true).Length != 0)
            throw new InvalidOperationException("시각 자식에 예기치 않은 충돌체가 있습니다.");
        // ExecuteAlways의 다음 업데이트를 기다리지 않고 저장/미리보기에도 줄 좌표를 반영한다.
        bands.SendMessage("LateUpdate");
        Debug.Log("[KitchenSpoon] 바퀴 충돌 제거 및 나머지 기믹·물리·위치 보존 검사 통과. 숟가락 교체 완료.");
    }

    private static bool IsNewVisual(Transform t)
    {
        for (; t != null; t = t.parent) if (t.name == FixedVisual || t.name == MovingVisual) return true;
        return false;
    }
    private static void RemoveVisual(Transform t) { if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject); }
    private static Transform Group(Transform parent, string name)
    { var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform; }
    private static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material mat, Vector3 position)
    {
        Transform t = Group(parent, name); t.localPosition = position;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return t.gameObject;
    }
    private static void Cylinder(Transform parent, string name, Vector3 position, float radius, float length, Quaternion rotation, string mat)
    {
        var go = MeshObject(parent, name, SaveMesh("Cylinder12", CylinderMesh()), materials[mat], position);
        go.transform.localRotation = rotation;
        go.transform.localScale = new Vector3(radius, length, radius);
    }
    private static LineRenderer Band(Transform parent, string name)
    {
        LineRenderer line = Group(parent, name).gameObject.AddComponent<LineRenderer>();
        line.sharedMaterial = materials["Elastic"]; line.widthMultiplier = .105f;
        line.numCornerVertices = 1; line.numCapVertices = 2; line.useWorldSpace = false;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return line;
    }
    private static void PrepareAssets()
    {
        EnsureFolder(ArtPath);
        Material("Steel", new Color(.76f, .84f, .86f), .35f, .42f);
        Material("Maple", new Color(.73f, .46f, .22f), 0, .25f);
        Material("MapleGrain", new Color(.37f, .20f, .095f), 0, .2f);
        Material("Grip", new Color(.79f, .20f, .105f), 0, .26f);
        Material("GripDark", new Color(.39f, .085f, .04f), 0, .2f);
        Material("Elastic", new Color(.96f, .64f, .23f), 0, .3f);
        Material("Linen", new Color(.37f, .65f, .62f), 0, .12f);
        Material("LinenDark", new Color(.20f, .39f, .37f), 0, .12f);
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    private static void Material(string key, Color color, float metallic, float smoothness)
    {
        string path = ArtPath + "/Spoon_" + key + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null
                ? Shader.Find("Standard") : Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(shader) { name = "Spoon_" + key };
            AssetDatabase.CreateAsset(mat, path);
        }
        // LineRenderer에는 조명용 메시 노멀이 없으므로 고무줄은 색이 확실히 보이는 셰이더를 쓴다.
        if (key == "Elastic")
            mat.shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null
                ? Shader.Find("Unlit/Color") : Shader.Find("Universal Render Pipeline/Unlit");
        mat.color = color; mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (key == "Steel" && mat.HasProperty("_EmissionColor"))
        {
            // 기존 맵의 어두운 선반 조명에서도 오목한 면이 검게 뭉개지지 않게 최소 색을 확보한다.
            mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", color * .08f);
        }
        EditorUtility.SetDirty(mat); materials[key] = mat;
    }
    private static Mesh SaveMesh(string key, Mesh mesh)
    {
        string path = ArtPath + "/Spoon_" + key + ".asset";
        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved == null) { mesh.name = "Spoon_" + key; AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, saved); saved.name = "Spoon_" + key;
        EditorUtility.SetDirty(saved); UnityEngine.Object.DestroyImmediate(mesh); return saved;
    }

    // 면마다 정점을 분리해 평평한 노멀을 유지하는 로우폴리 메시 작성기.
    private sealed class MeshData
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<int> indices = new List<int>();
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0) { Vector3 swap = b; b = c; c = swap; }
            int i = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
            indices.Add(i); indices.Add(i + 1); indices.Add(i + 2);
        }
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        { Tri(a, b, c, normal); Tri(a, c, d, normal); }
        public Mesh Finish()
        {
            Mesh mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
    private static Mesh SpoonBowl(Vector3 center)
    {
        var data = new MeshData(); const int segments = 16;
        float[] radii = { 0, .28f, .55f, .8f, 1f };
        float[] depths = { .92f, .84f, .53f, -.1f, -1.02f };
        Func<int, int, bool, Vector3> point = (ring, seg, outer) =>
        {
            float angle = seg * Mathf.PI * 2 / segments;
            float r = radii[ring] * (outer ? 1.035f : 1f);
            return center + new Vector3(Mathf.Sin(angle) * 1.7f * r, Mathf.Cos(angle) * 2.25f * r,
                .92f + (depths[ring] - .92f) * .6f + (outer ? .13f : 0));
        };
        for (int ring = 0; ring < radii.Length - 1; ring++)
            for (int seg = 0; seg < segments; seg++)
            {
                Vector3 a = point(ring, seg, false), b = point(ring + 1, seg, false);
                Vector3 c = point(ring + 1, seg + 1, false), d = point(ring, seg + 1, false);
                Vector3 normal = new Vector3(-(b.x - center.x) * .3f, -(b.y - center.y) * .3f, -1);
                if (ring == 0) data.Tri(a, b, c, normal); else data.Quad(a, b, c, d, normal);
                a = point(ring, seg, true); b = point(ring + 1, seg, true);
                c = point(ring + 1, seg + 1, true); d = point(ring, seg + 1, true);
                if (ring == 0) data.Tri(a, b, c, -normal); else data.Quad(a, b, c, d, -normal);
            }
        for (int i = 0; i < segments; i++)
            data.Quad(point(4, i, false), point(4, i + 1, false), point(4, i + 1, true), point(4, i, true),
                point(4, i, false) - center);
        return data.Finish();
    }
    private static Mesh BeveledBox(Vector3 size, float bevel)
    {
        var data = new MeshData();
        float x = size.x * .5f, z = size.z * .5f, y = size.y * .5f;
        bevel = Mathf.Min(bevel, Mathf.Min(x, z) * .4f);
        Vector2[] outline = { new Vector2(-x + bevel, -z), new Vector2(x - bevel, -z), new Vector2(x, -z + bevel),
            new Vector2(x, z - bevel), new Vector2(x - bevel, z), new Vector2(-x + bevel, z),
            new Vector2(-x, z - bevel), new Vector2(-x, -z + bevel) };
        for (int i = 0; i < 8; i++)
        {
            Vector2 a = outline[i], b = outline[(i + 1) % 8];
            Vector3 lowA = new Vector3(a.x, -y, a.y), lowB = new Vector3(b.x, -y, b.y);
            Vector3 highA = new Vector3(a.x, y, a.y), highB = new Vector3(b.x, y, b.y);
            data.Quad(lowA, lowB, highB, highA, new Vector3(a.x + b.x, 0, a.y + b.y));
            data.Tri(Vector3.up * y, highA, highB, Vector3.up);
            data.Tri(Vector3.down * y, lowA, lowB, Vector3.down);
        }
        return data.Finish();
    }
    private static Mesh Slab(Vector2[] outline, float halfDepth)
    {
        var data = new MeshData();
        // 손잡이의 오목한 목 부분을 중심 팬으로 채우면 면이 겹쳐 깜빡인다. 귀 자르기로 삼각분할한다.
        foreach (int[] triangle in Triangulate(outline))
        {
            Vector2 a = outline[triangle[0]], b = outline[triangle[1]], c = outline[triangle[2]];
            data.Tri(new Vector3(a.x, a.y, -halfDepth), new Vector3(b.x, b.y, -halfDepth),
                new Vector3(c.x, c.y, -halfDepth), Vector3.back);
            data.Tri(new Vector3(a.x, a.y, halfDepth), new Vector3(b.x, b.y, halfDepth),
                new Vector3(c.x, c.y, halfDepth), Vector3.forward);
        }
        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
            Vector3 frontA = new Vector3(a.x, a.y, -halfDepth), frontB = new Vector3(b.x, b.y, -halfDepth);
            Vector3 backA = new Vector3(a.x, a.y, halfDepth), backB = new Vector3(b.x, b.y, halfDepth);
            data.Quad(frontA, frontB, backB, backA, new Vector3(b.y - a.y, a.x - b.x, 0));
        }
        return data.Finish();
    }
    private static float Cross2D(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    private static List<int[]> Triangulate(Vector2[] polygon)
    {
        var indices = Enumerable.Range(0, polygon.Length).ToList();
        float area = 0;
        for (int i = 0; i < polygon.Length; i++) area += Cross2D(polygon[i], polygon[(i + 1) % polygon.Length]);
        if (area < 0) indices.Reverse();
        var triangles = new List<int[]>();
        while (indices.Count > 3)
        {
            bool clipped = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int a = indices[(i + indices.Count - 1) % indices.Count], b = indices[i], c = indices[(i + 1) % indices.Count];
                if (Cross2D(polygon[b] - polygon[a], polygon[c] - polygon[b]) <= .000001f) continue;
                bool occupied = indices.Any(p => p != a && p != b && p != c &&
                    Cross2D(polygon[b] - polygon[a], polygon[p] - polygon[a]) >= -.000001f &&
                    Cross2D(polygon[c] - polygon[b], polygon[p] - polygon[b]) >= -.000001f &&
                    Cross2D(polygon[a] - polygon[c], polygon[p] - polygon[c]) >= -.000001f);
                if (occupied) continue;
                triangles.Add(new[] { a, b, c }); indices.RemoveAt(i); clipped = true; break;
            }
            if (!clipped) throw new InvalidOperationException("숟가락 손잡이 윤곽 삼각분할 실패");
        }
        triangles.Add(indices.ToArray()); return triangles;
    }
    private static Mesh CylinderMesh()
    {
        var data = new MeshData();
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI / 6, b = (i + 1) * Mathf.PI / 6;
            Vector3 lowA = new Vector3(Mathf.Cos(a), -.5f, Mathf.Sin(a)), lowB = new Vector3(Mathf.Cos(b), -.5f, Mathf.Sin(b));
            Vector3 highA = lowA + Vector3.up, highB = lowB + Vector3.up;
            data.Quad(lowA, lowB, highB, highA, lowA + lowB + Vector3.up);
            data.Tri(Vector3.down * .5f, lowA, lowB, Vector3.down);
            data.Tri(Vector3.up * .5f, highA, highB, Vector3.up);
        }
        return data.Finish();
    }
    private static Mesh Torus(float radius, float thickness, int sides, int tubeSides)
    {
        var data = new MeshData();
        Func<int, int, Vector3> p = (i, j) =>
        {
            float a = i * Mathf.PI * 2 / sides, b = j * Mathf.PI * 2 / tubeSides;
            return new Vector3(Mathf.Cos(a) * (radius + thickness * Mathf.Cos(b)), thickness * Mathf.Sin(b),
                Mathf.Sin(a) * (radius + thickness * Mathf.Cos(b)));
        };
        for (int i = 0; i < sides; i++) for (int j = 0; j < tubeSides; j++)
        {
            Vector3 a = p(i, j), b = p(i + 1, j), c = p(i + 1, j + 1), d = p(i, j + 1);
            Vector3 normal = (a + b + c + d) * .25f - new Vector3(Mathf.Cos((i + .5f) * Mathf.PI * 2 / sides) * radius,
                0, Mathf.Sin((i + .5f) * Mathf.PI * 2 / sides) * radius);
            data.Quad(a, b, c, d, normal);
        }
        return data.Finish();
    }
    private static Mesh RectRing(float width, float depth, float thickness)
    {
        var data = new MeshData();
        Vector3[] p = { new Vector3(-width / 2, 0, -depth / 2), new Vector3(width / 2, 0, -depth / 2),
            new Vector3(width / 2, 0, depth / 2), new Vector3(-width / 2, 0, depth / 2) };
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = p[i], b = p[(i + 1) % 4];
            Vector3 insetA = new Vector3(a.x - Mathf.Sign(a.x) * thickness, 0, a.z - Mathf.Sign(a.z) * thickness);
            Vector3 insetB = new Vector3(b.x - Mathf.Sign(b.x) * thickness, 0, b.z - Mathf.Sign(b.z) * thickness);
            data.Quad(a, b, insetB, insetA, Vector3.up);
        }
        return data.Finish();
    }

    private static void Render(CatapultLoadController load, bool pulled)
    {
        // 씬과 분리한 프리팹 복제본을 촬영하여 맵 배치·팔 각도를 저장 상태에서 변경하지 않는다.
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject model = UnityEngine.Object.Instantiate(load.gameObject);
        SceneManager.MoveGameObjectToScene(model, preview);
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        CatapultArm arm = model.GetComponentInChildren<CatapultArm>();
        Vector3 euler = arm.armPivot.localEulerAngles;
        arm.armPivot.localRotation = Quaternion.Euler(pulled ? arm.pulledAngle : arm.restAngle, euler.y, euler.z);
        model.GetComponentInChildren<KitchenSpoonBands>().SendMessage("LateUpdate");
        var cameraObject = new GameObject("Spoon preview camera", typeof(Camera));
        SceneManager.MoveGameObjectToScene(cameraObject, preview);
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.scene = preview;
        camera.orthographic = true; camera.orthographicSize = 8.5f;
        camera.backgroundColor = new Color(.12f, .18f, .21f); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.transform.position = new Vector3(14, 12, 19);
        camera.transform.LookAt(new Vector3(-1, 4f, 2));
        var lightObject = new GameObject("Spoon preview light", typeof(Light));
        SceneManager.MoveGameObjectToScene(lightObject, preview);
        lightObject.GetComponent<Light>().type = LightType.Directional;
        lightObject.GetComponent<Light>().intensity = 1.5f;
        lightObject.transform.rotation = Quaternion.Euler(45, -30, 0);
        var fillObject = new GameObject("Spoon preview fill", typeof(Light));
        SceneManager.MoveGameObjectToScene(fillObject, preview);
        fillObject.GetComponent<Light>().type = LightType.Directional;
        fillObject.GetComponent<Light>().intensity = .75f;
        fillObject.transform.rotation = Quaternion.Euler(20, 150, 0);
        var frontObject = new GameObject("Spoon preview front", typeof(Light));
        SceneManager.MoveGameObjectToScene(frontObject, preview);
        frontObject.GetComponent<Light>().type = LightType.Directional;
        frontObject.GetComponent<Light>().intensity = .45f;
        frontObject.transform.rotation = camera.transform.rotation;
        RenderTexture rt = new RenderTexture(1440, 1200, 24);
        RenderTexture old = RenderTexture.active;
        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
        Texture2D texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply();
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
            pulled ? "KitchenSpoon_Loaded.png" : "KitchenSpoon_Rest.png");
        File.WriteAllBytes(path, texture.EncodeToPNG());
        RenderTexture.active = old; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(rt);
        EditorSceneManager.ClosePreviewScene(preview);
        Debug.Log("[KitchenSpoon] 미리보기 저장: " + path);
    }
}
#endif
