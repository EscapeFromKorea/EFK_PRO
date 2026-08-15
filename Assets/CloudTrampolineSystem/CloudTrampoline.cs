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
/// 왕복 이동(선택):
/// pointA/pointB를 둘 다 꽂으면 구름이 두 지점을 movePeriodSec 주기로 왕복한다(코사인 이징 — 양 끝에서
/// 감속·반전). 비워두면 기존 고정 구름 그대로다. 위에 얹힌 도형은 position 가산으로 함께 실려 간다.
/// ponytail: 경로에 서 있는 도형은 키네마틱 스윕에 옆으로 밀린다(doorPhysics의 isBlocked 같은 끼임 방지
/// 없음). 양 끝에 플레이어가 서 있을 수 있는 레벨을 만들 때 필요하면 그때 추가.
/// ponytail: 승객 운반은 position 대입이라 보간(Interpolate) 기준 포즈를 매 스텝 갱신한다 — 플레이테스트에서
/// 판 위 도형이 떠는 지터가 보이면 구름 쪽 interpolation을 None으로 맞춰 표현을 일치시킨다(미확인).
///
/// 연속 도약 부스트(2단계):
/// 같은 구름에서 이어 튕길 때마다 목표 높이가 heightBoostPerStep씩 오른다(기본 6 → 8 → 10 → 12,
/// maxBoostSteps에서 천장). 단수는 <b>바디별·판별로</b> 따로 쌓여, 구와 세모가 서로의 진척을 나눠 갖지
/// 않고 판 두 개를 오가며 쌓을 수도 없다(BounceHeightFor 주석). <b>이 판이 아닌 곳에 착지하면 누적이
/// 사라진다</b>(DropBoostOfBodiesLandedElsewhere) — 콤보는 한 판에서 이어 튕기는 동안만 유지된다.
/// 무거운 도형은 눌러앉기 밴드에서 걸려
/// 발사 경로에 오지 않으므로 "구·세모만 부스트를 쌓는다"가 Kind 분기 없이 성립한다.
///
/// "위에서 착지" 판정·발사 위임은 JumpSystem/JumpPad 패턴을, 알파 페이드+콜라이더 타이밍은
/// RainbowBridgeSystem 패턴을 이식했다(두 원본 모두 수정하지 않음).
/// </summary>
[RequireComponent(typeof(Collider))]
public class CloudTrampoline : MonoBehaviour
{
    [Header("Bounce")]
    [Tooltip("플레이어를 튀어올릴 목표 높이 H(Unit). PlayerJump가 velocity.y=√(2gH)로 역산 대입하므로 " +
             "도형 질량과 무관하게 정확히 이 높이까지 오른다.")]
    public float baseBounceHeight = 6f;

    [Header("Overload Collapse (무게 과부하 붕괴)")]
    [Tooltip("구름 위 합산 무게가 이 값 이상이면 튕기지 않고 눌러앉는다(구름이 버팀). " +
             "실제 에셋 질량(구1.5/세모1.0/네모3.0)에서 네모 단독이 눌러앉는 기준.")]
    public float restMassThreshold = 3.0f;

    [Tooltip("구름 위 합산 무게가 이 값 이상이면 과부하로 구름이 붕괴한다. " +
             "실제 에셋 질량에서 네모+다른 도형(≥4.0) 기준 — 구+세모(2.5)는 둘 다 튕긴다.")]
    public float collapseMassThreshold = 3.5f;

    [Tooltip("붕괴 후 다시 나타나기까지 숨어 있는 시간(초).")]
    public float reappearDelaySec = 5f;

    [Header("Bounce Boost (2단계 — 연속 도약 부스트)")]
    [Tooltip("같은 구름에서 연속으로 튕길 때마다 도약 높이에 더할 값(Unit). 기본 2면 6 → 8 → 10 → 12.")]
    public float heightBoostPerStep = 2f;

