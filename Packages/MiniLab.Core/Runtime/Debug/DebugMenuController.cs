using MiniLab.Core.Policy;
using UnityEngine;

namespace MiniLab.Core.DebugTools
{
    public sealed class DebugMenuController : MonoBehaviour
    {
        [SerializeField] private bool startOpened;
        private bool opened;

        private void Awake()
        {
            opened = startOpened;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.BackQuote))
            {
                opened = !opened;
            }
        }

        private void OnGUI()
        {
            if (!opened)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(12, 12, 320, 220), GUI.skin.box);
            GUILayout.Label("MiniLab Debug Menu");
            GUILayout.Label($"ConsentResolved: {AdsGate.ConsentResolved}");
            GUILayout.Label($"AdsAllowed: {AdsGate.AdsAllowed}");
            GUILayout.EndArea();
        }
    }
}
