using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MsCrmTools.SolutionComponentsMover.AppCode
{
    internal sealed class InputValidationResult
    {
        public SolutionInput Input { get; set; }
        public Entity Solution { get; set; }
        public string Error { get; set; }
        public bool Duplicate { get; set; }
    }

    internal sealed class PlannedComponent
    {
        public Guid Id { get; set; }
        public int Type { get; set; }
        public int? Behavior { get; set; }
        public List<string> Sources { get; set; }
        public bool AlreadyExists { get; set; }
        public bool TargetPresent { get; set; }
        public string DisplayName { get; set; }
        public string TypeName { get; set; }
        public string NameWarning { get; set; }
        public string Fingerprint { get; set; }
        public string Tracking { get; set; }
        public string ActionLabel => AlreadyExists ? "Skip — already included" : TargetPresent ? "Expand existing coverage" : "Add to target";
        public string Key => Type + ":" + Id;
    }

    internal sealed class BatchPlan
    {
        public Guid OrganizationId { get; set; }
        public Guid UserId { get; set; }
        public string EnvironmentUrl { get; set; }
        public RunReport PreviousRun { get; set; }
        public CopySettings Settings { get; set; }
        public Entity Target { get; set; }
        public int RetrievedCount { get; set; }
        public List<PlannedComponent> Components { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public int ExistingCount => Components.Count(c => c.AlreadyExists);
        public int PlannedCount => Components.Count - ExistingCount;
        public string Signature => string.Join(";", Components.OrderBy(c => c.Key)
            .Select(c => c.Key + ":" + c.Behavior + ":" + c.AlreadyExists));
    }

    internal sealed class ComponentCopyResult
    {
        public string DisplayName { get; set; }
        public string TypeName { get; set; }
        public int Attempts { get; set; }
        public string RetryNotes { get; set; }
        public string Source { get; set; }
        public int Type { get; set; }
        public Guid Id { get; set; }
        public string Target { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
    }

    internal static class ComponentCoverage
    {
        public static int? Behavior(Entity component)
        {
            var behavior = component.GetAttributeValue<OptionSetValue>("rootcomponentbehavior")?.Value;
            if (behavior.HasValue && (behavior < 0 || behavior > 2))
                throw new InvalidOperationException("Unsupported rootcomponentbehavior: " + behavior);
            return behavior;
        }

        public static int? Merge(IEnumerable<int?> behaviors)
        {
            var values = behaviors.Distinct().ToList();
            if (values.Count == 1) return values[0];
            if (values.Contains(null))
                throw new InvalidOperationException("Cannot safely merge missing and explicit inclusion behaviors. Review the source components.");
            // 0: include all; 1: exclude subcomponents; 2: shell only.
            return values.Contains(0) ? 0 : values.Contains(1) ? 1 : 2;
        }

        public static bool Satisfies(int? existing, int? required)
        {
            if (existing == required) return true;
            if (!existing.HasValue || !required.HasValue) return false;
            return existing == 0 || (existing == 1 && required == 2);
        }
    }
}
