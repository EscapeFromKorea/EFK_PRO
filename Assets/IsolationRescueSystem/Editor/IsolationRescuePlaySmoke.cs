using System.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 격리 구출 PlayMode 스모크 — 시험 배치 메뉴로 만든 격리실을 실제 물리로 돌려 본다. EditMode 자가검증
/// (IsolationRescueSceneSelfTest)이 못 보는 부분, 곧 실제 트리거 이벤트·문이 내려오며 끼는 판정·UnityEvent
/// 배선이 문을 움직이는지를 확인한다. docs/PRD/IsolationRescue.md §2, §4.
///
/// [사람 대신 공을 굴린다]
/// 참가자는 PlayerMover.ExternallyDriven(이동·감쇠 모두 중단) 공이고, 속도를 스크립트가 직접 준다. 레버는
/// LeverHead 피벗을 직접 돌려 "당긴" 상태를 흉내 낸다(밀기 물리 자체는 검증 범위 밖). E 키 입력은 쓰지 않고
/// 버튼의 Press()를 부른다.
///
/// [진행 방식]
/// 배치모드에서 -quit 없이 실행한다: executeMethod가 PlayMode로 진입시키고, 도메인 리로드 뒤
/// [InitializeOnLoad]가 러너를 띄워 끝나면 종료 코드로 Exit한다.
/// `-batchmode -nographic -executeMethod IsolationRescuePlaySmoke.RunFromCommandLine`
/// (실패가 하나라도 있거나 워치독이 만료되면 종료 코드 1).
///
/// [이 검증이 다루지 않는 것] 사람이 레버를 밀 때의 감각, 화면(OnGUI) 배치, E 키 반응, 도형별(구/정육면체/
/// 정사면체) 끼임 차이(C2-07), 멀티플레이.
/// </summary>
[InitializeOnLoad]
public static class IsolationRescuePlaySmoke
{
    private const string PendingKey = "IsolationRescuePlaySmoke.Pending";

    static IsolationRescuePlaySmoke()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/Isolation Rescue/Run Play Smoke (enters Play Mode)")]
    public static void RunFromMenu()
    {
        RunFromCommandLine();
    }

    // -executeMethod IsolationRescuePlaySmoke.RunFromCommandLine  (-quit를 붙이지 않는다)
    public static void RunFromCommandLine()
    {
        SessionState.SetBool(PendingKey, true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        if (!SessionState.GetBool(PendingKey, false)) return;

        SessionState.SetBool(PendingKey, false);
        new GameObject("IsolationPlaySmokeRunner").AddComponent<IsolationPlaySmokeRunner>();
    }
}

public class IsolationPlaySmokeRunner : MonoBehaviour
{
    private class Ball
    {
        public GameObject go;
        public Rigidbody rb;
        public PlayerMover mover;
        public Vector3 velocity; // 수평 목표 속도(FixedUpdate마다 대입)
    }

    private const float OpenY = 3.936f + 2.8f;   // 이 이상이면 열린 것으로 본다(문 중심 3.936 + 3 개방)
    private const float ClosedY = 3.936f + 0.15f; // 이 이하면 닫힌 것으로 본다
    private const float Watchdog = 150f;

    private int passed;
    private int failed;
    private float startedAt;
    private bool finished;
    private bool waitOk;
    private IsolationRescueMenuItem.Result r;
    private readonly System.Collections.Generic.List<Ball> balls = new System.Collections.Generic.List<Ball>();

    private void Start()
    {
        Application.runInBackground = true;
        startedAt = Time.realtimeSinceStartup;
        StartCoroutine(Main());
    }

    private void Update()
    {
        if (!finished && Time.realtimeSinceStartup - startedAt > Watchdog)
        {
            Check("워치독: 시간 안에 시나리오가 끝남", false);
            Finish();
        }
    }

    private void FixedUpdate()
    {
        // 공의 수평 속도를 목표 속도로 유지하고 수직 속도(중력)는 그대로 둔다.
        foreach (Ball b in balls)
        {
            if (b.rb == null) continue;
            Vector3 v = b.rb.velocity;
            b.rb.velocity = new Vector3(b.velocity.x, v.y, b.velocity.z);
        }
    }

    // ── 시나리오 ──────────────────────────────────────────────────────────

