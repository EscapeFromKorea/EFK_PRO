using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// T0RS 순차 게이트 회귀 테스트 — 런타임 러너 (2026-09-06, 근거: 협업/코워크채널/FROM_코워크.md
/// [2026-09-06 #3] 1-②). 외부 검토(codex, [확인함] 등급) 지적: "Unity는 <b>비활성(enabled=false)
/// MonoBehaviour에도 OnTriggerEnter를 보낸다</b>"가 사실이면, T0RS_SequentialGate.cs(당시판)가
/// 전제하던 "다음 깃발 잠금 = RespawnZone.enabled=false"가 무효하다 — 잠긴 RespawnZone도 여전히
/// OnTriggerEnter를 받아 RespawnController.SetCheckpoint(this)를 호출할 수 있기 때문이다
/// (RespawnZone.OnTriggerEnter()에 enabled 검사가 없다). 이 러너는 그 재현 여부를 플레이 모드 물리로 직접 측정한다.
///
/// [2026-09-06 컨트롤타워 확정 + 잠금 재설계 — 이 러너가 지금 검증하는 것]
/// 위 지적은 컨트롤타워가 사실로 확정했다(TO_코워크 09-06 #8). 그 결과 T0RS_SequentialGate.cs·
/// V3_Checkpoints.cs가 잠금 표현을 "다음 깃발 자신의 BoxCollider.enabled"로 전면 재설계했다
/// (RespawnZone MonoBehaviour는 이제 항상 enabled=true, 게이트는 부모와 동일 규격의 전용 자식
/// GameObject `T0RS_nn_Gate`로 옮겨졌다 — 세부는 T0RS_SequentialGate.cs 클래스 주석 참고). 이
/// 러너의 "잠김" 판정은 그래서 더 이상 RespawnZone.enabled가 아니라 <b>해당 존 자신의
/// BoxCollider.enabled==false</b>로 갱신했다(IsLocked() 헬퍼). 기대값도 재설계 이후 기준으로
/// 바뀐다: G1(잠긴 03 직행 → 컨트롤러 체크포인트 불변, bugConfirmed=false여야 정상) · G3(잠긴
/// 04 직행 → 05는 계속 잠김 유지, chainBypassed=false여야 정상) · G2(01→02→03 정상 경로 →
/// 매 단계 next가 정상 언락). 즉 이 러너가 처음 쓰였을 때는 "버그가 재현되는가"를 확인하는
/// 도구였지만, 지금은 "고친 잠금이 의도대로 작동하는가"를 확인하는 회귀 테스트다.
///
/// [왜 플레이 모드인가 — Edit Mode로는 이 질문 자체가 재현 불가]
/// V3_PhysLab.cs(2026-09-06, 서랍 계단 실험)가 이미 겪은 한계와 같다: RespawnZone·
/// T0RS_SequentialGate·PlayerMover는 전부 ExecuteInEditMode가 없는 일반 MonoBehaviour라, Edit
/// Mode에서는 Awake 이후의 생명주기 콜백(Update·FixedUpdate·OnTriggerEnter 전부 포함)이 아예
/// 호출되지 않는다 — Physics.Simulate로 물리 스텝을 직접 돌려도 "컴포넌트가 그 콜백을 받는가"
/// 자체가 Edit Mode의 스크립트 실행 루프 정지 때문에 성립하지 않는다. 그래서 이번 질문(비활성
/// MonoBehaviour가 물리 메시지를 받는가)은 실제 Play Mode 위에서만 답이 나온다.
///
/// [사본 무수정] RespawnZone·RespawnController·PlayerControlSwitcher(저장소 사본)는 읽기만
/// 한다. 존 자신의 BoxCollider.enabled 대입도 하지 않는다(이미 T0RS_SequentialGate.cs·
/// V3_Checkpoints.cs가 쓰는 잠금 표현이며, 이 러너는 그 상태를 관찰만 한다 — IsLocked() 참고).
/// RespawnController의 현재 체크포인트는 공개 API가 없어(private currentZone·hasCheckpoint)
/// 리플렉션으로 읽는다 — 값을 쓰지는 않는다.
///
/// [체크포인트 식별 방식] 이름을 하드코딩하지 않는다 — V3_Checkpoints.cs 클래스 주석의 확정 규칙
/// "이름 접두는 반드시 T0RS_nn"만 의존해 GameObject 이름의 "T0RS_" 다음 두 자리 숫자로 정렬한다.
///
/// [테스트 실행 순서가 G1→G3→G2인 이유] 지시서의 번호는 G1·G2·G3이지만, G2(01→02→03 정상 경로)를
/// 먼저 돌리면 T0RS_03을 정상적으로(self.enabled=true인 채) 밟는 순간 그 자신의 게이트가 next
/// (T0RS_04)를 합법적으로 언락해버려, G3이 요구하는 "04가 아직 잠긴 채" 전제가 깨진다. G1은
/// T0RS_03만 건드리고 T0RS_03의 게이트 자체는 self.enabled==false라 next를 안 푸므로(잠금 로직은
/// 그대로 작동 — RespawnZone.OnTriggerEnter만 우회되는지가 쟁점) T0RS_04 잠금 상태에 영향이
/// 없다. 그래서 G1 → G3(04 아직 잠김 확인) → G2(정상 경로, 그 과정에서 04가 합법적으로 풀리는
/// 것은 기대된 부작용) 순서로 실행하고, 보고서에는 G1·G2·G3 표기 그대로 결과를 정리한다.
/// </summary>
public class V3_GateTestRunner : MonoBehaviour
{
    // ── RespawnController 비공개 필드 리플렉션(읽기 전용 — 공개 API 없음) ──
    private static readonly FieldInfo CurrentZoneField =
        typeof(RespawnController).GetField("currentZone", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo HasCheckpointField =
        typeof(RespawnController).GetField("hasCheckpoint", BindingFlags.NonPublic | BindingFlags.Instance);

    // [R5, map-reviewer 23차] Run()의 사전 전체 스캔(체인 전체를 훑어 구조 이상을 미리 기록)과
    // RunG3()의 개별 판정이 같은 존을 각자 report.structuralIssues에 추가해 중복 등록되던 문제를
    // 존 이름 기준으로 막는다 — AddStructuralIssue() 참고.
    private readonly HashSet<string> _reportedStructuralZones = new HashSet<string>();

    private void AddStructuralIssue(GateTestReport report, string zoneName, string detail)
    {
        if (!_reportedStructuralZones.Add(zoneName)) return; // 이미 이 존은 기록됨 — 중복 스킵
        report.structuralIssues.Add(detail);
    }

    [Serializable]
    public class ZoneSnapshot
    {
        public string name;
        public bool respawnZoneEnabled; // [2026-09-06] false = 잠김(=자기 BoxCollider.enabled==false). 필드명은 구판 유지, 값은 IsLocked() 반전.
        public string gateNext;         // T0RS_SequentialGate.next 대상 이름 ("(null)" 가능)
    }

    [Serializable]
    public class G1Result
    {
        public string targetZone;
        public bool targetLockedBefore;
        public string controllerCheckpointBefore;
        public string controllerCheckpointAfter;
        public bool targetLockedAfterEntry;
        public bool bugConfirmed; // true = 잠긴 채로도 체크포인트가 이 존으로 갱신됨(=새 콜라이더 잠금 실패)
        // [R1, map-reviewer 21차] 텔레포트가 실제로 그 존의 볼륨 안에 떨어졌는지 좌표·거리로
        // 기록 — 목적지만 존 중심으로 잡고 실제로는 볼륨 밖(물리에 밀려남 등)에 떨어진 채
        // "정상"으로 오판되는 공허 통과를 막는다. InsideZoneVolume() 참고.
        public Vector3 teleportDestination;
        public bool insideVolumeAfterTeleport;
        public float distanceOutsideVolume;
    }

    [Serializable]
    public class G2Step
    {
        public string zone;
        public bool zoneEnabledBeforeEnter;
        public string nextZone;
        public bool nextEnabledAfter;
        public string controllerCheckpointAfter;
        public string note;
        // [R1] 위 G1Result와 동일 목적 — 이 스텝이 텔레포트한 zone 볼륨 안에 실제로 도달했는지.
        public Vector3 teleportDestination;
        public bool insideVolumeAfterTeleport;
        public float distanceOutsideVolume;
    }

    [Serializable]
    public class G3Result
    {
        public string targetZone;
        public string nextZone;
        public bool targetLockedBefore;
        public bool nextEnabledBefore;
        public bool nextEnabledAfter;
        public bool chainBypassed; // true = 잠긴 04 진입만으로 05가 풀림(순서 보증 실패)
        public string note;
        // [R1] 위 G1Result와 동일 목적 — target 볼륨 안에 실제로 도달했는지.
        public Vector3 teleportDestination;
        public bool insideVolumeAfterTeleport;
        public float distanceOutsideVolume;
        // [R5, map-reviewer 21차] BoxCollider가 없는(구조 이상) 경우를 "이미 언락"과 구분한다.
        public bool structuralError;
    }

    [Serializable]
    public class GateTestReport
    {
        public string unityVersion;
        public string startedAtUtc;
        public string fatalError;
        public List<ZoneSnapshot> initialState = new List<ZoneSnapshot>();
        public string initialCheckpoint;
        public G1Result g1;
        public List<G2Step> g2 = new List<G2Step>();
        public G3Result g3;
        public string finalCheckpoint;
        // [2026-09-06] 필드명은 최초 가설(비활성 MonoBehaviour가 OnTriggerEnter를 받는가) 시절
        // 그대로 유지 — 지금은 g1.bugConfirmed를 그대로 담아 "새 BoxCollider 잠금이 뚫렸는가"를
        // 뜻한다(값 계산 로직은 RunG1() 그대로, IsLocked()가 BoxCollider 기준으로 바뀐 것만 반영).
        public bool disabledMonoBehaviourReceivesOnTriggerEnter;
        // [R1, map-reviewer 23차] 위 판정은 텔레포트가 실제로 T0RS_03 볼륨 안에 도달했을 때만
        // 의미가 있다 — g1.insideVolumeAfterTeleport가 false면(물리에 밀려나거나 계산이 틀려
        // 볼륨 밖에 떨어진 경우) "체크포인트가 안 바뀌었다"는 관찰이 새 잠금이 유효해서인지,
        // 애초에 그 볼륨에 안 들어가서(트리거 자체가 안 걸려서)인지 구분할 수 없다. false면
        // 헤드라인·판정 둘 다 "잠금 유효"를 인쇄하지 않고 "판정 무효(볼륨 미도달)"로 하드
        // 게이트한다 — BuildMarkdownReport() 헤드라인 섹션 참고.
        public bool verdictValid;
        // [R5, map-reviewer 21차] BoxCollider가 없는 등 "잠김/해제 판정 자체가 불가능한" 구조
        // 이상을 별도로 모은다 — GetLockStatus()가 StructuralError를 만날 때마다 여기 추가된다.
        // 이전에는 이런 경우가 조용히 "언락"으로 계산돼(IsLocked 반환값 false) G3 등이 "이미
        // 언락된 상태라 스킵"이라는 잘못된 진단을 리포트에 남길 수 있었다 — 이 목록이 그 구분을
        // 대신한다.
        public List<string> structuralIssues = new List<string>();
    }

    private void Start()
    {
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        var report = new GateTestReport
        {
            unityVersion = Application.unityVersion,
            startedAtUtc = DateTime.UtcNow.ToString("u")
        };

        RespawnController rc = FindObjectOfType<RespawnController>();
        if (rc == null) { Fail(report, "RespawnController를 찾지 못함 — Setup Play 확인 필요."); yield break; }

        List<GameObject> chain = FindOrderedCheckpoints();
        if (chain.Count < 5)
        {
            Fail(report, $"T0RS 체크포인트가 {chain.Count}개만 발견됨(G3 판정에 최소 5개=T0RS_01~05 필요) — " +
                          "'8. Place T0RS Flags'가 정상 실행됐는지 확인해라.");
            yield break;
        }

        PlayerMover mover = FindTestMover();
        if (mover == null) { Fail(report, "PlayerMover를 찾지 못함 — Setup Play 확인 필요."); yield break; }
        Rigidbody rb = mover.GetComponent<Rigidbody>();
        if (rb == null) { Fail(report, $"'{mover.gameObject.name}'에 Rigidbody가 없음."); yield break; }

        // [R5] 체인 전체를 미리 훑어 구조 이상(BoxCollider 없음)이 있으면 리포트 최상단에
        // 기록해 둔다 — 개별 G1/G3/G2 판정이 그 존을 다루기도 전에 문제를 알 수 있게 한다.
        foreach (GameObject go in chain)
        {
            if (GetLockStatus(go, out string structErr) == LockStatus.StructuralError)
                AddStructuralIssue(report, go.name, $"{go.name}: {structErr}");
        }

        report.initialState = SnapshotAll(chain);
        report.initialCheckpoint = CurrentCheckpointName(rc);

        // 스폰 직후 물리 안정화 1프레임.
        yield return new WaitForFixedUpdate();

        yield return RunG1(report, chain, rc, mover, rb);
        yield return RunG3(report, chain, rc, mover, rb);
        yield return RunG2(report, chain, rc, mover, rb);

        report.finalCheckpoint = CurrentCheckpointName(rc);
        report.disabledMonoBehaviourReceivesOnTriggerEnter = report.g1 != null && report.g1.bugConfirmed;
        // [R1, map-reviewer 23차] 하드 게이트 — G1이 볼륨에 도달 못 했으면 위 판정 자체가 무효다.
        report.verdictValid = report.g1 != null && report.g1.insideVolumeAfterTeleport;

        WriteReports(report);

        yield return new WaitForSeconds(0.2f);
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#endif
    }

    // ── 케이스 G1: 잠긴 T0RS_03에 직행 — 비활성 RespawnZone이 OnTriggerEnter를 받는가 ──
    private IEnumerator RunG1(GateTestReport report, List<GameObject> chain, RespawnController rc,
        PlayerMover mover, Rigidbody rb)
    {
        var g1 = new G1Result();
        GameObject target = chain[2]; // T0RS_03 (0-based index 2)

        g1.targetZone = target.name;
        g1.targetLockedBefore = IsLocked(target);
        g1.controllerCheckpointBefore = CurrentCheckpointName(rc);

        g1.teleportDestination = ZoneCenter(target);
        yield return TeleportAndWait(mover, rb, g1.teleportDestination, 3);
        // [R1] 텔레포트가 실제로 볼륨 안에 도달했는지 — 목적지 좌표만으로는 물리에 밀려나거나
        // 계산이 틀려도 "정상"으로 오판할 수 있다. 실제 정착 위치(mover.transform.position)로 검증.
        g1.insideVolumeAfterTeleport = InsideZoneVolume(target, mover.transform.position, out float g1Dist);
        g1.distanceOutsideVolume = g1Dist;

        g1.controllerCheckpointAfter = CurrentCheckpointName(rc);
        g1.targetLockedAfterEntry = IsLocked(target);
        // 잠긴 상태였는데(targetLockedBefore) 체크포인트가 이 존으로 바뀌었다면 codex 지적 확정.
        g1.bugConfirmed = g1.targetLockedBefore && g1.controllerCheckpointAfter == target.name;

        report.g1 = g1;
    }

    // ── 케이스 G3: 잠긴 T0RS_04 직행 — 게이트 자신의 self.enabled 검사가 사슬을 지키는가 ──
    private IEnumerator RunG3(GateTestReport report, List<GameObject> chain, RespawnController rc,
        PlayerMover mover, Rigidbody rb)
    {
        var g3 = new G3Result();
        GameObject target = chain[3];   // T0RS_04
        GameObject nextGo = chain[4];   // T0RS_05

        g3.targetZone = target.name;
        g3.nextZone = nextGo.name;

        // [R5, map-reviewer 21차] BoxCollider가 없는 구조 이상을 "이미 언락"과 분리한다 — 이전
        // IsLocked()는 구조 이상도 false(언락)로 반환해, 아래 스킵 분기가 "04가 이미 풀려 있어서
        // 건너뛴다"는 잘못된 진단을 리포트에 남길 수 있었다(실제로는 판정 자체가 불가능한 상태).
        LockStatus targetStatus = GetLockStatus(target, out string targetStructErr);
        g3.targetLockedBefore = targetStatus == LockStatus.Locked;
        g3.nextEnabledBefore = !IsLocked(nextGo);

        if (targetStatus == LockStatus.StructuralError)
        {
            g3.structuralError = true;
            g3.note = "판정 불가(구조 이상 — BoxCollider 없음): " + targetStructErr;
            AddStructuralIssue(report, target.name, $"G3/{target.name}: {targetStructErr}");
            report.g3 = g3;
            yield break;
        }
        if (!g3.targetLockedBefore)
        {
            g3.note = "04가 이미 언락된 상태라 '우회 진입' 전제가 성립하지 않음 — 판정 보류(스킵).";
            report.g3 = g3;
            yield break;
        }

        g3.teleportDestination = ZoneCenter(target);
        yield return TeleportAndWait(mover, rb, g3.teleportDestination, 3);
        // [R1] 실제 정착 위치가 target 볼륨 안인지 검증(공허 통과 방지).
        g3.insideVolumeAfterTeleport = InsideZoneVolume(target, mover.transform.position, out float g3Dist);
        g3.distanceOutsideVolume = g3Dist;

        g3.nextEnabledAfter = !IsLocked(nextGo);
        g3.chainBypassed = !g3.nextEnabledBefore && g3.nextEnabledAfter;
        report.g3 = g3;
    }

    // ── 케이스 G2: 01→02→03 순서 진행 — 정상 경로에서 next가 매번 풀리는가 ──
    private IEnumerator RunG2(GateTestReport report, List<GameObject> chain, RespawnController rc,
        PlayerMover mover, Rigidbody rb)
    {
        var steps = new List<G2Step>();
        int count = Mathf.Min(3, chain.Count);
        for (int i = 0; i < count; i++)
        {
            GameObject cur = chain[i];
            var step = new G2Step
            {
                zone = cur.name,
                zoneEnabledBeforeEnter = !IsLocked(cur),
            };
            if (!step.zoneEnabledBeforeEnter)
                step.note = "이 시점에 이미 잠긴 상태 — 정상 순차 경로라면 발생하지 않아야 함(선행 케이스 간섭 의심).";

            step.teleportDestination = ZoneCenter(cur);
            yield return TeleportAndWait(mover, rb, step.teleportDestination, 3);
            // [R1] 실제 정착 위치가 cur 볼륨 안인지 검증(공허 통과 방지).
            step.insideVolumeAfterTeleport = InsideZoneVolume(cur, mover.transform.position, out float g2Dist);
            step.distanceOutsideVolume = g2Dist;

            if (i + 1 < chain.Count)
            {
                GameObject nextGo = chain[i + 1];
                step.nextZone = nextGo.name;
                step.nextEnabledAfter = !IsLocked(nextGo);
            }
            step.controllerCheckpointAfter = CurrentCheckpointName(rc);
            steps.Add(step);
        }
        report.g2 = steps;
    }

    // ── 공통 유틸 ──────────────────────────────────────────────────
    private static List<GameObject> FindOrderedCheckpoints()
    {
        var byIndex = new SortedDictionary<int, GameObject>();
        foreach (RespawnZone rz in FindObjectsOfType<RespawnZone>())
        {
            string n = rz.gameObject.name;
            if (n.Length < 7 || !n.StartsWith("T0RS_")) continue;
            if (!int.TryParse(n.Substring(5, 2), out int idx)) continue;
            byIndex[idx] = rz.gameObject;
        }
        return new List<GameObject>(byIndex.Values);
    }

    private static PlayerMover FindTestMover()
    {
        Transform active = PlayerControlSwitcher.ActiveTarget;
        if (active != null)
        {
            PlayerMover m = active.GetComponent<PlayerMover>();
            if (m != null) return m;
        }
        PlayerMover[] all = FindObjectsOfType<PlayerMover>();
        foreach (PlayerMover m in all)
            if (m.IsControlled) return m;
        return all.Length > 0 ? all[0] : null;
    }

    /// <summary>[R1, map-reviewer 21차] Collider.bounds는 콜라이더가 disabled이거나 GameObject가
    /// inactive면 빈 바운딩 박스(0 크기, 사실상 transform.position)를 반환한다(Unity 문서 확정
    /// 동작) — 이 러너가 텔레포트 목적지로 삼는 존은 정확히 "잠긴(BoxCollider.enabled=false)"
    /// 상태가 정상 케이스라, box.bounds.center를 그대로 쓰면 잠긴 존은 전부 transform.position
    /// (구역 바닥, 볼륨 중심이 아니다)으로 잘못 계산돼 버린다. enabled 여부와 무관하게 항상 정확한
    /// 볼륨 중심을 주는 transform.TransformPoint(box.center)로 교체.</summary>
    private static Vector3 ZoneCenter(GameObject go)
    {
        BoxCollider box = go.GetComponent<BoxCollider>();
        if (box != null) return go.transform.TransformPoint(box.center);
        Collider any = go.GetComponent<Collider>();
        return any != null ? any.bounds.center : go.transform.position;
    }

    /// <summary>[R1] 텔레포트 결과가 실제로 그 존의 볼륨(부모 BoxCollider의 center/size로 계산한
    /// 로컬 AABB) 안에 도달했는지 검증 — "목적지 좌표는 볼륨 중심으로 잡았지만 실제 정착 위치는
    /// 물리에 밀려나거나 계산 오류로 볼륨 밖"인 공허 통과를 리포트가 놓치지 않게 한다. 월드 좌표를
    /// 존의 로컬 좌표계로 변환해 각 축 half-extent와 비교하므로 회전된 존(예: T0RS_10, Y180)에도
    /// 정확하다. BoxCollider가 없으면(구조 이상) 판정 불가로 처리한다(distanceOutside=-1, false).</summary>
    private static bool InsideZoneVolume(GameObject zoneGo, Vector3 worldPos, out float distanceOutside)
    {
        BoxCollider box = zoneGo.GetComponent<BoxCollider>();
        if (box == null) { distanceOutside = -1f; return false; }

        Vector3 local = zoneGo.transform.InverseTransformPoint(worldPos) - box.center;
        Vector3 half = box.size * 0.5f;
        Vector3 excess = new Vector3(
            Mathf.Max(0f, Mathf.Abs(local.x) - half.x),
            Mathf.Max(0f, Mathf.Abs(local.y) - half.y),
            Mathf.Max(0f, Mathf.Abs(local.z) - half.z));
        distanceOutside = excess.magnitude;
        return distanceOutside <= 0f;
    }

    /// <summary>[R5, map-reviewer 21차] 잠김/해제/구조 이상(BoxCollider 없음) 셋을 분리한다.
    /// 이전 IsLocked()는 구조 이상도 "언락(false)"으로 반환했는데, 주석은 "잠기지 않은 것으로
    /// 취급하지 않겠다"고 해놓고 실제 반환값(false=언락)은 정확히 그렇게 취급하는 코드였다(주석·
    /// 코드 불일치). 특히 G3은 대상이 "이미 언락"이면 우회 진입 전제가 안 맞는다고 보고 조용히
    /// 스킵하는데, 구조 이상도 같은 경로로 스킵되면 "이미 풀려 있어서 건너뛴다"는 오진단이
    /// 리포트에 남는다 — 그래서 호출부가 구조 이상을 놓치지 않도록 별도 상태로 내놓는다.</summary>
    private enum LockStatus { Locked, Unlocked, StructuralError }

    private static LockStatus GetLockStatus(GameObject zoneGo, out string errorDetail)
    {
        errorDetail = null;
        BoxCollider box = zoneGo.GetComponent<BoxCollider>();
        if (box == null)
        {
            errorDetail = $"'{zoneGo.name}'에 BoxCollider가 없음 — T0RS_ 접두 명명 규칙으로 걸러진 " +
                          "체크포인트에선 정상적으로는 발생하지 않는 구조 이상(잠금/해제 판정 불가).";
            return LockStatus.StructuralError;
        }
        return box.enabled ? LockStatus.Unlocked : LockStatus.Locked;
    }

    /// <summary>[2026-09-06 재설계] "잠김"의 정의 — 이 존(부모 RespawnZone GameObject) 자신의
    /// BoxCollider.enabled==false. RespawnZone MonoBehaviour의 enabled는 이제 항상 true라
    /// 더 이상 잠금 신호가 아니다(V3_Checkpoints.cs·T0RS_SequentialGate.cs 2026-09-06 재설계
    /// 참고). 구조 이상(BoxCollider 없음) 여부까지 구분해야 하는 호출부는 GetLockStatus()를
    /// 직접 써라 — 이 래퍼는 편의상 Locked만 true를 반환하고 Unlocked·StructuralError는 둘 다
    /// false로 뭉뚱그린다(스냅샷 표시처럼 구조 이상 구분이 필요 없는 자리 전용, R5 참고).</summary>
    private static bool IsLocked(GameObject zoneGo) => GetLockStatus(zoneGo, out _) == LockStatus.Locked;

    /// <summary>텔레포트 직후 SyncTransforms로 콜라이더 위치를 즉시 갱신하고(지시 5.항), 물리
    /// 스텝이 몇 프레임 지나 트리거 Enter가 실제로 처리될 때까지 기다린다. RespawnController.
    /// TeleportRoutine()과 동일한 rb.position+transform.position
    /// 이중 대입 패턴을 따른다.</summary>
    private static IEnumerator TeleportAndWait(PlayerMover mover, Rigidbody rb, Vector3 worldPos, int fixedUpdatesToWait)
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = worldPos;
        mover.transform.position = worldPos;
        Physics.SyncTransforms();

        for (int i = 0; i < fixedUpdatesToWait; i++)
            yield return new WaitForFixedUpdate();
    }