    [Tooltip("부스트 최대 단수. 3이면 baseBounceHeight + 3 × heightBoostPerStep이 천장이다(기본 12 U). " +
             "0으로 두면 부스트가 꺼지고 1단계와 똑같이 항상 baseBounceHeight로만 튕긴다.")]
    public int maxBoostSteps = 3;

    [Tooltip("사라짐/나타남 알파 페이드 시간(초). 0이면 즉시 전환.")]
    public float fadeDuration = 0.45f;

    [Header("Movement (선택 — 두 지점 왕복)")]
    [Tooltip("왕복 구간의 한쪽 끝. pointA/pointB 둘 다 지정되면 구름이 두 지점을 주기적으로 왕복한다. " +
             "비워두면 고정 구름(기존 동작). 씬에서 마커를 집어 옮기면 경로가 바뀐다.")]
    public Transform pointA;

    [Tooltip("왕복 구간의 반대쪽 끝.")]
    public Transform pointB;

    [Tooltip("A→B→A 왕복 한 바퀴에 걸리는 시간(초). 작을수록 빠르다. 이 값이 곧 이동 속도 노브다 — " +
             "양 끝에서는 코사인 곡선으로 자동 감속·반전하므로 승객이 튕겨나가지 않는다.")]
    public float movePeriodSec = 4f;

    public string playerTag = "Player";

    private enum State { Active, Collapsing, Hidden, Reappearing }
    private State state = State.Active;
    private float reappearTimer = 0f;

    // 구름 위에 있는 플레이어 Rigidbody들(합산 질량 계산용).
    private readonly HashSet<Rigidbody> riders = new HashSet<Rigidbody>();

    // 바디별 부스트 단수. 도형별로 따로 쌓이는 것은 이 딕셔너리가 바디 단위라 자동으로 성립한다
    // (Kind 분기 없음 - 저장소 규칙). 이 판의 필드이므로 부스트는 판마다 독립이다.
    private readonly Dictionary<Rigidbody, int> boostSteps = new Dictionary<Rigidbody, int>();

    // 바디가 "마지막으로 튕긴 판". 판을 옮겨 다니며 쌓는 것을 막으려면 자기 판 정보만으로는 알 수 없어
    // (A에서 튕기고 B를 거쳐 A로 돌아와도 A 입장에서는 그냥 두 번째 튕김이다) 판 전체가 공유한다.
    // ponytail: static 딕셔너리라 파괴된 바디 키가 남는다. 플레이어 바디는 3개 고정이라 프루닝을 넣지
    // 않았다 - 런타임에 생멸하는 바디가 이 판을 밟게 되면 그때 SlowZone식 주기 프루닝을 붙인다.
    private static readonly Dictionary<Rigidbody, CloudTrampoline> lastBounced =
        new Dictionary<Rigidbody, CloudTrampoline>();

    // 딕셔너리를 순회하며 지울 수 없어 쓰는 임시 버퍼(매 스텝 할당하지 않으려고 필드로 둔다).
    private readonly List<Rigidbody> boostCleanup = new List<Rigidbody>();

    // "발밑에 뭐가 있나" 확인 거리(무게중심 → 바닥). PlayerGroundContact의 SphereCast 거리(0.6)에
    // 도형 반 높이와 커지기 패드로 부푼 경우까지 얹은 여유값이다. 접지가 잡힌 바디에만 쏘므로
    // 넉넉해도 비용이 없고(플레이어 3개), 가장 가까운 히트만 쓰므로 길어서 틀릴 일도 없다.
    private const float GroundProbeDistance = 3f;

    // 페이드 대상: 구름 시각(자식 puff 렌더러들). 지지/도약 콜라이더는 루트에 있는 supportCollider.
    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Color> baseColors = new List<Color>();
    private MaterialPropertyBlock mpb;
    private Collider supportCollider;
    private float alpha = 1f;
    private float lastAppliedAlpha = -1f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in Standard
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP/HDRP Lit

    // 왕복 이동용. moving이 false면 아래 필드는 전부 쓰이지 않고 FixedUpdate가 즉시 빠진다.
    private bool moving;
    private Rigidbody body;
    private float moveElapsed;

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

