using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace MsCrmTools.SolutionComponentsMover.AppCode
{
    internal sealed class SolutionInput
    {
        public int LineNumber { get; set; }
        public string Text { get; set; }
        public Guid? Id { get; set; }
        public string UniqueName { get; set; }
        public Uri Url { get; set; }
        public string OrganizationPath { get; set; }
        public string Error { get; set; }
    }

    internal static class SolutionInputParser
    {
        private const string SolutionPath = "/tools/solution/edit.aspx";

        public static List<SolutionInput> Parse(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Select((line, index) => ParseLine(line, index + 1))
                .Where(input => input.Text.Length > 0).ToList();
        }

        public static SolutionInput ParseLine(string text, int lineNumber = 1)
        {
            var input = new SolutionInput { Text = (text ?? "").Trim(), LineNumber = lineNumber };
            // Single-column CSV is supported; escaped quotes are decoded before validation.
            if (input.Text.StartsWith("\"") && input.Text.EndsWith("\"") && input.Text.Length >= 2)
                input.Text = input.Text.Substring(1, input.Text.Length - 2).Replace("\"\"", "\"");
            Guid id;
            if (Guid.TryParse(input.Text, out id))
            {
                if (id == Guid.Empty) input.Error = "Solution ID must not be empty.";
                else input.Id = id;
                return input;
            }

            Uri uri;
            if (Uri.TryCreate(input.Text, UriKind.Absolute, out uri))
            {
                if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
                    !string.IsNullOrEmpty(uri.Fragment) ||
                    !uri.AbsolutePath.EndsWith(SolutionPath, StringComparison.OrdinalIgnoreCase))
                {
                    input.Error = "Expected an absolute HTTPS URL ending with " + SolutionPath + ".";
                    return input;
                }
                var query = HttpUtility.ParseQueryString(uri.Query);
                var ids = query.GetValues("id");
                if (ids == null || ids.Length != 1 || !Guid.TryParse(ids[0], out id) || id == Guid.Empty)
                {
                    input.Error = "URL must contain exactly one valid, nonempty id GUID.";
                    return input;
                }
                input.Id = id;
                input.Url = uri;
                input.OrganizationPath = NormalizePath(uri.AbsolutePath.Substring(0, uri.AbsolutePath.Length - SolutionPath.Length));
                return input;
            }

            if (Regex.IsMatch(input.Text, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                input.UniqueName = input.Text;
            else
                input.Error = "Expected a Solution URL, GUID or Unique Name (letters, digits and underscores).";
            return input;
        }

        public static bool MatchesEnvironment(SolutionInput input, string connectedUrl)
        {
            if (input.Url == null) return true;
            Uri active;
            return Uri.TryCreate(connectedUrl, UriKind.Absolute, out active) &&
                string.Equals(input.Url.IdnHost, active.IdnHost, StringComparison.OrdinalIgnoreCase) &&
                input.Url.Port == active.Port &&
                string.Equals(input.OrganizationPath, NormalizePath(active.AbsolutePath), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            return Uri.UnescapeDataString(path).TrimEnd('/');
        }
    }
}
