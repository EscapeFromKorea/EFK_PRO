using UnityEngine;

namespace Map2KitchenDraft
{
    /// <summary>
    /// Map 2 sink pest laser preview. It notices horizontal movement by the currently
    /// controlled shape inside the assigned wash area, checks a clear sight line, and
    /// visibly follows that shape. Hit, death and restart rules are deliberately absent.
    /// All numerical values are review settings, not approved balance values.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class KitchenWashHazards : MonoBehaviour
    {
        [Header("Wash area and target")]
        [Tooltip("A trigger BoxCollider covering only the sink wash area.")]
        public BoxCollider activityBounds;

        [Tooltip("Maximum emitter-to-shape distance in metres. Preview setting.")]
        [Min(0.1f)] public float detectionRange = 70f;

        [Tooltip("Minimum horizontal Rigidbody speed that counts as movement. Preview setting.")]
        [Min(0f)] public float movementSpeedThreshold = 0.12f;

        [Tooltip("Solid geometry that can hide the shape or stop the visible beam.")]
        public LayerMask obstructionMask = ~0;

        [Header("Visual review settings")]
        [Tooltip("Angular follow speed. This is not an approved difficulty value.")]
        [Min(0.1f)] public float followDegreesPerSecond = 120f;

        [Tooltip("Neutral preview color until the pest-trap lamp color is approved.")]
        public Color previewColor = new Color(0.9f, 0.94f, 0.96f, 0.9f);

        [Min(0.01f)] public float beamWidth = 0.09f;

        public bool IsTracking { get; private set; }
        public Transform CurrentTarget { get; private set; }
        public Vector3 BeamEnd { get; private set; }

        private LineRenderer line;
        private Material generatedMaterial;
        private Vector3 aimDirection;
        private readonly RaycastHit[] sightHits = new RaycastHit[64];

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;

            if (line.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    generatedMaterial = new Material(shader);
                    line.sharedMaterial = generatedMaterial;
                }
            }
        }

        private void LateUpdate()
        {
            Transform actor = PlayerControlSwitcher.ActiveTarget;
            Vector3 targetPoint;
            if (!TryGetMovingVisibleTarget(actor, out targetPoint))
            {
                StopTracking();
                return;
            }

            Vector3 origin = transform.position;
            Vector3 desiredDirection = (targetPoint - origin).normalized;
            if (CurrentTarget != actor || aimDirection.sqrMagnitude < 0.0001f)
                aimDirection = desiredDirection;
            else
                aimDirection = Vector3.RotateTowards(
                    aimDirection, desiredDirection,
                    followDegreesPerSecond * Mathf.Deg2Rad * Time.deltaTime, 0f);

            CurrentTarget = actor;
            IsTracking = true;
            float aimDistance = Mathf.Min(detectionRange, Vector3.Distance(origin, targetPoint));
            BeamEnd = FirstObstructionPoint(origin, aimDirection, aimDistance);

            line.enabled = true;
            line.startWidth = beamWidth;
            line.endWidth = beamWidth;
            line.startColor = previewColor;
            line.endColor = previewColor;
            line.SetPosition(0, origin);
            line.SetPosition(1, BeamEnd);
        }

        /// <summary>True only when the active shape moves inside the sink and is directly visible.</summary>
        public bool TryGetMovingVisibleTarget(Transform actor, out Vector3 aimPoint)
        {
            aimPoint = Vector3.zero;
            if (actor == null || !actor.gameObject.activeInHierarchy || activityBounds == null ||
                !activityBounds.enabled || !activityBounds.gameObject.activeInHierarchy)
                return false;

            PlayerShapeIdentity shape = actor.GetComponent<PlayerShapeIdentity>();
            Rigidbody body = actor.GetComponent<Rigidbody>();
            if (shape == null || body == null || body.isKinematic)
                return false;

            PlayerMover mover = actor.GetComponent<PlayerMover>();
            if (mover != null && mover.ExternallyDriven)
                return false;

            Vector3 horizontalVelocity = body.velocity;
            horizontalVelocity.y = 0f;
            if (horizontalVelocity.sqrMagnitude < movementSpeedThreshold * movementSpeedThreshold)
                return false;

            aimPoint = shape.solidCollider != null ? shape.solidCollider.bounds.center : actor.position;
            if (!activityBounds.bounds.Contains(aimPoint))
                return false;

            Vector3 fromEmitter = aimPoint - transform.position;
            float distance = fromEmitter.magnitude;
            if (distance < 0.001f || distance > detectionRange)
                return false;

            return !HasObstruction(transform.position, fromEmitter / distance, distance, actor);
        }

        private bool HasObstruction(Vector3 origin, Vector3 direction, float distance, Transform actor)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, sightHits, distance,
                obstructionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = sightHits[i].collider;
                if (collider == null || collider.transform.IsChildOf(transform) ||
                    collider.transform.IsChildOf(actor))
                    continue;
                if (sightHits[i].distance < distance - 0.01f)
                    return true;
            }
            return false;
        }

        private Vector3 FirstObstructionPoint(Vector3 origin, Vector3 direction, float distance)
        {
            Vector3 end = origin + direction * distance;
            int count = Physics.RaycastNonAlloc(origin, direction, sightHits, distance,
                obstructionMask, QueryTriggerInteraction.Ignore);
            float nearest = distance;
            for (int i = 0; i < count; i++)
            {
                Collider collider = sightHits[i].collider;
                if (collider == null || collider.transform.IsChildOf(transform))
                    continue;
                if (sightHits[i].distance >= nearest)
                    continue;
                nearest = sightHits[i].distance;
                end = sightHits[i].point;
            }
            return end;
        }

        private void StopTracking()
        {
            IsTracking = false;
            CurrentTarget = null;
            aimDirection = Vector3.zero;
            if (line != null) line.enabled = false;
        }

        private void OnDisable() => StopTracking();

        private void OnDestroy()
        {
            if (generatedMaterial != null) Destroy(generatedMaterial);
        }
    }
}
