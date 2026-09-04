using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 구-정사면체 협동 리프트 — 홀드형(dead man's switch) 수직 이동판. 패드(<see cref="LiftPad"/>)가
/// 눌리는 동안 목표 위치로 오르고, 안 눌리면 항상 원위치로 되돌아간다. 상세: docs/PRD/Lift.md.
///
/// [왜 발신자-수신자 분리를 안 쓰는가] `CloudTrampoline`의 왕복판과 같은 이유다 — 매 프레임 위치에
/// 계속 관여해야 하는 이동판은 "특정 시점에 플레이어 행동 하나를 바꿔주는" 가속판·바람과 성격이 달라
/// 리시버보다 라이더의 Rigidbody를 직접 조작하는 편이 저장소에서 이미 검증된 방식이다(PRD §6).
///
/// [왜 이동 축이 수직뿐인가] `PlayerRollModeReceiver`는 굴리기 모드 중 매 FixedUpdate 수평 velocity를
/// 무조건 0으로 만든다(수평 이동의 유일한 출처는 텀블이어야 한다는 안전장치). 이 안전장치와 "위치를
/// 직접 가산해 캐리한다"는 이동판 방식이 같은 프레임에서 경합하는지가 미검증이라, 그 경합 자체가
/// 생기지 않는 Y축 이동만으로 범위를 제한한다(PRD §4). Y는 저 안전장치가 건드리지 않는 축이다.
///
/// [왜 트리거 + OnTriggerEnter/Stay인가 — OnCollisionStay를 베끼면 안 되는 이유]
/// 정사면체는 텀블 사이 대기(홀드 반복 간격) 동안에도 <c>PlayerRollModeReceiver.Grab()</c>이 걸어 둔
/// <c>rb.isKinematic == true</c>일 수 있다(PRD §5). kinematic 플랫폼(이 리프트) 위에 kinematic
/// 라이더가 있으면 <c>OnCollisionEnter/Stay</c>는 절대 발화하지 않는다 — 2026-08-21
/// <c>PortalDiag_KinematicTest</c> 실측 검증 완료(`PortalSystem/CLAUDE.md` "구-정사면체 협동 리프트"
/// 절 참고). `CloudTrampoline`의 <c>OnCollisionStay</c> 패턴을 그대로 가져오면 정사면체가 텀블 사이
/// 대기 중일 때(=거의 항상, 붙잡힌 구간이 텀블 그 자체다) 리프트가 라이더를 놓친다.
///
/// [왜 콜라이더가 두 개인가] 트리거 하나만 두면 <c>PlayerGroundContact</c>의 아래 SphereCast가
/// <c>QueryTriggerInteraction.Ignore</c>로 트리거를 무시하므로 라이더가 이 리프트 위에서 "접지"로
/// 잡히지 않는다 — 중력이 계속 끌어당겨 사실상 뚫고 떨어진다. 그래서 라이더를 실제로 떠받치는
/// **솔리드 콜라이더(비-트리거)** 위에, 라이더 감지 전용 **트리거 콜라이더**를 겹쳐 얹는다
/// (`doorPhysics`의 문 콜라이더 + `isBlocked` 트리거와 같은 이중 구성).
///
/// [안전 속도 상한] `moveSpeed` 기본값은 §9가 역산한 안전 상한(`GroundSnapCorrectionCap ÷ 텀블
/// 소요시간`)이다 — 정사면체가 탑승 중 텀블을 시도해도(레벨 디자인 위반, 별도 입력 차단은 없음)
/// 텀블 한 번(0.163초) 동안 리프트가 옮기는 거리가 재접지 보정 허용치보다 작아 `SnapPivotToGround`가
/// 흡수한다. 이 값을 근거(§9) 없이 올리지 마라.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class LiftPlatform : MonoBehaviour
{
    [Header("이동 범위 — 층간 높이차 TBD (PRD §9·§10, 레벨 배치 시 확정)")]
    [Tooltip("원위치에서 목표 위치까지 오르는 높이(Unit, Y축만). 기본 2f는 임시값이다 — 실제 레벨의 " +
             "층간 높이차가 정해지면 이 필드를 그 값으로 바꾼다. 폐기된 정사면체 계단(6단×0.2U=1.2U " +
             "낙차)보다 커도 된다는 것만 확정됐다(PRD §9) — 그 이상 세부값은 레벨 배치 시 결정.")]
    public float riseHeight = 2f;

    [Header("이동 속도 — 안전 상한 (PRD §9, 근거 없이 올리지 말 것)")]
    [Tooltip("리프트가 오르내리는 속도(Unit/s). 기본값은 PortalSystem의 재접지 보정 상한 " +
             "(PlayerRollModeReceiver.GroundSnapCorrectionCap = 0.25×scale)을 정사면체 텀블 소요 시간 " +
             "(0.163초, 이속 5.0 기준)으로 나눈 안전 상한(scale=1 기준 ≈1.53 U/s)이다. 라이더가 " +
             "scale≠1이면 실제 안전선은 이 값 × 그 scale이다 — 확대된 정사면체를 태울 계획이면 " +
             "이 값을 그만큼 낮춰라.")]
    public float moveSpeed = PlayerRollModeReceiver.GroundSnapCorrectionCap / 0.163f;

    [Header("라이더 감지 트리거 (필수 — 위 클래스 요약 참고)")]
    [Tooltip("라이더 감지 전용 트리거 콜라이더. 비워두면 Awake가 이 오브젝트의 콜라이더 중 " +
             "isTrigger인 첫 번째를 자동으로 찾는다. Reset()이 수동 부착 시 기본값을 만들어 준다.")]
    public Collider riderSensor;

    public string playerTag = "Player";

    private Rigidbody body;
    private Vector3 restPosition;
    private bool isHeld;

    // 바디별 겹침 콜라이더 수. 플레이어가 트리거(Player_Mesh)와 솔리드(Player_Collider) 두 콜라이더를
    // 함께 가져 한 도형당 Enter/Exit가 여러 번 불릴 수 있다 — PadTrigger/ExitWeightPlate와 같은 이유로
    // 바디별 카운트를 쓴다(카운트가 0이 될 때만 완전히 내려간 것으로 본다).
    private readonly Dictionary<Rigidbody, int> riderOverlap = new Dictionary<Rigidbody, int>();
    private readonly List<Rigidbody> cleanupBuffer = new List<Rigidbody>();

    private void Reset()
    {
        Collider[] existing = GetComponents<Collider>();
        BoxCollider support = null;
        BoxCollider sensor = null;
        foreach (Collider c in existing)
        {
            BoxCollider box = c as BoxCollider;
            if (box == null) continue;
            if (box.isTrigger && sensor == null) sensor = box;
            else if (!box.isTrigger && support == null) support = box;
        }

        if (support == null) support = gameObject.AddComponent<BoxCollider>();
        support.isTrigger = false;
        support.size = new Vector3(2f, 0.4f, 2f);
        support.center = Vector3.zero;

        if (sensor == null) sensor = gameObject.AddComponent<BoxCollider>();
        sensor.isTrigger = true;
        sensor.size = new Vector3(2f, 0.6f, 2f);
        sensor.center = new Vector3(0f, support.size.y * 0.5f + sensor.size.y * 0.5f, 0f);
        riderSensor = sensor;

        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true; // Reset을 거치지 않고 수동 부착됐어도 강제한다 — 키네마틱 스윕 전제가 깨지면 라이더가 파고든다
        body.useGravity = false;

        if (riderSensor == null)
        {
            foreach (Collider c in GetComponents<Collider>())
                if (c.isTrigger) { riderSensor = c; break; }
        }
        if (riderSensor == null)
            Debug.LogWarning($"[LiftPlatform] {name}: 라이더 감지용 트리거 콜라이더가 없습니다. " +
                             "Reset()으로 기본 콜라이더를 생성하거나 직접 붙이세요.", this);
    }

    private void Start()
    {
        restPosition = body.position;
    }

    /// <summary>패드가 부른다(발신자-수신자 아님, `doorPhysics.SetPadPressed`와 같은 직접 상태 전달).
    /// true인 동안 목표 위치로, false면 원위치로 향한다 — 홀드형(dead man's switch) 전체가 이 한 줄이다.</summary>
    public void SetHeld(bool held) => isHeld = held;

    private void FixedUpdate()
    {
        CleanupDeadRiders();

        Vector3 target = restPosition + (isHeld ? Vector3.up * riseHeight : Vector3.zero);
        Vector3 next = Vector3.MoveTowards(body.position, target, moveSpeed * Time.fixedDeltaTime);
        Vector3 delta = next - body.position;

        body.MovePosition(next);

        // 위에 얹힌 라이더를 같은 만큼 옮겨 함께 캐리한다(CloudTrampoline 왕복판과 같은 방식) — Y만
        // 움직이므로 delta는 항상 수직이고, 라이더의 텀블 좌우 이동(X/Z)과 절대 경합하지 않는다.
        if (delta.sqrMagnitude > 0f)
            foreach (Rigidbody r in riderOverlap.Keys)
                if (r != null) r.position += delta;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;

        riderOverlap.TryGetValue(rb, out int n);
        riderOverlap[rb] = n + 1;
    }

    // Enter만으로 충분한 정상 경로지만, 물리 타이밍 상 Enter 콜백을 놓쳤을 경우를 대비한 자가 회복이다
    // (CloudTrampoline이 같은 이유로 Stay를 쓴다). 이미 등록된 바디는 건드리지 않는다 — 여기서 매
    // 프레임 카운트를 올리면 Exit 하나로는 절대 0에 도달하지 못해 라이더가 영구히 안 내려간다.
    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null || riderOverlap.ContainsKey(rb)) return;
        riderOverlap[rb] = 1;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null || !riderOverlap.TryGetValue(rb, out int n)) return;

        if (n <= 1) riderOverlap.Remove(rb);
        else riderOverlap[rb] = n - 1;
    }

    private void CleanupDeadRiders()
    {
        if (riderOverlap.Count == 0) return;
        cleanupBuffer.Clear();
        foreach (Rigidbody r in riderOverlap.Keys)
            if (r == null) cleanupBuffer.Add(r);
        foreach (Rigidbody r in cleanupBuffer) riderOverlap.Remove(r);
    }

    // 원위치↔목표 사이 수직 이동 범위를 씬 뷰에 표시해 레벨 배치를 돕는다(riseHeight가 TBD라 특히 중요).
    private void OnDrawGizmosSelected()
    {
        Vector3 basePos = Application.isPlaying ? restPosition : transform.position;
        Gizmos.color = new Color(0.4f, 0.9f, 1f);
        Gizmos.DrawLine(basePos, basePos + Vector3.up * riseHeight);
        Gizmos.DrawWireSphere(basePos + Vector3.up * riseHeight, 0.2f);
    }
}
