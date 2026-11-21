using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MsCrmTools.SolutionComponentsMover.AppCode
{
    internal sealed class StaleContentException : InvalidOperationException
    {
        public int PreviousWriteAttempts { get; set; }
        public StaleContentException(string message) : base(message) { }
    }

    internal static class ContentSnapshots
    {
        private sealed class Definition
        {
            public string Table, Id, Payload;
            public string[] Fields;
            public Definition(string table, string id, string payload, params string[] fields) { Table = table; Id = id; Payload = payload; Fields = fields; }
        }
        private static readonly Dictionary<int, Definition> Definitions = new Dictionary<int, Definition>
        {
            { 61, new Definition("webresource", "webresourceid", "content", "content", "name", "displayname", "description", "webresourcetype", "languagecode") },
            { 60, new Definition("systemform", "formid", "formxml", "name", "formxml", "type", "objecttypecode") },
            { 26, new Definition("savedquery", "savedqueryid", "fetchxml", "name", "fetchxml", "layoutxml", "returnedtypecode", "querytype") },
            { 29, new Definition("workflow", "workflowid", "xaml", "name", "xaml", "clientdata", "category", "type", "statecode", "statuscode") },
            { 91, new Definition("pluginassembly", "pluginassemblyid", "content", "name", "content", "version", "culture", "publickeytoken", "isolationmode", "sourcetype") },
            { 90, new Definition("plugintype", "plugintypeid", null, "typename", "name", "pluginassemblyid", "friendlyname") },
            { 92, new Definition("sdkmessageprocessingstep", "sdkmessageprocessingstepid", null, "name", "configuration", "filteringattributes", "stage", "mode", "rank", "statecode", "statuscode", "plugintypeid", "sdkmessageid", "sdkmessagefilterid", "impersonatinguserid", "supporteddeployment") }
        };

        public static void Capture(BatchPlan plan, IOrganizationService service, bool warnings = true)
        {
            foreach (var group in plan.Components.GroupBy(c => c.Type))
            {
                Definition definition;
                if (!Definitions.TryGetValue(group.Key, out definition))
                {
                    foreach (var c in group) { c.Fingerprint = null; c.Tracking = "Not supported for this type"; }
                    continue;
                }
                var components = group.ToList();
                for (var offset = 0; offset < components.Count; offset += 200)
                {
                    var batch = components.Skip(offset).Take(200).ToList();
                    try
                    {
                        var query = new QueryExpression(definition.Table) { ColumnSet = new ColumnSet(definition.Fields.Concat(new[] { "modifiedon" }).ToArray()), PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 } };
                        query.Criteria.AddCondition(definition.Id, ConditionOperator.In, batch.Select(c => (object)c.Id).ToArray());
                        query.AddOrder(definition.Id, OrderType.Ascending);
                        var rows = new Dictionary<Guid, Entity>(); EntityCollection page;
                        do
                        {
                            page = service.RetrieveMultiple(query);
                            foreach (var row in page.Entities) rows[row.Id] = row;
                            query.PageInfo.PageNumber++; query.PageInfo.PagingCookie = page.PagingCookie;
                        } while (page.MoreRecords);
                        foreach (var c in batch)
                        {
                            Entity row;
                            c.Fingerprint = null;
                            if (!rows.TryGetValue(c.Id, out row)) { c.Tracking = "Unavailable: component not returned"; continue; }
                            var full = definition.Payload == null || (row.Attributes.Contains(definition.Payload) && row[definition.Payload] != null);
                            if (full)
                            {
                                c.Tracking = definition.Payload == null ? "Selected metadata fields SHA-256" : "Content and selected fields SHA-256";
                                c.Fingerprint = Hash(definition.Fields.Select(field => field + ":" + Encode(row.Attributes.Contains(field) ? row[field] : null)));
                            }
                            else if (row.Attributes.Contains("modifiedon"))
                            {
                                c.Tracking = "Modified timestamp only (payload unavailable)";
                                c.Fingerprint = Hash(new[] { "modifiedon:" + Encode(row["modifiedon"]) });
                            }
                            else c.Tracking = "Unavailable: no payload or modified timestamp";
                        }
                    }
                    catch (Exception error)
                    {
                        foreach (var c in batch) { c.Fingerprint = null; c.Tracking = "Unavailable: " + error.Message; }
                    }
                }
            }
            if (!warnings) return;
            var missing = plan.Components.Count(c => c.Fingerprint == null);
            var limited = plan.Components.Count(c => c.Tracking != null && (c.Tracking.StartsWith("Modified") || c.Tracking.StartsWith("Selected metadata")));
            plan.Warnings.Add($"Change detection: {plan.Components.Count - missing} tracked, {missing} not tracked, {limited} with limited metadata/timestamp coverage. See technical details.");
            plan.Warnings.Add("Change detection covers selected fields of supported types only, not all dependencies/secure configuration. It is a best-effort check, not a frozen export or lock.");
        }

        public static void Validate(BatchPlan preview, IOrganizationService service)
        {
            var tracked = preview.Components.Where(c => c.Fingerprint != null).ToList();
            if (tracked.Count == 0) return;
            var current = new BatchPlan { Components = tracked.Select(c => new PlannedComponent { Id = c.Id, Type = c.Type }).ToList() };
            Capture(current, service, false);
            foreach (var pair in tracked.Zip(current.Components, (before, after) => new { before, after }))
                if (pair.after.Fingerprint == null || pair.after.Fingerprint != pair.before.Fingerprint || pair.after.Tracking != pair.before.Tracking)
                    throw new StaleContentException("Component changed or cannot be rechecked since preview: " + (pair.before.DisplayName ?? pair.before.Id.ToString()) + ". Run Preview components again. Earlier completed additions, if any, remain.");
        }

        public static void ValidateOne(PlannedComponent component, IOrganizationService service)
        {
            if (component.Fingerprint != null) Validate(new BatchPlan { Components = new List<PlannedComponent> { component } }, service);
        }
        private static string Encode(object value)
        {
            if (value == null) return "null";
            if (value is EntityReference reference) return reference.LogicalName + ":" + reference.Id.ToString("N");
            if (value is OptionSetValue option) return option.Value.ToString(CultureInfo.InvariantCulture);
            if (value is Money money) return money.Value.ToString(CultureInfo.InvariantCulture);
            if (value is DateTime date) return date.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        private static string Hash(IEnumerable<string> values)
        {
            var canonical = string.Join("", values.Select(v => v.Length.ToString(CultureInfo.InvariantCulture) + ":" + v));
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "").ToLowerInvariant();
        }
    }
}
