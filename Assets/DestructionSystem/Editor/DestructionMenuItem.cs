// 이 파일은 반드시 프로젝트의 "Editor" 폴더 안에 위치해야 합니다.

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > DestructionSystem > Create Breakable 메뉴로 부서지는 장애물을 즉석 생성한다.
/// PlayerObjectMenuItem/CatapultMenuItem과 같은 패턴(PrimitiveType 즉석 생성, SceneView 중앙
/// 스폰, Undo 등록)을 따른다 — 아트 에셋이 아직 없어(docs/art-pipeline/ 비어 있음) 프리팹 프리셋
/// 대신 PrimitiveType으로 만든다.
///
/// fragmentPrefabs는 항상 비운 채로 만든다 — 레벨 디자이너가 직접 채운다(docs/PRD/Destruction.md
/// §4 참고). "Breakable" 태그는 이 저장소에 아직 없는 커스텀 태그라, 없으면 여기서 새로 만든다.
///
/// [균열 텍스처 — 일반 오브젝트와 시각적으로 구분]
/// 아트 에셋이 없어 크랙 텍스처를 코드로 절차 생성한다 — 밝은 돌 톤 베이스에 어두운 갈라짐 선을
/// 여러 갈래 그려 넣는다. `CatapultMenuItem.LoadOrCreateMaterial`과 같은 idempotent
/// load-or-create 패턴(있으면 그대로 로드, 없으면 한 번만 생성해 에셋으로 저장)이라 매번 다시
/// 그리지 않는다 — 시드를 고정해 재생성해도 같은 패턴이 나온다.
/// </summary>
public static class DestructionMenuItem
{
    private const string BreakableTag = "Breakable";
    private const string SystemFolder = "Assets/DestructionSystem";
    private const string MaterialSavePath = SystemFolder + "/Materials";
    private const string CrackTexturePath = MaterialSavePath + "/Breakable_CrackTexture.png";
    private const string CrackMaterialPath = MaterialSavePath + "/Breakable_Mat.mat";
    private const int CrackTextureSize = 256;

    [MenuItem("Tools/DestructionSystem/Create Breakable/Cube")]
    private static void CreateBreakableCube() => CreateBreakable(PrimitiveType.Cube);

    [MenuItem("Tools/DestructionSystem/Create Breakable/Sphere")]
    private static void CreateBreakableSphere() => CreateBreakable(PrimitiveType.Sphere);

    private static void CreateBreakable(PrimitiveType shape)
    {
        EnsureTagExists(BreakableTag);
        EnsureMaterialFolder();

        Vector3 spawnPos = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            spawnPos = SceneView.lastActiveSceneView.pivot;

        GameObject go = GameObject.CreatePrimitive(shape);
        Undo.RegisterCreatedObjectUndo(go, "Create Breakable");
        go.name = $"Breakable_{shape}";
        go.transform.position = spawnPos;
        go.tag = BreakableTag;

        // CreatePrimitive가 이미 알맞은 콜라이더(BoxCollider/SphereCollider, non-trigger)를
        // 붙여 준다 — 새로 붙일 필요가 없다.
        go.AddComponent<BreakableObject>();

        // 균열 텍스처 머티리얼 — 일반 오브젝트와 한눈에 구분되도록(사용자 요청).
        go.GetComponent<Renderer>().sharedMaterial = LoadOrCreateCrackedMaterial();

        Selection.activeGameObject = go;
        Debug.Log($"[DestructionSystem] '{go.name}' 생성 완료 (fragmentPrefabs 비어 있음 — 레벨 " +
                   "디자이너가 직접 채워야 합니다).");
    }

