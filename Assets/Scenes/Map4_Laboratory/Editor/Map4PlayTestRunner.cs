#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// F1-6 항목 2·4·5·6 — 플레이 모드 자동 검사. 메뉴로 실행하면 Master 씬을 열고 플레이 모드에
/// 들어가 러너(Driver, 이 파일 안에 같이 정의된 MonoBehaviour)를 스폰해 코루틴으로 검사를 돌린
/// 뒤, 결과를 검증/F1_playtests_&lt;시각&gt;.txt에 쓰고 자동으로 플레이 모드를 빠져나온다.
///
/// [패턴 출처] KitchenMapV3/Assets/KitchenMapV3/Editor/V3_GateTest.cs — SessionState pending
/// 플래그 + [InitializeOnLoadMethod]/playModeStateChanged 부트스트랩. 로직 복제 아님, 이 파일은
/// 맵4 전용 신규 코드다.
///
/// [-quit을 쓰지 않는 이유] 이 진입점은 비동기 플레이 모드 전환에 의존한다. -quit을 같이 주면
/// -executeMethod 대상 메서드가 반환하는 즉시 에디터가 종료돼 플레이 모드 전환 자체가
/// 무효화된다. Driver.Finish()가 검사를 마친 뒤 배치 모드에서만 EditorApplication.Exit()를
/// 호출해 종료를 책임진다.
///
/// [F1 재작업 판정 2차 반려 A 항목 — "중첩 코루틴 예외 경로"]
/// 1차 수정(A4)은 Start()를 try/finally로 감쌌으나, `yield return SomeMethodReturningIEnumerator()`
/// 형태로 IEnumerator를 직접 yield하면 Unity 코루틴 스케줄러가 그 내부 열거자를 **별도로**
/// MoveNext()하는데, 그 안에서 예외가 나면 Unity가 그 예외를 잡아 코루틴 자체를 조용히 멈출 뿐
/// 바깥 메서드(Start)의 컴파일된 상태 머신 스택으로 되돌아와 try/finally를 통과한다는 보장이
/// 없다(이 프로젝트 안에서 직접 반증하지는 못했다 — 신뢰할 수 없는 가정에 기대지 않기 위해
/// 아래 두 겹의 방어를 둔다):
/// 1) RunSubStep()이 각 하위 테스트 IEnumerator를 "이 메서드 자신의" try/catch 범위 안에서 직접
///    MoveNext()해 펼친다(별도 IEnumerator 객체를 Unity 스케줄러에 넘기지 않는다) — 예외가 나면
///    이 자리에서 바로 잡아 Fail()로 기록하고 그 하위 단계만 중단, 전체 시퀀스는 계속된다.
/// 2) 혹시도 못 잡는 경우에 대비해 Update() 워치독을 둔다 — 메인 시퀀스가 180초 안에 Finish를
///    부르지 못하면(코루�ine가 어떤 경로로든 조용히 죽은 경우 포함) 워치독이 강제로 Finish(true)
///    를 부른다. 결과 파일이 항상 남고 exitCode도 항상 결정된다.
/// </summary>
public static class Map4PlayTestRunner
{
    private const string PendingKey = "Map4PlayTest_Pending_20260928";
    private const string MasterScenePath = "Assets/LaboratoryMap4/Scenes/Map4_Master.unity";
    private const float WatchdogSeconds = 180f;

    // [PTC] 섹터 원점의 유일한 출처 — Map4SceneBuilder.LayoutPath(private)와 같은 경로. 이전엔
    // Map4Layout의 09-22 고정 원점 배열을 썼는데, S1이 132→188(S1_설계 §11-1)로
    // 바뀌면 S2~S8 원점이 +56 밀려 고정값과 어긋난다. 씬 빌더가 섹터 루트를 layout.GetOrigin(id)에
    // 두므로(Map4SceneBuilder.BuildSectorScene) 검사도 같은 에셋에서 계산한다. 에셋이 없거나 섹터가
    // 빠져 있으면 [실패]로 기록하고 그 검사를 중단한다 — 옛 고정값으로 조용히 되돌아가지 않는다.
    private const string LayoutAssetPath = "Assets/LaboratoryMap4/Data/Map4Layout.asset";

    // [F1 재작업 판정 A1] 팀 기본값과 확실히 다른 시험용 값. 팀 기본값: Sphere 7.0 / Cube 3.5 /
    // Tetrahedron 5.0(PlayerObjectMenuItem.cs:185-194).
    private static readonly (string shape, float sentinel)[] Sentinels =
    {
        ("Sphere", 12.34f), ("Cube", 22.34f), ("Tetrahedron", 32.34f)
    };

    public static float SentinelFor(string playerName)
    {
        foreach ((string shape, float sentinel) in Sentinels)
            if (playerName.Contains(shape)) return sentinel;
        return float.NaN;
    }

    [MenuItem("Tools/Laboratory Map4/Run Play Mode Tests (F1-6 2.4.5.6)")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[Map4PlayTest] 이미 플레이 모드다 — 먼저 정지해라.");
            return;
        }
        if (!Map4SceneGuard.CanReplaceCurrentScenes())
        {
            Debug.LogWarning("[Map4PlayTest] 사용자가 저장을 취소해 테스트를 중단한다.");
            return;
        }

        ApplySentinelStats();

        EditorSceneManager.OpenScene(MasterScenePath, OpenSceneMode.Single);
        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    private static void ApplySentinelStats()
    {
        foreach ((string shape, float sentinel) in Sentinels)
        {
            string path = $"Assets/LaboratoryMap4/Profiles/Lab_{shape}Stats.asset";
            PlayerShapeStats stats = AssetDatabase.LoadAssetAtPath<PlayerShapeStats>(path);
            if (stats == null)
            {
                Debug.LogError($"[Map4PlayTest] {path}를 찾지 못해 시험값을 넣지 못했다.");
                continue;
            }
            SessionState.SetFloat("Map4PlayTest_Orig_" + shape, stats.moveSpeed);
            stats.moveSpeed = sentinel;
            EditorUtility.SetDirty(stats);
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>[F1 재작업 판정 2차 반려 A 항목 — 복원 보장] 이전엔 CheckProfileApplied 중간에서만
    /// 불렀다 — 그보다 앞에서(예: Director를 못 찾아 즉시 중단) 실패하면 시험값이 영원히 남는다.
    /// 이제 Finish()가 **항상**(성공·실패·워치독 강제종료 무관) 마지막에 이 메서드를 부른다.
    /// 여러 번 불러도 안전하다(SessionState 값이 남아 있는 한 같은 원본으로 되돌린다).</summary>
    public static void RestoreSentinelStats()
    {
        foreach ((string shape, float _) in Sentinels)
        {
            string path = $"Assets/LaboratoryMap4/Profiles/Lab_{shape}Stats.asset";
            PlayerShapeStats stats = AssetDatabase.LoadAssetAtPath<PlayerShapeStats>(path);
            if (stats == null) continue;
            float orig = SessionState.GetFloat("Map4PlayTest_Orig_" + shape, stats.moveSpeed);
            stats.moveSpeed = orig;
            EditorUtility.SetDirty(stats);
        }
        AssetDatabase.SaveAssets();
    }

    [InitializeOnLoadMethod]
    private static void Bootstrap()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode) return;
        if (!SessionState.GetBool(PendingKey, false)) return;
        SessionState.SetBool(PendingKey, false);

        if (Object.FindObjectOfType<Driver>() != null) return;
        new GameObject("Map4PlayTest_RUNTIME").AddComponent<Driver>();
        Debug.Log("[Map4PlayTest] 플레이 모드 진입 — 러너 생성, 검사 시작.");
    }

    /// <summary>실제 검사 로직. 플레이 모드 동안에만 존재한다.</summary>
    private class Driver : MonoBehaviour
    {
        private readonly StringBuilder log = new StringBuilder();
        private bool anyFailure;
        private bool finished;
        private float watchdogDeadline;

        private int failCount; // [PTC-3] 콘솔 요약 한 줄(계약 R2-C4 공통)용 — 판정에는 쓰지 않는다.

        private void Fail(string line)
        {
            anyFailure = true;
            failCount++;
            log.AppendLine(line);
        }

        void Start()
        {
            watchdogDeadline = Time.time + WatchdogSeconds;
            StartCoroutine(RunAllChecksSafely());
        }

        void Update()
        {
            if (!finished && Time.time > watchdogDeadline)
            {
                Fail($"[치명] 워치독 타임아웃({WatchdogSeconds}초) — 테스트 시퀀스가 끝나지 않았다 " +
                     "(중첩 코루틴 예외로 조용히 죽었을 가능성 포함).");
                Finish(true);
            }
        }

        private IEnumerator RunAllChecksSafely()
        {
            // StartCoroutine으로 독립 실행되므로, 여기서 예외가 나 이 코루틴이 조용히 멈추더라도
            // Update()의 워치독이 감지해 Finish를 강제한다(클래스 주석 참고) — 정상 경로에서는
            // 아래 각 단계 내부의 RunSubStep이 예외를 이미 흡수하므로 여기까지 새는 일은 드물다.
            yield return RunAllChecks();
            Finish(anyFailure);
        }

        /// <summary>하위 테스트 IEnumerator를 "이 메서드 자신의" try/catch 안에서 직접 MoveNext()해
        /// 펼친다 — Unity 스케줄러에 별도 IEnumerator 객체로 넘기지 않으므로, 그 안에서 예외가 나도
        /// 정확히 이 자리에서 잡힌다(클래스 주석의 "중첩 코루틴 예외 경로" 대응).</summary>
        private IEnumerator RunSubStep(IEnumerator step, string stepName)
        {
            while (true)
            {
                bool moved;
                object current = null;
                try
                {
                    moved = step.MoveNext();
                    if (moved) current = step.Current;
                }
                catch (System.Exception e)
                {
                    Fail($"[예외] {stepName}: {e}");
                    yield break;
                }
                if (!moved) yield break;
                yield return current;
            }
        }

