#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class V3ProgressionValidator
{
    public static void RebuildScene()
    {
        const string path = "Assets/Scenes/Map2_Kitchen/Scenes/TeamKitchen.unity";
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        bool ok = V3Build.BuildAll();
        ok &= V3Play.SetupPlay();
        ok &= Run();
        if (!ok) throw new System.InvalidOperationException("TeamKitchen S5 재생성 또는 검증 실패");
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, path))
            throw new System.InvalidOperationException("TeamKitchen 저장 실패");
        Debug.Log("[KitchenMapV3] S5 팬트리 경사 동선 저장 완료 — 레일카 없음.");
    }

    [MenuItem("Tools/KitchenMapV3/5. Validate Full Route", false, 20)]
    public static bool Run()
    {
        var errors = new List<string>();
        GameObject root = GameObject.Find(V3.RootName);
        if (root == null) { Debug.LogError("[KitchenMapV3] 완주 검증: 맵 루트가 없습니다."); return false; }

        string[] required = {
            "Start_WeightPlate", "Island_Crossing", "S5_Ramp_01", "S5_Ramp_02",
            "S5_Ramp_03", "S5_Ramp_04", "S5_Ramp_05_ToRidge", "S5_Ridge_Connector", "Goal_WeightPlate",
            "Goal_SafeLanding"
        };
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (string name in required)
            if (!all.Any(t => t.name == name)) errors.Add("필수 동선 오브젝트 누락: " + name);

        ThreadBridge island = root.GetComponentsInChildren<ThreadBridge>(true).FirstOrDefault(x => x.name == "Island_Crossing");
        if (island == null || island.anchorA == null || island.anchorB == null) errors.Add("아일랜드 줄다리 배선 누락");
        else if (island.segmentWidth < 1.2f) errors.Add($"아일랜드 줄다리 폭 부족: {island.segmentWidth:F2}");

        if (root.GetComponentsInChildren<RailCart>(true).Length != 0)
            errors.Add("요청 범위 밖 레일카가 씬에 남아 있음");

        RespawnZone[] checkpoints = root.GetComponentsInChildren<RespawnZone>(true);
        if (checkpoints.Length != 12) errors.Add($"체크포인트 수 오류: {checkpoints.Length}/12");

        if (errors.Count > 0) {
            Debug.LogError("[KitchenMapV3] 완주 동선 검증 실패\n- " + string.Join("\n- ", errors));
            return false;
        }
        Debug.Log("[KitchenMapV3] 완주 동선 정적 검증 통과 — 체크포인트 12, S5 팬트리 경사 동선, 기존 실다리·앵커, 출구 연결 확인. 레일카 0개.");
        return true;
    }
}
#endif
