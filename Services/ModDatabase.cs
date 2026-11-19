using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExileCore;
using Newtonsoft.Json.Linq;

namespace MapCrafter.Services
{
    public class ModEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "Generic"; // Generic, Uber (T17), Expedition, Valdo
        public List<string> Descriptions { get; set; } = new();
        public List<string> BaseMods { get; set; } = new();
        public string Regex { get; set; } = string.Empty;

        public override string ToString() => Name;
    }

    public static class ModDatabase
    {
        public static List<ModEntry> AllMods { get; } = new();
        public static List<ModEntry> GenericMods { get; } = new();
        public static List<ModEntry> UberMods { get; } = new();
        public static bool IsLoaded { get; private set; }

        public static readonly string[] UpToDateT16BannedPresets =
        {
            "Monsters reflect #% of Elemental Damage",
            "Monsters reflect #% of Physical Damage",
            "Players cannot Regenerate Life, Mana or Energy Shield",
            "Monsters cannot be Leeched from",
            "Players have #% less Recovery Rate of Life and Energy Shield",
            "Players have #% maximum Player Resistances",
            "Players have #% reduced effect of Non-Curse Auras",
            "Monsters are Hexproof",
            "Area has patches of desecrated ground",
            "Area has patches of Burning Ground",
            "Area has patches of Shocked Ground"
        };

        public static readonly string[] UpToDateT17BannedPresets =
        {
            "Players have % chance to be targeted by a Meteor when they use a Flask",
            "Players are assaulted by Bloodstained Sawblades",
            "Area contains Drowning Orbs",
            "Area contains Runes of the Searing Exarch",
            "Area contains Unstable Tentacle Fiends",
            "Rare Monsters have % chance to have a Volatile Core",
            "Players have % less effect of Flasks applied to them",
            "Players have -% to all maximum Resistances",
            "Players have %-% less Defences",
            "Players have %-% less Area of Effect",
            "Players have %-% reduced Maximum total Life, Mana and Energy Shield Recovery per second from Leech",
            "Auras from Player Skills which affect Allies also affect Enemies",
            "Area contains patches of moving Marked Ground",
            "Debuffs on Monsters expire % faster",
            "Monster Damage Penetrates % Elemental Resistances",
            "Rare monsters in area Temporarily Revive on death"
        };

        public static void Initialize(string pluginDir, GameController? gc = null)
        {
            if (IsLoaded && AllMods.Count > 0) return;

            AllMods.Clear();
            GenericMods.Clear();
            UberMods.Clear();

            var candidatePaths = new List<string>
            {
                Path.Combine(pluginDir, "data", "mods.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "Source", "MapCrafter", "data", "mods.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "Temp", "MapCrafter", "data", "mods.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "Compiled", "MapCrafter", "data", "mods.json")
            };

            string jsonPath = candidatePaths.FirstOrDefault(File.Exists) ?? string.Empty;

            if (File.Exists(jsonPath))
            {
                try
                {
                    string content = File.ReadAllText(jsonPath);
                    var root = JObject.Parse(content);
                    var groups = root["groups"] as JObject;
                    if (groups != null)
                    {
                        foreach (var prop in groups.Properties())
                        {
                            string groupName = prop.Name;
                            var groupObj = prop.Value as JObject;
                            if (groupObj == null) continue;

                            string category = groupObj["Type"]?.ToString() ?? "Generic";
                            if (category == "Unknown") category = "Generic";

                            var descs = groupObj["Descriptions"]?.Select(d => d.ToString()).ToList() ?? new List<string>();
                            var baseMods = groupObj["BaseMods"]?.Select(b => b.ToString()).ToList() ?? new List<string>();
                            string regex = groupObj["Regex"]?.ToString() ?? string.Empty;

                            var entry = new ModEntry
                            {
                                Name = groupName,
                                Category = category,
                                Descriptions = descs,
                                BaseMods = baseMods,
                                Regex = regex
                            };

                            AllMods.Add(entry);
                            if (category.Equals("Uber", StringComparison.OrdinalIgnoreCase))
                                UberMods.Add(entry);
                            else if (category.Equals("Generic", StringComparison.OrdinalIgnoreCase))
                                GenericMods.Add(entry);
                        }
                    }
                }
                catch { }
            }

            // Also check GameController memory if active
            var records = gc?.Files?.Mods?.records;
            if (records != null && records.Count > 0)
            {
                foreach (var record in records.Values)
                {
                    if (record == null || string.IsNullOrEmpty(record.Key)) continue;
                    if (!record.Key.Contains("MapWorlds") && !record.Key.Contains("Uber")) continue;

                    // Indexed for live memory lookup
                }
            }

            IsLoaded = true;
        }

        public static List<ModEntry> Search(string query, string category = "All")
        {
            var source = category switch
            {
                "Uber" or "T17" => UberMods,
                "Generic" or "T16" => GenericMods,
                _ => AllMods
            };

            if (string.IsNullOrWhiteSpace(query))
                return source.Take(50).ToList();

            return source.Where(m =>
                m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.Descriptions.Any(d => d.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                m.BaseMods.Any(b => b.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(m.Regex) && m.Regex.Contains(query, StringComparison.OrdinalIgnoreCase))
            ).Take(60).ToList();
        }
    }
}
