using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using MsCrmTools.SolutionComponentsMover.AppCode;
using System;
using System.Collections.Generic;
using System.Linq;

internal static partial class EngineTests
{
    public static void Run(Action<bool, string> check)
    {
        check(ComponentCoverage.Merge(new int?[] { 2, 1, 0 }) == 0, "all assets win source merge");
        check(ComponentCoverage.Merge(new int?[] { 2, 1 }) == 1, "exclude-subcomponents beats shell");
        check(!ComponentCoverage.Satisfies(2, 0) && !ComponentCoverage.Satisfies(1, 0), "narrow target must expand");
        check(ComponentCoverage.Satisfies(0, 1) && ComponentCoverage.Satisfies(1, 2), "broader target satisfies source");
        check(!ComponentCoverage.Satisfies(null, 0), "unknown coverage cannot satisfy explicit coverage");
        ExpectFailure(() => ComponentCoverage.Merge(new int?[] { null, 0 }), check, "ambiguous merge blocked");

        ChangeGuardIntegration(check);
        var crm = new FakeCrm();
        var source1 = crm.Solution("SourceOne");
        var source2 = crm.Solution("SourceTwo");
        var target = crm.Solution("Release");
        var shared = Guid.NewGuid();
        var missing = Guid.NewGuid();
        crm.Component(source1, shared, 1, 2);
        crm.Component(source2, shared, 1, 0);
        crm.Component(target, shared, 1, 2);
        crm.Component(source1, missing, 61, 0);
        var present = Guid.NewGuid();
        crm.Component(source1, present, 60, 1);
        crm.Component(target, present, 60, 0);
        var definition = Guid.NewGuid();
        var value = Guid.NewGuid();
        crm.Component(source1, definition, 380, 0);
        crm.Component(source1, value, 381, 0);
        var settings = new CopySettings
        {
            SourceSolutions = new List<Entity> { source1, source2 }, TargetSolutions = new List<Entity> { target },
            AllComponents = true, OrganizationMajorVersion = 9
        };
        var manager = new SolutionManager(crm);
        crm.PageSize = 2;
        var preview = manager.Preview(settings);
        check(preview.RetrievedCount == 6 && preview.Components.Count == 5, "source deduplication with paging");
        check(preview.ExistingCount == 1 && preview.PlannedCount == 4, "preview respects coverage differences");
        check(preview.Components.Single(c => c.Id == shared).Sources.Count == 2, "all source origins retained");
        check(preview.Components.Single(c => c.Id == shared).Behavior == 0, "strongest inclusion retained");
        check(crm.Writes.Count == 0, "preview is read-only");
        check(crm.Pages.All(p => p >= 1) && crm.Pages.Contains(3), "explicit first page and subsequent pages");

        foreach (var named in preview.Components)
        {
            named.DisplayName = "friendly-name-" + named.Id;
            named.TypeName = "Readable CRM type";
        }
        crm.FailId = missing;
        var results = new List<ComponentCopyResult>();
        manager.CopyComponents(preview, () => false, (n, r) => results.Add(r));
        check(results.Count == 5 && results.Count(r => r.Status == "Failed") == 1, "component fault isolation");
        check(results.Single(r => r.Id == missing).Error == "simulated CRM failure", "failure detail retained");
        check(results.All(r => r.DisplayName == "friendly-name-" + r.Id && r.TypeName == "Readable CRM type"), "results retain preview names for added skipped and failed components");
        check(results.Last().Status == "Added", "continue after failed component");
        check(crm.Writes.Count == 4 && crm.Writes.All(r => !r.AddRequiredComponents), "one request per missing key, dependencies disabled");
        var variableRequest = crm.Writes.Single(r => r.ComponentType == 380);
        check(variableRequest.DoNotIncludeSubcomponents && variableRequest.IncludedComponentSettingsValues.Length == 0, "variable definition exclusion retained");
        check(!crm.Writes.Single(r => r.ComponentId == shared).DoNotIncludeSubcomponents, "entity inclusion retained");
        check(crm.Components.Count(c => c.GetAttributeValue<EntityReference>("solutionid").Id == source1.Id) == 5, "source memberships unchanged");

        crm.FailId = null;
        var retry = manager.Preview(settings);
        check(retry.PlannedCount == 1, "retry only plans failed request");
        manager.CopyComponents(retry, () => false, (n, r) => { });
        var repeat = manager.Preview(settings);
        var writeCount = crm.Writes.Count;
        manager.CopyComponents(repeat, () => false, (n, r) => { });
        check(crm.Writes.Count == writeCount && repeat.PlannedCount == 0, "safe re-execution skips satisfied components");

        var extra = Guid.NewGuid();
        crm.Component(source2, extra, 61, 0);
        ExpectFailure(() => manager.CopyComponents(repeat, () => false, (n, r) => { }), check, "stale preview blocked");
        check(crm.Writes.Count == writeCount, "stale preview produces no writes");
        manager.CopyComponents(manager.Preview(settings), () => true, (n, r) => { });
        check(crm.Writes.Count == writeCount, "cancellation before execution");
        var cancelAfterOne = false;
        manager.CopyComponents(manager.Preview(settings), () => cancelAfterOne, (n, r) => cancelAfterOne = true);
        check(crm.Writes.Count <= writeCount + 1, "cancellation stops remaining requests");

        var input = SolutionInputParser.Parse(source1.Id + "\nSourceOne\nhttps://wrong.example/Org/tools/solution/edit.aspx?id=" + source1.Id + "\nNotFound");
        var validated = manager.ResolveInputs(input, "https://crm.example/Org");
        check(validated[0].Error == null && validated[1].Duplicate, "GUID/name duplicate resolution");
        check(validated[2].Error != null && validated[3].Error != null, "environment mismatch and missing solution errors");
        check(manager.RetrieveSolutions().Count() == 3, "solution list pagination");
        target["ismanaged"] = true;
        ExpectFailure(() => manager.Preview(settings), check, "managed target blocked");
        target["ismanaged"] = false;
        settings.SourceSolutions.Add(target);
        ExpectFailure(() => manager.Preview(settings), check, "target as source blocked");
        settings.SourceSolutions.Remove(target);
        settings.TargetSolutions.Add(source2);
        ExpectFailure(() => manager.Preview(settings), check, "multiple targets blocked");
        settings.TargetSolutions.Remove(source2);
        settings.AllComponents = false;
        settings.ComponentsTypes = new List<int>();
        ExpectFailure(() => manager.Preview(settings), check, "empty component filter blocked");
        settings.ComponentsTypes.Add(61);
        check(manager.Preview(settings).Components.All(c => c.Type == 61), "component type filter retained");
        settings.AllComponents = true;
        var entityComponent = crm.Components.First(c => c.GetAttributeValue<Guid>("objectid") == shared);
        var savedBehavior = entityComponent["rootcomponentbehavior"];
        entityComponent.Attributes.Remove("rootcomponentbehavior");
        ExpectFailure(() => manager.Preview(settings), check, "missing entity coverage must not assume all assets");
        entityComponent["rootcomponentbehavior"] = savedBehavior;
        settings.CheckBestPractice = true;
        crm.ManagedEntities.Add(new EntityMetadata { MetadataId = shared, SchemaName = "ActualProblemEntity", DisplayName = new Label("Actual problem", 1033) });
        try { manager.Preview(settings); throw new Exception("expected managed validation"); }
        catch (InvalidOperationException error)
        {
            check(error.Message.Contains("ActualProblemEntity") || error.Message.Contains("Actual problem"), "best-practice message lists actual problem entity");
            check(!error.Message.Contains("UnrelatedEntity"), "no unrelated entities in validation message");
        }
    }

