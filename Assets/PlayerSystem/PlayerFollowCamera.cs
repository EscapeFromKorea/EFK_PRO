using UnityEngine;

/// <summary>
/// 활성 플레이어(PlayerControlSwitcher가 현재 조작권을 준 플레이어)를 부드럽게 따라가는 3인칭
/// 팔로우 카메라. 씬의 기존 Main Camera 오브젝트에 이 스크립트를 부착해 쓴다 — 카메라 오브젝트
/// 자체는 PlayerSystem 밖의 씬 자산이므로 새 카메라를 만들지 않고 우리 스크립트만 붙인다.
///
/// [구르기 회전에 휩쓸리지 않기 — 이 스크립트의 핵심 설계 의도]
/// 토크 모드 플레이어(정육면체/정사면체)는 Root가 실제로 회전하며 굴러간다. 카메라가 타깃의
/// 회전을 그대로 물려받으면 화면이 통째로 빙글빙글 돌아 멀미가 난다. 그래서 이 카메라는
/// 타깃의 "위치"만 읽고(offset은 타깃의 로컬 축이 아니라 월드 공간에 고정), 회전은 타깃의
/// 회전과 완전히 분리해 "카메라 -> 타깃 위치"를 월드 up 기준으로 바라보는 방향으로만 정한다.
/// offset이 월드 고정이라 시선 방향도 사실상 일정하게 유지되어 구르기 회전에 휩쓸리지 않는다.
///
/// 타깃 갱신은 PlayerControlSwitcher가 Tab으로 활성 플레이어를 바꿀 때 SetActiveTarget()으로
/// 밀어준다. 부착/실행 순서와 무관하게 동작하도록, 이 카메라도 Start에서 스위처의 현재 활성
/// 타깃을 한 번 당겨온다(양방향 보정).
///
/// [멀미 완화 2차 조치 — 위치는 부드러운데 회전은 즉각 반응하던 불일치]
/// 정육면체/정사면체는 모서리를 피벗 삼아 실제로 굴러가므로, Root의 "위치" 자체가 물리적으로
/// 정상적인 결과로 매 모서리마다 위아래로 통통 튄다(회전과 달리 이건 실제 이동이라 그대로 두면
/// 화면이 흔들린다). 기존에는 위치만 SmoothDamp로 지연시키고 회전(LookRotation)은 그 튀는
/// 타깃을 매 프레임 즉시 스냅해 바라봤는데, "부드럽게 지연된 위치 + 즉각 반응하는 회전"의
/// 불일치 자체가 출렁이는 느낌을 더했다. 그래서 타깃의 Y만 더 강하게 감쇠해 상하 튐을
/// 완화했다(verticalDampingMultiplier).
/// [2026-09-15 정정] 회전도 한때 Slerp(lookSmoothness)로 지연시켰으나, 그건 이 시점(마우스 궤도
/// 도입 전, position 전체가 SmoothDamp)의 조치였다. 이후 mnppi가 즉시-반영 orbit offset을
/// 추가하며 "position은 마우스에 즉각 반응·회전만 지연"이라는 새 불일치가 생겨 마우스 회전이
/// 버벅이게 됐다 — 제거하고 즉시 LookRotation을 대입한다(LateUpdate 주석 참고).
///
/// [mnppi 추가 — 마우스 궤도 회전 (feat/mnppi-orbit-cam #75, 박진수 승인 2026-09-03)]
/// offset을 고정 yaw(cameraYawOffset) 하나로만 돌리던 것을, 마우스로 누적하는 궤도 각도
/// orbitYaw/orbitPitch로 돌리게 확장했다. 여전히 타깃의 "회전"은 읽지 않으므로(궤도 각도는
/// 카메라 자신의 상태다) 구르기 멀미 방지 특성은 그대로다. enableMouseOrbit=false면 Start의
/// 초기화(orbitYaw=cameraYawOffset, orbitPitch=0)만 적용돼 기존 고정 팔로우와 동일하게 동작한다.
/// </summary>
public class PlayerFollowCamera : MonoBehaviour
{
    private static PlayerFollowCamera instance;

    [Header("타깃")]
    [Tooltip("비워두면 PlayerControlSwitcher가 활성 플레이어를 자동으로 넣어준다. 스위처가 없는 " +
             "씬에서 단독으로 쓰려면 여기에 따라갈 대상을 직접 지정한다.")]
    public Transform target;

