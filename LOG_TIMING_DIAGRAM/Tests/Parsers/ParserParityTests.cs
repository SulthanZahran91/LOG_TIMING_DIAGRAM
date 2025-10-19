using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LOG_TIMING_DIAGRAM.Models;
using LOG_TIMING_DIAGRAM.Parsers;
using Xunit;

namespace LOG_TIMING_DIAGRAM.Tests.Parsers
{
    public sealed class ParserParityTests
    {
        public static IEnumerable<object[]> SampleLogs()
        {
            foreach (var (file, parserName) in SampleLogDefinitions)
            {
                yield return new object[] { file, parserName };
            }
        }

        private static readonly (string FileName, string ParserName)[] SampleLogDefinitions =
        {
            ("plc_debug_parser_01.log", "plc_debug"),
            ("plc_debug_parser_02.log", "plc_debug"),
            ("plc_debug_parser_03.log", "plc_debug"),
            ("plc_debug_parser_04.log", "plc_debug"),
            ("plc_debug_parser_05.log", "plc_debug"),
            ("plc_tab_parser_01.log", "plc_tab"),
            ("plc_tab_parser_02.log", "plc_tab"),
            ("plc_tab_parser_03.log", "plc_tab"),
            ("plc_tab_parser_04.log", "plc_tab"),
            ("plc_tab_parser_05.log", "plc_tab")
        };

        [Theory]
        [MemberData(nameof(SampleLogs))]
        public async Task ParserProcessesGeneratedSamples(string logFile, string parserName)
        {
            var parser = ParserRegistry.Instance.Resolve(parserName);
            Assert.NotNull(parser);

            var path = ResolveLogPath(logFile);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Sample log '{logFile}' was not found at '{path}'.");
            }

            var result = await parser.ParseAsync(path, CancellationToken.None, progress: null)
                .ConfigureAwait(false);

            Assert.True(
                result.Success,
                result.Errors.Count > 0
                    ? $"Parse failed: {string.Join("; ", result.Errors.Select(e => $"{e.Line}:{e.Reason}"))}"
                    : "Parse result reported failure.");
            Assert.NotNull(result.Data);
            Assert.Empty(result.Errors);
            Assert.NotEmpty(result.Data.Entries);

            var first = result.Data.Entries.First();
            Assert.NotNull(first.DeviceId);
            Assert.NotNull(first.SignalName);
            Assert.False(string.IsNullOrWhiteSpace(first.SignalKey));
            Assert.Contains("::", first.SignalKey, StringComparison.Ordinal);
            Assert.True(first.Timestamp.Year >= 2023);
            Assert.True(Enum.IsDefined(typeof(SignalType), first.SignalType));

            foreach (var key in result.Data.Signals)
            {
                Assert.Contains("::", key);
            }
        }

        private static string ResolveLogPath(string logFile)
        {
            var baseDirectory = AppContext.BaseDirectory;
            var candidate = Path.Combine(baseDirectory, "generated", logFile);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            // Fallback to project root if running outside of published test assets.
            return Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "generated", logFile));
        }
    }
}