    private IEnumerator Main()
    {
        r = IsolationRescueMenuItem.Build(Vector3.zero);
        IsolationRescueController c = r.controller;
        Ball a = NewBall("A", new Vector3(0f, 0.6f, -8f));
        Ball b = NewBall("B", new Vector3(3f, 0.6f, -12f));

        // 1) 진입문은 시작하면 열려 있어야 한다
        yield return new WaitForSeconds(2.5f);
        Check("진입문: 시작 후 열림(Start에서 Open)", DoorY(r.entryDoor.door) > OpenY);
        Check("초기: 격리 대기 상태", c.Current == IsolationRescueController.State.Idle);

        // 2) A가 진입점을 안쪽으로 통과 → 격리 확정 → 문이 안전하게 닫힘
        yield return Walk(a, new Vector3(0f, 0f, 6f), 9f);
        yield return WaitFor(() => c.Current == IsolationRescueController.State.Preparing, 3f);
        Check("진입: 안쪽으로 통과하면 실제 트리거 이벤트로 격리 확정",
            waitOk && c.InsidePlayer == a.go);
        yield return WaitFor(() => r.entryDoor.Current == IsolationEntryDoor.Phase.Closed, 6f);
        Check("진입문: 닫힘 위치 도달을 실제 물리로 감지(IsAtClosedPosition)", waitOk);
        Check("진입문: 닫힌 높이", DoorY(r.entryDoor.door) < ClosedY);

        // 3) 양쪽 준비 → 진행
        r.readyOut.Press();
        r.readyIn.Press();
        yield return null;
        Check("준비: 양쪽 확인으로 1단계 시작", c.Current == IsolationRescueController.State.InProgress);

        // 4) 오답 → 순환, 문은 안 열림
        int first = c.CurrentOrder[0];
        int wrong = first == 3 ? 2 : 3;
        yield return Pull(wrong);
        Check("레버: 오답 입력으로 정답 순서 순환(버전 1)·입력 삭제",
            c.QuestionVersion == 1 && c.InputCount == 0);
        yield return new WaitForSeconds(0.6f);
        Check("레버: 오답이면 사이 문은 닫힌 채", DoorY(r.passDoors[0]) < ClosedY);

        // 5) 단계 3개를 정답으로 푼다 — 단계 완료마다 대응 문이 실제로 올라가야 한다
        for (int stage = 0; stage < 3; stage++)
        {
            for (int i = 0; i < IsolationRescueController.LeverCount; i++)
                yield return Pull(c.CurrentOrder[c.InputCount]);

            if (stage < 2)
            {
                Check($"단계 {stage + 1}: 완료 → 다음 단계", c.StageIndex == stage + 1);
                yield return new WaitForSeconds(2.8f);
                Check($"단계 {stage + 1}: 사이 문 {stage + 1}이 UnityEvent 배선으로 실제로 열림",
                    DoorY(r.passDoors[stage]) > OpenY);
                if (stage == 0)
                    Check("단계 1: 아직 사이 문 2는 닫힘", DoorY(r.passDoors[1]) < ClosedY);
            }
        }
        Check("단계 3: 완료 → 최종 해제 대기", c.Current == IsolationRescueController.State.AwaitFinalRelease);

        // 6) 전원 OFF에서 해제 시도 → 거부 + 사유
        r.release.Press();
        Check("해제: 전원 OFF면 거부·진행 보존·사유 표시",
            c.Current == IsolationRescueController.State.AwaitFinalRelease && r.hud.CurrentMessage.Contains("전원"));

        // 7) B가 실제로 전원 판을 밟는다
        yield return Walk(b, new Vector3(0f, 0f, -22f), 9f);
        yield return WaitFor(() => c.PowerOn, 3f);
        Check("전원: 판을 밟으면 실제 트리거 이벤트로 켜짐", waitOk);

        // 8) 해제 → 성공 → 해제문 개방
        r.release.Press();
        Check("해제: 전원 ON·마감 전 → 성공", c.Current == IsolationRescueController.State.Success);
        yield return new WaitForSeconds(2.8f);
        Check("성공: 해제문(CH4 숏컷)이 UnityEvent 배선으로 실제로 열림", DoorY(r.releaseDoor) > OpenY);
        Check("성공: 타이머 정지", !r.timer.IsRunning);

        // 9) 전체 재시작 → 문 닫힘·진입문 열림, 전원 스위치 상태는 보존
        c.FullRestart();
        yield return new WaitForSeconds(3.2f);
        Check("재시작: 대기 복귀·시도 번호 증가", c.Current == IsolationRescueController.State.Idle && c.Attempt == 1);
        Check("재시작: 열렸던 문이 모두 다시 닫힘",
            DoorY(r.passDoors[0]) < ClosedY && DoorY(r.passDoors[1]) < ClosedY && DoorY(r.releaseDoor) < ClosedY);
        Check("재시작: 진입문은 열림", DoorY(r.entryDoor.door) > OpenY);
        Check("재시작: 물리 스위치 상태(PowerOn)는 보존(B가 아직 판 위)", c.PowerOn);

        // 10) 끼임 안전검사: A가 다시 통과해 격리가 확정되는 순간 B가 문틈에 서 있으면 취소돼야 한다
        Teleport(a, new Vector3(0f, 0.6f, -6f));
        yield return new WaitForSeconds(0.3f);
        yield return Walk(a, new Vector3(0f, 0f, 6f), 9f);
        yield return WaitFor(() => c.Current == IsolationRescueController.State.Preparing, 3f);
        Check("끼임 시험: 두 번째 격리 확정", waitOk);
        Teleport(b, new Vector3(0f, 0.6f, 0f)); // 문틈에 선다
        // 4.5초 안에 취소돼야 한다 — 끼임 유예(1초) 경로만 통과하고, 닫힘 시간 초과(6초) 경로로는 못 통과한다.
        yield return WaitFor(() => c.Current == IsolationRescueController.State.Idle, 4.5f);
        Check("끼임 시험: 문틈에 참가자가 끼면 유예(1초) 뒤 격리 취소·대기 복귀", waitOk);
        yield return WaitFor(() => DoorY(r.entryDoor.door) > OpenY, 6f);
        Check("끼임 시험: 취소 후 진입문이 다시 열림", waitOk && r.entryDoor.Current == IsolationEntryDoor.Phase.Open);

        Finish();
    }

