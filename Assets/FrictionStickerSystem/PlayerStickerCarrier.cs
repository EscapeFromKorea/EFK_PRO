using UnityEngine;

/// <summary>
/// 플레이어가 스티커를 조준·부착·회수하는 주체. <see cref="FrictionStickerController"/>가 런타임에
/// 플레이어 Root에 AddComponent한다 — 씬의 플레이어 3종 프리팹도, PlayerSystem 파일도 건드리지
/// 않기 위함(교차 폴더 하드룰). AccelSystem/PlayerAccelReceiver·PortalSystem/PlayerRollModeReceiver와
/// 같은 "기믹이 붙여주는 리시버" 패턴이다.
///
/// [입력 게이트] 이 플레이어가 조작 대상일 때만(PlayerMover.IsControlled) 입력을 읽는다. Tab으로
/// 다른 도형을 조작 중이면 조준·부착을 하지 않아 같은 키에 동시 반응하지 않는다. 인벤토리는
/// 도형별로 각자 유지된다.
///
/// [조준] 카메라 화면 중앙에서 레이캐스트. 카메라가 없으면 플레이어 forward로 폴백. 맞은 콜라이더의
/// 부모에서 StickerSurface를 찾는다(콜라이더가 자식에 있어도 동작).
///
/// [키] attachKey(기본 F): 조준한 표면에 스티커가 없으면 부착, 있으면 회수(환불). switchKindKey(기본 Q):
/// 미끄럼 ↔ 벨크로 전환. 모든 수치·키는 인스펙터 노출(컨트롤러가 기본값을 밀어 넣는다).
/// </summary>
[RequireComponent(typeof(PlayerMover))]
[DisallowMultipleComponent]
public class PlayerStickerCarrier : MonoBehaviour
{
    [Header("조준")]
    [Tooltip("스티커를 붙일 수 있는 최대 거리(Unit). 카메라 화면 중앙에서 이 거리까지 레이캐스트한다.")]
    public float aimRange = 4f;
    [Tooltip("조준 레이캐스트가 부딪힐 레이어. 기본 전체.")]
    public LayerMask aimMask = ~0;

    [Header("입력 키")]
    [Tooltip("조준한 표면에 스티커를 부착 / 회수하는 키.")]
    public KeyCode attachKey = KeyCode.F;
    [Tooltip("미끄럼 ↔ 벨크로 선택을 전환하는 키.")]
    public KeyCode switchKindKey = KeyCode.Q;

    [Header("보유량 (-1 = 무한)")]
    [Tooltip("미끄럼 스티커 보유 개수. -1이면 무한.")]
    public int slipCount = -1;
    [Tooltip("벨크로 스티커 보유 개수. -1이면 무한.")]
    public int velcroCount = -1;

    [Header("선택 상태")]
    [Tooltip("현재 붙일 스티커 종류.")]
    public StickerKind selectedKind = StickerKind.Slip;

    /// <summary>컨트롤러가 주입하는 공유 마찰/시각 튜닝값. null이면 기본값으로 동작.</summary>
    [System.NonSerialized] public FrictionStickerSettings settings = new FrictionStickerSettings();

    private PlayerMover mover;
    private Camera aimCamera;
    private Transform reticle;              // 조준점 표시(작은 판). 이 컴포넌트가 소유·파괴.
    private Renderer reticleRenderer;
    private StickerSurface aimedSurface;    // 이번 프레임 조준 중인 표면(없으면 null).
    private RaycastHit aimHit;

    private void Awake()
    {
        mover = GetComponent<PlayerMover>();
    }

    private void OnDisable()
    {
        HideReticle();
    }

    private void OnDestroy()
    {
        if (reticleRenderer != null) Destroy(reticleRenderer.material);
        if (reticle != null) Destroy(reticle.gameObject);
    }

    private void Update()
    {
        // 조작 대상이 아니거나 외부 주도(리스폰/매달림 등) 상태면 개입하지 않는다.
        if (mover == null || !mover.IsControlled || mover.ExternallyDriven)
        {
            aimedSurface = null;
            HideReticle();
            return;
        }

        if (Input.GetKeyDown(switchKindKey))
            selectedKind = selectedKind == StickerKind.Slip ? StickerKind.Velcro : StickerKind.Slip;

        UpdateAim();

        if (Input.GetKeyDown(attachKey))
            HandleAttachKey();
    }

