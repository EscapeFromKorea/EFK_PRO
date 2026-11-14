using UnityEngine;

namespace Map2KitchenDraft
{
    /// <summary>
    /// A Map2 creature that follows the authored kitchen route toward the selected shape.
    /// The route is the movement boundary: this component never cuts a corner through a wall,
    /// jumps a belt gap, or leaves the sink. Capture and player recovery belong to a separate rule.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class KitchenPursuit : MonoBehaviour
    {
        [Header("Kitchen scene references")]
        public KitchenDraftController draft;
        public DraftCreatureMarker marker;
        [Tooltip("Optional movable visual child. Leave empty to move the whole pursuer object.")]
        public Transform facingVisual;

        [Header("Authored route, in world coordinates")]
        public Vector3[] route;
        [Tooltip("Provisional review value in metres per second.")]
        public float speed = 4f;
        [Tooltip("Initial distance behind the selected shape along the route.")]
        public float spawnGap = 5f;
        [Min(0f), Tooltip("Time the creature stays in place after the pursuit is triggered.")]
        public float reactionDelay = 1.5f;
        [Tooltip("Maximum horizontal distance from the route at which a player can engage pursuit.")]
        public float engageWidth = 5f;

        public bool IsChasing { get; private set; }
        public bool HasReachedEnd { get; private set; }
        public float CurrentPathDistance { get; private set; }
        public float PathLength { get; private set; }
        public CreatureKind Kind => marker != null ? marker.kind : CreatureKind.Rat;
        public int ChapterIndex => marker != null ? marker.chapterIndex : -1;

        private Rigidbody body;
        private float[] cumulative;
        private bool armed;
        private float movementStartsAt;
        private Vector3 initialPosition;
        private Renderer[] visuals;
        private const float MaxHeightDifference = 2.4f;

        /// <summary>Configure one pursuer from a static review marker and an authored, surface-following route.</summary>
        public void Configure(KitchenDraftController controller, DraftCreatureMarker sourceMarker,
            Vector3[] authoredRoute, float movementSpeed, float initialGap, float width = 5f)
        {
            draft = controller;
            marker = sourceMarker;
            route = authoredRoute != null ? (Vector3[])authoredRoute.Clone() : null;
            speed = Mathf.Max(0f, movementSpeed);
            spawnGap = Mathf.Max(0f, initialGap);
            engageWidth = Mathf.Max(0f, width);
            RebuildPath();
            ResetPursuit();
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            // The creature is a moving review hazard, not a solid obstacle. A later
            // capture rule may listen for trigger contact without changing its route.
            foreach (Collider collider in GetComponents<Collider>()) collider.isTrigger = true;
            initialPosition = transform.position;
            visuals = GetComponentsInChildren<Renderer>(true);
            RebuildPath();
            ResetPursuit();
        }

        private void OnEnable()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (cumulative == null) RebuildPath();
        }

        private void OnValidate()
        {
            speed = Mathf.Max(0f, speed);
            spawnGap = Mathf.Max(0f, spawnGap);
            reactionDelay = Mathf.Max(0f, reactionDelay);
            engageWidth = Mathf.Max(0f, engageWidth);
            RebuildPath();
        }

        private void Update()
        {
            if (draft == null || marker == null || cumulative == null || cumulative.Length < 2) return;
            Transform actor = draft.ActivePlayer;
            if (Kind == CreatureKind.Rat && draft.HasKey)
            {
                Complete(false);
                return;
            }
            if (HasReachedEnd)
            {
                SetVisualsActive(false);
                return;
            }
            if (IsSortingPast(actor))
            {
                Complete(false);
                return;
            }
            if (!IsEligible(actor))
            {
                IsChasing = false;
                SetVisualsActive(false);
                return;
            }

            float playerDistance = Project(actor.position, out float lateralDistance,
                out float heightDifference, out bool pastEnd);
            bool nearRoute = lateralDistance <= engageWidth && heightDifference <= MaxHeightDifference;
            if (!nearRoute)
            {
                IsChasing = false;
                SetVisualsActive(false);
                return;
            }
            if (pastEnd && armed && CurrentPathDistance >= PathLength - .01f)
            {
                Complete(false);
                return;
            }
            SetVisualsActive(true);
            if (!armed)
            {
                // Keep the rat at the entrance until the runner has a real head start.
                // The cockroach always starts at the drain.
                if (Kind == CreatureKind.Rat && playerDistance < Mathf.Min(spawnGap, PathLength))
                    return;
                ArmAt(Kind == CreatureKind.Cockroach ? 0f :
                    Mathf.Max(0f, playerDistance - spawnGap));
            }
            IsChasing = Time.time >= movementStartsAt;
        }