    /// <summary>
    /// 격자 분할 계산(BreakableObject.ComputeGridDivisions/BuildGridCells)을 여러 비율에 대해
    /// 자체 점검한다. 이 저장소에는 테스트 프레임워크가 없어서, 격자 계산처럼 눈으로는 틀린 걸
    /// 알기 어려운 부분(부피 보존, 개수 상·하한, 셀 비율)만 메뉴 하나로 확인할 수 있게 뒀다 —
    /// 씬에 Breakable을 놓고 실제로 부숴 보기 전에 계산이 깨졌는지부터 걸러내는 용도다.
    /// </summary>
    // public인 이유: 메뉴로도 돌리지만 배치모드에서도 돌리기 위해서다(-executeMethod
    // DestructionMenuItem.ValidateGridSplit). Unity Editor를 열지 않고 격자 계산을 검증할 수 있는
    // 유일한 경로라, 격자 코드를 고친 뒤 이걸로 먼저 거른다.
    [MenuItem("Tools/DestructionSystem/Validate Grid Split")]
    public static void ValidateGridSplit()
    {
        int failures = 0;
        const int target = 4, min = 2, max = 16;

        var cases = new (Vector3 size, string label)[]
        {
            (new Vector3(1f, 1f, 1f), "정육면체"),
            (new Vector3(4f, 3f, 0.3f), "납작한 벽"),
            (new Vector3(6f, 1f, 1f), "긴 기둥"),
            (new Vector3(0.4f, 2.5f, 0.4f), "가는 기둥"),
            (new Vector3(12f, 8f, 0.5f), "큰 벽"),
            (new Vector3(0.2f, 0.2f, 0.2f), "아주 작은 상자"),
        };

        foreach (var c in cases)
        {
            Vector3Int div = BreakableObject.ComputeGridDivisions(c.size, target, min, max);
            int count = div.x * div.y * div.z;

            failures += Check(count >= target && count <= max,
                $"{c.label}: 조각 수 {count}가 [{target}, {max}] 범위를 벗어남 (div={div})");

            var cells = BreakableObject.BuildGridCells(c.size, div);
            failures += Check(cells.Count == count, $"{c.label}: 셀 개수 불일치 ({cells.Count} vs {count})");

            // 부피 보존 — 셀 사이에 틈이나 겹침이 있으면 여기서 깨진다.
            float cellVolumeSum = 0f;
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            foreach (var cell in cells)
            {
                cellVolumeSum += cell.size.x * cell.size.y * cell.size.z;
                lo = Vector3.Min(lo, cell.center - cell.size * 0.5f);
                hi = Vector3.Max(hi, cell.center + cell.size * 0.5f);
            }
            float originalVolume = c.size.x * c.size.y * c.size.z;
            failures += Check(Mathf.Abs(cellVolumeSum - originalVolume) <= originalVolume * 1e-4f,
                $"{c.label}: 셀 부피 합 {cellVolumeSum}이 원본 부피 {originalVolume}와 다름");
            failures += Check((hi - lo - c.size).magnitude <= 1e-4f && (lo + hi).magnitude <= 1e-4f,
                $"{c.label}: 셀이 채운 범위 {hi - lo}가 원본 크기 {c.size}와 다르거나 중심이 어긋남");

            // 정육면체에 가까운가 — 셀 비율은 원본 비율이나 슬래브(2) 중 나쁜 쪽까지만 허용한다.
            // 원본 비율만으로 재면 안 되는 이유: 목표 개수가 세제곱수가 아니면 정육면체 셀이 원리적
            // 으로 불가능하다(1x1x1을 4조각 내는 방법은 4x1x1(비율 4)과 2x2x1(비율 2)뿐이고 후자가
            // 최선이다). 슬래브까지는 정상이고, 그보다 나쁘면 그리디가 짧은 축을 잘못 쪼갠 것이다.
            Vector3 cellSize = new Vector3(c.size.x / div.x, c.size.y / div.y, c.size.z / div.z);
            float cellAspect = Aspect(cellSize), sourceAspect = Aspect(c.size);
            float allowedAspect = Mathf.Max(sourceAspect, 2f);
            failures += Check(cellAspect <= allowedAspect + 1e-4f,
                $"{c.label}: 셀 비율 {cellAspect:F2}가 허용치 {allowedAspect:F2}보다 나쁨 (div={div})");
        }

        // 얇은 축은 쪼개지 않는다 — 납작한 벽이 두께 방향으로 갈라지면 종잇장 조각이 나온다.
        failures += Check(BreakableObject.ComputeGridDivisions(new Vector3(4f, 3f, 0.3f), target, min, max).z == 1,
            "납작한 벽의 두께 축이 쪼개짐");
        // 긴 기둥은 긴 축만 쪼갠다.
        Vector3Int pillar = BreakableObject.ComputeGridDivisions(new Vector3(6f, 1f, 1f), target, min, max);
        failures += Check(pillar.y == 1 && pillar.z == 1, $"긴 기둥이 짧은 축까지 쪼개짐 (div={pillar})");

        // 상·하한 — 목표값을 극단으로 줘도 성능 방어선과 최소 조각 수를 지킨다.
        Vector3Int overTarget = BreakableObject.ComputeGridDivisions(new Vector3(4f, 3f, 2f), 1000, min, max);
        failures += Check(overTarget.x * overTarget.y * overTarget.z <= max, $"상한 초과 (div={overTarget})");
        Vector3Int underTarget = BreakableObject.ComputeGridDivisions(new Vector3(4f, 3f, 2f), 0, min, max);
        failures += Check(underTarget.x * underTarget.y * underTarget.z >= min, $"하한 미달 (div={underTarget})");

        // 인스펙터 기본값 조합에서 타격점 재분할이 실제로 일어나는가. 상한(maxProceduralFragmentCount)이
        // 재분할 예산을 겸하기 때문에, 목표 조각 수를 올리다 여유(셀 하나당 +7)가 사라지면 재분할이
        // 조용히 죽는다 — 씬에서는 "타격 부위가 잘게 안 부서지네" 정도로만 보여 원인을 찾기 어렵다.
        // 값을 하드코딩하지 않고 컴포넌트에서 직접 읽어, 기본값을 바꾸면 이 점검이 같이 따라온다.
        GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        BreakableObject defaults = probe.AddComponent<BreakableObject>();
        int defTarget = defaults.proceduralFragmentCount;
        int defMin = defaults.minProceduralFragmentCount;
        int defMax = defaults.maxProceduralFragmentCount;
        float defRadius = defaults.hitRefineRadius;
        Object.DestroyImmediate(probe);

        failures += Check(defMax - defTarget >= 7,
            $"기본값에 재분할 여유가 없음 — 목표 {defTarget} / 상한 {defMax} (셀 하나를 쪼개면 7개가 순증한다)");

        foreach (var c in cases)
        {
            Vector3Int div = BreakableObject.ComputeGridDivisions(c.size, defTarget, defMin, defMax);
            var cells = BreakableObject.BuildGridCells(c.size, div);
            int baseCount = cells.Count;
            Vector3 baseCellSize = new Vector3(c.size.x / div.x, c.size.y / div.y, c.size.z / div.z);

            Vector3 hit = new Vector3(-c.size.x * 0.5f, 0f, 0f); // -x 면 한가운데를 때린 상황
            BreakableObject.RefineCellsNearHit(cells, hit, defRadius, defMax);

            failures += Check(cells.Count <= defMax,
                $"{c.label}: 재분할 후 조각 수 {cells.Count}가 상한 {defMax}를 넘음");
            // 어떤 크기의 오브젝트든 '맞은 칸'은 반드시 쪼개져야 한다. 거리를 셀 중심으로 재던 시절엔
            // 큰 벽(셀이 3x4)에서 기본 반경 1로 재분할이 한 번도 안 일어났다 — 그 회귀를 여기서 잡는다.
            failures += Check(cells.Count > baseCount,
                $"{c.label}: 기본값에서 타격점 재분할이 전혀 일어나지 않음 (기본 {baseCount}조각, 반경 {defRadius})");

            if (cells.Count > baseCount)
            {
                // 쪼개진 게 하필 타격점에서 가장 가까운 셀인가 — 거리순으로 안 고르면 예산이 엉뚱한
                // 구석 셀에 쓰여 "얻어맞은 데가 잘게 부서진다"가 성립하지 않는다.
                int nearest = 0;
                for (int i = 1; i < cells.Count; i++)
                {
                    if ((cells[i].center - hit).sqrMagnitude < (cells[nearest].center - hit).sqrMagnitude)
                        nearest = i;
                }
                failures += Check(cells[nearest].size.x < baseCellSize.x - 1e-4f,
                    $"{c.label}: 타격점에서 가장 가까운 셀이 안 쪼개짐 — 재분할이 거리순이 아니다");
            }

            Debug.Log($"[DestructionSystem] {c.label} {c.size}: div={div} → 기본 {baseCount}조각, " +
                      $"타격점 재분할 후 {cells.Count}조각");
        }

        if (failures == 0)
            Debug.Log("[DestructionSystem] 격자 분할 자체 점검 통과 — 개수 범위/부피 보존/셀 비율/타격점 재분할 이상 없음.");
        else
            Debug.LogError($"[DestructionSystem] 격자 분할 자체 점검 실패 {failures}건 — 위 로그 참고.");
    }