    [Header("따라가기")]
    [Tooltip("타깃 기준 카메라 위치 오프셋(월드 공간). 예: (0, 4, -10) = 뒤/위에서 내려다봄. " +
             "cameraYawOffset만큼 회전해 적용된다. 월드 공간 고정이라 플레이어가 굴러도(회전해도) " +
             "시점이 휩쓸리지 않는다.\n" +
             "[2026-09-15] Y/Z 비율이 곧 '피치로 이 오프셋을 얼마나 돌리면 카메라가 타깃 정수직 " +
             "위에 오는가'를 정한다(그 각도 = atan(|Z|/Y), 극점 — 그 근방에서 카메라 yaw가 수학적으로 " +
             "정의가 안 돼 실측(Loki)으로 화면이 튀는 걸 확인했다). Y를 6→4로 낮춰 극점을 59°→68°로 " +
             "밀어내고 maxPitch(아래)와 같이 여유를 벌었다 — Y만 더 낮추면 여유는 늘지만 카메라가 " +
             "그만큼 덜 높은 위치에서 내려다보게 돼 기본 시점 느낌이 바뀐다.")]
    public Vector3 offset = new Vector3(0f, 4f, -10f);

    [Tooltip("[mnppi] 이제 '초기 yaw'다 — Start에서 orbitYaw의 시작값으로 쓰인다. enableMouseOrbit이 " +
             "켜져 있으면 이후 마우스로 orbitYaw가 바뀌고, 꺼져 있으면 이 값에 고정된다(기존 동작). " +
             "방향키 보정(PlayerMover.inputYawOffset)과 시점을 맞추는 값. 방향이 반대면 -90으로 뒤집는다.")]
    public float cameraYawOffset = 90f;

    [Tooltip("위치 추적 부드러움(SmoothDamp 시간, 초). 작을수록 즉각적이고, 클수록 부드럽지만 느리다. " +
             "[2026-09-15] 0.2→0.12→0.07로 낮춰 카메라가 플레이어를 더 즉각적으로 따라가게 했다.")]
    public float followSmoothness = 0.07f;

    [Tooltip("타깃의 Y(상하) 위치만 추가로 감쇠하는 배수(1 = X/Z와 동일). 정육면체/정사면체가 " +
             "모서리를 넘을 때마다 Root가 실제로 위아래로 튀는데, 이 값을 키우면 카메라가 그 상하 " +
             "튐을 덜 따라가 화면이 덜 울렁인다. 너무 크면 계단/점프처럼 진짜 높이 변화에도 늦게 반응한다.")]
    public float verticalDampingMultiplier = 3f;

    [Tooltip("시선이 향하는 지점을 타깃 위치에서 이만큼 위로 올린다(발밑이 아니라 몸통을 보게).")]
    public float lookHeightOffset = 1f;

