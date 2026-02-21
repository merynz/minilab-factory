using System;
using System.Collections.Generic;

namespace MiniLab.Core.StoreOps
{
    [Serializable]
    public sealed class StoreMetadata
    {
        public string GameCode = "game_code";
        public string BundleId = "com.company.game";
        public string ApplicationId = "com.company.game";
        public string PrivacyPolicyUrl = "";
        public string SupportEmail = "";
        public Dictionary<string, LocalizedStoreText> Locales = new Dictionary<string, LocalizedStoreText>();
        public List<string> CoreSdkInventory = new List<string>();
        public List<string> GameSdkInventoryDelta = new List<string>();
    }

    [Serializable]
    public sealed class LocalizedStoreText
    {
        public string ShortDescription = "";
        public string LongDescription = "";
        public string AdText = "";
    }
}
