using System.Collections.Generic;
using UnityEngine;

/// <summary>CH8의 실제 출구 통과를 맵4 완료로 연결한다. 팀 기믹 상태를 읽고 맵 소유 안전 위치만 이동한다.</summary>
[RequireComponent(typeof(BoxCollider))]
public sealed class Lab_FinalExitFlow : MonoBehaviour
{
    public ManagerKeyDoor keyDoor;
    public ManagerChapterController chapter;
    public RoleAssignmentManager roles;
    public Collider exitFloor;
    public RespawnZone finalCheckpoint;
    public Transform[] finalSlots = new Transform[0];
    public float floorClearance = 0.06f;

    public bool Completed { get; private set; }
    public int CompletionCount { get; private set; }
    public PlayerShapeStats.ShapeKind FirstEntrantKind { get; private set; }
    public string LastFailure { get; private set; }

    private bool bound;
    private bool busy;
    private GUIStyle completionStyle, completionBodyStyle;

    private void Start() => Bind();
    private void OnEnable() => Bind();
    private void OnDisable() => Unbind();

    private void Bind()
    {
        if (bound || chapter == null || chapter.manager == null) return;
        chapter.manager.ChapterReset += ResetCompletion;
        bound = true;
    }

    private void Unbind()
    {
        if (!bound) return;
        if (chapter != null && chapter.manager != null) chapter.manager.ChapterReset -= ResetCompletion;
        bound = false;
    }

    private void ResetCompletion()
    {
        Completed = false;
        CompletionCount = 0;
        FirstEntrantKind = default;
        LastFailure = null;
        busy = false;
    }

    private void OnTriggerEnter(Collider other) => TryComplete(other);
    private void OnTriggerStay(Collider other) => TryComplete(other);

    private void TryComplete(Collider other)
    {
        if (Completed || busy || other == null || !other.CompareTag("Player")) return;
        PlayerMover entrant = other.GetComponentInParent<PlayerMover>();
        if (entrant == null) return;
        if (keyDoor == null || chapter == null || !keyDoor.IsOpen ||
            chapter.Current != ManagerChapterController.State.Asleep) return;

        busy = true;
        try
        {
            if (!TryGather(entrant, out string reason)) { LastFailure = reason; return; }
            Completed = true;
            CompletionCount++;
            FirstEntrantKind = entrant.GetComponent<PlayerShapeIdentity>().Kind;
            LastFailure = null;
            if (finalCheckpoint != null) RespawnController.SetCheckpoint(finalCheckpoint);
            Debug.Log("[Map4] 탈출 완료: 세 도형이 최종 구역에 모였다.", this);
        }
        finally { busy = false; }
    }