    // ═══════════ [mnppi 추가 시작] 마우스 궤도 회전 — feat/mnppi-orbit-cam (#75), 박진수 승인 2026-09-03 ═══════════
    // 원신/ZZZ식 3인칭: 마우스로 타깃 주변을 yaw/pitch 궤도 회전한다. 타깃의 "위치"만 읽는 기존 설계는
    // 그대로다 — 궤도 각도(orbitYaw/orbitPitch)는 카메라 자신의 상태이고 마우스로만 바뀌므로,
    // 구르는 도형의 회전에 화면이 휩쓸리지 않는 특성이 유지된다. cameraYawOffset은 이제 "초기 yaw"다.
    [Header("[mnppi] 마우스 궤도 회전")]
    [Tooltip("끄면 기존 고정 팔로우와 동일하게 동작한다(orbitYaw = cameraYawOffset 고정, 마우스 입력 무시).")]
    public bool enableMouseOrbit = true;
    [Tooltip("마우스 이동 1당 회전 각도(도) 계수.")]
    public float mouseSensitivity = 2f;
    [Tooltip("상하(pitch) 회전 하한(도). 이 밑으로는 안 내려간다.")]
    public float minPitch = -50f;
    [Tooltip("상하(pitch) 회전 상한(도). 이 위로는 안 올라간다(뒤집힘 방지).\n" +
             "[2026-09-15] offset의 Y/Z 비율이 만드는 극점(atan(|offset.z|/offset.y), 현재 설정에선 " +
             "68°)보다 충분히 낮게 잡아야 한다 — 극점 근방(수평 성분이 이동 지연 잡음(대략 " +
             "moveSpeed×followSmoothness ≈ 1유닛) 정도로 작아지는 구간)에서는 위를 봐도 안 봐도 " +
             "화면이 스스로 요동친다. 75→60으로 낮춰 극점까지 8° 여유(수평 성분 기준 1.5유닛 이상)를 " +
             "벌었다 — 이 값을 다시 올리려면 offset도 같이 넓혀 극점을 더 밀어내야 한다.")]
    public float maxPitch = 60f;
    [Tooltip("마우스 Y축 반전.")]
    public bool invertY = false;
    [Tooltip("플레이 시작 시 커서를 화면 중앙에 고정(숨김). Game 뷰 클릭 시 재고정, Esc로 해제.")]
    public bool lockCursorOnPlay = true;
    [Tooltip("마우스 델타에 거는 저역통과 필터 시간(초, SmoothDamp). 고주사율 마우스나 프레임타임 " +
             "편차로 생기는 델타 자체의 미세 떨림만 걸러내려는 값이라 아주 짧게 잡는다 — 0이면 필터 " +
             "없이 원래 델타를 그대로 쓴다. 크게 잡으면 조준 반응이 늦어져 방금 없앤 것과 같은 " +
             "종류의 '버벅임'이 다른 형태로 되살아난다(2026-09-15 추가).")]
    public float mouseDeltaSmoothingTime = 0.03f;

    private float orbitYaw;    // 누적 yaw(도). Start에서 cameraYawOffset으로 초기화.
    private float orbitPitch;  // 누적 pitch(도). minPitch~maxPitch로 클램프.
    private float smoothedMouseX, smoothedMouseY;         // 저역통과 필터를 거친 프레임당 델타.
    private float mouseXVelocity, mouseYVelocity;         // SmoothDamp 내부 속도 누산기.

    /// <summary>PlayerMover가 카메라 상대 이동에 쓰는 현재 시점 yaw(도). 씬에 이 카메라가 없으면
    /// null → PlayerMover가 기존 inputYawOffset로 폴백한다.</summary>
    public static float? ViewYaw => instance != null ? instance.orbitYaw : (float?)null;

    /// <summary>씬에 이 카메라가 있고 마우스 궤도 회전이 켜져 있는가. PlayerMover가 "궤도 카메라가
    /// 활성이면 이동도 카메라 상대"를 자동으로 성립시키는 데 쓴다(둘은 한 세트 — 궤도 카메라 +
    /// 월드축 고정 이동은 방향이 어긋나 못 쓴다).</summary>
    public static bool MouseOrbitActive => instance != null && instance.enableMouseOrbit;

    // 타깃 추적 지점(offset 적용 전). 마우스로 시점을 홱 돌릴 때 카메라 최종 위치 전체를 SmoothDamp하면
    // 궤도 원의 현을 가로질러 미끄러지므로, 추적 지점만 SmoothDamp하고 offset은 즉시 얹는다.
    private Vector3 smoothedFollowPoint;
    // ═══════════ [mnppi 추가 끝] ═══════════

    // ═══════════ [SpacePortalSystem 추가] 조준 모드 — 2026-09-10 교차 폴더 수정 허가됨(PRD §4.3) ═══════════
    // 에너지볼을 든 플레이어가 우클릭으로 포탈을 조준하는 동안 어깨너머 시점으로 잠깐 바꾼다.
    // 타깃의 "위치"만 읽는 기존 설계(클래스 상단 주석)는 전혀 건드리지 않는다 — offset을 aimOffset으로
    // 부드럽게 블렌드하는 것뿐이라, 구르기 멀미 방지 특성이 그대로 유지된다.
    [Header("[SpacePortalSystem] 조준 모드")]
    [Tooltip("우클릭 조준 중 offset 대신 쓸 값(어깨너머 시점, 월드 공간). PlayerEnergyReceiver.cs가 " +
             "EnterAimMode()/ExitAimMode()로 전환을 요청한다.")]
    public Vector3 aimOffset = new Vector3(1.2f, 2.0f, -3.5f);
    [Tooltip("평소 시점 ↔ 조준 시점을 블렌드하는 데 걸리는 시간(초).")]
    public float aimTransitionSeconds = 0.25f;

