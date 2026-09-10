using UnityEngine;

/// <summary>
/// KitchenMapV3 자유시점 3인칭 카메라.
/// 출처: develop_snapshot/Assets/LevelDesign/Map1/Map1ThirdPersonCamera.cs (@codex, 2026-08-24)를
/// V3 블록아웃용으로 이식 — 씬 이름 게이트를 제거하고 V3_Play(Setup Play)가 직접 부착한다.
/// 원본 파일·저장소는 수정하지 않았다(읽기 전용 원칙).
///
/// 조작: 우클릭 드래그 = 시점 회전 / Q·E = 좌우 회전 / Z·C = 상하 회전 / V = 시점 리셋 /
///       마우스 휠 = 줌. W/A/S/D는 현재 카메라가 보는 방향 기준으로 움직인다(PlayerMover.inputYawOffset).
/// </summary>
public sealed class V3ThirdPersonCamera : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private float distance = 9.5f;
    [SerializeField] private float minDistance = 3f;
    [SerializeField] private float maxDistance = 30f;
    [SerializeField] private float targetHeight = 1.1f;
    [SerializeField] private float followSmoothTime = 0.10f;
    [SerializeField] private float yaw = 0f;
    [SerializeField] private float pitch = 18f;
    [SerializeField] private float minPitch = -12f;
    [SerializeField] private float maxPitch = 70f;

    [Header("Look controls")]
    [SerializeField] private float mouseSensitivity = 3.0f;
    [SerializeField] private float keyboardYawSpeed = 120f;
    [SerializeField] private float keyboardPitchSpeed = 70f;
    [SerializeField] private float zoomSpeed = 6f;

    // ── [후보 구현 — P5 뒷마당 카메라 제한] ──────────────────────────────────────
    // 근거: 맵2_V3_릴레이설계/하18_P5카메라_산출보고서_2026-09-05.md §5(구현 스펙 초안) +
    // 검문 1차 "관찰 2"(레이어 설정 프로젝트 전체 0건 · Update()의 distance Clamp가 휠 입력
    // 시에만 실행되는 결함). 이 구현은 릴레이 질의 (마)-2 [수단 인정 여부] 판정 전까지
    // "후보"다 — 정식 채택이 아니라 T0 체감 측정용. 실내(z<86) 경로에는 영향을 주지 않는다.
    [Header("P5 뒷마당 카메라 제한 (후보 — (마)-2 판정 대기)")]
    [SerializeField] public bool yardCameraLimit = true;
    [SerializeField] private float yardMinDocY = 86f;      // 문서 Y=unity z. RoomY(V3_Build.cs)와 동일.
    [SerializeField] private float yardMaxDistance = 8f;
    [SerializeField] private float collisionPull = 0.3f;

    private Transform target;
    private Vector3 followVelocity;
    private bool rightMouseWasHeld;

    private void Awake()
    {
        // [정정 2026-09-10, 컨트롤타워 36-b A2 결정①] 고정 팔로우 카메라(PlayerFollowCamera)가
        // 이 오브젝트에 붙어 있으면 enabled=false가 아니라 파괴한다. develop
        // PlayerMover.EffectiveInputYaw()는 PlayerFollowCamera.ViewYaw가 non-null이면
        // inputYawOffset을 무시한다(§36-b 실측) — enabled=false만 하면 Awake는 그대로 돌아
        // instance=this가 서지만 Start(orbitYaw=cameraYawOffset 초기화)는 비활성 컴포넌트라
        // 돌지 않으므로, ViewYaw가 미초기화 orbitYaw(0)를 계속 돌려주고 방향키 보정이 0도로
        // 죽는다. 파괴하면 PFC.OnDestroy가 instance=null을 세워(62f4e29:
        // Assets/PlayerSystem/PlayerFollowCamera.cs "void OnDestroy() { if (instance == this)
        // instance = null; }" 확인) ViewYaw가 다시 null로 돌아가 PlayerMover가 기존
        // inputYawOffset(아래 LateUpdate가 매 프레임 갱신)으로 폴백한다. V3_Play.cs SetupPlay가
        // 에디터 단계에서 이미 DestroyImmediate로 걷어내므로 정상 경로에선 여기 걸릴 일이
        // 없다 — 다른 경로로 PFC가 붙어 있을 때의 방어선이다.
        PlayerFollowCamera fixedFollow = GetComponent<PlayerFollowCamera>();
        if (fixedFollow != null) Destroy(fixedFollow);

        Camera cam = GetComponent<Camera>();
        if (cam != null)
        {
            cam.orthographic = false;
            cam.fieldOfView = 65f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;   // 부엌 98×110 전경이 보이도록(원본 160 → 확장)
        }
    }

    private void OnDisable()
    {
        ReleaseMouse();
    }

    private void Update()
    {
        bool rightMouseHeld = Input.GetMouseButton(1);
        if (rightMouseHeld)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        }
        else if (rightMouseWasHeld)
        {
            ReleaseMouse();
        }
        rightMouseWasHeld = rightMouseHeld;

        if (Input.GetKey(KeyCode.Q)) yaw -= keyboardYawSpeed * Time.unscaledDeltaTime;
        if (Input.GetKey(KeyCode.E)) yaw += keyboardYawSpeed * Time.unscaledDeltaTime;
        if (Input.GetKey(KeyCode.Z)) pitch += keyboardPitchSpeed * Time.unscaledDeltaTime;
        if (Input.GetKey(KeyCode.C)) pitch -= keyboardPitchSpeed * Time.unscaledDeltaTime;
        if (Input.GetKeyDown(KeyCode.V)) { yaw = 0f; pitch = 18f; distance = 9.5f; }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
            distance = Mathf.Clamp(distance - scroll * zoomSpeed, minDistance, maxDistance);

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void LateUpdate()
    {
        Transform activeTarget = PlayerControlSwitcher.ActiveTarget;
        if (activeTarget != null && activeTarget != target)
        {
            target = activeTarget;
            followVelocity = Vector3.zero;
            SnapToTarget();
        }
        if (target == null) return;

        // W가 현재 시선의 앞, A/D가 좌우가 되게 — 카메라 방위를 이동 입력에 반영(원본 방식).
        PlayerMover mover = target.GetComponent<PlayerMover>();
        if (mover != null) mover.inputYawOffset = yaw;

        // [후보 구현] 뒷마당(문서 y>=86) 구간 한정 거리 상한 — 관찰 2 결함 보완: Update()의
        // Mathf.Clamp는 마우스 휠 입력이 있을 때만 실행되므로, 이미 늘어난 distance를 매 프레임
        // 강제로 줄이려면 여기서 별도로 클램프해야 한다. 실내(z<86)이거나 토글이 꺼져 있으면
        // 이 블록 자체에 진입하지 않아 기존 동작과 완전히 동일하다 — 구간 밖 복귀 시에도 이
        // 클램프가 실행되지 않으므로 distance 값은 그대로 유지되고(휠로 다시 30까지 늘릴 수
        // 있음), 유효 상한은 원래 maxDistance(30)로 자동 복원된다(Update()가 그 상수를 그대로 씀).
        bool inYard = yardCameraLimit && target.position.z >= yardMinDocY;
        if (inYard)
        {
            distance = Mathf.Clamp(distance, minDistance, yardMaxDistance);
        }

        Vector3 lookPoint = target.position + Vector3.up * targetHeight;
        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredPosition = lookPoint + orbit * (Vector3.back * distance);
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref followVelocity, followSmoothTime);
        if (inYard)
        {
            // 검문 N3(컨트롤타워 채택): 실내(distance 30)에서 뒷마당 진입 시 SmoothDamp 지연으로
            // 실거리가 yardMaxDistance(8)를 순간 초과하는 것을 반경 클램프로 막는다(followVelocity 비관여).
            Vector3 pulled = transform.position - lookPoint;
            if (pulled.magnitude > yardMaxDistance) transform.position = lookPoint + pulled.normalized * yardMaxDistance;
        }
        transform.rotation = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);

        // [후보 구현] 뒷마당 충돌 당김 — SmoothDamp가 끝난 뒤, 실제로 화면에 쓰일 위치만
        // 당김 결과로 덮어쓴다. followVelocity는 위 SmoothDamp 호출이 이미 원래 desiredPosition
        // 기준으로 갱신했으므로 여기서 다시 건드리지 않는다(다음 프레임 감쇠가 당김으로 오염되지
        // 않고 정상 진행되게 하기 위함 — 판단 근거는 산출 보고서 §5 및 회신 "검토 요청" 참고).
        if (inYard)
        {
            transform.position = ApplyYardCollisionPull(lookPoint, transform.position);
            transform.rotation = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
        }
    }

    // [후보 구현 — P5 뒷마당 카메라 충돌 당김]
    // 레이어 마스크는 쓰지 않는다 — 검문 1차 관찰 2: 프로젝트 어디에도 레이어 설정이 없다
    // (V3_Core.cs·V3_Build.cs·카메라 전부 .layer/LayerMask 0건). 대신 히트된 콜라이더의
    // transform이 target(플레이어 루트) 자신이거나 그 자식(Player_Collider 등)이면 그 히트를
    // 자기 자신으로 보고 무시한다.
    //
    // 검문 C1 정정: "lookPoint가 자기 콜라이더 내부"는 사실이 아니다 — 정식 생성기 콜라이더
    // 반지름은 0.5~0.866, targetHeight(1.1)로 lookPoint는 몸 밖 0.407~0.600U(솔리드 콜라이더
    // 기준: 구 +0.5·네모 상면 +0.5·정사면체 chamfered 상단 +0.693; 트리거 메쉬는 레이가 무시)
    // 지점에 있다. Physics.Linecast가 아니라 RaycastAll을 쓴 진짜 이유는 두 가지: (1) Linecast는
    // "가장 가까운 히트 1개"만 주므로 그 1개가 자기 자신(플레이어 콜라이더)이면 뒤쪽(진짜
    // 차폐물)을 영영 못 본다 — lookPoint가 몸 밖이어도 선분이 몸통을 스치며 자기 히트가 섞일
    // 수 있다. (2) 스케일 확대 시 lookPoint가 몸 안으로 들어올 수 있어 위 여유가 항상 보장되지
    // 않는다. 현 설정(minPitch −12°·ScalePad 미배치)에서는 둘 다 도달 불가하나 방어적으로
    // RaycastAll을 유지한다. RaycastAll로 선분 위 모든 히트를 모은 뒤 자기 자신을 걸러내고 남은
    // 것 중 최근접을 골라야 한다.
    private Vector3 ApplyYardCollisionPull(Vector3 lookPoint, Vector3 candidatePosition)
    {
        Vector3 delta = candidatePosition - lookPoint;
        float segmentLength = delta.magnitude;
        if (segmentLength < 0.0001f) return candidatePosition;
        Vector3 direction = delta / segmentLength;

        // 트리거 콜라이더(Player_Mesh 등)는 QueryTriggerInteraction.Ignore로 전부 제외.
        RaycastHit[] hits = Physics.RaycastAll(lookPoint, direction, segmentLength, ~0, QueryTriggerInteraction.Ignore);

        float nearestDistance = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform == target || hitTransform.IsChildOf(target)) continue; // 자기 자신 제외
            if (hits[i].distance < nearestDistance)
            {
                nearestDistance = hits[i].distance;
                found = true;
            }
        }
        if (!found) return candidatePosition;

        // lookPoint 쪽으로 collisionPull(0.3)만큼 당긴다. minDistance(3) 미만이 되어도 허용
        // (요구사항 3 — 벽에 바짝 붙는 경우를 막지 않는다). 다만 당김이 lookPoint 자체를
        // 지나쳐 반대편으로 넘어가지 않게 바닥 처리한다 — 검문 C2: 바닥을 0f가 아니라
        // nearClipPlane(0.1f)로 잡는다. 거리 0이면 이후 LookRotation(lookPoint - transform.position)이
        // 영벡터를 받아 에러를 낸다.
        float pulledDistance = Mathf.Max(0.1f, nearestDistance - collisionPull);
        return lookPoint + direction * pulledDistance;
    }

    private void SnapToTarget()
    {
        if (target == null) return;
        Vector3 lookPoint = target.position + Vector3.up * targetHeight;
        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = lookPoint + orbit * (Vector3.back * distance);
        transform.rotation = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
    }

    private static void ReleaseMouse()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