    private static int Check(bool condition, string failMessage)
    {
        if (condition) return 0;
        Debug.LogError($"[DestructionSystem] 격자 점검 실패 — {failMessage}");
        return 1;
    }

    private static float Aspect(Vector3 v)
    {
        float longest = Mathf.Max(v.x, Mathf.Max(v.y, v.z));
        float shortest = Mathf.Max(Mathf.Min(v.x, Mathf.Min(v.y, v.z)), 1e-4f);
        return longest / shortest;
    }

    /// <summary>
    /// "Breakable"은 Unity 기본 태그가 아니라 이 기믹 전용 커스텀 태그다. TagManager.asset을
    /// SerializedObject로 직접 편집하는 표준 Unity 에디터 패턴을 쓴다 — 이 저장소에 기존 태그
    /// 생성 선례가 없어(다른 기믹은 전부 이미 있는 "Player" 태그만 읽는다) 새로 작성했다.
    /// </summary>
    private static void EnsureTagExists(string tag)
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tagsProp = tagManager.FindProperty("tags");

        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag)
                return;
        }

        tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
        tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
        tagManager.ApplyModifiedProperties();
    }

    private static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder(MaterialSavePath))
            AssetDatabase.CreateFolder(SystemFolder, "Materials");
    }

    /// <summary>있으면 그대로 로드하고, 없을 때만 균열 텍스처를 새로 생성해 머티리얼과 함께
    /// 에셋으로 저장한다(CatapultMenuItem.LoadOrCreateMaterial과 같은 idempotent 패턴).</summary>
    private static Material LoadOrCreateCrackedMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(CrackMaterialPath);
        if (existing != null) return existing;

        Material mat = new Material(ResolveShader()) { mainTexture = LoadOrCreateCrackTexture() };
        AssetDatabase.CreateAsset(mat, CrackMaterialPath);
        return AssetDatabase.LoadAssetAtPath<Material>(CrackMaterialPath);
    }

    private static Texture2D LoadOrCreateCrackTexture()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(CrackTexturePath);
        if (existing != null) return existing;

        Texture2D tex = GenerateCrackTexture(CrackTextureSize);
        File.WriteAllBytes(CrackTexturePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(CrackTexturePath);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(CrackTexturePath);
    }

    /// <summary>밝은 돌 톤 바탕에 어두운 갈라짐 선을 절차적으로 그려 넣는다 — 실제 크랙 아트가
    /// 생기기 전까지 "이건 부술 수 있다"를 한눈에 알려주는 용도라 정교할 필요는 없다. 고정 시드를
    /// 써서 에셋을 지우고 다시 생성해도 매번 같은 패턴이 나온다.</summary>
    private static Texture2D GenerateCrackTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
        var baseColor = new Color(0.72f, 0.70f, 0.65f);
        var crackColor = new Color(0.16f, 0.14f, 0.12f);

        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = baseColor;
        tex.SetPixels(pixels);

        Random.InitState(12345);
        const int crackCount = 5;
        for (int c = 0; c < crackCount; c++)
        {
            Vector2 pos = new Vector2(Random.Range(0, size), Random.Range(0, size));
            float angle = Random.Range(0f, 360f);
            int segments = Random.Range(6, 12);
            for (int s = 0; s < segments; s++)
            {
                angle += Random.Range(-35f, 35f);
                float len = Random.Range(size * 0.05f, size * 0.12f);
                Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                Vector2 next = pos + dir * len;
                DrawLine(tex, pos, next, crackColor);
                pos = next;
            }
        }

        tex.Apply();
        return tex;
    }

    private static void DrawLine(Texture2D tex, Vector2 a, Vector2 b, Color color)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
        for (int i = 0; i <= steps; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, (float)i / steps);
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = Mathf.Clamp((int)p.x + dx, 0, tex.width - 1);
                int y = Mathf.Clamp((int)p.y, 0, tex.height - 1);
                tex.SetPixel(x, y, color);
            }
        }
    }

    /// <summary>현재 렌더 파이프라인에 맞는 셰이더를 반환한다(CatapultMenuItem.ResolveShader과
    /// 같은 방식 — Built-in/URP/HDRP 대응). 이 저장소 관례상 작은 패턴은 컴포넌트마다 각자
    /// 복제한다(CatapultLoadController.cs 상단 주석 참고).</summary>
    private static Shader ResolveShader()
    {
        var pipeline = GraphicsSettings.defaultRenderPipeline;
        if (pipeline == null)
        {
            Shader s = Shader.Find("Standard");
            if (s != null) return s;
        }
        else
        {
            string n = pipeline.GetType().Name;
            if (n.Contains("Universal") || n.Contains("URP"))
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit");
                if (s != null) return s;
            }
            else if (n.Contains("HighDefinition") || n.Contains("HDRP"))
            {
                Shader s = Shader.Find("HDRP/Lit");
                if (s != null) return s;
            }
        }

        Debug.LogWarning("[DestructionSystem] 렌더 파이프라인에 맞는 셰이더를 찾지 못해 기본 셰이더로 대체합니다.");
        return Shader.Find("Diffuse") ?? Shader.Find("Hidden/InternalErrorShader");
    }
}
