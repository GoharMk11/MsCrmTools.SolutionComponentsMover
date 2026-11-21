using System;
using System.Linq;
using MsCrmTools.SolutionComponentsMover.AppCode;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        ParserTests();
        EngineTests.Run(Check);
        PreviewTests.Run(Check);
#if NET48
        PickerTests.Run(Check);
        WorkspaceTests.Run(Check);
        if (args.Length > 0) HostUiTests.Run(Check, args[0]);
#endif
        Check(CsvFormatting.Field("a,b") == "\"a,b\"", "CSV comma quoting");
        Check(CsvFormatting.Field("CRM \"fault\"\r\nnext") == "\"CRM \"\"fault\"\"\r\nnext\"", "CSV quotes and newlines");
        Check(CsvFormatting.Field(null) == "\"\"", "CSV empty error detail");
        Console.WriteLine("Passed " + assertions + " assertions.");
    }

    private static void ParserTests()
    {
        var id = Guid.NewGuid();
        var url = "https://crm.example/Org/tools/solution/edit.aspx?id=%7b" + id + "%7d";
        var parsed = SolutionInputParser.ParseLine(url);
        Check(parsed.Error == null && parsed.Id == id && parsed.OrganizationPath == "/Org", "encoded solution URL");
        Check(SolutionInputParser.MatchesEnvironment(parsed, "https://CRM.example/org/"), "same organization");
        Check(!SolutionInputParser.MatchesEnvironment(parsed, "https://other.example/Org"), "wrong host");
        Check(!SolutionInputParser.MatchesEnvironment(parsed, "https://crm.example/Other"), "wrong organization");
        Check(!SolutionInputParser.MatchesEnvironment(parsed, "https://crm.example:444/Org"), "wrong port");
        Check(!SolutionInputParser.MatchesEnvironment(parsed, "invalid"), "missing active environment");
        Check(SolutionInputParser.ParseLine(id.ToString()).Id == id, "GUID");
        Check(SolutionInputParser.ParseLine("{" + id + "}").Id == id, "braced GUID");
        Check(SolutionInputParser.ParseLine("My_Source123").UniqueName == "My_Source123", "unique name");
        Check(SolutionInputParser.ParseLine("\"My_Source123\"").UniqueName == "My_Source123", "single-column CSV");
        Check(SolutionInputParser.Parse("\"\"").Single().Error != null, "empty CSV cell is an error");
        var invalid = new[] {
            "http://crm.example/Org/tools/solution/edit.aspx?id=" + id,
            "https://crm.example/Org/wrong.aspx?id=" + id,
            "https://crm.example/Org/tools/solution/edit.aspx",
            url + "&id=" + id, url + "#fragment",
            "https://user@crm.example/Org/tools/solution/edit.aspx?id=" + id,
            "https://crm.example/Org/tools/solution/edit.aspx?id=wrong", "bad name", "one,two", "{broken}",
            Guid.Empty.ToString(), "https://crm.example/Org/tools/solution/edit.aspx?id=" + Guid.Empty
        };
        foreach (var value in invalid) Check(SolutionInputParser.ParseLine(value).Error != null, "reject " + value);
        var batch = SolutionInputParser.Parse(string.Join("\r\n", Enumerable.Repeat(url, 12)) + "\n\ninvalid input");
        Check(batch.Count == 13 && batch.Last().LineNumber == 14 && batch.Last().Error != null, "12 URLs and line errors");
        Check(SolutionInputParser.Parse("\r\n  \n").Count == 0, "blank lines");
    }
}
