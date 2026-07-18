// 이 파일은 반드시 프로젝트의 "Editor" 폴더 안에 위치해야 합니다.
//    예: Assets/ScalingSystem/Editor/ShapeGimmickSetup.cs
//    Editor 폴더 밖에 두면 런타임 빌드 시 UnityEditor 네임스페이스 참조로 컴파일 에러가 발생합니다.

using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// 에디터 메뉴에서 씬에 ScalePad 5종을 자동으로 생성해주는 헬퍼입니다.
/// Unity 상단 메뉴 → Tools → ShapeGimmick → Create Pads Only 를 클릭하세요.
///
/// 플레이어 오브젝트 생성은 PlayerSystem/Editor/PlayerObjectMenuItem.cs
/// (Tools → PlayerSystem → Create Player)가 전담합니다 — 구/정육면체/정사면체 3단 계층,
/// 자유 회전 물리 구르기, 접지·Tab 전환 기믹과 호환되는 유일한 정식 생성 경로입니다.
/// (예전에는 이 파일에도 CreateFullSetup으로 플레이어를 만들었으나, FreezeRotation 고정·
/// 정사면체 Sphere 대체 등 구식 결과물이라 현행 기믹과 맞지 않아 제거했습니다.)
/// </summary>
public static class ShapeGimmickSetup
{
    // ① 머티리얼을 저장할 에셋 경로
    private const string MaterialSavePath = "Assets/ScalingSystem/Materials";

    /// <summary>
    /// 패드 5개를 생성합니다. 플레이어 오브젝트는 Tools → PlayerSystem → Create Player로 따로 만드세요.
    /// Tools → ShapeGimmick → Create Pads Only
    /// </summary>
    [MenuItem("Tools/ShapeGimmick/Create Pads Only")]
    public static void CreatePadsOnly()
    {
        EnsureMaterialFolder();

        // 패드들을 묶을 루트 그룹 (플레이어 루트와 분리)
        GameObject padRoot = new GameObject("ShapeGimmick_Pads");
        Undo.RegisterCreatedObjectUndo(padRoot, "Create ShapeGimmick Pads");

        CreatePadsUnder(padRoot);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[ShapeGimmick] 패드 5개 생성 완료!");
        Selection.activeGameObject = padRoot;
    }

    // ────────────────────────────────────────────────────────
    // 내부 공통 헬퍼
    // ────────────────────────────────────────────────────────

    /// <summary>머티리얼 저장 폴더가 없으면 생성합니다.</summary>
    private static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/ScalingSystem"))
            AssetDatabase.CreateFolder("Assets", "ScalingSystem");
        if (!AssetDatabase.IsValidFolder(MaterialSavePath))
            AssetDatabase.CreateFolder("Assets/ScalingSystem", "Materials");
    }

    /// <summary>패드 5개를 지정한 부모 오브젝트 아래에 생성합니다.</summary>
    private static void CreatePadsUnder(GameObject parent)
    {
        float padY = 0.05f;
        CreatePad(parent, "Pad_IncreaseVertical",   ScalePad.EPadType.IncreaseVertical,   new Vector3(-4, padY, 6), new Color(0.2f, 0.8f, 0.2f));
        CreatePad(parent, "Pad_DecreaseVertical",   ScalePad.EPadType.DecreaseVertical,   new Vector3(-2, padY, 6), new Color(0.8f, 0.2f, 0.2f));
        CreatePad(parent, "Pad_IncreaseHorizontal", ScalePad.EPadType.IncreaseHorizontal, new Vector3( 0, padY, 6), new Color(0.2f, 0.4f, 1.0f));
        CreatePad(parent, "Pad_DecreaseHorizontal", ScalePad.EPadType.DecreaseHorizontal, new Vector3( 2, padY, 6), new Color(1.0f, 0.6f, 0.0f));
        CreatePad(parent, "Pad_Reset",              ScalePad.EPadType.Reset,              new Vector3( 4, padY, 6), new Color(0.9f, 0.9f, 0.0f));
    }

    private static void CreatePad(GameObject parent, string name, ScalePad.EPadType padType, Vector3 pos, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        // ② Undo 등록: 생성 직후 바로 등록
        Undo.RegisterCreatedObjectUndo(go, "Create Pad");

        go.name = name;
        go.transform.SetParent(parent.transform);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(1.5f, 0.1f, 1.5f);

        BoxCollider col = go.GetComponent<BoxCollider>();
        col.isTrigger = true;

        // ① 머티리얼을 에셋으로 저장하여 씬 재로딩 후에도 유지
        // ⑥ 렌더 파이프라인에 따라 셰이더 자동 선택 (Built-in / URP / HDRP 대응)
        Shader shader = ResolveShader();
        Material mat = new Material(shader);
        mat.color = color;
        string matPath = $"{MaterialSavePath}/{name}_Mat.mat";

        // B - 동일 경로에 에셋이 이미 존재하면 덮어쓰기 (중복 실행 시 CreateAsset 에러 방지)
        Material existingMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existingMat != null)
        {
            // 기존 에셋의 프로퍼티를 직접 갱신하여 참조를 유지한 채 덮어씀
            existingMat.shader = shader;
            existingMat.color = color;
            EditorUtility.SetDirty(existingMat);
        }
        else
        {
            AssetDatabase.CreateAsset(mat, matPath);
        }
        Material savedMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
            r.sharedMaterial = savedMat;

        ScalePad pad = go.AddComponent<ScalePad>();
        pad.padType = padType;
        pad.defaultColor = color;
    }

    /// <summary>
    /// ⑥ 현재 프로젝트의 렌더 파이프라인에 맞는 셰이더를 반환합니다.
    /// Built-in → Standard / URP → Universal Render Pipeline/Lit / HDRP → HDRP/Lit
    /// </summary>
    private static Shader ResolveShader()
    {
        var pipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;

        if (pipeline == null)
        {
            // Built-in Render Pipeline
            Shader s = Shader.Find("Standard");
            if (s != null) return s;
        }
        else
        {
            string pipelineName = pipeline.GetType().Name;

            if (pipelineName.Contains("Universal") || pipelineName.Contains("URP"))
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit");
                if (s != null) return s;
            }
            else if (pipelineName.Contains("HighDefinition") || pipelineName.Contains("HDRP"))
            {
                Shader s = Shader.Find("HDRP/Lit");
                if (s != null) return s;
            }
        }

        // 최후 fallback: 어떤 파이프라인에서도 동작하는 기본 셰이더
        Debug.LogWarning("[ShapeGimmick] 렌더 파이프라인에 맞는 셰이더를 찾지 못했습니다. 기본 Diffuse로 대체합니다.");
        return Shader.Find("Diffuse") ?? Shader.Find("Hidden/InternalErrorShader");
    }
}