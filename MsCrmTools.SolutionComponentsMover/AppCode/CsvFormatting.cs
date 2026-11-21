namespace MsCrmTools.SolutionComponentsMover.AppCode
{
    internal static class CsvFormatting
    {
        public static string Field(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        }
    }
}
