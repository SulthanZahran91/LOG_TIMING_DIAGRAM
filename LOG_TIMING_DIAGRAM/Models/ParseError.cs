namespace LOG_TIMING_DIAGRAM.Models
{
    public sealed class ParseError
    {
        public ParseError(int line, string content, string reason, string filePath)
        {
            Line = line;
            Content = content;
            Reason = reason;
            FilePath = filePath;
        }

        public int Line { get; }

        public string Content { get; }

        public string Reason { get; }

        public string FilePath { get; }

        public override string ToString()
        {
            return $"[{FilePath}] Line {Line}: {Reason} | {Content}";
        }
    }
}
