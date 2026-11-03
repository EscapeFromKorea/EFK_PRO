#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>배치 실행 가능한 플레이 모드 스모크 검사. 실제 사용자 입력 완주 검사는 별도로 수행한다.</summary>
[InitializeOnLoad]
public static class KitchenTenSectorPlaySmoke
{
    private const string ActiveKey = "Kitchen10_PlaySmoke_Active";
    private const string PhaseKey = "Kitchen10_PlaySmoke_Phase";
    private static double nextStep;

    static KitchenTenSectorPlaySmoke() => EditorApplication.update += Update;

    [MenuItem("Tools/Kitchen Map2/Run Play Mode Smoke")]
    public static void Start()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(KitchenTenSectorBuilder.ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetBool(ActiveKey, true);
        EditorPrefs.SetInt(PhaseKey, 0);
        nextStep = 0;
        EditorApplication.isPlaying = true;
    }

    private static void Update()
    {
        if (!EditorPrefs.GetBool(ActiveKey, false) || !EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup < nextStep) return;
        try
        {
            int phase = EditorPrefs.GetInt(PhaseKey, 0);
            PlayerMover[] players = UnityEngine.Object.FindObjectsOfType<PlayerMover>();
            if (players.Length != 3) throw new Exception("플레이어 수: " + players.Length);

            if (phase == 0)
            {
                ThreadBridge[] bridges = UnityEngine.Object.FindObjectsOfType<ThreadBridge>();
                if (bridges.Length != 5) throw new Exception("줄다리 수: " + bridges.Length);
                foreach (ThreadBridge bridge in bridges)
                {
                    Transform span = bridge.transform.Find("ThreadBridge_Span0");
                    if (span == null || !span.gameObject.activeSelf ||
                        span.GetComponentsInChildren<BoxCollider>().Length < bridge.segmentCount)
                        throw new Exception("줄다리 런타임 콜라이더 생성 실패: " + bridge.name);
                }
                ExitWeightPlate firstPlate = UnityEngine.Object.FindObjectsOfType<ExitWeightPlate>()
                    .OrderBy(p => p.name).FirstOrDefault();
                if (firstPlate == null) throw new Exception("3인 합류판이 없습니다.");
                Teleport(players, firstPlate.transform.position - Vector3.up * .06f);
                EditorPrefs.SetInt(PhaseKey, 1);
                nextStep = EditorApplication.timeSinceStartup + 1.0;
                Debug.Log("[Kitchen10 Smoke] 플레이어 3명과 줄다리 5경간의 런타임 콜라이더 확인");
                return;
            }

            if (phase >= 1 && phase <= 10)
            {
                ExitWeightPlate[] plates = UnityEngine.Object.FindObjectsOfType<ExitWeightPlate>()
                    .OrderBy(p => p.name).ToArray();
                if (plates.Length != 10) throw new Exception("3인 합류판 수: " + plates.Length);
                ExitWeightPlate current = plates[phase - 1];
                if (current.exitBarriers[0].activeSelf)
                {
                    string playerState = string.Join("; ", players.Select(p =>
                    {
                        Rigidbody body = p.GetComponent<Rigidbody>();
                        return $"{p.name} pos={body.position} weight={PlayerWeight.Of(body):0.###}";
                    }));
                    MethodInfo totalWeight = typeof(ExitWeightPlate).GetMethod("TotalWeight", BindingFlags.NonPublic | BindingFlags.Instance);
                    throw new Exception($"세 명이 판 위에 있어도 관문이 열리지 않았습니다: {current.name}, " +
                        $"plate={current.transform.position}, registeredWeight={totalWeight?.Invoke(current, null)}, players={playerState}");
                }
                if (phase < 10)
                    Teleport(players, plates[phase].transform.position - Vector3.up * .06f);
                else
                    Teleport(players, new Vector3(55, 22.05f, 98));
                EditorPrefs.SetInt(PhaseKey, phase + 1);
                nextStep = EditorApplication.timeSinceStartup + 1.0;
                Debug.Log("[Kitchen10 Smoke] 3인 관문 " + phase + "/10 래치 확인");
                return;
            }

            KitchenTeamGoal goal = UnityEngine.Object.FindObjectOfType<KitchenTeamGoal>();
            if (goal == null) throw new Exception("목표 트리거가 없습니다.");
            FieldInfo completed = typeof(KitchenTeamGoal).GetField("completed", BindingFlags.NonPublic | BindingFlags.Instance);
            if (completed == null || !(bool)completed.GetValue(goal))
                throw new Exception("세 명이 창문 뒤에 있어도 완주 처리가 발생하지 않았습니다.");
            Debug.Log("[Kitchen10 Smoke] 통과: 런타임 줄다리, 3인 관문 10개, 전원 목표 처리. 입력 기반 전체 완주는 별도 검사 필요.");
            Finish(0);
        }
        catch (Exception e)
        {
            Debug.LogError("[Kitchen10 Smoke] 실패: " + e);
            Finish(1);
        }
    }

    private static void Teleport(PlayerMover[] players, Vector3 center)
    {
        for (int i = 0; i < players.Length; i++)
        {
            Rigidbody body = players[i].GetComponent<Rigidbody>();
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = center + new Vector3((i - 1) * 1.2f, 0, 0);
        }
        Physics.SyncTransforms();
    }

    private static void Finish(int code)
    {
        EditorPrefs.DeleteKey(ActiveKey);
        EditorPrefs.DeleteKey(PhaseKey);
        if (Application.isBatchMode) EditorApplication.Exit(code);
        else EditorApplication.isPlaying = false;
    }
}
#endif
