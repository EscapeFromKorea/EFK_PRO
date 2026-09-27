using UnityEngine;

/// <summary>
/// 관리자 상태 표시(C8_STATUS) — 순찰/의심(주황)/추격(빨강)/수면 색을 월드에 칠한다. 감시 담당은 CCTV 화면으로
/// 이 색을 보고 동료에게 말로 전달한다(CCTV가 월드를 찍으므로 따로 화면 UI를 두지 않는다).
/// 색은 MaterialPropertyBlock으로만 덮어써 공유 머티리얼을 오염시키지 않는다.
/// 추격음 등 소리는 저장소에 오디오 시스템이 없어 넣지 않았다.
/// </summary>
public class ManagerStatusIndicator : MonoBehaviour
{
    public ManagerAgent managerAgent;

    [Tooltip("색을 칠할 렌더러(아이콘·모델 등).")]
    public Renderer[] renderers = new Renderer[0];

    public Color inactiveColor = Color.gray;
    public Color patrolColor = new Color(0.2f, 0.9f, 0.4f);
    public Color suspectColor = new Color(1f, 0.6f, 0.1f);
    public Color chaseColor = new Color(1f, 0.15f, 0.15f);
    public Color sleepColor = new Color(0.35f, 0.45f, 1f);

    private MaterialPropertyBlock mpb;
    private Color applied;
    private bool hasApplied;

    private void Update()
    {
        if (managerAgent == null) return;
        Color c = ColorOf(managerAgent.Current);
        if (hasApplied && c == applied) return;

        if (mpb == null) mpb = new MaterialPropertyBlock();
        mpb.SetColor("_Color", c);
        foreach (Renderer r in renderers)
            if (r != null) r.SetPropertyBlock(mpb);
        applied = c;
        hasApplied = true;
    }

    private Color ColorOf(ManagerAgent.State s)
    {
        switch (s)
        {
            case ManagerAgent.State.Patrol: return patrolColor;
            case ManagerAgent.State.Suspect: return suspectColor;
            case ManagerAgent.State.Chase: return chaseColor;
            case ManagerAgent.State.Asleep: return sleepColor;
            default: return inactiveColor;
        }
    }
}
