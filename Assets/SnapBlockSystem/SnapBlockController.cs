using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 딱딱 블록 시스템의 씬 진입점. 조작 중인 플레이어의 조준을 읽어 결합 후보를 찾고 하이라이트하며,
/// 키 입력으로 결합/해제를 실행한다. 상태(조인트)는 각 <see cref="SnapBlock"/>이 갖는다.
///
/// [씬에 안 놔도 동작] Tools 메뉴로 만들어 튜닝할 수 있지만, 없으면 RuntimeInitializeOnLoadMethod가
/// 기본값 인스턴스를 자동 생성한다(FrictionStickerController와 같은 방식). 씬 무수정.
///
/// [규약] PlayerSystem·씬 무수정. 플레이어에서 IsControlled만 읽는다. 전역 Physics.* 미변경(MP-01).
/// </summary>
[DisallowMultipleComponent]
public class SnapBlockController : MonoBehaviour
{
    [Header("결합 판정")]
    [Tooltip("두 면 중심이 이 거리(Unit) 안일 때 결합 후보가 된다.")]
    public float snapDistance = 0.6f;
    [Tooltip("두 면 법선이 정반대에서 이 각도(도) 이내로 마주 볼 때만 결합 후보가 된다.")]
    public float snapAngleToleranceDeg = 20f;
    [Tooltip("한 구조물(조인트로 이어진 블록 묶음)의 최대 블록 수. 초과하는 결합은 거부한다.")]
    public int maxBlocksPerStructure = 12;

    [Header("조준 / 입력")]
    [Tooltip("카메라 화면 중앙에서 블록을 조준하는 최대 거리(Unit).")]
    public float aimRange = 5f;
    [Tooltip("조준 레이캐스트가 부딪힐 레이어.")]
    public LayerMask aimMask = ~0;
    [Tooltip("결합 / 해제 키. 조준한 블록에 결합이 있으면 해제, 없으면 후보와 결합.")]
    public KeyCode weldKey = KeyCode.E;

    [Header("결합 조인트")]
    [Tooltip("각 블록에 주입할 파괴 힘(N). 무한이면 절대 안 끊어진다.")]
    public float jointBreakForce = Mathf.Infinity;

    [Header("하이라이트 색")]
    public Color candidateColor = new Color(0.3f, 1f, 0.5f, 0.9f);
    public Color blockedColor = new Color(1f, 0.3f, 0.3f, 0.9f);
    public Color detachColor = new Color(1f, 1f, 1f, 0.9f);

    private static SnapBlockController instance;

    private readonly List<SnapBlock.Face> facesA = new List<SnapBlock.Face>();
    private readonly List<SnapBlock.Face> facesB = new List<SnapBlock.Face>();
    private readonly List<SnapBlock> sceneBlocks = new List<SnapBlock>();

    private Camera aimCamera;
    private Transform hlA, hlB;                 // 후보 면 하이라이트 2개
    private Renderer hlARenderer, hlBRenderer;

    // 이번 프레임 조준 결과
    private SnapBlock aimedBlock;
    private SnapBlock candBlock;
    private SnapBlock.Face aimedFace, candFace;
    private bool hasCandidate;

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
        if (controlled == null)
        {
            HideHighlights();
            return;
        }

        UpdateAim(controlled);

        if (Input.GetKeyDown(weldKey))
            HandleWeldKey();
    }

    private static PlayerMover FindControlledPlayer()
    {
        foreach (PlayerMover m in Object.FindObjectsOfType<PlayerMover>())
            if (m.IsControlled && !m.ExternallyDriven) return m;
        return null;
    }

    private void UpdateAim(PlayerMover controlled)
    {
        aimedBlock = null;
        candBlock = null;
        hasCandidate = false;

        Ray ray = BuildAimRay(controlled);
        if (Physics.Raycast(ray, out RaycastHit hit, aimRange, aimMask, QueryTriggerInteraction.Ignore))
            aimedBlock = hit.collider.GetComponentInParent<SnapBlock>();

        if (aimedBlock == null)
        {
            HideHighlights();
            return;
        }

        EnsureHighlights();

        if (aimedBlock.HasConnections)
        {
            // 해제 대상 — 조준한 블록 중심에 흰색 표시.
            SetHighlight(hlA, hlARenderer, aimedBlock.transform.position, Vector3.up, detachColor);
            HideOne(hlB);
            return;
        }

        FindBestCandidate();

        if (hasCandidate)
        {
            bool fits = CountStructureWith(candBlock, aimedBlock) <= maxBlocksPerStructure;
            Color c = fits ? candidateColor : blockedColor;
            SetHighlight(hlA, hlARenderer, aimedFace.center, aimedFace.normal, c);
            SetHighlight(hlB, hlBRenderer, candFace.center, candFace.normal, c);
        }
        else
        {
            HideHighlights();
        }
    }

    // 조준한 블록의 6면 × 씬의 다른 모든 블록의 6면 중, 거리·각도 조건을 통과하는 가장 가까운 쌍.
    private void FindBestCandidate()
    {
        aimedBlock.GetFaces(facesA);
        CollectSceneBlocks();

        float bestSqr = snapDistance * snapDistance;
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

    private void HandleWeldKey()
    {
        if (aimedBlock == null) return;

        if (aimedBlock.HasConnections)
        {
            aimedBlock.DetachAll();
            return;
        }

        if (!hasCandidate) return;
        if (CountStructureWith(candBlock, aimedBlock) > maxBlocksPerStructure)
        {
            Debug.Log($"[SnapBlock] 구조물이 최대 {maxBlocksPerStructure}개를 넘어 결합할 수 없습니다.");
            return;
        }

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
        sceneBlocks.AddRange(Object.FindObjectsOfType<SnapBlock>());
    }

    // --- 조준 레이 / 카메라 ---

    private Ray BuildAimRay(PlayerMover controlled)
    {
        if (aimCamera == null || !aimCamera.isActiveAndEnabled)
            aimCamera = ResolveCamera();

        if (aimCamera != null)
            return aimCamera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        return new Ray(controlled.transform.position + Vector3.up * 0.2f, controlled.transform.forward);
    }

    private static Camera ResolveCamera()
    {
        if (Camera.main != null) return Camera.main;
        return Camera.current != null ? Camera.current : Object.FindObjectOfType<Camera>();
    }

    // --- 하이라이트 판 2개 ---

    private static void SetHighlight(Transform t, Renderer r, Vector3 pos, Vector3 normal, Color c)
    {
        if (t == null) return;
        t.gameObject.SetActive(true);
        Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
        t.position = pos + n * 0.01f;
        t.rotation = Quaternion.LookRotation(n);
        if (r != null)
        {
            if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", c);
            if (r.material.HasProperty("_Color")) r.material.SetColor("_Color", c);
        }
    }

    private void EnsureHighlights()
    {
        if (hlA == null) { hlA = MakeQuad("SnapHL_A"); hlARenderer = hlA.GetComponent<Renderer>(); }
        if (hlB == null) { hlB = MakeQuad("SnapHL_B"); hlBRenderer = hlB.GetComponent<Renderer>(); }
    }

    private static Transform MakeQuad(string name)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.5f;
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
