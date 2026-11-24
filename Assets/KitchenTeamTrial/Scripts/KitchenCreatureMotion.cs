using System;
using System.Collections.Generic;
using UnityEngine;

namespace Map2KitchenDraft
{
    /// <summary>Animates the creature artwork from pursuit movement, without moving the gameplay root.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]
    public sealed class KitchenCreatureMotion : MonoBehaviour
    {
        private readonly List<Limb> joints = new List<Limb>();
        private KitchenPursuit pursuit;
        private Transform body;
        private Vector3 bodyRestPosition;
        private Quaternion facing;
        private float previousDistance;
        private float gait;
        private float stride;

        private struct Limb
        {
            public Transform pivot;
            public Quaternion rest;
            public bool alternate;
            public bool left;
            public bool antenna;
            public bool tail;
        }

        private void Awake()
        {
            pursuit = GetComponentInParent<KitchenPursuit>();
            if (pursuit == null)
            {
                enabled = false;
                return;
            }

            foreach (Transform part in GetComponentsInChildren<Transform>(true))
            {
                string label = part.name;
                if (label == "Body")
                {
                    body = part;
                    bodyRestPosition = part.localPosition;
                }
                bool leg = label.StartsWith("Leg ", StringComparison.Ordinal);
                bool antenna = label == "Antenna L" || label == "Antenna R";
                bool tail = label == "Tail";
                if (!leg && !antenna && !tail) continue;
                bool left = label.EndsWith("L", StringComparison.Ordinal);
                bool oppositeRow = pursuit.Kind == CreatureKind.Rat
                    ? label.StartsWith("Leg B", StringComparison.Ordinal)
                    : label.StartsWith("Leg M", StringComparison.Ordinal);
                joints.Add(new Limb
                {
                    pivot = part, rest = part.localRotation, left = left, antenna = antenna, tail = tail,
                    alternate = left ^ oppositeRow
                });
            }
            previousDistance = pursuit.CurrentPathDistance;
            facing = transform.rotation;
        }

        private void FixedUpdate()
        {
            float distance = pursuit.CurrentPathDistance;
            float travelled = Mathf.Abs(distance - previousDistance);
            previousDistance = distance;
            // Arming and resetting can snap the creature along the route; those snaps are not steps.
            float dt = Time.fixedDeltaTime;
            float motion = pursuit.IsChasing && travelled < .7f ? travelled : 0f;
            float target = Mathf.Clamp01(motion / (Mathf.Max(pursuit.speed, .01f) * dt));
            gait = Mathf.Lerp(gait, target, 1f - Mathf.Exp(-12f * dt));
            stride += motion * (pursuit.Kind == CreatureKind.Cockroach ? 17f : 8f);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            bool roach = pursuit.Kind == CreatureKind.Cockroach;
            float idle = Time.time * (roach ? 3.4f : 2.5f);
            float wave = Mathf.Sin(stride);

            facing = Quaternion.RotateTowards(facing, transform.parent.rotation, 420f * dt);
            transform.rotation = facing;
            if (body != null)
                body.localPosition = bodyRestPosition + Vector3.up *
                    (Mathf.Sin(idle) * .018f + Mathf.Abs(wave) * gait * .035f);

            foreach (Limb limb in joints)
            {
                float sign = limb.left ? -1f : 1f;
                float step = limb.alternate ? -wave : wave;
                Quaternion pose;
                if (limb.tail)
                    pose = Quaternion.Euler(0f, Mathf.Sin(idle + stride * .35f) * (5f + 9f * gait), 0f);
                else if (limb.antenna)
                    pose = Quaternion.Euler(Mathf.Sin(idle + sign) * 7f, sign * Mathf.Sin(idle * .84f) * 14f, 0f);
                else
                    pose = roach
                        ? Quaternion.Euler(0f, step * 17f * gait, sign * Mathf.Max(0f, step) * 11f * gait)
                        : Quaternion.Euler(step * 24f * gait, 0f, 0f);
                limb.pivot.localRotation = limb.rest * pose;
            }
        }
    }
}
