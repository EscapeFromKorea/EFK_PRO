using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 딱딱 블록 시스템의 씬 진입점. 조작 중인 플레이어 근처의 블록을 잡아 결합 후보를 찾고 하이라이트하며,
/// 키 입력으로 결합/해제를 실행한다. 상태(조인트)는 각 <see cref="SnapBlock"/>이 갖는다.
///
/// [타겟팅 — 근접] 카메라 조준이 아니라 "플레이어에서 aimRange 안, 가장 가까운 SnapBlock"을 대상으로
/// 삼는다(마찰 스티커·DreamThreadController와 동일). 플레이어가 블록 A를 블록 B에 밀어붙이면 A가
/// 대상이 되고, B가 정렬 후보로 잡힌다. 두 맞물릴 면에 하이라이트 구가 뜬다.
///
/// [입력] 키를 직접 읽지 않는다. 결합/해제는 E 홀드 액션으로 <see cref="InteractionController"/>에
/// 올린다(키맵 통합안 §7-1). 플레이어가 블록을 들고 있으면(<see cref="PlayerBlockCarrier.Carried"/>)
/// 그 블록이 대상이고, 홀드 확정 때 들기를 풀고 같은 프레임에 Weld한다(B' 방식). 든 블록은 SnapBlock이
/// 비활성이라 <c>AllBlocks</c>에 없으므로 후보 상대는 AllBlocks에서, 대상은 명시해서 쓴다.
/// 플레이어가 블록 <b>위에 서 있으면 결합하지 못한다</b> — 결합하면 블록이 서 있는 자리로 정렬돼
/// 플레이어와 물리 충돌을 일으켜 튕겨 나가기 때문(옆에서 위쪽 면으로 결합하는 건 가능). 해제는 허용.
///
/// [씬에 안 놔도 동작] Tools 메뉴로 만들어 튜닝할 수 있지만, 없으면 RuntimeInitializeOnLoadMethod가
/// 기본값 인스턴스를 자동 생성한다. 씬 무수정.
///
/// [규약] PlayerSystem·씬 무수정. 플레이어에서 IsControlled만 읽는다. 전역 Physics.* 미변경(MP-01).
/// </summary>
[DisallowMultipleComponent]
public class SnapBlockController : MonoBehaviour, IInteractionProvider
{
    [Header("결합 판정")]
    [Tooltip("두 면 중심이 이 거리(Unit) 안일 때 결합 후보가 된다.")]
    public float snapDistance = 0.6f;
    [Tooltip("블록을 든 채 결합할 때의 면 중심 거리(Unit). 든 블록은 머리 위 고정 높이라 일반 거리로는 " +
             "후보가 거의 안 잡힌다. 결합 시 Weld가 면을 정렬하므로 넓혀도 어긋나지 않는다.")]
    public float carriedSnapDistance = 1.5f;
    [Tooltip("두 면 법선이 정반대에서 이 각도(도) 이내로 마주 볼 때만 결합 후보가 된다.")]
    public float snapAngleToleranceDeg = 20f;
    [Tooltip("한 구조물(조인트로 이어진 블록 묶음)의 최대 블록 수. 초과하는 결합은 거부한다.")]
    public int maxBlocksPerStructure = 12;

    [Header("타겟팅 (근접)")]
    [Tooltip("플레이어에서 이 거리(Unit) 안, 가장 가까운 SnapBlock을 대상으로 삼는다.")]
    public float aimRange = 4f;

    [Header("결합 조인트")]
    [Tooltip("각 블록에 주입할 파괴 힘(N). 무한이면 절대 안 끊어진다.")]
    public float jointBreakForce = Mathf.Infinity;

    [Header("하이라이트 색")]
    public Color candidateColor = new Color(0.3f, 1f, 0.5f, 0.95f);
    public Color blockedColor = new Color(1f, 0.3f, 0.3f, 0.95f);
    public Color detachColor = new Color(1f, 1f, 1f, 0.95f);

    private static SnapBlockController instance;

    private readonly List<SnapBlock.Face> facesA = new List<SnapBlock.Face>();
    private readonly List<SnapBlock.Face> facesB = new List<SnapBlock.Face>();
    private readonly List<SnapBlock> sceneBlocks = new List<SnapBlock>();

    private Transform hlA, hlB;                 // 후보 면 하이라이트 2개
    private Renderer hlARenderer, hlBRenderer;

    // 이번 프레임 대상 결과
    private SnapBlock aimedBlock;
    private SnapBlock candBlock;
    private SnapBlock.Face aimedFace, candFace;
    private bool hasCandidate;
    private SnapBlock standingOnBlock;       // 플레이어가 밟고 있는 블록(결합 금지 판정용).

