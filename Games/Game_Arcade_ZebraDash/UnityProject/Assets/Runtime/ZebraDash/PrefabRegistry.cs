using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZebraDash
{
    [CreateAssetMenu(fileName = "PrefabRegistry", menuName = "ZebraDash/Prefab Registry")]
    public sealed class PrefabRegistry : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string prefabId = "tap_basic";
            public GameObject prefab;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private Dictionary<string, GameObject> cache;

        private void BuildCache()
        {
            if (cache != null)
            {
                return;
            }

            cache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.prefabId) || entry.prefab == null)
                {
                    continue;
                }

                cache[entry.prefabId] = entry.prefab;
            }
        }

        public GameObject GetPrefab(string prefabId)
        {
            BuildCache();
            if (!string.IsNullOrWhiteSpace(prefabId) && cache.TryGetValue(prefabId, out GameObject prefab))
            {
                return prefab;
            }

            return null;
        }
    }
}
