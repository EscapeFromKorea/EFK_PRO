using UnityEngine;

namespace Map2KitchenDraft
{
    /// <summary>Kitchen map review controls and the confirmed key-only exit condition.</summary>
    public sealed class KitchenDraftController : MonoBehaviour
    {
        public Transform[] chapterStarts;
        public string[] chapterNames;
        public DraftChapterRegion[] chapters;
        // Kept only for older serialized draft scenes; chapter selection uses 3D regions.
        public float[] chapterBoundaries;
        public Transform key;
        public Transform doorVisual;
        public Collider doorBlocker;
        public Vector3 doorOpenOffset = new Vector3(0f, 3f, 0f);
        public Transform finalDropPoint;
        public Camera previewCamera;
        // Set by the rebuilt scene to the actual lower, hollow sink interior.
        // The record supports review; the physical kitchen layout defines the route.
        public BoxCollider sinkInteriorVolume;
        public float interactionRadius = 2f;

        public bool HasKey { get; private set; }
        public bool ExitReached { get; private set; }
        public bool HasVisitedSink { get; private set; }
        // Zero-based; display text uses +1.
        public int CurrentChapter { get; private set; }

        public Transform ActivePlayer
        {
            get
            {
                Transform switched = PlayerControlSwitcher.ActiveTarget;
                if (switched != null) return switched;
                PlayerMover fallback = FindFirstObjectByType<PlayerMover>();
                return fallback != null ? fallback.transform : null;
            }
        }

        private Vector3 closedDoorLocalPosition;
        private bool doorPositionCaptured;
        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle smallStyle;
        private GUIStyle accentStyle;
        private Texture2D panelPixel;
        private bool showReviewHelp;
        private KitchenMapHandoff mapHandoff;

        private void Awake()
        {
            mapHandoff = FindFirstObjectByType<KitchenMapHandoff>();
            CaptureDoorPosition();
            ApplyDoorState();
        }