    private bool TryGather(PlayerMover entrant, out string reason)
    {
        reason = null;
        if (exitFloor == null || exitFloor.isTrigger || finalCheckpoint == null ||
            FindObjectOfType<RespawnController>() == null ||
            finalSlots == null || finalSlots.Length != 3)
        { reason = "최종 바닥·공용 복귀 컨트롤러·체크포인트·세 안전 위치 참조 누락"; return false; }

        BoxCollider trigger = GetComponent<BoxCollider>();
        PlayerShapeIdentity entrantIdentity = entrant.GetComponent<PlayerShapeIdentity>();
        Collider entrantSolid = entrantIdentity != null ? entrantIdentity.solidCollider : null;
        if (trigger == null || !trigger.isTrigger || entrantSolid == null || !entrantSolid.enabled ||
            entrantSolid.isTrigger || !trigger.bounds.Intersects(entrantSolid.bounds))
        { reason = "출구 통과 트리거와 실제 도형 몸통 교차 판정 실패"; return false; }

        PlayerShapeIdentity[] identities = FindObjectsOfType<PlayerShapeIdentity>();
        List<PlayerMover> movers = new List<PlayerMover>(3);
        foreach (PlayerShapeIdentity id in identities)
        {
            if (id == null) continue;
            PlayerMover mover = id.GetComponent<PlayerMover>();
            if (mover != null && mover.isActiveAndEnabled) movers.Add(mover);
        }
        if (movers.Count != 3 || !movers.Contains(entrant))
        { reason = "활성 도형 3개 또는 첫 통과 도형 확인 실패"; return false; }
        movers.Sort((a, b) => a.GetComponent<PlayerShapeIdentity>().Kind.CompareTo(b.GetComponent<PlayerShapeIdentity>().Kind));
        if (movers[0].GetComponent<PlayerShapeIdentity>().Kind == movers[1].GetComponent<PlayerShapeIdentity>().Kind ||
            movers[1].GetComponent<PlayerShapeIdentity>().Kind == movers[2].GetComponent<PlayerShapeIdentity>().Kind)
        { reason = "세 도형의 종류가 서로 다르지 않음"; return false; }

        Vector3[] destination = new Vector3[3];
        Vector3[] oldPosition = new Vector3[3];
        Quaternion[] oldRotation = new Quaternion[3];
        Vector3[] oldVelocity = new Vector3[3];
        Vector3[] oldAngular = new Vector3[3];
        Rigidbody[] bodies = new Rigidbody[3];
        Collider[] solids = new Collider[3];
        Bounds[] targetBounds = new Bounds[3];
        Bounds floor = exitFloor.bounds;

        for (int i = 0; i < 3; i++)
        {
            PlayerMover mover = movers[i];
            PlayerShapeIdentity id = mover.GetComponent<PlayerShapeIdentity>();
            bodies[i] = mover.GetComponent<Rigidbody>();
            solids[i] = id != null ? id.solidCollider : null;
            if (finalSlots[i] == null || bodies[i] == null || bodies[i].isKinematic ||
                mover.ExternallyDriven || solids[i] == null || !solids[i].enabled)
            { reason = $"안전 위치·몸체 준비 실패({i})"; return false; }
            oldPosition[i] = mover.transform.position;
            oldRotation[i] = mover.transform.rotation;
            oldVelocity[i] = bodies[i].velocity;
            oldAngular[i] = bodies[i].angularVelocity;
            Bounds current = solids[i].bounds;
            float footBelowPivot = mover.transform.position.y - current.min.y;
            destination[i] = new Vector3(finalSlots[i].position.x,
                floor.max.y + footBelowPivot + floorClearance, finalSlots[i].position.z);
            Vector3 delta = destination[i] - oldPosition[i];
            targetBounds[i] = new Bounds(current.center + delta, current.size);
            Bounds target = targetBounds[i];
            if (target.min.x < floor.min.x + 0.1f || target.max.x > floor.max.x - 0.1f ||
                target.min.z < floor.min.z + 0.1f || target.max.z > floor.max.z - 0.1f ||
                target.min.y < floor.max.y + 0.03f)
            { reason = $"안전 위치가 탈출 바닥 밖이거나 발이 바닥에 겹침({i})"; return false; }
            for (int j = 0; j < i; j++)
                if (targetBounds[i].Intersects(targetBounds[j]))
                { reason = $"안전 위치끼리 충돌({j},{i})"; return false; }

            Collider[] hits = Physics.OverlapBox(target.center, target.extents * 0.98f,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider hit in hits)
            {
                if (hit == exitFloor || hit.GetComponentInParent<PlayerMover>() != null) continue;
                reason = $"안전 위치에 지형/장치 충돌({i}, {hit.name})";
                return false;
            }
        }

        try
        {
            for (int i = 0; i < 3; i++)
            {
                bodies[i].velocity = Vector3.zero;
                bodies[i].angularVelocity = Vector3.zero;
                bodies[i].position = destination[i];
                movers[i].transform.position = destination[i];
            }
            Physics.SyncTransforms();
            for (int i = 0; i < 3; i++)
            {
                Bounds b = solids[i].bounds;
                if (Vector3.Distance(movers[i].transform.position, destination[i]) > 0.03f ||
                    b.min.y < floor.max.y + 0.03f || !floor.Contains(new Vector3(b.center.x, floor.center.y, b.center.z)))
                    throw new System.InvalidOperationException($"최종 위치 사후 확인 실패({i})");
            }
            if (roles != null)
                for (int i = 0; i < 3; i++) roles.ClosePanelsOf(movers[i]);
            return true;
        }
        catch (System.Exception e)
        {
            for (int i = 0; i < 3; i++)
            {
                bodies[i].position = oldPosition[i];
                movers[i].transform.SetPositionAndRotation(oldPosition[i], oldRotation[i]);
                bodies[i].velocity = oldVelocity[i];
                bodies[i].angularVelocity = oldAngular[i];
            }
            Physics.SyncTransforms();
            reason = e.Message;
            return false;
        }
    }

    private void OnGUI()
    {
        if (!Completed) return;
        if (completionStyle == null)
        {
            completionStyle = new GUIStyle(GUI.skin.box) { fontSize = 28,
                alignment = TextAnchor.UpperCenter, padding = new RectOffset(12, 12, 12, 12) };
            completionBodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 18,
                alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }
        float width = Mathf.Min(560f, Screen.width - 32f);
        Rect box = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.18f, width, 128f);
        GUI.Box(box, "맵4 탈출 완료", completionStyle);
        GUI.Label(new Rect(box.x + 20f, box.y + 56f, box.width - 40f, 56f),
            "세 도형이 함께 출구에 도착했습니다.", completionBodyStyle);
    }
}