    private static List<ZoneSnapshot> SnapshotAll(List<GameObject> chain)
    {
        var list = new List<ZoneSnapshot>();
        foreach (GameObject go in chain)
        {
            // [2026-09-06 재설계] 게이트는 이제 부모(RespawnZone) GameObject가 아니라 그 자식
            // ("T0RS_nn_Gate")에 있다 — GetComponentInChildren로 찾는다.
            T0RS_SequentialGate gate = go.GetComponentInChildren<T0RS_SequentialGate>(true);
            list.Add(new ZoneSnapshot
            {
                name = go.name,
                respawnZoneEnabled = !IsLocked(go), // 필드명은 유지 — 의미는 "언락 상태(=BoxCollider.enabled)"
                gateNext = (gate != null && gate.next != null) ? gate.next.gameObject.name : "(null)"
            });
        }
        return list;
    }

    /// <summary>RespawnController의 현재 체크포인트 — 공개 API가 없어(RespawnController의
    /// private 필드 currentZone·hasCheckpoint) 리플렉션으로만 읽는다. 값을 쓰지 않는다.</summary>
    private static string CurrentCheckpointName(RespawnController rc)
    {
        if (rc == null) return "(컨트롤러없음)";
        if (HasCheckpointField == null || CurrentZoneField == null)
            return "(리플렉션실패:필드못찾음)";

        bool has = (bool)HasCheckpointField.GetValue(rc);
        if (!has) return "(없음)";

        RespawnZone z = (RespawnZone)CurrentZoneField.GetValue(rc);
        return z != null ? z.gameObject.name : "(없음,currentZone=null)";
    }