        private void FixedUpdate()
        {
            if (!armed || HasReachedEnd || draft == null || marker == null || body == null ||
                Time.time < movementStartsAt) return;
            Transform actor = draft.ActivePlayer;
            if (!IsEligible(actor) || (Kind == CreatureKind.Rat && draft.HasKey) || IsSortingPast(actor))
            {
                IsChasing = false;
                return;
            }

            float playerDistance = Project(actor.position, out float lateralDistance,
                out float heightDifference, out _);
            if (lateralDistance > engageWidth || heightDifference > MaxHeightDifference)
            {
                IsChasing = false;
                return;
            }
            IsChasing = true;

            // Follow the active shape both forward and back along the baked route;
            // this does not let the creature cut through a kitchen fixture.
            float goal = Mathf.Clamp(playerDistance, 0f, PathLength);
            float next = Mathf.MoveTowards(CurrentPathDistance, goal, speed * Time.fixedDeltaTime);
            Vector3 destination = PointAt(next);
            Vector3 movement = destination - body.position;
            body.MovePosition(destination);
            CurrentPathDistance = next;
            if (facingVisual != null && movement.sqrMagnitude > .0001f)
            {
                Vector3 flat = new Vector3(movement.x, 0f, movement.z);
                if (flat.sqrMagnitude > .0001f)
                    facingVisual.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
            if (CurrentPathDistance >= PathLength - .01f)
                Complete(false);
        }

        /// <summary>Rearm and return to the authored start, including after a review reset.</summary>
        public void ResetPursuit()
        {
            armed = false;
            movementStartsAt = 0f;
            IsChasing = false;
            HasReachedEnd = false;
            CurrentPathDistance = 0f;
            Vector3 start = route != null && route.Length > 0 ? route[0] : initialPosition;
            transform.position = start;
            if (body != null) body.position = start;
            SetVisualsActive(false);
        }

        private bool IsEligible(Transform actor)
        {
            if (actor == null || !actor.gameObject.activeInHierarchy) return false;
            PlayerMover mover = actor.GetComponentInParent<PlayerMover>();
            if (mover == null || !mover.isActiveAndEnabled) return false;
            if (marker.kind == CreatureKind.Cockroach)
            {
                if (marker.activityBounds == null) return false;
                Bounds basin = marker.activityBounds.bounds;
                // Some shape roots settle below the belt surface even when the
                // collision hull is standing on it. Use the body occupying the sink.
                var shape = mover.GetComponent<PlayerShapeIdentity>();
                return shape != null && shape.solidCollider != null
                    ? basin.Intersects(shape.solidCollider.bounds)
                    : basin.Contains(mover.transform.position);
            }
            // Rats have small legacy visual-marker bounds, not traversal bounds.
            return marker.kind == CreatureKind.Rat && !draft.HasKey;
        }

        private bool IsSortingPast(Transform actor)
        {
            return marker != null && marker.kind == CreatureKind.Rat &&
                marker.chapterIndex == 5 && actor != null &&
                actor.position.x >= 42f && Mathf.Abs(actor.position.z - 100f) <= 8f &&
                Mathf.Abs(actor.position.y - route[route.Length - 1].y) <= MaxHeightDifference;
        }

        private void Complete(bool showAtEnd)
        {
            HasReachedEnd = true;
            IsChasing = false;
            armed = false;
            SetVisualsActive(showAtEnd);
        }

        private void SetVisualsActive(bool visible)
        {
            if (!Application.isPlaying) return;
            if (visuals == null) visuals = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer visual in visuals)
                if (visual != null) visual.enabled = visible;
        }

