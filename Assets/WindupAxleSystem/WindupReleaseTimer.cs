using UnityEngine;

/// <summary>
/// 태엽 축 위에 뜨는 스톱워치형 카운트다운 — "감고 나서 발동까지 남은 시간"을 보여준다
/// (2026-09-05, 사용자 요청). 축 자신은 파생 장치의 `releaseDelay`를 모르므로(설계 원칙 —
/// `WindupAxle`은 무엇이 연결됐는지 모른 채 신호만 낸다, `WindupAxle.cs` 상단 참고) 이 값은
/// 이 컴포넌트가 독립적으로 들고 있다 — 연결된 `RotatingPlatform`/`RailCart`의 `releaseDelay`와
/// 값을 맞춰 둘 것(현재 둘 다 기본 3초로 일치).
///
/// `IWindupReceiver`로 등록해 `OnCrankSwing`(한 번 밀 때마다 호출되는 이산 이벤트)만 구독한다 —
/// 실제 출력 소비는 하지 않는 순수 표시용 수신자다. 미는 순간 카운트다운이 보이기 시작하고,
/// 0에 도달하면 `fadeOutDuration`(기본 1초)에 걸쳐 서서히 투명해진 뒤 완전히 사라진다. 다시
/// 밀면 그 시점부터 카운트다운이 재시작된다(RotatingPlatform.releaseDelay와 같은 "다시 밀면
/// 타이머가 그 시점부터 다시 시작" 규칙). 별도 상태 플래그 없이 `Time.time - lastSwingTime`
/// 하나로 "숨김/카운트다운/페이드아웃"이 전부 자연히 갈린다.
/// </summary>
[RequireComponent(typeof(TextMesh))]
public class WindupReleaseTimer : MonoBehaviour, IWindupReceiver
{
    [Tooltip("신호를 받을 태엽 축.")]
    public WindupAxle axle;
    [Tooltip("발동까지 걸리는 시간(초) — 연결된 파생 장치의 releaseDelay와 맞춰야 한다.")]
    public float releaseDelay = 3f;
    [Tooltip("0초 도달 후 서서히 사라지는 시간(초).")]
    public float fadeOutDuration = 1f;

    private TextMesh label;
    private float lastSwingTime = float.NegativeInfinity;

    void Awake()
    {
        label = GetComponent<TextMesh>();
    }

    void OnEnable()
    {
        if (axle != null) axle.Subscribe(this);
    }

    void OnDisable()
    {
        if (axle != null) axle.Unsubscribe(this);
    }

    public void ApplyOutput(float power, float ratio) { }

    public void OnCrankSwing(float direction)
    {
        lastSwingTime = Time.time;
    }

    void Update()
    {
        // 카메라를 향해 계속 도는 빌보드 — 플레이어가 축 주위 어디에 있든 숫자가 읽힌다.
        if (Camera.main != null)
            transform.rotation = Camera.main.transform.rotation;

        float remaining = releaseDelay - (Time.time - lastSwingTime);
        float alpha;
        if (remaining > 0f)
        {
            alpha = 1f;
            label.text = remaining.ToString("F1");
        }
        else
        {
            float overtime = -remaining;
            alpha = overtime < fadeOutDuration ? 1f - overtime / fadeOutDuration : 0f;
            label.text = "0.0";
        }

        Color c = label.color;
        c.a = alpha;
        label.color = c;
    }
}
