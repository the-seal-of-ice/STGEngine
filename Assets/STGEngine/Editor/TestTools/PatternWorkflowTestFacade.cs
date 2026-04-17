namespace STGEngine.Editor.TestTools
{
    public class PatternPreviewRequest
    {
        public string PatternId;
        public string Status;
        public string RequestName;
        public string WorkflowName;
        public string EntryPointName;
    }

    public static class PatternWorkflowTestFacade
    {
        private const string DefaultPatternId = "pattern-default";

        public static PatternPreviewRequest CreatePreviewRequest(string patternId)
        {
            var stablePatternId = string.IsNullOrWhiteSpace(patternId) ? DefaultPatternId : patternId;
            return new PatternPreviewRequest
            {
                PatternId = stablePatternId,
                Status = "Prepared",
                RequestName = $"pattern-preview-{stablePatternId}",
                WorkflowName = "PatternPreview",
                EntryPointName = $"{stablePatternId}-entry"
            };
        }
    }
}
