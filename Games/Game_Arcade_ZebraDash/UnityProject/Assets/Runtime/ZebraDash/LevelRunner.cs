using UnityEngine;

namespace ZebraDash
{
    public sealed class LevelRunner : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Transform playerTransform;
        [SerializeField] private Transform worldRoot;
        [SerializeField] private float worldScrollSpeed = 7f;
        [SerializeField] private float restScrollMultiplier = 0.85f;
        [SerializeField] private float accentShiftMagnitude = 0.45f;
        [SerializeField] private float accentRecoverySpeed = 2.5f;
        [SerializeField] private bool runScrolling = true;

        private bool isRestSection;
        private float accentOffsetX;
        private float previousAccentOffsetX;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;

            PlacePlayerAtLeftThird();
        }

        private void Update()
        {
            if (runScrolling && worldRoot != null)
            {
                float speedMul = isRestSection ? restScrollMultiplier : 1f;
                worldRoot.position += Vector3.left * (worldScrollSpeed * speedMul * Time.deltaTime);
                accentOffsetX = Mathf.MoveTowards(accentOffsetX, 0f, accentRecoverySpeed * Time.deltaTime);
                float accentDelta = accentOffsetX - previousAccentOffsetX;
                if (Mathf.Abs(accentDelta) > 0.0001f)
                {
                    worldRoot.position += Vector3.right * accentDelta;
                }

                previousAccentOffsetX = accentOffsetX;
            }
        }

        private void PlacePlayerAtLeftThird()
        {
            if (targetCamera == null || playerTransform == null)
            {
                return;
            }

            Vector3 viewport = targetCamera.WorldToViewportPoint(playerTransform.position);
            viewport.x = 0.33f;
            playerTransform.position = targetCamera.ViewportToWorldPoint(viewport);
            playerTransform.position = new Vector3(playerTransform.position.x, 0f, 0f);
        }

        public float ScrollSpeed => worldScrollSpeed;

        public Transform WorldRoot => worldRoot;

        public void SetScrolling(bool enabled)
        {
            runScrolling = enabled;
        }

        public void SetRestSection(bool isRest)
        {
            isRestSection = isRest;
        }

        public void TriggerAccentPulse(float strength)
        {
            float pulse = Mathf.Clamp(strength, 0.1f, 2f) * accentShiftMagnitude;
            accentOffsetX = Mathf.Clamp(accentOffsetX + pulse, -accentShiftMagnitude * 2f, accentShiftMagnitude * 2f);
        }
    }
}
