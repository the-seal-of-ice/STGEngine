using System.Collections.Generic;
using STGEngine.Core.Timeline;

namespace STGEngine.Core.DataModel
{
    /// <summary>
    /// Top-level stage data: a sequence of timeline segments.
    /// Serialized as a standalone YAML file referencing pattern files by ID.
    /// </summary>
    public class Stage
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        /// <summary>Ordered list of segments composing this stage.</summary>
        public List<TimelineSegment> Segments { get; set; } = new();
    }
}
