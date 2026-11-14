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

        public bool HasCrossedUpperExit => qualifiedActor != null && kitchen != null &&
            kitchen.HasKey && qualifiedActor == PlayerControlSwitcher.ActiveTarget;
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
            if (kitchen == null || !kitchen.HasKey)
            {
                qualifiedActor = null;
                return;
            }

            if (qualifiedActor != null && !dispatchShaft.bounds.Contains(qualifiedActor.position))
                qualifiedActor = null;

            Transform actor = PlayerControlSwitcher.ActiveTarget;
            if (actor != null && upperLedge != null && upperLedge.bounds.Contains(actor.position))
                qualifiedActor = actor;
        }

        private void OnTriggerEnter(Collider other) => TryLoadFromLanding(other);
        private void OnTriggerStay(Collider other) => TryLoadFromLanding(other);

        private void TryLoadFromLanding(Collider other)
        {
            if (IsLoading || !HasCrossedUpperExit || kitchen.ExitReached) return;
            var mover = other.GetComponentInParent<PlayerMover>();
            if (mover == null || !mover.isActiveAndEnabled || mover.transform != qualifiedActor) return;

            int buildIndex = SceneUtility.GetBuildIndexByScenePath(destinationScene);
            if (buildIndex < 0 || !Application.CanStreamedLevelBeLoaded(buildIndex))
            {
                Debug.LogError("Map 3 is missing from the enabled scene list: " + destinationScene, this);
                enabled = false;
                return;
            }

            IsLoading = true;
            AsyncOperation loading = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
            if (loading == null)
            {
                Debug.LogError("Unity did not start loading Map 3: " + destinationScene, this);
                enabled = false;
                return;
            }
            kitchen.NotifyExit(mover.transform);
        }
    }
}
