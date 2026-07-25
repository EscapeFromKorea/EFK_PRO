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
/// IsControlled/Kind를 "읽기만" 하고 mover.enabled를 "런타임 토글"할 뿐, 파일을 수정하지 않는다.
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
/// [왜 매달림 중 PlayerMover를 끄나]
/// PlayerMover는 조작 중 매 FixedUpdate에 수평 velocity를 입력값으로 하드 대입(공중 0.6배)한다.
/// 이 대입이 진자 스윙을 매 스텝 덮어써 짓밟으므로, 매달림 진입 시 mover.enabled=false로 끄고
/// 좌우 스윙은 이 컨트롤러가 전담한다. 놓을 때 다시 켜되 — 공중에서 켜면 mover가 접선 속도를
/// 즉시 입력값으로 덮어써 발사 자체가 사라지므로 — "착지할 때까지" 켜지 않고 미룬다(아래 Launching).
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

    // 매달림 진입 시 mover.enabled=false가 PlayerControlSwitcher 로스터에서 매달린 플레이어를 빼며
    // 다른 플레이어에게 조작권을 조기 이양한다. 그 이양 대상을 여기 스냅샷해 두고, 매달림/발사 도중
    // 이 값이 바뀌면(=사용자가 Tab을 눌러 진짜로 조작권을 넘김) 그때만 우리 우회를 풀어 준다.
    // 스위처가 없는 씬에서는 항상 null → "이양 없음"으로 취급된다.
    private Transform handoffTarget;

    private LineRenderer line;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
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
                Release(intoLaunch: true);
            else
                TryConnect(); // Idle 또는 Launching 중 재연결 시도(실패해도 상태 유지)
        }

        if (state == ThreadState.Hanging)
        {
            // 대상이 사라졌거나, 사용자가 Tab을 눌러 조작권을 다른 플레이어로 넘기면 즉시 떼어낸다.
            // 결정: 매달림 중 Tab → 자동으로 놓는다(스윙 유지 아님). 발사 없이 그냥 떨어뜨린다.
            // (activeMover.IsControlled는 우리가 GrantControl로 계속 true로 붙잡고 있어 신호로 못 쓴다.
            //  대신 스위처의 활성 타깃 변화로 진짜 Tab을 감지한다 — ControlSwitchedAway 참고.)
            if (activeMover == null || anchor == null || ControlSwitchedAway())
            {
                Release(intoLaunch: false);
                return;
            }
            HandleWheel();
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
        // Tab으로 조작권이 넘어간 프레임엔 펌핑하지 않는다(Update가 곧 Release). IsControlled는
        // GrantControl로 항상 true라 게이트로 못 쓰므로, 스위처 활성 타깃 변화로 판정한다.
        if (activeMover == null || ControlSwitchedAway()) return;

        // 길이 릴: 조인트 리밋을 목표 길이로 reelSpeed(Unit/s)만큼만 이동시킨다. 줄일 때 한 스텝에 확
        // 당기지 않고 물리 스텝에 걸쳐 나눠 당겨(하드 리밋 임펄스 분산) 스냅 없이 부드럽게 만든다.
        if (!Mathf.Approximately(currentLength, targetLength))
        {
            currentLength = Mathf.MoveTowards(currentLength, targetLength, reelSpeed * Time.fixedDeltaTime);
            joint.linearLimit = new SoftJointLimit { limit = currentLength, bounciness = 0f, contactDistance = 0.02f };
        }

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

        // 공중: 스윙 평면(Y-Z)으로 다시 잠근다(접지 중 풀었을 수 있으므로 매 프레임 보장).
        activeBody.constraints = savedConstraints | RigidbodyConstraints.FreezePositionX;

        // 접선 방향 펌핑: 실 방향(앵커→몸)에 수직인 Y-Z 평면 벡터로 힘을 준다. 직접 각도 조종이 아니라
        // 힘으로 진폭을 키우는 방식이라, 스윙 방향에 맞춰 입력해야(펌핑) 진폭이 는다.
        Vector3 rope = activeBody.position - anchor.transform.position;
        if (rope.sqrMagnitude < 1e-4f) return;
        Vector3 ropeDir = rope.normalized;
        Vector3 tangent = Vector3.Cross(Vector3.right, ropeDir); // (0,-z,y): Y-Z 평면 내, ropeDir에 수직
        float input = Input.GetAxis("Horizontal");
        if (invertSwing) input = -input;
        activeBody.AddForce(tangent * (input * pumpAcceleration), ForceMode.Acceleration);
    }

    void LateUpdate()
    {
        if (state != ThreadState.Hanging || activeBody == null || anchor == null) return;
        line.SetPosition(0, anchor.transform.position);
        line.SetPosition(1, activeBody.position);
    }

    // 매단 채 컨트롤러가 꺼지거나 파괴되면 플레이어를 영구 비활성/구속 상태로 남기지 않도록 원복한다.
    void OnDisable()
    {
        if (joint != null) Destroy(joint);
        if (activeBody != null) activeBody.constraints = savedConstraints;
        if (activeMover != null) activeMover.enabled = true;
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

        // 게이트는 도형 종류(Kind)로 판정한다 — 네모 거부, 구·세모 허용(질량/태그 하드코딩 금지).
        PlayerShapeIdentity identity = mover.GetComponent<PlayerShapeIdentity>();
        if (identity != null && identity.Kind == PlayerShapeStats.ShapeKind.Cube)
        {
            Debug.Log("[DreamThread] 네모는 무거워 실에 매달릴 수 없습니다(Kind 게이트).");
            return;
        }

        Rigidbody body = mover.GetComponent<Rigidbody>();
        if (body == null) return;

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

        // 앵커와 같은 Y-Z 평면(X = 앵커 X)으로 스냅하고 X를 얼린다 → 깔끔한 옆모습 진자.
        Vector3 p = body.position;
        p.x = near.transform.position.x;
        body.position = p;
        Vector3 v = body.velocity;
        v.x = 0f;
        body.velocity = v;
        body.constraints = savedConstraints | RigidbodyConstraints.FreezePositionX;

        mover.enabled = false; // 진자 스윙을 mover의 velocity 하드 대입이 덮어쓰지 못하게.

        // mover.enabled=false는 PlayerMover.OnDisable → PlayerControlSwitcher.UnregisterPlayer를 태워
        // 매달린 플레이어를 로스터에서 빼고, 남은 다른 플레이어에게 조작권/카메라를 즉시 넘긴다(조기 이양).
        // 그 이양 대상을 스냅샷해 두고(=진짜 Tab 감지 기준), GrantControl로 조기 이양을 되돌려 매달린
        // 플레이어를 유일한 조작 대상으로 다시 붙잡는다.
        handoffTarget = PlayerControlSwitcher.ActiveTarget;
        GrantControl(mover);

        currentLength = Mathf.Clamp(
            Vector3.Distance(body.position, near.transform.position), minLength, maxLength);
        targetLength = currentLength; // 릴 시작점 = 연결 시점 실제 거리(휠 전엔 목표=현재라 릴 없음).

        CreateJoint();
        line.enabled = true;
        state = ThreadState.Hanging;
        launchTimer = 0f;
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
            // activeMover/activeBody/activeShape는 착지 감지를 위해 유지. mover는 계속 꺼둔다.
        }
        else
        {
            if (activeMover != null) EndControl(activeMover);
            ClearActive();
            state = ThreadState.Idle;
        }
    }

    private void FinishLaunch()
    {
        if (activeMover != null) EndControl(activeMover);
        ClearActive();
        state = ThreadState.Idle;
    }

    // 매달림/발사를 끝내며 mover를 다시 켠다. 다시 켜지면 PlayerMover.OnEnable이 스위처에 재등록되고
    // ApplyActive가 (진입 때 이양받은) 다른 플레이어를 조작 중으로 되돌린다. 그동안 사용자가 실제로
    // Tab을 눌러 조작권을 넘긴 게 아니라면(handoffTarget 그대로) 그 조기 이양을 취소하고 원래
    // 플레이어에게 조작권/카메라를 되돌린다. 진짜 Tab이었으면 사용자의 선택(다른 플레이어)을 존중한다.
    private void EndControl(PlayerMover mover)
    {
        mover.enabled = true;
        if (!ControlSwitchedAway())
            GrantControl(mover);
    }

    // 지정 플레이어를 유일한 조작 대상으로 못박고 카메라를 그쪽으로 되돌린다. mover.enabled=false가
    // 유발한 조기 조작권 이양을 상쇄한다. 다른 플레이어는 모두 조작권을 내려 같은 입력에 동시 반응하지
    // 않게 한다. 스위처가 없는 씬에서는 SetControlled/SetActiveTarget이 각각 무해하게 동작한다.
    // ponytail: 스위처 내부 activePlayer는 public API로 되돌릴 수 없어 잠시 어긋난 채 남는다 —
    // 발사 착지 후 첫 Tab이 순환 순서에서 한 명을 건너뛸 수 있고, 그 Tab에서 스위처가 스스로 정합을
    // 회복한다. 스위처에 SetActive(mover) public API가 생기면 이 우회를 없앨 수 있다(PlayerSystem 변경 필요).
    private static void GrantControl(PlayerMover target)
    {
        foreach (PlayerMover m in Object.FindObjectsOfType<PlayerMover>())
            m.SetControlled(m == target);
        if (target != null)
            PlayerFollowCamera.SetActiveTarget(target.transform);
    }

    // 매달림 진입 시 조기 이양받았던 플레이어(handoffTarget)가 지금 스위처의 활성 타깃과 다르면,
    // 그 사이 사용자가 Tab을 눌러 조작권을 진짜로 넘긴 것이다. 스위처가 없으면 둘 다 null → false.
    private bool ControlSwitchedAway()
    {
        return handoffTarget != PlayerControlSwitcher.ActiveTarget;
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