        private void Update()
        {
            Transform actor = ActivePlayer;
            if (actor != null)
            {
                CurrentChapter = FindChapter(actor.position);
                TrackSinkVisit(actor);
                if (Input.GetKeyDown(KeyCode.E)) TryCollectKey(actor);
            }

            // These shortcuts only support review of the first draft. They are not checkpoints.
            for (int index = 0; index < 7; index++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + index)))
                {
                    PreviewChapter(index);
                    break;
                }
            }
            if (Input.GetKeyDown(KeyCode.R)) ResetDraft();
            if (Input.GetKeyDown(KeyCode.F1)) showReviewHelp = !showReviewHelp;
        }

        /// <summary>Records an active shape reaching the actual lower sink floor.</summary>
        public bool TrackSinkVisit(Transform actor)
        {
            if (HasVisitedSink || sinkInteriorVolume == null || actor == null) return false;
            PlayerMover mover = actor.GetComponentInParent<PlayerMover>();
            if (mover == null || !mover.isActiveAndEnabled) return false;
            Transform selected = PlayerControlSwitcher.ActiveTarget;
            if (selected != null && selected != mover.transform) return false;
            if (mover.transform.position.y >= 4.1f ||
                !sinkInteriorVolume.bounds.Contains(mover.transform.position)) return false;

            HasVisitedSink = true;
            return true;
        }

        public void PreviewChapter(int zeroBasedIndex)
        {
            if (chapterStarts == null || zeroBasedIndex < 0 || zeroBasedIndex >= chapterStarts.Length) return;
            Transform start = chapterStarts[zeroBasedIndex];
            Transform actor = ActivePlayer;
            if (start == null || actor == null) return;

            MoveActor(actor, start.position);
            CurrentChapter = zeroBasedIndex;
            PlayerFollowCamera.SnapToTarget(actor);
        }

        public void ResetDraft()
        {
            HasKey = false;
            ExitReached = false;
            HasVisitedSink = false;
            if (key != null) key.gameObject.SetActive(true);
            ApplyDoorState();
            foreach (var pursuer in FindObjectsByType<KitchenPursuit>(FindObjectsSortMode.None))
                pursuer.ResetPursuit();
            // Only the selected shape moves. Other shapes retain their positions.
            PreviewChapter(0);
        }

        public bool TryCollectKey(Transform actor)
        {
            if (HasKey || key == null || actor == null) return false;
            PlayerMover mover = actor.GetComponentInParent<PlayerMover>();
            if (mover == null || !mover.isActiveAndEnabled) return false;
            if (Vector3.Distance(mover.transform.position, key.position) > interactionRadius) return false;

            HasKey = true;
            key.gameObject.SetActive(false);
            ApplyDoorState();
            return true;
        }

        public void NotifyExit(Transform actor)
        {
            if (!HasKey || ExitReached || actor == null) return;
            PlayerMover mover = actor.GetComponentInParent<PlayerMover>();
            if (mover == null || !mover.isActiveAndEnabled) return;
            ExitReached = true;
            // In the playable scene KitchenMapHandoff begins the scene load before recording arrival.
        }

        private void CaptureDoorPosition()
        {
            if (doorVisual == null || doorPositionCaptured) return;
            closedDoorLocalPosition = doorVisual.localPosition;
            doorPositionCaptured = true;
        }

        private void ApplyDoorState()
        {
            CaptureDoorPosition();
            if (doorBlocker != null) doorBlocker.enabled = !HasKey;
            if (doorVisual != null && doorPositionCaptured)
                doorVisual.localPosition = closedDoorLocalPosition + (HasKey ? doorOpenOffset : Vector3.zero);
        }

        private int FindChapter(Vector3 playerPosition)
        {
            // The food dispatch shaft continues CH7 beyond the cooking chapter volume.
            if (HasKey && mapHandoff != null && mapHandoff.ContainsDispatchRoute(playerPosition))
                return 6;

            int index = 0;
            float nearestInside = float.PositiveInfinity;
            bool insideAny = false;
            if (chapters != null)
            {
                for (int i = 0; i < chapters.Length; i++)
                {
                    DraftChapterRegion region = chapters[i];
                    if (region == null || region.volume == null || !region.volume.bounds.Contains(playerPosition)) continue;
                    float distance = region.start != null
                        ? (playerPosition - region.start.position).sqrMagnitude : 0f;
                    if (!insideAny || distance < nearestInside)
                    {
                        insideAny = true;
                        nearestInside = distance;
                        index = region.index;
                    }
                }
            }
            if (insideAny) return Mathf.Clamp(index, 0, 6);

            float nearest = float.PositiveInfinity;
            if (chapters != null)
            {
                for (int i = 0; i < chapters.Length; i++)
                {
                    DraftChapterRegion region = chapters[i];
                    if (region == null || region.start == null) continue;
                    float distance = (playerPosition - region.start.position).sqrMagnitude;
                    if (distance < nearest)
                    {
                        nearest = distance;
                        index = region.index;
                    }
                }
            }
            if (float.IsPositiveInfinity(nearest) && chapterStarts != null)
            {
                for (int i = 0; i < chapterStarts.Length; i++)
                {
                    if (chapterStarts[i] == null) continue;
                    float distance = (playerPosition - chapterStarts[i].position).sqrMagnitude;
                    if (distance < nearest)
                    {
                        nearest = distance;
                        index = i;
                    }
                }
            }
            return Mathf.Clamp(index, 0, 6);
        }

        internal static void MoveActor(Transform actor, Vector3 destination)
        {
            Rigidbody body = actor.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = destination;
            }
            actor.position = destination;
            Physics.SyncTransforms();
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            EnsureHud();

            DraftChapterRegion currentRegion = null;
            if (chapters != null)
                for (int i = 0; i < chapters.Length; i++)
                    if (chapters[i] != null && chapters[i].index == CurrentChapter)
                    { currentRegion = chapters[i]; break; }
            string chapterName = currentRegion != null && !string.IsNullOrEmpty(currentRegion.chapterName)
                ? currentRegion.chapterName
                : chapterNames != null && CurrentChapter < chapterNames.Length
                    ? chapterNames[CurrentChapter] : "주방";
            string status = ExitReached ? "음식 배출구 아래에 도착했습니다"
                : CurrentChapter == 6 && HasKey ? "상단 출구가 열렸습니다"
                : currentRegion != null && !string.IsNullOrEmpty(currentRegion.objective)
                    ? currentRegion.objective : "벨트를 따라 다음 구역으로 이동하세요";

            Matrix4x4 previousMatrix = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                Fill(new Rect(20, 20, 580, 124), new Color(.055f, .09f, .11f, .88f));
                Fill(new Rect(20, 20, 5, 124), new Color(.49f, .84f, .78f, 1f));
                GUI.Label(new Rect(42, 30, 300, 25), $"THE AXIOM   /   CH {CurrentChapter + 1:00} — 07", smallStyle);
                GUI.Label(new Rect(42, 56, 530, 36), chapterName, titleStyle);
                GUI.Label(new Rect(42, 98, 530, 38), status, labelStyle);

                Fill(new Rect(1066, 20, 194, 46), HasKey
                    ? new Color(.52f, .34f, .11f, .95f) : new Color(.055f, .09f, .11f, .88f));
                GUI.Label(new Rect(1080, 30, 168, 28), HasKey ? "열쇠  획득" : "열쇠  미획득", accentStyle);

                Fill(new Rect(20, 674, 790, 29), new Color(.055f, .09f, .11f, .88f));
                GUI.Label(new Rect(32, 678, 766, 23),
                    "WASD 이동   마우스 시점   Space 점프   Tab 도형   E 상호작용   F1 안내", smallStyle);

                if (showReviewHelp)
                {
                    Fill(new Rect(870, 78, 390, 185), new Color(.055f, .09f, .11f, .94f));
                    Fill(new Rect(870, 78, 390, 4), new Color(.49f, .84f, .78f, 1f));
                    GUI.Label(new Rect(890, 93, 350, 33), "플레이 안내", titleStyle);
                    GUI.Label(new Rect(890, 133, 350, 30), "1–7  구역 미리보기     R  처음부터", labelStyle);
                    GUI.Label(new Rect(890, 165, 350, 30), "Esc  커서 해제     F1  안내 닫기", labelStyle);
                    GUI.Label(new Rect(890, 209, 350, 30),
                        HasVisitedSink ? "싱크대 내부 통과 확인" : "싱크대 내부를 지나가세요", smallStyle);
                }
            }
            finally { GUI.matrix = previousMatrix; }
        }

        private void EnsureHud()
        {
            if (titleStyle != null) return;
            panelPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            panelPixel.SetPixel(0, 0, Color.white);
            panelPixel.Apply();
            panelPixel.hideFlags = HideFlags.HideAndDontSave;

            Font koreanFont = null;
            string[] installedFonts = Font.GetOSInstalledFontNames();
            for (int i = 0; i < installedFonts.Length; i++)
            {
                if (!installedFonts[i].Contains("Malgun Gothic") &&
                    !installedFonts[i].Contains("맑은 고딕")) continue;
                koreanFont = Font.CreateDynamicFontFromOSFont(installedFonts[i], 20);
                break;
            }

            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = Color.white;
            titleStyle.font = koreanFont;
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            labelStyle.normal.textColor = new Color(.85f, .91f, .90f);
            labelStyle.font = koreanFont;
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            smallStyle.normal.textColor = new Color(.71f, .80f, .79f);
            smallStyle.font = koreanFont;
            accentStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter };
            accentStyle.normal.textColor = new Color(1f, .87f, .60f);
            accentStyle.font = koreanFont;
        }

        private void Fill(Rect rect, Color tint)
        {
            Color previousColor = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, panelPixel, ScaleMode.StretchToFill);
            GUI.color = previousColor;
        }

        private void OnDestroy()
        {
            if (panelPixel != null) Destroy(panelPixel);
        }
    }
}