    private bool aiming;
    private float aimBlend; // 0 = 평소, 1 = 완전 조준

    /// <summary>SpacePortalSystem/PlayerEnergyReceiver.cs가 우클릭을 누르는 동안 호출한다.</summary>
    public static void EnterAimMode() { if (instance != null) instance.aiming = true; }

    /// <summary>우클릭을 떼거나 조준 권한을 잃으면 호출한다.</summary>
    public static void ExitAimMode() { if (instance != null) instance.aiming = false; }
    // ═══════════ [SpacePortalSystem 추가 끝] ═══════════

    // ═══════════ [PortalSystem 추가] 굴리기 모드 카메라 안정화 — 2026-09-12 교차 폴더 수정 허가됨 ═══════════
    // 굴리기(모서리 텀블) 중엔 몸의 실제 위치가 회전 때문에 매 칸 위아래로 튄다. PlayerRollModeReceiver
    // (PortalSystem)가 "바닥 높이 고정 + 등속 수평 보간"으로 계산한 가상 기준점을 SetTrackingAnchor로
    // 매 FixedUpdate 밀어주면, 그 값을 target.position 대신(또는 블렌드해서) 따라간다. 이 클래스는
    // PortalSystem의 존재를 전혀 모른다 — EnterAimMode/ExitAimMode와 같은 기믹→코어 단방향 훅이다.
    [Header("[PortalSystem] 굴리기 모드 카메라 안정화")]
    [Tooltip("굴리기 모드 중 몸의 실제 위치 대신 PlayerRollModeReceiver가 밀어주는 기준점을 따라간다. " +
             "꺼도 SetTrackingAnchor 호출 자체는 무해하다(그냥 무시).")]
    public bool useRollModeCameraAnchor = true;
    [Tooltip("굴리기 모드 진입(또는 그 몸으로 Tab 복귀) 시 기존 추적 → 기준점 추적으로 넘어가는 " +
             "블렌드 시간(초). 0에 가까울수록 즉각 전환된다.")]
    public float rollAnchorEnterSeconds = 0.15f;
    [Tooltip("굴리기 모드 이탈(낙하로 해제 등) 시 기준점 추적 → 기존 추적으로 되돌아오는 블렌드 " +
             "시간(초). 되돌아오는 쪽은 몸이 실제로 낙하하기 시작한 뒤라 너무 짧으면 그 순간 튄다.")]
    public float rollAnchorExitSeconds = 0.25f;

    private Transform anchorOwner;   // SetTrackingAnchor를 마지막으로 부른 대상
    private Vector3? rollAnchor;     // 그 대상이 지금 밀어주는 값(null = 추적 끔)
    private Vector3 lastAnchorPoint; // 이탈 블렌드 동안 유지할 마지막 값(rollAnchor가 null이 된 뒤에도 보존)
    private bool anchorEverSet;
    private float rollBlend;         // 0 = target.position 그대로, 1 = 기준점 그대로

    /// <summary>[PortalSystem 등] 몸의 실제 위치 대신 가상 기준점을 보여주고 싶은 기믹이 매 FixedUpdate
    /// 부른다. owner가 지금 카메라의 target과 다르면(Tab으로 다른 플레이어를 보고 있음) 조용히
    /// 무시된다 — 남의 텀블이 내 화면에 끼어들지 않는다. anchor에 null을 넘기면 그 owner의 추적을
    /// 끈다(이후 rollAnchorExitSeconds에 걸쳐 기존 추적으로 되돌아간다).</summary>
    public static void SetTrackingAnchor(Transform owner, Vector3? anchor)
    {
        if (instance == null) return;
        instance.anchorOwner = owner;
        instance.rollAnchor = anchor;
    }
    // ═══════════ [PortalSystem 추가 끝] ═══════════

    private Vector3 followVelocity;
    private float smoothedTargetY;
    private float targetYVelocity;

