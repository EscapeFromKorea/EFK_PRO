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
/// 불일치 자체가 출렁이는 느낌을 더했다. 그래서 (1) 타깃의 Y만 더 강하게 감쇠해 상하 튐을
/// 완화하고(verticalDampingMultiplier), (2) 회전도 Slerp로 부드럽게 따라붙게 했다(lookSmoothness).
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
    [Tooltip("타깃 기준 카메라 위치 오프셋(월드 공간). 예: (0, 6, -10) = 뒤/위에서 내려다봄. " +
             "cameraYawOffset만큼 회전해 적용된다. 월드 공간 고정이라 플레이어가 굴러도(회전해도) " +
             "시점이 휩쓸리지 않는다.")]
    public Vector3 offset = new Vector3(0f, 6f, -10f);

    [Tooltip("[mnppi] 이제 '초기 yaw'다 — Start에서 orbitYaw의 시작값으로 쓰인다. enableMouseOrbit이 " +
             "켜져 있으면 이후 마우스로 orbitYaw가 바뀌고, 꺼져 있으면 이 값에 고정된다(기존 동작). " +
             "방향키 보정(PlayerMover.inputYawOffset)과 시점을 맞추는 값. 방향이 반대면 -90으로 뒤집는다.")]
    public float cameraYawOffset = 90f;

    [Tooltip("위치 추적 부드러움(SmoothDamp 시간, 초). 작을수록 즉각적이고, 클수록 부드럽지만 느리다.")]
    public float followSmoothness = 0.2f;

    [Tooltip("타깃의 Y(상하) 위치만 추가로 감쇠하는 배수(1 = X/Z와 동일). 정육면체/정사면체가 " +
             "모서리를 넘을 때마다 Root가 실제로 위아래로 튀는데, 이 값을 키우면 카메라가 그 상하 " +
             "튐을 덜 따라가 화면이 덜 울렁인다. 너무 크면 계단/점프처럼 진짜 높이 변화에도 늦게 반응한다.")]
    public float verticalDampingMultiplier = 3f;

    [Tooltip("시선이 향하는 지점을 타깃 위치에서 이만큼 위로 올린다(발밑이 아니라 몸통을 보게).")]
    public float lookHeightOffset = 1f;

    [Tooltip("카메라 회전이 목표 방향으로 따라붙는 속도(초당 보간 비율). 낮을수록 부드럽지만 시선이 " +
             "느리게 반응하고, 높을수록 즉각적이지만 타깃의 상하 튐이 회전에도 그대로 묻어난다.")]
    public float lookSmoothness = 8f;

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
    [Tooltip("상하(pitch) 회전 상한(도). 이 위로는 안 올라간다(뒤집힘 방지).")]
    public float maxPitch = 75f;
    [Tooltip("마우스 Y축 반전.")]
    public bool invertY = false;
    [Tooltip("플레이 시작 시 커서를 화면 중앙에 고정(숨김). Game 뷰 클릭 시 재고정, Esc로 해제.")]
    public bool lockCursorOnPlay = true;

    private float orbitYaw;    // 누적 yaw(도). Start에서 cameraYawOffset으로 초기화.
    private float orbitPitch;  // 누적 pitch(도). minPitch~maxPitch로 클램프.
    // ═══════════ [mnppi 추가 끝] ═══════════

    private Vector3 followVelocity;
    private float smoothedTargetY;
    private float targetYVelocity;

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
            smoothedTargetY = target.position.y;

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
                SetCursorLocked(true);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                orbitYaw += Input.GetAxis("Mouse X") * mouseSensitivity;
                orbitPitch += Input.GetAxis("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);
                orbitPitch = Mathf.Clamp(orbitPitch, minPitch, maxPitch);
            }
        }
        // ─── [mnppi 추가 끝] ───

        // 타깃 Y(상하)만 더 강하게 감쇠한 뒤 위치/시선 계산 모두에 이 값을 쓴다 — 모서리를 넘을 때
        // 마다 생기는 실제 물리적 상하 튐을 완화한다(회전을 무시하는 것과 별개의 조치).
        float verticalTime = followSmoothness * Mathf.Max(1f, verticalDampingMultiplier);
        smoothedTargetY = Mathf.SmoothDamp(smoothedTargetY, target.position.y, ref targetYVelocity, verticalTime);
        Vector3 smoothedTargetPos = new Vector3(target.position.x, smoothedTargetY, target.position.z);

        // 위치: offset을 궤도 각도(yaw/pitch)만큼 회전시켜 적용한 뒤 부드럽게 추적한다.
        // 타깃의 회전은 여전히 쓰지 않으므로(월드 고정), 시점 각도만 돌아갈 뿐 구르기에 휩쓸리지 않는다.
        // [mnppi 수정] 기존: Vector3 rotatedOffset = Quaternion.AngleAxis(cameraYawOffset, Vector3.up) * offset;
        //   → yaw만 돌리던 것을 pitch(상하)까지 포함한 궤도 회전으로 교체. enableMouseOrbit=false여도
        //     Start에서 orbitYaw=cameraYawOffset / orbitPitch=0이라 결과가 기존과 동일하다.
        Vector3 rotatedOffset = Quaternion.Euler(orbitPitch, orbitYaw, 0f) * offset;
        Vector3 desired = smoothedTargetPos + rotatedOffset;
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref followVelocity, followSmoothness);

        // 회전: 실제 카메라 위치에서 타깃 위치(살짝 위)를 바라보되, 위치처럼 Slerp로 부드럽게
        // 따라붙는다(예전엔 즉시 스냅 — 부드러운 위치와 즉각 회전의 불일치가 출렁임을 더했다).
        Vector3 lookPoint = smoothedTargetPos + Vector3.up * lookHeightOffset;
        Vector3 dir = lookPoint - transform.position;
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, Time.deltaTime * lookSmoothness);
        }
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

        // LateUpdate의 목표 위치·시선 계산과 같은 식이다(offset을 궤도 각도만큼 돌린 지점에서
        // 몸통을 본다). 여기서 식이 갈라지면 스냅 직후 한 프레임 튄다.
        // [mnppi 수정] 기존: t.position + Quaternion.AngleAxis(instance.cameraYawOffset, Vector3.up) * instance.offset;
        //   → LateUpdate와 동일하게 궤도 회전(yaw/pitch) 식으로 교체.
        instance.transform.position =
            t.position + Quaternion.Euler(instance.orbitPitch, instance.orbitYaw, 0f) * instance.offset;
        Vector3 dir = t.position + Vector3.up * instance.lookHeightOffset - instance.transform.position;
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
            instance.smoothedTargetY = newTarget.position.y;
    }
}
