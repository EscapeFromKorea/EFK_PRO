// 이 파일은 반드시 "Editor" 폴더 안에 위치해야 한다.
//    Editor 폴더 밖에 두면 런타임 빌드 시 UnityEditor 참조로 컴파일 에러가 난다.

using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > ZeroGravityBubble 메뉴로 씬에 무중력 기류 버블 하나를 생성한다.
/// SphereCollider(트리거) + ZeroGravityBubble 컴포넌트 + rim-light 경계 시각 + 상승 파티클을 만든다.
/// 외부 에셋 없이 내장 리소스(기본 파티클 머티리얼, 커스텀 rim 셰이더)만 사용한다.
///
/// 기존 에디터 세팅 패턴(CloudTrampolineSystem/RainbowBridgeSystem)을 따른다: SceneView 중앙 스폰,
/// Undo 등록, Selection 설정.
/// </summary>
public static class ZeroGravityBubbleMenuItem
{
    private const string MaterialSavePath = "Assets/ZeroGravityBubbleSystem/Materials";

    [MenuItem("Tools/ZeroGravityBubble")]
    private static void CreateZeroGravityBubble()
    {
        EnsureMaterialFolder();

        Vector3 origin = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            origin = SceneView.lastActiveSceneView.pivot;

        GameObject bubble = new GameObject("ZeroGravityBubble", typeof(SphereCollider), typeof(ZeroGravityBubble));
        bubble.transform.position = origin;
        Undo.RegisterCreatedObjectUndo(bubble, "Create ZeroGravityBubble");

        SphereCollider col = bubble.GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 4f;

        // 경계 시각 — rim-light 구 메시. 충돌 없음(순수 연출), 반경은 트리거와 맞춘다.
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Visual_Boundary";
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(bubble.transform, false);
        visual.transform.localScale = Vector3.one * (col.radius * 2f);
        visual.GetComponent<Renderer>().sharedMaterial = LoadOrCreateRimMaterial();

        // 상승 기류 파티클 — 내장 파티클 머티리얼만 사용.
        GameObject vfx = new GameObject("VFX_Bubble");
        vfx.transform.SetParent(bubble.transform, false);
        var ps = vfx.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.startLifetime = 2.5f;
        main.startSpeed = 1.5f;
        main.startSize = 0.15f;
        main.startColor = new Color(0.8f, 0.95f, 1f, 0.6f);
        main.maxParticles = 60;

        var emission = ps.emission;
        emission.rateOverTime = 12f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = col.radius;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        // x/y/z는 반드시 같은 커브 모드여야 한다(안 맞으면 매 프레임 에러 스팸). 셋 다 TwoConstants로
        // 통일하고 x/z는 0~0으로 고정해 순수 상승만 남긴다.
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        vfx.GetComponent<ParticleSystemRenderer>().sharedMaterial =
            AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = bubble;
        Debug.Log("[ZeroGravityBubble] 생성 완료. Player 태그 오브젝트로 안에 들어가 도형별로 뜨는 " +
                  "정도가 다른지 확인하세요 (세모 최고 / 구 중간 / 네모 거의 안 뜸).");
    }

    private static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/ZeroGravityBubbleSystem"))
            AssetDatabase.CreateFolder("Assets", "ZeroGravityBubbleSystem");
        if (!AssetDatabase.IsValidFolder(MaterialSavePath))
            AssetDatabase.CreateFolder("Assets/ZeroGravityBubbleSystem", "Materials");
    }

    private static Material LoadOrCreateRimMaterial()
    {
        string path = $"{MaterialSavePath}/GimmickBubbleRim.mat";
        Material cached = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (cached != null) return cached;

        Shader shader = Shader.Find("ZeroGravityBubble/Rim");
        if (shader == null)
        {
            Debug.LogError("[ZeroGravityBubble] ZeroGravityBubble/Rim 셰이더를 못 찾았다. " +
                            "Assets/ZeroGravityBubbleSystem/GimmickBubbleRim.shader 확인해라.");
            return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
        }

        Material mat = new Material(shader) { name = "GimmickBubbleRim" };
        mat.SetColor("_Color", new Color(0.4f, 0.8f, 1f, 0.12f));
        mat.SetColor("_RimColor", new Color(0.6f, 0.9f, 1f, 1f));
        mat.SetFloat("_RimPower", 3f);

        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
