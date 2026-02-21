using UnityEngine;

namespace ZebraDash
{
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private float laneStep = 1.5f;
        [SerializeField] private int minLane = -1;
        [SerializeField] private int maxLane = 1;
        [SerializeField] private float lerpSpeed = 12f;

        private int lane;
        private bool pointerHeld;

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                pointerHeld = true;
                Jump();
            }

            if (Input.GetMouseButton(0) && pointerHeld)
            {
                Slide();
            }

            if (Input.GetMouseButtonUp(0))
            {
                pointerHeld = false;
            }

            Vector3 target = transform.position;
            target.y = lane * laneStep;
            transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * lerpSpeed);
        }

        private void Jump()
        {
            lane = Mathf.Clamp(lane + 1, minLane, maxLane);
        }

        private void Slide()
        {
            lane = Mathf.Clamp(lane - 1, minLane, maxLane);
        }
    }
}