    // ═══════════ [진단 전용, 2026-09-15] "위를 볼 때 시점이 계속 바뀐다" 제보 조사 — Grafana/Loki ═══════════
    // 의심 지점: dir(카메라→lookPoint)이 피치가 커질수록 Vector3.up에 가까워지고, LookRotation은
    // dir이 up과 거의 나란해지면 수평 성분(분모 역할)이 아주 작아져 target Y의 미세한 잡음(SmoothDamp
    // 수렴 오차·접지 튐)조차 결과 yaw를 크게 흔들 수 있다 — 가설일 뿐이라 고치지 않고 실측부터 한다.
    // 굴리기 모드(rollBlend>0)는 PortalSystem이 미는 기준점까지 섞여 원인 후보가 늘어나므로 뺀다
    // (사용자 지시 — 일반 이동 상태 기준). 조사가 끝나면 이 블록과 호출부를 통째로 지운다.
    [Header("[진단 전용] 카메라 시점 로그 (Grafana/Loki)")]
    [Tooltip("일반 이동 상태(굴리기 앵커 블렌드 밖)에서 주기적으로 카메라 방향 진단값을 Loki로 " +
             "보낸다. 조사용 — 기본 꺼짐, 재현 중에만 켠다.")]
    public bool logCameraDebug = false;
    [Tooltip("로그 전송 간격(초). ponytail: 매 프레임 보내면 버퍼만 커진다.")]
    public float cameraDebugLogInterval = 0.2f;

    private float nextCamDebugLogTime;
    private float lastCamEulerY;
    private bool camDebugPrimed;

    // ponytail: 0.2초(기본) 간격 throttle — PlayerEnergyReceiver.LogAimDebug와 같은 패턴.
    private void LogCameraDebug(Vector3 dir)
    {
        if (Time.time < nextCamDebugLogTime) return;
        nextCamDebugLogTime = Time.time + cameraDebugLogInterval;

        float angleFromUp = Vector3.Angle(dir.normalized, Vector3.up);
        float horizontalMag = new Vector2(dir.x, dir.z).magnitude;
        float camEulerY = transform.eulerAngles.y;
        // Mathf.DeltaAngle로 360°↔0° 랩어라운드를 실제 스핀으로 오판하지 않게 한다.
        float yawJump = camDebugPrimed ? Mathf.DeltaAngle(lastCamEulerY, camEulerY) : 0f;
        lastCamEulerY = camEulerY;
        camDebugPrimed = true;

        LokiTelemetry.Event("cam_view_debug",
            $"yaw={orbitYaw:F1} pitch={orbitPitch:F1} dirY={dir.y:F3} dirXZ={horizontalMag:F3} " +
            $"angFromUp={angleFromUp:F2} camEulerY={camEulerY:F2} yawJump={yawJump:F2} " +
            $"targetY={target.position.y:F3} smoothY={smoothedTargetY:F3}");
    }
    // ═══════════ [진단 전용 끝] ═══════════