    private void UpdateAim()
    {
        aimedSurface = null;

        Ray ray = BuildAimRay();
        if (Physics.Raycast(ray, out aimHit, aimRange, aimMask, QueryTriggerInteraction.Ignore))
        {
            StickerSurface surface = aimHit.collider.GetComponentInParent<StickerSurface>();
            if (surface != null)
                aimedSurface = surface;
        }

        UpdateReticle();
    }

    private Ray BuildAimRay()
    {
        if (aimCamera == null || !aimCamera.isActiveAndEnabled)
            aimCamera = ResolveCamera();

        if (aimCamera != null)
            return aimCamera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        // 카메라를 못 찾으면 몸 중심에서 바라보는 방향으로.
        return new Ray(transform.position + Vector3.up * 0.2f, transform.forward);
    }

    private static Camera ResolveCamera()
    {
        if (Camera.main != null) return Camera.main;
        return Camera.current != null ? Camera.current : Object.FindObjectOfType<Camera>();
    }

    private void HandleAttachKey()
    {
        if (aimedSurface == null) return;

        // 조준한 표면에 이미 스티커가 있으면 회수(종류 무관) → 환불.
        if (aimedSurface.Current != null)
        {
            StickerKind refunded = aimedSurface.Current.Retract();
            AddToInventory(refunded, 1);
            return;
        }

        // 없으면 부착 — 허용 종류 + 보유량 확인.
        if (!aimedSurface.Accepts(selectedKind))
        {
            Debug.Log($"[FrictionSticker] '{aimedSurface.name}'은(는) {selectedKind} 스티커를 허용하지 않습니다.");
            return;
        }
        if (!HasStock(selectedKind))
        {
            Debug.Log($"[FrictionSticker] {selectedKind} 스티커가 없습니다.");
            return;
        }

        FrictionSticker placed = FrictionSticker.Attach(aimedSurface, selectedKind, settings, aimHit.point, aimHit.normal);
        if (placed != null)
            AddToInventory(selectedKind, -1);
    }

    private bool HasStock(StickerKind kind) =>
        (kind == StickerKind.Slip ? slipCount : velcroCount) != 0;

    private void AddToInventory(StickerKind kind, int delta)
    {
        if (kind == StickerKind.Slip)
        {
            if (slipCount >= 0) slipCount = Mathf.Max(0, slipCount + delta);
        }
        else
        {
            if (velcroCount >= 0) velcroCount = Mathf.Max(0, velcroCount + delta);
        }
    }

    // --- 조준점 표시: 부착 가능 여부와 선택 종류를 색으로 알린다(명세서 "색상으로 구분") ---

    private void UpdateReticle()
    {
        if (aimedSurface == null)
        {
            HideReticle();
            return;
        }

        EnsureReticle();
        reticle.gameObject.SetActive(true);
        reticle.position = aimHit.point + aimHit.normal * 0.01f;
        reticle.rotation = Quaternion.LookRotation(aimHit.normal);

        Color c;
        if (aimedSurface.Current != null)
            c = new Color(1f, 1f, 1f, 0.9f);                 // 흰색 = 회수 대상
        else if (!aimedSurface.Accepts(selectedKind))
            c = new Color(0.9f, 0.2f, 0.2f, 0.9f);           // 빨강 = 이 종류 불가
        else
            c = settings != null ? settings.ColorFor(selectedKind) : Color.cyan; // 종류 색 = 부착 가능

        if (reticleRenderer != null)
        {
            if (reticleRenderer.material.HasProperty("_BaseColor")) reticleRenderer.material.SetColor("_BaseColor", c);
            if (reticleRenderer.material.HasProperty("_Color")) reticleRenderer.material.SetColor("_Color", c);
        }
    }

    private void EnsureReticle()
    {
        if (reticle != null) return;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "StickerReticle";
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.25f;
        reticleRenderer = go.GetComponent<Renderer>();
        Shader shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
        reticleRenderer.material = new Material(shader);
        reticle = go.transform;
    }

    private void HideReticle()
    {
        if (reticle != null) reticle.gameObject.SetActive(false);
    }
}
