using UnityEngine;

/// <summary>
/// 대상 Rigidbody 하나의 중력을 "개별로" 오버라이드한다. 기믹(무중력 버블, 추후 모래시계 감속
/// 구역 등)이 중력을 조정해야 할 때 이 컴포넌트 하나를 거치면 된다 — PlayerJump.LaunchToHeight가
/// "발사"의 단일 창구이듯, 이건 "중력 변경"의 단일 창구다.
///
/// 전역 Physics.gravity는 절대 건드리지 않는다(다른 플레이어까지 영향받는 사고 방지). 대신 이
/// 오브젝트만 useGravity를 끄고, 매 FixedUpdate에 원하는 배율만큼 직접 중력 가속도를 얹는다
/// (AddForce, Acceleration 모드) — 질량이 개입하지 않아 배율이 곧 실제 체감 배율이 된다.
///
/// 안전 복원: OnDisable/OnDestroy에서 즉시 원상 복구해, 파괴·리스폰·씬 전환 어느 경로로 빠져나가도
/// 중력이 꺼진 채로 영구 부양하는 사고를 막는다.
///
/// 씬에 미리 붙여둘 필요 없다 — 기믹이 대상에 이 컴포넌트가 없으면 그 자리에서 자동으로 붙인다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerGravityOverride : MonoBehaviour
{
    private Rigidbody rb;
    private float defaultDrag;

    private bool overriding;
    private float currentScale = 1f;
    private float targetScale = 1f;
    private float lerpSpeed;
    private float uplift;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        defaultDrag = rb.drag;
    }

    void OnEnable() => ForceRestoreImmediate();
    void OnDisable() => ForceRestoreImmediate();
    void OnDestroy() => ForceRestoreImmediate();

    /// <summary>중력 배율을 scale로, 저항을 기본값 x dragMultiplier로 transitionTime에 걸쳐 바꾼다.
    /// uplift는 중력 감소와 별개로 추가로 미는 위쪽 가속도(선택, 기본 0 = 순수 감소만).</summary>
    public void SetGravityScale(float scale, float dragMultiplier, float uplift, float transitionTime)
    {
        overriding = true;
        rb.useGravity = false;
        targetScale = scale;
        lerpSpeed = transitionTime > 0f ? 1f / transitionTime : 999f;
        rb.drag = defaultDrag * dragMultiplier;
        this.uplift = uplift;
    }

    /// <summary>정상 중력(배율 1)/기본 저항으로 되돌린다. 구역 이탈 시 호출.</summary>
    public void RestoreDefault(float transitionTime)
    {
        if (!overriding) return; // 이미 정상이면 할 일 없음.
        targetScale = 1f;
        lerpSpeed = transitionTime > 0f ? 1f / transitionTime : 999f;
        rb.drag = defaultDrag;
        uplift = 0f;
    }

    void FixedUpdate()
    {
        if (!overriding) return;

        currentScale = Mathf.MoveTowards(currentScale, targetScale, lerpSpeed * Time.fixedDeltaTime);
        rb.AddForce(Physics.gravity * currentScale + Vector3.up * uplift, ForceMode.Acceleration);

        if (Mathf.Approximately(targetScale, 1f) && Mathf.Approximately(currentScale, 1f))
            ForceRestoreImmediate();
    }

    private void ForceRestoreImmediate()
    {
        overriding = false;
        currentScale = 1f;
        targetScale = 1f;
        uplift = 0f;
        if (rb == null) rb = GetComponent<Rigidbody>();
        rb.useGravity = true;
        rb.drag = defaultDrag;
    }
}