        private IEnumerator RunAllChecks()
        {
            log.AppendLine("F1-6 플레이 모드 검사 — " + System.DateTime.Now);

            Map4Director director = Object.FindObjectOfType<Map4Director>();
            if (director == null)
            {
                Fail("[치명] Map4Director를 씬에서 찾지 못했다 — 중단.");
                yield break;
            }

            PlayerMover[] players = Object.FindObjectsOfType<PlayerMover>();
            log.AppendLine($"플레이어 인스턴스: {players.Length}개(기대 3)");
            if (players.Length != 3) Fail($"[실패] 플레이어 인스턴스가 3개가 아니다({players.Length}).");

            CheckProfileApplied(players);

            float directorDeadline = Time.time + 15f;
            while (director.LoadedSectorCount < 8 && Time.time < directorDeadline) yield return null;

            log.AppendLine($"항목4-a 섹터 로드: {director.LoadedSectorCount}/8 " +
                            (director.LoadedSectorCount == 8 ? "✅" : "❌"));
            if (director.LoadedSectorCount != 8) Fail("[실패] 항목4-a — 섹터가 8개 모두 로드되지 않았다.");

            // [PTC — S1_설계 §9 로컬 검증 6] 섹터가 로드되자마자(0.3초 대기·스폰 검사보다 먼저) 팀 추격자를
            // 검사 동안 멈춘다. 이유는 NeutralizeChasersForTest 주석 참고.
            NeutralizeChasersForTest();

            // [PTC] 씬의 섹터 루트 위치가 레이아웃 에셋 원점과 같은지(= 씬이 현재 에셋으로 Generate됐는지).
            // 아래 항목N1이 에셋 원점으로 경사로 좌표를 계산하므로, 씬이 옛 에셋(S1 132)으로 지어진 채면
            // N1이 엉뚱한 자리를 재게 된다 — 그 경우를 원인이 드러나는 [실패]로 먼저 잡는다(추가 검사).
            CheckSectorOriginsMatchLayout(director);

            // [PTC-2 — 판정 1차 B-10 "Exit 마커 대조를 러너에 넣음"] 로드된 섹터마다 SectorController.exit의
            // 섹터 루트 기준 로컬 z = 에셋 def.length(±0.01, Map4SceneBuilder.ReconcileExitMarker와 같은 규칙).
            // [PTC-3 — 2차판정 18] x(|x| ≤ 4)·y(rise+0.1 ±0.01)도 [실패] 판정한다.
            // 읽기만 한다 — 마커를 옮기거나 씬을 저장하지 않는다(판정 B-6: 자동 이동 스위치 false 유지).
            CheckExitMarkersMatchLayout(director);

            yield return new WaitForSeconds(0.3f);

            List<Rigidbody> bodies = new List<Rigidbody>();
            foreach (PlayerMover p in players) bodies.Add(p.GetComponent<Rigidbody>());
            bool spawnOk = director.SpawnPlayersAt(1, bodies, out int overlapCount);
            log.AppendLine($"항목4-b 스폰 겹침 0(도형·지형 포함): {(spawnOk ? "✅" : "❌")} (겹침 {overlapCount}건, 섹터1 기준)");
            if (!spawnOk) Fail("[실패] 항목4-b — 스폰 겹침이 0이 아니다.");

            // [PTC-3 — 2차판정 18 · 계약 R2-C4 PTC] CP_S4_Start 막대와 S4 스폰 슬롯 사이 수평 거리 ≥ 1.0(읽기만, 막대는 S4-W 몫).
            CheckS4CheckpointPoleClearance(director);

            // 항목4-c: [2차 반려 C-c] 다음 도형을 보내기 전 "다른 두 도형"을 멀리 파킹한다 —
            // 이전 버전은 이전 도형이 방금 테스트한 섹터의 슬롯 0에 그대로 남아 있어, 다음 도형이
            // 같은 자리로 순간이동하면 그 위에 올라서는 값(S8 Cube 35.00·Tetra 34.79 등)이
            // "접지"로 잘못 기록됐다 — 실제로는 바닥이 아니라 남의 몸 위였다.
            // [PTC-3] 파킹 자리 밑에 임시 발판을 둬 킬 라인·OOB 리스폰 재출현 경로를 끊고(EnsureParkPad), 측정 직전마다
            // 다른 도형이 모두 파킹 자리에 있는지(측정 도형과 ≥ 100) 확인 줄을 남긴다. 어긋나면 [실패](측정 오염 가능).
            LogManagerChapterStates("4-c 전");
            foreach (PlayerMover p in players)
            {
                ParkOthers(players, p);

                Rigidbody rb = p.GetComponent<Rigidbody>();
                PlayerShapeController shape = p.GetComponent<PlayerShapeController>();
                for (int id = 1; id <= 8; id++)
                {
                    bool grounded = false;
                    string detail;
                    if (director.TryGetSector(id, out _))
                    {
                        director.TeleportToSectorSpawn(rb, id, 0);
                        yield return new WaitForSeconds(1.0f);
                        bool parkedOk = OthersStillParked(players, p, out string parkDetail);
                        log.AppendLine($"항목4-c 파킹 확인 '{p.name}' 섹터{id} 측정 직전: {(parkedOk ? "✅" : "❌")}{parkDetail}");
                        if (!parkedOk)
                            Fail($"[실패] 항목4-c — '{p.name}' 섹터{id} 측정 직전 다른 도형이 파킹 자리에 없다(리스폰·기믹 이동 등) — " +
                                 "이 측정값은 오염됐을 수 있다.");
                        grounded = shape != null && shape.IsGrounded();
                        detail = $"pos={rb.position:F2}";
                    }
                    else
                    {
                        detail = "섹터 미발견";
                    }
                    log.AppendLine($"항목4-c '{p.name}' 섹터{id} 1초뒤 접지: {(grounded ? "✅" : "❌")} {detail}");
                    if (!grounded) Fail($"[실패] 항목4-c — '{p.name}' 섹터{id}에서 1초 뒤 접지 안 됨.");
                }
            }
            RestoreParked(players);
            LogManagerChapterStates("4-c 뒤");

            // 항목5 — 실제 입력 경로 이동(결정6 — 미충족·하네스 준비됨).
            // [3차 반려 C3] 이전 버전은 정착 대기 없이 곧바로 0.5초 재고 "이동 거리=0.8113"을
            // 찍었는데, 그 거리는 사실상 낙하(수직) 거리였다 — 입력과 무관한 중력을 "이동"으로
            // 오인시킬 수 있는 표현이었다. 먼저 접지될 때까지(최대 2초) 기다려 수직 낙하를 끝낸
            // 뒤, 그 다음 0.5초 동안은 수평(XZ) 거리만 잰다 — 배치모드 Play는 OS 입력 큐가 없어
            // Input.GetAxis가 항상 0이므로, 이 수평 거리는 낙하가 섞이지 않은 "입력 없음 대조값"
            // (자연 표류·물리 잡음)이다.
            if (players.Length > 0)
            {
                Rigidbody rb0 = players[0].GetComponent<Rigidbody>();
                PlayerShapeController shape0 = players[0].GetComponent<PlayerShapeController>();
                float settleDeadline = Time.time + 2f;
                while (Time.time < settleDeadline && !(shape0 != null && shape0.IsGrounded()))
                    yield return new WaitForFixedUpdate();

                Vector3 before = players[0].transform.position;
                float h = Input.GetAxis("Horizontal");
                float v = Input.GetAxis("Vertical");
                yield return new WaitForSeconds(0.5f);
                Vector3 after = players[0].transform.position;
                float movedXZ = Vector2.Distance(new Vector2(before.x, before.z), new Vector2(after.x, after.z));
                log.AppendLine($"항목5 [미충족·하네스 준비됨] 입력 없음 대조값 — Input.GetAxis(H={h},V={v}) · " +
                                $"정착 후 0.5초간 수평(XZ) 이동 거리={movedXZ:F4}U (batchmode=" +
                                $"{Application.isBatchMode}) — 에디터 배치모드 Play는 OS 입력 큐가 없어 " +
                                "GetAxis가 항상 0이다. 실제 입력 검증은 창 모드 EXE + keybd_event/" +
                                "SendInput 하네스가 필요(준비됨, 사용자 허락 전까지 미실행 — 결정6).");
            }

            // [PTC-3 — C1 (가)] 6-a를 둘로 나눈다: 음성(무시 쌍 개입 0) · 양성(IgnoreCollision 아닌 격리로 밀어냄).
            yield return RunSubStep(TestAntiStuckIgnoredPairNegative(players), "항목6-a-neg");
            yield return RunSubStep(TestAntiStuckPushIsolated(players), "항목6-a-pos");
            yield return RunSubStep(TestAntiStuckRevert(players), "항목6-b");
            yield return RunSubStep(TestAntiStuckIntermittent(players), "항목6-c(N2)");
            yield return RunSubStep(TestRampSupport(players), "항목N1(S4·S6 TEMP_Ramp 없음 — 경사로 드롭 대상 0)");
            yield return RunSubStep(TestS4ExitBridgeSupport(players), "항목N1-S4(PTC-3) S4 출구 높이 다리 지지");

            VerifyChasersStillNeutralized();
        }

        // ───────────────────────── [PTC] 추격자 간섭 차단 ─────────────────────────

        private readonly List<PathChaserController> neutralizedChasers = new List<PathChaserController>();

        /// <summary>[PTC — S1_설계 §9 로컬 검증 6] 검사 동안 팀 추격자가 도형을 쫓지 못하게 한다.
        /// [왜] 4-b·4-c·RestoreParked가 도형을 S1 스폰(시작 구역 z0~10)에 놓는다 → 우리 어댑터
        /// Lab_ActivateWhenPlayersReady가 "중력 켜진 도형이 zone 안" 조건으로 PathChaserController
        /// GameObject를 켠다 → 팀 startDelay(2초) 뒤 추격 시작 → 근접 추격(detectRadius 6)이 도형을 밀거나
        /// 잡힘(PathChaserCatchZone → NotifyCaught → SectionHitCounter → 리스폰)이 도형을 순간이동시켜
        /// 접지·경사로·AntiStuck 측정값을 오염시킬 수 있다.
        /// [방법 — 전부 공개 API, 팀 코드 무수정·private 리플렉션 없음, 런타임만(씬·파일 저장 없음 —
        /// 플레이 모드 변경은 종료 시 버려진다)]
        /// 1) 모든 Lab_ActivateWhenPlayersReady.enabled = false — 우리 어댑터라 다시 켜는 경로를 끊는다.
        /// 2) 모든 PathChaserController(비활성 포함, 이름 의존 없이 팀 타입으로 탐색)의 GameObject를
        ///    SetActive(false) — 팀 상태 머신(FixedUpdate)이 멈춰 추격이 시작되지 않는다.
        /// 3) 그 컨트롤러의 공개 필드 agent의 GameObject도 SetActive(false). [근거] 에이전트는 컨트롤러와
        ///    별도 GameObject(PathChaserMenuItem.BuildRig: "PathChaserAgent_CH1")이고, 잡힘 트리거
        ///    PathChaserCatchZone이 거기 붙어 있다. NotifyCaught는 컨트롤러 GameObject 활성 여부가 아니라
        ///    컴포넌트 enabled와 내부 상태만 보므로, 검사 시작 전에 어댑터가 이미 켜고 2초가 지나 추격
        ///    상태였다면 컨트롤러 GameObject만 꺼서는 잡힘이 계속 발생할 수 있다. 에이전트 GameObject를
        ///    끄는 것은 팀 FinishChapter가 추격 종료 때 하는 것과 같은 상태다(PathChaserController.cs).
        /// 추격자가 없으면(S1이 빈 틀로 지어진 경우 등) 기록만 하고 넘어간다 — 이 검사 대상이 아니다.</summary>
        private void NeutralizeChasersForTest()
        {
            Lab_ActivateWhenPlayersReady[] activators = Object.FindObjectsOfType<Lab_ActivateWhenPlayersReady>(true);
            foreach (Lab_ActivateWhenPlayersReady a in activators)
                if (a != null) a.enabled = false;

            PathChaserController[] chasers = Object.FindObjectsOfType<PathChaserController>(true);
            StringBuilder detail = new StringBuilder();
            foreach (PathChaserController c in chasers)
            {
                if (c == null) continue;
                bool wasActive = c.gameObject.activeSelf;
                bool agentWasActive = c.agent != null && c.agent.gameObject.activeSelf;
                bool agentWasEnabled = c.agent != null && c.agent.enabled;
                c.gameObject.SetActive(false);
                if (c.agent != null) c.agent.gameObject.SetActive(false);
                neutralizedChasers.Add(c);
                detail.Append($" [{c.gameObject.scene.name}/{c.name}: 컨트롤러 활성 {wasActive}→False, " +
                              $"에이전트 GO 활성 {agentWasActive}→False(에이전트 컴포넌트 enabled={agentWasEnabled})]");
            }
            log.AppendLine($"항목PTC 추격자 간섭 차단(검사 전용, 런타임만): Lab_ActivateWhenPlayersReady " +
                            $"{activators.Length}개 비활성, PathChaserController {chasers.Length}개 정지" +
                            (chasers.Length == 0 ? " — 씬에 추격자 없음(S1 빈 틀 등), 차단할 것 없음." : ":" + detail));
        }

        /// <summary>[PTC] 검사 끝에서 추격자가 여전히 꺼져 있는지 확인 — 다른 경로로 다시 켜졌다면
        /// 위 측정값이 추격자에 오염됐을 수 있으므로 [실패]로 남긴다.</summary>
        private void VerifyChasersStillNeutralized()
        {
            foreach (PathChaserController c in neutralizedChasers)
            {
                if (c == null) continue;
                bool reactivated = c.gameObject.activeSelf || (c.agent != null && c.agent.gameObject.activeSelf);
                if (reactivated)
                    Fail($"[실패] 항목PTC — 검사 도중 추격자 '{c.name}'가 다시 켜졌다(컨트롤러 {c.gameObject.activeSelf}, " +
                         $"에이전트 {(c.agent != null && c.agent.gameObject.activeSelf)}) — 위 측정값 오염 가능.");
            }
            if (neutralizedChasers.Count > 0)
                log.AppendLine($"항목PTC 검사 종료 시점 추격자 {neutralizedChasers.Count}개 여전히 정지 확인.");
        }

        // ───────────────────────── [PTC] 레이아웃 에셋 원점 ─────────────────────────

        private Map4Layout layoutCache;
        private bool layoutLoadFailed;

        /// <summary>레이아웃 에셋을 읽는다. 없으면 [실패]를 한 번 기록하고 null — 호출자는 그 검사를 중단한다
        /// (옛 09-22 고정 원점 배열로 조용히 폴백하지 않는다).</summary>
        private Map4Layout LoadLayoutOrFail(string context)
        {
            if (layoutCache != null) return layoutCache;
            layoutCache = AssetDatabase.LoadAssetAtPath<Map4Layout>(LayoutAssetPath);
            if (layoutCache == null)
            {
                if (!layoutLoadFailed)
                    Fail($"[실패] {context} — 레이아웃 에셋 {LayoutAssetPath}를 읽지 못했다. 섹터 원점을 계산할 수 " +
                         "없어 이 검사를 중단한다(옛 고정 원점으로 폴백하지 않음).");
                layoutLoadFailed = true;
            }
            return layoutCache;
        }

        /// <summary>id가 에셋에 있을 때만 GetOrigin을 부른다 — Map4Layout.GetOrigin은 id가 없으면 경고만
        /// 찍고 (0,0,0)을 돌려주므로(조용한 폴백), 그 경로를 여기서 막고 [실패]로 바꾼다.</summary>
        private bool TryGetLayoutOrigin(Map4Layout layout, int id, string context, out Vector3 origin, out Map4Layout.SectorDef def)
        {
            origin = Vector3.zero;
            def = layout != null ? layout.GetSector(id) : null;
            if (def == null)
            {
                Fail($"[실패] {context} — 레이아웃 에셋에 섹터 id {id}가 없다. 원점을 계산할 수 없다.");
                return false;
            }
            origin = layout.GetOrigin(id);
            return true;
        }

        /// <summary>[PTC 추가 검사] 로드된 섹터 루트(SectorController, S{n}_Root)의 월드 위치 = 에셋
        /// GetOrigin(id). 빌더가 rootGo.transform.position = layout.GetOrigin(def.id)로 두므로 정상이면
        /// 정확히 같다(정수 합이라 부동소수 오차 없음). 허용 0.01U [제안 — 부동소수 여유].</summary>
        private void CheckSectorOriginsMatchLayout(Map4Director director)
        {
            const string ctx = "항목3-b(PTC) 씬 원점=레이아웃";
            Map4Layout layout = LoadLayoutOrFail(ctx);
            if (layout == null) return;

            const float originTol = 0.01f;
            StringBuilder row = new StringBuilder();
            bool allOk = true;
            for (int id = 1; id <= 8; id++)
            {
                if (!TryGetLayoutOrigin(layout, id, ctx, out Vector3 expected, out _)) { allOk = false; continue; }
                if (!director.TryGetSector(id, out SectorController sc) || sc == null)
                {
                    row.Append($" S{id}=미로드");
                    allOk = false;
                    Fail($"[실패] {ctx} — 섹터{id}가 로드되지 않아 원점을 대조할 수 없다.");
                    continue;
                }
                Vector3 actual = sc.transform.position;
                bool ok = Vector3.Distance(actual, expected) <= originTol;
                row.Append($" S{id}={(ok ? "✅" : "❌")}(에셋{expected:F2}/씬{actual:F2})");
                if (!ok)
                {
                    allOk = false;
                    Fail($"[실패] {ctx} — 섹터{id} 루트 {actual:F3}가 에셋 원점 {expected:F3}과 다르다(허용 {originTol}U) — " +
                         "씬이 현재 Map4Layout.asset으로 다시 Generate되지 않았을 수 있다.");
                }
            }
            log.AppendLine($"{ctx}: {(allOk ? "✅" : "❌")}{row}");
        }

