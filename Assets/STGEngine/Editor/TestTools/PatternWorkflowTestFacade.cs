namespace STGEngine.Editor.TestTools
{
    public class PatternPreviewRequest
    {
        public string PatternId;
        public string Status;
        public string RequestName;
    }

    public static class PatternWorkflowTestFacade
    {
        public static PatternPreviewRequest CreatePreviewRequest(string patternId)
        {
            return new PatternPreviewRequest
            {
                PatternId = patternId,
                Status = "Prepared",
                RequestName = $"pattern-preview-{patternId}"
            };
        }
    }
}
