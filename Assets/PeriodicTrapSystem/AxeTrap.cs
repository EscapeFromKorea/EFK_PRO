using UnityEngine;

/// <summary>
/// 주기형 물리 함정 — 도끼. 로컬 축 기준 -A~+A 진자 왕복(왕복 시간 P). 날(솔리드)만 위험, 손잡이/
/// 지지대(솔리드)는 절대 무피해다. 상세: docs/PRD/PeriodicTraps.md §3·§4.
///
/// [코사인 진자 — 순간 방향 전환 금지(확정)] 각도를 <c>-A·cos(2π t)</c>로 계산하면 각속도가
/// <c>sin(2π t)</c>에 비례해 양 끝(t=0, 0.5, 1)에서 정확히 0이 된다 — 실제 진자처럼 부드럽게
/// 감속해 멈췄다가 재가속한다. 선형 왕복(각속도가 사각파)이었다면 끝에서 속도가 즉시 뒤집혀
/// 확정 요구를 어긴다.
///
/// [블레이드·지지대가 둘 다 솔리드인 이유] 트리거로 두면 물리적으로 아무것도 못 밀어내 "무피해
/// 이동부가 압착한다"는 전제 자체가 성립하지 않는다. 둘 다 이 오브젝트의 별도 BoxCollider(같은
/// GameObject — LiftPlatform이 지지/센서 콜라이더를 같은 오브젝트에 두는 것과 같은 구성)로 둬서,
/// 자식 오브젝트로 나눴을 때 생기는 "트리거 콜백이 부모가 아니라 자식에서 온다" 문제를 피한다.
/// 어느 쪽에 닿았는지는 <c>OnCollisionEnter</c>의 <c>ContactPoint.thisCollider</c>로 가른다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class AxeTrap : PeriodicTrapBase
{
    [Header("도끼 — 진자 운동 (사양 §4)")]
    [Tooltip("왕복 최대각 A(degrees). 로컬 축 기준 -A~+A를 오간다.")]
    public float maxAngleDegrees = 45f;

    [Tooltip("왕복 1회(-A→+A→-A) 전체 시간 P(초). 값이 작을수록 빠르게 오간다. 미정값 — 배치 시 실측.")]
    public float period = 2f;

    [Tooltip("씬 시작 직후 날이 정지한 채 관찰만 시키는 예고 시간(초). 이 동안은 무피해다.")]
    public float warmupSeconds = 1f;

    [Tooltip("로컬 회전축. 기본 Z(옆모습 2D 횡스크롤 기준 화면 안쪽 축) — 배치에 맞게 바꿔라.")]
    public Vector3 localRotationAxis = Vector3.forward;

    [Header("도끼 — 콜라이더 (같은 오브젝트의 BoxCollider 2개)")]
    [Tooltip("날 영역(솔리드). 예고를 지나면 항상 위험(사양 §4 표 — 상태 분기 없음). 비우면 Reset이 " +
             "기본값을 만든다.")]
    public BoxCollider bladeCollider;

    [Tooltip("손잡이/지지대(솔리드). 절대 무피해지만 물리적으로 밀 수 있어 압착 판정 대상이다.")]
    public BoxCollider supportCollider;

    private Rigidbody body;
    private Quaternion restRotation;
    private float swingClock;
    private float warmupElapsed;
    private bool warmupDone;
    private int lastCycle = -1;

    private void Reset()
    {
        if (bladeCollider == null) bladeCollider = gameObject.AddComponent<BoxCollider>();
        bladeCollider.center = new Vector3(0f, 1f, 0f);
        bladeCollider.size = new Vector3(1f, 0.2f, 0.15f);

        if (supportCollider == null) supportCollider = gameObject.AddComponent<BoxCollider>();
        supportCollider.center = new Vector3(0f, 0.4f, 0f);
        supportCollider.size = new Vector3(0.2f, 0.8f, 0.2f);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        restRotation = body.rotation;
        swingClock = startPhase;
    }

    private bool IsDangerous => warmupDone; // 예고를 지나면 왕복 내내 위험(사양 §4 표).

    private void FixedUpdate()
    {
        if (!warmupDone)
        {
            warmupElapsed += Time.fixedDeltaTime;
            if (warmupElapsed < warmupSeconds) return;
            warmupDone = true;
        }

        float periodSafe = Mathf.Max(0.05f, period);
        Quaternion currentRot = RotationAt(swingClock, periodSafe);
        Quaternion nextRot = RotationAt(swingClock + Time.fixedDeltaTime, periodSafe);

        // 위험 상태의 이동은 압착 검사를 하지 않는다(접촉 = 의도된 피격, 맞으면 즉시 자리를 벗어난다).
        // 도끼는 예고 이후 항상 위험이라(IsDangerous == warmupDone) 이 분기는 사실상 걸릴 일이 없지만,
        // HammerTrap/SpikeTrap과 같은 형태로 남겨둔다 — 손잡이 전용 무피해 구간이 생기면 그대로 걸린다.
        bool canAdvance = true;
        if (supportCollider != null)
        {
            Vector3 localCenter = Vector3.Scale(supportCollider.center, transform.lossyScale);
            Vector3 delta = nextRot * localCenter - currentRot * localCenter;
            if (!IsDangerous) canAdvance = CanAdvance(supportCollider, delta);
        }

        if (canAdvance)
        {
            swingClock += Time.fixedDeltaTime;
            body.MoveRotation(nextRot);
        }
        // 압착 중이면 swingClock을 그대로 둔다 — 다음 프레임 같은 각도에서 재판정, 벗어나면 자연 재개.

        int cycle = Mathf.FloorToInt(swingClock / periodSafe);
        if (cycle != lastCycle)
        {
            ResetHitWindow();
            lastCycle = cycle;
        }
    }

    private Quaternion RotationAt(float clock, float periodSafe)
    {
        float t = Mathf.Repeat(clock, periodSafe) / periodSafe;
        float angle = -maxAngleDegrees * Mathf.Cos(2f * Mathf.PI * t);
        return restRotation * Quaternion.AngleAxis(angle, localRotationAxis);
    }

    private void OnCollisionEnter(Collision collision) => HandleCollision(collision);
    private void OnCollisionStay(Collision collision) => HandleCollision(collision);

    private void HandleCollision(Collision collision)
    {
        if (!IsDangerous || !collision.collider.CompareTag(playerTag)) return;

        foreach (ContactPoint contact in collision.contacts)
        {
            if (contact.thisCollider != bladeCollider) continue;
            TryRegisterHit(collision.rigidbody);
            return;
        }
    }
}