        /// <summary>[PTC-2 추가 검사 — 판정 1차 B-10] 로드된 8섹터의 SectorController.exit(= Manual/Markers/Exit,
        /// Map4SceneBuilder.BuildOrFindMarkers)가 섹터 끝(로컬 z = def.length)을 가리키는지 대조한다.
        /// [판정 근거 코드] Map4SceneBuilder.ReconcileExitMarker — "로컬 z가 현재 def.length와 ±0.01 안이면 일치".
        /// [PTC-3 — 2차판정 18 · 계약 R2-C4 PTC] x·y도 [실패] 판정으로 올린다: 로컬 |x| ≤ 4(출구 개구 x −4~4),
        /// y = rise + 0.1(±0.01, rise = exitHeight − floorHeight), z = def.length(±0.01, exitTol 불변).
        /// 기대값은 전부 에셋(Map4Layout.asset)에서 계산한다 — S8 길이를 하드코딩하지 않는다(에셋이 144면 144, 아직 132면 132).
        /// S8은 Map4MarkerTools.MoveS8ExitMarker(R3-INT-3) 실행 뒤에 이 판정이 뜻을 가진다(대기열 R3-INT-4 순서 4 → 5).
        /// exit가 null이면 [실패](SectorController가 출구를 모른다).
        /// 읽기만 한다: 마커 이동·씬 저장 없음(판정 B-6 — AutoRelocateStaleDefaultExit false 유지, Manual 불가침).
        /// 에셋·원점은 기존 LoadLayoutOrFail·TryGetLayoutOrigin을 재사용한다(옛 고정값 폴백 없음).</summary>
        private void CheckExitMarkersMatchLayout(Map4Director director)
        {
            const string ctx = "항목3-c(PTC-3) Exit 마커 x·y·z";
            Map4Layout layout = LoadLayoutOrFail(ctx);
            if (layout == null) return;

            const float exitTol = 0.01f; // [판정 근거 코드] Map4SceneBuilder.ReconcileExitMarker tol 0.01.
            const float exitHalfWidth = 4f; // [2차판정 18 · 계약 R2-C4 PTC] |x| ≤ 4 — 출구 개구 x −4~4(계약 K0-2).
            StringBuilder row = new StringBuilder();
            bool allOk = true;
            for (int id = 1; id <= 8; id++)
            {
                if (!TryGetLayoutOrigin(layout, id, ctx, out _, out Map4Layout.SectorDef def)) { allOk = false; continue; }
                if (!director.TryGetSector(id, out SectorController sc) || sc == null)
                {
                    row.Append($" S{id}=미로드");
                    allOk = false;
                    Fail($"[실패] {ctx} — 섹터{id}가 로드되지 않아 Exit 마커를 대조할 수 없다.");
                    continue;
                }
                if (sc.exit == null)
                {
                    row.Append($" S{id}=❌(exit null)");
                    allOk = false;
                    Fail($"[실패] {ctx} — 섹터{id} SectorController.exit가 비어 있다(Manual/Markers/Exit 미연결).");
                    continue;
                }

                Vector3 local = sc.transform.InverseTransformPoint(sc.exit.position); // 섹터 루트(S{n}_Root) 기준.
                float rise = def.exitHeight - def.floorHeight;
                Vector3 expected = new Vector3(0f, rise + 0.1f, def.length); // Map4SceneBuilder.BuildOrFindMarkers 기본 위치.
                float dz = Mathf.Abs(local.z - def.length);
                float dy = Mathf.Abs(local.y - expected.y);
                bool zOk = dz <= exitTol;
                bool xOk = Mathf.Abs(local.x) <= exitHalfWidth;
                bool yOk = dy <= exitTol;
                bool ok = xOk && yOk && zOk;
                row.Append($" S{id}={(ok ? "✅" : "❌")}(기대 x|≤{exitHalfWidth}|·y {expected.y:F3}·z {expected.z:F3} / " +
                           $"실측 ({local.x:F3}, {local.y:F3}, {local.z:F3}))");
                if (!zOk)
                {
                    allOk = false;
                    // [PTC-3] 옛 09-22 기본 길이에 남은 마커면 원인을 함께 적는다(판정 기준은 그대로 def.length).
                    float refLen = id - 1 < Map4Layout.ReferenceLengths_20260922.Length ? Map4Layout.ReferenceLengths_20260922[id - 1] : float.NaN;
                    string staleHint = !float.IsNaN(refLen) && !Mathf.Approximately(refLen, def.length) && Mathf.Abs(local.z - refLen) <= exitTol
                        ? $" — 옛 기본 길이 z {refLen}에 남아 있다" + (id == 8 ? "(Map4MarkerTools.MoveS8ExitMarker, R3-INT-3 미실행 가능)" : "")
                        : "";
                    Fail($"[실패] {ctx} — 섹터{id} Exit 마커 로컬 {local:F3}의 z가 섹터 길이 {def.length}와 {dz:F3}U 다르다" +
                         $"(허용 ±{exitTol}U){staleHint} — SectorController.exit가 실제 출구를 가리키지 않는다. 러너는 옮기지 않는다" +
                         "(판정 B-6). 수동 확인 대상.");
                }
                if (!xOk)
                {
                    allOk = false;
                    Fail($"[실패] {ctx} — 섹터{id} Exit 마커 로컬 x {local.x:F3}가 출구 개구 폭 밖이다(|x| ≤ {exitHalfWidth}, " +
                         "2차판정 18). 러너는 옮기지 않는다(판정 B-6).");
                }
                if (!yOk)
                {
                    allOk = false;
                    Fail($"[실패] {ctx} — 섹터{id} Exit 마커 로컬 y {local.y:F3}가 rise+0.1 = {expected.y:F3}와 {dy:F3}U 다르다" +
                         $"(허용 ±{exitTol}U, rise = exitHeight {def.exitHeight} − floorHeight {def.floorHeight}, 2차판정 18). " +
                         "러너는 옮기지 않는다(판정 B-6).");
                }

                Transform manualExit = sc.transform.Find("Manual/Markers/Exit");
                if (manualExit != sc.exit)
                    log.AppendLine($"{ctx} [참고] S{id} SectorController.exit('{sc.exit.name}')가 Manual/Markers/Exit" +
                                    $"({(manualExit == null ? "없음" : "다른 오브젝트")})와 같은 Transform이 아니다 — 판정은 exit 필드 기준.");
            }
            log.AppendLine($"{ctx}: {(allOk ? "✅" : "❌")}{row}");
        }

        // ───────────────────────── [PTC-3] 파킹 발판(4-c 재출현 오염 차단) ─────────────────────────
        // [2차판정 18 "ParkOthers 오염은 고친다" · PTC-2 보고 :63-65·:82] 이전 판은 치운 도형을 (5000+20i, 50, 5000)에
        // 바닥 없이 두었다 → 자유낙하 80U(약 4.0초 [계산]) 뒤 killY −30(Map4_Master.unity:554-555) 아래에서
        // outOfBoundsSeconds 3초(RespawnController.cs:59·306-344)가 지나면 킬 라인 리스폰으로 "그 순간의 현재
        // 체크포인트"에 다시 나타날 수 있었다. 이제 러너가 파킹 자리 밑에 임시 정적 발판(Map4PlayTest_TempWall 선례 —
        // CreatePrimitive, 플레이 모드 런타임 전용·저장 없음)을 만들어 도형이 그 위에 서 있게 하고, RestoreParked에서 지운다.
        // 팀 상태를 바꾸지 않는다(팀 API 호출 없음 — 판정 19 대상 아님). 파킹 y는 killY보다 위이고 OOB 볼륨 밖인지
        // 발판을 만들 때 OutOfBoundsVolume.AnyContains(팀 public static, 읽기 전용)로 확인한다.
        private static readonly Vector3 ParkBase = new Vector3(5000f, 50f, 5000f); // 이전 판과 같은 자리 [PTC-2 값 유지]
        private const float ParkSpacing = 20f;          // 이전 판과 같은 간격(도형끼리 20U)
        private const float ParkPadTopBelow = 1.0f;     // [제안] 발판 윗면 = 파킹 y − 1.0 → 도형은 최대 1U 떨어져 선다
        private const float ParkPadThickness = 1.0f;    // [제안]
        private const float ParkPadMargin = 10f;        // [제안] 파킹 점 둘레 여유(발판 가장자리에서 떨어지지 않게)
        private const float ParkMinDistance = 100f;     // [지시서 PTC-3 할 일 4 예시] 측정 도형과의 거리 ≥ 100
        private const float ParkSpotTol = 1.0f;         // [제안] 파킹 점에서 수평 1.0 안이면 "제자리"

        private GameObject parkPad;
        private readonly Dictionary<PlayerMover, Vector3> parkSpots = new Dictionary<PlayerMover, Vector3>();

        private float ParkPadTopY => ParkBase.y - ParkPadTopBelow;

        /// <summary>파킹 발판이 없으면 만든다. 처음 만들 때 파킹 자리가 killY 위·OOB 볼륨 밖인지 확인해 로그로 남긴다
        /// (아니면 [실패] — 발판이 있어도 리스폰 경로가 남는다).</summary>
        private void EnsureParkPad()
        {
            if (parkPad != null) return;
            const int maxParked = 2; // 도형 3개 중 측정 1개를 뺀 나머지
            float x0 = ParkBase.x - ParkPadMargin;
            float x1 = ParkBase.x + (maxParked - 1) * ParkSpacing + ParkPadMargin;
            parkPad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            parkPad.name = "Map4PlayTest_ParkPad";
            parkPad.transform.position = new Vector3((x0 + x1) * 0.5f, ParkPadTopY - ParkPadThickness * 0.5f, ParkBase.z);
            parkPad.transform.localScale = new Vector3(x1 - x0, ParkPadThickness, ParkPadMargin * 2f);
            Physics.SyncTransforms();

            RespawnController respawn = Object.FindObjectOfType<RespawnController>();
            float killY = respawn != null ? respawn.killY : float.NaN; // 팀 public 필드 — 읽기만.
            StringBuilder detail = new StringBuilder();
            bool ok = true;
            for (int i = 0; i < maxParked; i++)
            {
                Vector3 spot = ParkBase + Vector3.right * (i * ParkSpacing);
                Vector3 rest = new Vector3(spot.x, ParkPadTopY, spot.z);
                bool aboveKill = float.IsNaN(killY) || rest.y - 1f > killY;
                bool inOob = OutOfBoundsVolume.AnyContains(spot) || OutOfBoundsVolume.AnyContains(rest);
                if (!aboveKill || inOob) ok = false;
                detail.Append($" 자리{i}={spot:F1}(killY 위 {aboveKill}, OOB 안 {inOob})");
            }
            log.AppendLine($"항목4-c(PTC-3) 파킹 발판 생성: {(ok ? "✅" : "❌")} 윗면 y {ParkPadTopY:F2}, " +
                            $"killY {(float.IsNaN(killY) ? "(RespawnController 없음 — 확인 불가)" : killY.ToString("F2"))}{detail}");
            if (!ok)
                Fail("[실패] 항목4-c(PTC-3) — 파킹 자리가 killY 아래이거나 장외(OOB) 볼륨 안이다 — 치운 도형이 리스폰으로 " +
                     "현재 체크포인트에 다시 나타날 수 있다.");
        }

        private void ParkOthers(PlayerMover[] players, PlayerMover keep)
        {
            EnsureParkPad();
            parkSpots.Clear();
            int i = 0;
            foreach (PlayerMover p in players)
            {
                if (p == keep) continue;
                Rigidbody rb = p.GetComponent<Rigidbody>();
                if (rb == null) continue;
                Vector3 park = ParkBase + Vector3.right * (i * ParkSpacing);
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = park;
                rb.transform.position = park;
                parkSpots[p] = park;
                i++;
            }
        }

        /// <summary>[PTC-3 — 할 일 4] 측정 직전 확인: keep을 뺀 도형이 모두 파킹 자리(발판 위, 수평 ±1.0)에 있고
        /// 측정 도형과 ≥ 100 떨어져 있는가. 한 줄 설명을 돌려준다.</summary>
        private bool OthersStillParked(PlayerMover[] players, PlayerMover keep, out string detail)
        {
            StringBuilder sb = new StringBuilder();
            bool ok = true;
            Vector3 keepPos = keep.transform.position;
            foreach (PlayerMover q in players)
            {
                if (q == keep) continue;
                Vector3 pos = q.transform.position;
                float dist = Vector3.Distance(pos, keepPos);
                bool hasSpot = parkSpots.TryGetValue(q, out Vector3 spot);
                float spotErr = hasSpot ? Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(spot.x, spot.z)) : float.PositiveInfinity;
                bool onPad = pos.y > ParkPadTopY - 1f && pos.y < ParkBase.y + 1f; // 발판 위(떨어지지 않음). 세모 발바닥 −0.21 포함.
                bool qOk = hasSpot && dist >= ParkMinDistance && spotErr <= ParkSpotTol && onPad;
                if (!qOk) ok = false;
                sb.Append($" '{q.name}' {(qOk ? "✅" : "❌")}(pos {pos:F2}, 측정 도형과 {dist:F1}U ≥ {ParkMinDistance}, " +
                          $"파킹 점 수평 오차 {spotErr:F2} ≤ {ParkSpotTol}, 발판 위 {onPad})");
            }
            detail = sb.ToString();
            return ok;
        }

