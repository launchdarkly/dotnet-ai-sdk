using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace LaunchDarkly.AI.Tests
{
    /// <summary>
    /// Every C# block in the README and agents.md files is a sample that compiles: it sits under a
    /// <c>&lt;!-- snippet: name --&gt;</c> marker and is identical to the code between the
    /// <c>// snippet: name</c> and <c>// end-snippet</c> comments in the examples project, which the
    /// solution builds. Several spans with one name are joined with a blank line.
    /// </summary>
    public class DocSnippetTests
    {
        private const string DocMarkerStart = "<!-- snippet: ";
        private const string DocMarkerEnd = " -->";
        private const string CodeMarkerStart = "// snippet: ";
        private const string CodeMarkerEnd = "// end-snippet";

        private static readonly string _root = FindRepositoryRoot();

        [Fact]
        public void EveryCSharpBlockInTheDocsMatchesACompiledSample()
        {
            var samples = CompiledSamples();
            var problems = new List<string>();
            foreach (var doc in DocFiles())
            {
                foreach (var block in CSharpBlocks(doc, problems))
                {
                    if (!samples.TryGetValue(block.Name, out var sample))
                    {
                        problems.Add($"{block.Location}: no sample \"{block.Name}\" in examples/");
                    }
                    else if (sample != block.Code)
                    {
                        problems.Add($"{block.Location}: block \"{block.Name}\" differs from its sample in examples/");
                    }
                }
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }

        [Fact]
        public void EveryCompiledSampleIsUsedInTheDocs()
        {
            var used = new HashSet<string>(
                DocFiles().SelectMany(doc => CSharpBlocks(doc, new List<string>())).Select(b => b.Name),
                StringComparer.Ordinal);
            var unused = CompiledSamples().Keys.Where(name => !used.Contains(name)).OrderBy(n => n, StringComparer.Ordinal).ToList();

            Assert.True(unused.Count == 0, "Samples no document uses: " + string.Join(", ", unused));
        }

        [Fact]
        public void TheExamplesHoldSamples()
        {
            Assert.NotEmpty(CompiledSamples());
        }

        /// <summary>The documents whose C# blocks must compile.</summary>
        private static IEnumerable<string> DocFiles()
        {
            yield return Path.Combine(_root, "README.md");
            yield return Path.Combine(_root, "AGENTS.md");
            yield return Path.Combine(_root, "examples", "README.md");
            foreach (var package in Directory.GetDirectories(Path.Combine(_root, "src")).OrderBy(d => d, StringComparer.Ordinal))
            {
                foreach (var name in new[] { "README.md", "agents.md" })
                {
                    var path = Path.Combine(package, name);
                    if (File.Exists(path))
                    {
                        yield return path;
                    }
                }
            }
        }

        /// <summary>The samples in the examples project's C# files, by name.</summary>
        private static Dictionary<string, string> CompiledSamples()
        {
            var spans = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var files = Directory.GetFiles(Path.Combine(_root, "examples"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsBuildOutput(f))
                .OrderBy(f => f, StringComparer.Ordinal);
            foreach (var file in files)
            {
                var lines = ReadLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].Trim();
                    if (!trimmed.StartsWith(CodeMarkerStart, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var name = trimmed.Substring(CodeMarkerStart.Length).Trim();
                    var body = new List<string>();
                    for (i++; i < lines.Length && lines[i].Trim() != CodeMarkerEnd; i++)
                    {
                        body.Add(lines[i]);
                    }

                    Assert.True(i < lines.Length, $"{file}: sample \"{name}\" has no end-snippet comment");
                    if (!spans.TryGetValue(name, out var list))
                    {
                        spans[name] = list = new List<string>();
                    }

                    list.Add(Dedent(body));
                }
            }

            return spans.ToDictionary(e => e.Key, e => string.Join("\n\n", e.Value), StringComparer.Ordinal);
        }

        /// <summary>
        /// The C# blocks of a document. A block without a marker, or a marker without a block, is
        /// added to <paramref name="problems"/>.
        /// </summary>
        private static List<(string Name, string Code, string Location)> CSharpBlocks(string doc, List<string> problems)
        {
            var blocks = new List<(string, string, string)>();
            var lines = ReadLines(doc);
            var relative = doc.Substring(_root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? pending = null;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith(DocMarkerStart, StringComparison.Ordinal) && line.EndsWith(DocMarkerEnd, StringComparison.Ordinal))
                {
                    pending = line.Substring(DocMarkerStart.Length, line.Length - DocMarkerStart.Length - DocMarkerEnd.Length).Trim();
                    continue;
                }

                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    var language = line.Substring(3).Trim();
                    var start = i + 1;
                    for (i++; i < lines.Length && lines[i].Trim() != "```"; i++)
                    {
                    }

                    if (language == "csharp" || language == "cs" || language == "c#")
                    {
                        var location = $"{relative}:{start}";
                        if (pending == null)
                        {
                            problems.Add($"{location}: C# block has no <!-- snippet: name --> marker");
                        }
                        else
                        {
                            blocks.Add((pending, Dedent(lines.Skip(start).Take(i - start).ToList()), location));
                        }
                    }

                    pending = null;
                    continue;
                }

                if (pending != null && line.Length > 0)
                {
                    problems.Add($"{relative}:{i + 1}: snippet marker \"{pending}\" is not followed by a C# block");
                    pending = null;
                }
            }

            return blocks;
        }

        private static string Dedent(IReadOnlyList<string> lines)
        {
            var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
            return string.Join("\n", lines.Select(l => l.Trim().Length == 0 ? "" : l.Substring(indent).TrimEnd())).Trim('\n');
        }

        private static string[] ReadLines(string path) => File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');

        private static bool IsBuildOutput(string path)
        {
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return parts.Contains("bin") || parts.Contains("obj");
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LaunchDarkly.AI.Server.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
        }
    }
}
