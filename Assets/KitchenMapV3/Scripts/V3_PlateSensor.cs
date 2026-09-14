using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// P5 뒷마당 GOAL 판정 전용 감지 트리거 — 기믹배선_계획_2026-09-06.md §1(R2) B-2.
///
/// [왜 필요한가]
/// V3_GoalGate가 "판 3개가 거의 동시에 눌렸는가"를 판정하려면 각 판의 현재 눌림 상태와 마지막
/// 눌림 시각을 읽어야 한다. 그런데:
///  · 판 A는 저장소 사본 `ExitWeightPlate`(무게 2.75 임계)를 그대로 쓰는데, 그 컴포넌트의
///    `isOpen`은 private라 외부에서 읽을 수 없다(저장소 코드 무수정 원칙상 public으로 못 고친다).
///  · 판 B·C는 "실 게이트(3.0) 너머 도달"이 조건이라 무게 임계가 아니라 "도형 존재만"으로
///    판정해야 하는데, ExitWeightPlate는 무게 임계 판정만 한다.
/// 그래서 판 셋 모두에 이 컴포넌트를 형제로 붙여 별도로 판정한다. 무게 계산은 **절대 새로
/// 유도하지 않고** PlayerSystem의 `PlayerWeight.Of` 하나만 호출한다(식 복제 금지 — 배선 지시
/// 원문). 플레이어가 트리거+솔리드 두 콜라이더를 가져 Enter/Exit가 여러 번 불리므로 바디별로
/// 겹친 콜라이더를 모아 센다(ExitWeightPlate·PadTrigger의 바디별 카운트 관례와 같은 목적).
///
/// [K07 정정, 2026-09-12 — Codex 검수 실측: play-results.txt LIFECYCLE]
/// 이전 버전은 바디별 "콜라이더 개수"만 세고 PruneDead가 파괴(null)된 바디만 지웠다. 그런데 판 위의
/// 플레이어를 SetActive(false)하면 Unity는 OnTriggerExit를 보내지 않고(실측) Rigidbody도 파괴되지
/// 않아 딕셔너리에 남았다 — Pressed가 true로 고정되고 LastPressedTime이 계속 현재 시각으로 갱신됐다
/// (비활성 몸이 GOAL의 "최근 눌림"으로 계속 집계되는 오개방 위험). 이제 바디별로 "겹친 콜라이더
/// 집합"을 들고, 판정 때마다 살아 있는 콜라이더(파괴 안 됨·enabled·GameObject 활성·바디 활성)만
/// 인정하고 죽은 항목은 집합에서 뺀다. 비활성이었다가 다시 켜진 몸은 자동으로 복원하지 않는다 —
/// PhysX가 다시 OnTriggerEnter를 보내야만 겹침으로 센다(활성화만으로 없는 접촉을 만들지 않는다).
/// 센서 자신이 꺼지면(OnDisable) 집합을 비우고 조명 오버라이드를 지운다 — 다시 켜질 때도
/// 실제 Enter가 와야 복원된다. GOAL의 "마지막 눌림 기준 3초 창" 규칙과 LastPressedTime 갱신
/// 조건(Pressed인 프레임에만 갱신)은 그대로다.
///
/// [K02 정정, 2026-09-12 — Codex 검수 실측: PLATE_RENDERER B/C present=False]
/// 조명 대상 렌더러 참조(cachedRenderer)가 직렬화되지 않았고 Awake가 ConfigureRenderer(null)로
/// 자기/자식 탐색만 했다 — 판 B·C 센서는 렌더러 없는 트리거 GameObject(V3_Gimmicks.FootprintTrigger)
/// 라 빌드 시 명시 전달한 판 렌더러가 저장·재로드·Play 진입 때마다 사라졌다. 이제 명시 참조를
/// [SerializeField] explicitRenderer에 저장하고 Awake는 그 참조를 우선한다(없을 때만 자기/자식 탐색).
///
/// [Q2 반려, map-reviewer 22차 — 🔒H2 P5 "위치에 갇히는 채널: 발밑 진동·자기 판 조명"]
/// 이 채널 중 "자기 판 조명"만 구현한다 — 눌려 있는 동안 **이 컴포넌트가 붙은 그 판 하나만**
/// 렌더러 프로퍼티 블록의 밝기/emission을 올리고, 떠나면 원래 값으로 되돌린다. 다른 판·개수
/// 표시·게이트 상태와는 완전히 무관하고 소리도 안 낸다 — 🔒H2 "0 아니면 1"과 충돌하지 않는다.
/// **"발밑 진동"은 미구현이다(코드 없음)** — 이 프로젝트 Assets 전수 grep 결과 컨트롤러 진동 API
/// 없음(확인). "무게판 0.1U 침강" 채널을 촉각 대용으로 쓸지는 컨트롤타워 상신 대기 중이다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class V3_PlateSensor : MonoBehaviour
{
    [Tooltip("0이면 '플레이어 몸이 하나라도 겹쳐 있는가'만 본다(판 B·C — 실 게이트 너머 도달 " +
             "판정, 도형 종류 무관). 0보다 크면 겹친 몸들의 PlayerWeight.Of 합이 이 값 이상이어야 " +
             "눌린 것으로 본다(판 A — 2.75, ExitWeightPlate와 같은 임계를 공유).")]
    public float requiredWeight = 0f;

    public string playerTag = "Player";

    /// <summary>[r3 재보완 2026-09-14 — 검증 사본 전용, 제품 미반영] "실제 접촉 지지" 판정(Codex R1/R2).
    /// 이전(09-14 후속) 레이캐스트 판정은 몸 하단+0.05에서 0.20 아래까지 보므로 판보다 최대 0.15U 위에 뜬 몸도 "밟음"으로
    /// 셌고(R1: 새 래치 성공 로그의 하단 0.53 vs 상면 0.40), 적중한 물체가 이 판의 지지체인지 확인하지 않았다(R2). 이제
    /// Physics.ContactEvent(Unity 2022.2+)로 매 물리 스텝의 실제 접촉점을 받아, 겹친 몸이 "이 판의 지지체(supportCollider)와
    /// 지지 방향(|normal.y|>0.5) 접촉"을 갖는 물리 스텝에만 밟은 것으로 센다. 공중(접촉 없음)에서는 Pressed=false·LastPressedTime
    /// 비갱신. 지지체가 지정되지 않은 옛 배선은 기하 폴백(접촉점이 트리거 발자국 안·판 상면 ±0.03, 플레이어 아님).
    /// 정적 토글은 검증 하네스의 전(OFF)/후(ON) 대조 전용 — 제품안에는 두지 않는다. 접촉 보고는 지지체 콜라이더의
    /// providesContacts를 켜서 받는다(우리 판 박스에만 설정 — 팀 오브젝트는 건드리지 않음).</summary>
    public static bool RequireBodyAboveTop = true;

    [Tooltip("[r3 재보완] 이 판을 실제로 떠받치는 콜라이더(판 박스). 배선(V3_Gimmicks)이 지정. 비어 있으면 기하 폴백.")]
    public Collider supportCollider;

    [Tooltip("[r3 재보완] 기하 폴백용: 접촉점 높이가 판 상면 ± 이 값이면 이 판의 지지 접촉으로 본다(U).")]
    public float contactHeightTolerance = 0.03f;

    [Tooltip("[r3 재보완 2] 접촉 분리(separation) 상한(U): PhysX는 접촉 오프셋(기본 0.01) 안의 '떠 있는' 쌍도 보고하므로, 이 값 이하(정지 접촉 잔차 ~0.001, 착지 순간 음수)만 실제 접촉으로 본다. 명세: 0.003U.")]
    public float contactSeparationMax = 0.003f;

    /// <summary>판 상면 = 이 트리거 콜라이더의 하단(FitOnTop/FootprintTrigger가 상면+두께/2에 둔다).</summary>
    public float PlateTopY { get { Collider c = GetComponent<Collider>(); return c != null ? c.bounds.min.y : transform.position.y; } }

    /// <summary>[검증용] 마지막 판정의 몸별 진단(이름{근거}=P/-).</summary>
    public string LastDiag { get; private set; } = "-";

    /// <summary>[검증용] 겹친 몸별 "이름:솔리드하단y:인정 여부"(간단형).</summary>
    public string DebugBodies()
    {
        PruneDead();
        var sb = new System.Text.StringBuilder();
        foreach (KeyValuePair<Rigidbody, HashSet<Collider>> kv in overlaps)
        {
            string diag; bool on = BodyOnTop(kv.Key, out diag);
            sb.Append(kv.Key.name).Append(':').Append(UnionMinY(kv.Key).ToString("F2")).Append(':').Append(on ? "T" : "F").Append(' ');
        }
        return sb.Length == 0 ? "-" : sb.ToString().TrimEnd();
    }

    /// <summary>[검증용] 겹친 몸별 상세 진단(콜라이더·자세·질량·지지 접촉 목록).</summary>
    public string DebugBodiesDetail()
    {
        PruneDead();
        var sb = new System.Text.StringBuilder();
        foreach (KeyValuePair<Rigidbody, HashSet<Collider>> kv in overlaps)
        {
            string diag; bool on = BodyOnTop(kv.Key, out diag);
            sb.Append('[').Append(kv.Key.name).Append(" pos=").Append(kv.Key.position.ToString("F2")).Append(" rot=").Append(kv.Key.rotation.eulerAngles.ToString("F0")).Append(" mass=").Append(kv.Key.mass.ToString("F2")).Append(" solidMinY=").Append(UnionMinY(kv.Key).ToString("F3"));
            sb.Append(" overlappingCols=");
            foreach (Collider c in kv.Value) if (c != null) sb.Append(c.name).Append(':').Append(c.GetType().Name).Append(":trig=").Append(c.isTrigger).Append(';');
            sb.Append(" ").Append(diag).Append(" => ").Append(on ? "PRESS" : "no").Append("] ");
        }
        return sb.Length == 0 ? "-" : sb.ToString().TrimEnd();
    }

    /// <summary>몸(rb)에 직접 붙은 활성 솔리드 콜라이더들의 통합 하단 y(진단용). 자식 Rigidbody의 콜라이더는 제외(R2).</summary>
    private static float UnionMinY(Rigidbody rb)
    {
        float m = float.MaxValue; int n = 0;
        foreach (Collider c in rb.GetComponentsInChildren<Collider>())
        {
            if (c == null || c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy || c.attachedRigidbody != rb) continue;
            m = Mathf.Min(m, c.bounds.min.y); n++;
        }
        return n == 0 ? float.NaN : m;
    }

    private bool BodyOnTop(Rigidbody rb, out string why)
    {
        why = "";
        if (!RequireBodyAboveTop) { why = "toggle OFF(legacy: any overlap)"; return true; }
        V3_ContactRegistry.Ensure();
        Collider trig = GetComponent<Collider>();
        Bounds tb = trig != null ? trig.bounds : new Bounds(transform.position, Vector3.one);
        float top = tb.min.y;
        // [재보완 2] 신선도: 이 몸의 접촉이 마지막 물리 스텝에 보고됐거나(스탬프 일치), 몸이 잠들어 있어(움직일 수 없음) 마지막 보고 상태가 유효할 때만 본다.
        float stamp = V3_ContactRegistry.StampOf(rb);
        bool fresh = stamp >= Time.fixedTime - Time.fixedDeltaTime * 0.5f;
        bool sleeping = rb.IsSleeping();
        if (!fresh && !sleeping) { why = "stale contacts (last report " + (Time.fixedTime - stamp).ToString("F3") + "s ago, body awake) solidMinY=" + UnionMinY(rb).ToString("F3") + " top=" + top.ToString("F2"); return false; }
        IReadOnlyList<V3_ContactRegistry.Support> sups = V3_ContactRegistry.Of(rb);
        var seen = new System.Text.StringBuilder();
        for (int i = 0; i < sups.Count; i++)
        {
            V3_ContactRegistry.Support sp = sups[i];
            if (sp.other == null) continue;
            string tag = sp.other.name + "@" + sp.point.y.ToString("F3") + "/sep" + sp.separation.ToString("F4");
            if (sp.other.CompareTag(playerTag)) { seen.Append(tag).Append("(player);"); continue; }   // 다른 플레이어 위는 이 판의 지지가 아님
            if (sp.separation > contactSeparationMax) { seen.Append(tag).Append("(gap>").Append(contactSeparationMax.ToString("F3")).Append(");"); continue; }   // 접촉 오프셋 안에서 떠 있는 쌍 제외
            bool identity = supportCollider != null && sp.other == supportCollider;
            bool geom = supportCollider == null && Mathf.Abs(sp.point.y - top) <= contactHeightTolerance
                        && sp.point.x >= tb.min.x - 0.05f && sp.point.x <= tb.max.x + 0.05f && sp.point.z >= tb.min.z - 0.05f && sp.point.z <= tb.max.z + 0.05f;
            if (identity || geom)
            {
                why = "contact=" + tag + (identity ? "(support)" : "(geom)") + (sleeping ? "(sleeping,last)" : "") + " solidMinY=" + UnionMinY(rb).ToString("F3");
                return true;
            }
            seen.Append(tag).Append(identity ? "" : "(not support);");
        }
        why = "no plate contact (contacts=" + sups.Count + (seen.Length > 0 ? " " + seen : "") + (sleeping ? " sleeping" : "") + ") solidMinY=" + UnionMinY(rb).ToString("F3") + " top=" + top.ToString("F2") + " step=" + V3_ContactRegistry.Step;
        return false;
    }

    [Tooltip("[K02] 자기 판 조명 대상 렌더러(명시). V3_Gimmicks가 판 B·C처럼 렌더러 없는 센서에 실제로 " +
             "보이는 판의 Renderer를 넣는다. 비어 있으면 자기/자식에서 찾는다(판 A).")]
    [SerializeField] private Renderer explicitRenderer;

    /// <summary>지금 눌려 있는가.</summary>
    public bool Pressed { get; private set; }

    /// <summary>마지막으로 Pressed가 true였던 Time.time. 한 번도 눌린 적 없으면 매우 음수.</summary>
    public float LastPressedTime { get; private set; } = -9999f;

    /// <summary>[K02 검증용] 조명에 쓰는 렌더러(명시 우선). 없으면 null — 조명 생략(정상).</summary>
    public Renderer LitRenderer => cachedRenderer;

    /// <summary>[K07 검증용] 지금 살아 있는 겹침 콜라이더 총수(죽은 항목 정리 후).</summary>
    public int LiveOverlapCount { get { PruneDead(); int n = 0; foreach (var kv in overlaps) n += kv.Value.Count; return n; } }

    // [K07] 바디 → 그 바디의 "지금 겹쳐 있다고 통지된" 콜라이더 집합.
    private readonly Dictionary<Rigidbody, HashSet<Collider>> overlaps = new Dictionary<Rigidbody, HashSet<Collider>>();

    // ── 자기 판 조명 (Q2) ────────────────────────────────────────────────────────────
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // 폴백(Standard 등)
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private const float LitColorMultiplier = 2.2f; // 임의 연출값 — 수치 검증 대상 아님(질감 담당 몫).

    // [정정 2026-09-10] `rend.material` 인스턴스 getter는 에디트 모드에서 "Instantiating material"
    // 에러를 내고 머티리얼을 씬에 누수시킨다 — MaterialPropertyBlock으로 값만 오버라이드한다.
    // 베이스값은 항상 `rend.sharedMaterial`에서만 읽는다(공유 에셋 읽기만, 복제 없음).
    private Renderer cachedRenderer;
    private MaterialPropertyBlock mpb;
    private bool hasBaseColorProp, hasColorProp, hasEmissionProp;
    private Color baseColorValue = Color.white;
    private bool wasLit;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void Awake()
    {
        ConfigureRenderer(explicitRenderer);   // [K02] 직렬화된 명시 참조 우선.
        // [r3 재보완] 접촉 이벤트 등록부 구독 + 이 판의 지지체 콜라이더가 접촉을 보고하도록 설정(우리 판 박스에만).
        V3_ContactRegistry.Ensure();
        if (supportCollider != null) supportCollider.providesContacts = true;
    }

    void OnDisable()
    {
        // [K07] 꺼진 동안엔 Exit가 오지 않으므로 겹침 기록을 버린다 — 다시 켜지면 PhysX가 실제로
        // 겹친 콜라이더에 OnTriggerEnter를 다시 보내야만 복원된다. [K02] 조명 오버라이드도 지운다
        // (잔광 방지). LastPressedTime(이력)은 지우지 않는다 — GOAL 3초 창의 "마지막 눌림" 사실이다.
        overlaps.Clear();
        Pressed = false;
        if (wasLit) SetLit(false);
    }

    /// <summary>
    /// 자기 판 조명(Q2)이 쓸 렌더러를 정한다. explicitRenderer가 주어지면 [SerializeField] 필드에
    /// 저장해 저장·재로드·Play 진입 뒤에도 유지한다(K02). 없으면 기존처럼 자기/자식을 탐색한다
    /// (판 A처럼 이 GameObject 자체가 보이는 판). 멱등 — 매번 baseColor를 새로 읽고 프로퍼티
    /// 블록을 빈 상태로 되돌린다.
    /// </summary>
    public void ConfigureRenderer(Renderer explicitRendererArg)
    {
        if (explicitRendererArg != null) explicitRenderer = explicitRendererArg;
        Renderer rend = explicitRenderer;
        if (rend == null) rend = GetComponent<Renderer>();
        if (rend == null) rend = GetComponentInChildren<Renderer>();
        if (rend == null) { cachedRenderer = null; return; } // 렌더러를 끝내 못 찾음 — 조명 연출 조용히 생략(정상 구성).

        cachedRenderer = rend;
        if (mpb == null) mpb = new MaterialPropertyBlock();

        Material shared = rend.sharedMaterial; // 읽기 전용 접근 — 인스턴스화 없음.
        hasBaseColorProp = shared != null && shared.HasProperty(BaseColorId);
        hasColorProp = shared != null && shared.HasProperty(ColorId);
        hasEmissionProp = shared != null && shared.HasProperty(EmissionColorId);
        if (hasBaseColorProp) baseColorValue = shared.GetColor(BaseColorId);
        else if (hasColorProp) baseColorValue = shared.GetColor(ColorId);

        wasLit = false;
        mpb.Clear();
        rend.SetPropertyBlock(mpb);
    }

    private void SetLit(bool lit)
    {
        wasLit = lit;
        if (cachedRenderer == null) return; // 렌더러 없는 판 — 연출 없음, 정상.
        if (mpb == null) mpb = new MaterialPropertyBlock();

        mpb.Clear();
        if (lit)
        {
            if (hasBaseColorProp) mpb.SetColor(BaseColorId, Brighten(baseColorValue));
            else if (hasColorProp) mpb.SetColor(ColorId, Brighten(baseColorValue));
            // [37차 R2 반영] 베이스가 흰색(KV3_accent)이면 Brighten만으론 화면 변화 0이라 에미션을 주
            // 채널로 삼는다 — 키워드(_EMISSION)는 V3_Materials가 accent 재질에 미리 켜 두고, 값만 MPB.
            if (hasEmissionProp) mpb.SetColor(EmissionColorId, baseColorValue * (LitColorMultiplier - 1f));
        }
        // lit=false면 빈 블록 — 렌더러가 다시 sharedMaterial 원래 값을 그대로 쓴다.
        cachedRenderer.SetPropertyBlock(mpb);
    }

    private static Color Brighten(Color c) => new Color(
        Mathf.Clamp01(c.r * LitColorMultiplier),
        Mathf.Clamp01(c.g * LitColorMultiplier),
        Mathf.Clamp01(c.b * LitColorMultiplier),
        c.a); // 알파는 그대로 — Color*float 내장 연산자는 알파까지 스케일해 투명해질 수 있어 직접 계산한다.

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.attachedRigidbody != null ? other.attachedRigidbody : other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;
        if (!overlaps.TryGetValue(rb, out HashSet<Collider> set))
        {
            set = new HashSet<Collider>();
            overlaps[rb] = set;
        }
        set.Add(other);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.attachedRigidbody != null ? other.attachedRigidbody : other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;
        if (!overlaps.TryGetValue(rb, out HashSet<Collider> set)) return;
        set.Remove(other);
        if (set.Count == 0) overlaps.Remove(rb);
    }

    void Update()
    {
        if (supportCollider != null && !supportCollider.providesContacts) supportCollider.providesContacts = true;   // [재보완 2] Awake 이후 지정된 지지체도 접촉 보고
        bool now = requiredWeight > 0f ? TotalWeight() >= requiredWeight : AnyBodyPresent();
        Pressed = now;
        if (now) LastPressedTime = Time.time;
        // 의도적으로 로그·소리 0(🔒H2 P5 "0 아니면 1" — 개수·판별 신호를 새지 않는다). 조명은 예외
        // (Q2) — 자기 판 하나만 밝힌다, 다른 판·게이트·개수와 무관.
        if (now != wasLit) SetLit(now);
    }

    private bool AnyBodyPresent()
    {
        PruneDead();
        var diag = new System.Text.StringBuilder();
        bool result = false;
        foreach (KeyValuePair<Rigidbody, HashSet<Collider>> kv in overlaps)
        {
            string why; bool on = BodyOnTop(kv.Key, out why);   // [r3 후속] 실제 지지 판정
            diag.Append(kv.Key.name).Append('{').Append(why).Append('}').Append(on ? "=P " : "=- ");
            if (on) result = true;
        }
        LastDiag = diag.Length == 0 ? "-" : diag.ToString().TrimEnd();
        return result;
    }

    private float TotalWeight()
    {
        PruneDead();
        float sum = 0f;
        var diag = new System.Text.StringBuilder();
        foreach (KeyValuePair<Rigidbody, HashSet<Collider>> kv in overlaps)
        {
            string why; bool on = BodyOnTop(kv.Key, out why);   // [r3 후속] 실제 지지 판정
            diag.Append(kv.Key.name).Append('{').Append(why).Append('}').Append(on ? "=P " : "=- ");
            if (on) sum += PlayerWeight.Of(kv.Key); // 식 복제 금지 — PlayerSystem의 창구 하나만 호출.
        }
        LastDiag = diag.Length == 0 ? "-" : diag.ToString().TrimEnd();
        return sum;
    }

    /// <summary>[K07] 살아 있는 콜라이더만 남긴다: 파괴 안 됨 · Collider.enabled · GameObject 활성 ·
    /// 바디 GameObject 활성. 집합이 비면 바디도 뺀다.</summary>
    private void PruneDead()
    {
        List<Rigidbody> deadBodies = null;
        List<Collider> deadCols = null;
        foreach (KeyValuePair<Rigidbody, HashSet<Collider>> kv in overlaps)
        {
            Rigidbody rb = kv.Key;
            bool bodyAlive = rb != null && rb.gameObject.activeInHierarchy;
            if (bodyAlive)
            {
                deadCols?.Clear();
                foreach (Collider c in kv.Value)
                    if (c == null || !c.enabled || !c.gameObject.activeInHierarchy)
                        (deadCols ??= new List<Collider>()).Add(c);
                if (deadCols != null) foreach (Collider c in deadCols) kv.Value.Remove(c);
            }
            if (!bodyAlive || kv.Value.Count == 0)
                (deadBodies ??= new List<Rigidbody>()).Add(rb);
        }
        if (deadBodies != null) foreach (Rigidbody r in deadBodies) overlaps.Remove(r);
    }
}

