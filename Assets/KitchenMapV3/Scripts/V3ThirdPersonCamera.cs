using UnityEngine;

/// <summary>
/// KitchenMapV3 자유시점 3인칭 카메라.
/// 출처: develop_snapshot/Assets/LevelDesign/Map1/Map1ThirdPersonCamera.cs (@codex, 2026-08-24)를
/// V3 블록아웃용으로 이식 — 씬 이름 게이트를 제거하고 V3_Play(Setup Play)가 직접 부착한다.
/// 원본 파일·저장소는 수정하지 않았다(읽기 전용 원칙).
///
/// 조작: 우클릭 드래그 = 시점 회전 / Q·E = 좌우 회전 / Z·C = 상하 회전 / V = 시점 리셋 /
///       마우스 휠 = 줌. W/A/S/D는 현재 카메라가 보는 방향 기준으로 움직인다(PlayerMover.inputYawOffset).
///
/// [K09 정정, 2026-09-12 — Codex 검수 실측: 키전환후_벽가림.jpg]
/// 충돌 당김(장애물 뒤로 나간 카메라를 시선점 쪽으로 당기기)이 뒷마당(문서 y≥86) 구간에서만
/// 실행됐다. 기본값 yaw 0·pitch 18·distance 9.5면 카메라는 시선점에서 (0, +2.94, −9.03)에 놓이는데
/// 스폰 지점(unity z=6)에서는 z≈−3.0 — Wall_S(V3_Build.cs Shell(), unity z −1~0) 바깥이라 시작
/// 화면과 Tab 전환 직후 화면이 벽 텍스처로 덮였다. 이제 충돌 당김을 실내·실외 상시 적용하고
/// SnapToTarget(시작·Tab 전환)에도 적용한다. 뒷마당 거리 상한(8U)만 기존 토글(yardCameraLimit,
/// 메뉴 8d)에 남긴다. 지형은 옮기지 않는다.
///
/// [K05, 2026-09-12] 카메라의 C(피치)·휠(줌)이 실타래(DreamThreadController 휠 = 매달림 길이)·
/// 투석기(C 장전/탑승/도킹·휠 당김)와 같은 키를 쓴다. 활성 대상의 PlayerMover.ExternallyDriven이
/// true(실타래 매달림·발사 중)이면 C·휠 입력을 카메라가 무시한다 — 팀 키 배치는 바꾸지 않는다.
/// 투석기 장전 상태는 팀 컴포넌트가 비공개(connectedBody private)라 여기서 감지하지 못한다(미해결,
/// 투석기는 기본 OFF).
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
    // 근거: 맵2_V3_릴레이설계/하18_P5카메라_산출보고서_2026-09-05.md §5(구현 스펙 초안). 릴레이
    // 질의 (마)-2 [수단 인정 여부] 판정 전까지 "후보"다. [K09] 이 토글은 이제 "뒷마당 거리 상한"만
    // 담당한다 — 충돌 당김은 구간·토글과 무관하게 항상 실행된다.
    [Header("P5 뒷마당 카메라 거리 상한 (후보 — (마)-2 판정 대기)")]
    [SerializeField] public bool yardCameraLimit = true;
    [SerializeField] private float yardMinDocY = 86f;      // 문서 Y=unity z. RoomY(V3_Build.cs)와 동일.
    [SerializeField] private float yardMaxDistance = 8f;

    [Header("충돌 당김 (실내·실외 상시 — K09)")]
    [SerializeField] private float collisionPull = 0.3f;

    private Transform target;
    private Vector3 followVelocity;
    private bool rightMouseWasHeld;

    /// <summary>[K05 검증용] 이번 프레임 카메라가 C·휠 입력을 기믹에 양보했는가.</summary>
    public bool GimmickInputSuppressed { get; private set; }

    /// <summary>[검증용] 현재 추적 대상.</summary>
    public Transform CurrentTarget => target;

    private void Awake()
    {
        // [정정 2026-09-10, 컨트롤타워 36-b A2 결정①] 고정 팔로우 카메라(PlayerFollowCamera)가
        // 이 오브젝트에 붙어 있으면 enabled=false가 아니라 파괴한다. develop
        // PlayerMover.EffectiveInputYaw()는 PlayerFollowCamera.ViewYaw가 non-null이면
        // inputYawOffset을 무시한다(§36-b 실측) — enabled=false만 하면 Awake는 그대로 돌아
        // instance=this가 서지만 Start(orbitYaw=cameraYawOffset 초기화)는 비활성 컴포넌트라
        // 돌지 않으므로, ViewYaw가 미초기화 orbitYaw(0)를 계속 돌려주고 방향키 보정이 0도로
        // 죽는다. 파괴하면 PFC.OnDestroy가 instance=null을 세워 ViewYaw가 다시 null로 돌아가
        // PlayerMover가 기존 inputYawOffset(아래 LateUpdate가 매 프레임 갱신)으로 폴백한다.
        // V3_Play.cs SetupPlay가 에디터 단계에서 이미 DestroyImmediate로 걷어내므로 정상 경로에선
        // 여기 걸릴 일이 없다 — 다른 경로로 PFC가 붙어 있을 때의 방어선이다.
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

    /// <summary>[K05] 활성 대상의 몸을 기믹(실타래 등)이 직접 몰고 있는가 — 그동안 C·휠은 기믹 몫.</summary>
    public static bool IsGimmickDriving(Transform activeTarget)
    {
        if (activeTarget == null) return false;
        PlayerMover mover = activeTarget.GetComponent<PlayerMover>();
        if (mover == null) return false;
        if (mover.ExternallyDriven) return true;
        return CatapultUsesInputOf(mover);   // [r3 시제품] 투석기가 이 플레이어의 입력(C·휠)을 쓰는 중이면 카메라가 양보
    }

    /// <summary>[r3 시제품 — 검증 사본 전용, 제품 미반영] 팀 투석기가 "현재 플레이어의 입력을 사용 중"인지.
    /// 팀 상태가 비공개(connectedMover·dockedMover)라 리플렉션으로 읽기만 한다 — 팀 API가 생기면 그 창구로 교체.
    /// 의미 구분: 당김줄 연결(비율 0 포함, Arm.State Idle이어도 연결 상태) · 조향석 도킹 · 버킷 탑승(공개 OccupantBody).</summary>
    public static bool CatapultUsesInputOf(PlayerMover mover)
    {
        const System.Reflection.BindingFlags NP = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        foreach (MonoBehaviour mb in FindObjectsOfType<MonoBehaviour>())
        {
            System.Type t = mb.GetType();
            if (t.Name == "CatapultLoadController")
            {
                var f = t.GetField("connectedMover", NP);
                if (f != null && ReferenceEquals(f.GetValue(mb), mover)) return true;
            }
            else if (t.Name == "CatapultSteerHandle")
            {
                var f = t.GetField("dockedMover", NP);
                if (f != null && ReferenceEquals(f.GetValue(mb), mover)) return true;
            }
            else if (t.Name == "CatapultBucket")
            {
                var p = t.GetProperty("OccupantBody");
                Rigidbody rb = p != null ? p.GetValue(mb) as Rigidbody : null;
                if (rb != null && rb.GetComponent<PlayerMover>() == mover) return true;
            }
        }
        return false;
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

        GimmickInputSuppressed = IsGimmickDriving(PlayerControlSwitcher.ActiveTarget);

        if (Input.GetKey(KeyCode.Q)) yaw -= keyboardYawSpeed * Time.unscaledDeltaTime;
        if (Input.GetKey(KeyCode.E)) yaw += keyboardYawSpeed * Time.unscaledDeltaTime;
        if (Input.GetKey(KeyCode.Z)) pitch += keyboardPitchSpeed * Time.unscaledDeltaTime;
        if (!GimmickInputSuppressed && Input.GetKey(KeyCode.C)) pitch -= keyboardPitchSpeed * Time.unscaledDeltaTime;
        if (Input.GetKeyDown(KeyCode.V)) { yaw = 0f; pitch = 18f; distance = 9.5f; }

        if (!GimmickInputSuppressed)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
                distance = Mathf.Clamp(distance - scroll * zoomSpeed, minDistance, maxDistance);
        }

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

        // [후보 구현] 뒷마당(문서 y>=86) 구간 한정 거리 상한 — Update()의 Mathf.Clamp는 마우스 휠
        // 입력이 있을 때만 실행되므로, 이미 늘어난 distance를 매 프레임 강제로 줄이려면 여기서
        // 별도로 클램프해야 한다. 실내(z<86)이거나 토글이 꺼져 있으면 이 블록에 진입하지 않는다 —
        // 구간 밖 복귀 시 distance 값은 그대로 유지되고 유효 상한은 원래 maxDistance(30)로 복원된다.
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

        // [K09] 충돌 당김 — 실내·실외 상시. SmoothDamp가 끝난 뒤 실제로 화면에 쓰일 위치만 당김
        // 결과로 덮어쓴다. followVelocity는 위 SmoothDamp 호출이 이미 원래 desiredPosition 기준으로
        // 갱신했으므로 여기서 다시 건드리지 않는다(다음 프레임 감쇠가 당김으로 오염되지 않게).
        transform.position = ApplyCollisionPull(lookPoint, transform.position);
        transform.rotation = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
    }

    // [충돌 당김 — 실내·실외 상시(K09)]
    // 레이어 마스크는 쓰지 않는다 — 프로젝트 어디에도 레이어 설정이 없다(검문 1차 관찰 2). 대신
    // 히트된 콜라이더의 transform이 target(플레이어 루트) 자신이거나 그 자식(Player_Collider 등)이면
    // 자기 자신으로 보고 무시한다. Linecast(최근접 1개)가 아니라 RaycastAll을 쓰는 이유: 최근접
    // 히트가 자기 자신이면 뒤쪽의 진짜 차폐물을 못 보고, 스케일 확대 시 lookPoint가 몸 안으로 들어올
    // 수 있다(검문 C1). 트리거 콜라이더(Player_Mesh 등)는 QueryTriggerInteraction.Ignore로 제외.
    private Vector3 ApplyCollisionPull(Vector3 lookPoint, Vector3 candidatePosition)
    {
        Vector3 delta = candidatePosition - lookPoint;
        float segmentLength = delta.magnitude;
        if (segmentLength < 0.0001f) return candidatePosition;
        Vector3 direction = delta / segmentLength;

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

        // lookPoint 쪽으로 collisionPull(0.3)만큼 당긴다. minDistance(3) 미만이 되어도 허용(벽에 바짝
        // 붙는 경우를 막지 않는다). 당김이 lookPoint 자체를 지나치지 않게 바닥은 nearClipPlane(0.1f)
        // — 거리 0이면 LookRotation이 영벡터를 받아 에러를 낸다(검문 C2).
        float pulledDistance = Mathf.Max(0.1f, nearestDistance - collisionPull);
        return lookPoint + direction * pulledDistance;
    }

    /// <summary>[K09 검증용] 지금 시선점→카메라 선분이 자기 자신·트리거를 제외한 콜라이더에 막혀
    /// 있는가. 막혀 있으면 hitName에 그 이름.</summary>
    public bool IsViewOccluded(out string hitName)
    {
        hitName = null;
        if (target == null) return false;
        Vector3 lookPoint = target.position + Vector3.up * targetHeight;
        Vector3 delta = transform.position - lookPoint;
        float len = delta.magnitude;
        if (len < 0.0001f) return false;
        RaycastHit[] hits = Physics.RaycastAll(lookPoint, delta / len, len, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            if (h.transform == target || h.transform.IsChildOf(target)) continue;
            if (h.distance < nearest) { nearest = h.distance; hitName = h.transform.name; }
        }
        return hitName != null;
    }

    private void SnapToTarget()
    {
        if (target == null) return;
        Vector3 lookPoint = target.position + Vector3.up * targetHeight;
        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 snapped = lookPoint + orbit * (Vector3.back * distance);
        // [K09] 시작·Tab 전환 직후 첫 프레임부터 벽 뒤로 나가지 않게 스냅 위치에도 당김을 적용한다.
        transform.position = ApplyCollisionPull(lookPoint, snapped);
        transform.rotation = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
    }

    private static void ReleaseMouse()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
