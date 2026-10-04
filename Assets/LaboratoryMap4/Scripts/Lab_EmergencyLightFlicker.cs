using UnityEngine;

/// <summary>
/// S1 복도 노란 비상등 깜빡임 — 연출 전용 스크립트(S1_설계.md §11-4). 게임 규칙·기믹에는
/// 영향을 주지 않는다. Light 컴포넌트와(있으면) 발광 머티리얼의 _EmissionColor를 함께
/// 토글한다. 등마다 불규칙한 간격(꺼짐 0.05~0.4초, 켜짐 1~4초, 인스펙터로 조절)으로 반복.
/// Update는 프레임당 할당 0(MaterialPropertyBlock·System.Random을 Awake에서 1회만 만든다).
/// 꺼질 때 Light.enabled=false로 끄므로 그동안은 픽셀 조명 슬롯도 차지하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public class Lab_EmergencyLightFlicker : MonoBehaviour
{
    [Header("깜빡임 간격(초) — [제안 §11-4] S1_설계.md, 실측 조정 대상")]
    [SerializeField] private float offDurationMin = 0.05f;
    [SerializeField] private float offDurationMax = 0.4f;
    [SerializeField] private float onDurationMin = 1f;
    [SerializeField] private float onDurationMax = 4f;

    [Header("시드(검사 재현용) — 선택")]
    [Tooltip("켜면 seed 값으로 고정된 난수열을 쓴다(검사 재현용). 끄면 인스턴스마다 다른 시드. " +
             "등마다 seed를 다르게 줘야 서로 다르게 깜빡인다(S1_Lighting이 등 번호로 다르게 넣는다).")]
    [SerializeField] private bool useFixedSeed = false;
    [SerializeField] private int seed = 0;

    [Header("대상(비우면 자동으로 자기 자신/자식에서 찾는다)")]
    [SerializeField] private Light targetLight;
    [SerializeField] private Renderer targetRenderer;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private System.Random rng;
    private MaterialPropertyBlock mpb;
    private bool hasEmission;
    private Color baseEmissionColor;
    private bool isOn;
    private float timer;

    private void Awake()
    {
        if (targetLight == null) targetLight = GetComponent<Light>();
        if (targetLight == null) targetLight = GetComponentInChildren<Light>();
        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();

        // 주의: 인자 없는 new System.Random()은 시각(TickCount) 기반 시드라 같은 프레임에 Awake하는
        // 11개 등이 같은 난수열을 받아 동시에 깜빡인다 → 인스턴스 ID를 섞어 등마다 다르게 한다.
        rng = useFixedSeed
            ? new System.Random(seed)
            : new System.Random(unchecked(System.Environment.TickCount ^ (GetInstanceID() * 73856093) ^ seed));
        mpb = new MaterialPropertyBlock();

        hasEmission = false;
        if (targetRenderer != null && targetRenderer.sharedMaterial != null
            && targetRenderer.sharedMaterial.HasProperty(EmissionColorId))
        {
            hasEmission = true;
            baseEmissionColor = targetRenderer.sharedMaterial.GetColor(EmissionColorId);
        }

        isOn = true;
        timer = NextDuration(onDurationMin, onDurationMax);
        ApplyState();
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;

        isOn = !isOn;
        timer = isOn ? NextDuration(onDurationMin, onDurationMax) : NextDuration(offDurationMin, offDurationMax);
        ApplyState();
    }

    private float NextDuration(float min, float max)
    {
        if (max < min) { float tmp = min; min = max; max = tmp; }
        double t = rng.NextDouble();
        // 0 이하 간격이면 매 프레임 토글되어 번쩍임이 생기므로 하한 0.01초.
        return Mathf.Max(0.01f, min + (float)t * (max - min));
    }

    private void OnValidate()
    {
        offDurationMin = Mathf.Max(0.01f, offDurationMin);
        offDurationMax = Mathf.Max(offDurationMin, offDurationMax);
        onDurationMin = Mathf.Max(0.01f, onDurationMin);
        onDurationMax = Mathf.Max(onDurationMin, onDurationMax);
    }

    private void ApplyState()
    {
        if (targetLight != null)
        {
            targetLight.enabled = isOn;
        }

        if (hasEmission && targetRenderer != null)
        {
            targetRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionColorId, isOn ? baseEmissionColor : Color.black);
            targetRenderer.SetPropertyBlock(mpb);
        }
    }
}
