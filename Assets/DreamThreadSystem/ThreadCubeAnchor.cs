using UnityEngine;

/// <summary>
/// 꿈의 실타래 Phase 3 — "네모 닻". 네모 플레이어 위에 고리(ThreadAnchor)를 하나 달아, 네모를
/// 세워 두면 구·세모가 그 고리에 F로 매달려 크게 스윙할 수 있게 한다. 씬에 하나만 둔다.
///
/// [왜 '월드 지점 앵커'인가 — 네모 Rigidbody 연결을 버린 이유 (PRD §9 미결 항목 해소)]
/// 매달림 조인트를 네모의 Rigidbody에 connectedBody로 걸면, 매달린 도형의 무게·스윙 반작용이 그대로
/// 네모를 끌고 다닌다. 그래서 네모를 제자리에 붙들 별도 고정 로직(질량 뻥튀기, 위치 고정, 탈출 조건…)이
/// 새로 필요해지고, 그건 Phase 2에서 세모 벽부착으로 이미 겪은 "고정 상태 관리" 비용을 한 번 더 치르는
/// 일이다. 반면 컨트롤러의 조인트는 원래 connectedBody=null + 앵커 위치 스냅샷으로 도는 월드 지점
/// 방식이라(Phase 1), 네모를 **위치 마커**로만 쓰면 코드가 한 줄도 안 늘어난다.
///
/// [대신 게이트로 판타지를 지킨다]
/// 월드 지점 앵커는 네모의 무게가 물리적으로 아무 의미가 없다 — 종이짝이 앵커여도 똑같이 버틴다.
/// "네모가 버텨준다"는 기획 의도가 죽지 않게, 고리는 네모가 **접지해 있고 거의 멈춰 있을 때만**
/// 성립한다. 공중에 뜬 네모, 굴러가는 네모, 떨어지는 네모는 닻이 못 된다 — 플레이어 입장에서는
/// "네모를 제 자리에 딱 세워야 실을 걸 수 있다"로 읽히고, 그게 딱 협동 동선이다.
///
/// [게이트를 어떻게 거나 — connectRange를 0으로 만든다]
/// 컨트롤러는 씬의 모든 ThreadAnchor 중 connectRange 안에 든 것만 고른다. 그래서 조건이 깨지면
/// 고리를 부수거나 끄지 않고 connectRange만 0으로 낮춘다.
/// - 고리를 Destroy하면 매달려 있던 사람이 그 순간 떨어진다(컨트롤러의 anchor==null 가드). 네모가
///   살짝 밀린 것만으로 스윙 중인 구를 떨어뜨리는 건 너무 가혹하다.
/// - GameObject를 비활성화하면 매달리는 중에 고리 마커만 사라져 보인다.
/// connectRange=0이면 **새로 거는 것만** 막히고, 이미 매달린 사람은 (조인트가 월드 지점 스냅샷이라)
/// 그대로 스윙을 이어간다. 상태 저장도 필요 없고 컨트롤러도 무변경이다.
///
/// [하드룰] 네모에 붙이는 건 런타임 자식 마커뿐이다(ThreadPinPlacer가 핀을 만드는 것과 같은 방식).
/// PlayerSystem은 Kind / IsGrounded / velocity를 읽기만 한다 — 파일 수정 없음.
/// </summary>
public class ThreadCubeAnchor : MonoBehaviour
{
    [Header("고리 위치/범위")]
    [Tooltip("네모 중심에서 고리를 얼마나 위에 달지(Unit). 실이 네모 몸통에 파묻히지 않을 만큼 띄운다.")]
    public float anchorHeightOffset = 0.9f;
    [Tooltip("닻이 성립할 때의 연결 범위(Unit). 다른 도형이 이 거리 안에서 F로 매달릴 수 있다.")]
    public float connectRange = 4f;
    [Tooltip("고리 마커 지름(Unit).")]
    public float markerSize = 0.28f;

    [Header("닻 성립 조건")]
    [Tooltip("네모가 이 속력(Unit/s) 이하로 거의 멈춰 있어야 닻이 된다. 굴러가거나 떨어지는 네모에는 못 건다.")]
    public float maxAnchorSpeed = 0.35f;
    [Tooltip("네모가 접지해 있어야 닻이 되는지. 끄면 공중에 뜬 네모(무중력 버블 안 등)에도 걸 수 있다.")]
    public bool requireGrounded = true;

