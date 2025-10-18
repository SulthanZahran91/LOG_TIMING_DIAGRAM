using System;

namespace LOG_TIMING_DIAGRAM.Models
{
    public sealed class ParseProgress
    {
        public ParseProgress(string filePath, int linesRead, int? totalLines = null)
        {
            FilePath = filePath;
            LinesRead = linesRead;
            TotalLines = totalLines;
            Timestamp = DateTime.UtcNow;
        }

        public string FilePath { get; }

        public int LinesRead { get; }

        public int? TotalLines { get; }

        public DateTime Timestamp { get; }
    }
}