    private static void ChangeGuardIntegration(Action<bool, string> check)
    {
        var crm = new FakeCrm(); var source = crm.Solution("GuardSource"); var target = crm.Solution("GuardTarget");
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        crm.Component(source, first, 61, 0); crm.Component(source, second, 61, 0);
        crm.ContentRows[first] = new Entity("webresource", first) { ["content"] = "first original", ["name"] = "first.js" };
        crm.ContentRows[second] = new Entity("webresource", second) { ["content"] = "second original", ["name"] = "second.js" };
        var settings = new CopySettings { SourceSolutions = new List<Entity> { source }, TargetSolutions = new List<Entity> { target }, AllComponents = true, OrganizationMajorVersion = 9 };
        var manager = new SolutionManager(crm, delay => { });
        var preview = manager.Preview(settings); ContentSnapshots.Capture(preview, crm);
        crm.ContentRows[first]["content"] = "edited before copy";
        ExpectFailure(() => manager.CopyComponents(preview, () => false, (n, r) => { }), check, "engine blocks edited content before any writes");
        check(crm.Writes.Count == 0, "stale content does not modify CRM");
        crm.ContentRows[first]["content"] = "first original";
        preview = manager.Preview(settings); ContentSnapshots.Capture(preview, crm);
        crm.AfterWrite = () => crm.ContentRows[second]["content"] = "edited during copy";
        var recorded = new List<ComponentCopyResult>();
        ExpectFailure(() => manager.CopyComponents(preview, () => false, (n, r) => recorded.Add(r)), check, "engine stops a batch when a later component changes");
        check(crm.Writes.Count == 1 && recorded.Count == 1 && recorded[0].Status == "Added", "mid-batch stop preserves completed results and performs no stale second write");
        var retryCrm = new FakeCrm(); var retrySource = retryCrm.Solution("RetrySource"); var retryTarget = retryCrm.Solution("RetryTarget");
        retryCrm.Component(retrySource, first, 61, 0);
        retryCrm.AfterWrite = () => { throw new TimeoutException("reply lost after adding membership"); };
        var retryManager = new SolutionManager(retryCrm, delay => { });
        var retryPlan = retryManager.Preview(new CopySettings { SourceSolutions = new List<Entity> { retrySource }, TargetSolutions = new List<Entity> { retryTarget }, AllComponents = true, OrganizationMajorVersion = 9 });
        recorded.Clear(); retryManager.CopyComponents(retryPlan, () => false, (n, r) => recorded.Add(r));
        check(retryCrm.Writes.Count == 1 && recorded.Single().Status == "Added" && recorded[0].Attempts == 1 && recorded[0].RetryNotes.Contains("confirmed"), "engine verifies target coverage after timeout without duplicate writes");
    }

