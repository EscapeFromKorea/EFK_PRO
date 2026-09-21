using UnityEngine;

/// <summary>
/// 주기형 물리 함정 — 망치. 상태 순서 상부 대기 → 바닥 예고 → 하강 → 저점 대기 → 상승(반복). 하강/
/// 저점 대기 상태에서만 머리(솔리드)가 위험하고, 나머지 상태는 무피해다. 상세:
/// docs/PRD/PeriodicTraps.md §3·§4.
///
/// [속도 기반 이동 — 지속시간이 아니라] <see cref="dropDistance"/>(하강 거리)를 정해두고 하강/상승은
/// <c>MoveTowards</c> 속도로 움직인다(<c>LiftPlatform</c>과 같은 방식) — 지속시간으로 두면 거리가
/// 바뀔 때마다 속도가 달라져 통로마다 체감이 갈린다. 하강/상승에 걸리는 실제 시간은 거리÷속도로
/// 자연히 정해진다.
///
/// [끼임 안전 정지는 상승에만 적용] 하강은 위험 상태의 이동이라 접촉 = 의도된 피격이고 맞으면 즉시
/// 자리를 벗어나 압착이 성립하지 않는다. 상승(무피해 이동)만 압착 후보다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class HammerTrap : PeriodicTrapBase
{
    private enum Phase { TopWait, Descending, BottomWait, Ascending }

    [Header("망치 — 상태 지속시간 (사양 §4, 미정값 그레이박스)")]
    [Tooltip("상부 대기 + 바닥 예고를 합친 시간(초). 예고 표시는 이 중 마지막 telegraphLeadSeconds " +
             "동안만 켠다.")]
    public float topWaitSeconds = 2f;

    [Tooltip("topWaitSeconds 중 예고 표시가 켜지는 마지막 구간 길이(초).")]
    public float telegraphLeadSeconds = 0.6f;

    [Tooltip("저점(바닥)에서 머무는 시간(초). 이 동안도 위험하다(사양 §4 표).")]
    public float bottomWaitSeconds = 0.4f;

    [Header("망치 — 이동 (속도 기반, 거리는 배치 시 실측)")]
    [Tooltip("상부 대기 위치에서 내려가는 거리(Unit, 월드 -Y).")]
    public float dropDistance = 2f;

    public float descendSpeed = 6f;
    public float ascendSpeed = 3f;

    [Header("망치 — 콜라이더")]
    [Tooltip("머리(솔리드). 하강/저점 대기 상태에서만 위험. 비우면 Reset이 기본값을 만든다.")]
    public BoxCollider headCollider;

    [Header("예고 표시 (선택, 사양 §4 — 위험 투영 범위와 동일 크기로 배치해 둬라)")]
    [Tooltip("바닥 예고 렌더러(선택). 비우면 표시하지 않는다. 색은 MaterialPropertyBlock으로만 " +
             "덮어써 공유 머티리얼을 오염시키지 않는다(RespawnSystem 체크포인트 깃발과 같은 방식).")]
    public Renderer telegraphMarker;
    public Color telegraphColor = new Color(1f, 0.6f, 0.1f);
    public Color idleColor = Color.white;

    private Rigidbody body;
    private Vector3 topPosition;
    private Vector3 bottomPosition;
    private Phase phase;
    private float phaseElapsed;
    private MaterialPropertyBlock mpb;

    private void Reset()
    {
        if (headCollider == null) headCollider = GetComponent<BoxCollider>();
        if (headCollider == null) headCollider = gameObject.AddComponent<BoxCollider>();
        headCollider.size = new Vector3(1f, 0.6f, 1f);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        topPosition = body.position;
        bottomPosition = topPosition + Vector3.down * dropDistance;
        phase = Phase.TopWait;
        phaseElapsed = Mathf.Repeat(startPhase, Mathf.Max(0.05f, topWaitSeconds));
        mpb = new MaterialPropertyBlock();
    }

    private bool IsDangerous => phase == Phase.Descending || phase == Phase.BottomWait;

    private void FixedUpdate()
    {
        UpdateTelegraph();

        switch (phase)
        {
            case Phase.TopWait:
                phaseElapsed += Time.fixedDeltaTime;
                if (phaseElapsed >= topWaitSeconds) EnterPhase(Phase.Descending);
                break;

            case Phase.Descending:
                if (MoveToward(bottomPosition, descendSpeed)) EnterPhase(Phase.BottomWait);
                break;

            case Phase.BottomWait:
                phaseElapsed += Time.fixedDeltaTime;
                if (phaseElapsed >= bottomWaitSeconds) EnterPhase(Phase.Ascending);
                break;

            case Phase.Ascending:
                if (MoveToward(topPosition, ascendSpeed)) EnterPhase(Phase.TopWait);
                break;
        }
    }

    private void EnterPhase(Phase next)
    {
        // 위험 상태를 벗어나는 전이(저점 대기 → 상승)에서만 피격 집계 창을 연다(사양 §4 "장치·주기·
        // 대상당 1회").
        if (IsDangerous && next == Phase.Ascending) ResetHitWindow();
        phase = next;
        phaseElapsed = 0f;
    }

    /// <returns>목표 위치에 도달하면 true.</returns>
    private bool MoveToward(Vector3 target, float speed)
    {
        Vector3 next = Vector3.MoveTowards(body.position, target, speed * Time.fixedDeltaTime);
        Vector3 delta = next - body.position;

        if (!IsDangerous && headCollider != null && !CanAdvance(headCollider, delta))
            return false; // 압착 위험 — 이 스텝은 제자리에 머문다.

        body.MovePosition(next);
        return (next - target).sqrMagnitude < 1e-6f;
    }

    private void OnCollisionEnter(Collision collision) => HandleCollision(collision);
    private void OnCollisionStay(Collision collision) => HandleCollision(collision);

    private void HandleCollision(Collision collision)
    {
        if (!IsDangerous || !collision.collider.CompareTag(playerTag)) return;
        TryRegisterHit(collision.rigidbody);
    }

    private void UpdateTelegraph()
    {
        if (telegraphMarker == null) return;
        bool warn = phase == Phase.TopWait && (topWaitSeconds - phaseElapsed) <= telegraphLeadSeconds;
        mpb.SetColor("_Color", warn ? telegraphColor : idleColor);
        telegraphMarker.SetPropertyBlock(mpb);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 top = Application.isPlaying ? topPosition : transform.position;
        Vector3 bottom = top + Vector3.down * dropDistance;
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
        Gizmos.DrawLine(top, bottom);
        Gizmos.DrawWireSphere(bottom, 0.2f);
    }
}
