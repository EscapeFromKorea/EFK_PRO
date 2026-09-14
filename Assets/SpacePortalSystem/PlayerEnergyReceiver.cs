using UnityEngine;

/// <summary>
/// 플레이어 쪽 수신자(리시버는 반드시 이 폴더 안에 둔다 — <c>Assets/PortalSystem/CLAUDE.md</c>가
/// 이미 지적한 "Player~ 이름이라고 PlayerSystem에 두지 않는다" 함정을 반복하지 않는다).
///
/// 소지(<see cref="CarriedBall"/>)/설치 소유(<see cref="InstalledColor"/>)/조준/회수 상태를 전부
/// 이 컴포넌트가 갖는다(PRD §4.2). <see cref="EnergyBall"/>이 F 획득 시 그 자리에서 AddComponent한다
/// (리시버는 포탈이 즉석에서 붙이는 <c>Portal.cs</c> 관례와 동일).
/// </summary>
[RequireComponent(typeof(PlayerMover))]
public class PlayerEnergyReceiver : MonoBehaviour
{
    [Header("몸의 빛(글로우) — 소지 중에만 표시")]
    [Tooltip("공유 머티리얼을 오염시키지 않으려고 MaterialPropertyBlock으로 _Color만 덮어쓴다 " +
             "(RespawnZone 깃발과 같은 방식). 진짜 발광(Emission)은 아니다 — 그레이박스 단계의 " +
             "색 태그이며, 나중에 진짜 발광이 필요하면 이 메서드만 바꾸면 된다.")]
    public Color orangeGlowColor = new Color(1f, 0.55f, 0.1f);
    public Color blueGlowColor = new Color(0.25f, 0.55f, 1f);

    [Header("조준")]
    public float aimRange = 30f;
    [Tooltip("패널 가장자리에서 포탈 풋프린트까지 남겨야 하는 여유(Unit).")]
    public float edgeMargin = 0.1f;
    [Tooltip("겹침 판정용 박스의 두께(Unit, 포탈 평면 법선 방향).")]
    public float overlapCheckDepth = 0.5f;

    [Header("회수 (R 길게 누름)")]
    public float recallHoldSeconds = 2f;

    /// <summary>지금 들고 있는 볼(없으면 null).</summary>
    public EnergyBall CarriedBall { get; private set; }

    /// <summary>이 플레이어가 설치해 필드에 살아있는 포탈의 색(없으면 null).</summary>
    public EnergyColor? InstalledColor { get; private set; }

    public bool IsControlledByLocalPlayer => mover == null || mover.IsControlled;

    private PlayerMover mover;
    private Renderer[] visualRenderers;
    private MaterialPropertyBlock glowBlock;

    private bool aiming;
    private bool placementValid;
    private PortalSurface aimSurface;
    private Vector3 aimCenter, aimNormal, aimUp;

    private GameObject silhouette;
    private MeshRenderer silhouetteRenderer;

    // ponytail: PortalSurface 인식 문제 진단용 임시 계측(Loki로 전송). 원인 확정되면 지운다.
    private float nextAimLogTime;

    private float rHoldTimer;
    private bool rRecallFired;

    private void Awake()
    {
        mover = GetComponent<PlayerMover>();
        visualRenderers = GetComponentsInChildren<Renderer>();
        glowBlock = new MaterialPropertyBlock();
    }

    private void OnDestroy()
    {
        if (silhouette != null) Destroy(silhouette);
    }

    private void Update()
    {
        if (!IsControlledByLocalPlayer)
        {
            // 조작권을 잃은 동안엔 입력을 전부 무시한다(저장소 관용구 — PlayerJump 등과 동일).
            if (aiming) StopAiming();
            rHoldTimer = 0f;
            rRecallFired = false;
            return;
        }

        HandleR();
        HandleAim();
    }

    /// <summary>EnergyBall이 근접 F 판정에서 호출한다.</summary>
    public void TryPickup(EnergyBall ball)
    {
        if (ball == null) return;
        if (CarriedBall != null)
        {
            Debug.Log($"[SpacePortal] 이미 {CarriedBall.color} 에너지볼을 들고 있어 {ball.color}를 주울 수 없다.");
            return;
        }

        CarriedBall = ball;
        ball.SetCarried(this);
        ApplyGlow(ball.color);
        Debug.Log($"[SpacePortal] {ball.color} 에너지볼 획득.");
    }

    // ── R: 드랍 / 회수 ───────────────────────────────────────────────────────

