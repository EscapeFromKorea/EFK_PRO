#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 게이트 회귀 테스트 진입점 — 메뉴 한 번으로 플레이 모드에 들어가 T0RS 순차 게이트를 재현
/// 테스트하고 자동으로 정지한다. 근거: 협업/코워크채널/FROM_코워크.md [2026-09-06 #3] 1-②
/// (외부 검토 codex [확인함] 등급 — "Unity는 비활성 MonoBehaviour에도 OnTriggerEnter를 보낸다"
/// 지적). 실제 측정·판정 로직은 런타임 러너 Assets/KitchenMapV3/Scripts/V3_GateTestRunner.cs가
/// 한다 — 이 파일은 사전 점검 + 플레이 모드 진입 + 러너 스폰만 맡는다.
///
/// [메뉴 한 번으로 끝나는 이유] 사용자가 지금 에디터를 열어 두고 있다는 전제(작업 지시)라,
/// 여기서 EditorApplication.EnterPlaymode()만 호출하면 이후(러너 생성 → 3케이스 실행 →
/// 보고서 저장 → Play 모드 자동 종료)는 전부 자동이다.
///
/// [도메인 리로드에도 살아남는 구독] EnterPlaymode()는 프로젝트의 "Enter Play Mode Options"
/// 설정에 따라 Edit→Play 전환 시 도메인을 리로드할 수 있다 — 이 메서드 실행 중에 즉석으로 건
/// 이벤트 구독은 그 리로드로 사라질 수 있다. [InitializeOnLoadMethod]는 에디터가 스크립트를
/// (재)로드할 때마다(도메인 리로드 포함, 에디터 최초 기동 포함) 다시 실행되므로 이 방식으로
/// 구독하면 리로드 여부와 무관하게 항상 살아 있다. SessionState(에디터 프로세스 생존 동안 유지 —
/// 도메인 리로드에도 유지됨)로 "이번 Play 진입이 테스트 요청인지"를 표시해, 사용자가 그냥 ▶을
/// 누르는 일반 플레이와 구분한다.
/// </summary>
public static class V3_GateTest
{
    private const string PendingKey = "V3_GateTest_Pending_20260906";

    [MenuItem("Tools/KitchenMapV3/8e. Test Sequential Gate (플레이모드 회귀)", false, 40)]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            V3.Warn("이미 플레이 모드다 — 먼저 정지하고 다시 실행해라.");
            return;
        }

        if (!VerifyScenePreconditions()) return;

        SessionState.SetBool(PendingKey, true);
        // [정정 2026-09-10, 컨트롤타워 지시 R4] 리포트 출력 경로가 axiom 루트 밖 "맵2_V3_릴레이설계/
        // 무인검증"에서 프로젝트 루트 "V3_Reports/"로 바뀌었다(Scripts/V3_GateTestRunner.cs
        // ResolveOutDir()) — 이 안내 문구도 같이 갱신한다(파일명은 실행 시각으로 자동 결정되므로
        // 여기선 폴더까지만 알린다). V3_REPORT_DIR 환경변수가 있으면 그쪽이 우선한다.
        V3.Log("게이트 테스트: 플레이 모드 진입 예약 완료 — 자동 실행 후 자동 정지한다. 결과: " +
               "<프로젝트 루트>/V3_Reports/게이트테스트_<실행시각>.md(+.json) — V3_REPORT_DIR " +
               "환경변수가 설정돼 있으면 그 폴더.");
        EditorApplication.EnterPlaymode();
    }

    /// <summary>사용자가 이미 '2. Build All' → '3. Setup Play'를 실행한 상태를 전제한다(작업
    /// 지시) — 여기서는 그 전제가 실제로 성립하는지만 확인하고, 안 되면 경고 후 중단한다(플레이
    /// 모드 진입 자체를 안 함).</summary>
    private static bool VerifyScenePreconditions()
    {
        int checkpointCount = Object.FindObjectsOfType<RespawnZone>()
            .Count(z => z.gameObject.name.StartsWith("T0RS_"));
        if (checkpointCount < 12)
        {
            V3.Warn($"T0RS 체크포인트가 {checkpointCount}개뿐이다(기대 12개) — 먼저 " +
                     "'Tools/KitchenMapV3/8. Place T0RS Flags'를 실행해라.");
            return false;
        }

        int gateCount = Object.FindObjectsOfType<T0RS_SequentialGate>().Length;
        if (gateCount < 12)
        {
            V3.Warn($"T0RS_SequentialGate가 {gateCount}개뿐이다(기대 12개) — Place()가 게이트를 " +
                     "붙였는지 확인해라(재실행하면 기존 것은 지우고 새로 만든다).");
            return false;
        }

        if (Object.FindObjectOfType<RespawnController>() == null)
        {
            V3.Warn("RespawnController가 씬에 없다 — 먼저 'Tools/KitchenMapV3/3. Setup Play'를 실행해라.");
            return false;
        }

        if (Object.FindObjectOfType<PlayerMover>() == null)
        {
            V3.Warn("Player_* 오브젝트가 씬에 없다 — 먼저 'Tools/KitchenMapV3/3. Setup Play'를 실행해라.");
            return false;
        }

        return true;
    }

    [InitializeOnLoadMethod]
    private static void Bootstrap()
    {
        // 중복 구독 방지: 이 메서드는 도메인 리로드마다 다시 실행되므로 -= 로 먼저 정리한다.
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode) return;
        if (!SessionState.GetBool(PendingKey, false)) return;
        SessionState.SetBool(PendingKey, false); // 1회성 — 다음 일반 ▶ 플레이에는 관여하지 않는다.

        if (Object.FindObjectOfType<V3_GateTestRunner>() != null) return; // 중복 생성 방지

        GameObject go = new GameObject("V3_GateTestRunner_RUNTIME");
        go.AddComponent<V3_GateTestRunner>();
        Debug.Log("[V3_GateTest] 플레이 모드 진입 — 런타임 러너 생성, 테스트 시작.");
    }
}
#endif