        moving = pointA != null && pointB != null && movePeriodSec > 0.01f;
        if (moving)
        {
            body = GetComponent<Rigidbody>();
            if (body == null)
            {
                // 키네마틱 Rigidbody 없이 transform만 옮기면 PhysX가 스윕하지 않아 위에 선 도형과
                // 파고들었다 튕긴다(ThreadBridge 주석과 같은 이유). 조용히 대체하지 않고 알린다.
                Debug.LogWarning($"[CloudTrampoline] {name}: 왕복 이동에는 키네마틱 Rigidbody가 필요합니다. " +
                                 "Tools > CloudTrampoline > Create Cloud Trampoline (Moving)로 생성하세요. 이동을 끕니다.", this);
                moving = false;
            }
            else
            {
                // 씬에 놓인 현재 위치에 해당하는 위상에서 출발한다. A로 스냅해 버리면 "구름 자식만 원하는
                // 자리로 옮겨 배치했는데 Play 하면 순간이동한다"가 되고, 마커가 빈 오브젝트라 씬 뷰에서
                // 원인이 눈에 안 보인다(판 위에 미리 얹어 둔 도형 배치도 함께 깨진다).
                // u = 0.5 - 0.5cos(2πt/T)의 역함수: t = T·acos(1-2u)/2π.
                Vector3 span = pointB.position - pointA.position;
                float u0 = span.sqrMagnitude > 0.0001f
                    ? Mathf.Clamp01(Vector3.Dot(body.position - pointA.position, span) / span.sqrMagnitude)
                    : 0f;
                moveElapsed = movePeriodSec * Mathf.Acos(1f - 2f * u0) / (2f * Mathf.PI);
            }
        }
    }

    /// <summary>두 지점 왕복 + 위에 얹힌 도형 운반. pointA/pointB가 비어 있으면 아무 일도 하지 않는다.</summary>
    private void FixedUpdate()
    {
        DropBoostOfBodiesLandedElsewhere();

        // 숨어 있는 동안(콜라이더 off)은 제자리에 멈춘다. 계속 움직이면 "5초 뒤 돌아온다"가 "5초 뒤 다른
        // 자리에 돌아온다"가 되어 떨어진 플레이어가 기다릴 자리가 사라지고, 지형 안에서 콜라이더가 켜져
        // 디페네트레이션으로 튕길 수도 있다. 시간(moveElapsed)도 함께 멈추므로 같은 위상에서 재개한다.
        if (!moving || state == State.Hidden) return;

        moveElapsed += Time.fixedDeltaTime;

        // 코사인 이징: elapsed=0 → A, period/2 → B, period → A(왕복 한 바퀴). 양 끝에서 속도가 0이 되어
        // 반전이 부드럽고, 승객에게 급가속이 걸리지 않는다.
        float u = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * moveElapsed / movePeriodSec);
        Vector3 next = Vector3.Lerp(pointA.position, pointB.position, u);
        Vector3 delta = next - body.position;

        body.MovePosition(next);

        // 위에 얹힌 도형을 같은 만큼 평행이동시켜 함께 실어 나른다. velocity가 아니라 position에 더하는
        // 이유는 PlayerMover가 매 FixedUpdate 수평 velocity를 입력값으로 통째 대입하기 때문이다 — velocity로
        // 주면 그 스텝에 지워진다. position 가산은 이동 입력·점프·도약 속도를 하나도 건드리지 않고(수직도
        // 그대로) 좌표만 옮기므로, 구름 위에서 계속 점프하는 중에도 운반이 성립한다.
        // 실려 가는 건 사실상 "눌러앉은" 무거운 도형이다 — 가벼운 도형은 닿는 즉시 튕겨 올라 접촉이 끊긴다.
        // 덕분에 네모가 판 위에 남아 뒤에 얹히는 도형과 합산 무게를 이루는 협동 설계가 이동판에서도 유지된다.
        foreach (Rigidbody r in riders)
            if (r != null) r.position += delta;
    }

    /// <summary>부스트를 쌓아둔 바디가 <b>이 판이 아닌 곳에 착지하면</b> 누적을 버린다(2026-08-15
    /// 플레이테스트 반영). 콤보는 "이 판에서 계속 튕기는 동안"만 이어진다.
    ///
    /// 착지 판정은 PlayerSystem의 PlayerGroundContact를 읽는다(읽기 전용 — 하드 룰 준수). 그런데
    /// 그것만으로는 안 된다: 그 컴포넌트는 아래로 SphereCast(기본 0.6 U)를 쏘므로 <b>이 판으로
    /// 되돌아오는 하강 중 접촉 몇 프레임 전에 이미 IsGrounded가 켜진다.</b> 그 순간 riders에는 아직
    /// 안 들어와 있어서, 가드가 없으면 같은 판으로 돌아오는 정상 콤보가 매번 착지 직전에 초기화된다.
    ///
    /// 그래서 접지가 잡힌 바디에 한해 <b>발밑에 실제로 무엇이 있는지</b>를 직접 확인한다 — 그게
    /// 이 판이면 "돌아오는 중"이라 콤보를 유지하고, 다른 것이면 다른 데 착지한 것이라 버린다.
    /// 처음에는 "판 발자국 XZ 안 + 중심보다 위"라는 위치 근사를 썼는데, 구름 <b>위에 다른 발판이
    /// 겹쳐</b> 놓인 배치에서는 그 발판에 착지해도 근사가 참이라 콤보가 남았다(2026-08-15 PR 점검).
    /// 레이캐스트는 가장 가까운 것을 돌려주므로 위에 겹친 발판이 이 판을 가린다.
    /// (SphereCast가 아니라 Raycast인 이유: 시작점이 바디 자기 콜라이더 안이라 자기 자신은 자동으로
    /// 제외되고, 겹친 채 시작하는 SphereCast는 거리 0 히트로 쓰레기값을 준다.)
    ///
    /// PlayerGroundContact가 없는 바디(계층 관례를 안 따르는 오브젝트)는 초기화되지 않고 예전처럼
    /// 누적만 이어간다 — 조용히 안 도는 대신 기능이 하나 덜한 쪽으로 열화된다.</summary>
    private void DropBoostOfBodiesLandedElsewhere()
    {
        if (boostSteps.Count == 0) return;

        boostCleanup.Clear();

        foreach (Rigidbody rb in boostSteps.Keys)
        {
            if (rb == null) { boostCleanup.Add(rb); continue; }
            if (riders.Contains(rb)) continue; // 이 판 위에 있다 — 콤보 유지.

            PlayerGroundContact ground = rb.GetComponentInChildren<PlayerGroundContact>();
            if (ground == null || !ground.IsGrounded) continue;

            // 접지가 잡혔다. 발밑이 이 판이면 되돌아오는 중(위 주석), 아니면 다른 데 착지한 것이다.
            if (Physics.Raycast(rb.worldCenterOfMass, Vector3.down, out RaycastHit hit,
                    GroundProbeDistance, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider == supportCollider) continue;

            boostCleanup.Add(rb);
        }

        foreach (Rigidbody rb in boostCleanup) boostSteps.Remove(rb);
    }

    /// <summary>판 위 도형 집계는 Enter가 아니라 Stay로 유지한다. Enter만 쓰면 "접촉이 시작된 순간
    /// Active가 아니었던" 도형이 영구히 미등록으로 남는다 — 붕괴 후 그 자리에 서 있던 도형은 콜라이더가
    /// 다시 켜지는 순간(Reappearing) 접촉하는데, 그 뒤로는 새 Enter 이벤트가 없어서 Active로 돌아가도
    /// 집계에 안 들어간다. 고정 구름에서는 "그 도형만 안 튕긴다" 정도로 눈에 안 띄었지만, 왕복판에서는
    /// 운반 목록에서도 빠져 판이 발밑에서 빠져나가 떨어진다. Stay는 멱등하므로(HashSet) 매 프레임 갱신해도
    /// 안전하고, 붕괴 페이드(Collapsing) 중에도 운반이 유지돼 승객이 서 있던 자리에서 지지를 잃는다.</summary>
    private void OnCollisionStay(Collision collision)
    {
        if (!collision.gameObject.CompareTag(playerTag)) return;
        if (!IsTopLanding(collision)) return;

        Rigidbody rb = collision.gameObject.GetComponentInParent<Rigidbody>();
        if (rb != null) riders.Add(rb);
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

        // 가벼움: 튕겨 오름(질량 무관 결정론 도약). 같은 판에서 연속으로 튕기면 단수만큼 높아진다.
        PlayerJump jump = collision.gameObject.GetComponentInParent<PlayerJump>();
        if (jump == null) return;
        jump.LaunchToHeight(rb != null ? BounceHeightFor(rb) : baseBounceHeight);
    }

    /// <summary>이번 튕김의 목표 높이를 구하고 그 바디의 부스트 단수를 한 단 올린다.
    /// 첫 튕김은 baseBounceHeight 그대로이고, 같은 판에서 이어 튕길수록 heightBoostPerStep씩 오른다
    /// (maxBoostSteps에서 천장). 무거운 도형은 애초에 눌러앉기 밴드에서 걸려 여기 오지 않으므로,
    /// "구·세모만 부스트를 쌓는다"는 Kind 분기 없이 무게 판정만으로 성립한다.
    ///
    /// 콤보는 <b>이 판에서 쉬지 않고 튕기는 동안</b>만 이어진다. 끊는 경로가 둘이다:
    /// 다른 판에서 튕기고 오면 여기서 이전 누적을 버리고(판 두 개를 오가며 쌓는 우회 차단),
    /// 이 판이 아닌 곳에 착지하면 DropBoostOfBodiesLandedElsewhere가 버린다.</summary>
    private float BounceHeightFor(Rigidbody rb)
    {
        if (lastBounced.TryGetValue(rb, out CloudTrampoline prev) && prev != this)
            boostSteps.Remove(rb);
        lastBounced[rb] = this;

        int step = boostSteps.TryGetValue(rb, out int s) ? s : 0;
        boostSteps[rb] = Mathf.Min(step + 1, Mathf.Max(0, maxBoostSteps));
        return baseBounceHeight + heightBoostPerStep * step;
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
        // 같은 접촉으로 붕괴가 재발동하지 않게 집계를 비운다. 페이드 중에도 도형이 판 위에 남아 있으면
        // OnCollisionStay가 다시 채우는데(운반이 유지되도록 의도한 것), 그동안 state가 Active가 아니라
        // OnCollisionEnter의 판정에는 도달하지 않는다.
        riders.Clear();

        // 무너진 구름은 쌓아둔 부스트도 잃는다. 과부하로 무너뜨려 놓고 복귀 후 높은 도약을 이어받는 건
        // "무겁게 태우면 벌을 받는다"는 붕괴 규칙과 반대로 간다.
        boostSteps.Clear();
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

    // 왕복 경로를 항상 표시한다(선택 시가 아니라 상시). 지점을 조정할 때는 마커를 선택하므로 구름이
    // 선택 해제되는데, 그때 경로가 안 보이면 어디로 옮기는지 모른 채 드래그하게 된다.
    private void OnDrawGizmos()
    {
        if (pointA == null || pointB == null) return;
        // 끝점은 판 규격 박스가 아니라 작은 구로 찍는다 — 박스를 그리면 루트를 회전시키거나 콜라이더
        // 크기를 바꿨을 때 미리보기 발자국이 실제와 어긋나 "이 틈에 들어간다"는 잘못된 확신을 준다.
        Gizmos.color = new Color(0.6f, 0.85f, 1f);
        Gizmos.DrawLine(pointA.position, pointB.position);
        Gizmos.DrawWireSphere(pointA.position, 0.35f);
        Gizmos.DrawWireSphere(pointB.position, 0.35f);
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
