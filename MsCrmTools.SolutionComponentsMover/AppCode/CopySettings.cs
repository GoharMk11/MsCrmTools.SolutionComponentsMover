using Microsoft.Xrm.Sdk;
using System.Collections.Generic;

namespace MsCrmTools.SolutionComponentsMover.AppCode
{
    internal class CopySettings
    {
        public bool CheckBestPractice { get; set; }
        public List<int> ComponentsTypes { get; set; }
        public int OrganizationMajorVersion { get; set; }
        public List<Entity> SourceSolutions { get; set; }
        public List<Entity> TargetSolutions { get; set; }
        public bool AllComponents { get; internal set; }
    }
}