/// <summary>[r3 재보완 2026-09-14 — 사본 전용] 접촉 사실 등록부. Physics.ContactEvent로 매 물리 스텝의 접촉점을 받아
/// 몸(Rigidbody)별 "지지 방향(|normal.y|>0.5) 접촉" 목록을 보관한다. 판정은 V3_PlateSensor가 한다. 접촉은 쌍의 어느 한쪽이
/// Collider.providesContacts=true일 때 보고된다(센서가 지지체 판 박스에 설정).</summary>
public static class V3_ContactRegistry
{
    public struct Support { public Collider mine; public Collider other; public Vector3 point; public Vector3 normal; public float separation; }
    static readonly Dictionary<int, List<Support>> byBody = new Dictionary<int, List<Support>>();
    static readonly Dictionary<int, float> stampByBody = new Dictionary<int, float>();   // 몸별 마지막 보고 물리 시각(Time.fixedTime)
    static readonly List<Support> empty = new List<Support>();
    static bool subscribed;
    public static int Step { get; private set; }
    public static int LastPairs { get; private set; }

    public static void Ensure()
    {
        if (subscribed) return;
        Physics.ContactEvent += OnContact;
        subscribed = true;
    }

    static void OnContact(PhysicsScene scene, Unity.Collections.NativeArray<ContactPairHeader>.ReadOnly headers)
    {
        Step++;
        // [재보완 2] 전역 clear 대신 이 스텝에 보고된 몸만 갱신(스탬프) — 보고가 없는 몸은 오래된 것으로 판정되거나(깨어 있음) 잠든 상태면 마지막 보고 유지.
        int pairs = 0;
        for (int i = 0; i < headers.Length; i++)
        {
            ContactPairHeader h = headers[i];
            for (int j = 0; j < h.PairCount; j++)
            {
                ContactPair p = h.GetContactPair(j);
                Collider a = p.Collider, b = p.OtherCollider;
                if (a == null || b == null) continue;
                pairs++;
                for (int k = 0; k < p.ContactCount; k++)
                {
                    ContactPairPoint cp = p.GetContactPoint(k);
                    if (Mathf.Abs(cp.Normal.y) < 0.5f) continue;
                    Add(a.attachedRigidbody, a, b, cp);
                    Add(b.attachedRigidbody, b, a, cp);
                }
            }
        }
        LastPairs = pairs;
    }

    static void Add(Rigidbody rb, Collider mine, Collider other, ContactPairPoint cp)
    {
        if (rb == null) return;
        int id = rb.GetInstanceID();
        float now = Time.fixedTime;
        if (!byBody.TryGetValue(id, out List<Support> l)) { l = new List<Support>(); byBody[id] = l; }
        if (!stampByBody.TryGetValue(id, out float st) || st != now) { l.Clear(); stampByBody[id] = now; }
        l.Add(new Support { mine = mine, other = other, point = cp.Position, normal = cp.Normal, separation = cp.Separation });
    }

    /// <summary>몸의 마지막 접촉 보고 물리 시각(없으면 -9999).</summary>
    public static float StampOf(Rigidbody rb) => rb != null && stampByBody.TryGetValue(rb.GetInstanceID(), out float st) ? st : -9999f;

    public static IReadOnlyList<Support> Of(Rigidbody rb)
    {
        if (rb == null) return empty;
        return byBody.TryGetValue(rb.GetInstanceID(), out List<Support> l) ? l : empty;
    }
}