    private void Fail(GateTestReport report, string message)
    {
        Debug.LogError("[V3_GateTestRunner] " + message);
        report.fatalError = message;
        WriteReports(report);
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#endif
    }

    // ── 결과 출력 ──────────────────────────────────────────────────
    /// <summary>[정정 2026-09-10, 컨트롤타워 지시 R4] 기본 출력 폴더를 axiom 루트 밖(클론마다
    /// 존재 여부가 다른 "맵2_V3_릴레이설계" 폴더) 대신 이 유니티 프로젝트 안
    /// "&lt;프로젝트 루트&gt;/V3_Reports"로 통일한다(Assets 밖이라 유니티가 임포트하지 않는다 —
    /// 36차 R4 지적: 팀원 클론에는 저장소 밖 한글 폴더가 없다). V3_REPORT_DIR 환경변수가 있으면
    /// 그것을 최우선으로 쓴다(컨트롤타워가 무인검증 폴더로 지정 — Editor/V3_PhysLab.cs·
    /// Editor/V3_Batch.cs와 동일 규칙, -v3out과 달리 axiom 루트 제한 없이 그대로 채택). 이
    /// 러너는 Play Mode 런타임에서 돈다 — Environment.GetEnvironmentVariable은 런타임에서도
    /// 그대로 동작한다(에디터 전용 API 아님).</summary>
    private static string ResolveOutDir()
    {
        string envOverride = Environment.GetEnvironmentVariable("V3_REPORT_DIR");
        if (!string.IsNullOrEmpty(envOverride))
            return Path.GetFullPath(envOverride);

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.GetFullPath(Path.Combine(projectRoot, "V3_Reports"));
    }