    private static void ExpectFailure(Action action, Action<bool, string> check, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { check(true, message); return; }
        check(false, message);
    }

    private sealed class FakeCrm : IOrganizationService
    {
        public readonly List<Entity> Solutions = new List<Entity>();
        public readonly List<Entity> Components = new List<Entity>();
        public readonly List<AddSolutionComponentRequest> Writes = new List<AddSolutionComponentRequest>();
        public readonly List<int> Pages = new List<int>();
        public readonly EntityMetadataCollection ManagedEntities = new EntityMetadataCollection();
        public int PageSize = 5000;
        public Guid? FailId;
        public Action AfterWrite;
        public readonly Dictionary<Guid, Entity> ContentRows = new Dictionary<Guid, Entity>();

        public Entity Solution(string name)
        {
            var solution = new Entity("solution", Guid.NewGuid());
            solution["uniquename"] = name;
            solution["friendlyname"] = name;
            solution["ismanaged"] = false;
            solution["isvisible"] = true;
            Solutions.Add(solution);
            return solution;
        }
        public void Component(Entity solution, Guid id, int type, int behavior)
        {
            var component = new Entity("solutioncomponent", Guid.NewGuid());
            component["objectid"] = id;
            component["componenttype"] = new OptionSetValue(type);
            component["rootcomponentbehavior"] = new OptionSetValue(behavior);
            component["solutionid"] = solution.ToEntityReference();
            component["solution.ismanaged"] = new AliasedValue("solution", "ismanaged", solution.GetAttributeValue<bool>("ismanaged"));
            Components.Add(component);
        }
        public Entity Retrieve(string name, Guid id, ColumnSet columns) => Solutions.Single(s => s.Id == id);
        public EntityCollection RetrieveMultiple(QueryBase queryBase)
        {
            var query = (QueryExpression)queryBase;
            if (query.EntityName == "webresource")
            {
                var ids = query.Criteria.Conditions[0].Values.Cast<Guid>().ToList();
                return new EntityCollection(ids.Where(id => ContentRows.ContainsKey(id)).Select(id => ContentRows[id]).ToList());
            }
            var rows = (query.EntityName == "solution" ? Solutions : Components).AsEnumerable();
            foreach (var condition in query.Criteria.Conditions)
            {
                var values = condition.Values.SelectMany(v => v is Guid[] ids ? ids.Cast<object>() : new[] { v }).ToList();
                rows = rows.Where(row =>
                {
                    object actual = condition.AttributeName == "solutionid" && query.EntityName == "solution" ? row.Id : row[condition.AttributeName];
                    if (actual is EntityReference reference) actual = reference.Id;
                    if (actual is OptionSetValue option) actual = option.Value;
                    var contains = values.Any(v => Equals(actual, v));
                    return condition.Operator == ConditionOperator.NotEqual ? !contains : contains;
                });
            }
            if (query.PageInfo.PageNumber == 0) return new EntityCollection(rows.ToList());
            Pages.Add(query.PageInfo.PageNumber);
            var all = rows.ToList();
            var offset = (query.PageInfo.PageNumber - 1) * PageSize;
            return new EntityCollection(all.Skip(offset).Take(PageSize).ToList())
            {
                MoreRecords = all.Count > offset + PageSize, PagingCookie = "test-page-" + query.PageInfo.PageNumber
            };
        }
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveMetadataChangesRequest)
            {
                var response = new RetrieveMetadataChangesResponse();
                response.Results["EntityMetadata"] = ManagedEntities;
                return response;
            }
            var add = (AddSolutionComponentRequest)request;
            Writes.Add(add);
            if (add.ComponentId == FailId) throw new InvalidOperationException("simulated CRM failure");
            var target = Solutions.Single(s => s.GetAttributeValue<string>("uniquename") == add.SolutionUniqueName);
            var existing = Components.FirstOrDefault(c => c.GetAttributeValue<EntityReference>("solutionid").Id == target.Id &&
                c.GetAttributeValue<Guid>("objectid") == add.ComponentId && c.GetAttributeValue<OptionSetValue>("componenttype").Value == add.ComponentType);
            var behavior = add.DoNotIncludeSubcomponents ? 1 : 0;
            if (existing == null) Component(target, add.ComponentId, add.ComponentType, behavior);
            else existing["rootcomponentbehavior"] = new OptionSetValue(behavior);
            AfterWrite?.Invoke();
            return new AddSolutionComponentResponse();
        }
        public Guid Create(Entity entity) => throw new NotSupportedException();
        public void Update(Entity entity) => throw new NotSupportedException();
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        public void Associate(string name, Guid id, Relationship relation, EntityReferenceCollection entities) => throw new NotSupportedException();
        public void Disassociate(string name, Guid id, Relationship relation, EntityReferenceCollection entities) => throw new NotSupportedException();
    }
}
