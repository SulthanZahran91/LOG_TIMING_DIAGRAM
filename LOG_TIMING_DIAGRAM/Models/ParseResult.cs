using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LOG_TIMING_DIAGRAM.Models
{
    public sealed class ParseResult
    {
        public ParseResult(ParsedLog data, IEnumerable<ParseError> errors)
        {
            Data = data;
            Errors = new ReadOnlyCollection<ParseError>((errors ?? Enumerable.Empty<ParseError>()).ToList());
        }

        public ParsedLog Data { get; }

        public IReadOnlyList<ParseError> Errors { get; }

        public bool Success => Data != null;

        public bool HasErrors => Errors.Count > 0;

        public static ParseResult Failed(IEnumerable<ParseError> errors)
        {
            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            return new ParseResult(null, errors);
        }

        public ParseResult WithData(ParsedLog log)
        {
            return new ParseResult(log, Errors);
        }
    }
}
