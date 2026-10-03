using UnityEngine;

/// <summary>
/// 순서 입력 레버(C2_LEVER_1~3) — 기존 DoorSystem의 <see cref="LeverHead"/>(플레이어가 밀면 각도가 변하는
/// 물리 레버)를 그대로 두고, 각도가 "당겨진 위치"에 도달하는 순간 컨트롤러에 입력 1회만 전달한다.
/// docs/PRD/IsolationRescue.md §3 SequenceLever.
///
/// [왜 각도를 에지 검출하는가]
/// PRD가 "선택 시 1회 신호만 전달하고 상태 유지 중 반복 신호를 여러 번으로 세지 않는다"고 못박았다. 레버는
/// 한 번 밀면 한동안 당겨진 채 머물므로, 각도가 문턱을 넘는 순간(위로 건넌 프레임)에만 신호를 보내고 충분히
/// 되돌아올 때까지 다시 쏘지 않는다. 문턱을 두 개(누름/해제)로 둔 이유는 문턱 부근에서 각도가 떨리면
/// 한 번의 조작이 여러 번으로 세어지는 것을 막으려는 것이다(히스테리시스).
///
/// [판정은 컨트롤러가]
/// 이 컴포넌트는 정답 여부를 모른다. 정답이면 쌓고 오답이면 순환시키는 것은
/// <see cref="IsolationRescueController.SubmitLever"/>다. 진행 중이 아닐 때의 입력은 거기서 무시된다.
///
/// [되돌림 속도 주의]
/// LeverHead 기본값은 놓은 뒤 천천히 되돌아온다(씬의 문 레버는 전체 복귀에 수십 초). 순서 입력은 다음 레버를
/// 바로 조작해야 하므로 시험 배치 메뉴는 returnDelay/returnSpeed를 빠르게 설정한다. 직접 배치한다면
/// 같은 값으로 맞출 것.
/// </summary>
public class SequenceLever : MonoBehaviour
{
    [Tooltip("격리 컨트롤러.")]
    public IsolationRescueController controller;

    [Tooltip("각도를 읽을 레버. 비워 두면 같은 오브젝트나 자식에서 찾는다.")]
    public LeverHead lever;

    [Tooltip("이 레버의 번호(1~3). 컨트롤러 정답 순서의 레버 번호와 같다.")]
    [Range(1, IsolationRescueController.LeverCount)]
    public int leverNumber = 1;

    [Tooltip("이 값(0~1, 최대 각도 대비 위치) 이상으로 당겨지면 입력 1회. 0.75 = 최대 각도의 약 3/4. 실제 플레이어가 밀 때 막대가 끝까지 가기 전에 몸이 밀려나므로 끝에 가깝게 두면 입력이 안 먹는다(2026-10-03 실측).")]
    [Range(0f, 1f)]
    public float pressThreshold = 0.75f;

    [Tooltip("이 값 이하로 되돌아와야 다음 입력을 받는다. pressThreshold보다 작아야 한다.")]
    [Range(0f, 1f)]
    public float releaseThreshold = 0.4f;

    private bool armed = true;

    /// <summary>다음 당김을 받을 수 있는 상태인가(되돌림 확인 후 true).</summary>
    public bool Armed => armed;

    private void Reset()
    {
        lever = GetComponentInChildren<LeverHead>();
    }

    private void Awake()
    {
        if (lever == null) lever = GetComponentInChildren<LeverHead>();
    }

    private void Start()
    {
        if (lever == null)
            Debug.LogWarning($"[SequenceLever] '{name}'에 LeverHead가 연결되지 않아 동작하지 않는다.", this);
        if (controller == null)
            Debug.LogWarning($"[SequenceLever] '{name}'에 컨트롤러가 연결되지 않았다.", this);
        if (releaseThreshold >= pressThreshold)
            Debug.LogWarning($"[SequenceLever] '{name}'의 releaseThreshold({releaseThreshold})가 " +
                             $"pressThreshold({pressThreshold}) 이상이라 한 번 당기면 다시 받지 못할 수 있다.", this);
    }

    private void Update()
    {
        if (lever == null) return;
        Evaluate(NormalizedPosition(lever));
    }

    /// <summary>레버 각도를 0(닫힘 끝) ~ 1(당김 끝)로 정규화한다. doorPhysics가 문 위치를 구하는 식과 같다.</summary>
    public static float NormalizedPosition(LeverHead head)
    {
        if (head == null || head.maxAngle <= 0f) return 0f;
        return Mathf.InverseLerp(-head.maxAngle, head.maxAngle, head.GetCurrentAngle());
    }

    /// <summary>
    /// 정규화된 레버 위치(0~1)를 한 번 평가한다. 문턱을 위로 넘는 순간에만 입력을 보내고 true를 돌려준다.
    /// 입력을 보낸 뒤에는 releaseThreshold 아래로 내려올 때까지 무장 해제 상태다.
    /// </summary>
    public bool Evaluate(float position)
    {
        if (!armed)
        {
            if (position <= releaseThreshold) armed = true;
            return false;
        }

        if (position < pressThreshold) return false;

        armed = false;
        if (controller == null) return false;

        IsolationRescueController.SubmitResult result = controller.SubmitLever(leverNumber);
        Debug.Log($"[SequenceLever] 레버 {leverNumber} 당김 → {result}", this);
        return true;
    }
}
