using UnityEngine;

/// <summary>맵4 CH5 전용: 천장 경고등 → 하강 → 안전 위치 확인 → 팀 도끼 예고·진자.</summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class Lab_AxeReveal : MonoBehaviour
{
    public enum RevealPhase { Hidden, Warning, Descending, WaitingForClearance, TeamWarmup, Running }
    public AxeTrap trap;
    public Light warningLight;
    public float lift = 6f;
    public float warningSeconds = 0.15f;
    public float descendSpeed = 10f;
    public float restWarningSeconds = 0.15f;
    public float warningAfterWarmup = 0.1f;
    public float revealBelowWorldY; // 천장 밑면. 날 끝이 이 높이 아래에 와야 자식 시각물을 켠다.
    public RevealPhase Phase { get; private set; } = RevealPhase.Hidden;
    public bool VisualsShown { get; private set; }
    public Vector3 RestPosition { get; private set; }
    public float StartedAt { get; private set; }
    public float ReachedRestAt { get; private set; }
    public float TrapEnabledAt { get; private set; }
    private Rigidbody body;
    private float phaseTime;
    private readonly Collider[] playerBuffer = new Collider[16];
    private Renderer[] visuals;
    private Quaternion baseRotation;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        // AxeTrap.Awake도 이 시점의 Rigidbody 회전을 restRotation으로 저장한다.
        // 두 Awake의 호출 순서와 무관하게 어느 쪽도 아직 회전을 바꾸지 않았다.
        baseRotation = body.rotation;
        RestPosition = transform.position - Vector3.up * lift;
    }

    private void OnEnable()
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (trap == null || body == null || trap.bladeCollider == null || trap.supportCollider == null || warningLight == null)
        {
            Debug.LogError("[Lab_AxeReveal] 도끼·Rigidbody·날/손잡이 콜라이더·경고등 연결 누락 — 운동 시작 취소.", this);
            enabled = false;
            return;
        }
        if (revealBelowWorldY <= 0f)
        {
            Debug.LogError("[Lab_AxeReveal] 천장 밑면 세계 높이가 0 이하 — 이전 생성 씬일 수 있어 등장 시작 취소. S5 재생성 필요.", this);
            enabled = false;
            return;
        }
        body.isKinematic = true;
        // 최신 AxeTrap은 StartRun 뒤 자체 예고에서 중립→시작 위상을 연속 회전한다.
        // 하강을 중립 자세로 끝내야 팀의 내부 currentRotation과 실제 Rigidbody 자세가 일치한다.
        trap.enabled = false;
        trap.bladeCollider.enabled = false;
        trap.supportCollider.enabled = false;
        visuals = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer visual in visuals) visual.enabled = false;
        VisualsShown = false;
        if (warningLight != null) warningLight.enabled = true;
        StartedAt = Time.time;
        phaseTime = 0f;
        Phase = RevealPhase.Warning;
    }

    private void FixedUpdate()
    {
        phaseTime += Time.fixedDeltaTime;
        switch (Phase)
        {
            case RevealPhase.Warning:
                if (phaseTime >= warningSeconds) Enter(RevealPhase.Descending);
                break;
            case RevealPhase.Descending:
                if (!VisualsShown && BladeBottomY() <= revealBelowWorldY) ShowVisuals();
                Vector3 next = Vector3.MoveTowards(body.position, RestPosition,
                    Mathf.Max(0.01f, descendSpeed) * Time.fixedDeltaTime);
                body.MovePosition(next);
                body.MoveRotation(baseRotation);
                if ((next - RestPosition).sqrMagnitude <= 0.000001f)
                {
                    if (!VisualsShown) ShowVisuals();
                    ReachedRestAt = Time.time;
                    Enter(RevealPhase.WaitingForClearance);
                }
                break;
            case RevealPhase.WaitingForClearance:
                if (phaseTime < restWarningSeconds || PlayerInsideBladeOrHandle()) break;
                trap.bladeCollider.enabled = true;
                trap.supportCollider.enabled = true;
                trap.enabled = true;
                TrapEnabledAt = Time.time;
                Enter(RevealPhase.TeamWarmup);
                break;
            case RevealPhase.TeamWarmup:
                if (phaseTime < trap.warmupSeconds + warningAfterWarmup) break;
                if (warningLight != null) warningLight.enabled = false;
                Enter(RevealPhase.Running);
                break;
        }
    }

    private void Enter(RevealPhase next) { Phase = next; phaseTime = 0f; }
    private void ShowVisuals()
    {
        foreach (Renderer visual in visuals) visual.enabled = true;
        VisualsShown = true;
    }

    private float BladeBottomY()
    {
        BoxCollider blade = trap.bladeCollider;
        float bottom = float.PositiveInfinity;
        Vector3 half = blade.size * 0.5f;
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = blade.center + Vector3.Scale(half, new Vector3(x, y, z));
                    bottom = Mathf.Min(bottom, transform.TransformPoint(corner).y);
                }
        return bottom;
    }
    private bool PlayerInsideBladeOrHandle() => PlayerInside(trap.bladeCollider) || PlayerInside(trap.supportCollider);

    private bool PlayerInside(BoxCollider box)
    {
        Vector3 scale = transform.lossyScale;
        Vector3 half = Vector3.Scale(box.size * 0.5f,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        Vector3 center = transform.TransformPoint(box.center);
        int count = Physics.OverlapBoxNonAlloc(center, half, playerBuffer, transform.rotation, ~0,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = playerBuffer[i];
            if (hit != null && hit.GetComponentInParent<PlayerMover>() != null) return true;
        }
        return false;
    }
}
