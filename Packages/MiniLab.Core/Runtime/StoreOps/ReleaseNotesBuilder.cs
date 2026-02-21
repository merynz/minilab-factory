using System;
using System.Text;

namespace MiniLab.Core.StoreOps
{
    public static class ReleaseNotesBuilder
    {
        public static string Build(string version, string highlights)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"Version {version}");
            builder.AppendLine($"Date: {DateTime.UtcNow:yyyy-MM-dd}");
            builder.AppendLine("- New mini game content and balancing updates.");
            builder.AppendLine("- Stability and loading time improvements.");
            if (!string.IsNullOrWhiteSpace(highlights))
            {
                builder.AppendLine($"- Notes: {highlights}");
            }

            return builder.ToString();
        }
    }
}
