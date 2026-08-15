using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// BreakableObject가 런타임에 생성한 파편 하나에 붙어 등장·수명·소멸을 담당한다(씬에 손으로
/// 배치하는 컴포넌트가 아니다 — FallingRockShard와 같은 패턴).
///
/// [등장 — 커지기, 구 폴백 경로 전용]
/// 랜덤 파편이 스폰 즉시 최종 크기로 나타나면 원본이 그 프레임에 파편으로 바뀌는 하드 컷처럼
/// 보인다(2026-08-11 사용자 피드백 — "갑자기 변하는 느낌"). 작은 크기에서 growDuration 동안
/// 최종 크기로 커지게 해 "부서져 나오는" 느낌을 낸다. 물리는 스케일과 무관하게 스폰 즉시 걸리므로
/// 커지는 동안에도 이미 날아가고 있다. 격자 경로는 growDuration을 0으로 넘겨 이 연출을 끈다 —
/// 격자 조각은 파괴 순간 이미 원본 실루엣을 이루므로 커지는 연출이 오히려 역효과다.
/// 콜라이더 크기가 로컬 스케일을 따라가므로 0 근처에서 PhysX가 경고를 뱉지 않도록 MinScale로
/// 하한을 둔다(FallingRockShard와 같은 이유).
///
/// [소멸 — 알파 페이드, 두 경로 공통]
/// 예전엔 생성 측이 Destroy(fragment, lifetime)으로 수명만 걸어 파편이 5초 뒤 한 프레임에
/// 사라졌다(2026-08-14 코드 리뷰에서 발견 — 등장은 세 번이나 다듬었는데 퇴장이 없었다).
/// FallingRockShard는 축소로 사라지지만 여기는 알파 페이드를 쓴다(사용자 확정 2026-08-14) —
/// 축소는 격자 경로에서 "조각이 도로 빨려 들어가는" 그림이 되어 형태 정합이라는 목적과 어긋난다.
///
/// [페이드가 이 저장소의 블렌드 드리프트 함정을 피하는 방법]
/// 이 저장소는 반투명 머티리얼 <b>에셋</b>의 `_Mode`와 블렌드 값이 어긋나 Unity가 프로젝트를 열
/// 때마다 조용히 덮어쓰는 문제를 오래 앓았다(RainbowBridgeSystem/CLAUDE.md 참고). 그래서
/// DestructionSystem은 원래 알파 페이드를 일부러 안 썼다. 지금은 `renderer.material`(런타임
/// <b>인스턴스</b>)에만 블렌드 상태를 세팅한다 — 인스턴스는 직렬화되지 않아 `_Mode` 재유도
/// 대상이 아니고, 파편과 함께 파괴되므로 공유 에셋(.mat)에 알파가 남지도 않는다. 그래서 여기서는
/// 그 함정이 성립하지 않는다. 대신 `_Mode`와 블렌드 값을 <b>반드시 함께</b> 맞춘다(같은 문서의
/// 교훈 — 어긋나면 에러 없이 조용히 틀린다).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DestructionFragment : MonoBehaviour
{
    private const float MinScale = 0.05f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in Standard
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP/HDRP Lit

    /// <summary>생성한 BreakableObject가 넣어준다. 0이면 즉시 최종 크기(격자 경로).</summary>
    [HideInInspector] public float growDuration = 0.12f;

    /// <summary>생성한 BreakableObject가 fragmentLifetime을 넣어준다. 이 시간이 지나면 스스로
    /// 파괴한다 — 생성 측의 Destroy(obj, t)를 대신한다(페이드가 끝난 뒤에 사라져야 하므로).</summary>
    [HideInInspector] public float lifetime = 5f;

    /// <summary>수명 끝에서 알파가 1→0으로 가는 데 걸리는 시간(초). lifetime보다 크면 lifetime으로
    /// clamp된다(태어나자마자 페이드 시작). 0이면 예전처럼 하드 팝.</summary>
    [HideInInspector] public float fadeDuration = 0.6f;

    private Vector3 targetScale;
    private float bornAt;
    private bool grown;
    private Material fadeMaterial;
    private Color baseColor;
    private int colorId;
    private bool fading;
    private bool fadeUnavailable;

    // Start다 - 생성 측이 AddComponent 전에 localScale을 최종 크기로 대입하므로 Start 시점에는
    // 그 값을 targetScale로 붙잡을 수 있다(FallingRockShard와 같은 순서 이유).
    private void Start()
    {
        targetScale = transform.localScale;
        bornAt = Time.time;

        if (growDuration > 0f) transform.localScale = targetScale * MinScale;
        else grown = true;
    }

    private void Update()
    {
        float age = Time.time - bornAt;

        // 커지는 동안만 스케일을 만진다 - 다 자란 뒤에도 매 프레임 대입하면 콜라이더가 계속
        // 다시 구워진다(예전엔 enabled=false로 컴포넌트를 껐지만 이제 페이드가 남아 있어 못 끈다).
        if (!grown)
        {
            float t = age / growDuration;
            if (t >= 1f) { transform.localScale = targetScale; grown = true; }
            else transform.localScale = targetScale * Mathf.Max(MinScale, t);
        }

        if (lifetime <= 0f) return;

        float remaining = lifetime - age;
        if (remaining <= 0f) { Destroy(gameObject); return; }

        float fade = Mathf.Min(fadeDuration, lifetime);
        if (fade <= 0f || remaining > fade) return;

        // 투명 전환에 실패한 파편(렌더러·색 프로퍼티 없음)은 예전처럼 수명 끝에 그냥 사라진다 —
        // 침묵 실패보다는 낫고, 여기서 예외를 던져 파편 하나가 영원히 남는 것보다도 낫다.
        if (!fading && !fadeUnavailable) { fading = BeginFade(); fadeUnavailable = !fading; }
        if (fading) SetAlpha(remaining / fade);
    }

    /// <summary>렌더러의 머티리얼 <b>인스턴스</b>를 알파 블렌드로 전환한다. 상단 주석의
    /// "블렌드 드리프트를 피하는 방법" 참고 — sharedMaterial을 건드리면 안 된다.</summary>
    private bool BeginFade()
    {
        Renderer fragmentRenderer = GetComponent<Renderer>();
        if (fragmentRenderer == null) return false;

        // .material 접근이 인스턴스를 만든다. 이 GameObject와 함께 파괴되므로 별도 해제가 없다.
        fadeMaterial = fragmentRenderer.material;
        if (fadeMaterial == null) return false;

        if (fadeMaterial.HasProperty(ColorId)) colorId = ColorId;
        else if (fadeMaterial.HasProperty(BaseColorId)) colorId = BaseColorId;
        else return false;
        baseColor = fadeMaterial.GetColor(colorId);

        // Built-in Standard의 Fade(_Mode 2) 구성. 이 프로젝트는 Built-in + Standard다.
        // ponytail: URP/HDRP로 옮기면 _Surface/_Blend와 _SURFACE_TYPE_TRANSPARENT 키워드를
        // 여기에 함께 세팅해야 한다(HasProperty 가드 덕에 지금은 조용히 팝으로 폴백한다).
        if (fadeMaterial.HasProperty("_Mode")) fadeMaterial.SetFloat("_Mode", 2f);
        if (fadeMaterial.HasProperty("_SrcBlend")) fadeMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (fadeMaterial.HasProperty("_DstBlend")) fadeMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        if (fadeMaterial.HasProperty("_ZWrite")) fadeMaterial.SetInt("_ZWrite", 0);
        fadeMaterial.EnableKeyword("_ALPHABLEND_ON");
        fadeMaterial.DisableKeyword("_ALPHATEST_ON");
        fadeMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        fadeMaterial.renderQueue = (int)RenderQueue.Transparent;
        return true;
    }

    private void SetAlpha(float alpha)
    {
        Color c = baseColor;
        c.a = Mathf.Clamp01(alpha);
        fadeMaterial.SetColor(colorId, c);
    }
}
