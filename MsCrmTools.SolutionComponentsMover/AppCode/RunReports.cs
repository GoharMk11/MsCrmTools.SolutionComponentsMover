using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MsCrmTools.SolutionComponentsMover.AppCode
{
    [DataContract]
    internal sealed class ReportSolution
    {
        [DataMember] public Guid Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string UniqueName { get; set; }
    }

    [DataContract]
    internal sealed class ReportComponent
    {
        [DataMember] public Guid Id { get; set; }
        [DataMember] public int Type { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string TypeName { get; set; }
        [DataMember] public int? Behavior { get; set; }
        [DataMember] public bool AlreadyExists { get; set; }
        [DataMember] public List<string> Sources { get; set; }
        [DataMember] public string Fingerprint { get; set; }
        [DataMember] public string Tracking { get; set; }
        public string Key => Type + ":" + Id;
    }

    [DataContract]
    internal sealed class ReportResult
    {
        [DataMember] public Guid Id { get; set; }
        [DataMember] public int Type { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string TypeName { get; set; }
        [DataMember] public string Source { get; set; }
        [DataMember] public string Target { get; set; }
        [DataMember] public string Status { get; set; }
        [DataMember] public string Error { get; set; }
        [DataMember] public int Attempts { get; set; }
        [DataMember] public string RetryNotes { get; set; }
    }

    [DataContract]
    internal sealed class RunReport
    {
        [DataMember] public int SchemaVersion { get; set; } = 1;
        [DataMember] public Guid RunId { get; set; } = Guid.NewGuid();
        [DataMember] public string Build { get; set; }
        [DataMember] public string Version { get; set; }
        [DataMember] public Guid OrganizationId { get; set; }
        [DataMember] public Guid UserId { get; set; }
        [DataMember] public string EnvironmentUrl { get; set; }
        [DataMember] public string StartedUtc { get; set; }
        [DataMember] public string FinishedUtc { get; set; }
        [DataMember] public string Status { get; set; }
        [DataMember] public ReportSolution Target { get; set; }
        [DataMember] public List<ReportSolution> Sources { get; set; }
        [DataMember] public bool AllComponentTypes { get; set; }
        [DataMember] public List<int> ComponentTypes { get; set; }
        [DataMember] public bool BestPracticeCheck { get; set; }
        [DataMember] public bool AddRequiredComponents { get; set; } = false;
        [DataMember] public int MaxWriteAttempts { get; set; } = 3;
        [DataMember] public List<string> Warnings { get; set; }
        [DataMember] public List<ReportComponent> Components { get; set; }
        [DataMember] public List<ReportResult> Results { get; set; } = new List<ReportResult>();
        [DataMember] public string Error { get; set; }
        [DataMember] public string PersistenceWarning { get; set; }
        public int NotProcessed => Components.Count - Results.Count;

        public static RunReport Create(BatchPlan plan, string status)
        {
            Func<Microsoft.Xrm.Sdk.Entity, ReportSolution> solution = e => new ReportSolution
            {
                Id = e.Id, Name = e.GetAttributeValue<string>("friendlyname"), UniqueName = e.GetAttributeValue<string>("uniquename")
            };
            return new RunReport
            {
                Build = BuildInfo.Revision, Version = BuildInfo.Version, OrganizationId = plan.OrganizationId, UserId = plan.UserId,
                EnvironmentUrl = plan.EnvironmentUrl, StartedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), Status = status,
                Target = solution(plan.Target), Sources = plan.Settings.SourceSolutions.Select(solution).ToList(),
                AllComponentTypes = plan.Settings.AllComponents, ComponentTypes = plan.Settings.ComponentsTypes?.ToList() ?? new List<int>(),
                BestPracticeCheck = plan.Settings.CheckBestPractice, Warnings = plan.Warnings.ToList(),
                Components = plan.Components.Select(c => new ReportComponent { Id = c.Id, Type = c.Type, Name = c.DisplayName,
                    TypeName = c.TypeName, Behavior = c.Behavior, AlreadyExists = c.AlreadyExists, Sources = c.Sources?.ToList(),
                    Fingerprint = c.Fingerprint, Tracking = c.Tracking }).ToList()
            };
        }

        public void Add(ComponentCopyResult r)
        {
            Results.Add(new ReportResult { Id = r.Id, Type = r.Type, Name = r.DisplayName, TypeName = r.TypeName, Source = r.Source,
                Target = r.Target, Status = r.Status, Error = r.Error, Attempts = r.Attempts, RetryNotes = r.RetryNotes });
        }

        public string Summary()
        {
            return BuildInfoText() + Environment.NewLine +
                "Run ID: " + RunId + Environment.NewLine + "Status: " + Status + Environment.NewLine +
                "Started (UTC): " + StartedUtc + Environment.NewLine + "Finished (UTC): " + FinishedUtc + Environment.NewLine +
                "CRM: " + EnvironmentUrl + Environment.NewLine + "Organization ID: " + OrganizationId + Environment.NewLine + "User ID: " + UserId + Environment.NewLine +
                "Target: " + Target.Name + " (" + Target.UniqueName + ", " + Target.Id + ")" + Environment.NewLine +
                "Sources:" + Environment.NewLine + string.Join(Environment.NewLine, Sources.Select(s => "- " + s.Name + " (" + s.UniqueName + ", " + s.Id + ")")) + Environment.NewLine +
                "Component types: " + (AllComponentTypes ? "All" : string.Join(", ", ComponentTypes)) + Environment.NewLine +
                "Best-practice check: " + BestPracticeCheck + "; AddRequiredComponents: " + AddRequiredComponents + "; maximum write attempts: " + MaxWriteAttempts + Environment.NewLine +
                "Unique planned components: " + Components.Count + "; tracked: " + Components.Count(c => c.Fingerprint != null) + Environment.NewLine +
                "Added: " + Results.Count(r => r.Status == "Added") + "; Already Exists: " + Results.Count(r => r.Status == "Already Exists") +
                "; Failed: " + Results.Count(r => r.Status == "Failed") + "; Unconfirmed: " + Results.Count(r => r.Status == "Unconfirmed") + "; Not processed: " + NotProcessed + Environment.NewLine +
                "Error: " + Error + Environment.NewLine + "Local history warning: " + PersistenceWarning + Environment.NewLine + string.Join(Environment.NewLine, Warnings) + Environment.NewLine +
                "This is a record of the selected plan, not a frozen solution export. No source components were removed.";
        }
        private string BuildInfoText() => "Version " + Version + " | Build " + Build;
    }

    internal sealed class RunHistory
    {
        public string DirectoryPath { get; }
        public RunHistory(string path = null)
        {
            DirectoryPath = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MscrmTools", "XrmToolBox", "SolutionComponentsMover", "History");
        }

        public string Save(RunReport report)
        {
            Validate(report);
            Directory.CreateDirectory(DirectoryPath);
            var timestamp = DateTime.Parse(report.StartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime().ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture);
            var path = Path.Combine(DirectoryPath, Prefix(report.OrganizationId, report.Target.Id) + timestamp + "_" + report.RunId.ToString("N") + ".json");
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, Serialize(report));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return path;
        }

        public RunReport Latest(Guid organization, Guid target)
        {
            if (!Directory.Exists(DirectoryPath)) return null;
            var path = Directory.GetFiles(DirectoryPath, Prefix(organization, target) + "*.json").OrderByDescending(p => Path.GetFileName(p), StringComparer.Ordinal).FirstOrDefault();
            return path == null ? null : Load(path);
        }

        private static string Prefix(Guid organization, Guid target) => "run_" + organization.ToString("N") + "_" + target.ToString("N") + "_";
        public static byte[] Serialize(RunReport report)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                    new DataContractJsonSerializer(typeof(RunReport), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 2000000 }).WriteObject(writer, report);
                return stream.ToArray();
            }
        }
        public static RunReport Load(string path)
        {
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Run report exceeds 32 MiB.");
            using (var file = File.OpenRead(path))
            {
                if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                    {
                        var entry = zip.GetEntry("run.json");
                        if (entry == null || entry.Length > 32 * 1024 * 1024) throw new InvalidDataException("The archive must contain a run.json report smaller than 32 MiB.");
                        using (var json = entry.Open()) return Read(json);
                    }
                }
                return Read(file);
            }
        }
        private static RunReport Read(Stream stream)
        {
            var report = (RunReport)new DataContractJsonSerializer(typeof(RunReport), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 2000000 }).ReadObject(stream);
            Validate(report); return report;
        }
        private static void Validate(RunReport report)
        {
            if (report == null || report.SchemaVersion != 1 || report.RunId == Guid.Empty || report.OrganizationId == Guid.Empty || report.Target == null || report.Target.Id == Guid.Empty ||
                report.Components == null || report.Results == null || report.Sources == null || report.Warnings == null || report.ComponentTypes == null ||
                !DateTime.TryParse(report.StartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp) ||
                report.Sources.Any(s => s == null || s.Id == Guid.Empty) || report.Results.Any(r => r == null || r.Id == Guid.Empty) || report.Components.Any(c => c == null || c.Id == Guid.Empty) || report.Components.Select(c => c.Key).Distinct().Count() != report.Components.Count)
                throw new InvalidDataException("Invalid or unsupported run report.");
        }
        public static void Export(string path, RunReport report)
        {
            Validate(report);
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                Write(zip, "run.json", Serialize(report)); Write(zip, "summary.txt", Encoding.UTF8.GetBytes(report.Summary()));
                var csv = new StringBuilder("Source Solution(s),Component name,Component Type,Component ID,Target Solution,Status,Error Details,Attempts,Retry notes\r\n");
                foreach (var r in report.Results)
                    csv.AppendLine(string.Join(",", new[] { r.Source, r.Name, r.TypeName, r.Id.ToString(), r.Target, r.Status, r.Error, r.Attempts.ToString(CultureInfo.InvariantCulture), r.RetryNotes }.Select(CsvFormatting.Field)));
                Write(zip, "results.csv", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray());
            }
        }
        private static void Write(ZipArchive zip, string name, byte[] content)
        {
            using (var stream = zip.CreateEntry(name).Open()) stream.Write(content, 0, content.Length);
        }
    }

    internal sealed class PlanDifference
    {
        public string Change { get; set; }
        public ReportComponent Before { get; set; }
        public ReportComponent After { get; set; }
    }

    internal static class PlanComparison
    {
        public static List<PlanDifference> Compare(RunReport before, RunReport after)
        {
            if (before.OrganizationId != after.OrganizationId || before.Target.Id != after.Target.Id)
                throw new InvalidOperationException("Comparison requires the same CRM organization and target solution ID.");
            var old = before.Components.ToDictionary(c => c.Key); var current = after.Components.ToDictionary(c => c.Key);
            var changes = new List<PlanDifference>();
            foreach (var c in current.Values)
            {
                ReportComponent previous;
                if (!old.TryGetValue(c.Key, out previous)) changes.Add(new PlanDifference { Change = "Added to plan", After = c });
                else
                {
                    if (previous.Behavior != c.Behavior) changes.Add(new PlanDifference { Change = "Inclusion changed", Before = previous, After = c });
                    if (previous.Tracking != c.Tracking) changes.Add(new PlanDifference { Change = "Change-detection coverage changed", Before = previous, After = c });
                    if (previous.Tracking == c.Tracking && previous.Fingerprint != null && c.Fingerprint != null && previous.Fingerprint != c.Fingerprint)
                        changes.Add(new PlanDifference { Change = "Content / tracked fields changed", Before = previous, After = c });
                }
            }
            changes.AddRange(old.Values.Where(c => !current.ContainsKey(c.Key)).Select(c => new PlanDifference { Change = "Removed from plan", Before = c }));
            return changes;
        }
    }
}
