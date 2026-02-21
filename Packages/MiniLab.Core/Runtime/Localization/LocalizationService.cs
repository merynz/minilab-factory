using System.Collections.Generic;
using MiniLab.Core.Config;

namespace MiniLab.Core.Localization
{
    public sealed class LocalizationService
    {
        private readonly Dictionary<string, Dictionary<string, string>> table;
        private string currentLocale;

        public LocalizationService(CoreSettings settings)
        {
            currentLocale = settings.DefaultLocale;
            table = new Dictionary<string, Dictionary<string, string>>();
        }

        public void SetLocale(string locale)
        {
            currentLocale = locale;
        }

        public string Get(string key)
        {
            if (table.TryGetValue(currentLocale, out Dictionary<string, string> localeTable) &&
                localeTable.TryGetValue(key, out string value))
            {
                return value;
            }

            return key;
        }
    }
}
