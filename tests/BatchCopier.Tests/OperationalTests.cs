using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using MsCrmTools.SolutionComponentsMover.AppCode;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.ServiceModel;
using System.Text;

internal static class OperationalTests
{
    public static void Run(Action<bool, string> check)
    {
        var source = Solution("Source"); var target = Solution("Release");
        var c = new PlannedComponent { Id = Guid.NewGuid(), Type = 61, Behavior = 0, DisplayName = "sample.js", TypeName = "Web Resource", Sources = new List<string> { "Source" } };
        var plan = new BatchPlan { OrganizationId = Guid.NewGuid(), UserId = Guid.NewGuid(), EnvironmentUrl = "https://crm.example/Org", Target = target,
            Settings = new CopySettings { SourceSolutions = new List<Entity> { source }, TargetSolutions = new List<Entity> { target }, AllComponents = true, OrganizationMajorVersion = 9 },
            Components = new List<PlannedComponent> { c }, RetrievedCount = 1 };
        var snapshots = new SnapshotService(c.Id);
        ContentSnapshots.Capture(plan, snapshots);
        check(c.Fingerprint != null && c.Tracking.StartsWith("Content"), "supported content receives a fingerprint");
        var fingerprint = c.Fingerprint;
        ContentSnapshots.Validate(plan, snapshots);
        check(c.Fingerprint == fingerprint, "revalidation does not mutate reviewed snapshot");
        snapshots.Body = "changed-script";
        ExpectFailure(() => ContentSnapshots.Validate(plan, snapshots), check, "content changes reject stale preview");
        snapshots.Fail = true;
        ExpectFailure(() => ContentSnapshots.Validate(plan, snapshots), check, "read failure cannot silently downgrade a tracked snapshot");
        snapshots.Fail = false; snapshots.Body = "private-script-marker";
        var unsupported = new PlannedComponent { Id = Guid.NewGuid(), Type = 1, Behavior = 2 };
        plan.Components.Add(unsupported); ContentSnapshots.Capture(plan, snapshots);
        check(unsupported.Fingerprint == null && unsupported.Tracking.StartsWith("Not supported"), "unsupported tracking is explicit");
        var previous = RunReport.Create(plan, "Preview baseline");
        previous.Add(new ComponentCopyResult { Id = c.Id, Type = c.Type, DisplayName = "sample.js", TypeName = "Web Resource", Source = "Source", Target = "Release", Status = "Added", Attempts = 2, RetryNotes = "Recovered" });
        check(!Encoding.UTF8.GetString(RunHistory.Serialize(previous)).Contains("private-script-marker"), "run report never stores raw component content");
        check(previous.Summary().Contains("Build") && previous.Summary().Contains("Added: 1") && previous.NotProcessed == 1, "human report records build and honest execution totals");
        var directory = Path.Combine(Path.GetTempPath(), "batch-run-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var history = new RunHistory(directory);
            check(history.Latest(plan.OrganizationId, target.Id) == null, "empty history is read-only");
            var path = history.Save(previous);
            var loaded = RunHistory.Load(path);
            check(loaded.RunId == previous.RunId && loaded.Components.Count == 2 && loaded.Results[0].Attempts == 2, "run history JSON round-trip");
            previous.Status = "Finished"; history.Save(previous);
            check(Directory.GetFiles(directory, "*.json").Length == 1 && RunHistory.Load(path).Status == "Finished", "run progress atomically updates one report");
            check(history.Latest(Guid.NewGuid(), target.Id) == null && history.Latest(plan.OrganizationId, Guid.NewGuid()) == null, "history is scoped by organization and target ID");
            var zipPath = Path.Combine(directory, "export.zip"); RunHistory.Export(zipPath, previous);
            using (var zip = ZipFileReader(zipPath))
            {
                check(new[] { "summary.txt", "results.csv", "run.json" }.All(n => zip.GetEntry(n) != null), "export contains readable summary results and structured plan");
                using (var reader = new StreamReader(zip.GetEntry("results.csv").Open())) check(reader.ReadToEnd().Contains("sample.js"), "report CSV includes component names");
            }
            check(RunHistory.Load(zipPath).RunId == previous.RunId, "exported report ZIP can be used as comparison baseline");
            var current = RunReport.Create(plan, "Current preview");
            check(PlanComparison.Compare(previous, current).Count == 0, "identical component plans compare equal regardless of execution status");
            current.Components[0].Behavior = 2;
            check(PlanComparison.Compare(previous, current).Any(d => d.Change == "Inclusion changed"), "comparison detects inclusion changes");
            current.Components[0].Fingerprint = "different";
            check(PlanComparison.Compare(previous, current).Any(d => d.Change.Contains("tracked fields")), "comparison detects available fingerprint differences");
            current.Components.RemoveAt(1);
            current.Components.Add(new ReportComponent { Id = Guid.NewGuid(), Type = 61, Name = "new.js" });
            var changes = PlanComparison.Compare(previous, current);
            check(changes.Count(d => d.Change == "Added to plan") == 1 && changes.Count(d => d.Change == "Removed from plan") == 1, "comparison shows added and removed memberships without mutating CRM");
            current.OrganizationId = Guid.NewGuid(); ExpectFailure(() => PlanComparison.Compare(previous, current), check, "cross-organization comparison is rejected");
            current.OrganizationId = previous.OrganizationId; current.Target.Id = Guid.NewGuid();
            ExpectFailure(() => PlanComparison.Compare(previous, current), check, "cross-target comparison is rejected");
            File.WriteAllText(Path.Combine(directory, "bad.json"), "{}");
            ExpectFailure(() => RunHistory.Load(Path.Combine(directory, "bad.json")), check, "malformed report is rejected");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        var writes = 0; var reads = 0; var waits = 0;
        var retry = CopyRetry.Execute(() => { writes++; if (writes == 1) throw new TimeoutException("network timeout"); }, () => { reads++; return false; }, () => false, null, delay => waits++);
        check(retry.Status == "Added" && writes == 2 && reads == 1 && waits == 1, "temporary fault is verified then retried with bounded delay");
        writes = 0;
        retry = CopyRetry.Execute(() => { writes++; throw new TimeoutException("response lost after commit"); }, () => true, () => false, null, delay => { });
        check(retry.Status == "Added" && writes == 1 && retry.Notes.Contains("confirmed"), "timeout after commit causes no duplicate write");
        writes = 0;
        retry = CopyRetry.Execute(() => { writes++; throw new TimeoutException(); }, () => { throw new TimeoutException("verification unavailable"); }, () => false, null, delay => { });
        check(retry.Status == "Unconfirmed" && writes == 1, "ambiguous outcome is not blindly retried or falsely called success");
        writes = 0;
        retry = CopyRetry.Execute(() => { writes++; throw new InvalidOperationException("invalid component"); }, () => true, () => false, null, delay => { });
        check(retry.Status == "Failed" && writes == 1, "permanent errors are not retried");
        var denied = new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220960, Message = "Access denied" });
        check(!CopyRetry.IsTransient(denied), "business permission faults are never classified from CommunicationException base type");
        writes = 0;
        retry = CopyRetry.Execute(() => { writes++; throw new TimeoutException(); }, () => false, () => false, null, delay => { });
        check(retry.Status == "Unconfirmed" && writes == 3, "network attempts are capped and uncertain outcomes are labeled honestly");
        writes = 0;
        retry = CopyRetry.Execute(() => writes++, () => false, () => true, null, delay => { });
        check(writes == 0 && retry.Status == "Not processed", "cancelled remaining work issues no new requests");
        var cancel = false; writes = 0;
        retry = CopyRetry.Execute(() => { writes++; throw new TimeoutException(); }, () => false, () => cancel, null, delay => cancel = true);
        check(writes == 1 && retry.Status == "Unconfirmed", "cancellation during retry delay stops further writes");
        var busy = new OrganizationServiceFault { ErrorCode = -2147015902, Message = "Busy" }; busy.ErrorDetails["Retry-After"] = TimeSpan.FromSeconds(61);
        writes = 0; waits = 0;
        retry = CopyRetry.Execute(() => { writes++; throw new FaultException<OrganizationServiceFault>(busy); }, () => false, () => false, null, delay => waits++);
        check(writes == 1 && waits == 0 && retry.Status == "Failed", "long server retry-after is honored by stopping instead of retrying early");
        ExpectFailure(() => CopyRetry.Execute(() => { throw new StaleContentException("changed"); }, () => false, () => false), check, "stale content cannot become a retried write");
        writes = 0;
        try
        {
            CopyRetry.Execute(() => { writes++; if (writes == 1) throw new TimeoutException(); throw new StaleContentException("changed before retry"); }, () => false, () => false, null, delay => { });
            check(false, "expected stale retry guard");
        }
        catch (StaleContentException stale) { check(stale.PreviousWriteAttempts == 1 && writes == 2, "stale retry guard preserves prior uncertain attempt count"); }
        check(BuildInfo.Display.Contains("Build") && BuildInfo.Version.Length > 0, "build information is available for UI and reports");
    }

    private static ZipArchive ZipFileReader(string path) => new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read);
    private static Entity Solution(string name) => new Entity("solution", Guid.NewGuid()) { ["friendlyname"] = name, ["uniquename"] = name };
    private static void ExpectFailure(Action action, Action<bool, string> check, string message)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException || error is InvalidDataException || error is System.Runtime.Serialization.SerializationException)
        { check(true, message); return; }
        check(false, message);
    }
    private sealed class SnapshotService : IOrganizationService
    {
        private readonly Guid id;
        public string Body = "private-script-marker";
        public bool Fail;
        public SnapshotService(Guid id) { this.id = id; }
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (Fail) throw new InvalidOperationException("Read failed");
            return new EntityCollection(new List<Entity> { new Entity("webresource", id) { ["content"] = Body, ["name"] = "sample.js" } });
        }
        public Entity Retrieve(string name, Guid id, ColumnSet columns) => throw new NotSupportedException();
        public OrganizationResponse Execute(OrganizationRequest request) => throw new Exception("Snapshot must never write");
        public Guid Create(Entity e) => throw new NotSupportedException();
        public void Update(Entity e) => throw new NotSupportedException();
        public void Delete(string n, Guid id) => throw new NotSupportedException();
        public void Associate(string n, Guid id, Relationship r, EntityReferenceCollection e) => throw new NotSupportedException();
        public void Disassociate(string n, Guid id, Relationship r, EntityReferenceCollection e) => throw new NotSupportedException();
    }
}
