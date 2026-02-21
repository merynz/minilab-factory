using System;
using UnityEngine;

namespace MiniLab.Core.Save
{
    public static class SaveService
    {
        public static void SetJson<T>(string key, T model)
        {
            string json = JsonUtility.ToJson(model);
            PlayerPrefs.SetString(key, json);
            PlayerPrefs.Save();
        }

        public static bool TryGetJson<T>(string key, out T model) where T : new()
        {
            if (!PlayerPrefs.HasKey(key))
            {
                model = new T();
                return false;
            }

            string json = PlayerPrefs.GetString(key);
            model = JsonUtility.FromJson<T>(json);
            return model != null;
        }

        public static void Delete(string key)
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }
}
