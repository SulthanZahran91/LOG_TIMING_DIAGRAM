using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LOG_TIMING_DIAGRAM.Models;

namespace LOG_TIMING_DIAGRAM.Parsers
{
    public sealed class ParserRegistry
    {
        private readonly List<GenericTemplateLogParser> _parsers = new List<GenericTemplateLogParser>();
        private GenericTemplateLogParser _defaultParser;

        private ParserRegistry()
        {
            Register(new PLCDebugParser(), isDefault: true);
            Register(new PLCTabParser());
        }

        public static ParserRegistry Instance { get; } = new ParserRegistry();

        public IReadOnlyCollection<GenericTemplateLogParser> Parsers => _parsers.AsReadOnly();

        public void Register(GenericTemplateLogParser parser, bool isDefault = false)
        {
            if (parser == null)
            {
                throw new ArgumentNullException(nameof(parser));
            }

            if (_parsers.Any(p => string.Equals(p.Name, parser.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Parser with name '{parser.Name}' already registered.");
            }

            _parsers.Add(parser);
            if (isDefault || _defaultParser == null)
            {
                _defaultParser = parser;
            }
        }

        public GenericTemplateLogParser Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return _defaultParser;
            }

            return _parsers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public GenericTemplateLogParser DetectForFile(string filePath)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            Debug.WriteLine($"[ParserRegistry] Detecting parser for '{filePath}'.");
            var sampleLines = ReadSampleLines(filePath, 10);
            foreach (var parser in _parsers)
            {
                Debug.WriteLine($"[ParserRegistry] Testing parser '{parser.Name}'.");
                if (parser.CanParse(sampleLines))
                {
                    Debug.WriteLine($"[ParserRegistry] Parser '{parser.Name}' can parse the file.");
                    return parser;
                }
            }

            Debug.WriteLine($"[ParserRegistry] Falling back to default parser '{_defaultParser?.Name ?? "null"}'.");
            return _defaultParser;
        }

        public async Task<ParseResult> ParseAsync(
            string filePath,
            string parserName,
            CancellationToken cancellationToken,
            IProgress<ParseProgress> progress = null)
        {
            Debug.WriteLine($"[ParserRegistry] ParseAsync invoked. File='{filePath}', RequestedParser='{parserName ?? "(auto)"}'.");
            var parser = Resolve(parserName);
            if (parser == null)
            {
                Debug.WriteLine("[ParserRegistry] Requested parser unavailable. Attempting detection.");
                parser = DetectForFile(filePath);
            }
            else
            {
                Debug.WriteLine($"[ParserRegistry] Resolved parser '{parser.Name}'.");
            }

            if (parser == null)
            {
                Debug.WriteLine("[ParserRegistry] No parser resolved. Aborting.");
                return ParseResult.Failed(Array.Empty<ParseError>());
            }

            var result = await parser.ParseAsync(filePath, cancellationToken, progress).ConfigureAwait(false);
            Debug.WriteLine($"[ParserRegistry] ParseAsync completed. Parser='{parser.Name}', Success={result?.Success ?? false}, Entries={result?.Data?.Entries.Count ?? 0}, Errors={result?.Errors?.Count ?? 0}.");
            return result;
        }

        private static IEnumerable<string> ReadSampleLines(string filePath, int maxLines)
        {
            Debug.WriteLine($"[ParserRegistry] Reading up to {maxLines} sample lines from '{filePath}'.");
            var lines = new List<string>(maxLines);
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                while (!reader.EndOfStream && lines.Count < maxLines)
                {
                    var line = reader.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        lines.Add(line);
                    }
                }
            }

            Debug.WriteLine($"[ParserRegistry] Collected {lines.Count} sample line(s).");
            return lines;
        }
    }
}
