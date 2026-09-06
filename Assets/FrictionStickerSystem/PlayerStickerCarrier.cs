using UnityEngine;

/// <summary>
/// 플레이어가 스티커를 부착·회수·교체하는 주체. <see cref="FrictionStickerController"/>가 런타임에
/// 플레이어 Root에 AddComponent한다 — 씬의 플레이어 3종 프리팹도, PlayerSystem 파일도 건드리지
/// 않기 위함(교차 폴더 하드룰). AccelSystem/PlayerAccelReceiver·PortalSystem/PlayerRollModeReceiver와
/// 같은 "기믹이 붙여주는 리시버" 패턴이다.
///
/// [입력 게이트] 이 플레이어가 조작 대상일 때만(PlayerMover.IsControlled) 입력을 읽는다. Tab으로
/// 다른 도형을 조작 중이면 반응하지 않는다. 인벤토리는 도형별로 각자 유지된다.
///
/// [타겟팅 — 근접] 카메라 조준이 아니라 "플레이어에서 aimRange 안, 가장 가까운 StickerSurface"를
/// 대상으로 삼는다(DreamThreadController가 앵커를 잡는 방식과 동일). 3인칭 궤도 카메라에서 작은
/// 표면을 화면 중앙에 맞추는 건 비현실적이라, 표면 근처에 서서 키만 누르면 되게 한다. 대상 표면
/// 위에 작은 공(조준점)이 떠서 색으로 상태를 알린다.
///
/// [키] attachKey(기본 V — F·G는 꿈의 실타래와 겹침):
///  - 대상 표면에 스티커 없음        → 선택 종류 부착
///  - 대상 표면에 "다른 종류" 스티커 → 그 스티커를 떼고 선택 종류로 교체
///  - 대상 표면에 "같은 종류" 스티커 → 회수(토글 오프)
/// switchKindKey(기본 Q): 미끄럼 ↔ 벨크로 선택 전환. 모든 수치·키는 인스펙터 노출.
/// </summary>
[RequireComponent(typeof(PlayerMover))]
[DisallowMultipleComponent]
public class PlayerStickerCarrier : MonoBehaviour
{
    [Header("타겟팅 (근접)")]
    [Tooltip("이 거리(Unit) 안에서 가장 가까운 StickerSurface를 대상으로 삼는다. 플레이어 위치에서 " +
             "표면 콜라이더의 가장 가까운 점까지의 거리로 잰다.")]
    public float aimRange = 4f;

    [Header("입력 키")]
    [Tooltip("스티커 부착 / 교체 / 회수 키. (F·G는 꿈의 실타래와 겹쳐 V 사용)")]
    public KeyCode attachKey = KeyCode.V;
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
    private Transform reticle;              // 대상 표면 위에 뜨는 조준점(작은 공). 이 컴포넌트가 소유·파괴.
    private Renderer reticleRenderer;
    private StickerSurface aimedSurface;    // 이번 프레임 대상 표면(없으면 null).
    private Vector3 aimPoint;               // 대상 표면에서 플레이어에 가장 가까운 점(데칼 위치).
    private Vector3 aimNormal;              // 그 점에서의 표면 법선(데칼 방향).

    private static readonly System.Collections.Generic.List<StickerSurface> surfaceBuf =
        new System.Collections.Generic.List<StickerSurface>();

    private void Awake()
    {
        mover = GetComponent<PlayerMover>();
    }

    private void OnDisable() => HideReticle();

    private void OnDestroy()
    {
        if (reticleRenderer != null) Destroy(reticleRenderer.material);
        if (reticle != null) Destroy(reticle.gameObject);
    }

    private void Update()
    {
        if (mover == null || !mover.IsControlled || mover.ExternallyDriven)
        {
            aimedSurface = null;
            HideReticle();
            return;
        }

        if (Input.GetKeyDown(switchKindKey))
            selectedKind = selectedKind == StickerKind.Slip ? StickerKind.Velcro : StickerKind.Slip;

        UpdateTarget();
        UpdateReticle();

        if (Input.GetKeyDown(attachKey))
            HandleAttachKey();
    }

