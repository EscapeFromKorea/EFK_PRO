using UnityEngine;

/// <summary>
/// 정육면체가 딱딱 블록(<see cref="SnapBlock"/>)에 자기 몸을 결합(도킹)해, 조립한 구조물을 통째로
/// 끌고 다니거나, 절벽 가장자리에서 결합을 유지한 채 남아 다른 도형이 건너는 다리의 고정 앵커가
/// 되는 주체. <see cref="CubeDockController"/>가 런타임에 플레이어 Root에 AddComponent한다 —
/// PlayerSystem 파일도, 씬의 플레이어 3종도, SnapBlockSystem도 건드리지 않는다(교차 폴더 하드룰).
///
/// [도형 게이트] 정육면체(PlayerShapeIdentity.Kind == Cube)만 도킹한다. 구·정사면체는 조준·도킹을
/// 스스로 거부한다.
///
/// [입력 게이트] 새 도킹 / 해제는 이 플레이어가 조작 대상일 때만(IsControlled &amp;&amp;
/// !ExternallyDriven) 받는다. "결합 해제는 정육면체 자신만" 요구를 이 게이트가 만족한다.
///
/// [왜 SnapBlock 프록시가 아니라 전용 조인트인가]
/// SnapBlock은 블록↔블록 조인트만 관리하고 BoxCollider+Rigidbody를 요구한다. 플레이어에 프록시
/// SnapBlock을 붙이면 SnapBlockController가 이 프록시를 결합/타겟 후보로 오인한다. 그래서 도킹은
/// 정육면체 Rigidbody에 붙인 <b>전용 ConfigurableJoint</b>(connectedBody = 대상 블록)로만 처리한다.
/// 조인트 설정(전축 Locked + projection)은 SnapBlock.Weld를 그대로 미러링해 처짐/폭주를 억제한다.
///
/// [도형 전환 유지] Tab으로 조작을 넘겨 IsControlled가 false가 돼도 조인트를 파괴하지 않는다 —
/// 정육면체가 앵커로 남아 다른 도형이 구조물 위를 건널 수 있어야 하기 때문. 단 자동 해제 조건
/// (리스폰·리셋·크기 변형·굴리기 모드·외부 소유)은 조작 여부와 무관하게 계속 감시한다.
///
/// [규약] 전역 상태(Physics.*) 미변경(MP-01). PlayerSystem·씬 무수정.
/// </summary>
[RequireComponent(typeof(PlayerMover))]
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public class PlayerCubeDock : MonoBehaviour
{
    [Header("타겟팅 (근접)")]
    [Tooltip("이 거리(Unit) 안에서 가장 가까운 SnapBlock을 대상으로 삼는다.")]
    public float dockRange = 3.5f;

    [Header("결합 판정 (딱딱 블록과 동일 기준)")]
    [Tooltip("정육면체 면과 블록 면의 중심이 이 거리(Unit) 안일 때 도킹 후보가 된다.")]
    public float snapDistance = 0.6f;
    [Tooltip("두 면 법선이 정반대에서 이 각도(도) 이내로 마주 볼 때만 도킹 후보가 된다.")]
    public float snapAngleToleranceDeg = 20f;
    [Tooltip("도킹 직전, 정육면체를 블록 면에 맞물리도록 면 법선 방향으로만 밀어 정렬한다(위치만).")]
    public bool snapAlignOnDock = true;

    [Header("한도")]
    [Tooltip("도킹 가능한 결합 구조물의 최대 블록 수.")]
    public int maxDockedBlocks = 6;
    [Tooltip("도킹 가능한 결합 구조물의 최대 총 질량.")]
    public float maxDockedMass = 12f;

    [Header("입력 키")]
    [Tooltip("도킹 / 해제 토글 키. (G는 ThreadPinPlacer가 이미 써서 H로 변경 — 2026-09-14 QA에서 발견)")]
    public KeyCode dockKey = KeyCode.H;

    [Header("자동 해제 감지")]
    [Tooltip("한 프레임에 플레이어가 이 거리(Unit) 넘게 순간이동하면 리스폰/리셋으로 보고 즉시 해제한다.")]
    public float teleportReleaseThreshold = 3f;

    [Header("성능")]
    [Tooltip("대상 블록을 다시 찾는 간격(초). FindObjectsOfType로 씬을 매 프레임 훑으면 매 프레임 " +
             "GC 할당이 생겨 눈에 띄는 프레임 히치를 유발한다(BlockCarrySystem과 동일 원인, " +
             "플레이테스트에서 실측됨).")]
    public float retargetInterval = 0.15f;

    [Header("조인트")]
    [Tooltip("도킹 조인트 파괴 힘(N). 무한이면 절대 안 끊어진다. 유한이면 과부하 시 도킹이 풀린다.")]
    public float jointBreakForce = Mathf.Infinity;
    [Tooltip("도킹 조인트 파괴 토크(N·m).")]
    public float jointBreakTorque = Mathf.Infinity;

    private PlayerMover mover;
    private Rigidbody body;
    private PlayerShapeIdentity shapeId;
    private BoxCollider selfBox;                 // 정육면체의 솔리드 박스 콜라이더(면 계산용).

    private ConfigurableJoint joint;             // 살아 있으면 도킹 중.
    private SnapBlock dockedBlock;

    private Vector3 lastPos;
    private Vector3 lastScale;

    // 마지막 스캔 시점의 후보. retargetInterval 간격으로만 갱신된다.
    private SnapBlock aimed;
    private Vector3 aimSelfFaceCenter, aimBlockFaceCenter, aimBlockFaceNormal;
    private bool hasCandidate;
    private string rejectReason;
    private float nextRetargetTime;

    private Transform reticle;
    private Renderer reticleRenderer;

    private static readonly Vector3[] LocalNormals =
    {
        Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
    };

    private static readonly System.Collections.Generic.List<SnapBlock> blockBuf =
        new System.Collections.Generic.List<SnapBlock>();
    private static readonly System.Collections.Generic.List<SnapBlock.Face> blockFaces =
        new System.Collections.Generic.List<SnapBlock.Face>();

    private static readonly Color colorOk = new Color(0.3f, 1f, 0.5f, 0.95f);
    private static readonly Color colorReject = new Color(0.9f, 0.2f, 0.2f, 0.95f);
    private static readonly Color colorDocked = new Color(1f, 1f, 1f, 0.95f);

    private bool IsCube =>
        shapeId != null && shapeId.Kind == PlayerShapeStats.ShapeKind.Cube;

    private void Awake()
    {
        mover = GetComponent<PlayerMover>();
        body = GetComponent<Rigidbody>();
        shapeId = GetComponentInChildren<PlayerShapeIdentity>();
        selfBox = ResolveSelfBox();
        lastPos = transform.position;
        lastScale = transform.localScale;
    }

    private void OnDisable()
    {
        if (joint != null) Undock("컴포넌트 비활성화");
        HideReticle();
    }

    private void OnDestroy()
    {
        if (joint != null) Undock("컴포넌트 파괴");
        if (reticleRenderer != null) Destroy(reticleRenderer.material);
        if (reticle != null) Destroy(reticle.gameObject);
    }

    private void Update()
    {
        // 자동 해제는 조작권과 무관하게 감시한다(앵커로 남은 정육면체가 리스폰될 수 있다).
        if (joint != null || dockedBlock != null)
        {
            if (joint == null || dockedBlock == null)
            {
                // 조인트가 breakForce로 끊겼거나 블록이 사라짐 — 상태만 정리.
                CleanupDockState();
            }
            else if (ShouldAutoRelease(out string why))
            {
                Undock(why);
            }
        }

        RecordFrameSnapshot();

        bool controllable = mover != null && mover.IsControlled && !mover.ExternallyDriven;

        if (!controllable || !IsCube)
        {
            aimed = null;
            hasCandidate = false;
            // 도킹 중이면(조작권을 넘긴 앵커 상태) 마커는 유지, 아니면 숨김.
            if (joint == null) HideReticle();
            else ShowReticle(dockedBlock != null ? dockedBlock.transform.position : transform.position, colorDocked);
            return;
        }

        // FindObjectsOfType로 씬을 훑는 건 매 프레임이 아니라 retargetInterval마다만(BlockCarrySystem과 동일 사유).
        if (joint == null && Time.time >= nextRetargetTime)
        {
            UpdateTarget();
            nextRetargetTime = Time.time + Mathf.Max(0.02f, retargetInterval);
        }

        UpdateReticle();

        if (Input.GetKeyDown(dockKey))
        {
            if (joint != null) Undock("수동 해제");
            else TryDock();
        }
    }

    private void RecordFrameSnapshot()
    {
        lastPos = transform.position;
        lastScale = transform.localScale;
    }

    // ── 타겟팅 / 후보 탐색 ────────────────────────────────────

    private void UpdateTarget()
    {
        aimed = null;
        hasCandidate = false;
        rejectReason = null;

        if (selfBox == null)
        {
            rejectReason = "정육면체 솔리드 콜라이더를 찾지 못했습니다.";
            return;
        }

        float bestSqr = dockRange * dockRange;
        Vector3 me = transform.position;
        SnapBlock best = null;

        blockBuf.Clear();
        blockBuf.AddRange(Object.FindObjectsOfType<SnapBlock>());
        foreach (SnapBlock b in blockBuf)
        {
            if (b == null) continue;
            Collider col = b.GetComponent<Collider>();
            if (col == null) continue;

            float sqr = (col.ClosestPoint(me) - me).sqrMagnitude;
            if (sqr > bestSqr) continue;

            bestSqr = sqr;
            best = b;
        }

        if (best == null) return;
        aimed = best;

        FindBestFacePair(best);
        if (hasCandidate)
            rejectReason = EvaluateReject(best);
    }

    // 정육면체 6면 × 대상 블록 6면 중, 거리·각도 조건을 통과하는 가장 가까운 쌍.
    private void FindBestFacePair(SnapBlock block)
    {
        hasCandidate = false;

        block.GetFaces(blockFaces);
        float bestSqr = snapDistance * snapDistance;
        float cosTol = Mathf.Cos(Mathf.Deg2Rad * Mathf.Clamp(snapAngleToleranceDeg, 0f, 179f));

        Vector3 c = selfBox.center;
        Vector3 h = selfBox.size * 0.5f;

        for (int i = 0; i < 6; i++)
        {
            Vector3 nLocal = LocalNormals[i];
            Vector3 selfCenter = selfBox.transform.TransformPoint(c + Vector3.Scale(nLocal, h));
            Vector3 selfNormal = selfBox.transform.TransformDirection(nLocal).normalized;

            for (int k = 0; k < blockFaces.Count; k++)
            {
                if (Vector3.Dot(selfNormal, -blockFaces[k].normal) < cosTol) continue;

                float sqr = (selfCenter - blockFaces[k].center).sqrMagnitude;
                if (sqr > bestSqr) continue;

                bestSqr = sqr;
                aimSelfFaceCenter = selfCenter;
                aimBlockFaceCenter = blockFaces[k].center;
                aimBlockFaceNormal = blockFaces[k].normal;
                hasCandidate = true;
            }
        }
    }

    // 도킹 불가면 사유, 가능하면 null.
    private string EvaluateReject(SnapBlock block)
    {
        CountStructure(block, out int count, out float mass);
        if (count > maxDockedBlocks)
            return $"구조물이 너무 큽니다 (블록 {count} > 상한 {maxDockedBlocks}).";
        if (mass > maxDockedMass)
            return $"구조물이 너무 무겁습니다 (질량 {mass:0.##} > 상한 {maxDockedMass:0.##}).";
        return null;
    }

    private void CountStructure(SnapBlock seed, out int count, out float mass)
    {
        var seen = new System.Collections.Generic.HashSet<SnapBlock>();
        var q = new System.Collections.Generic.Queue<SnapBlock>();
        seen.Add(seed);
        q.Enqueue(seed);
        mass = 0f;
        while (q.Count > 0)
        {
            SnapBlock b = q.Dequeue();
            Rigidbody rb = b.Body != null ? b.Body : b.GetComponent<Rigidbody>();
            if (rb != null) mass += rb.mass;
            foreach (SnapBlock n in b.ConnectedBlocks)
                if (n != null && seen.Add(n)) q.Enqueue(n);
        }
        count = seen.Count;
    }

    // ── 도킹 / 해제 ───────────────────────────────────────────

    private void TryDock()
    {
        if (aimed == null)
        {
            Debug.Log($"[CubeDock] {dockRange}칸 안에 SnapBlock이 없습니다 — 블록 가까이 서세요.");
            return;
        }
        if (!hasCandidate)
        {
            Debug.Log($"[CubeDock] 맞물릴 면이 없습니다 — 블록 면을 {snapDistance}칸 안, 나란히 맞대세요.");
            return;
        }
        if (rejectReason != null)
        {
            Debug.Log($"[CubeDock] {rejectReason}");
            return;
        }

        Rigidbody blockBody = aimed.Body != null ? aimed.Body : aimed.GetComponent<Rigidbody>();
        if (blockBody == null)
        {
            Debug.Log("[CubeDock] 블록에 Rigidbody가 없어 도킹할 수 없습니다.");
            return;
        }

        if (snapAlignOnDock)
        {
            // 면 법선 방향으로만 밀어 두 면을 같은 평면에 맞춘다(측면 위치·회전 미변경 → FreezeRotation과 무충돌).
            float gap = Vector3.Dot(aimBlockFaceCenter - aimSelfFaceCenter, aimBlockFaceNormal);
            transform.position += aimBlockFaceNormal * gap;
        }

        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        blockBody.velocity = Vector3.zero;
        blockBody.angularVelocity = Vector3.zero;

        joint = gameObject.AddComponent<ConfigurableJoint>();
        joint.connectedBody = blockBody;
        joint.autoConfigureConnectedAnchor = true;
        joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
        // 각축 Free — 회전은 Rigidbody.constraints(FreezeRotation)에 맡긴다(DreamThreadController와
        // 동일 이유). 정육면체 Rigidbody는 이미 FreezeRotation이라 월드 기준으로 절대 안 도는데,
        // 여기서 angular까지 Locked를 걸면 "블록 기준 상대 회전 고정"과 "월드 기준 회전 고정"이라는
        // 서로 다른 두 구속이 동시에 걸려 못 풀리는 각 오차를 솔버가 매 스텝 떠안는다 — 그 미해결
        // 오차가 선형 쪽으로 새어나와 조작도 안 했는데 위치가 끌려다니는 것처럼 보였다(플레이테스트로
        // 재현: 여러 블록이 결합된 구조물에 도킹하면 특히 두드러짐 — 상대 구조물도 자체 Weld
        // 조인트로 살짝씩 안 맞아 있어서 대상 자체가 절대 회전 기준으로 안 고정돼 있기 때문).
        joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
        // JointProjectionMode엔 Position 단독 값이 없다(None / PositionAndRotation 둘뿐 — Unity API).
        // 각 구속은 Free라 투영에서 "회전" 쪽은 사실상 손댈 게 없고, 선형 드리프트만 정리된다.
        joint.projectionMode = JointProjectionMode.PositionAndRotation;
        joint.projectionDistance = 0.01f;
        joint.enablePreprocessing = false;
        joint.breakForce = jointBreakForce;
        joint.breakTorque = jointBreakTorque;

        dockedBlock = aimed;
        aimed = null;
        hasCandidate = false;
        Debug.Log($"[CubeDock] '{dockedBlock.name}' 구조물에 도킹했습니다.");
    }

    private void Undock(string reason)
    {
        string blockName = dockedBlock != null ? dockedBlock.name : "블록";
        if (joint != null) Destroy(joint);
        CleanupDockState();
        Debug.Log($"[CubeDock] '{blockName}' 도킹을 해제했습니다 ({reason}).");
    }

    private void CleanupDockState()
    {
        joint = null;
        dockedBlock = null;
    }

    // ── 자동 해제 판정 ────────────────────────────────────────

    private bool ShouldAutoRelease(out string why)
    {
        why = null;

        if (mover != null && mover.ExternallyDriven)
        {
            why = "몸이 외부 기믹에 붙잡힘(실타래 등)";
            return true;
        }
        if (body != null && body.isKinematic)
        {
            why = "몸이 고정됨(굴리기 모드·벽 부착 등)";
            return true;
        }
        if (GetComponent("PlayerRollModeReceiver") != null)
        {
            why = "굴리기 모드 진입";
            return true;
        }
        if ((transform.position - lastPos).sqrMagnitude > teleportReleaseThreshold * teleportReleaseThreshold)
        {
            why = "몸이 순간이동함(리스폰·리셋)";
            return true;
        }
        if ((transform.localScale - lastScale).sqrMagnitude > 1e-4f)
        {
            why = "크기 변형 중";
            return true;
        }
        return false;
    }

    // ── 헬퍼 ─────────────────────────────────────────────────

    private BoxCollider ResolveSelfBox()
    {
        if (shapeId != null && shapeId.solidCollider is BoxCollider fromId) return fromId;
        foreach (BoxCollider bc in GetComponentsInChildren<BoxCollider>())
            if (!bc.isTrigger) return bc;
        return GetComponentInChildren<BoxCollider>();
    }

    // ── 조준점 ───────────────────────────────────────────────

    private void UpdateReticle()
    {
        if (joint != null)
        {
            ShowReticle(dockedBlock != null ? dockedBlock.transform.position : transform.position, colorDocked);
            return;
        }
        if (aimed == null)
        {
            HideReticle();
            return;
        }

        Color c = (hasCandidate && rejectReason == null) ? colorOk : colorReject;
        Vector3 pos = hasCandidate ? aimBlockFaceCenter : aimed.transform.position;
        ShowReticle(pos, c);
    }

    private void ShowReticle(Vector3 pos, Color c)
    {
        EnsureReticle();
        reticle.gameObject.SetActive(true);
        reticle.position = pos;
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
        go.name = "CubeDockReticle";
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.28f;
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
