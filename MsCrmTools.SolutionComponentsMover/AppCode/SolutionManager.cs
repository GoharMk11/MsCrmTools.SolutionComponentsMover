using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Metadata.Query;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace MsCrmTools.SolutionComponentsMover.AppCode
{
    internal class SolutionManager
    {
        private readonly IOrganizationService service;


        public SolutionManager(IOrganizationService service)
        {
            this.service = service;
        }

        public IEnumerable<Entity> RetrieveSolutions()
        {
            var qe = new QueryExpression
            {
                EntityName = "solution",
                ColumnSet = new ColumnSet(new[]{
                                            "publisherid", "installedon", "version",
                                            "uniquename", "friendlyname", "description",
                                            "ismanaged"
                                        }),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("isvisible", ConditionOperator.Equal, true),
                        new ConditionExpression("uniquename", ConditionOperator.NotEqual, "Default")
                    }
                }
            };

            return RetrieveAll(qe);
        }

        internal List<InputValidationResult> ResolveInputs(IEnumerable<SolutionInput> inputs, string connectedUrl)
        {
            var results = new List<InputValidationResult>();
            var seen = new HashSet<Guid>();
            foreach (var input in inputs)
            {
                var result = new InputValidationResult { Input = input, Error = input.Error };
                results.Add(result);
                if (result.Error != null) continue;
                if (!SolutionInputParser.MatchesEnvironment(input, connectedUrl))
                {
                    result.Error = "URL host, port or organization does not match the active connection.";
                    continue;
                }
                try
                {
                    var query = new QueryExpression("solution")
                    {
                        ColumnSet = new ColumnSet("friendlyname", "uniquename", "ismanaged", "isvisible")
                    };
                    query.Criteria.AddCondition(input.Id.HasValue ? "solutionid" : "uniquename",
                        ConditionOperator.Equal, input.Id.HasValue ? (object)input.Id.Value : input.UniqueName);
                    var matches = service.RetrieveMultiple(query).Entities;
                    if (matches.Count != 1) result.Error = "Solution was not found uniquely in the connected organization.";
                    else if (!matches[0].GetAttributeValue<bool>("isvisible") ||
                        matches[0].GetAttributeValue<string>("uniquename") == "Default")
                        result.Error = "This solution is not available for selection.";
                    else
                    {
                        result.Solution = matches[0];
                        result.Duplicate = !seen.Add(result.Solution.Id);
                    }
                }
                catch (Exception error) { result.Error = error.Message; }
            }
            return results;
        }

        internal BatchPlan Preview(CopySettings settings)
        {
            if (settings.SourceSolutions == null || settings.SourceSolutions.Count == 0)
                throw new InvalidOperationException("Select at least one source solution.");
            if (settings.TargetSolutions == null || settings.TargetSolutions.Count != 1)
                throw new InvalidOperationException("Select exactly one unmanaged target solution.");
            if (!settings.AllComponents && (settings.ComponentsTypes == null || settings.ComponentsTypes.Count == 0))
                throw new InvalidOperationException("Select at least one component type.");

            var target = service.Retrieve("solution", settings.TargetSolutions[0].Id,
                new ColumnSet("friendlyname", "uniquename", "ismanaged", "isvisible"));
            if (target.GetAttributeValue<bool>("ismanaged") || !target.GetAttributeValue<bool>("isvisible") ||
                target.GetAttributeValue<string>("uniquename") == "Default")
                throw new InvalidOperationException("The target must be an existing, visible unmanaged solution.");
            if (settings.SourceSolutions.Any(s => s.Id == target.Id))
                throw new InvalidOperationException("The target cannot also be a source solution.");

            // Resolve all selected IDs again using this organization's service (including manual selections).
            var sources = settings.SourceSolutions.Select(s => service.Retrieve("solution", s.Id,
                new ColumnSet("friendlyname", "uniquename", "ismanaged", "isvisible"))).ToList();
            if (sources.Any(s => !s.GetAttributeValue<bool>("isvisible") || s.GetAttributeValue<string>("uniquename") == "Default"))
                throw new InvalidOperationException("A source solution is no longer available for selection.");
            var components = RetrieveComponentsFromSolutions(sources.Select(s => s.Id).ToList(), settings.ComponentsTypes, settings.AllComponents);

            if (components.Any(c => c.GetAttributeValue<OptionSetValue>("componenttype").Value == 1 &&
                !ComponentCoverage.Behavior(c).HasValue))
                throw new InvalidOperationException("An entity has no inclusion behavior. Review the source solution; copying with an assumed all-assets rule is unsafe.");

            if (settings.CheckBestPractice)
            {
                var entityIds = components.Where(c => c.GetAttributeValue<OptionSetValue>("componenttype").Value == 1 &&
                    ComponentCoverage.Behavior(c) == 0 && !(bool)c.GetAttributeValue<AliasedValue>("solution.ismanaged").Value)
                    .Select(c => c.GetAttributeValue<Guid>("objectid")).Distinct().ToArray();
                if (entityIds.Length > 0)
                {
                    var managed = GetManagedEntities(entityIds);
                    if (managed.Count > 0)
                        throw new InvalidOperationException("Managed entities included with all assets in unmanaged sources: " +
                            string.Join(", ", managed.Take(10).Select(m => m.DisplayName?.UserLocalizedLabel?.Label ?? m.SchemaName)) +
                            (managed.Count > 10 ? " (and more)" : "") +
                            ". Review the sources or deliberately disable the best-practice check.");
                }
            }

            var existing = RetrieveComponentsFromSolutions(new List<Guid> { target.Id }, null, true)
                .GroupBy(ComponentKey).ToDictionary(g => g.Key, g => g.Select(ComponentCoverage.Behavior).ToList());
            var plan = new BatchPlan { Settings = settings, Target = target, RetrievedCount = components.Count };
            plan.Components = components.GroupBy(ComponentKey).Select(group =>
            {
                var first = group.First();
                var type = first.GetAttributeValue<OptionSetValue>("componenttype").Value;
                // The established 380 rule explicitly excludes settings and implicit values.
                var behavior = type == ComponentTypes.EnvironmentVariableDefinition
                    ? 1 : ComponentCoverage.Merge(group.Select(ComponentCoverage.Behavior));
                List<int?> targetBehaviors;
                var present = existing.TryGetValue(group.Key, out targetBehaviors);
                return new PlannedComponent
                {
                    Id = first.GetAttributeValue<Guid>("objectid"), Type = type, Behavior = behavior,
                    Sources = group.Select(c => sources.First(s => s.Id == c.GetAttributeValue<EntityReference>("solutionid").Id)
                        .GetAttributeValue<string>("uniquename")).Distinct().OrderBy(n => n).ToList(),
                    AlreadyExists = present && targetBehaviors.Any(b => ComponentCoverage.Satisfies(b, behavior))
                };
            }).OrderBy(c => c.Type).ThenBy(c => c.Id).ToList();
            if (plan.Components.Any(c => !c.Behavior.HasValue))
                plan.Warnings.Add("Some components have no inclusion behavior; only matching coverage is skipped.");
            plan.Warnings.Add("Required dependencies are not automatically added. A failure does not roll back earlier additions.");
            return plan;
        }

        private static string ComponentKey(Entity component)
        {
            return component.GetAttributeValue<OptionSetValue>("componenttype").Value + ":" + component.GetAttributeValue<Guid>("objectid");
        }

        internal void CopyComponents(BatchPlan preview, Func<bool> cancelled, Action<int, ComponentCopyResult> report)
        {
            // Re-read and validate immediately before writes. Changed coverage requires a new preview.
            var current = Preview(preview.Settings);
            if (current.Signature != preview.Signature)
                throw new InvalidOperationException("CRM components changed since preview. Run Preview Components again.");
            var completed = 0;
            foreach (var component in current.Components)
            {
                if (cancelled()) return; // An in-flight SDK request completes; no rollback is implied.
                var result = new ComponentCopyResult
                {
                    Source = string.Join("; ", component.Sources), Type = component.Type, Id = component.Id,
                    Target = current.Target.GetAttributeValue<string>("uniquename"), Status = "Already Exists", Error = ""
                };
                if (!component.AlreadyExists)
                {
                    try
                    {
                        var request = new AddSolutionComponentRequest
                        {
                            AddRequiredComponents = false, ComponentId = component.Id, ComponentType = component.Type,
                            SolutionUniqueName = result.Target
                        };
                        if (preview.Settings.OrganizationMajorVersion >= 8)
                            request.DoNotIncludeSubcomponents = component.Behavior == 1 || component.Behavior == 2;
                        if (component.Type == ComponentTypes.EnvironmentVariableDefinition)
                        {
                            request.DoNotIncludeSubcomponents = true;
                            request.IncludedComponentSettingsValues = Array.Empty<string>();
                        }
                        service.Execute(request);
                        result.Status = "Added";
                    }
                    catch (Exception error)
                    {
                        result.Status = "Failed";
                        result.Error = error.Message;
                    }
                }
                report(++completed, result);
            }
        }

        internal List<Entity> RetrieveComponentsFromSolutions(List<Guid> solutionsIds, List<int> componentsTypes, bool allComponents)
        {
            var qe = new QueryExpression("solutioncomponent")
            {
                ColumnSet = new ColumnSet("objectid", "componenttype", "rootcomponentbehavior", "solutionid"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("solutionid", ConditionOperator.In, solutionsIds.ToArray())
                    }
                },
                LinkEntities =
                {
                    new LinkEntity
                    {
                        LinkFromEntityName = "solutioncomponent",
                        LinkFromAttributeName = "solutionid",
                        LinkToAttributeName = "solutionid",
                        LinkToEntityName = "solution",
                        EntityAlias = "solution",
                        Columns = new ColumnSet("ismanaged")
                    }
                }
            };

            if (!allComponents)
            {
                var ce = new ConditionExpression("componenttype", ConditionOperator.In, componentsTypes);

                qe.Criteria.Conditions.Add(ce);
            }

            return RetrieveAll(qe);
        }

        private List<Entity> RetrieveAll(QueryExpression query)
        {
            query.PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 };
            query.AddOrder(query.EntityName == "solution" ? "solutionid" : "solutioncomponentid", OrderType.Ascending);
            var result = new List<Entity>();
            EntityCollection page;
            do
            {
                page = service.RetrieveMultiple(query);
                result.AddRange(page.Entities);
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            } while (page.MoreRecords);
            return result;
        }

        private List<EntityMetadata> GetManagedEntities(params Guid[] ids)
        {
            EntityQueryExpression entityQueryExpressionFull = new EntityQueryExpression
            {
                Properties = new MetadataPropertiesExpression
                {
                    AllProperties = false,
                    PropertyNames =
                    {
                        "DisplayName",
                        "LogicalName",
                        "SchemaName",
                    }
                },
                Criteria = new MetadataFilterExpression
                {
                    Conditions =
                    {
                        new MetadataConditionExpression("MetadataId", MetadataConditionOperator.In, ids),
                        new MetadataConditionExpression("IsManaged", MetadataConditionOperator.Equals, true)
                    }
                }
            };

            RetrieveMetadataChangesRequest request = new RetrieveMetadataChangesRequest
            {
                Query = entityQueryExpressionFull,
                ClientVersionStamp = null
            };

            var fullResponse = (RetrieveMetadataChangesResponse)service.Execute(request);
            return fullResponse.EntityMetadata.ToList();
        }
    }

    internal static class ComponentTypes
    {
        public const int EnvironmentVariableDefinition = 380;
        public const int EnvironmentVariableValue = 381;
        // add others here as needed
    }

}