        private void ArmAt(float distance)
        {
            CurrentPathDistance = Mathf.Clamp(distance, 0f, PathLength);
            Vector3 start = PointAt(CurrentPathDistance);
            transform.position = start;
            if (body != null) body.position = start;
            armed = true;
            movementStartsAt = Time.time + reactionDelay;
        }

        private void RebuildPath()
        {
            if (route == null || route.Length < 2)
            {
                cumulative = null;
                PathLength = 0f;
                return;
            }
            cumulative = new float[route.Length];
            for (int i = 1; i < route.Length; i++)
                cumulative[i] = cumulative[i - 1] + Vector3.Distance(route[i - 1], route[i]);
            PathLength = cumulative[cumulative.Length - 1];
            if (PathLength < .01f) cumulative = null;
        }

        private float Project(Vector3 point, out float horizontalDistance,
            out float heightDifference, out bool pastEnd)
        {
            float best = float.PositiveInfinity;
            float progress = 0f;
            float bestHeight = float.PositiveInfinity;
            for (int i = 1; i < route.Length; i++)
            {
                Vector2 a = new Vector2(route[i - 1].x, route[i - 1].z);
                Vector2 b = new Vector2(route[i].x, route[i].z);
                Vector2 p = new Vector2(point.x, point.z);
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                float distance = Vector2.Distance(p, a + ab * t);
                if (distance < best)
                {
                    best = distance;
                    progress = Mathf.Lerp(cumulative[i - 1], cumulative[i], t);
                    bestHeight = Mathf.Abs(point.y - Mathf.Lerp(route[i - 1].y, route[i].y, t));
                }
            }
            pastEnd = false;
            // Extend the last segment's corridor as a finite end cap. The player can
            // leave a belt while the pursuer still recognizes that the endpoint was passed.
            int last = route.Length - 1;
            Vector2 lastPoint = new Vector2(route[last].x, route[last].z);
            Vector2 priorPoint = new Vector2(route[last - 1].x, route[last - 1].z);
            Vector2 direction = (lastPoint - priorPoint).normalized;
            Vector2 fromEnd = new Vector2(point.x, point.z) - lastPoint;
            float beyondEnd = Vector2.Dot(fromEnd, direction);
            if (direction.sqrMagnitude > .5f && beyondEnd > 0f && beyondEnd <= 12f)
            {
                float crossTrack = Mathf.Abs(direction.x * fromEnd.y - direction.y * fromEnd.x);
                if (crossTrack <= engageWidth)
                {
                    best = crossTrack;
                    bestHeight = Mathf.Abs(point.y - route[last].y);
                    progress = PathLength;
                    pastEnd = true;
                }
            }
            horizontalDistance = best;
            heightDifference = bestHeight;
            return progress;
        }

        private Vector3 PointAt(float distance)
        {
            if (route == null || route.Length == 0) return transform.position;
            if (cumulative == null || distance <= 0f) return route[0];
            if (distance >= PathLength) return route[route.Length - 1];
            for (int i = 1; i < route.Length; i++)
            {
                if (distance > cumulative[i]) continue;
                float span = cumulative[i] - cumulative[i - 1];
                float t = span > .0001f ? (distance - cumulative[i - 1]) / span : 0f;
                return Vector3.Lerp(route[i - 1], route[i], t);
            }
            return route[route.Length - 1];
        }

        private void OnDrawGizmosSelected()
        {
            if (route == null) return;
            Gizmos.color = marker != null && marker.kind == CreatureKind.Cockroach ? Color.yellow : Color.red;
            for (int i = 1; i < route.Length; i++) Gizmos.DrawLine(route[i - 1], route[i]);
        }
    }
}
