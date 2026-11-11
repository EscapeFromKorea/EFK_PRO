using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// WiringPort의 트리거 겹침 집계 자가검증. 플레이어는 트리거(Player_Mesh)와 솔리드(Player_Collider)
/// 콜라이더를 함께 가져 한 도형당 Enter/Exit가 여러 번 불리는데, 참가자 단위 목록이면 첫 Exit에
/// 아직 안에 있는 참가자가 사라진다. 콜라이더 수 단위 집계가 이를 막는지 확인한다.
///
/// [왜 리플렉션인가] OnTriggerEnter/Exit는 물리 엔진이 부르는 private 메서드라 EditMode에서는 직접
/// 부를 수밖에 없다. 저장소에 Test Framework 패키지가 없어 Editor 메뉴 + 배치모드 진입점 방식을 쓴다
/// (RoleClueTerminalSelfTest와 동일).
///
/// 실행: 메뉴 Tools &gt; Wiring Panel &gt; Run Port Self-Test, 또는 배치모드
/// `-batchmode -nographic -quit -executeMethod WiringPortSelfTest.RunFromCommandLine`
/// (실패가 하나라도 있으면 종료 코드 1).
/// </summary>
public static class WiringPortSelfTest
{
    private static int passed;
    private static int failed;

    [MenuItem("Tools/Wiring Panel/Run Port Self-Test")]
    public static void RunFromMenu()
    {
        Run();
    }

    // -executeMethod WiringPortSelfTest.RunFromCommandLine
    public static void RunFromCommandLine()
    {
        bool ok = Run();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static bool Run()
    {
        passed = 0;
        failed = 0;

        MethodInfo enter = typeof(WiringPort).GetMethod("OnTriggerEnter", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo exit = typeof(WiringPort).GetMethod("OnTriggerExit", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo overlapsField = typeof(WiringPort).GetField("overlaps", BindingFlags.Instance | BindingFlags.NonPublic);
        Check("리플렉션 대상(OnTriggerEnter/Exit/overlaps) 존재", enter != null && exit != null && overlapsField != null);
        if (enter == null || exit == null || overlapsField == null) return Finish();

        var created = new List<GameObject>();
        try
        {
            GameObject portObj = new GameObject("Port_Test");
            created.Add(portObj);
            WiringPort port = portObj.AddComponent<WiringPort>();
            var overlaps = (Dictionary<PlayerMover, int>)overlapsField.GetValue(port);

            Collider aMesh, aBody, bMesh;
            PlayerMover a = NewPlayer("A", created, out aMesh, out aBody);
            PlayerMover b = NewPlayer("B", created, out bMesh, out _);

            void Enter(Collider c) => enter.Invoke(port, new object[] { c });
            void Exit(Collider c) => exit.Invoke(port, new object[] { c });

            // 콜라이더 2개를 가진 도형이 들어왔다가 하나만 먼저 나간다 — 아직 안에 있어야 한다.
            Enter(aMesh);
            Enter(aBody);
            Check("Enter 2회: 참가자 1명으로 집계", overlaps.Count == 1 && overlaps[a] == 2);
            Exit(aMesh);
            Check("콜라이더 하나만 Exit: 아직 트리거 안(회귀 방지)", overlaps.ContainsKey(a) && overlaps[a] == 1);
            Exit(aBody);
            Check("마지막 콜라이더 Exit: 목록에서 제거", !overlaps.ContainsKey(a) && overlaps.Count == 0);

            // 다른 도형은 서로 영향이 없어야 한다.
            Enter(aMesh);
            Enter(bMesh);
            Exit(aMesh);
            Check("도형별 독립 집계(A 나가도 B 유지)", !overlaps.ContainsKey(a) && overlaps.ContainsKey(b));
            Exit(bMesh);

            // Enter 없이 Exit만 와도 오류 없이 무시한다.
            Exit(aMesh);
            Check("Enter 없는 Exit: 무시(예외·음수 카운트 없음)", overlaps.Count == 0);

            // 도형이 트리거 안에서 파괴되면(Exit 미수신) 입력 시점에 정리된다.
            Enter(aMesh);
            Object.DestroyImmediate(a.gameObject);
            MethodInfo prune = typeof(WiringPort).GetMethod("PruneDestroyed", BindingFlags.Instance | BindingFlags.NonPublic);
            prune.Invoke(port, null);
            Check("파괴된 참가자는 정리됨", overlaps.Count == 0);
        }
        finally
        {
            foreach (GameObject go in created)
                if (go != null) Object.DestroyImmediate(go);
        }

        return Finish();
    }

    private static PlayerMover NewPlayer(string name, List<GameObject> created, out Collider mesh, out Collider body)
    {
        GameObject root = new GameObject("Player_" + name);
        created.Add(root);
        PlayerMover mover = root.AddComponent<PlayerMover>();

        GameObject meshObj = new GameObject("Player_Mesh");
        meshObj.transform.SetParent(root.transform, false);
        mesh = meshObj.AddComponent<BoxCollider>();

        GameObject bodyObj = new GameObject("Player_Collider");
        bodyObj.transform.SetParent(root.transform, false);
        body = bodyObj.AddComponent<BoxCollider>();
        return mover;
    }

    private static void Check(string name, bool condition)
    {
        if (condition) passed++;
        else failed++;
        Debug.Log($"[WiringPortSelfTest] {(condition ? "PASS" : "FAIL")}  {name}");
    }

    private static bool Finish()
    {
        Debug.Log($"[WiringPortSelfTest] 결과: PASS {passed} / FAIL {failed}");
        return failed == 0;
    }
}