        /// <summary>테스트가 끝난 뒤 파킹된 도형들을 섹터1 스폰 슬롯으로 되돌린다(뒷정리 —
        /// 필수는 아니지만 씬을 그대로 두고 검사하는 사람이 있을 수 있어 정상 상태로 되돌린다).
        /// [PTC-3] 도형을 옮긴 뒤 파킹 발판을 지운다(런타임 생성물 — 저장 없음).</summary>
        private void RestoreParked(PlayerMover[] players)
        {
            Map4Director director = Object.FindObjectOfType<Map4Director>();
            if (director != null)
            {
                List<Rigidbody> bodies = new List<Rigidbody>();
                foreach (PlayerMover p in players) bodies.Add(p.GetComponent<Rigidbody>());
                director.SpawnPlayersAt(1, bodies, out _);
            }
            parkSpots.Clear();
            if (parkPad != null)
            {
                Object.Destroy(parkPad);
                parkPad = null;
            }
        }

        private void CheckProfileApplied(PlayerMover[] players)
        {
            foreach (PlayerMover p in players)
            {
                PlayerShapeIdentity identity = p.GetComponent<PlayerShapeIdentity>();
                Lab_ShapeProfileApplier applier = p.GetComponent<Lab_ShapeProfileApplier>();
                Rigidbody rb = p.GetComponent<Rigidbody>();
                string statsName = identity != null && identity.stats != null ? identity.stats.name : "(null)";
                float expected = Map4PlayTestRunner.SentinelFor(p.name);
                bool matchesSentinel = !float.IsNaN(expected) && Mathf.Approximately(p.moveSpeed, expected);
                log.AppendLine($"항목2 '{p.name}': applier={(applier != null)} profile=" +
                                $"{(applier != null && applier.profile != null ? applier.profile.name : "(null)")} " +
                                $"identity.stats={statsName} rb.constraints={rb.constraints} " +
                                $"rb.collisionDetectionMode={rb.collisionDetectionMode} " +
                                $"rb.maxAngularVelocity={rb.maxAngularVelocity} moveSpeed={p.moveSpeed} " +
                                $"(기대 시험값 {expected}) 적용확인={(matchesSentinel ? "✅" : "❌")} " +
                                $"jumpHeight={p.GetComponent<PlayerJump>()?.jumpHeight}");
                if (!matchesSentinel) Fail($"[실패] 항목2 — '{p.name}' moveSpeed가 시험값 {expected}이 아니다(실제 {p.moveSpeed}).");
            }
        }

        // ───────────────────────── [PTC-3] 항목6-a 개정 [C1 (가)] ─────────────────────────
        // [왜] 새 Lab_AntiStuck(스테이징 AS-1 산출 `진행/스테이징/공통/Scripts/Lab_AntiStuck.cs`:187, INT-2가 Assets에 교체)은
        // Physics.GetIgnoreCollision(mine, other)가 참인 쌍을 관통 후보에서 뺀다. 옛 6-a는 바로 그 IgnoreCollision으로 PhysX
        // 접촉을 끄고 AntiStuck만의 밀어냄을 봤으므로 새 코드에서는 '밀어냄 ❌'가 정상이 된다(계약 K-AS '알려진 충돌',
        // AS-1 보고 :53-54). 그래서 둘로 나눈다 [C1 (가) · 계약 R2-C4 PTC].
        //  - 6-a-neg(음성): IgnoreCollision으로 박힌 쌍 → AntiStuck을 켜고 3초 동안 InterventionCount 증가 0이면 ✅.
        //    그동안 러너가 직접 잰 관통 깊이가 > 0.2로 유지돼야 한다(박힘이 풀려서 "0"이 된 것이 아님을 함께 확인).
        //  - 6-a-pos(양성): IgnoreCollision을 쓰지 않는 격리 = Rigidbody.detectCollisions = false(지시서 후보 (a)).
        //    이 플래그는 그 바디 콜라이더들의 물리 접촉 생성을 끈다(엔진 공개 API). 쌍 단위 무시 목록(GetIgnoreCollision)과
        //    별개라 새 AntiStuck의 제외 조건(isKinematic·ExternallyDriven·ConfigurableJoint·트리거·Rigidbody 상대·무시 쌍,
        //    Lab_AntiStuck :102-104·:181-187)에 걸리지 않는다 — 실행 중에 그 조건들을 하나씩 읽어 로그로 남긴다.
        //    적용 가능 여부는 (추정)이므로 대조군(AntiStuck 끔) 관통 깊이 > 0.2 유지를 먼저 보인다.
        //    (b) 벽 콜라이더 토글(6-c 방식)은 콜라이더가 켜진 물리 스텝에 PhysX가 분리를 시도할 수 있어 대조군 깊이 유지가
        //    보장되지 않고, 6-c(간헐 관통)와 같은 조건이 돼 "밀어냄 단독"을 가르지 못해 고르지 않았다.
        // [공통 격리 자리] 두 검사 모두 지형·파킹 발판에서 멀리 떨어진 빈 공중(AntiStuckTestSpot)에서 한다 — 둘레의 정적
        // 콜라이더가 임시 벽 하나뿐이어야 "무시 쌍이라 0"과 "AntiStuck이 밀었다"를 가를 수 있다(시작 전 둘레 6U를 비어
        // 있는지 확인). 공중이라 중력(팀 PlayerGravityOverride는 useGravity를 끄고 AddForce로 중력을 얹는다 —
        // PlayerGravityOverride.cs:54·102)으로 벽에서 빠져나가지 않게 rb.constraints = FreezeAll로 붙잡는다. AntiStuck의
        // 밀어냄은 rb.position 대입이라(Lab_AntiStuck :147-148) 제약과 무관하게 동작한다. isKinematic은 AntiStuck이
        // held로 보고 개입하지 않으므로(:103·:121) 쓰지 않는다. 두 검사의 차이는 "무시 쌍이냐, detectCollisions=false냐"뿐이다.
        // [원복 — 판정 19 네 조건 준용] 검사 코드 안·플레이 모드 실행 중에만 바꾸고 finally에서 constraints·detectCollisions·
        // IgnoreCollision·AntiStuck.enabled·위치·회전을 되돌린다. 저장 없음. 팀 코드·레이어 행렬·ProjectSettings 변경 없음.
        private static readonly Vector3 AntiStuckTestSpot = new Vector3(6000f, 80f, 6000f); // [제안] 섹터(z 0~약 1160)·파킹 발판(x·z 5000대)과 먼 빈 공중
        private const float AntiStuckEmbedMin = 0.2f;   // [지시서 PTC-3 할 일 5] 대조군 관통 깊이 > 0.2 (옛 6-a 표기와 같은 기준)
        private const float AntiStuckSpotClearRadius = 6f; // [제안] 격리 자리 둘레 이 반경 안에 다른 콜라이더 0

        private struct BodySnapshot
        {
            public Vector3 position;
            public Quaternion rotation;
            public RigidbodyConstraints constraints;
            public bool detectCollisions;
            public bool stuckEnabled;
        }

        private static BodySnapshot Snapshot(Rigidbody rb, Lab_AntiStuck stuck)
        {
            BodySnapshot s;
            s.position = rb.position;
            s.rotation = rb.rotation;
            s.constraints = rb.constraints;
            s.detectCollisions = rb.detectCollisions;
            s.stuckEnabled = stuck.enabled;
            return s;
        }

