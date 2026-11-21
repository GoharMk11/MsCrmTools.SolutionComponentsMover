using System.Reflection;
using System.Text.RegularExpressions;

namespace MsCrmTools.SolutionComponentsMover.AppCode
{
    internal static class BuildInfo
    {
        public static string Version => typeof(BuildInfo).Assembly.GetName().Version.ToString();
        public static string Revision => typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "local";
        public static string Display => "Version " + Version + " | Build " + (Regex.IsMatch(Revision, "^[0-9a-fA-F]{7,40}$") ? Revision.Substring(0, 7) : Revision);
    }
}