    /// <summary>[정정 2026-09-09, 검문27차 A2] 파일명을 실행 시각으로 고정한다 — 이전엔
    /// "2026-09-06" 리터럴이 박혀 있어 09-09 12:13 실행분도 그 이름으로 저장됐다(검문27차 §1
    /// (나) 지적, 실제 생성 09-09 12:13:41 — 파일명·제목 날짜가 사실과 달랐다). 기존
    /// 게이트테스트_2026-09-06.* 파일은 이 변경으로도 건드리지 않는다(다른 파일명이라 자동으로
    /// 보존됨).</summary>
    private static string ReportBaseName()
    {
        return "게이트테스트_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm");
    }

    private static void WriteReports(GateTestReport report)
    {
        string outDir = ResolveOutDir();
        Directory.CreateDirectory(outDir);
        string baseName = ReportBaseName();

        string json = JsonUtility.ToJson(report, true);
        File.WriteAllText(Path.Combine(outDir, baseName + ".json"), json, new UTF8Encoding(true));

        string md = BuildMarkdownReport(report);
        File.WriteAllText(Path.Combine(outDir, baseName + ".md"), md, new UTF8Encoding(true));

        Debug.Log("[V3_GateTestRunner] 리포트 저장 완료: " + outDir);
    }

    private static string YN(bool b) => b ? "예" : "아니오";

    private static string BuildMarkdownReport(GateTestReport r)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# 게이트 테스트 — T0RS 순차 잠금 플레이모드 회귀 (" + DateTime.Now.ToString("yyyy-MM-dd") + ")");
        sb.AppendLine();
        sb.AppendLine("근거: 협업/코워크채널/FROM_코워크.md [2026-09-06 #3] 1-② (외부 검토 codex [확인함] 등급).");
        sb.AppendLine($"Unity 버전: {r.unityVersion} · 실행 시각(UTC): {r.startedAtUtc}");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(r.fatalError))
        {
            sb.AppendLine("## 실행 중단");
            sb.AppendLine();
            sb.AppendLine("치명 오류: " + r.fatalError);
            return sb.ToString();
        }

        sb.AppendLine("## 핵심 판정 — 새 콜라이더 잠금(BoxCollider.enabled=false)이 실제로 체크포인트 등록을 막는가");
        sb.AppendLine();
        sb.AppendLine("[2026-09-06 재설계 이후] 이 러너는 원래 '비활성 MonoBehaviour가 OnTriggerEnter를 " +
                       "받는가'(codex 지적)를 재현하려고 만들어졌으나, 그 지적은 컨트롤타워가 이미 사실로 " +
                       "확정했고(TO_코워크 09-06 #8) 잠금 자체가 BoxCollider.enabled 기반으로 재설계됐다. " +
                       "지금은 그 새 잠금이 의도대로 막아 주는지를 재검증한다.");
        sb.AppendLine();
        sb.AppendLine("측정: 자기 BoxCollider.enabled=false(잠김)인 T0RS_03에 직행 후 RespawnController의 " +
                       "현재 체크포인트가 T0RS_03으로 바뀌었는지로 판정(G1) — 안 바뀌어야 정상(새 잠금 유효).");
        sb.AppendLine();
        // [R1, map-reviewer 23차 하드 게이트] insideVolumeAfterTeleport==false면 "잠금 유효"를
        // 인쇄하지 않는다 — 텔레포트가 볼륨에 안 들어갔으면 체크포인트 불변이 잠금 덕인지 트리거
        // 미도달 때문인지 구분 불가능하다.
        if (!r.verdictValid)
        {
            sb.AppendLine("**결과: 판정 무효(볼륨 미도달) — 텔레포트가 T0RS_03 볼륨 안에 실제로 도달하지 " +
                          "못했다(아래 G1 항목의 [R1] 볼륨 도달 여부 참고). 체크포인트가 안 바뀐 것이 새 " +
                          "잠금 덕인지 트리거 자체가 안 걸린 것인지 이 실행으로는 구분할 수 없다 — " +
                          "재실행 필요.**");
        }
        else
        {
            sb.AppendLine($"**결과: {(r.disabledMonoBehaviourReceivesOnTriggerEnter ? "예 — 잠긴 존인데도 체크포인트가 갱신됐다(새 콜라이더 잠금 실패 — 재검토 필요)." : "아니오 — 잠긴 존에 직행해도 체크포인트가 안 바뀌었다(새 콜라이더 잠금 유효, 기대값과 일치).")}**");
        }
        sb.AppendLine();

        // [R5] 구조 이상(BoxCollider 없음) — 있으면 눈에 띄게 최상단 근처에 노출한다.
        sb.AppendLine("## 구조 이상 (BoxCollider 없음 등 — 판정 불가와 '이미 언락'을 혼동하지 않기 위한 목록)");
        sb.AppendLine();
        if (r.structuralIssues != null && r.structuralIssues.Count > 0)
        {
            foreach (string issue in r.structuralIssues)
                sb.AppendLine("  ⚠️ " + issue);
        }
        else
        {
            sb.AppendLine("(없음)");
        }
        sb.AppendLine();

        // [R4, map-reviewer 23차] 아래 foreach가 r.initialState 전체(체인 12개 전부)를 출력하는데
        // 헤더가 "01~05, 발췌"라고 해 일부만 보여주는 것처럼 오독됐다 — 실제로 잘라내는 로직이
        // 없으므로 "발췌" 표기를 지우고 전체 출력임을 명시한다.
        sb.AppendLine("## 초기 상태 (T0RS_01~12 전체)");
        sb.AppendLine();
        sb.AppendLine("| 존 | 자기 BoxCollider.enabled | gate.next |");
        sb.AppendLine("|---|---|---|");
        foreach (var z in r.initialState)
            sb.AppendLine($"| {z.name} | {(z.respawnZoneEnabled ? "언락" : "잠김")} | {z.gateNext} |");
        sb.AppendLine();
        sb.AppendLine($"테스트 시작 전 컨트롤러 체크포인트: {r.initialCheckpoint}");
        sb.AppendLine();

        sb.AppendLine("## G1 — 잠긴 T0RS_03 직행 (기대: 체크포인트 불변)");
        sb.AppendLine();
        if (r.g1 != null)
        {
            sb.AppendLine("| 항목 | 값 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| 대상 | {r.g1.targetZone} |");
            sb.AppendLine($"| 진입 전 잠김 여부 | {YN(r.g1.targetLockedBefore)} |");
            sb.AppendLine($"| 진입 전 컨트롤러 체크포인트 | {r.g1.controllerCheckpointBefore} |");
            sb.AppendLine($"| 진입 후 컨트롤러 체크포인트 | {r.g1.controllerCheckpointAfter} |");
            sb.AppendLine($"| 진입 후에도 잠김 상태(자기 BoxCollider.enabled=false) 유지 | {YN(r.g1.targetLockedAfterEntry)} |");
            sb.AppendLine($"| **판정: 잠금 실패(체크포인트가 잠긴 존으로 바뀜)** | **{YN(r.g1.bugConfirmed)}** |");
            sb.AppendLine($"| [R1] 텔레포트 목적지(월드) | {r.g1.teleportDestination} |");
            sb.AppendLine($"| [R1] 실제 도달 위치가 볼륨 안인가(공허 통과 검증) | {YN(r.g1.insideVolumeAfterTeleport)}" +
                           $"{(r.g1.insideVolumeAfterTeleport ? "" : $" (볼륨 밖 {r.g1.distanceOutsideVolume:F3}U)")} |");
        }
        else
        {
            sb.AppendLine("(실행 안 됨)");
        }
        sb.AppendLine();

        sb.AppendLine("## G3 — 잠긴 T0RS_04 직행(우회 진입) 후 T0RS_05 언락 여부 (순서 보증 검증)");
        sb.AppendLine();
        if (r.g3 != null)
        {
            sb.AppendLine("| 항목 | 값 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| 대상 | {r.g3.targetZone} |");
            sb.AppendLine($"| 다음 존 | {r.g3.nextZone} |");
            sb.AppendLine($"| 진입 전 04 잠김 여부 | {YN(r.g3.targetLockedBefore)} |");
            sb.AppendLine($"| 진입 전 05 언락 여부 | {YN(r.g3.nextEnabledBefore)} |");
            sb.AppendLine($"| 진입 후 05 언락 여부 | {YN(r.g3.nextEnabledAfter)} |");
            sb.AppendLine($"| **판정: 순서 보증 실패(우회로 사슬이 풀림)** | **{YN(r.g3.chainBypassed)}** |");
            if (string.IsNullOrEmpty(r.g3.note))
            {
                // note가 비어 있다는 것은 스킵 분기(구조 이상·이미 언락) 둘 다 안 탔다는 뜻 —
                // 즉 실제로 텔레포트가 일어난 정상 케이스에서만 [R1] 검증값을 표시한다. 스킵
                // 케이스는 텔레포트 자체를 안 했으므로 목적지가 (0,0,0)으로 채워진 무의미한 값이다.
                sb.AppendLine($"| [R1] 텔레포트 목적지(월드) | {r.g3.teleportDestination} |");
                sb.AppendLine($"| [R1] 실제 도달 위치가 볼륨 안인가(공허 통과 검증) | {YN(r.g3.insideVolumeAfterTeleport)}" +
                               $"{(r.g3.insideVolumeAfterTeleport ? "" : $" (볼륨 밖 {r.g3.distanceOutsideVolume:F3}U)")} |");
            }
            sb.AppendLine($"| [R5] 구조 이상(BoxCollider 없음)으로 판정 불가 | {YN(r.g3.structuralError)} |");
            if (!string.IsNullOrEmpty(r.g3.note)) sb.AppendLine($"| 비고 | {r.g3.note} |");
        }
        else
        {
            sb.AppendLine("(실행 안 됨)");
        }
        sb.AppendLine();

        sb.AppendLine("## G2 — 01 → 02 → 03 순서 진입 (정상 경로)");
        sb.AppendLine();
        sb.AppendLine("| 존 | 진입 전 언락여부 | 다음 존 | 진입 후 다음 언락여부 | 진입 후 컨트롤러 체크포인트 | " +
                       "[R1] 볼륨 안 도달(공허 통과 검증) | 비고 |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var s in r.g2)
            sb.AppendLine($"| {s.zone} | {(s.zoneEnabledBeforeEnter ? "언락" : "잠김")} | {s.nextZone} | " +
                           $"{(s.nextEnabledAfter ? "언락" : "잠김")} | {s.controllerCheckpointAfter} | " +
                           $"{YN(s.insideVolumeAfterTeleport)}{(s.insideVolumeAfterTeleport ? "" : $" (볼륨 밖 {s.distanceOutsideVolume:F3}U)")} | " +
                           $"{s.note} |");
        sb.AppendLine();

        sb.AppendLine($"테스트 종료 후 최종 컨트롤러 체크포인트: {r.finalCheckpoint}");
        sb.AppendLine();
        sb.AppendLine("## 실행 순서 참고");
        sb.AppendLine();
        sb.AppendLine("내부 실행 순서는 G1 → G3 → G2다(클래스 상단 주석 참고) — G2(01→02→03)를 먼저 돌리면 " +
                       "T0RS_03을 정상적으로 밟는 순간 04가 합법적으로 언락되어 G3의 '04가 아직 잠긴' 전제가 " +
                       "깨지기 때문이다. 표기는 지시서 순서(G1·G2·G3) 그대로 두었다.");

        return sb.ToString();
    }
}
