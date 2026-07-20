using System.Collections.Generic;
using System.IO;
using JetBrains.Application.changes;
using JetBrains.Application.Parts;
using JetBrains.DataFlow;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.Util;
using WordsXaml.Ini;

namespace WordsXaml.Index
{
    /// <summary>
    /// Solution-scoped component that owns the current <see cref="WordsIndex"/> and rebuilds it when any
    /// *-words.ini file changes. This is the bridge between the pure Ini/ parser and the SDK.
    ///
    /// NOTE: this is deliberately a simple "rescan on change" implementation. It is correct and easy to
    /// follow; for a large solution you would migrate to an <c>ICache</c> (IPsiSourceFileCache) so only
    /// the changed file is reparsed and results survive across sessions. The parser and index types do
    /// not change when you make that move — only this class does.
    /// </summary>
    [SolutionComponent(Instantiation.DemandAnyThreadSafe)]
    public sealed class WordsIndexService
    {
        private readonly ISolution _solution;
        private readonly object _lock = new object();
        private volatile WordsIndex _index = new WordsIndex(EmptyList<WordsEntry>.Instance);

        public WordsIndexService(Lifetime lifetime, ISolution solution, ChangeManager changeManager)
        {
            _solution = solution;

            // Rebuild once on load, then whenever the change manager reports a change. This is coarse
            // (fires on many edits); migrating to an ICache keyed on *-words.ini would make it incremental.
            Rebuild();
            changeManager.Changed.Advise(lifetime, _ => Rebuild());
        }

        public WordsIndex Index => _index;

        private static bool IsWordsIni(string path) =>
            path.EndsWith("-words.ini", System.StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("words.ini", System.StringComparison.OrdinalIgnoreCase);

        private void Rebuild()
        {
            var entries = new List<WordsEntry>();

            // Walk the solution's project files. In a real cache you would instead iterate the SDK's
            // known source files; FileSystemPath enumeration keeps the skeleton dependency-light.
            foreach (var project in _solution.GetAllProjects())
            {
                var dir = project.ProjectFileLocation.Directory;
                if (dir.IsEmpty) continue;

                foreach (var file in Directory.EnumerateFiles(dir.FullPath, "*words.ini", SearchOption.AllDirectories))
                {
                    if (!IsWordsIni(file)) continue;
                    try
                    {
                        var text = File.ReadAllText(file);
                        entries.AddRange(WordsIniParser.Parse(text, file));
                    }
                    catch (IOException)
                    {
                        // File in flux during a save; the next Changed2 tick will pick it up.
                    }
                }
            }

            lock (_lock)
                _index = new WordsIndex(entries);
        }
    }
}
