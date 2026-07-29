using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 구름 트램펄린 — 위에서 착지/점프한 플레이어를 목표 높이(baseBounceHeight)까지 튀어올린다.
/// 발사는 신규 로직 없이 PlayerSystem의 PlayerJump.LaunchToHeight(H)에 위임한다(질량 무관 결정론).
///
/// 무게 과부하 붕괴(협동 축):
/// 구름 위 도형들의 합산 "무게"(Rigidbody.mass × 그 바디의 실효 중력 배율 — 에셋 실제 질량은
/// 구1.5/세모1.0/네모3.0)로 동작이 갈린다. 질량이 아니라 무게인 이유는 TotalLoad() 주석 참고.
/// - load &lt; restMassThreshold                         : 가벼워서 튕겨 오름(트램펄린).
/// - restMassThreshold ≤ load &lt; collapseMassThreshold : 무거워 못 튕기고 눌러앉음(구름이 버팀).
/// - load ≥ collapseMassThreshold                       : 과부하 — 구름이 서서히 사라져 콜라이더가
///   풀리며 위 도형이 낙하하고, reappearDelaySec 뒤 다시 서서히 나타난다.
///
/// 기본값(rest 3.0 / collapse 3.5)은 PRD §1.3 "네모는 혼자만 탑승, 구·세모는 함께" 규칙을 그대로
/// 물리로 실현한다: 네모(3.0) 단독은 눌러앉아 버티지만, 네모+다른 도형(≥4.0)이면 붕괴한다.
/// (네모 단독을 다시 튕기게 하려면 restMassThreshold를 collapseMassThreshold와 같게 올리면 된다 —
///  그럼 붕괴는 두 도형이 동시에 닿는 순간에만 성립한다.)
///
/// "위에서 착지" 판정·발사 위임은 JumpSystem/JumpPad 패턴을, 알파 페이드+콜라이더 타이밍은
/// RainbowBridgeSystem 패턴을 이식했다(두 원본 모두 수정하지 않음). 도형별 부스트(2단계)는 TBD.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CloudTrampoline : MonoBehaviour
{
    [Header("Bounce")]
    [Tooltip("플레이어를 튀어올릴 목표 높이 H(Unit). PlayerJump가 velocity.y=√(2gH)로 역산 대입하므로 " +
             "도형 질량과 무관하게 정확히 이 높이까지 오른다.")]
    public float baseBounceHeight = 6f;

    [Header("Overload Collapse (무게 과부하 붕괴)")]
    [Tooltip("구름 위 합산 질량이 이 값 이상이면 튕기지 않고 눌러앉는다(구름이 버팀). " +
             "세트 B에서 네모(3.0) 단독이 눌러앉는 기준.")]
    public float restMassThreshold = 3.0f;

    [Tooltip("구름 위 합산 질량이 이 값 이상이면 과부하로 구름이 붕괴한다. " +
             "세트 B에서 네모+다른 도형(≥4.0) 기준.")]
    public float collapseMassThreshold = 3.5f;

    [Tooltip("붕괴 후 다시 나타나기까지 숨어 있는 시간(초).")]
    public float reappearDelaySec = 5f;

    [Tooltip("사라짐/나타남 알파 페이드 시간(초). 0이면 즉시 전환.")]
    public float fadeDuration = 0.45f;

    public string playerTag = "Player";

    private enum State { Active, Collapsing, Hidden, Reappearing }
    private State state = State.Active;
    private float reappearTimer = 0f;

    // 구름 위에 있는 플레이어 Rigidbody들(합산 질량 계산용).
    private readonly HashSet<Rigidbody> riders = new HashSet<Rigidbody>();

    // 페이드 대상: 구름 시각(자식 puff 렌더러들). 지지/도약 콜라이더는 루트에 있는 supportCollider.
    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Color> baseColors = new List<Color>();
    private MaterialPropertyBlock mpb;
    private Collider supportCollider;
    private float alpha = 1f;
    private float lastAppliedAlpha = -1f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in Standard
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP/HDRP Lit

    private void Reset()
    {
        // 인스펙터에서 수동 부착 시 기본 판 규격을 세팅(구름 시각 생성은 Tools 메뉴의 역할이라 여기서 안 함).
        BoxCollider box = GetComponent<Collider>() as BoxCollider;
        if (box != null)
        {
            box.isTrigger = false;
            box.size = new Vector3(3f, 1f, 2f);
        }
    }

    private void Start()
    {
        supportCollider = GetComponent<Collider>();
        mpb = new MaterialPropertyBlock();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            renderers.Add(r);
            baseColors.Add(r.sharedMaterial != null ? r.sharedMaterial.color : Color.white);
        }
        alpha = 1f;
        ApplyVisual();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (state != State.Active) return;
        if (!collision.gameObject.CompareTag(playerTag)) return;
        if (!IsTopLanding(collision)) return;

        Rigidbody rb = collision.gameObject.GetComponentInParent<Rigidbody>();
        if (rb != null) riders.Add(rb);

        float load = TotalLoad();
        if (load >= collapseMassThreshold)
        {
            BeginCollapse();
            return;
        }
        if (load >= restMassThreshold)
            return; // 눌러앉음: 튕기지 않고 구름이 버틴다.

        // 가벼움: 튕겨 오름(질량 무관 결정론 도약).
        PlayerJump jump = collision.gameObject.GetComponentInParent<PlayerJump>();
        if (jump != null) jump.LaunchToHeight(baseBounceHeight);
    }

    private void OnCollisionExit(Collision collision)
    {
        Rigidbody rb = collision.gameObject.GetComponentInParent<Rigidbody>();
        if (rb != null) riders.Remove(rb);
    }

    /// <summary>구름이 견디는 하중. 질량이 아니라 "무게"를 잰다 — 무중력 버블처럼 개별 중력을 낮추는
    /// 구역 안에서는 같은 도형도 가벼워져야 하기 때문이다. 덕분에 네모(3.0)가 버블 안(×0.60)에서 1.8이
    /// 되어 눌러앉기 밴드 아래로 내려가 튕긴다 — 도형별 특수 분기 없이 물리로 성립하고, 버블 밖에서는
    /// 배율이 1이라 기존 동작 그대로다.
    /// 기준식은 PlayerSystem의 `PlayerWeight.Of` 하나뿐이다(저장소 공통 규칙, `Assets/CLAUDE.md`).</summary>
    private float TotalLoad()
    {
        riders.RemoveWhere(r => r == null);
        float sum = 0f;
        foreach (Rigidbody r in riders) sum += PlayerWeight.Of(r);
        return sum;
    }

    /// <summary>판 윗면 착지(위에서 부딪힘)인지 판정한다. 접점 법선이 아래로 강하게 향하면
    /// (normal.y ≤ -0.5) 위에서 내려온 착지로 본다. 측면(normal.y≈0) 스침은 발사/집계에서 제외해
    /// 옆으로 스치며 점프할 때의 오발사를 막는다(평평한 박스 윗면이라 이 엄격 판정이 정확하다).</summary>
    private bool IsTopLanding(Collision collision)
    {
        foreach (ContactPoint c in collision.contacts)
            if (c.normal.y <= -0.5f) return true;
        return false;
    }

    private void BeginCollapse()
    {
        state = State.Collapsing;
        riders.Clear(); // 붕괴 후엔 위 도형이 낙하하므로 집계 초기화.
    }

    private void Update()
    {
        float target = (state == State.Collapsing || state == State.Hidden) ? 0f : 1f;

        if (fadeDuration <= 0f) alpha = target;
        else alpha = Mathf.MoveTowards(alpha, target, Time.deltaTime / fadeDuration);

        if (!Mathf.Approximately(alpha, lastAppliedAlpha))
        {
            ApplyVisual();
            lastAppliedAlpha = alpha;
        }

        switch (state)
        {
            case State.Collapsing:
                // 완전히 사라지는 순간 콜라이더 해제 → 위 도형이 낙하한다.
                supportCollider.enabled = alpha > 0.001f;
                if (alpha <= 0.001f)
                {
                    state = State.Hidden;
                    reappearTimer = reappearDelaySec;
                }
                break;

            case State.Hidden:
                reappearTimer -= Time.deltaTime;
                if (reappearTimer <= 0f)
                {
                    supportCollider.enabled = true; // 나타나기 시작하면 즉시 밟히도록.
                    state = State.Reappearing;
                }
                break;

            case State.Reappearing:
                if (alpha >= 0.999f) state = State.Active;
                break;
        }
    }

    private void ApplyVisual()
    {
        bool visible = alpha > 0.001f;
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;

            r.enabled = visible;
            if (!visible) continue;

            Color c = baseColors[i];
            c.a = alpha;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(ColorId, c);
            mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }

    // 선택 시 도약 도달 높이를 씬 뷰에 표시해 레벨 배치를 돕는다.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 top = transform.position + Vector3.up * 0.5f;
        Gizmos.DrawLine(top, top + Vector3.up * baseBounceHeight);
        Gizmos.DrawWireSphere(top + Vector3.up * baseBounceHeight, 0.2f);
    }
}
