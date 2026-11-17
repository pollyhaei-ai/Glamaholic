using Lumina.Excel.Sheets;
using System;
using Lumina.Extensions;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Glamaholic.Ui {
    internal class FilterInfo {
        private uint MaxLevel { get; }
        private string Query { get; }
        private HashSet<ClassJob> WantedJobs { get; } = new();
        private HashSet<string> Tags { get; } = new();
        private HashSet<string> ExcludeTags { get; } = new();
        private HashSet<uint> ItemIds { get; } = new();
        private HashSet<string> ItemNames { get; } = new();

        internal FilterInfo(string filter) {
            var queryWords = new List<string>();

            var quoteType = -1;
            string? quoted = null;
            foreach (var immutableWord in filter.Split(' ')) {
                var word = immutableWord;

                if (quoted != null) {
                    quoted += " ";

                    var quoteIndex = word.IndexOf('"');
                    if (quoteIndex > -1) {
                        quoted += word[..quoteIndex];

                        switch (quoteType) {
                            case 1:
                                this.Tags.Add(quoted);
                                break;
                            case 2:
                                this.ItemNames.Add(quoted);
                                break;
                            case 3:
                                this.ExcludeTags.Add(quoted);
                                break;
                        }

                        quoted = null;
                        quoteType = -1;

                        var rest = word[(quoteIndex + 1)..];
                        if (rest.Length > 0) {
                            word = rest;
                        } else {
                            continue;
                        }
                    } else {
                        quoted += word;
                        continue;
                    }
                }

                if (word.StartsWith("j:")) {
                    var abbr = word[2..];
                    var job = FindJob(abbr);
                    if (job != null) {
                        this.WantedJobs.Add(job.Value);
                    }

                    continue;
                }

                if (word.StartsWith("lvl:")) {
                    if (uint.TryParse(word[4..], out var level)) {
                        this.MaxLevel = level;
                    }

                    continue;
                }

                if (word.StartsWith("t:")) {
                    if (word.StartsWith("t:\"")) {
                        if (word.EndsWith('"') && word.Length >= 5) {
                            this.Tags.Add(word[3..^1]);
                        } else {
                            quoteType = 1;
                            quoted = word[3..];
                        }
                    } else {
                        this.Tags.Add(word[2..]);
                    }

                    continue;
                }

                if (word.StartsWith("!t:")) {
                    if (word.StartsWith("!t:\"")) {
                        if (word.EndsWith('"') && word.Length >= 6) {
                            this.ExcludeTags.Add(word[4..^1]);
                        } else {
                            quoteType = 3;
                            quoted = word[4..];
                        }
                    } else {
                        this.ExcludeTags.Add(word[3..]);
                    }

                    continue;
                }

                if (word.StartsWith("id:")) {
                    if (uint.TryParse(word[3..], out var id)) {
                        this.ItemIds.Add(id);
                    }

                    continue;
                }

                if (word.StartsWith("i:")) {
                    if (word.StartsWith("i:\"")) {
                        if (word.EndsWith('"') && word.Length >= 5) {
                            this.ItemNames.Add(word[3..^1]);
                        } else {
                            quoteType = 2;
                            quoted = word[3..];
                        }
                    } else {
                        this.ItemNames.Add(word[2..]);
                    }

                    continue;
                }

                queryWords.Add(word);
            }

            this.Query = string.Join(' ', queryWords).ToLowerInvariant();
        }

        internal bool Matches(SavedPlate plate) {
            // if the name doesn't match the query, it's not a match, obviously
            if (this.Query.Length != 0 && !plate.Name.ToLowerInvariant().Contains(this.Query)) {
                return false;
            }

            // if there's nothing custom about this filter, this is a match
            var notCustom = this.MaxLevel == 0
                            && this.WantedJobs.Count == 0
                            && this.Tags.Count == 0
                            && this.ExcludeTags.Count == 0
                            && this.ItemIds.Count == 0
                            && this.ItemNames.Count == 0;
            if (notCustom) {
                return true;
            }

            foreach (var tag in this.Tags) {
                if (!plate.Tags.Contains(tag)) {
                    return false;
                }
            }

            foreach (var tag in this.ExcludeTags) {
                if (plate.Tags.Contains(tag)) {
                    return false;
                }
            }

            if (this.ItemIds.Count > 0) {
                var matching = plate.Items.Values
                    .Select(mirage => mirage.ItemId)
                    .Intersect(this.ItemIds)
                    .Count();

                if (matching != this.ItemIds.Count) {
                    return false;
                }
            }

            if (this.ItemNames.Count > 0) {
                var sheet = GameData.Sheet<Item>();
                if (sheet == null) {
                    return false;
                }

                var names = plate.Items.Values
                    .Select(mirage => sheet.GetRowOrDefault(mirage.ItemId % Util.HqItemOffset))
                    .Where(item => item != null)
                    .Cast<Item>()
                    .Select(item => item.Name.ExtractText().ToLowerInvariant())
                    .ToArray();

                foreach (var needle in this.ItemNames) {
                    var lower = needle.ToLowerInvariant();
                    if (!names.Any(name => name.Contains(lower))) {
                        return false;
                    }
                }
            }

            foreach (var mirage in plate.Items.Values) {
                var itemSheet = GameData.Sheet<Item>();
                if (itemSheet == null || !itemSheet.TryGetRow(mirage.ItemId % Util.HqItemOffset, out var item)) {
                    continue;
                }

                if (this.MaxLevel != 0 && item.LevelEquip > this.MaxLevel) {
                    return false;
                }

                foreach (var job in this.WantedJobs) {
                    var category = item.ClassJobCategory.ValueNullable;
                    if (category == null) {
                        continue;
                    }

                    if (!this.CanWear(category.Value, job)) {
                        return false;
                    }
                }
            }

            return true;
        }

        private ClassJob? FindJob(string query) {
            foreach (var sheet in new[] { GameData.Sheet<ClassJob>(), GameData.EnglishSheet<ClassJob>() }) {
                if (sheet == null) {
                    continue;
                }

                var job = sheet.FirstOrNull(row =>
                    row.Abbreviation.ExtractText().Equals(query, StringComparison.OrdinalIgnoreCase)
                    || row.Name.ExtractText().Equals(query, StringComparison.OrdinalIgnoreCase));
                if (job != null) {
                    return job.Value;
                }
            }

            return null;
        }

        private bool CanWear(ClassJobCategory category, ClassJob classJob) {
            // ClassJobCategory columns are the English abbreviations (PLD, WAR, ...).
            var job = GameData.EnglishSheet<ClassJob>()?.GetRowOrDefault(classJob.RowId);
            var abbreviation = job?.Abbreviation.ExtractText();
            if (string.IsNullOrEmpty(abbreviation)) {
                abbreviation = classJob.Abbreviation.ExtractText();
            }

            var getter = category.GetType().GetProperty(abbreviation, BindingFlags.Public | BindingFlags.Instance);
            if (getter == null) {
                return false;
            }

            var value = getter.GetValue(category);
            if (value is bool res) {
                return res;
            }

            return false;
        }
    }
}