        private static void RestoreBody(Rigidbody rb, Lab_AntiStuck stuck, BodySnapshot s)
        {
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = s.position;
                rb.rotation = s.rotation;
                rb.transform.SetPositionAndRotation(s.position, s.rotation);
                rb.constraints = s.constraints;
                rb.detectCollisions = s.detectCollisions;
            }
            if (stuck != null) stuck.enabled = s.stuckEnabled;
            Physics.SyncTransforms();
        }

        /// <summary>Lab_AntiStuck.RefreshSolidColliders와 같은 규칙 — 자식 포함 비트리거 콜라이더 전부.</summary>
        private static List<Collider> SolidColliders(Rigidbody rb)
        {
            List<Collider> list = new List<Collider>();
            if (rb == null) return list;
            foreach (Collider c in rb.GetComponentsInChildren<Collider>())
                if (!c.isTrigger) list.Add(c);
            return list;
        }

        private static float MaxPenetration(List<Collider> solids, Collider other)
        {
            float max = 0f;
            if (other == null) return max;
            foreach (Collider s in solids)
            {
                if (s == null) continue;
                if (Physics.ComputePenetration(s, s.transform.position, s.transform.rotation,
                        other, other.transform.position, other.transform.rotation, out _, out float d) && d > max)
                    max = d;
            }
            return max;
        }

        /// <summary>옛 6-a와 같은 임시 벽(4×4×1 큐브, Rigidbody 없음 = 정적).</summary>
        private static GameObject MakeTestWall(string name, Vector3 center)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = center;
            wall.transform.localScale = new Vector3(4f, 4f, 1f);
            return wall;
        }

        private static void TeleportBody(Rigidbody rb, Vector3 pos)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = pos;
            rb.transform.position = pos;
        }

        /// <summary>격리 자리가 killY 위·OOB 볼륨 밖이고, 둘레 AntiStuckSpotClearRadius 안에 (self 말고) 콜라이더가 없는가.</summary>
        private bool AntiStuckSpotUsable(string ctx, Rigidbody self)
        {
            RespawnController respawn = Object.FindObjectOfType<RespawnController>();
            bool aboveKill = respawn == null || AntiStuckTestSpot.y - 3f > respawn.killY; // killY는 팀 public 필드 — 읽기만.
            bool inOob = OutOfBoundsVolume.AnyContains(AntiStuckTestSpot);
            List<string> others = new List<string>();
            foreach (Collider c in Physics.OverlapSphere(AntiStuckTestSpot, AntiStuckSpotClearRadius, ~0, QueryTriggerInteraction.Collide))
                if (c != null && (self == null || c.attachedRigidbody != self)) others.Add(PathOf(c.transform));
            if (aboveKill && !inOob && others.Count == 0) return true;
            Fail($"[실패] {ctx} — 격리 자리 {AntiStuckTestSpot:F1}를 쓸 수 없다(killY 위 {aboveKill}, OOB 안 {inOob}, " +
                 $"둘레 {AntiStuckSpotClearRadius}U 콜라이더 {others.Count}개{(others.Count > 0 ? ": " + string.Join(", ", others) : "")}) — 검사하지 않는다.");
            return false;
        }

        /// <summary>[PTC-3 — 6-a-neg, C1 (가) 음성] IgnoreCollision으로 무시된 쌍에는 AntiStuck이 개입 0이어야 한다.</summary>
        private IEnumerator TestAntiStuckIgnoredPairNegative(PlayerMover[] players)
        {
            const string ctx = "항목6-a-neg";
            if (players.Length == 0) { log.AppendLine($"{ctx} [건너뜀] 플레이어 없음"); yield break; }

            Lab_AntiStuck stuck = players[0].GetComponent<Lab_AntiStuck>();
            Rigidbody rb = players[0].GetComponent<Rigidbody>();
            List<Collider> solids = SolidColliders(rb);
            // profile이 없으면 AntiStuck FixedUpdate가 곧바로 돌아가(Lab_AntiStuck :120) '개입 0'이 거저 나온다 — 음성이 뜻을 잃으므로 막는다.
            if (stuck == null || rb == null || solids.Count == 0 || stuck.profile == null)
            {
                Fail($"{ctx} [건너뜀] Lab_AntiStuck·profile·Rigidbody 또는 솔리드 콜라이더 없음");
                yield break;
            }
            if (!AntiStuckSpotUsable(ctx, rb)) yield break;

            BodySnapshot saved = Snapshot(rb, stuck);
            GameObject wall = null;
            Collider wallCollider = null;
            try
            {
                stuck.enabled = false;
                rb.constraints = RigidbodyConstraints.FreezeAll;
                wall = MakeTestWall("Map4PlayTest_TempWall_Neg", AntiStuckTestSpot + Vector3.forward * 2f);
                wallCollider = wall.GetComponent<Collider>();
                // 솔리드가 여럿이면 전부 무시 쌍으로 만든다 — 하나만 걸면 나머지 솔리드가 벽을 관통으로 세어 음성이 흐려진다.
                foreach (Collider s in solids) Physics.IgnoreCollision(s, wallCollider, true);
                TeleportBody(rb, wall.transform.position - Vector3.forward * 0.2f); // 옛 6-a와 같은 박힘 자리
                Physics.SyncTransforms();

                float controlDepth = 0f;
                for (int i = 0; i < 10; i++)
                {
                    yield return new WaitForFixedUpdate();
                    controlDepth = MaxPenetration(solids, wallCollider);
                }
                bool allIgnored = true;
                foreach (Collider s in solids) allIgnored &= Physics.GetIgnoreCollision(s, wallCollider);
                // 무시 쌍 말고 다른 제외 조건(held·트리거·Rigidbody 상대)이 걸려 있으면 '개입 0'이 무시 쌍 때문인지 가를 수 없다.
                PlayerMover mover = players[0];
                bool otherExclusion = mover.ExternallyDriven || rb.isKinematic || mover.GetComponent<ConfigurableJoint>() != null
                                      || wallCollider.isTrigger || wallCollider.attachedRigidbody != null;
                log.AppendLine($"{ctx} 대조군(AntiStuck 끔 · IgnoreCollision 솔리드 {solids.Count}개 전부 · FreezeAll · 격리 자리) — " +
                                $"10프레임 뒤 관통 깊이 {controlDepth:F3}U(> {AntiStuckEmbedMin} 필요), GetIgnoreCollision 전부 참 {allIgnored}, " +
                                $"다른 제외 조건(ExternallyDriven·isKinematic·ConfigurableJoint·벽 트리거·벽 Rigidbody) {otherExclusion}(False여야 함).");
                if (controlDepth <= AntiStuckEmbedMin || !allIgnored || otherExclusion)
                {
                    Fail($"[실패] {ctx} — 음성 검사 전제 불성립(박힘 깊이 {controlDepth:F3}U ≤ {AntiStuckEmbedMin}, 무시 쌍 미설정 " +
                         $"{!allIgnored}, 다른 제외 조건 {otherExclusion} 중 하나) — '개입 0'을 판정할 수 없다.");
                    yield break;
                }

                stuck.enabled = true;
                stuck.RefreshSolidColliders();
                int before = stuck.InterventionCount;
                float minDepth = float.PositiveInfinity;
                int frames = 0;
                float deadline = Time.time + 3f;
                while (Time.time < deadline)
                {
                    yield return new WaitForFixedUpdate();
                    frames++;
                    minDepth = Mathf.Min(minDepth, MaxPenetration(solids, wallCollider));
                }
                int delta = stuck.InterventionCount - before;
                bool ok = delta == 0 && minDepth > AntiStuckEmbedMin;
                log.AppendLine($"{ctx} 본실험(AntiStuck 켬 · 무시 쌍 박힘 3초) — 개입 증가 {delta}회(기대 0), 그동안 관통 깊이 최소 " +
                                $"{minDepth:F3}U(> {AntiStuckEmbedMin} 유지 필요), FixedUpdate {frames}회: {(ok ? "✅" : "❌")} " +
                                "— [참고] INT-2 교체 전 옛 Lab_AntiStuck(무시 쌍 제외 없음)으로 돌리면 이 항목은 개입 > 0으로 [실패]하는 것이 " +
                                "예상된 결과다(C1 (가) 이전 동작, AS-1 보고 :53-54).");
                if (!ok)
                    Fail($"[실패] 항목6-a-neg — IgnoreCollision으로 무시된 쌍에 AntiStuck이 개입했다(증가 {delta}회) 또는 박힘이 " +
                         $"풀렸다(최소 깊이 {minDepth:F3}U) [C1 (가)]. 옛 AntiStuck(INT-2 전)이면 예상된 실패다.");
            }
            finally
            {
                if (wallCollider != null)
                    foreach (Collider s in solids)
                        if (s != null) Physics.IgnoreCollision(s, wallCollider, false);
                if (wall != null) Object.DestroyImmediate(wall);
                RestoreBody(rb, stuck, saved);
            }
            yield return new WaitForSeconds(0.5f); // 원래 자리(섹터1 스폰)에 다시 정착 — 뒤 6-b가 그 자리를 안전 위치로 쓴다.
        }

        /// <summary>[PTC-3 — 6-a-pos, C1 (가) 양성] IgnoreCollision이 아닌 격리(rb.detectCollisions = false)에서 AntiStuck만의
        /// 밀어냄을 확인한다. 격리 방식을 고른 이유는 위 블록 주석.</summary>
        private IEnumerator TestAntiStuckPushIsolated(PlayerMover[] players)
        {
            const string ctx = "항목6-a-pos";
            if (players.Length == 0) { log.AppendLine($"{ctx} [건너뜀] 플레이어 없음"); yield break; }

            Lab_AntiStuck stuck = players[0].GetComponent<Lab_AntiStuck>();
            Rigidbody rb = players[0].GetComponent<Rigidbody>();
            List<Collider> solids = SolidColliders(rb);
            // profile이 없으면 AntiStuck FixedUpdate가 곧바로 돌아가(Lab_AntiStuck :120) '개입 0'이 거저 나온다 — 음성이 뜻을 잃으므로 막는다.
            if (stuck == null || rb == null || solids.Count == 0 || stuck.profile == null)
            {
                Fail($"{ctx} [건너뜀] Lab_AntiStuck·profile·Rigidbody 또는 솔리드 콜라이더 없음");
                yield break;
            }
            if (!AntiStuckSpotUsable(ctx, rb)) yield break;

            BodySnapshot saved = Snapshot(rb, stuck);
            GameObject wall = null;
            try
            {
                stuck.enabled = false;
                rb.constraints = RigidbodyConstraints.FreezeAll;
                rb.detectCollisions = false; // [격리 (a)] 물리 접촉 생성만 끈다 — 쌍 무시 목록은 건드리지 않는다.
                wall = MakeTestWall("Map4PlayTest_TempWall_Pos", AntiStuckTestSpot + Vector3.forward * 2f);
                Collider wallCollider = wall.GetComponent<Collider>();
                TeleportBody(rb, wall.transform.position - Vector3.forward * 0.2f); // 옛 6-a와 같은 박힘 자리
                Physics.SyncTransforms();

                // 새 AntiStuck 제외 조건에 걸리지 않는지 — 하나라도 참이면 이 양성 검사는 뜻이 없다(Lab_AntiStuck :102-104·:181-187).
                bool anyIgnored = false;
                foreach (Collider s in solids) anyIgnored |= Physics.GetIgnoreCollision(s, wallCollider);
                PlayerMover mover = players[0];
                bool externally = mover.ExternallyDriven;
                bool kinematic = rb.isKinematic;
                bool joint = mover.GetComponent<ConfigurableJoint>() != null;
                bool wallTrigger = wallCollider.isTrigger;
                bool wallHasBody = wallCollider.attachedRigidbody != null;
                bool excluded = anyIgnored || externally || kinematic || joint || wallTrigger || wallHasBody;
                log.AppendLine($"{ctx} 제외 조건 점검(모두 False여야 함): 무시 쌍 {anyIgnored} · ExternallyDriven {externally} · " +
                                $"isKinematic {kinematic} · ConfigurableJoint {joint} · 벽 트리거 {wallTrigger} · 벽 Rigidbody {wallHasBody} " +
                                $"(격리 = detectCollisions {rb.detectCollisions}, 솔리드 {solids.Count}개).");
                if (excluded)
                {
                    Fail($"[실패] {ctx} — 격리 상태가 새 AntiStuck 제외 조건에 걸린다 — 양성 검사가 뜻이 없다.");
                    yield break;
                }

                float controlDepth = 0f;
                for (int i = 0; i < 10; i++)
                {
                    yield return new WaitForFixedUpdate();
                    controlDepth = MaxPenetration(solids, wallCollider);
                }
                bool controlOk = controlDepth > AntiStuckEmbedMin;
                log.AppendLine($"{ctx} 대조군(AntiStuck 끔 · detectCollisions=false · FreezeAll · 격리 자리) — 10프레임 뒤 관통 깊이 " +
                                $"{controlDepth:F3}U({(controlOk ? "그대로 박혀 있음 — PhysX 단독으로는 안 풀림, 격리 (추정) 확인" : "예상과 다르게 풀림")}).");
                if (!controlOk)
                {
                    Fail($"[실패] {ctx} — detectCollisions=false 격리 대조군이 박힌 채로 유지되지 않았다(깊이 {controlDepth:F3}U ≤ " +
                         $"{AntiStuckEmbedMin}) — 이 격리 방식의 (추정)이 틀렸다(보고 대상). 밀어냄을 판정할 수 없다.");
                    yield break;
                }

                stuck.enabled = true;
                stuck.RefreshSolidColliders();
                int before = stuck.InterventionCount;
                float deadline = Time.time + 3f;
                bool intervened = false;
                bool pushed = false;
                while (Time.time < deadline)
                {
                    yield return new WaitForFixedUpdate();
                    if (stuck.InterventionCount > before)
                    {
                        intervened = true;
                        pushed = stuck.LastInterventionWasPush;
                        break;
                    }
                }
                float depthAfter = MaxPenetration(solids, wallCollider);
                log.AppendLine($"{ctx} 본실험(AntiStuck 켬 · PhysX 접촉은 detectCollisions=false로 계속 꺼짐) — 밀어냄: " +
                                $"{(pushed ? "✅" : "❌")} (개입 {intervened}, 개입횟수 {before}->{stuck.InterventionCount}, 직후 관통 깊이 " +
                                $"{depthAfter:F3}U) — PhysX 접촉이 꺼진 채로 밀렸다면 이 결과는 AntiStuck만의 효과다.");
                if (!pushed)
                    Fail("[실패] 항목6-a-pos — IgnoreCollision이 아닌 격리(detectCollisions=false)에서 AntiStuck이 밀어내지 못했다.");
            }
            finally
            {
                if (wall != null) Object.DestroyImmediate(wall);
                RestoreBody(rb, stuck, saved);
            }
            yield return new WaitForSeconds(0.5f); // 원래 자리(섹터1 스폰)에 다시 정착 — 뒤 6-b가 그 자리를 안전 위치로 쓴다.
        }

        private IEnumerator TestAntiStuckRevert(PlayerMover[] players)
        {
            if (players.Length == 0) { log.AppendLine("항목6-b [건너뜀] 플레이어 없음"); yield break; }

            Lab_AntiStuck stuck = players[0].GetComponent<Lab_AntiStuck>();
            if (stuck == null) { Fail("항목6-b [건너뜀] Lab_AntiStuck 컴포넌트 없음"); yield break; }
            stuck.enabled = true;

            Rigidbody rb = players[0].GetComponent<Rigidbody>();
            Vector3 safeStart = rb.position;
            yield return new WaitForSeconds(1.0f);

            GameObject wallA = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject wallB = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallA.name = "Map4PlayTest_TrapWallA";
            wallB.name = "Map4PlayTest_TrapWallB";
            Vector3 trapCenter = rb.position + Vector3.right * 5f;
            wallA.transform.position = trapCenter + Vector3.forward * 0.65f;
            wallB.transform.position = trapCenter - Vector3.forward * 0.65f;
            wallA.transform.localScale = wallB.transform.localScale = new Vector3(4f, 4f, 1f);
            yield return null;

            rb.position = trapCenter; rb.transform.position = trapCenter;
            rb.velocity = Vector3.zero;

            int before = stuck.InterventionCount;
            float deadline = Time.time + 5f;
            bool reverted = false;
            while (Time.time < deadline)
            {
                yield return new WaitForFixedUpdate();
                if (stuck.LastInterventionWasRevert) { reverted = true; break; }
            }
            float distFromSafe = Vector3.Distance(rb.position, safeStart);
            log.AppendLine($"항목6-b 좁은 틈(0.3U, 지속 관통) 복귀: {(reverted ? "✅" : "❌")} (개입횟수 {before}->{stuck.InterventionCount}, " +
                            $"복귀 위치 {rb.position:F2}, 원래 안전 위치와 거리 {distFromSafe:F2}U) — [정정, N2] 이 시나리오는 " +
                            "양쪽에서 항상 관통 중이라 간헐적 클린 프레임이 없었을 가능성이 높다 — 구 카운터 설계로도 같은 " +
                            "결과가 나왔을 수 있어(캐시 #47의 '이전 설계로는 불가능' 주장은 과장이었다는 지적, N2) 이 " +
                            "테스트 하나만으로 카운터 리셋 수정을 증명하지 않는다. 진짜 증명은 아래 항목6-c다.");
            if (!reverted) Fail("[실패] 항목6-b — 좁은 틈에서 안전 위치로 복귀하지 않았다.");

            Object.DestroyImmediate(wallA);
            Object.DestroyImmediate(wallB);
        }

        /// <summary>[F1 재작업 판정 결정12/N2] 관통이 "간헐적으로" 끊기는 시나리오 — 벽 콜라이더를
        /// 매 프레임 켬/끔으로 토글해(OverlapSphere가 꺼진 콜라이더를 아예 못 찾으므로 그 프레임은
        /// 확실한 "클린"이 된다) cleanFrameStreak가 5(기본값) 미만에서 계속 끊기게 만든다. 구
        /// 설계(클린 1프레임에 즉시 리셋)라면 framesPenetrating이 절대 1을 넘지 못해 개입이 전혀
        /// 일어나지 않는다 — 새 설계에서만 통과해야 진짜 증명이다.</summary>
        private IEnumerator TestAntiStuckIntermittent(PlayerMover[] players)
        {
            if (players.Length == 0) { log.AppendLine("항목6-c(N2) [건너뜀] 플레이어 없음"); yield break; }

            Lab_AntiStuck stuck = players[0].GetComponent<Lab_AntiStuck>();
            Rigidbody rb = players[0].GetComponent<Rigidbody>();
            if (stuck == null) { Fail("항목6-c(N2) [건너뜀] Lab_AntiStuck 컴포넌트 없음"); yield break; }
            stuck.enabled = true;

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Map4PlayTest_IntermittentWall";
            Vector3 spot = rb.position + Vector3.right * 10f;
            wall.transform.position = spot + Vector3.forward * 2f;
            wall.transform.localScale = new Vector3(4f, 4f, 1f);
            Collider wallCollider = wall.GetComponent<Collider>();
            yield return null;

            rb.position = wall.transform.position - Vector3.forward * (0.5f - 0.3f); // 0.3U 관통.
            rb.transform.position = rb.position;
            rb.velocity = Vector3.zero;

            int before = stuck.InterventionCount;
            float deadline = Time.time + 8f;
            bool intervened = false;
            int frame = 0;
            while (Time.time < deadline)
            {
                // 홀수 프레임엔 콜라이더를 꺼서 이번 프레임을 강제로 "클린"으로 만든다 —
                // cleanFrameStreak가 절대 2를 넘지 못하므로(매번 다음 프레임에 다시 켜서 관통
                // 재확인), profile.cleanFramesToResetStuck(기본 5) 미만에서 계속 끊긴다.
                wallCollider.enabled = (frame % 2 == 0);
                yield return new WaitForFixedUpdate();
                frame++;
                if (stuck.InterventionCount > before) { intervened = true; break; }
            }
            log.AppendLine($"항목6-c(N2) 간헐 관통(2프레임 주기로 벽 콜라이더 온/오프) 끝에 개입: " +
                            $"{(intervened ? "✅" : "❌")} (개입횟수 {before}->{stuck.InterventionCount}, " +
                            $"경과 프레임 {frame}) — 매 짝수 프레임마다 확실한 클린 프레임을 강제로 끼워 " +
                            "넣었는데도(cleanFrameStreak이 5 미만에서 계속 끊김) 결국 개입했다면, 이는 " +
                            "구 설계(클린 1회에 즉시 리셋)로는 통과 불가능했을 조건을 새 설계(N2 누적 " +
                            "유지)가 통과한다는 뜻이다 — #47의 과장 주장을 대신할 진짜 증거.");
            if (!intervened) Fail("[실패] 항목6-c(N2) — 간헐 관통 시나리오에서 AntiStuck이 끝내 개입하지 않았다.");

            wallCollider.enabled = true;
            Object.DestroyImmediate(wall);
        }

        /// <summary>[2차 반려 N1 — "가능하면 경사로 보행 실측"] 입력 없이도 도형을 TEMP_Ramp 표면
        /// 바로 위에 놓고 낙하시켜, 실제로 경사로 표면 높이에 정확히 얹히는지 확인한다(이름은
        /// "경사로 드롭" — 걸어서 오르는 것은 하네스(결정6) 몫이라 "보행"이라 부르지 않는다, 3차
        /// 반려 A). [PTC-3] 경사로 드롭 대상은 이제 0개다(S4·S6 모두 전용 빌더 — 계약 R2-C4 PTC). [PTC-2 기록] S6만이었다 — S4는 전용 빌더라 "TEMP_Ramp 없음"을
        /// 확인한다(CheckNoTempRamp, 판정 1차 B-10). 아래 "S4·S6" 서술·실측 수치는 R1 이전 기록으로 남긴다.
        /// S4·S6 각각의 경사로 기하는 Map4SceneBuilder.BuildEmptyShell과 같은 공식을 이
        /// 테스트가 독립적으로 재계산한다(의도된 중복 — 빌더 코드를 호출하지 않고 결과 씬만으로
        /// 검증).
        /// [3차 반려 C1] 이전 버전은 세 도형을 순서대로 같은 지점에 떨어뜨려 다음 도형이 앞
        /// 도형 위에 얹히는 오탐이 있었고(S4 Cube y=10.22·S6 27.23, 기대 표면보다 높음),
        /// 합격 조건도 "접지 또는 표면−1.5U 이내"로 너무 느슨해 그 오탐을 그대로 통과시켰다.
        /// 이제 4-c와 같은 ParkOthers로 매번 나머지 두 도형을 치우고, 도형별 "발바닥 오프셋"
        /// (콜라이더 하단 기하가 도형마다 달라 Root 정지 높이가 다르다 — 예: 구=+0.5·정육면체=0·
        /// 정사면체는 그보다 낮음)을 하드코딩으로 추정하지 않고 같은 섹터의 평평한 바닥에서 이
        /// 테스트 자신이 직접 측정한다("모르는 수치는 지어내지 않는다" 원칙) — 그 실측 오프셋을
        /// 경사로 중간 표면 기대 높이에 더한 값과 ±0.05U 이내로 맞는지 비교한다.</summary>
        private IEnumerator TestRampSupport(PlayerMover[] players)
        {
            if (players.Length == 0) { log.AppendLine("항목N1 [건너뜀] 플레이어 없음"); yield break; }

            // (섹터id, floorHeight, exitHeight, length) — [실좌표 09-22/F1 재작업] Map4SceneBuilder.
            // SectorSource와 동일 값.
            // [PTC] 이 표는 이제 "09-22 기대값 대조용"이다. 실제 계산은 씬을 지은 것과 같은 레이아웃
            // 에셋(Map4Layout.asset)의 SectorDef·GetOrigin으로 한다 — 원점을 에셋에서 가져오면서 길이·높이만
            // 고정표로 두면 두 출처가 섞인다. 에셋 값이 이 표와 다르면 [참고]로 로그에 남긴다(판정 불변).
            // [PTC-2 — 판정 1차 B-10 "N1은 S4·S6 구체화 때 갱신" · R2 계약 C2-4 "S4 빌더는 TEMP_Ramp를
            // 만들지 않는다" · 1차 계약 C3 "floorHeight≠exitHeight 빌더는 출구 높이 길을 스스로 책임"]
            // 섹터별로 분기한다:
            //  - S4: 전용 빌더(나선 계단·버블)가 지형을 책임진다 → Generated에 TEMP_Ramp가 **없어야** 통과.
            //        있으면 [실패](전용 빌더가 false를 돌려 빈 틀로 되돌아갔거나 잔존물). 섹터 미로드도 [실패].
            //        S4 출구 높이 길(계단→버블→다리)의 실제 입력 주행은 범위 밖(결정6). [PTC-3] 다리 윗면 지지·z124 턱은
            //        TestS4ExitBridgeSupport(항목N1-S4)가 잰다.
            //  - S6: [PTC-2 기록] 빈 틀 → 경사로 드롭. [PTC-3] 통합 뒤 전용 빌더 → S4와 같은 "TEMP_Ramp 없음" 분기(출구 층 y16은 전용 빌더 몫).
            // 이전 표의 S4 행 (4, 0, 18, 124)는 경사로 드롭 대상에서 빠지고 "TEMP_Ramp 없음" 대상으로 옮겨졌다.
            // [PTC-3 — 계약 R2-C4 PTC · 확인 요청 R3-7] 통합 뒤 S6도 전용 빌더(포탈 섹터)가 출구 층 y16을 책임진다(K0-2).
            // 그래서 S6을 전용 빌더 목록에 넣고 S6 경사로 행 (6, 18, 34, 98)을 뺀다 → 경사로 드롭 대상 0개.
            // 드롭 대상이 0개인 것은 [실패]가 아니다(전부 전용 빌더). 아래 경사로 드롭 코드는 빈 틀 섹터가 다시 생길 때를
            // 위해 그대로 둔다(값·tol 불변). S4 출구 높이 길은 TestS4ExitBridgeSupport가 따로 잰다.
            int[] dedicatedNoRampSectors = { 4, 6 };
            (int id, float floorHeight, float exitHeight, float length)[] ramps =
            {
            };

            const float tol = 0.05f; // [3차 반려 C1] ±1.5U(사실상 "안 뚫렸으면 통과")에서 좁힘 —
                                      // ParkOthers로 옆에 다른 도형이 없으니 지금은 "표면에 정확히
                                      // 얹혔는가"를 실제로 가릴 수 있다.

            Map4Layout layout = LoadLayoutOrFail("항목N1");
            if (layout == null) yield break; // [실패]는 LoadLayoutOrFail이 이미 기록했다.
            Map4Director rampDirector = Object.FindObjectOfType<Map4Director>();

            foreach (int noRampId in dedicatedNoRampSectors)
                CheckNoTempRamp(rampDirector, layout, noRampId);
            LogKnownTempMarkers(rampDirector);
            if (ramps.Length == 0)
                log.AppendLine($"항목N1 경사로 드롭 대상 없음(전부 전용 빌더 — S{string.Join("·S", dedicatedNoRampSectors)}, TEMP_Ramp 없음 검사로 대신함).");

            foreach (var legacy in ramps)
            {
                if (!TryGetLayoutOrigin(layout, legacy.id, "항목N1", out Vector3 origin, out Map4Layout.SectorDef def))
                    continue;
                var s = (id: def.id, floorHeight: def.floorHeight, exitHeight: def.exitHeight, length: def.length);
                if (!Mathf.Approximately(s.floorHeight, legacy.floorHeight) || !Mathf.Approximately(s.exitHeight, legacy.exitHeight)
                    || !Mathf.Approximately(s.length, legacy.length))
                    log.AppendLine($"항목N1 [참고] S{s.id} 에셋 값(바닥 {s.floorHeight}·출구 {s.exitHeight}·길이 {s.length})이 " +
                                    $"09-22 표(바닥 {legacy.floorHeight}·출구 {legacy.exitHeight}·길이 {legacy.length})와 다르다 — 에셋 값으로 잰다.");

                // [PTC] 이 검사는 빈 틀 경로의 TEMP_Ramp(Map4SceneBuilder.BuildEmptyShell)를 전제로 한다. 전용
                // 빌더(SectorBuilderRegistry)가 이 섹터를 구체화해 TEMP_Ramp가 없으면 아래 공식이 실제 지형과
                // 무관해지므로, 조용히 통과·오탐시키지 않고 원인이 드러나는 [실패]로 멈춘다.
                if (rampDirector == null || !rampDirector.TryGetSector(s.id, out SectorController rampSector) || rampSector == null
                    || rampSector.transform.Find("Generated/TEMP_Ramp") == null)
                {
                    Fail($"[실패] 항목N1 — S{s.id}에서 Generated/TEMP_Ramp를 찾지 못했다(섹터 미로드이거나 전용 빌더가 " +
                         "경사로를 대체함) — 이 검사의 경사로 공식을 그 섹터 지형에 맞게 갱신해야 한다.");
                    continue;
                }

                float rise = s.exitHeight - s.floorHeight;
                float minRun = Mathf.Abs(rise) / Mathf.Tan(Map4Build.RampMaxAngleDeg * Mathf.Deg2Rad);
                float rampRun = Mathf.Ceil(minRun * 1.1f / 2f) * 2f;
                float flatEnd = s.length - rampRun;
                float originZ = origin.z; // [PTC] 에셋 GetOrigin — S1 188이면 S4 492·S6 776(옛 고정값 436·720 +56).

                float midLocalZ = flatEnd + rampRun / 2f;
                float midLocalY = rise * 0.5f; // 경사로 중간 지점 표면 높이(로컬, floorHeight 기준).
                float expectedSurfaceY = s.floorHeight + midLocalY;
                Vector3 rampTarget = new Vector3(0f, expectedSurfaceY + 1f, originZ + midLocalZ);
                // 같은 섹터의 경사로 시작 전 평지 중앙 — 발바닥 오프셋 실측용 기준점.
                Vector3 flatTarget = new Vector3(0f, s.floorHeight + 2f, originZ + flatEnd * 0.5f);

                foreach (PlayerMover p in players)
                {
                    ParkOthers(players, p);

                    Rigidbody rb = p.GetComponent<Rigidbody>();

                    // 1) 평지에서 이 도형의 발바닥 오프셋을 직접 측정한다.
                    // [실측 버그·수정] 처음엔 IsGrounded()를 순간이동 직후부터 즉시 폴링해 "그루닛
                    // 되면 바로 빠져나가는" 방식을 썼는데, PlayerGroundContact는 SphereCast+코요테
                    // 유예(≈0.1초, 캐시 #28)라 순간이동 직전까지 다른 곳에서 접지해 있었다면 새
                    // 위치로 옮긴 직후에도 그 유예 동안 계속 "접지"를 보고한다 — 실측으로 확인:
                    // Tetrahedron의 오프셋이 1.996(낙하 시작 높이 2.0에서 물리 스텝 1~2개치만
                    // 떨어진 값)로 찍혀 명백히 착지 전에 폴링이 끝난 것이었다. 아래 경사로 드롭과
                    // 같은 검증된 고정 대기(2초 — 2U 자유낙하 시간 0.64초보다 훨씬 김)로 통일한다.
                    rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                    rb.position = flatTarget; rb.transform.position = flatTarget;
                    yield return new WaitForSeconds(2.0f);
                    float footOffset = rb.position.y - s.floorHeight;

                    // 2) 경사로 중간에 떨어뜨려 실측.
                    rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                    rb.position = rampTarget; rb.transform.position = rampTarget;
                    yield return new WaitForSeconds(2.0f);

                    // [실측 개선] 경사면이라 완전히 미끄럼 없이 드롭 지점 그대로 정지한다는 보장이
                    // 없다(특히 구는 rb.constraints=None이라 자유 회전·구름 가능 — 실측: 첫 시도에서
                    // 구가 목표 Z에서 최대 0.35U가량 밀려 정지, 고정 드롭 지점 기준 기대 표면
                    // 높이로 비교하면 미끄러진 만큼이 그대로 오차로 잡혔다). 최종 정지 위치의
                    // 실제 Z에서 경사로 표면 높이를 다시 계산해 비교한다 — "정확히 그 자리에
                    // 멈췄는가"가 아니라 "멈춘 자리의 경사로 표면에 실제로 얹혔는가"를 본다.
                    float restLocalZ = Mathf.Clamp(rb.position.z - originZ, flatEnd, flatEnd + rampRun);
                    float restSurfaceY = s.floorHeight + rise * (restLocalZ - flatEnd) / rampRun;

                    // [실측 발견 — 발바닥 오프셋만으로는 부족했다] 위 수정 후에도 Cube·Tetrahedron은
                    // S4·S6 양쪽에서 각각 +0.20~0.21U·+0.26~0.27U만큼 기대보다 "일관되게 높게"
                    // 정지했다(Sphere는 0건, ✅). 원인: Cube·Tetrahedron은 rb.constraints=
                    // FreezeRotation(항목2 로그 확인)이라 경사에 맞춰 기울지 못한다 — 평평한
                    // 바닥면 전체가 아니라 그 "아랫쪽(내리막) 모서리" 한 줄로만 경사면에 걸친다.
                    // 이때 중심 높이는 평지 접촉보다 (콜라이더 경사방향 반폭)×tanθ만큼 더 뜬다.
                    // 검증: 실제 콜라이더 Z 반폭을 읽어 이 공식을 대입하면 Cube 0.5×tan(22.2°)=
                    // 0.204U(실측 0.205U)·0.5×tan(22.8°)=0.210U(실측 0.211U), Tetrahedron도
                    // 실측 콜라이더 반폭(≈0.64)×tanθ가 두 섹터 모두에서 오차 0.01U 이내로 맞아
                    // 우연이 아니다 — 지어낸 상수가 아니라 이 실행에서 직접 읽은 콜라이더 크기로
                    // 계산한다. 자유 회전(Sphere, rb.constraints=None)은 기울어 접촉점을 스스로
                    // 맞추므로 이 모서리 보정을 적용하지 않는다(대신 아래 법선 보정을 쓴다).
                    // (용어 정정 — PTC R2) 바닥면이 수평인 채로 +z로 오르는 면에 놓이면 먼저 닿는 쪽은
                    // 오르막 쪽 모서리다. 식 e·tanθ는 어느 모서리든 같은 값이라 판정은 바뀌지 않는다.
                    float angleRad = Mathf.Atan2(Mathf.Abs(rise), rampRun); // [계산] S4 ≈22.25°·S6 ≈22.83°
                    float edgeContactCorrection = 0f;
                    if (rb.constraints == RigidbodyConstraints.FreezeRotation)
                    {
                        Collider solid = FindSolidCollider(rb);
                        if (solid != null)
                            edgeContactCorrection = solid.bounds.extents.z * Mathf.Tan(angleRad);
                    }

                    // [PTC R2 — 경사면 법선 보정] 자유 회전 도형(구)은 면과의 "법선 방향" 거리가 평지와
                    // 같다(= 평지에서 잰 footOffset, 반지름+접촉 오프셋). 같은 xz에서 수직으로 재면 그
                    // 거리는 footOffset/cosθ다. 이전 식(표면+footOffset)은 footOffset·(1/cosθ−1)만큼의
                    // 계통 오차를 빼먹었다 — r=0.5 기준 [계산] S4 0.0402·S6 0.0425로 R1 실측 오차
                    // 0.040·0.043과 같았고, 한도 25°면 0.0517이라 정상 구도 [실패]가 났을 것이다.
                    // 반지름은 지어내지 않고 이 실행의 평지 실측값(footOffset)을 쓴다. 판정 상수 tol=0.05 불변.
                    float slopeNormalCorrection = 0f;
                    if (rb.constraints == RigidbodyConstraints.None)
                        slopeNormalCorrection = footOffset * (1f / Mathf.Cos(angleRad) - 1f);

                    float expectedRestY = restSurfaceY + footOffset + edgeContactCorrection + slopeNormalCorrection;
                    float error = Mathf.Abs(rb.position.y - expectedRestY);
                    bool ok = error <= tol;
                    log.AppendLine($"항목N1 '{p.name}' S{s.id} 경사로 드롭: {(ok ? "✅" : "❌")} " +
                                    $"pos={rb.position:F3} 정지지점로컬Z={restLocalZ:F2}(드롭목표{midLocalZ:F2}) " +
                                    $"기대Y={expectedRestY:F3}(정지지점표면{restSurfaceY:F3}+발바닥오프셋" +
                                    $"{footOffset:F3}+모서리접촉보정{edgeContactCorrection:F3}" +
                                    $"+법선보정{slopeNormalCorrection:F3}, 경사{angleRad * Mathf.Rad2Deg:F2}°) " +
                                    $"오차={error:F3}U(허용 ±{tol}U)");
                    if (!ok) Fail($"[실패] 항목N1 — '{p.name}'가 S{s.id} 경사로 표면에서 ±{tol}U를 " +
                                  $"벗어났다(pos={rb.position:F3}, 기대Y={expectedRestY:F3}).");
                }
            }

            RestoreParked(players);
        }

        /// <summary>[PTC-2 — 항목N1 S4 분기] 전용 빌더 섹터에 빈 틀 경사로(TEMP_Ramp)가 남아 있지 않은지 확인한다.
        /// 섹터 루트 아래 전체(Generated 직속뿐 아니라 모든 깊이)와 그 섹터 씬의 다른 루트까지 이름으로 찾는다 —
        /// 직속 경로만 보면 다른 자리에 옮겨진 잔존물을 놓친다(느슨해지지 않게 넓게 찾는다).
        /// [실패] 조건: 섹터 미로드 · Generated 없음 · TEMP_Ramp 발견. 읽기만 한다(삭제·이동 없음).</summary>
        private void CheckNoTempRamp(Map4Director director, Map4Layout layout, int id)
        {
            string ctx = $"항목N1 S{id} TEMP_Ramp 없음(전용 빌더)";
            if (!TryGetLayoutOrigin(layout, id, ctx, out _, out Map4Layout.SectorDef def)) return;
            if (director == null || !director.TryGetSector(id, out SectorController sc) || sc == null)
            {
                Fail($"[실패] {ctx} — 섹터{id}가 로드되지 않아 확인할 수 없다.");
                log.AppendLine($"{ctx}: ❌ 섹터 미로드");
                return;
            }
            Transform generated = sc.transform.Find("Generated");
            if (generated == null)
            {
                Fail($"[실패] {ctx} — 섹터{id} 루트 아래 Generated가 없다(씬이 Generate되지 않았다).");
                log.AppendLine($"{ctx}: ❌ Generated 없음");
                return;
            }

            List<string> found = new List<string>();
            CollectByName(sc.transform, "TEMP_Ramp", found);
            foreach (GameObject r in sc.gameObject.scene.GetRootGameObjects())
                if (r != null && r.transform != sc.transform) CollectByName(r.transform, "TEMP_Ramp", found);

            bool ok = found.Count == 0;
            log.AppendLine($"{ctx}: {(ok ? "✅" : "❌")} (바닥 {def.floorHeight}·출구 {def.exitHeight}·길이 {def.length}, " +
                            $"TEMP_Ramp {found.Count}개{(ok ? "" : ": " + string.Join(", ", found))}, Generated 직속 자식 {generated.childCount}개) " +
                            "— 출구 높이 길 지지는 S4는 항목N1-S4, S6은 전용 빌더 자체 검사 몫(K0-2).");
            if (!ok)
                Fail($"[실패] {ctx} — 전용 빌더인데 빈 틀 경사로 잔존: {string.Join(", ", found)} " +
                     $"(전용 빌더가 false를 돌려 빈 틀로 되돌아갔을 수 있다 — Generate 로그의 S{id}_Builder 오류 확인).");
        }

        // ───────────────────────── [PTC-3] 알려진 TEMP 표지 ─────────────────────────
        // [지시서 PTC-3 할 일 6 · 계약 K0-3 · C11 · 판정 17-S7] 팀 기능이 들어올 때까지의 빈 GameObject 표지. 러너가 이름으로
        // 찾는 곳(CollectByName ==, CollectTransformsByName ==, Transform.Find 경로)은 전부 **정확한 이름 일치**라 이 표지들이
        // TEMP_Ramp 수집 등에 섞이지 않는다. 판정 대상이 아니라 [참고]로 개수만 남긴다.
        private static readonly (int sectorId, string name)[] KnownTempMarkers =
        {
            (2, "TEMP_S2_ExitS3_Open"),     // S2 선례
            (7, "TEMP_S7_EntryDoor_Open"),  // [판정 17-S7] S7_Builder TempDoorMarkers
            (7, "TEMP_S7_ExitDoor_Open"),   // [판정 17-S7 · C11 이름 규약]
        };

        private void LogKnownTempMarkers(Map4Director director)
        {
            StringBuilder sb = new StringBuilder();
            foreach ((int sectorId, string name) in KnownTempMarkers)
            {
                if (director == null || !director.TryGetSector(sectorId, out SectorController sc) || sc == null)
                {
                    sb.Append($" S{sectorId} '{name}' (섹터 미로드)");
                    continue;
                }
                List<string> found = new List<string>();
                CollectByName(sc.transform, name, found);
                foreach (GameObject r in sc.gameObject.scene.GetRootGameObjects())
                    if (r != null && r.transform != sc.transform) CollectByName(r.transform, name, found);
                sb.Append($" S{sectorId} '{name}' {found.Count}개");
            }
            log.AppendLine($"항목N1 [참고] 알려진 TEMP 표지(정확한 이름 일치라 TEMP_Ramp 수집에 섞이지 않음):{sb}");
        }

        // ───────────────────────── [PTC-3] CP_S4_Start 막대 ↔ 스폰 슬롯 ─────────────────────────
        private const string S4CheckpointName = "CP_S4_Start"; // S4_Wiring.cs:67 [계약 이름]
        private const string CheckpointPoleName = "Pole";       // RespawnMenuItem.cs:45 [팀] · S4_Wiring.cs:78
        private const float PoleSlotMinClearance = 1.0f;        // [2차판정 18 · 계약 R2-C4 PTC] 수평 ≥ 1.0

        /// <summary>[PTC-3 — 2차판정 18] CP_S4_Start/Pole의 월드 수평 위치와 S4 스폰 슬롯 0~2(SectorController.spawnSlots)의
        /// 최소 수평 거리 ≥ 1.0. 막대(팀 생성, 콜라이더 없음 — RespawnMenuItem StripCollider)는 옮기지 않는다(S4-W 몫).
        /// CP_S4_Start가 0개 또는 2개 이상이거나 Pole·슬롯이 없으면 [실패].</summary>
        private void CheckS4CheckpointPoleClearance(Map4Director director)
        {
            const string ctx = "항목4-c(PTC-3) CP_S4_Start 막대 ↔ S4 스폰 슬롯";
            if (director == null || !director.TryGetSector(4, out SectorController sc) || sc == null)
            {
                Fail($"[실패] {ctx} — 섹터4가 로드되지 않아 확인할 수 없다.");
                log.AppendLine($"{ctx}: ❌ 섹터 미로드");
                return;
            }
            List<Transform> cps = new List<Transform>();
            CollectTransformsByName(sc.transform, S4CheckpointName, cps);
            foreach (GameObject r in sc.gameObject.scene.GetRootGameObjects())
                if (r != null && r.transform != sc.transform) CollectTransformsByName(r.transform, S4CheckpointName, cps);
            if (cps.Count != 1)
            {
                Fail($"[실패] {ctx} — '{S4CheckpointName}'가 {cps.Count}개다(기대 1).");
                log.AppendLine($"{ctx}: ❌ '{S4CheckpointName}' {cps.Count}개");
                return;
            }
            Transform pole = cps[0].Find(CheckpointPoleName);
            if (pole == null)
            {
                Fail($"[실패] {ctx} — '{PathOf(cps[0])}' 아래 '{CheckpointPoleName}'이 없다.");
                log.AppendLine($"{ctx}: ❌ 막대 없음");
                return;
            }

            float minDist = float.PositiveInfinity;
            StringBuilder sb = new StringBuilder();
            bool slotsOk = true;
            for (int i = 0; i < 3; i++)
            {
                Transform slot = sc.spawnSlots != null && i < sc.spawnSlots.Length ? sc.spawnSlots[i] : null;
                if (slot == null)
                {
                    slotsOk = false;
                    sb.Append($" 슬롯{i}=없음");
                    continue;
                }
                float d = Vector2.Distance(new Vector2(pole.position.x, pole.position.z), new Vector2(slot.position.x, slot.position.z));
                minDist = Mathf.Min(minDist, d);
                sb.Append($" 슬롯{i}(로컬 {sc.transform.InverseTransformPoint(slot.position):F2}) {d:F2}");
            }
            bool ok = slotsOk && minDist >= PoleSlotMinClearance;
            log.AppendLine($"{ctx}: {(ok ? "✅" : "❌")} 막대 섹터 로컬 {sc.transform.InverseTransformPoint(pole.position):F2}, " +
                            $"최소 수평 거리 {minDist:F2}(≥ {PoleSlotMinClearance} 필요):{sb} — 러너는 막대를 옮기지 않는다(S4-W 몫).");
            if (!slotsOk) Fail($"[실패] {ctx} — S4 스폰 슬롯이 비어 있다(Manual/Markers/Spawn_0~2).");
            else if (!ok)
                Fail($"[실패] {ctx} — 막대와 스폰 슬롯의 최소 수평 거리 {minDist:F2} < {PoleSlotMinClearance} [2차판정 18] — " +
                     "S4-W가 막대를 옮겨야 한다(러너는 옮기지 않음).");
        }

        /// <summary>[PTC-3 — 할 일 7 판단 기록] S8 스폰 슬롯은 CH8 시작 구역 안이라(S8_Wiring StartZone, ManagerChapterController.cs:9·
        /// 150-163) 4-c 측정 도형이 들어가면 관리자 챕터가 자동 시작될 수 있다. 잡힘 → 전원 CH8 복귀(:270-305)가 생기면 파킹된
        /// 도형도 옮겨진다 — 그 경우는 4-c 파킹 확인 줄이 ❌로 드러낸다. 여기서는 챕터 상태를 읽기만 한다(팀 public getter).</summary>
        private void LogManagerChapterStates(string when)
        {
            ManagerChapterController[] mcs = Object.FindObjectsOfType<ManagerChapterController>(true);
            if (mcs.Length == 0)
            {
                log.AppendLine($"항목4-c [참고] ({when}) ManagerChapterController 없음.");
                return;
            }
            StringBuilder sb = new StringBuilder();
            foreach (ManagerChapterController m in mcs)
                if (m != null) sb.Append($" [{m.gameObject.scene.name}/{m.name}: {m.Current}{(m.Paused ? "·Paused" : "")}]");
            log.AppendLine($"항목4-c [참고] ({when}) CH8 관리자 챕터 상태(읽기만):{sb}");
        }

        // ───────────────────────── [PTC-3] S4 출구 높이(y18) 다리 지지 ─────────────────────────
        // [2차판정 18 "S4 출구 높이 보행 검사 추가" · 계약 R2-C4 PTC] 배치 모드에는 입력이 없다(항목5·결정6). 그래서 "보행"은
        // ① 다리 윗면 두 점에 도형 3종을 하나씩 떨어뜨려 1초 뒤 접지 + 정착 높이(= 윗면 + 발바닥 오프셋, N1 공식의 평지 경우 —
        //   경사 0이라 모서리 접촉·법선 보정 0) ±0.05, ② z = length 끝에서 다리 윗면과 연결 통로 윗면의 턱 0(±0.02, 레이캐스트)
        // 로 대신한다. 계단 → 버블 → 다리의 실제 입력 주행은 범위 밖(결정6).
        // 기대 윗면은 하드코딩하지 않는다: 에셋 rise = exitHeight − floorHeight(S4 18)를 섹터 루트 로컬 → 월드로 바꿔 쓴다.
        private const string S4BridgeName = "GEO_S4_Bridge_Exit";                  // S4_Builder.cs:117 [계약 C2-4]
        private static readonly float[] S4BridgeProbeLocalZ = { 110f, 123f };      // [추정 — 지시서 PTC-3 수치표 제안] 버블 조준 레이저 도달 z ≤ 65.95+20 ≈ 86 밖, 계단 조준 레이저(z 78.8, range 20)와 3D 거리 ≥ 33.7 [계산]
        private static readonly Vector3 S4FlatRefLocal = new Vector3(0f, 2f, 12f); // [제안] GEO_S4_Floor_Entry(x ±4, z 0~27.5, 윗면 0 — S4_Builder.cs:108) 가운데, CP_S4_Start 구역(z 0~6) 밖
        private static readonly float[] S4SeamProbeLocalX = { -3f, 0f, 3f };       // [제안] 출구 개구 x −4~4 안
        private const float SeamProbeOffsetZ = 0.05f;                              // [제안] z = length ± 0.05에서 잰다(이 폭보다 큰 틈이면 한쪽이 안 맞는다)
        private const float SeamTol = 0.02f;                                       // [계약 K0-2] 턱 0·틈 0(±0.02)

        /// <summary>surfacePoint 위 1.0에서 아래로 3.0 RaycastAll(트리거 제외). 플레이어 콜라이더는 거른다. 가장 높은 맞은 면.</summary>
        private static bool ProbeTop(Vector3 surfacePoint, out float topY, out Collider hitCollider)
        {
            topY = float.NegativeInfinity;
            hitCollider = null;
            RaycastHit[] hits = Physics.RaycastAll(surfacePoint + Vector3.up * 1.0f, Vector3.down, 3.0f, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null) continue;
                if (h.collider.GetComponentInParent<PlayerMover>() != null) continue;
                if (h.point.y > topY)
                {
                    topY = h.point.y;
                    hitCollider = h.collider;
                }
            }
            return hitCollider != null;
        }

        private IEnumerator TestS4ExitBridgeSupport(PlayerMover[] players)
        {
            const string ctx = "항목N1-S4(PTC-3) S4 출구 높이 다리";
            const float tol = 0.05f; // N1과 같은 판정 상수(불변).
            if (players.Length == 0) { log.AppendLine($"{ctx} [건너뜀] 플레이어 없음"); yield break; }

            Map4Layout layout = LoadLayoutOrFail(ctx);
            if (layout == null) yield break; // [실패]는 LoadLayoutOrFail이 이미 기록했다.
            if (!TryGetLayoutOrigin(layout, 4, ctx, out _, out Map4Layout.SectorDef def)) yield break;
            Map4Director director = Object.FindObjectOfType<Map4Director>();
            if (director == null || !director.TryGetSector(4, out SectorController sc) || sc == null)
            {
                Fail($"[실패] {ctx} — 섹터4가 로드되지 않았다.");
                yield break;
            }
            List<Transform> bridges = new List<Transform>();
            CollectTransformsByName(sc.transform, S4BridgeName, bridges);
            if (bridges.Count != 1)
            {
                Fail($"[실패] {ctx} — '{S4BridgeName}'가 {bridges.Count}개다(기대 1) — 전용 빌더 결과가 아니다.");
                yield break;
            }
            Transform bridge = bridges[0];
            Collider deck = null;
            foreach (Collider c in bridge.GetComponentsInChildren<Collider>())
                if (!c.isTrigger) { deck = c; break; }
            if (deck == null)
            {
                Fail($"[실패] {ctx} — '{S4BridgeName}'에 비트리거 콜라이더가 없다.");
                yield break;
            }

            Transform root = sc.transform;
            float rise = def.exitHeight - def.floorHeight;                    // [에셋] S4 18
            float topWorldY = root.TransformPoint(new Vector3(0f, rise, 0f)).y; // 다리 윗면(월드)
            float flatTopWorldY = root.TransformPoint(Vector3.zero).y;         // 섹터 바닥 윗면 = 로컬 0(K0-2)

            // ① 두 점 아래 윗면 = 다리, 높이 rise(±0.02).
            foreach (float z in S4BridgeProbeLocalZ)
            {
                bool hit = ProbeTop(root.TransformPoint(new Vector3(0f, rise, z)), out float y, out Collider col);
                bool onBridge = hit && col.transform.IsChildOf(bridge);
                bool ok = hit && onBridge && Mathf.Abs(y - topWorldY) <= SeamTol;
                log.AppendLine($"{ctx} 지지면 로컬 (0, {rise}, {z}): {(ok ? "✅" : "❌")} 맞은 면 " +
                                $"{(hit ? PathOf(col.transform) + $" y {y:F3}" : "없음")}(기대 '{S4BridgeName}' y {topWorldY:F3} ±{SeamTol})");
                if (!ok) Fail($"[실패] {ctx} — 로컬 z {z}에서 다리 윗면이 기대 높이 {topWorldY:F3}(±{SeamTol})에 없다.");
            }

            // ② z = length 끝 턱: 다리 윗면(length − 0.05) ↔ 연결 통로 윗면(length + 0.05). 연결 통로는 Map4SceneBuilder.
            //    BuildConnectorAndMarkers가 같은 섹터 Generated에 윗면 rise로 짓는다.
            foreach (float x in S4SeamProbeLocalX)
            {
                bool hIn = ProbeTop(root.TransformPoint(new Vector3(x, rise, def.length - SeamProbeOffsetZ)), out float yIn, out Collider cIn);
                bool hOut = ProbeTop(root.TransformPoint(new Vector3(x, rise, def.length + SeamProbeOffsetZ)), out float yOut, out Collider cOut);
                float step = hIn && hOut ? Mathf.Abs(yIn - yOut) : float.PositiveInfinity;
                bool ok = hIn && hOut && step <= SeamTol && Mathf.Abs(yIn - topWorldY) <= SeamTol && Mathf.Abs(yOut - topWorldY) <= SeamTol;
                log.AppendLine($"{ctx} z{def.length} 끝 턱 x {x}: {(ok ? "✅" : "❌")} 안쪽 " +
                                $"{(hIn ? PathOf(cIn.transform) + $" y {yIn:F3}" : "없음")} ↔ 바깥 " +
                                $"{(hOut ? PathOf(cOut.transform) + $" y {yOut:F3}" : "없음")} 턱 {step:F3}(허용 ±{SeamTol})");
                if (!ok) Fail($"[실패] {ctx} — z {def.length} 끝(x {x})에서 다리 ↔ 연결 통로 턱 {step:F3}이 ±{SeamTol}을 넘거나 면이 없다.");
            }

            // ③ 도형 3종 드롭 — 나머지 둘은 파킹 발판 위로 치운다(4-c와 같은 ParkOthers).
            foreach (PlayerMover p in players)
            {
                ParkOthers(players, p);
                Rigidbody rb = p.GetComponent<Rigidbody>();
                PlayerShapeController shape = p.GetComponent<PlayerShapeController>();

                // 발바닥 오프셋 — N1과 같은 방식(같은 섹터 평지에 놓고 2초 대기 뒤 실측, 지어낸 상수 없음).
                TeleportBody(rb, root.TransformPoint(S4FlatRefLocal));
                yield return new WaitForSeconds(2.0f);
                float footOffset = rb.position.y - flatTopWorldY;

                foreach (float z in S4BridgeProbeLocalZ)
                {
                    TeleportBody(rb, root.TransformPoint(new Vector3(0f, rise + 1f, z)));
                    yield return new WaitForSeconds(1.0f);
                    bool grounded = shape != null && shape.IsGrounded();
                    bool parkedOk = OthersStillParked(players, p, out string parkDetail);
                    yield return new WaitForSeconds(1.0f);

                    Vector3 pos = rb.position;
                    float expectedY = topWorldY + footOffset;
                    float error = Mathf.Abs(pos.y - expectedY);
                    Bounds db = deck.bounds;
                    bool onDeck = pos.x >= db.min.x && pos.x <= db.max.x && pos.z >= db.min.z && pos.z <= db.max.z;
                    bool ok = grounded && error <= tol && parkedOk && onDeck;
                    log.AppendLine($"{ctx} '{p.name}' 로컬 z {z}: {(ok ? "✅" : "❌")} 1초뒤 접지 {(grounded ? "✅" : "❌")} · " +
                                    $"pos={pos:F3}(로컬 {root.InverseTransformPoint(pos):F3}) 기대Y={expectedY:F3}(다리 윗면 {topWorldY:F3}" +
                                    $"+발바닥오프셋 {footOffset:F3}, 경사 0 → 보정 0) 오차={error:F3}U(허용 ±{tol}U) · 다리 위 {onDeck} · " +
                                    $"파킹 {(parkedOk ? "✅" : "❌")}{parkDetail}");
                    if (!grounded) Fail($"[실패] {ctx} — '{p.name}'가 로컬 z {z} 다리 위에서 1초 뒤 접지 안 됨.");
                    if (error > tol) Fail($"[실패] {ctx} — '{p.name}' 로컬 z {z} 정착 높이 {pos.y:F3}가 기대 {expectedY:F3}에서 ±{tol}U를 벗어났다.");
                    if (!onDeck) Fail($"[실패] {ctx} — '{p.name}'가 로컬 z {z}에서 다리 윗면 밖으로 벗어났다(pos {pos:F3}).");
                    if (!parkedOk) Fail($"[실패] {ctx} — '{p.name}' 로컬 z {z} 측정 중 다른 도형이 파킹 자리에 없다(측정 오염 가능).");
                }
            }
            RestoreParked(players);
        }

        /// <summary>root 이하(root 포함) 모든 깊이에서 이름이 정확히 name인 Transform을 모은다(CollectByName의 Transform판).</summary>
        private static void CollectTransformsByName(Transform root, string name, List<Transform> into)
        {
            if (root == null) return;
            if (root.name == name) into.Add(root);
            for (int i = 0; i < root.childCount; i++) CollectTransformsByName(root.GetChild(i), name, into);
        }

        /// <summary>root 이하(root 포함) 모든 깊이에서 이름이 정확히 name인 Transform의 경로를 모은다.</summary>
        private static void CollectByName(Transform root, string name, List<string> into)
        {
            if (root == null) return;
            if (root.name == name) into.Add(PathOf(root));
            for (int i = 0; i < root.childCount; i++) CollectByName(root.GetChild(i), name, into);
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static Collider FindSolidCollider(Rigidbody rb)
        {
            if (rb == null) return null;
            foreach (Collider c in rb.GetComponentsInChildren<Collider>())
                if (!c.isTrigger) return c;
            return null;
        }

        private void Finish(bool failed)
        {
            if (finished) return; // 워치독과 정상 종료 중복 방지.
            finished = true;

            // [2차 반려 A 항목 — 시험값 복원 보장] 어디서 끝났든(성공·[실패]·워치독) 항상 원상복구.
            Map4PlayTestRunner.RestoreSentinelStats();

            string outDir = ResolveOutDir();
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, $"F1_playtests_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
            string header = failed ? "판정: 실패 항목 있음(아래 [실패] 참고)\n\n" : "판정: 전부 통과\n\n";
            File.WriteAllText(path, header + log.ToString(), new UTF8Encoding(true));
            Debug.Log("[Map4PlayTest] 결과 저장: " + path + "\n" + header + log.ToString());
            // [PTC-3 — 계약 R2-C4 공통] 콘솔 마지막 요약 한 줄: [<Class>] 결과 <요약> exit=<code> file=<txt 파일명>.
            Debug.Log($"[Map4PlayTestRunner] 결과 {(failed ? "FAIL" : "PASS")} FAIL={failCount} exit={(failed ? 1 : 0)} file={Path.GetFileName(path)}");

            if (Application.isBatchMode)
                EditorApplication.Exit(failed ? 1 : 0);
            else
                EditorApplication.isPlaying = false;
        }

        private static string ResolveOutDir()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string mapRoot = Directory.GetParent(projectRoot).FullName;
            return Path.Combine(mapRoot, "검증");
        }
    }
}
#endif