    void Awake()
    {
        instance = this;
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    void Start()
    {
        // 스위처가 Awake에서 이미 활성 타깃을 정했다면 그것을 당겨온다(부착 순서와 무관하게 동작).
        if (target == null)
            target = PlayerControlSwitcher.ActiveTarget;

        if (target != null)
        {
            smoothedTargetY = target.position.y;
            smoothedFollowPoint = target.position;   // [mnppi] 첫 프레임 카메라가 원점에서 날아오지 않게
        }

        // ─── [mnppi 추가] 궤도 각도 초기화 + 커서 락 ───
        // orbitYaw를 cameraYawOffset으로 시작하면 마우스를 안 움직인 첫 프레임의 위치/시선이
        // 기존 고정 팔로우와 정확히 같다(회귀 없음). orbitPitch=0이라 offset의 위/뒤 각도가 그대로 유지된다.
        orbitYaw = cameraYawOffset;
        orbitPitch = 0f;
        if (lockCursorOnPlay)
            SetCursorLocked(true);
        // ─── [mnppi 추가 끝] ───
    }

    void LateUpdate()
    {
        if (target == null) return;

        // ─── [mnppi 추가] 마우스 입력 → 궤도 각도 갱신 + 커서 락 유지 ───
        if (enableMouseOrbit)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                SetCursorLocked(false);
            else if (lockCursorOnPlay && Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
            {
                SetCursorLocked(true);
                // 잠금 해제 동안 멈춰 있던 필터 상태를 지운다 — 안 지우면 재잠금 첫 프레임에 그
                // 사이 쌓인 델타/속도가 갑자기 반영돼 시점이 한 번 튄다.
                smoothedMouseX = smoothedMouseY = mouseXVelocity = mouseYVelocity = 0f;
            }

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                float rawX = Input.GetAxis("Mouse X") * mouseSensitivity;
                float rawY = Input.GetAxis("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);
                // 델타 자체를 SmoothDamp로 걸러 미세 떨림만 죽인다(방향 자체를 지연시키는 게
                // 아니라 신호를 다듬는 것이라 위 회전 즉시-대입 수정과 충돌하지 않는다).
                if (mouseDeltaSmoothingTime > 0f)
                {
                    smoothedMouseX = Mathf.SmoothDamp(smoothedMouseX, rawX, ref mouseXVelocity, mouseDeltaSmoothingTime);
                    smoothedMouseY = Mathf.SmoothDamp(smoothedMouseY, rawY, ref mouseYVelocity, mouseDeltaSmoothingTime);
                }
                else
                {
                    smoothedMouseX = rawX;
                    smoothedMouseY = rawY;
                }
                orbitYaw += smoothedMouseX;
                orbitPitch += smoothedMouseY;
                orbitPitch = Mathf.Clamp(orbitPitch, minPitch, maxPitch);
            }
        }
        // ─── [mnppi 추가 끝] ───

        // ─── [PortalSystem 추가] 굴리기 기준점 블렌드 ───
        // rollAnchor는 owner(그걸 미는 기믹의 트랜스폼)가 지금 target과 같을 때만 반영한다 — Tab으로
        // 다른 플레이어를 보고 있으면 그 사람의 텀블이 여기 안 끼어든다. rollAnchor가 null이 된
        // 뒤에도 lastAnchorPoint는 보존해 rollBlend가 rollAnchorExitSeconds에 걸쳐 0으로 빠지는
        // 동안 블렌드 대상이 사라져 순간적으로 raw 추적으로 튀는 일이 없게 한다.
        bool anchorRequested = useRollModeCameraAnchor && rollAnchor.HasValue && anchorOwner == target;
        if (anchorRequested) { lastAnchorPoint = rollAnchor.Value; anchorEverSet = true; }
        float rollBlendStep = Time.deltaTime / Mathf.Max(0.001f, anchorRequested ? rollAnchorEnterSeconds : rollAnchorExitSeconds);
        rollBlend = Mathf.MoveTowards(rollBlend, anchorRequested ? 1f : 0f, rollBlendStep);

        // 굴리기 모드 밖(rollBlend == 0)에서는 followSourceRaw == target.position이라 이하 계산이
        // 기존과 완전히 동일하다 — 회귀 없음(요구사항 "굴리기 모드 밖에서 같은 궤적").
        Vector3 followSourceRaw = target.position;
        if (rollBlend > 0f && anchorEverSet)
            followSourceRaw = Vector3.Lerp(followSourceRaw, lastAnchorPoint, rollBlend);
        // ─── [PortalSystem 추가 끝] ───

        // 타깃 Y(상하)만 더 강하게 감쇠한 뒤 위치/시선 계산 모두에 이 값을 쓴다 — 모서리를 넘을 때
        // 마다 생기는 실제 물리적 상하 튐을 완화한다(회전을 무시하는 것과 별개의 조치).
        float verticalTime = followSmoothness * Mathf.Max(1f, verticalDampingMultiplier);
        smoothedTargetY = Mathf.SmoothDamp(smoothedTargetY, followSourceRaw.y, ref targetYVelocity, verticalTime);
        Vector3 smoothedTargetPos = new Vector3(followSourceRaw.x, smoothedTargetY, followSourceRaw.z);

        // 위치: offset을 궤도 각도(yaw/pitch)만큼 회전시켜 적용한다.
        // 타깃의 회전은 여전히 쓰지 않으므로(월드 고정), 시점 각도만 돌아갈 뿐 구르기에 휩쓸리지 않는다.
        // [mnppi 수정] 기존: Vector3 rotatedOffset = Quaternion.AngleAxis(cameraYawOffset, Vector3.up) * offset;
        //             그리고 transform.position = SmoothDamp(transform.position, smoothedTargetPos + rotatedOffset, ...)
        //   (1) yaw만 → pitch 포함 궤도 회전(enableMouseOrbit=false여도 Start 초기화로 기존과 동일).
        //   (2) 카메라 최종 위치 전체를 SmoothDamp하면 마우스로 시점을 홱 돌릴 때 카메라가 궤도 원의
        //       현을 가로질러 미끄러져 "붕 뜨는" 이질감이 난다 → 추적 지점만 SmoothDamp하고 offset은
        //       그 위에 즉시 얹어, 궤도는 즉각 반영하되 타깃 추적 부드러움(상하 튐 감쇠 포함)은 유지.
        // [SpacePortalSystem] 조준 모드 블렌드 — offset을 aimOffset으로 부드럽게 갈아탄다.
        // aimTransitionSeconds<=0이면 즉시 전환(블렌드 없음).
        float blendStep = aimTransitionSeconds > 0f ? Time.deltaTime / aimTransitionSeconds : 1f;
        aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, blendStep);
        Vector3 effectiveOffset = aimBlend > 0f ? Vector3.Lerp(offset, aimOffset, aimBlend) : offset;

        Quaternion orbitRotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        Vector3 rotatedOffset = orbitRotation * effectiveOffset;
        smoothedFollowPoint = Vector3.SmoothDamp(smoothedFollowPoint, smoothedTargetPos, ref followVelocity, followSmoothness);
        transform.position = smoothedFollowPoint + rotatedOffset;

        // 회전: dir을 "카메라 실제 위치 → 타깃 실제 위치" 벡터로 매 프레임 재구성하지 않는다.
        // [2026-09-15, 3차 수정] 그 방식(smoothedTargetPos - transform.position)은 두 항이 서로
        // 다른 지연을 갖는다 — 위치는 smoothedFollowPoint(XYZ 전부 followSmoothness), 시선점은
        // smoothedTargetPos(X/Z는 raw, Y만 verticalDampingMultiplier로 3배 더 감쇠) — 그 차이(지연
        // 오차)가 곧 dir의 수평 성분이라, 플레이어가 그냥 걷기만 해도(방향 전환·비탈·접지 튐) 매
        // 프레임 달라지며 화면이 미세하게 울렁였다(카메라를 전혀 안 돌려도 재현 — 제보 그대로).
        // 피치가 커질 때는 이 잡음이 지오메트리상 작아지는 분모(위 offset/maxPitch 조정 참고)와
        // 겹쳐 수십~백도급으로 증폭됐던 것뿐, 원인은 같다.
        // 대신 위치 계산에 이미 쓴 rotatedOffset을 그대로 재사용해 "궤도·오프셋·lookHeightOffset만의
        // 함수"인 결정론적 방향을 쓴다 — 타깃의 실시간 위치·지연 잡음을 아예 안 읽으므로 그 무엇도
        // 화면을 흔들 수 없다. up*lookHeightOffset은 회전시키지 않는다(코드 리뷰가 잡은 이전 실수:
        // 그걸 orbitRotation과 함께 통째로 돌리면 피치가 커질수록 최대 5.9°까지 타깃 중심이
        // 어긋났다 — 여기서는 오프셋만 돌리고 up 항은 월드 고정으로 남겨 그 결함이 없다).
        // 대가: 카메라 위치 자체가 갖는 정상적인 추격 지연(smoothedFollowPoint가 실제 타깃보다
        // 항상 조금 뒤처지는 것)만큼 화면 중앙이 살짝 밀릴 수 있다 — 어떤 3인칭 추격 카메라에나
        // 있는 통상적인 지연이고, 피치에 따라 커지지 않는다(리뷰가 지적한 결함과는 성격이 다르다).
        Vector3 dir = Vector3.up * lookHeightOffset - rotatedOffset;
        const float minHorizontal = 0.05f; // 극점 근방(위 offset/maxPitch 조정 이후 이론상 도달 불가) 안전망.
        Vector2 horizontal = new Vector2(dir.x, dir.z);
        if (horizontal.sqrMagnitude < minHorizontal * minHorizontal)
        {
            Vector3 fallback = Quaternion.Euler(0f, orbitYaw, 0f) * Vector3.forward * minHorizontal;
            dir = new Vector3(fallback.x, dir.y, fallback.z);
        }
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        // [진단 전용] 일반 이동 상태(rollBlend≈0)에서만 — 굴리기 모드는 조사 범위 밖.
        if (logCameraDebug && rollBlend < 0.01f)
            LogCameraDebug(dir);
    }