    private PlayerMover controlledPlayer;
    private PlayerBlockCarrier carrier;      // 조작 중인 플레이어의 운반 컴포넌트(없으면 null).
    private System.Action onHold;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (Object.FindObjectOfType<SnapBlockController>() != null) return;
        new GameObject("SnapBlockController (auto)").AddComponent<SnapBlockController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
        onHold = ExecuteHold;
    }

    private void OnEnable()
    {
        InteractionController.Register(this);
    }

    private void OnDisable()
    {
        InteractionController.Unregister(this);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (hlARenderer != null) Destroy(hlARenderer.material);
        if (hlBRenderer != null) Destroy(hlBRenderer.material);
        if (hlA != null) Destroy(hlA.gameObject);
        if (hlB != null) Destroy(hlB.gameObject);
    }

    private void Update()
    {
        PlayerMover controlled = FindControlledPlayer();
        controlledPlayer = controlled;
        if (controlled == null)
        {
            carrier = null;
            aimedBlock = null;
            hasCandidate = false;
            HideHighlights();
            return;
        }

        carrier = controlled.GetComponent<PlayerBlockCarrier>();
        UpdateTarget(controlled);
    }

    private static PlayerMover FindControlledPlayer()
    {
        foreach (PlayerMover m in Object.FindObjectsOfType<PlayerMover>())
            if (m.IsControlled && !m.ExternallyDriven) return m;
        return null;
    }

    // 플레이어에서 aimRange 안, 가장 가까운 SnapBlock을 대상으로 잡고 결합 후보를 찾는다.
    private void UpdateTarget(PlayerMover controlled)
    {
        aimedBlock = null;
        candBlock = null;
        hasCandidate = false;

        CollectSceneBlocks();
        standingOnBlock = FindBlockUnderfoot();
        SnapBlock held = carrier != null ? carrier.Carried : null;
        if (held != null)
        {
            aimedBlock = held; // 든 블록이 곧 대상(AllBlocks에는 없다).
        }
        else
        {
            Vector3 me = controlled.transform.position;
            float bestSqr = aimRange * aimRange;
            foreach (SnapBlock b in sceneBlocks)
            {
                Collider col = b != null ? b.GetComponent<Collider>() : null;
                if (col == null) continue;
                float sqr = (col.ClosestPoint(me) - me).sqrMagnitude;
                if (sqr > bestSqr) continue;
                bestSqr = sqr;
                aimedBlock = b;
            }
        }

        if (aimedBlock == null)
        {
            HideHighlights();
            return;
        }

        EnsureHighlights();

        if (aimedBlock.HasConnections)
        {
            // 해제 대상 — 블록 중심에 흰색 표시.
            SetHighlight(hlA, hlARenderer, aimedBlock.transform.position, detachColor);
            HideOne(hlB);
            return;
        }

        FindBestCandidate();

        if (hasCandidate)
        {
            bool fits = CountStructureWith(candBlock, aimedBlock) <= maxBlocksPerStructure;
            Color c = fits ? candidateColor : blockedColor;
            SetHighlight(hlA, hlARenderer, aimedFace.center, c);
            SetHighlight(hlB, hlBRenderer, candFace.center, c);
        }
        else
        {
            HideHighlights();
        }
    }

    // 대상 블록의 6면 × 씬의 다른 모든 블록의 6면 중, 거리·각도 조건을 통과하는 가장 가까운 쌍.
    private void FindBestCandidate()
    {
        aimedBlock.GetFaces(facesA);

        float dist = carrier != null && carrier.Carried == aimedBlock ? carriedSnapDistance : snapDistance;
        float bestSqr = dist * dist;
        float cosTol = Mathf.Cos(Mathf.Deg2Rad * Mathf.Clamp(snapAngleToleranceDeg, 0f, 179f));

        foreach (SnapBlock other in sceneBlocks)
        {
            if (other == null || other == aimedBlock) continue;
            if (aimedBlock.HasConnectionTo(other)) continue;

            other.GetFaces(facesB);
            for (int i = 0; i < facesA.Count; i++)
            {
                for (int k = 0; k < facesB.Count; k++)
                {
                    // 법선이 정반대를 향해야 한다: dot(nA, -nB) >= cos(tol)
                    if (Vector3.Dot(facesA[i].normal, -facesB[k].normal) < cosTol) continue;

                    float sqr = (facesA[i].center - facesB[k].center).sqrMagnitude;
                    if (sqr > bestSqr) continue;

                    bestSqr = sqr;
                    aimedFace = facesA[i];
                    candFace = facesB[k];
                    candBlock = other;
                    hasCandidate = true;
                }
            }
        }
    }

    // ── 블록 위 판정 ──────────────────────────────────────────

    // 플레이어 발밑(윗면 위, XZ가 블록 안)에 있는 블록. 없으면 null. AABB 판정이라 눕거나 기운 블록은 못 잡는다.
    private SnapBlock FindBlockUnderfoot()
    {
        PlayerShapeIdentity id = controlledPlayer != null ? controlledPlayer.GetComponentInChildren<PlayerShapeIdentity>() : null;
        Collider pc = id != null ? id.solidCollider : null;
        if (pc == null) return null;
        Bounds pb = pc.bounds;

        foreach (SnapBlock b in sceneBlocks)
        {
            Collider col = b != null ? b.GetComponent<Collider>() : null;
            if (col == null) continue;
            Bounds bb = col.bounds;
            if (pb.center.x < bb.min.x - 0.05f || pb.center.x > bb.max.x + 0.05f) continue;
            if (pb.center.z < bb.min.z - 0.05f || pb.center.z > bb.max.z + 0.05f) continue;
            if (pb.min.y < bb.max.y - 0.2f || pb.min.y > bb.max.y + 0.4f) continue;
            return b;
        }
        return null;
    }

    // ── 중앙 입력 연동 ────────────────────────────────────────

    // 결합 후보가 실제로 결합 가능한지(구조물 한도 포함). 사유는 가능하면 null.
    private string WeldBlockReason()
    {
        if (standingOnBlock != null)
            return "블록 위에서는 결합할 수 없습니다 — 내려와서 옆에서 결합하세요.";
        if (!hasCandidate)
            return "결합할 상대 블록이 없습니다 — 다른 블록의 면을 " +
                   (carrier != null && carrier.Carried != null ? carriedSnapDistance : snapDistance) + "칸 안, 나란히 맞대세요.";
        if (CountStructureWith(candBlock, aimedBlock) > maxBlocksPerStructure)
            return $"구조물이 최대 {maxBlocksPerStructure}개를 넘어 결합할 수 없습니다.";
        return null;
    }

    public void CollectActions(List<InteractionAction> into)
    {
        if (controlledPlayer == null || aimedBlock == null) return;

        bool detach = aimedBlock.HasConnections;
        string reason = detach ? null : WeldBlockReason();
        into.Add(new InteractionAction
        {
            channel = InteractionChannel.Hand, trigger = InteractionTrigger.Hold,
            verb = detach ? "결합 해제" : "결합", enabled = reason == null, reason = reason,
            priority = InteractionPriority.Block, distance = 0f, execute = onHold,
        });
    }

    private void ExecuteHold()
    {
        if (controlledPlayer == null) return;

        SnapBlock held = carrier != null ? carrier.Carried : null;
        if (held != null)
        {
            // B': 들기를 풀고 같은 프레임에 Weld. 후보는 풀기 전에 최신 자세로 다시 검증한다.
            aimedBlock = held;
            CollectSceneBlocks();
            standingOnBlock = FindBlockUnderfoot();
            FindBestCandidate();
            string heldReason = WeldBlockReason();
            if (heldReason != null) return;

            SnapBlock partner = candBlock;
            SnapBlock.Face mine = aimedFace, theirs = candFace;
            carrier.ReleaseInPlace();
            held.jointBreakForce = jointBreakForce;
            held.Weld(partner, mine, theirs);
            return;
        }

        if (aimedBlock == null) return;

        if (aimedBlock.HasConnections)
        {
            aimedBlock.DetachAll();
            return;
        }

        string reason = WeldBlockReason();
        if (reason != null) return;

        aimedBlock.jointBreakForce = jointBreakForce;
        aimedBlock.Weld(candBlock, aimedFace, candFace);
    }

    // candBlock이 속한 구조물의 블록 수 + (aimedBlock이 그 구조물에 아직 없으면) 1.
    private int CountStructureWith(SnapBlock seed, SnapBlock adding)
    {
        HashSet<SnapBlock> seen = new HashSet<SnapBlock>();
        Queue<SnapBlock> q = new Queue<SnapBlock>();
        q.Enqueue(seed);
        seen.Add(seed);
        while (q.Count > 0)
        {
            SnapBlock b = q.Dequeue();
            foreach (SnapBlock n in b.ConnectedBlocks)
            {
                if (n != null && seen.Add(n)) q.Enqueue(n);
            }
        }
        return seen.Contains(adding) ? seen.Count : seen.Count + 1;
    }

    private void CollectSceneBlocks()
    {
        sceneBlocks.Clear();
        sceneBlocks.AddRange(SnapBlock.AllBlocks);
    }

    // --- 하이라이트 구 2개 ---

    private static void SetHighlight(Transform t, Renderer r, Vector3 pos, Color c)
    {
        if (t == null) return;
        t.gameObject.SetActive(true);
        t.position = pos;
        if (r != null)
        {
            if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", c);
            if (r.material.HasProperty("_Color")) r.material.SetColor("_Color", c);
        }
    }

    private void EnsureHighlights()
    {
        if (hlA == null) { hlA = MakeMarker("SnapHL_A"); hlARenderer = hlA.GetComponent<Renderer>(); }
        if (hlB == null) { hlB = MakeMarker("SnapHL_B"); hlBRenderer = hlB.GetComponent<Renderer>(); }
    }

    private static Transform MakeMarker(string name)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.28f;
        Shader shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
        go.GetComponent<Renderer>().material = new Material(shader);
        go.SetActive(false);
        return go.transform;
    }

    private void HideHighlights()
    {
        HideOne(hlA);
        HideOne(hlB);
    }

    private static void HideOne(Transform t)
    {
        if (t != null) t.gameObject.SetActive(false);
    }
}
