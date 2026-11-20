using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Map2KitchenDraft
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class KitchenMapHandoff : MonoBehaviour
    {
        [SerializeField] private KitchenDraftController kitchen;
        [SerializeField] private BoxCollider upperLedge;
        [SerializeField] private BoxCollider dispatchShaft;
        [SerializeField] private string destinationScene;

        private Transform qualifiedActor;
        private readonly HashSet<Transform> mpQualified = new HashSet<Transform>(); // [멀티] 서버: 상단 출구를 지나 배출구로 내려가는 몸들

        public bool HasCrossedUpperExit => qualifiedActor != null && kitchen != null &&
            kitchen.HasKey && qualifiedActor == KitchenDraftController.ControlledTransform();
        public bool IsLoading { get; private set; }

        public bool ContainsDispatchRoute(Vector3 position) =>
            dispatchShaft != null && dispatchShaft.bounds.Contains(position);

        public void Configure(KitchenDraftController controller, BoxCollider ledge, BoxCollider shaft,
            string scenePath)
        {
            kitchen = controller;
            upperLedge = ledge;
            dispatchShaft = shaft;
            destinationScene = scenePath;
        }

        private void FixedUpdate()
        {
            if (MpSync.Active) { if (MpSync.IsServer) TrackMpBodies(); return; }
            if (kitchen == null || !kitchen.HasKey)
            {
                qualifiedActor = null;
                return;
            }

            if (qualifiedActor != null && !dispatchShaft.bounds.Contains(qualifiedActor.position))
                qualifiedActor = null;

            Transform actor = KitchenDraftController.ControlledTransform();
            if (actor != null && upperLedge != null && upperLedge.bounds.Contains(actor.position))
                qualifiedActor = actor;
        }

        // 팀 규칙: 열쇠를 얻은 뒤 누구든 한 명이 상단 출구를 지나 배출구에 닿으면 클리어(맵 1·4와 같다, 2026-11-18 확정)
        private void TrackMpBodies()
        {
            if (kitchen == null || !kitchen.HasKey || upperLedge == null || dispatchShaft == null) { mpQualified.Clear(); return; }
            mpQualified.RemoveWhere(t => t == null || !dispatchShaft.bounds.Contains(t.position));
            foreach (PlayerMover body in MpSync.Bodies)
                if (upperLedge.bounds.Contains(body.transform.position)) mpQualified.Add(body.transform);
        }

        private void OnTriggerEnter(Collider other) => TryLoadFromLanding(other);
        private void OnTriggerStay(Collider other) => TryLoadFromLanding(other);

        private void TryLoadFromLanding(Collider other)
        {
            if (MpSync.IsClient) return;
            var mover = other.GetComponentInParent<PlayerMover>();
            if (MpSync.Active)
            {
                if (IsLoading || kitchen == null || !kitchen.HasKey || kitchen.ExitReached ||
                    mover == null || !mpQualified.Contains(mover.transform)) return;
            }
            else
            {
                if (IsLoading || !HasCrossedUpperExit || kitchen.ExitReached) return;
                if (mover == null || !mover.isActiveAndEnabled || mover.transform != qualifiedActor) return;
            }

            if (LevelManager.Instance == null)
            {
                Debug.LogError("LevelManager is missing; the stage cannot be completed.", this);
                return;
            }
            IsLoading = true;
            kitchen.NotifyExit(mover.transform);
            LevelManager.Instance.CompleteLevel();
        }
    }
}