    // 플레이어에서 aimRange 안, 가장 가까운 StickerSurface를 찾는다.
    private void UpdateTarget()
    {
        aimedSurface = null;
        float bestSqr = aimRange * aimRange;
        Vector3 me = transform.position;

        surfaceBuf.Clear();
        surfaceBuf.AddRange(Object.FindObjectsOfType<StickerSurface>());
        foreach (StickerSurface s in surfaceBuf)
        {
            Collider col = s != null ? s.ResolvedCollider : null;
            if (col == null) continue;

            Vector3 near = col.ClosestPoint(me);
            float sqr = (near - me).sqrMagnitude;
            if (sqr > bestSqr) continue;

            bestSqr = sqr;
            aimedSurface = s;
            aimPoint = near;
        }

        if (aimedSurface != null)
            aimNormal = ResolveSurfaceNormal(aimedSurface.ResolvedCollider, aimPoint);
    }

    // 가장 가까운 점에서의 표면 법선. 플레이어→그 점으로 짧은 레이를 쏴 hit.normal을 얻고,
    // 실패하면 콜라이더 바운드 중심에서 바깥 방향으로 근사한다(박스 면에 충분).
    private Vector3 ResolveSurfaceNormal(Collider col, Vector3 point)
    {
        Vector3 from = transform.position + Vector3.up * 0.3f;
        Vector3 dir = point - from;
        if (dir.sqrMagnitude > 1e-4f &&
            col.Raycast(new Ray(from, dir.normalized), out RaycastHit hit, dir.magnitude + 0.5f))
            return hit.normal;

        Vector3 outward = point - col.bounds.center;
        return outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.up;
    }

    private void HandleAttachKey()
    {
        if (aimedSurface == null)
        {
            Debug.Log($"[FrictionSticker] {aimRange}칸 안에 StickerSurface가 없습니다 — 표면 가까이 서세요.");
            return;
        }

        FrictionSticker current = aimedSurface.Current;

        // 같은 종류가 이미 붙어 있으면 → 회수(토글 오프).
        if (current != null && current.Kind == selectedKind)
        {
            AddToInventory(current.Retract(), 1);
            return;
        }

        // 다른 종류가 붙어 있으면 → 떼고 이번 종류로 교체(환불 후 재부착).
        if (current != null)
        {
            AddToInventory(current.Retract(), 1);
            current = null;
        }

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

        FrictionSticker placed = FrictionSticker.Attach(aimedSurface, selectedKind, settings, aimPoint, aimNormal);
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

    // --- 조준점: 대상 표면 위에 뜨는 작은 공. 색으로 상태 표시(명세서 "색상으로 구분"). ---

    private void UpdateReticle()
    {
        if (aimedSurface == null)
        {
            HideReticle();
            return;
        }

        EnsureReticle();
        reticle.gameObject.SetActive(true);
        reticle.position = aimPoint + aimNormal * 0.05f;

        Color c;
        if (aimedSurface.Current != null && aimedSurface.Current.Kind == selectedKind)
            c = new Color(1f, 1f, 1f, 0.95f);                            // 흰색 = 같은 종류 → 회수
        else if (!aimedSurface.Accepts(selectedKind))
            c = new Color(0.9f, 0.2f, 0.2f, 0.95f);                      // 빨강 = 이 종류 불가
        else
            c = settings != null ? settings.ColorFor(selectedKind) : Color.cyan; // 종류 색 = 부착/교체

        if (reticleRenderer != null)
        {
            if (reticleRenderer.material.HasProperty("_BaseColor")) reticleRenderer.material.SetColor("_BaseColor", c);
            if (reticleRenderer.material.HasProperty("_Color")) reticleRenderer.material.SetColor("_Color", c);
        }
    }

    private void EnsureReticle()
    {
        if (reticle != null) return;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "StickerReticle";
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.3f;
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
