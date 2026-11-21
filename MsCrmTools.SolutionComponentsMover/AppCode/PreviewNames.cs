using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MsCrmTools.SolutionComponentsMover.AppCode
{
    // Presentation-only lookups: no requests are made to URLs supplied by the user.
    internal static class PreviewNames
    {
        private static readonly Dictionary<int, string[]> Tables = new Dictionary<int, string[]>
        {
            { 61, new[] { "webresource", "webresourceid", "name" } },
            { 60, new[] { "systemform", "formid", "name" } },
            { 26, new[] { "savedquery", "savedqueryid", "name" } },
            { 29, new[] { "workflow", "workflowid", "name" } },
            { 90, new[] { "plugintype", "plugintypeid", "typename" } },
            { 91, new[] { "pluginassembly", "pluginassemblyid", "name" } },
            { 92, new[] { "sdkmessageprocessingstep", "sdkmessageprocessingstepid", "name" } },
            { 80, new[] { "appmodule", "appmoduleid", "name" } }
        };

        public static void Populate(BatchPlan plan, IOrganizationService service, OptionMetadataCollection types)
        {
            foreach (var group in plan.Components.GroupBy(c => c.Type))
            {
                foreach (var component in group)
                    component.TypeName = group.Key == 61 ? "Web Resource" :
                        types?.FirstOrDefault(o => o.Value == group.Key)?.Label?.UserLocalizedLabel?.Label ?? "Component type " + group.Key;
                string[] table;
                if (!Tables.TryGetValue(group.Key, out table)) continue;
                var components = group.ToList();
                for (var offset = 0; offset < components.Count; offset += 200)
                {
                    var batch = components.Skip(offset).Take(200).ToList();
                    try
                    {
                        var query = new QueryExpression(table[0])
                        {
                            ColumnSet = new ColumnSet(table[2]),
                            PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 }
                        };
                        query.Criteria.AddCondition(table[1], ConditionOperator.In, batch.Select(c => (object)c.Id).ToArray());
                        query.AddOrder(table[1], OrderType.Ascending);
                        var names = new Dictionary<Guid, string>();
                        EntityCollection page;
                        do
                        {
                            page = service.RetrieveMultiple(query);
                            foreach (var row in page.Entities) names[row.Id] = row.GetAttributeValue<string>(table[2]);
                            query.PageInfo.PageNumber++;
                            query.PageInfo.PagingCookie = page.PagingCookie;
                        } while (page.MoreRecords);
                        foreach (var component in batch)
                        {
                            string name;
                            if (names.TryGetValue(component.Id, out name) && !string.IsNullOrWhiteSpace(name)) component.DisplayName = name;
                            else component.NameWarning = "Name not returned by CRM; use the component ID in technical details.";
                        }
                    }
                    catch (Exception error)
                    {
                        foreach (var component in batch) component.NameWarning = "Name lookup failed: " + error.Message;
                    }
                }
            }
            var missing = plan.Components.Count(c => string.IsNullOrEmpty(c.DisplayName));
            if (missing > 0) plan.Warnings.Add($"Names unavailable for {missing} component(s). IDs remain available in technical details; the copy plan is unchanged.");
        }
    }
}
