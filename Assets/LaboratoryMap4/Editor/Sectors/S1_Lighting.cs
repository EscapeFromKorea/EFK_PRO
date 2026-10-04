#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// S1 복도 노란 비상등 11개 배치(S1_설계.md §11-4, 지시서 S1-L 계약 C1). S1_Builder가
/// `S1_Lighting.Build(g, 10f, 100f, 4f)`로 부른다. parent = S1 Generated 루트(섹터 로컬 좌표),
/// ceilingY = 복도 천장 아랫면 로컬 y. Generated는 매 Generate마다 비워지므로 여기서는 기존
/// 오브젝트를 찾지 않고 항상 새로 만든다.
/// </summary>
public static class S1_Lighting
{
    /// <summary>[제안 §11-4] 등 색 RGB(노란색이라는 것 자체는 사용자 확정 ④, 수치는 제안 — 실측 조정).</summary>
    private static readonly Color LightColor = new Color(1.0f, 0.75f, 0.1f);
    /// <summary>[제안 §11-4] 점광원 범위(실측 조정 대상).</summary>
    private const float LightRange = 10f;
    /// <summary>[추정 — 설계서에 값 없음, 실측 필요] 점광원 강도.
    /// Built-in 기본 픽셀 조명 수(Quality 기본 4) 한계 안에서 11개 중 카메라 근접 등 위주로
    /// 픽셀 조명이 배정되므로, 먼 등이 버텍스 조명으로 떨어져도 어색하지 않도록 중간값(1.5)로
    /// 잡는다. 실측 필요.</summary>
    private const float LightIntensity = 1.5f;
    /// <summary>[추정] 등을 천장 아랫면에서 얼마나 내려 다는지(§11-4에 값 없음 — S1-L 추정, 실측 필요).</summary>
    private const float DropFromCeiling = 0.3f;
    /// <summary>[제안 §11-4] 등 간격("8m 간격").</summary>
    private const float LightSpacing = 8f;
    /// <summary>[계산] 복도 시작에서 첫 등까지 여유(§11-1·§11-4 [제안] 복도 z10~100, 등 z15부터 → 5).</summary>
    private const float StartMargin = 5f;
    /// <summary>[계산] 복도 끝에서 마지막 등까지 여유(대칭).</summary>
    private const float EndMargin = 5f;
    /// <summary>[추정] 등 시각물(구) 반지름 — 공통규칙 "도형 크기 약 1U" 기준 눈에 띄는 작은 등.</summary>
    private const float VisualRadius = 0.15f;
    /// <summary>[추정] 깜빡임 시드 기준값(검사 재현용, 등 번호를 더한다). 임의값.</summary>
    private const int SeedBase = 4100;

    private const string MaterialDir = "Assets/LaboratoryMap4/Materials/Generated";
    private const string MaterialPath = MaterialDir + "/M4_S1_EmergencyLight.mat";

    /// <summary>복도 천장에 노란 비상등을 z 15,23,...,95(11개) 배치한다.</summary>
    public static void Build(Transform parent, float corridorZStart, float corridorZEnd, float ceilingY)
    {
        if (parent == null)
        {
            Debug.LogError("[S1_Lighting] parent가 null이다 — 호출부(S1_Builder) 확인 필요.");
            return;
        }

        GameObject group = new GameObject("VIS_S1_EmergencyLights");
        group.transform.SetParent(parent, false);
        group.transform.localPosition = Vector3.zero;
        group.transform.localRotation = Quaternion.identity;

        Material mat = GetOrCreateEmissiveMaterial();
        float lightY = ceilingY - DropFromCeiling;

        // [계산] 정수 개수로 먼저 구해 float 누적 오차를 피한다: 복도 10~100 → (95-15)/8+1 = 11개(z 15,23,…,95).
        float firstZ = corridorZStart + StartMargin;
        float lastZ = corridorZEnd - EndMargin;
        int count = lastZ < firstZ ? 0 : Mathf.FloorToInt((lastZ - firstZ) / LightSpacing + 0.001f) + 1;
        int index = 0;
        for (; index < count; index++)
        {
            float z = firstZ + index * LightSpacing;
            BuildOneLight(group.transform, index, new Vector3(0f, lightY, z), mat);
        }
        if (count != 11)
            Debug.LogWarning($"[S1_Lighting] 등 개수 {count} ≠ 설계 11(§11-4) — 복도 범위 {corridorZStart}~{corridorZEnd} 확인.");

        Debug.Log($"[S1_Lighting] 노란 비상등 {index}개 생성 — z {corridorZStart + StartMargin:F0}부터 " +
                  $"{LightSpacing:F0}m 간격, y={lightY:F2}(천장 {ceilingY:F2} - {DropFromCeiling:F2}).");
    }

    private static void BuildOneLight(Transform group, int index, Vector3 localPos, Material mat)
    {
        GameObject lampGo = new GameObject($"VIS_S1_EmLight_{index:00}");
        lampGo.transform.SetParent(group, false);
        lampGo.transform.localPosition = localPos;
        lampGo.transform.localRotation = Quaternion.identity;

        Light light = lampGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = LightColor;
        light.range = LightRange;
        light.intensity = LightIntensity;
        // Built-in 픽셀 조명 수(Quality 기본 4) 한계 대응 — ProjectSettings 불변, 오브젝트 설정만:
        // Auto로 두면 카메라 거리 기준으로 가까운 등이 우선 픽셀 조명이 되고 먼 등은 자동으로
        // 버텍스 조명으로 내려가 한꺼번에 켜져도 성능이 무너지지 않는다. Important로 고정하면
        // 11개 전부가 픽셀 조명을 다투어 오히려 더 나쁘다.
        light.renderMode = LightRenderMode.Auto;
        light.shadows = LightShadows.None; // 11개 점광원 그림자는 비용이 커서 끈다(연출 전용) [추정].

        GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        mesh.name = $"VIS_S1_EmLight_{index:00}_Mesh";
        mesh.transform.SetParent(lampGo.transform, false);
        mesh.transform.localPosition = Vector3.zero;
        mesh.transform.localScale = Vector3.one * (VisualRadius * 2f);
        Object.DestroyImmediate(mesh.GetComponent<Collider>()); // 콜라이더 0개(공통규칙 — 시각 전용).
        MeshRenderer mr = mesh.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;

        Lab_EmergencyLightFlicker flicker = lampGo.AddComponent<Lab_EmergencyLightFlicker>();
        var so = new SerializedObject(flicker);
        so.FindProperty("targetLight").objectReferenceValue = light;
        so.FindProperty("targetRenderer").objectReferenceValue = mr;
        // 등마다 다른 시드(고정 시드 검사 시에도 11개가 동시에 깜빡이지 않게). useFixedSeed는 기본 false.
        so.FindProperty("seed").intValue = SeedBase + index;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Standard 셰이더 발광 머티리얼을 Materials/Generated에 코드로 만든다(있으면 재사용).
    /// Map4Palette·팀 머티리얼은 건드리지 않는다(Map4Build.GetMaterial 방식 참고, 팔레트 항목이
    /// 아니라 이 등 전용이라 별도 경로로 직접 생성).</summary>
    private static Material GetOrCreateEmissiveMaterial()
    {
        EnsureDir(MaterialDir);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }

        mat.color = LightColor;
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mat.SetColor("_EmissionColor", LightColor);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void EnsureDir(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string[] parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{cur}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
#endif