    [Header("스윙 방식")]
    [Tooltip("이 닻에 매달린 스윙을 Y-Z 평면(옆모습)에 가둬 좌우로만 흔들리게 한다(기본 꺼짐 = 앞뒤로도 " +
             "흔들리는 자유 스윙). 닻은 '네모를 중심으로 돈다'는 그림이라 어느 방향에서 접근하든 그쪽으로 " +
             "흔들려야 자연스러워서, 특별한 이유가 없으면 끈 채로 둔다.")]
    public bool lockToSidePlane = false;

    private const string RingName = "DreamThread_CubeAnchorRing";

    private Material armedMaterial;
    private Material idleMaterial;

    void Update()
    {
        // ponytail: 매 프레임 전체 탐색 — 플레이어가 3명뿐이라 무시할 비용이고, 이렇게 해야 리스폰으로
        // 네모가 새로 생겨도 자동으로 고리가 달린다. 플레이어 수가 늘면 등록식으로 바꾼다.
        foreach (PlayerShapeIdentity id in Object.FindObjectsOfType<PlayerShapeIdentity>())
        {
            if (id.Kind != PlayerShapeStats.ShapeKind.Cube) continue;

            Transform ring = EnsureRing(id.transform);
            bool armed = IsAnchorReady(id);

            ThreadAnchor a = ring.GetComponent<ThreadAnchor>();
            a.connectRange = armed ? connectRange : 0f; // 0이면 컨트롤러의 거리 판정에 절대 안 걸린다
            a.lockToSidePlane = lockToSidePlane;
            ring.GetComponent<Renderer>().sharedMaterial = armed ? GetArmedMaterial() : GetIdleMaterial();
        }
    }

    /// <summary>네모가 지금 닻으로 성립하는가. "세워 둔 네모 + 접지 + 거의 정지"가 조건이다.</summary>
    private bool IsAnchorReady(PlayerShapeIdentity cube)
    {
        Rigidbody rb = cube.GetComponent<Rigidbody>();
        if (rb == null) return false;

        // 지금 조작 중인 네모는 닻이 못 된다. 닻은 "세워 두고 Tab으로 넘어간 네모"를 위한 것이고,
        // 이 한 줄이 무중력 버블 안에서 가벼워진 네모가 제 머리 위 고리에 매달리는 퇴행적 조합도
        // 함께 막는다. (스위처가 없는 테스트 씬은 모두 IsControlled=true라 닻이 안 켜진다.)
        PlayerMover mover = cube.GetComponent<PlayerMover>();
        if (mover != null && mover.IsControlled) return false;

        if (rb.velocity.magnitude > maxAnchorSpeed) return false;

        if (!requireGrounded) return true;
        // 접지 판정은 컨트롤러가 착지 감지에 쓰는 것과 같은 창구를 쓴다. 없는 플레이어는 속도 조건만 본다.
        PlayerShapeController shape = cube.GetComponent<PlayerShapeController>();
        return shape == null || shape.IsGrounded();
    }

    // 네모의 자식으로 고리 마커를 하나 유지한다. 이름으로 찾으므로 상태를 따로 들고 있지 않아도 되고,
    // 네모가 파괴되면 고리도 함께 사라진다(수명 관리 불필요).
    private Transform EnsureRing(Transform cube)
    {
        Transform ring = cube.Find(RingName);
        if (ring != null)
        {
            ring.localPosition = Vector3.up * anchorHeightOffset;
            return ring;
        }

        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.name = RingName;
        Destroy(obj.GetComponent<Collider>()); // 앵커는 순수 마커 — 물리 접촉 없음(ThreadAnchor 주석 참고)
        obj.transform.SetParent(cube, false);
        obj.transform.localPosition = Vector3.up * anchorHeightOffset;
        obj.transform.localScale = Vector3.one * markerSize;
        obj.AddComponent<ThreadAnchor>().connectRange = 0f;
        return obj.transform;
    }

    // 닻이 걸리는 상태는 따뜻한 금색, 안 걸리는 상태는 어둡게 — 플레이어가 "지금 걸 수 있나"를 눈으로 안다.
    // 런타임 생성이라 에디터 API를 쓰지 않고 언릿 계열 셰이더로 파이프라인 무관하게 만든다(핀 마커와 동일).
    private Material GetArmedMaterial() => armedMaterial != null
        ? armedMaterial
        : (armedMaterial = MakeMarkerMaterial(new Color(1f, 0.85f, 0.35f, 1f)));

    private Material GetIdleMaterial() => idleMaterial != null
        ? idleMaterial
        : (idleMaterial = MakeMarkerMaterial(new Color(0.35f, 0.35f, 0.4f, 1f)));

    private static Material MakeMarkerMaterial(Color c)
    {
        Shader s = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
        return new Material(s) { color = c };
    }
}
