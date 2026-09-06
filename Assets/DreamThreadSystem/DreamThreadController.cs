using UnityEngine;

/// <summary>
/// 꿈의 실타래(Dream Thread) Phase 1 — 단일 앵커 스윙의 백엔드. 씬에 하나만 둔다.
///
/// [무엇을 하나]
/// F/마우스 휠/좌우 입력을 읽어, 지금 조작 중인 플레이어(Player 태그 + PlayerMover.IsControlled)를
/// 가장 가까운 ThreadAnchor에 "고정 길이 강체 진자"로 매단다. 좌우 입력으로 접선 방향 힘을 줘
/// 진폭을 키우고, 다시 F를 누르면 실을 끊어 그 순간의 접선 속도 그대로 날아간다. 실 길이는
/// 마우스 휠로 조절한다(최대 maxLength). 실은 LineRenderer로 그린다.
///
/// [왜 씬 컨트롤러가 전담하나 — 교차 폴더 하드룰 회피]
/// 매달림은 결국 플레이어 Root의 Rigidbody에 조인트를 붙였다 떼는 일이다. 플레이어에 상시
/// 리시버 컴포넌트를 심으려면 PlayerSystem의 생성 메뉴를 고쳐야 하고, 이는 교차 폴더 하드룰에
/// 걸린다. 그래서 입력·조인트 부착을 전부 이 컨트롤러가 런타임에 처리한다 — PlayerSystem은
/// IsControlled/Kind를 "읽기만" 하고 mover의 `ExternallyDriven` 플래그만 런타임에 세운다.
///
/// [왜 ConfigurableJoint인가 (HingeJoint/SpringJoint 아님)]
/// - SpringJoint는 탄력이라 채택 안 함(1차 설계 확정). HingeJoint는 고정 길이 진자엔 맞지만
///   회전축에 몸이 핀으로 박혀 "길이"를 런타임에 바꾸기 어렵다. Phase 1은 마우스 휠 가변 길이가
///   범위 안이라 가변 길이 조인트가 필요하다 → ConfigurableJoint를 쓴다.
/// - x/y/z 선형 모션을 모두 Limited(같은 linearLimit)로 두면 구속 영역이 "앵커 중심 반지름 L의 구"가
///   된다. 이 구는 회전 대칭이라 자유회전하는 구(Sphere)가 몸 프레임을 아무리 돌려도 길이 구속이
///   깨지지 않는다(축 하나를 Locked로 잠그면 몸 프레임이 함께 돌아 구에서 평면 구속이 무너진다).
///   중력이 몸을 구의 바닥으로 당겨 진자가 된다. 리밋은 "넘지 못하지만 안으로는 느슨해질 수 있는"
///   로프형 구속이라 실의 느낌과 정확히 맞는다. 길이 변경은 linearLimit.limit만 갱신하면 된다.
/// - 스윙을 Y-Z 평면(옆모습)으로 가두는 평면 구속은 조인트 축이 아니라 Rigidbody의
///   FreezePositionX(월드 공간)로 준다 — 위와 같은 이유로 조인트 축은 몸과 함께 돌기 때문이다.
///   연결 시 플레이어 X를 앵커 X에 맞춰 같은 평면에 올린 뒤 X를 얼린다.
///
/// [왜 매달림 중 PlayerMover의 간섭을 멈추나 — 컴포넌트를 끄지 않고 플래그로]
/// PlayerMover는 조작 중 매 FixedUpdate에 수평 velocity를 하드 대입(공중 0.6배)하고, 비조작 중엔
/// 감쇠시킨다. 둘 다 진자 스윙을 매 스텝 짓밟으므로 멈춰야 한다. 예전에는 `mover.enabled=false`로
/// 껐는데, 그러면 OnDisable이 PlayerControlSwitcher 로스터에서 이 플레이어를 빼 조작권이 튀고
/// **Tab으로 매달린 플레이어에게 돌아갈 수 없어**, 매달림 중 Tab을 감지해 실을 강제로 끊어야 했다.
/// 지금은 `mover.ExternallyDriven = true`로 간섭만 멈춘다 → 로스터가 유지돼 **매달린 채 Tab으로
/// 오갈 수 있고**, 실은 그대로 붙어 있다. 조작 대상이 아닌 동안에는 이 컨트롤러가 입력을 읽지 않아
/// 새로 조작하는 플레이어와 같은 키에 동시 반응하지 않는다.
/// 되돌리는 시점은 여전히 "착지할 때까지" 미룬다 — 공중에서 되돌리면 mover가 접선 발사 속도를
/// 입력값으로 덮어써 발사 자체가 사라지기 때문이다(아래 Launching).
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class DreamThreadController : MonoBehaviour
{
    [Header("실 길이 (마우스 휠 조절)")]
    [Tooltip("실의 최대 길이(Unit). PRD 진자 수치표가 실 8을 기준으로 한다.")]
    public float maxLength = 8f;
    [Tooltip("실의 최소 길이(Unit).")]
    public float minLength = 1.5f;
    [Tooltip("마우스 휠 한 눈금(스크롤 델타 1.0 기준)당 바뀌는 '목표' 길이(Unit). 실제 델타는 보통 0.1이라 " +
             "이 값의 약 1/10씩 조절된다. 위로 굴리면 짧아진다(끌어올림).")]
    public float wheelSensitivity = 10f;
    [Tooltip("실 길이가 목표 길이로 따라붙는 속도(Unit/s). 줄일 때 하드 로프 리밋을 한 번에 확 당기면 " +
             "임펄스가 뚝뚝 끊기므로, 리밋을 이 속도로 서서히 이동시켜 스냅을 분산한다(늘릴 때도 동일 적용). " +
             "크게 하면 반응이 빠르지만 다시 끊기고, 작게 하면 더 부드럽지만 느리다.")]
    public float reelSpeed = 4f;

    [Header("스윙 펌핑 (좌우 입력 → 접선 힘)")]
    [Tooltip("좌우 입력을 접선 방향으로 주는 가속(m/s^2, ForceMode.Acceleration이라 질량 무관 — 구·세모 동일 취급). " +
             "매 FixedUpdate 지속 인가되므로 값이 크면 몇 프레임 만에 진폭이 과하게 커진다(플레이테스트: 12는 너무 셈). " +
             "타이밍 맞춰 펌핑하면 몇 번 만에 진폭이 커진다. PRD 목표(1회 60°→6.9, 여러 번 90°→9.8~12.6)에 " +
             "맞춰 씬에서 조정하라(현실 물리는 튜닝 필요).")]
    public float pumpAcceleration = 6f;
    [Tooltip("좌우 입력 방향이 의도와 반대로 느껴지면 켠다(옆모습 카메라 방향에 따라 다름).")]
    public bool invertSwing = false;
    [Tooltip("매달린 채 바닥에 닿았을 때 평소처럼 걷는 속도(Unit/s). 접선 펌핑은 지면 그립 마찰에 짓밟혀 " +
             "안 움직이므로, 접지 중엔 FreezePositionX(평면 고정)를 풀고 PlayerMover와 같은 규약" +
             "(inputYawOffset 회전 + 전후좌우 2D 입력)으로 velocity를 직접 대입해 걷는다(마찰 무시). " +
             "공중에선 다시 평면 고정 + 접선 펌핑으로 스윙한다.")]
    public float groundMoveSpeed = 4f;

    [Header("매달림 무게 게이트")]
    [Tooltip("이 '무게'(질량 × 그 바디의 실효 중력 배율) 이상이면 실에 매달릴 수 없다.\n" +
             "에셋 질량이 네모3.0 / 구1.5 / 세모1.0이라 기본값 3.0은 평소엔 네모만 거부한다 " +
             "(기존 Kind 게이트와 결과 동일).\n" +
             "무중력 버블처럼 개별 중력을 낮추는 구역 안에서는 네모도 1.8까지 가벼워져 매달릴 수 있고, " +
             "매달린 채 구역을 벗어나 이 값을 다시 넘으면 실이 끊어진다.")]
    public float hangWeightThreshold = 3.0f;

    [Tooltip("무게가 임계를 넘긴 뒤 실이 버텨 주는 유예 시간(초). 이 시간 동안 실이 점점 뜯기다가 끊어진다.\n" +
             "유예 중 다시 임계 아래로 내려오면(버블 안으로 복귀) 타이머가 초기화되고 실이 원상복구된다.\n" +
             "0이면 임계를 넘는 즉시 끊긴다(뜯김 연출 없음).")]
    public float snapGraceSec = 0.7f;

    [Header("실 뜯김 연출")]
    [Tooltip("뜯길 때 실이 흔들리는 최대 폭(Unit). 0이면 흔들림 없이 굵기·색만 변한다.")]
    public float frayAmplitude = 0.18f;
    [Tooltip("끊어지기 직전(유예 소진 시점)의 실 색. 평소 색에서 이 색으로 서서히 물든다.")]
    public Color frayColor = new Color(1f, 0.35f, 0.3f, 0.5f);

    [Header("발사 후 조작 복구")]
    [Tooltip("놓은 뒤 이 시간(초) 안에 착지하지 못해도 강제로 PlayerMover를 다시 켠다(허공 낙하 안전망). " +
             "PlayerShapeController가 없어 접지 판정을 못 하는 플레이어의 폴백이기도 하다.")]
    public float launchReenableTimeout = 6f;

    [Header("실 시각")]
    [Tooltip("LineRenderer 실 두께.")]
    public float lineWidth = 0.06f;

    private enum ThreadState { Idle, Hanging, Launching }
    private ThreadState state = ThreadState.Idle;

    // 현재 매달렸거나 발사 중인 플레이어. Hanging→Launching 동안 계속 추적한다.
    private PlayerMover activeMover;
    private Rigidbody activeBody;
    private PlayerShapeController activeShape; // 착지 판정용(없을 수 있음 → 타임아웃 폴백)
    private ThreadAnchor anchor;
    private ConfigurableJoint joint;
    private RigidbodyConstraints savedConstraints;
    private float currentLength;   // 현재 조인트 리밋(실제 실 길이). 매 FixedUpdate targetLength로 릴된다.
    private float targetLength;     // 휠이 조절하는 목표 길이. currentLength가 reelSpeed로 여기에 수렴한다.
    private float launchTimer;
    private float overweightTimer; // 무게가 임계를 넘긴 채 흐른 시간. snapGraceSec에 도달하면 실이 끊긴다.
    private Vector3 lastAnchorPos; // 동적 앵커 추적용 캐시 — 위치가 실제로 바뀔 때만 조인트에 다시 쓴다.

    private LineRenderer line;
    private Color baseLineColor;               // 뜯김 연출이 물들이기 전의 원래 실 색(복구 기준).
    private const int FraySegments = 12;       // 뜯길 때 실을 쪼개는 마디 수(정점 13개).

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        baseLineColor = line.startColor;
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.enabled = false;
        if (line.sharedMaterial == null)
        {
            Shader s = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (s != null) line.material = new Material(s) { color = new Color(0.7f, 0.9f, 1f, 1f) };
        }
    }

    void Reset()
    {
        // 인스펙터에서 직접 컴포넌트를 붙였을 때도 실이 그럴듯하게 보이도록 기본값을 잡는다.
        LineRenderer lr = GetComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.widthMultiplier = 0.06f;
        lr.numCapVertices = 2;
        lr.enabled = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (state == ThreadState.Hanging)
            {
                // 매달린 당사자가 조작 대상일 때만 F로 놓는다. 이 게이트가 없으면 A가 매달린 채
                // 파킹돼 있고 B를 조작 중일 때 **B가 누른 F로 A의 실이 끊긴다** — 이 컴포넌트는 씬
                // 싱글턴이라 입력이 누구 것인지 스스로 구분하지 못하기 때문이다. 같은 함수의
                // HandleWheel과 FixedUpdate의 펌핑·접지이동에는 원래 같은 게이트가 있었고, F만
                // 빠져 있었다. 설계 의도는 "실을 강제로 끊는 경우는 대상/고리 소멸과 무게 초과
                // 유예 소진뿐"이다(폴더 CLAUDE.md).
                //
                // 여기서 TryConnect로 흘려보내면 안 된다: BeginHang이 activeMover를 B로 덮어써
                // A는 조인트가 붙고 ExternallyDriven이 true인 채 추적에서 사라진다 = 영구 조작 불능.
                // 매달림은 동시 1명이므로(단일 state/joint) B는 A가 놓을 때까지 기다린다.
                if (activeMover == null || activeMover.IsControlled)
                    Release(intoLaunch: true);
                else
                    Debug.Log("[DreamThread] 다른 플레이어가 매달려 있습니다 — Tab으로 그 플레이어를 " +
                              "조작해 F로 놓으세요(매달림은 동시 1명).");
            }
            else
            {
                TryConnect(); // Idle 또는 Launching 중 재연결 시도(실패해도 상태 유지)
            }
        }

        if (state == ThreadState.Hanging)
        {
            // 대상이나 고리가 사라진 경우에만 강제로 떼어낸다. **Tab으로 조작권이 넘어가도 실은
            // 유지한다** — 매달린 플레이어는 그대로 매달린 채 중력으로 흔들리고, 조작 대상이 아닌
            // 동안만 입력을 안 읽는다(아래 IsControlled 게이트). mover를 끄지 않아 로스터에 남아 있으므로
            // Tab으로 다시 돌아오면 스윙 조작이 그대로 살아난다.
            if (activeMover == null || anchor == null)
            {
                Release(intoLaunch: false);
                return;
            }
            // 무게 초과(버블 이탈 등)가 유예 시간을 다 쓰면 실이 끊어진다. 끊김은 "로프가 뜯겨 나간" 것이라
            // F로 놓은 것과 같이 접선 속도를 보존한 채 날려 보낸다(intoLaunch: true).
            if (UpdateOverweight())
            {
                Debug.Log($"[DreamThread] 무게를 못 버티고 실이 끊어졌습니다 (무게 {PlayerWeight.Of(activeBody):0.##} ≥ {hangWeightThreshold}).");
                Release(intoLaunch: true);
                return;
            }
            if (activeMover.IsControlled) HandleWheel(); // 조작 대상일 때만 휠로 길이 조절
        }
        else if (state == ThreadState.Launching)
        {
            launchTimer -= Time.deltaTime;
            bool landed = activeShape != null && activeShape.IsGrounded();
            if (activeMover == null || landed || launchTimer <= 0f)
                FinishLaunch();
        }
    }

    void FixedUpdate()
    {
        if (state != ThreadState.Hanging || joint == null || activeBody == null || anchor == null) return;

        // 앵커가 움직이는 오브젝트(레일카 등)에 붙어 있으면 물리 구속점도 따라가야 한다. 펌핑 계산과
        // 실 렌더링은 이미 매 프레임 anchor.transform.position을 읽으므로, 정적 앵커는 매번 같은 값을
        // 다시 쓰는 것뿐이라 동작 변화가 없다. 조작 대상이 아니어도(Tab으로 파킹된 상태) 갱신한다 —
        // "실은 그대로 붙어 있고 중력으로 흔들린다"는 기존 동작이 끌림에도 동일하게 적용돼야 한다.
        // 위치가 실제로 안 바뀌었으면 쓰지 않는다 — 압도적 다수인 정적 앵커에서 조인트 프로퍼티를
        // 매 틱 다시 써 PhysX의 웜스타트 솔버 상태를 건드릴 위험을 원천 차단한다(PRD가 이미 경고한
        // "로프형 하드 리밋의 완전 신장 시 미세 지터" 리스크를 동적 앵커 도입으로 넓히지 않기 위함).
        if (anchor.transform.position != lastAnchorPos)
        {
            joint.connectedAnchor = anchor.transform.position;
            lastAnchorPos = anchor.transform.position;
        }

        // 길이 릴은 조작 여부와 무관하게 계속 수렴시킨다(조인트 리밋의 물리적 상태이므로).
        if (!Mathf.Approximately(currentLength, targetLength))
        {
            currentLength = Mathf.MoveTowards(currentLength, targetLength, reelSpeed * Time.fixedDeltaTime);
            joint.linearLimit = new SoftJointLimit { limit = currentLength, bounciness = 0f, contactDistance = 0.02f };
        }

        // 조작 대상이 아니면(Tab으로 다른 플레이어를 조작 중) 여기서 멈춘다 — 실은 그대로 붙어 있고
        // 중력으로 흔들리지만, 입력은 읽지 않는다. 안 그러면 조작 중인 플레이어와 같은 키에 동시 반응한다.
        if (activeMover == null || !activeMover.IsControlled) return;

        // 접지 중: 로프에 매달린 채라도 바닥에선 평소처럼 걸어 이동하게 한다(자리잡기/반동 생성).
        // 접선 펌핑은 지면 그립 마찰에 짓밟혀 안 움직이고, X를 잠그면(FreezePositionX) 앞뒤로 못 움직이며
        // A/D 방향도 카메라와 어긋난다(월드 Z 직결). 그래서 접지 중엔 평면 고정을 풀고, PlayerMover와
        // 같은 규약(inputYawOffset 회전 + 전후좌우 2D)으로 월드 velocity를 직접 대입한다 → 방향이
        // 카메라와 일치하고 앞뒤로도 움직인다. 공중에선 다시 평면(FreezePositionX)으로 잠그고 펌핑한다.
        if (activeShape != null && activeShape.IsGrounded())
        {
            activeBody.constraints = savedConstraints; // 평면 고정 해제 → 앞뒤(X)로도 이동 가능
            Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
            if (move.sqrMagnitude > 1f) move.Normalize();
            move *= groundMoveSpeed;
            float yaw = activeMover != null ? activeMover.inputYawOffset : 0f;
            if (Mathf.Abs(yaw) > 0.0001f)
                move = Quaternion.AngleAxis(yaw, Vector3.up) * move;
            activeBody.velocity = new Vector3(move.x, activeBody.velocity.y, move.z);
            return;
        }

        // 공중: 자유 스윙이 아니면 스윙 평면(Y-Z)으로 다시 잠근다(접지 중 풀었을 수 있으므로 매 프레임 보장).
        bool free = !anchor.lockToSidePlane;
        activeBody.constraints = free
            ? savedConstraints
            : savedConstraints | RigidbodyConstraints.FreezePositionX;

        Vector3 rope = activeBody.position - anchor.transform.position;
        if (rope.sqrMagnitude < 1e-4f) return;
        Vector3 ropeDir = rope.normalized;

        // 접선 방향 펌핑: 실 방향에 수직인 성분으로만 힘을 준다. 직접 각도 조종이 아니라 힘으로 진폭을
        // 키우는 방식이라, 스윙 방향에 맞춰 입력해야(펌핑) 진폭이 는다.
        Vector3 push;
        if (free)
        {
            // 자유 스윙: 전후좌우 2D 입력을 접지 이동과 **같은 규약**(inputYawOffset 회전)으로 월드
            // 방향으로 바꾼 뒤, 실 방향 성분을 빼 접선 성분만 남긴다 → 미는 쪽으로 흔들린다.
            Vector3 wish = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            if (invertSwing) wish = -wish;
            float yaw = activeMover != null ? activeMover.inputYawOffset : 0f;
            if (Mathf.Abs(yaw) > 0.0001f) wish = Quaternion.AngleAxis(yaw, Vector3.up) * wish;
            push = Vector3.ProjectOnPlane(wish, ropeDir);
        }
        else
        {
            Vector3 tangent = Vector3.Cross(Vector3.right, ropeDir); // (0,-z,y): Y-Z 평면 내, ropeDir에 수직
            float input = Input.GetAxis("Horizontal");
            if (invertSwing) input = -input;
            push = tangent * input;
        }
        activeBody.AddForce(push * pumpAcceleration, ForceMode.Acceleration);
    }

    /// <summary>무게가 임계 이상인 채로 흐른 시간을 누적하고, 유예를 다 썼으면 true(끊어야 함)를 준다.
    /// 유예 중 다시 가벼워지면(버블로 복귀) 타이머를 0으로 되돌려 실이 원상복구되게 한다 —
    /// 한 번 넘었다고 확정 끊김이 아니라 "버티는 동안 돌아오면 산다"가 더 조작 여지가 있다.</summary>
    private bool UpdateOverweight()
    {
        if (activeBody == null) return false;

        if (PlayerWeight.Of(activeBody) < hangWeightThreshold)
        {
            overweightTimer = 0f;
            return false;
        }

        overweightTimer += Time.deltaTime;
        return overweightTimer >= snapGraceSec;
    }

    void LateUpdate()
    {
        if (state != ThreadState.Hanging || activeBody == null || anchor == null) return;

        Vector3 top = anchor.transform.position;
        Vector3 bottom = activeBody.position;
        // 0 = 멀쩡함, 1 = 끊기 직전. 유예 시간의 경과 비율이 그대로 뜯김 정도가 된다.
        float strain = snapGraceSec > 0f ? Mathf.Clamp01(overweightTimer / snapGraceSec)
                                         : (overweightTimer > 0f ? 1f : 0f);

        if (strain <= 0f)
        {
            if (line.positionCount != 2) line.positionCount = 2;
            line.SetPosition(0, top);
            line.SetPosition(1, bottom);
            line.widthMultiplier = lineWidth;
            SetLineTint(baseLineColor);
            return;
        }

        DrawFrayedThread(top, bottom, strain);
    }

    /// <summary>무게를 못 버티는 동안의 연출. 실을 여러 마디로 쪼개 실 방향과 수직으로 흔들고, 굵기를
    /// 줄이고, frayColor로 물들인다 — "올이 풀리며 뜯긴다"는 인상. 흔들림은 양 끝(앵커·몸)이 붙어 있고
    /// 가운데가 가장 크게 벌어지는 sin 프로파일이라, 실이 매달린 채 뜯기는 것처럼 보인다.
    /// strain이 유예 경과라 끊기기 직전에 가장 심하게 뜯긴다.</summary>
    private void DrawFrayedThread(Vector3 top, Vector3 bottom, float strain)
    {
        if (line.positionCount != FraySegments + 1) line.positionCount = FraySegments + 1;

        Vector3 rope = bottom - top;
        // 스윙이 Y-Z 평면(옆모습)이라 그 평면 안에서 흔들어야 카메라에 제대로 보인다.
        Vector3 perp = Vector3.Cross(Vector3.right, rope.sqrMagnitude > 1e-6f ? rope.normalized : Vector3.down);
        perp = perp.sqrMagnitude > 1e-6f ? perp.normalized : Vector3.up;

        for (int i = 0; i <= FraySegments; i++)
        {
            float t = (float)i / FraySegments;
            float profile = Mathf.Sin(t * Mathf.PI); // 양 끝 0, 가운데 1
            Vector3 p = Vector3.Lerp(top, bottom, t)
                        + perp * (Random.Range(-1f, 1f) * frayAmplitude * strain * profile);
            line.SetPosition(i, p);
        }

        line.widthMultiplier = lineWidth * (1f - 0.6f * strain); // 뜯길수록 가늘어진다
        SetLineTint(Color.Lerp(baseLineColor, frayColor, strain));
    }

    // 머티리얼 색은 건드리지 않는다(공유 에셋 오염 방지) — LineRenderer의 정점 색으로만 물들인다.
    private void SetLineTint(Color c)
    {
        line.startColor = c;
        line.endColor = c;
    }

    // 매단 채 컨트롤러가 꺼지거나 파괴되면 플레이어를 영구 구속/외부주도 상태로 남기지 않도록 원복한다.
    void OnDisable()
    {
        if (joint != null) Destroy(joint);
        if (activeBody != null) activeBody.constraints = savedConstraints;
        ReturnBodyToMover();
        ClearActive();
        state = ThreadState.Idle;
        if (line != null) line.enabled = false;
    }

    // 휠은 '목표' 길이만 바꾼다. 실제 조인트 리밋은 FixedUpdate에서 reelSpeed로 서서히 따라붙는다
    // — 줄일 때 하드 리밋을 한 번에 확 당겨 생기는 뚝뚝 끊김을 물리 스텝에 걸쳐 분산하기 위함.
    private void HandleWheel()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 1e-4f) return;
        // 위로 굴림(양수) → 짧아진다(끌어올림).
        targetLength = Mathf.Clamp(targetLength - scroll * wheelSensitivity, minLength, maxLength);
    }

    private void TryConnect()
    {
        // Launching 중 재연결이면(특히 그새 Tab으로 조작권이 넘어간 경우) 먼저 이전 발사를 마무리해
        // 이전 플레이어의 mover를 다시 켜 둔다 — 안 그러면 아래 BeginHang이 activeMover를 덮어써
        // 이전 플레이어가 mover 꺼진 채 조작 불능으로 방치된다(소프트락).
        if (state == ThreadState.Launching) FinishLaunch();

        PlayerMover mover = FindControlledPlayer();
        if (mover == null)
        {
            Debug.Log("[DreamThread] 조작 중인 플레이어를 찾지 못했습니다.");
            return;
        }

        Rigidbody body = mover.GetComponent<Rigidbody>();
        if (body == null) return;

        // 게이트는 도형 종류(Kind)가 아니라 "무게"로 판정한다 — 실이 버티는 물리량이 질량이 아니라
        // 무게라, 무중력 버블 안에서는 네모(3.0 → 1.8)도 매달릴 수 있게 하려는 것이다.
        // 버블 밖 무게는 에셋 질량 그대로라 기존 Kind 게이트(네모만 거부)와 결과가 같다.
        float weight = PlayerWeight.Of(body);
        if (weight >= hangWeightThreshold)
        {
            Debug.Log($"[DreamThread] 무거워서 실에 매달릴 수 없습니다 (무게 {weight:0.##} ≥ {hangWeightThreshold}).");
            return;
        }

        ThreadAnchor near = FindNearestAnchorInRange(body.position);
        if (near == null)
        {
            Debug.Log("[DreamThread] 범위 안에 걸 수 있는 앵커가 없습니다.");
            return;
        }

        BeginHang(mover, body, near);
    }

    private void BeginHang(PlayerMover mover, Rigidbody body, ThreadAnchor near)
    {
        activeMover = mover;
        activeBody = body;
        activeShape = mover.GetComponent<PlayerShapeController>();
        anchor = near;

        // Launching 상태였다면 그때 이미 원래 constraints로 되돌려 뒀으므로, 지금 읽으면 원본이다.
        savedConstraints = body.constraints;

        if (!near.lockToSidePlane)
        {
            // 자유 스윙(기본): 평면으로 끌어당기지 않는다. 다가간 위치·속도를 그대로 두고 조인트의
            // 구 구속만으로 매단다 — 어느 방향에서 걸든 그 방향으로 흔들 수 있다.
            body.constraints = savedConstraints;
        }
        else
        {
            // 앵커와 같은 Y-Z 평면(X = 앵커 X)으로 스냅하고 X를 얼린다 → 깔끔한 옆모습 진자.
            Vector3 p = body.position;
            p.x = near.transform.position.x;
            body.position = p;
            Vector3 v = body.velocity;
            v.x = 0f;
            body.velocity = v;
            body.constraints = savedConstraints | RigidbodyConstraints.FreezePositionX;
        }

        // 진자 스윙을 mover의 velocity 하드 대입/감쇠가 덮어쓰지 못하게 멈춘다. 컴포넌트를 끄지 않고
        // 플래그만 세우므로 PlayerControlSwitcher 로스터에 그대로 남는다 → 매달린 채 Tab으로 오갈 수 있고,
        // 예전의 조기 이양 상쇄(GrantControl/handoffTarget) 우회가 전부 불필요해졌다.
        mover.ExternallyDriven = true;

        currentLength = Mathf.Clamp(
            Vector3.Distance(body.position, near.transform.position), minLength, maxLength);
        targetLength = currentLength; // 릴 시작점 = 연결 시점 실제 거리(휠 전엔 목표=현재라 릴 없음).

        CreateJoint();
        line.enabled = true;
        state = ThreadState.Hanging;
        launchTimer = 0f;
        overweightTimer = 0f; // 이전 매달림의 뜯김 상태를 물려받지 않게(LateUpdate가 실 시각도 원복한다).
    }

    private void CreateJoint()
    {
        joint = activeBody.gameObject.AddComponent<ConfigurableJoint>();
        joint.autoConfigureConnectedAnchor = false;
        joint.connectedBody = null;                              // 월드 고정 앵커
        joint.connectedAnchor = anchor.transform.position;
        joint.anchor = Vector3.zero;                             // 실은 몸 중심에 걸린다
        // x/y/z를 같은 리밋으로 → 앵커 중심 반지름 L의 구속(회전 대칭 → 자유회전 구도 OK). 로프형 하드 리밋.
        joint.xMotion = ConfigurableJointMotion.Limited;
        joint.yMotion = ConfigurableJointMotion.Limited;
        joint.zMotion = ConfigurableJointMotion.Limited;
        joint.angularXMotion = ConfigurableJointMotion.Free;     // 회전은 Rigidbody constraints에 맡긴다
        joint.angularYMotion = ConfigurableJointMotion.Free;
        joint.angularZMotion = ConfigurableJointMotion.Free;
        joint.linearLimit = new SoftJointLimit { limit = currentLength, bounciness = 0f, contactDistance = 0.02f };
        joint.enablePreprocessing = false;
    }

    // 실을 끊는다. intoLaunch면 접선 속도 보존을 위해 mover를 아직 켜지 않고 Launching으로 넘어가
    // 착지 시점에 켠다(공중에서 켜면 mover가 속도를 덮어써 발사가 사라진다). intoLaunch가 false면
    // (조작권 상실 등) 그 자리에서 바로 mover를 켜고 정리한다.
    private void Release(bool intoLaunch)
    {
        if (joint != null) Destroy(joint);
        joint = null;
        if (activeBody != null) activeBody.constraints = savedConstraints;
        line.enabled = false;
        anchor = null;

        if (intoLaunch && activeMover != null)
        {
            state = ThreadState.Launching;
            launchTimer = launchReenableTimeout;
            // activeMover/activeBody/activeShape는 착지 감지를 위해 유지. mover는 계속 외부 주도로 둔다
            // (공중에서 되돌리면 mover가 접선 발사 속도를 입력값으로 덮어써 발사가 사라진다).
        }
        else
        {
            ReturnBodyToMover();
            ClearActive();
            state = ThreadState.Idle;
        }
    }

    private void FinishLaunch()
    {
        ReturnBodyToMover();
        ClearActive();
        state = ThreadState.Idle;
    }

    /// <summary>몸의 주도권을 PlayerMover에 되돌린다. 로스터를 건드리지 않으므로 조작권·카메라는
    /// 스위처가 관리하는 그대로 유지된다 — 예전처럼 조기 이양을 상쇄할 필요가 없다.</summary>
    private void ReturnBodyToMover()
    {
        if (activeMover != null) activeMover.ExternallyDriven = false;
    }

    private void ClearActive()
    {
        activeMover = null;
        activeBody = null;
        activeShape = null;
        anchor = null;
    }

    private static PlayerMover FindControlledPlayer()
    {
        // 스위처가 있으면 조작 중인 하나만 IsControlled=true. 스위처가 없는 단일 플레이어 테스트 씬에서는
        // 모두 기본값 true라 첫 번째를 집는다(테스트 씬엔 보통 하나뿐).
        foreach (PlayerMover m in Object.FindObjectsOfType<PlayerMover>())
            if (m.IsControlled) return m;
        return null;
    }

    private static ThreadAnchor FindNearestAnchorInRange(Vector3 from)
    {
        ThreadAnchor best = null;
        float bestSqr = float.PositiveInfinity;
        foreach (ThreadAnchor a in Object.FindObjectsOfType<ThreadAnchor>())
        {
            float sqr = (a.transform.position - from).sqrMagnitude;
            if (sqr <= a.connectRange * a.connectRange && sqr < bestSqr)
            {
                bestSqr = sqr;
                best = a;
            }
        }
        return best;
    }
}
