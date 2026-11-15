using System;
using UnityEngine;

namespace Map2KitchenDraft
{
    /// <summary>Chapter 2 spray timing and local recovery for the controlled shape.</summary>
    public sealed class KitchenSterilizer : MonoBehaviour
    {
        [Serializable]
        public struct Jet
        {
            public Transform head;
            public DraftWaterJet spray;
            public Vector3 center;
            public float travel;
            public float period;
            public float phase;
            public Transform recovery;

            public Vector3 PositionAt(float time)
            {
                float angle = (time / period + phase) * (2f * Mathf.PI);
                return center + Vector3.right * (travel * Mathf.Sin(angle));
            }
        }

        [SerializeField] private Jet[] jets = Array.Empty<Jet>();
        [SerializeField] private float contactRadius = .18f;
        private readonly Collider[] contacts = new Collider[24];
        private float showHitUntil = -1f;
        private Transform hitActor;
        private GUIStyle messageStyle;

        public int JetCount => jets.Length;
        internal Jet GetJet(int index) => jets[index];
        internal int HitCount { get; private set; }

        public void Configure(Jet[] stations)
        {
            jets = stations;
            UpdateHeads(0f);
        }

        private void FixedUpdate()
        {
            UpdateHeads(Time.fixedTime);
            Transform actor = PlayerControlSwitcher.ActiveTarget;
            if (actor == null || !actor.TryGetComponent(out Rigidbody body)) return;

            foreach (Jet jet in jets)
            {
                if (Mathf.Abs(body.position.z - jet.center.z) > 2f) continue;
                int count = Physics.OverlapCapsuleNonAlloc(jet.spray.WorldStart, jet.spray.WorldEnd,
                    contactRadius, contacts, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    if (contacts[i].attachedRigidbody != body) continue;
                    KitchenDraftController.MoveActor(actor, jet.recovery.position);
                    PlayerFollowCamera.SnapToTarget(actor);
                    hitActor = actor;
                    showHitUntil = Time.time + 1.7f;
                    HitCount++;
                    return;
                }
            }
        }

        private void UpdateHeads(float time)
        {
            foreach (Jet jet in jets)
                jet.head.position = jet.PositionAt(time);
        }

        private void OnGUI()
        {
            if (Time.time >= showHitUntil || PlayerControlSwitcher.ActiveTarget != hitActor) return;
            if (messageStyle == null)
            {
                messageStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 17
                };
            }
            GUI.Box(new Rect((Screen.width - 360f) * .5f, 22f, 360f, 34f),
                "물줄기에 맞아 안전 지점으로 돌아왔습니다", messageStyle);
        }
    }
}