    // ── 도구 ──────────────────────────────────────────────────────────────

    private Ball NewBall(string name, Vector3 position)
    {
        var ball = new Ball();
        ball.go = new GameObject("Player_" + name);
        ball.go.tag = "Player";
        ball.go.transform.position = position;

        var solid = ball.go.AddComponent<SphereCollider>();
        solid.radius = 0.5f;

        // 실제 도형처럼 트리거 콜라이더(Player_Mesh)를 자식에 하나 더 둔다 — 콜라이더 2개 집계 경로를 탄다.
        var meshObj = new GameObject("Player_Mesh");
        meshObj.transform.SetParent(ball.go.transform, false);
        meshObj.tag = "Player";
        var trigger = meshObj.AddComponent<SphereCollider>();
        trigger.radius = 0.5f;
        trigger.isTrigger = true;

        ball.mover = ball.go.AddComponent<PlayerMover>(); // Rigidbody 자동 추가(RequireComponent)
        ball.mover.ExternallyDriven = true;               // 이동·감쇠를 PlayerMover가 건드리지 않게
        ball.rb = ball.go.GetComponent<Rigidbody>();
        ball.rb.constraints = RigidbodyConstraints.FreezeRotation;
        ball.rb.drag = 0f;
        balls.Add(ball);
        return ball;
    }

    private static void Teleport(Ball ball, Vector3 position)
    {
        ball.velocity = Vector3.zero;
        ball.rb.velocity = Vector3.zero;
        ball.rb.position = position;
        ball.go.transform.position = position;
    }

    // 목표 z(또는 xz 평면 거리)에 닿을 때까지 걷는다. 도착하면 멈춘다.
    private IEnumerator Walk(Ball ball, Vector3 target, float timeout)
    {
        float t = 0f;
        while (t < timeout)
        {
            Vector3 delta = target - ball.go.transform.position;
            delta.y = 0f;
            if (delta.magnitude < 0.6f) break;
            ball.velocity = delta.normalized * 4f;
            t += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
        ball.velocity = Vector3.zero;
        yield return new WaitForFixedUpdate();
    }

    // SequenceLever는 LeverHead 각도를 본다 — 피벗을 당긴 각도로 돌렸다가 되돌려 한 번 당긴 것을 흉내 낸다.
    private IEnumerator Pull(int leverNumber)
    {
        Transform pivot = r.levers[leverNumber - 1].lever.leverPivot;
        pivot.localRotation = Quaternion.Euler(0f, 45f, 0f);
        yield return new WaitForSeconds(0.15f);
        pivot.localRotation = Quaternion.Euler(0f, -45f, 0f);
        yield return new WaitForSeconds(0.15f);
    }

    private IEnumerator WaitFor(System.Func<bool> condition, float timeout)
    {
        waitOk = false;
        float t = 0f;
        while (t < timeout)
        {
            if (condition())
            {
                waitOk = true;
                yield break;
            }
            t += Time.deltaTime;
            yield return null;
        }
        waitOk = condition();
    }

    private static float DoorY(doorPhysics d) => d.transform.position.y;

    private void Check(string name, bool condition)
    {
        if (condition) passed++;
        else failed++;
        Debug.Log($"[PlaySmoke] {(condition ? "PASS" : "FAIL")}  {name}");
    }

    private void Finish()
    {
        if (finished) return;
        finished = true;
        Debug.Log($"[PlaySmoke] 결과: PASS {passed} / FAIL {failed}");
        EditorApplication.Exit(failed == 0 ? 0 : 1);
    }
}
