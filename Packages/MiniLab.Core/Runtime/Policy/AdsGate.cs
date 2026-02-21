namespace MiniLab.Core.Policy
{
    public static class AdsGate
    {
        public static bool ConsentResolved { get; private set; }
        public static bool AdsAllowed { get; private set; }

        public static void SetConsentResult(bool canRequestAds)
        {
            ConsentResolved = true;
            AdsAllowed = canRequestAds;
        }
    }
}
