using System.Collections.Generic;

namespace LOG_TIMING_DIAGRAM.Models
{
    public sealed class FilterPreset
    {
        public string Name { get; set; } = string.Empty;

        public string SearchText { get; set; } = string.Empty;

        public bool IncludeBoolean { get; set; }

        public bool IncludeInteger { get; set; }

        public bool IncludeString { get; set; }

        public bool ShowOnlyChanged { get; set; }

        public List<string> SelectedSignals { get; set; } = new List<string>();

        public List<string> SelectedDevices { get; set; } = new List<string>();
    }
}
