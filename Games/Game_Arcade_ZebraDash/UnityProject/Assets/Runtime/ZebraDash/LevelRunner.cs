using UnityEngine;

namespace ZebraDash
{
    public sealed class LevelRunner : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Transform playerTransform;
        [SerializeField] private Transform worldRoot;
        [SerializeField] private float worldScrollSpeed = 7f;
        [SerializeField] private bool runScrolling = true;

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
                worldRoot.position += Vector3.left * (worldScrollSpeed * Time.deltaTime);
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
    }
}