    /// <summary>카메라를 지금 즉시 타깃 위치로 스냅한다(보간 없음). 타깃이 <b>순간이동</b>했을 때
    /// 쓴다 — 리스폰처럼 위치가 한 번에 크게 바뀌면 SmoothDamp가 옛 위치에서 새 위치까지 화면을
    /// 가로질러 날아가, "어디로 돌아갔는지" 대신 "이동하는 과정"이 보인다(RespawnSystem이 부른다).
    ///
    /// 보간 상태(smoothedTargetY·속도 누산기)까지 같이 지워야 한다 — 위치만 대입하면 다음
    /// LateUpdate가 남은 속도로 지나쳐 흔들리고, 감쇠된 Y가 옛 높이에서 다시 따라온다.
    ///
    /// onlyForTarget을 주면 그 대상이 지금 따라가는 타깃일 때만 스냅한다. 조작 중이 아닌 플레이어가
    /// 리스폰했다고 화면이 그쪽으로 튀면 안 되므로, 호출자가 판정을 복제하지 않게 여기서 거른다.</summary>
    public static void SnapToTarget(Transform onlyForTarget = null)
    {
        if (instance == null || instance.target == null) return;
        if (onlyForTarget != null && instance.target != onlyForTarget) return;

        Transform t = instance.target;
        instance.smoothedTargetY = t.position.y;
        instance.targetYVelocity = 0f;
        instance.followVelocity = Vector3.zero;
        instance.smoothedFollowPoint = t.position;   // [mnppi] 추적 지점도 함께 스냅(다음 LateUpdate가 여기서 SmoothDamp 시작)
        instance.rollBlend = 0f; // [PortalSystem] 순간이동 직후 옛 기준점으로 당겨지지 않게 블렌드도 초기화

        // LateUpdate의 목표 위치·시선 계산과 같은 식이다(추적 지점 + 궤도 각도만큼 돌린 offset).
        // 여기서 식이 갈라지면 스냅 직후 한 프레임 튄다.
        // [mnppi 수정] 기존: t.position + Quaternion.AngleAxis(instance.cameraYawOffset, Vector3.up) * instance.offset;
        //   → LateUpdate와 동일하게 궤도 회전(yaw/pitch) 식으로 교체.
        // [2026-09-15, 3차 수정] LateUpdate와 같은 결정론적 회전식(위 LateUpdate 주석 참고) — 여기는
        // 지연되는 추적점이 없는 순간 스냅이라 "t.position + up*h - transform.position"이 애초에
        // up*h - rotatedOffset과 대수적으로 같다(t.position이 상쇄된다). 그 사실을 식으로도 드러낸다.
        Quaternion orbitRotation = Quaternion.Euler(instance.orbitPitch, instance.orbitYaw, 0f);
        Vector3 rotatedOffset = orbitRotation * instance.offset;
        instance.transform.position = t.position + rotatedOffset;
        Vector3 dir = Vector3.up * instance.lookHeightOffset - rotatedOffset;
        const float minHorizontal = 0.05f;
        Vector2 horizontal = new Vector2(dir.x, dir.z);
        if (horizontal.sqrMagnitude < minHorizontal * minHorizontal)
        {
            Vector3 fallback = Quaternion.Euler(0f, instance.orbitYaw, 0f) * Vector3.forward * minHorizontal;
            dir = new Vector3(fallback.x, dir.y, fallback.z);
        }
        if (dir.sqrMagnitude > 0.0001f)
            instance.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    // ─── [mnppi 추가] 커서 락 헬퍼 ───
    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
    // ─── [mnppi 추가 끝] ───

    /// <summary>PlayerControlSwitcher가 활성 플레이어를 바꿀 때 호출한다. 씬에 카메라가 없으면 무시된다.</summary>
    public static void SetActiveTarget(Transform newTarget)
    {
        if (instance == null) return;
        instance.target = newTarget;

        // 전환 즉시 새 타깃의 실제 높이로 스냅 — 그러지 않으면 이전 플레이어 높이에서 새 플레이어
        // 높이까지 verticalDampingMultiplier만큼 느리게 따라잡아 전환 직후 부자연스럽게 떠 보인다.
        if (newTarget != null)
        {
            instance.smoothedTargetY = newTarget.position.y;
            instance.smoothedFollowPoint = newTarget.position;   // [mnppi] 추적 지점도 함께 스냅
            instance.rollBlend = 0f; // [PortalSystem] 새 타깃은 옛 타깃의 굴리기 기준점과 무관하다
        }
    }
}
