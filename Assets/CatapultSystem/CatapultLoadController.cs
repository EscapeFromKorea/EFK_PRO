using UnityEngine;

/// <summary>
/// 투석기별 장전 컨트롤러(씬 싱글턴 아님, PRD 확정 — 투석기 인스턴스마다 하나씩 붙어 여러 대를
/// 동시에 독립적으로 운용한다). C 입력으로 당김 줄 연결/해제 상태머신을 관리한다.
///
/// [DreamThreadController와의 차이 — ConfigurableJoint 대신 거리→비율 계산]
/// `DreamThreadController`의 F 연결/해제 상태머신·동시 1인 연결 게이트 패턴을 이식하되, 실타래처럼
/// 플레이어 몸에 조인트를 물리지 않는다. 스윙 물리가 필요 없으므로 실타래가 겪은 "로프 물리 안정화가
/// 최상위 리스크"를 애초에 지지 않는다(PRD §3). 연결된 플레이어는 평소처럼 자유롭게 걸어 다니고,
/// 이 컨트롤러는 매 프레임 앵커와의 거리만 재서 비율로 변환해 `CatapultArm.BeginPull`에 넘긴다.
/// 그래서 연결 중에도 `PlayerMover.ExternallyDriven`을 세우지 않는다 — 조준자의 이동 자체가 장전
/// 메커니즘의 입력이라 막으면 안 된다(실타래의 매달림과 근본적으로 다른 지점).
///
/// [C 게이트 — "다른 플레이어가 연결 중이면 대기"]
/// `DreamThreadController`가 2026-07-30 입력 게이트 감사에서 고친 것과 같은 이유로, C가 상태를
/// 바꾸는 지점은 반드시 "연결된 당사자가 지금 조작 대상인가"를 확인한다. 이 컨트롤러는 씬 싱글턴이
/// 아니라 투석기 인스턴스마다 있지만, C 입력 자체는 모든 인스턴스가 동시에 읽는 전역 키다 — 그래서
/// 연결된 플레이어가 Tab으로 파킹된 동안 다른 플레이어가 아무 데서나 누른 C가 이 투석기의 연결을
/// 끊어버리는 사고를 막아야 한다. `TryConnect`로 흘려보내면 안 되는 이유도 동일: 그러면 파킹된
/// 플레이어의 참조가 새 플레이어로 덮여 원래 플레이어가 영구히 추적에서 사라진다.
///
/// [겹치는 범위 — 가장 가까운 투석기에만 연결]
/// 투석기를 서로 충분히 떨어뜨려 배치하는 게 기본 전제지만, 혹시 두 투석기의 `connectRange`가
/// 겹치는 위치에서 C를 누르면(모든 컨트롤러가 같은 프레임에 전역 C를 독립적으로 읽으므로) 두
/// 투석기에 동시에 연결되는 사고가 날 수 있었다. `IsNearestCatapult`가 이를 막는다 — 자신보다
/// 가까운 다른 투석기가 범위 안에 있으면 연결을 양보한다.
///
/// [실 시각화 — 2026-08-04 플레이테스트 요청]
/// 연결 중(정사면체가 C로 당김 줄을 걸었을 때, 24차 개편부터 정사면체 전용)에만 플레이어와 당김 앵커 사이에 실이 보이도록
/// `LineRenderer`를 그린다. `DreamThreadSystem`(`ThreadBridge`/`DreamThreadController`)이 이미 쓰는
/// 패턴을 그대로 이식했다(파일은 건드리지 않고 패턴만 참고) — 상태에 따라 `enabled`만 토글하고,
/// 공유 머티리얼을 오염시키지 않도록 `line.material`(인스턴스)에 색을 직접 지정한다. 색은
/// `CatapultPullAnchor`의 주황 계열과 맞췄다.
///
/// [9차 개편(2026-08-05) — 마우스 휠이 "거리→비율" 계산을 완전히 대체]
/// 사용자가 설계 방향을 확정했다: 연결 중에는 조준자가 앵커에서 멀어지든 가까워지든 당김 비율에
/// 전혀 영향이 없고, 오직 마우스 휠 스크롤 누적값(`wheelRatio`)만으로 0~1 비율이 정해진다 — 거리
/// 기반 계산에 휠 오프셋을 "더하는" 방식이 아니라 완전한 대체다. `ComputeRatio`(거리→비율)는 이제
/// 어디서도 쓰이지 않아 삭제했다(죽은 코드를 남기지 않는다) — `minPullDistance`/`maxPullDistance`
/// 필드도 함께 삭제했다(PRD §6이 확정했던 "당김 거리" 수치 체계는 이 개편으로 폐기됐다 — 자세한
/// 근거는 `docs/PRD/Catapult.md` 상단 9차 개편 요약 참고). **단, `CatapultPullAnchor.connectRange`(C를 눌러 애초에
/// 연결을 "시작"할 수 있는 거리 게이트)는 이번 변경과 무관하게 그대로 남아 있다** — 완전히 다른
/// 메커니즘(연결 성사 여부)이라 혼동하지 않을 것.
/// - **스크롤 방향(`Input.GetAxis("Mouse ScrollWheel")` 양수 = 위로 스크롤)은 그대로 당김 증가로
///   매핑했다** — 위로 스크롤할수록 더 당긴다는 관례적 방향(줌인/증가 계열 UI와 같은 감각)이라
///   부호를 뒤집지 않았다. `wheelSensitivity`(감도)와 `resetRatioOnDisconnect`(연결 해제 시 누적값
///   초기화 여부)는 이 프로젝트의 다른 "씬 튜닝 전 임시값"과 같은 패턴으로 감각적 기본값을 정했다 —
///   씬 튜닝 전 임시값 `[TBD]`(정확한 수치·정책은 실측 필요, 아래 필드 툴팁 참고).
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class CatapultLoadController : MonoBehaviour
{
    private enum State { Idle, Connected }

    [Header("연결 대상")]
    [Tooltip("이 투석기의 당김 앵커.")]
    public CatapultPullAnchor anchor;
    [Tooltip("이 투석기의 팔. BeginPull/Fire를 호출한다.")]
    public CatapultArm arm;

    [Header("실 시각화 (신규, 2026-08-04)")]
    [Tooltip("연결 중 보이는 실의 두께.")]
    public float lineWidth = 0.05f;

    [Header("마우스 휠 당김 조절 (9차 개편, 신규 — 거리 기반 계산을 완전히 대체) [TBD, 임시값]")]
    [Tooltip("휠 스크롤 한 눈금(Input.GetAxis(\"Mouse ScrollWheel\")≈±0.1)당 당김 비율이 얼마나 " +
             "바뀌는지. 기본값 1.0은 스크롤 한 눈금당 약 0.1(≈10눈금에 0→1 완전 장전)이라, 이 파일의 " +
             "다른 '10단계' 감각(CatapultArm.pullNotchCount 기본값 10)과 우연이 아니라 의도적으로 " +
             "맞춘 감각적 기본값이다 — 씬 튜닝 전 임시값.")]
    public float wheelSensitivity = 1f;
    [Tooltip("연결을 해제할 때(발사 포함) 누적된 휠 비율을 0으로 되돌릴지. true(기본값)면 다음 연결은 " +
             "항상 비율 0부터 새로 시작한다 — '매번 처음부터 당긴다'는 감각이 직관적이라고 판단한 " +
             "기본값이다. false로 두면 이전 연결에서 남은 비율이 다음 연결에도 이어진다. 씬 튜닝 전 " +
             "임시값(정책 자체가 미확정).")]
    public bool resetRatioOnDisconnect = true;

    private State state = State.Idle;
    private PlayerMover connectedMover;
    private Rigidbody connectedBody;
    private LineRenderer line;
    private float wheelRatio; // 9차 개편 — 마우스 휠 누적 당김 비율(0~1). 거리 계산을 완전히 대체한다.

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.widthMultiplier = lineWidth;
        line.enabled = false;
        if (line.sharedMaterial == null)
        {
            Shader s = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (s != null) line.material = new Material(s) { color = new Color(1f, 0.6f, 0.1f, 1f) };
        }
    }

    void Reset()
    {
        LineRenderer lr = GetComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.widthMultiplier = 0.05f;
        lr.numCapVertices = 2;
        lr.enabled = false;
    }

    void Update()
    {
        HandleInput();
        if (state == State.Connected) UpdatePull();
    }

    private void HandleInput()
    {
        if (!Input.GetKeyDown(KeyCode.C)) return;

        if (state == State.Connected)
        {
            if (connectedMover != null && !connectedMover.IsControlled)
            {
                Debug.Log("[Catapult] 다른 플레이어가 이 투석기의 당김 줄에 연결되어 있습니다 — " +
                          "Tab으로 그 플레이어를 조작해 C로 해제하세요(동시 1인 연결).");
                return;
            }
            Disconnect(fire: true);
            return;
        }

        TryConnect();
    }

    private void UpdatePull()
    {
        if (connectedBody == null || anchor == null)
        {
            // 대상/앵커 소멸 — 발사 없이 안전하게 해제한다.
            Disconnect(fire: false);
            return;
        }

        // 9차 개편 — 조준자와 앵커 사이의 거리는 더 이상 당김 비율에 관여하지 않는다(클래스 상단
        // "9차 개편" 주석 참고). 휠 스크롤 누적값만이 유일한 입력이다.
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        wheelRatio = Mathf.Clamp01(wheelRatio + scroll * wheelSensitivity);

        if (arm != null) arm.BeginPull(wheelRatio);

        line.SetPosition(0, anchor.transform.position);
        line.SetPosition(1, connectedBody.position);
    }

    private void TryConnect()
    {
        if (anchor == null || arm == null) return;

        PlayerMover mover = FindControlledPlayer();
        if (mover == null)
        {
            Debug.Log("[Catapult] 조작 중인 플레이어를 찾지 못했습니다.");
            return;
        }

        // 장전은 정사면체 전용이다(24차 개편, 역할 게이트 — PRD §5. PlayerWeight 미사용).
        // 22차 개편 전까지는 "정육면체만 거부"(구·정사면체 둘 다 장전 가능)였다 — 그때는 구가 C를
        // 쓸 일이 조향(물리 충돌)엔 전혀 없어 무해한 여유였다. 22차 개편으로 구가 조향석 도킹에 C를
        // 쓰게 되면서, 조향석 dockRange와 이 당김 앵커 connectRange가 겹치는 위치에서 구가 C를
        // 누르면 두 컴포넌트가 같은 프레임에 동시에 반응해(도킹 + 당김줄 연결 둘 다 성사) "링크할
        // 때 실도 같이 매달리는" 간섭이 났다 — 사용자가 실제로 재현해 보고했다. 구 전용 역할
        // (`CatapultSteerHandle`)이 생긴 지금은 장전을 정사면체 하나로 좁혀야 두 C 기능이 겹칠 수
        // 없다(도킹은 구만, 장전은 정사면체만 — 교집합이 없다).
        PlayerShapeIdentity identity = mover.GetComponent<PlayerShapeIdentity>();
        if (identity == null || identity.Kind != PlayerShapeStats.ShapeKind.Tetrahedron)
        {
            Debug.Log("[Catapult] 당김 줄 연결은 정사면체 전용입니다(구는 조향석 도킹, 정육면체는 탑승).");
            return;
        }

        Rigidbody body = mover.GetComponent<Rigidbody>();
        if (body == null) return;

        float myDistance = Vector3.Distance(body.position, anchor.transform.position);
        if (myDistance > anchor.connectRange)
        {
            Debug.Log("[Catapult] 당김 앵커 범위 밖입니다.");
            return;
        }

        // 범위가 겹치는 투석기가 여럿이면 가장 가까운 것에만 연결한다 — 그렇지 않으면 C 한 번에
        // 여러 투석기가 동시에 같은 플레이어를 붙잡는다(각 컨트롤러가 전역 C 키를 독립적으로 읽으므로).
        if (!IsNearestCatapult(body.position, myDistance))
            return;

        connectedMover = mover;
        connectedBody = body;
        state = State.Connected;
        // 9차 개편 — 연결 순간의 초기 비율은 거리 계산이 아니라 현재 wheelRatio(리셋 정책에 따라
        // 0이거나 이전 값)를 그대로 쓴다.
        arm.BeginPull(wheelRatio);
        line.enabled = true;
        Debug.Log("[Catapult] 당김 줄에 연결했습니다. 마우스 휠로 당김 정도를 조절하세요.");
    }

    // 범위 안에 있는 다른 투석기 중 이 투석기보다 가까운 것(또는 정확히 같은 거리라면 InstanceID가
    // 더 작은 것)이 있으면 false를 반환해 연결을 양보한다.
    private bool IsNearestCatapult(Vector3 playerPosition, float myDistance)
    {
        foreach (CatapultLoadController other in FindObjectsOfType<CatapultLoadController>())
        {
            if (other == this || other.anchor == null) continue;

            float otherDistance = Vector3.Distance(playerPosition, other.anchor.transform.position);
            if (otherDistance > other.anchor.connectRange) continue;

            if (otherDistance < myDistance) return false;
            if (otherDistance == myDistance && other.GetInstanceID() < GetInstanceID()) return false;
        }
        return true;
    }

    // 그 순간의 당김 비율(wheelRatio)로 발사(fire: true)하거나, 대상/고리 소멸 등으로 발사 없이
    // 놓는다(fire: false).
    private void Disconnect(bool fire)
    {
        float ratio = wheelRatio;

        state = State.Idle;
        connectedMover = null;
        connectedBody = null;
        line.enabled = false;
        if (resetRatioOnDisconnect) wheelRatio = 0f; // 다음 연결은 새로 시작(정책, 클래스 상단 주석 참고).

        if (fire && arm != null)
            arm.Fire(ratio);
    }

    private static PlayerMover FindControlledPlayer()
    {
        foreach (PlayerMover m in Object.FindObjectsOfType<PlayerMover>())
            if (m.IsControlled) return m;
        return null;
    }
}