    private void HandleR()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            rHoldTimer = 0f;
            rRecallFired = false;
        }

        if (Input.GetKey(KeyCode.R))
        {
            rHoldTimer += Time.deltaTime;
            if (!rRecallFired && rHoldTimer >= recallHoldSeconds)
            {
                rRecallFired = true;
                TryRecall();
            }
        }

        if (Input.GetKeyUp(KeyCode.R))
        {
            if (!rRecallFired) TryDrop();
            rHoldTimer = 0f;
            rRecallFired = false;
        }
    }

    private void TryDrop()
    {
        if (CarriedBall == null) return;

        EnergyBall ball = CarriedBall;
        CarriedBall = null;
        ball.SetGround(transform.position + transform.forward * 1f);
        ApplyGlow(null);
        Debug.Log($"[SpacePortal] {ball.color} 에너지볼 드랍.");
    }

    private void TryRecall()
    {
        if (!InstalledColor.HasValue) return;
        if (CarriedBall != null)
        {
            // TryPickup과 같은 가드 — 이미 다른 색을 들고 있으면 회수된 볼을 담을 자리가 없다.
            // 가드 없이 CarriedBall을 덮어쓰면 원래 들고 있던 볼의 참조가 사라져(State는 계속
            // Carried로 남아 Ground로도 못 돌아옴) 레벨에 유일한 그 색 볼이 영구 봉인된다.
            Debug.Log($"[SpacePortal] 이미 {CarriedBall.color} 에너지볼을 들고 있어 포탈을 회수할 수 없다. 먼저 드랍하거나 설치해라.");
            return;
        }

        EnergyColor color = InstalledColor.Value;
        SpacePortal portal = color == EnergyColor.Orange ? SpacePortal.Orange : SpacePortal.Blue;
        InstalledColor = null;
        if (portal != null) Destroy(portal.gameObject);

        EnergyBall ball = FindBallOfColor(color);
        if (ball != null)
        {
            CarriedBall = ball;
            ball.SetCarried(this);
            ApplyGlow(color);
        }
        Debug.Log($"[SpacePortal] {color} 포탈 회수 완료.");
    }

    private static EnergyBall FindBallOfColor(EnergyColor color)
    {
        foreach (EnergyBall b in Object.FindObjectsOfType<EnergyBall>())
            if (b.color == color) return b;
        return null;
    }

    // ── 조준 / 설치 ──────────────────────────────────────────────────────────

    private void HandleAim()
    {
        EnergyColor? aimColor = CarriedBall != null ? CarriedBall.color : InstalledColor;
        bool wantAim = Input.GetMouseButton(1) && aimColor.HasValue;

        if (!wantAim)
        {
            if (aiming) StopAiming();
            return;
        }

        if (!aiming)
        {
            aiming = true;
            PlayerFollowCamera.EnterAimMode();
        }

        UpdateAimRay(aimColor.Value);

        if (Input.GetMouseButtonDown(0) && placementValid)
            ConfirmPlacement(aimColor.Value);
    }

    private void StopAiming()
    {
        aiming = false;
        PlayerFollowCamera.ExitAimMode();
        HideSilhouette();
    }

    private void UpdateAimRay(EnergyColor color)
    {
        GetPortalSize(color, out float w, out float h);

        if (!TryRaycastAimTarget(out RaycastHit hit))
        {
            placementValid = false;
            HideSilhouette();
            LogAimDebug("no-hit", null, 0f);
            return;
        }

        PortalSurface surface = hit.collider.GetComponentInParent<PortalSurface>();
        if (surface == null)
        {
            placementValid = false;
            ShowSilhouette(false, hit.point, Quaternion.LookRotation(-hit.normal, Vector3.up), hit.normal, w, h, hit.distance);
            LogAimDebug("no-surface", hit.collider, hit.distance);
            return;
        }

        Vector3 normal = surface.transform.forward;
        Vector3 up = surface.transform.up;
        Vector3 right = surface.transform.right;
        Vector3 center = hit.point;

        bool withinEdges = CheckWithinEdges(surface, center, right, up, w, h);
        bool overlapBlocked = withinEdges && CheckOverlapBlocked(color, surface, center, normal, up, w, h);
        placementValid = withinEdges && !overlapBlocked;

        aimSurface = surface;
        aimCenter = center;
        aimNormal = normal;
        aimUp = up;

        ShowSilhouette(placementValid, center, Quaternion.LookRotation(normal, up), normal, w, h, hit.distance);
        LogAimDebug(placementValid ? "valid" : "surface-blocked", hit.collider, hit.distance);
    }

    // ponytail: 0.3초 간격 throttle — 매 프레임 전송하면 버퍼만 불필요하게 커진다.
    private void LogAimDebug(string state, Collider hitCollider, float dist)
    {
        if (Time.time < nextAimLogTime) return;
        nextAimLogTime = Time.time + 0.3f;
        string name = hitCollider != null ? hitCollider.name : "none";
        LokiTelemetry.Event("aim_debug", $"state={state} hit={name} dist={dist:F2}");
    }

    // 조준 판정·실루엣 크기를 이미 설치된 그 색 포탈의 실제 인스턴스 크기에 맞춘다(설치 전이면
    // 새로 생성될 때 쓰일 기본값). SpacePortal.DefaultWidth/Height를 직접 참조하면 인스펙터에서
    // width/height를 튜닝한 뒤에도 조준 판정만 옛 기본값을 보는 불일치가 난다.
    private static void GetPortalSize(EnergyColor color, out float width, out float height)
    {
        SpacePortal existing = color == EnergyColor.Orange ? SpacePortal.Orange : SpacePortal.Blue;
        width = existing != null ? existing.width : SpacePortal.DefaultWidth;
        height = existing != null ? existing.height : SpacePortal.DefaultHeight;
    }

    // 매 프레임 조준 판정용 재사용 버퍼(할당 없는 물리 쿼리 — ScalingSystem/Playershapecontroller.cs,
    // PortalSystem/PlayerRollModeReceiver.cs와 같은 저장소 관례).
    private readonly RaycastHit[] aimRaycastBuffer = new RaycastHit[16];
    private readonly Collider[] overlapBuffer = new Collider[16];

    // 카메라 forward로 레이를 쏘되, 자신/다른 플레이어는 계층으로 걸러 제외한다(레이어가 아니라
    // 계층으로 거르는 저장소 관용구 — ThreadPinPlacer.TryRaycastWall과 동일한 이유:
    // 플레이어가 대개 Default 레이어라 레이어 필터는 벽까지 통째로 걸러버린다).
    private bool TryRaycastAimTarget(out RaycastHit hit)
    {
        hit = default;
        Camera cam = Camera.main;
        if (cam == null) return false;

        int count = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, aimRaycastBuffer,
                                             aimRange, ~0, QueryTriggerInteraction.Ignore);
        float bestDist = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = aimRaycastBuffer[i];
            if (h.collider.GetComponentInParent<PlayerMover>() != null) continue;
            if (h.distance < bestDist) { bestDist = h.distance; hit = h; found = true; }
        }
        return found;
    }

    private bool CheckWithinEdges(PortalSurface surface, Vector3 center, Vector3 right, Vector3 up, float width, float height)
    {
        float hw = width * 0.5f;
        float hh = height * 0.5f;
        Vector2 half = surface.HalfExtents();
        Vector3 boxCenter = surface.Box.center;

        Vector3[] corners =
        {
            center + right * hw + up * hh, center - right * hw + up * hh,
            center + right * hw - up * hh, center - right * hw - up * hh,
        };
        foreach (Vector3 c in corners)
        {
            Vector3 local = surface.transform.InverseTransformPoint(c) - boxCenter;
            if (Mathf.Abs(local.x) > half.x - edgeMargin || Mathf.Abs(local.y) > half.y - edgeMargin)
                return false;
        }
        return true;
    }

    private bool CheckOverlapBlocked(EnergyColor color, PortalSurface surface, Vector3 center, Vector3 normal, Vector3 up, float width, float height)
    {
        // 박스를 표면 기준 앞뒤 대칭으로 두면 절반이 패널이 붙은 벽 쪽으로 파고들어, 그 벽이 부동소수점
        // 경계에서 "장애물"로 깜빡이며 오탐지된다(Loki aim_debug 계측으로 valid/surface-blocked가
        // 같은 자리에서 매 프레임 뒤집히는 것을 실측 확인, 2026-09-11). 포탈이 실제로 존재하는 바깥쪽
        // 방향으로만 박스를 두면 벽은 애초에 범위 밖이라 예외 처리 없이 해결된다.
        Vector3 halfExtents = new Vector3(width * 0.5f, height * 0.5f, overlapCheckDepth * 0.5f);
        Vector3 boxCenter = center + normal * halfExtents.z;
        Quaternion rot = Quaternion.LookRotation(normal, up);
        int count = Physics.OverlapBoxNonAlloc(boxCenter, halfExtents, overlapBuffer, rot, ~0, QueryTriggerInteraction.Collide);

        SpacePortal ownExisting = color == EnergyColor.Orange ? SpacePortal.Orange : SpacePortal.Blue;
        for (int i = 0; i < count; i++)
        {
            Collider h = overlapBuffer[i];
            if (h.GetComponentInParent<PortalSurface>() == surface) continue; // 패널 자신은 항상 겹친다 — 정상.
            if (h.GetComponentInParent<PlayerMover>() != null) continue;      // 조준 중인 플레이어 자신도 제외.
            if (h.GetComponentInParent<RespawnZone>() != null) continue;      // 체크포인트는 물리 장애물이 아니다(§13 배치 실측 — 2026-09-11).
            SpacePortal existing = h.GetComponentInParent<SpacePortal>();
            if (existing != null && existing == ownExisting) continue;        // 자기 색 기존 포탈은 제외(§3.2).
            return true;
        }
        return false;
    }

    private void ConfirmPlacement(EnergyColor color)
    {
        SpacePortal portal = color == EnergyColor.Orange ? SpacePortal.Orange : SpacePortal.Blue;
        if (portal == null)
        {
            GameObject go = new GameObject($"SpacePortal_{color}");
            portal = go.AddComponent<SpacePortal>();
            portal.color = color;
        }
        portal.PlaceAt(aimCenter, aimNormal, aimUp, aimSurface.transform);

        if (CarriedBall != null)
        {
            CarriedBall.SetInstalled(this);
            CarriedBall = null;
            ApplyGlow(null);
        }
        InstalledColor = color;
        Debug.Log($"[SpacePortal] {color} 포탈 설치/이동 완료.");
    }

    // ── 실루엣(설치 가능 여부 미리보기) ─────────────────────────────────────────

    private void EnsureSilhouette()
    {
        if (silhouette != null) return;

        silhouette = GameObject.CreatePrimitive(PrimitiveType.Quad);
        silhouette.name = "PortalAimSilhouette";
        Destroy(silhouette.GetComponent<Collider>());
        silhouetteRenderer = silhouette.GetComponent<MeshRenderer>();
        // ZTest Always 전용 셰이더 — 거리 오프셋만으로는 깊이버퍼 정밀도 때문에 패널 시각 메쉬에
        // 묻혀 안 보이는 문제를 근본적으로 못 없앴다(2026-09-11 재확인). 항상 맨 위에 그린다.
        silhouetteRenderer.sharedMaterial = new Material(Shader.Find("SpacePortalSystem/AimSilhouette"));
        silhouette.SetActive(false);
    }

    // outward는 맞은 표면에서 카메라 쪽으로 나가는 방향(hit.normal 또는 패널의 바깥쪽 forward)이다.
    // rot의 forward로부터 역산하지 않고 명시적으로 받는 이유: 두 호출부가 서로 다른 관례로 rot을
    // 만들어서(하나는 -hit.normal을 forward로, 하나는 표면 바깥 normal을 forward로) rot 하나로
    // 오프셋 부호를 통일할 수 없었다 — 실제로 후자 경우 rot*Vector3.back이 벽 안쪽을 가리켜
    // 실루엣이 콜라이더 속에 파묻히는 버그였다.
    private void ShowSilhouette(bool valid, Vector3 pos, Quaternion rot, Vector3 outward, float width, float height, float aimDistance)
    {
        EnsureSilhouette();
        silhouette.SetActive(true);
        silhouette.transform.localScale = new Vector3(width, height, 1f);
        // 고정 0.01U 띄우기는 카메라에서 10U 이상 떨어지면 깊이버퍼 정밀도 아래로 묻혀 실루엣이
        // 패널 시각 큐브에 항상 져서 안 보였다(2026-09-11 aim_debug 로그로 조준 거리 5~16U 확인 후
        // 실측). 거리 비례로 띄운다.
        float offset = Mathf.Max(0.02f, aimDistance * 0.002f);
        silhouette.transform.SetPositionAndRotation(pos + outward.normalized * offset, rot);
        // 설치 가능 = 파랑, 불가능 = 빨강(2026-09-11 확정 — 기존 초록/빨강에서 변경).
        silhouetteRenderer.sharedMaterial.color = valid ? new Color(0.25f, 0.55f, 1f, 0.65f) : new Color(0.9f, 0.2f, 0.2f, 0.65f);
    }

    private void HideSilhouette()
    {
        if (silhouette != null) silhouette.SetActive(false);
        placementValid = false;
    }

    // ── 몸의 빛 ──────────────────────────────────────────────────────────────

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void ApplyGlow(EnergyColor? color)
    {
        foreach (Renderer r in visualRenderers)
        {
            if (r == null) continue;
            if (!color.HasValue)
            {
                r.SetPropertyBlock(null); // 원래 머티리얼 색으로 복귀 — 공유 에셋은 손대지 않는다.
                continue;
            }
            glowBlock.Clear();
            glowBlock.SetColor(ColorId, color.Value == EnergyColor.Orange ? orangeGlowColor : blueGlowColor);
            r.SetPropertyBlock(glowBlock);
        }
    }
}
