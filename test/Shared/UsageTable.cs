using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LaunchDarkly.AI.TestHelpers
{
    /// <summary>
    /// Reads a package's usage table (<c>usage-ids.txt</c>, copied next to the test assembly).
    /// </summary>
    public static class UsageTable
    {
        public sealed record Row(string Name, IReadOnlyList<string> Ids)
        {
            public string TypeName => Name.Substring(0, Name.LastIndexOf('.'));

            public string MemberName => Name.Substring(Name.LastIndexOf('.') + 1);
        }

        public static IReadOnlyList<Row> Read()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "usage-ids.txt");
            return File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
                .Select(l =>
                {
                    var parts = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var ids = parts.Single(p => p.StartsWith("usage=", StringComparison.Ordinal))
                        .Substring("usage=".Length)
                        .Split(',');
                    return new Row(parts[0], ids);
                })
                .ToList();
        }

        /// <summary>Theory data: one row per line.</summary>
        public static IEnumerable<object[]> Rows() => Read().Select(r => new object[] { r.Name });

        /// <summary>Whether the assembly exports a public type and member with the row's name.</summary>
        public static bool IsExported(Assembly assembly, Row row) =>
            assembly.GetExportedTypes()
                .Where(t => t.Name == row.TypeName)
                .Any(t => t.GetMember(row.MemberName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance).Length > 0);
    }
}
