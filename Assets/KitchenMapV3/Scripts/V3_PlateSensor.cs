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
/// 원문). 겹침 집계 방식(바디별 카운트)은 ExitWeightPlate·PadTrigger와 같은 폴더 관례를 그대로
/// 따른다 — 플레이어가 트리거+솔리드 두 콜라이더를 가져 Enter/Exit가 여러 번 불리기 때문이다.
///
/// [Q2 반려, map-reviewer 22차 — 🔒H2 P5 "위치에 갇히는 채널: 발밑 진동·자기 판 조명"]
/// 이 채널 중 "자기 판 조명"만 구현한다 — 눌려 있는 동안 **이 컴포넌트가 붙은 그 판 하나만**
/// 렌더러 머티리얼 인스턴스의 밝기/emission을 올리고, 떠나면 원래 값으로 되돌린다. 다른 판·개수
/// 표시·게이트 상태와는 완전히 무관(이 GameObject 하나의 상태만 본다)하고 소리도 안 낸다 — 🔒H2
/// "0 아니면 1"(판별·개수 신호를 새지 않는다)과 충돌하지 않는다 — "그 판을 지금 밟고 있다"는
/// 사실 자체는 신호가 아니라 그 판의 물리 상태를 보여주는 표시다. 판 B·C처럼 렌더러가 없는
/// (순수 트리거) 판은 밝힐 대상이 없어 조용히 생략된다 — 에러 아님, 정상.
/// **"발밑 진동"은 미구현이다(코드 없음)** — 이 프로젝트 Assets 전수 grep 결과
/// SetMotorSpeeds·Gamepad.current·Rumble류 컨트롤러 진동 API 없음(확인, 추측 아님). / 대안으로
/// 🔒H2 P5 §A-6가 빌린 "무게판 0.1U 침강" 물리 채널(사본 DoorSystem/PadTrigger.padPressDepth 필드
/// 기본값 0.1f — 판이 눌리면 그 깊이만큼 실제로 가라앉는 물리 이동)을 촉각 대용으로
/// 쓸지는 컨트롤타워 상신 대기 중이다(map-reviewer 24차 A2) — 채택 여부·구현 방식 모두 판정
/// 전이라 이번 회차는 손대지 않았다. 이번 회차 구현 범위는 조명(자기 판 밝기/emission) 채널
/// 하나뿐이다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class V3_PlateSensor : MonoBehaviour
{
    [Tooltip("0이면 '플레이어 몸이 하나라도 겹쳐 있는가'만 본다(판 B·C — 실 게이트 너머 도달 " +
             "판정, 도형 종류 무관). 0보다 크면 겹친 몸들의 PlayerWeight.Of 합이 이 값 이상이어야 " +
             "눌린 것으로 본다(판 A — 2.75, ExitWeightPlate와 같은 임계를 공유).")]
    public float requiredWeight = 0f;

    public string playerTag = "Player";

    /// <summary>지금 눌려 있는가.</summary>
    public bool Pressed { get; private set; }

    /// <summary>마지막으로 Pressed가 true였던 Time.time. 한 번도 눌린 적 없으면 매우 음수.</summary>
    public float LastPressedTime { get; private set; } = -9999f;

    // 바디별 겹침 콜라이더 수 — ExitWeightPlate.cs의 같은 패턴(overlaps 딕셔너리) 재사용.
    private readonly Dictionary<Rigidbody, int> overlaps = new Dictionary<Rigidbody, int>();

    // ── 자기 판 조명 (Q2) ────────────────────────────────────────────────────────────
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // 폴백(Standard 등)
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private const float LitColorMultiplier = 2.2f; // 임의 연출값 — 수치 검증 대상 아님(질감 담당 몫).
    // [삭제 2026-09-10, R2 복구] 구 상수 LitEmissionColor(고정 주황빛)는 에미션 채널을
    // "베이스색×(2.2−1)"로 재정의하며 미사용이 됐다 — dead code(컴파일 경고) 방지로 제거.

    // [정정 2026-09-10, 컨트롤타워 지시 — 관문실행_2026-09-10_0631.txt 로그 :91~92
    // "Instantiating material due to calling renderer.material during edit mode" 에러 2줄 제거]
    // 예전엔 `rend.material`(인스턴스 getter)로 판마다 머티리얼 사본을 만들어 색을 직접 고쳤다 —
    // 이 getter 자체가 에디트 모드에서 위 에러를 내고 머티리얼을 씬에 누수시킨다(원인, 주석
    // 그대로 인용됐던 문서화된 동작). MaterialPropertyBlock으로 전환한다 — 인스턴스를 만들지
    // 않고 렌더러별로 셰이더 프로퍼티 "값"만 오버라이드한다(Unity 2022.3 API:
    // `Renderer.GetPropertyBlock(MaterialPropertyBlock)`/`SetPropertyBlock(MaterialPropertyBlock)`).
    // 베이스값은 항상 `rend.sharedMaterial`에서만 읽는다 — sharedMaterial의 GetColor/HasProperty는
    // 공유 에셋을 읽기만 할 뿐 복제하지 않는다(인스턴스화를 유발하는 것은 `.material` getter뿐).
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
        ConfigureRenderer(null);
    }

    /// <summary>
    /// [A1 반영, map-reviewer 24차] 자기 판 조명(Q2)이 쓸 렌더러를 정한다. explicitRenderer가
    /// 있으면 그것을 그대로 쓴다 — 판 B·C는 FootprintTrigger가 만드는 GameObject(콜라이더만,
    /// 렌더러 없음)에 이 컴포넌트가 붙어 Awake()의 자기/자식 탐색이 항상 실패했다(수정 전 버그 —
    /// 판 A만 조명이 걸리고 판 B·C는 조용히 생략되던 원인). V3_Gimmicks.WirePresenceSensor가
    /// 실제로 보이는 판 오브젝트(footprintSource, "Orange" V3.Box)의 Renderer를 여기로 명시
    /// 전달해 3판 전부 조명이 동작하게 한다 — 밝히는 대상은 여전히 "그 판 자신"뿐이다(🔒H2 P5
    /// ①″ "자기 자신만" 유지, 다른 판·개수 표시와는 계속 무관). explicitRenderer가 없으면(판
    /// A처럼 이 GameObject 자체가 이미 보이는 판) 기존과 동일하게 자기/자식을 탐색한다. AddComponent
    /// 직후 Awake가 즉시 동기 실행되므로, 이 메서드를 외부에서 다시 불러 캐시를 덮어써도 안전하다
    /// (멱등 — 매번 baseColor를 새로 읽어 저장할 뿐, 렌더러 프로퍼티 블록도 빈 상태로 재설정한다).
    /// </summary>
    public void ConfigureRenderer(Renderer explicitRenderer)
    {
        Renderer rend = explicitRenderer;
        if (rend == null) rend = GetComponent<Renderer>();
        if (rend == null) rend = GetComponentInChildren<Renderer>();
        if (rend == null) { cachedRenderer = null; return; } // 렌더러를 끝내 못 찾음 — 조명 연출 조용히 생략(경고 없음, 정상 구성).

        cachedRenderer = rend;
        if (mpb == null) mpb = new MaterialPropertyBlock();

        Material shared = rend.sharedMaterial; // 읽기 전용 접근 — 인스턴스화 없음.
        hasBaseColorProp = shared != null && shared.HasProperty(BaseColorId);
        hasColorProp = shared != null && shared.HasProperty(ColorId);
        hasEmissionProp = shared != null && shared.HasProperty(EmissionColorId);
        if (hasBaseColorProp) baseColorValue = shared.GetColor(BaseColorId);
        else if (hasColorProp) baseColorValue = shared.GetColor(ColorId);

        // 이 메서드가 재호출(멱등)될 수 있으므로 프로퍼티 블록을 항상 빈 상태로 되돌려 둔다 —
        // 이전에 켜져 있던 밝기 오버라이드가 새 baseColor 캐시와 어긋난 채 남지 않게 한다.
        wasLit = false;
        mpb.Clear();
        rend.SetPropertyBlock(mpb);
    }

    private void SetLit(bool lit)
    {
        wasLit = lit;
        if (cachedRenderer == null) return; // 렌더러 없는 판 — 연출 없음, 정상.

        mpb.Clear();
        if (lit)
        {
            if (hasBaseColorProp) mpb.SetColor(BaseColorId, Brighten(baseColorValue));
            else if (hasColorProp) mpb.SetColor(ColorId, Brighten(baseColorValue));
            // [해소 2026-09-10, map-reviewer 37차 R2 반려 반영] 위 두 줄(_BaseColor/_Color)만으로는
            // 이 판들의 베이스가 흰색(KV3_accent._BaseColor=(1,1,1,1))이라 Brighten이 Clamp01(1×2.2)
            // =(1,1,1)로 베이스와 똑같아져 화면상 변화가 0이었다(37차 R2 실측 — 규격 통과이나 설계
            // 회귀). 그래서 에미션을 주 채널로 삼는다 — 베이스색×(2.2−1)만큼 발광을 더해 밝기 차를
            // 낸다. MPB는 값만 오버라이드하고 키워드(_EMISSION)는 못 켜므로, 그 키워드는
            // V3_Materials.cs BuildMaterials가 accent 재질 생성/재대입 시 미리 켜 둔다(키워드는
            // 재질에, 값은 MPB에 — 역할 분리). accent 재질을 안 받는 판(있다면)은 이 값을 넣어도
            // 키워드가 꺼져 있어 발광하지 않는다 — 이번 수정 범위 밖의 한계로 남긴다.
            if (hasEmissionProp) mpb.SetColor(EmissionColorId, baseColorValue * (LitColorMultiplier - 1f));
        }
        // lit=false면 빈 블록 그대로 둔다 — 렌더러가 다시 sharedMaterial 원래 값을 그대로 쓴다
        // (원래 밝기 "복원"은 오버라이드를 지우는 것과 같다, 별도 baseEmission 캐시 불필요).
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
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;
        overlaps.TryGetValue(rb, out int n);
        overlaps[rb] = n + 1;
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;
        if (!overlaps.TryGetValue(rb, out int n)) return;
        if (n <= 1) overlaps.Remove(rb);
        else overlaps[rb] = n - 1;
    }

    void Update()
    {
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
        return overlaps.Count > 0;
    }

    private float TotalWeight()
    {
        PruneDead();
        float sum = 0f;
        foreach (KeyValuePair<Rigidbody, int> kv in overlaps)
            sum += PlayerWeight.Of(kv.Key); // 식 복제 금지 — PlayerSystem의 창구 하나만 호출.
        return sum;
    }

    private void PruneDead()
    {
        List<Rigidbody> dead = null;
        foreach (KeyValuePair<Rigidbody, int> kv in overlaps)
            if (kv.Key == null) (dead ??= new List<Rigidbody>()).Add(kv.Key);
        if (dead != null) foreach (Rigidbody r in dead) overlaps.Remove(r);
    }
}
