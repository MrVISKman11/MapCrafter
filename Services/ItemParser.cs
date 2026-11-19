using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using MapCrafter.Models;

namespace MapCrafter.Services
{
    public static class ItemParser
    {
        private static readonly Regex UberPrefixRegex = new(@"^Map(UberWeak|UberMod|Uber)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TierNumberRegex = new(@"(\d+)(?=MapWorlds|Worlds|Expedition|Maven|Unique|_|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SuffixRegex = new(@"(MapWorlds|Worlds|Maven|Unique|_+).*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CamelCaseSplitRegex = new(@"([A-Z])", RegexOptions.Compiled);
        private static readonly Regex ExtraSpacesRegex = new(@"\s+", RegexOptions.Compiled);

        private static readonly string[] MoreThreadsStatKeys = { "map_scarab_drop_chance" }; // 3.30 Threads stat key kept its old internal name
        private static readonly string[] MoreCurrencyStatKeys = { "map_currency_drop_chance" };
        private static readonly string[] MoreMapsStatKeys = { "map_item_drop_chance", "map_map_item_drop_chance" };
        private static readonly string[] AtlasDropsStatKeys = { "map_drop_scaling" }; // 3.30 Atlas Drops key
        private static readonly string[] ItemQuantityStatKeys = { "map_item_drop_quantity", "map_total_item_quantity" }; // Explicit quantity mods
        private static readonly string[] ItemRarityStatKeys = { "map_item_drop_rarity", "map_total_item_rarity" };
        private static readonly string[] PackSizeStatKeys = { "map_pack_size", "map_monster_pack_size", "map_total_pack_size" };

        public static ParsedMapItem? ParseMap(GameController gc, NormalInventoryItem itemWrapper, int slotX, int slotY)
        {
            if (itemWrapper == null || itemWrapper.Item == null)
                return null;

            var entity = itemWrapper.Item;
            string path = entity.Path ?? string.Empty;

            var baseItem = gc.Files?.BaseItemTypes?.Translate(path);
            string baseName = baseItem?.BaseName ?? string.Empty;
            string className = baseItem?.ClassName ?? string.Empty;

            bool isMap = (className == "Map") ||
                         path.Contains("Metadata/Items/Maps/", StringComparison.OrdinalIgnoreCase) ||
                         entity.HasComponent<MapKey>();

            // Discard fragments, scarabs, divinations unless actual map
            if (!isMap || path.Contains("CurrencyAfflictionFragment") || path.Contains("MavenInvitation"))
                return null;

            var modsComp = entity.GetComponent<Mods>();
            var baseComp = entity.GetComponent<Base>();
            var qualityComp = entity.GetComponent<Quality>();
            var mapKeyComp = entity.GetComponent<MapKey>();

            var rect = itemWrapper.GetClientRect();
            var windowRect = gc.Window.GetWindowRectangle();
            var absCenter = new Vector2(windowRect.X + rect.Center.X, windowRect.Y + rect.Center.Y);

            int quality = ExtractQuality(gc, itemWrapper, entity, modsComp, qualityComp);

            var parsed = new ParsedMapItem
            {
                ItemWrapper = itemWrapper,
                Entity = entity,
                SlotX = slotX,
                SlotY = slotY,
                ClickPosition = absCenter,
                ClientRect = rect,
                BaseName = baseName,
                Path = path,
                Rarity = modsComp?.ItemRarity ?? ItemRarity.Normal,
                Quality = quality,
                ItemQuantity = quality, // In 3.30, base Item Quantity comes directly from Chisel Quality: "Quality (Quantity): +20%"
                Tier = mapKeyComp?.Tier ?? 0,
                IsCorrupted = baseComp?.isCorrupted ?? false,
                IsMirrored = false
            };

            // Derive tier if missing
            if (parsed.Tier <= 0 && !string.IsNullOrEmpty(baseName))
            {
                var match = Regex.Match(baseName, @"Tier\s*(\d+)", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var t))
                    parsed.Tier = t;
            }
            if (parsed.Tier <= 0 && path.Contains("Tier17", StringComparison.OrdinalIgnoreCase))
                parsed.Tier = 17;

            // Process Mods
            var itemMods = modsComp?.ItemMods;
            if (itemMods != null)
            {
                foreach (var mod in itemMods)
                {
                    if (string.IsNullOrEmpty(mod.RawName)) continue;
                    parsed.RawModNames.Add(mod.RawName);

                    string affixType = mod.ModRecord?.AffixType.ToString() ?? "";
                    if (affixType.Contains("Prefix", StringComparison.OrdinalIgnoreCase))
                        parsed.PrefixCount++;
                    else if (affixType.Contains("Suffix", StringComparison.OrdinalIgnoreCase))
                        parsed.SuffixCount++;

                    // Extract stats (Atlas Drops, Item Quantity, Rarity, Pack Size, More Maps, Currency, Threads)
                    var stats = mod.ModRecord?.StatNames;
                    var values = mod.Values;
                    if (stats != null && values != null)
                    {
                        for (int i = 0; i < stats.Length && i < values.Count; i++)
                        {
                            var key = stats[i].Key ?? string.Empty;
                            int val = values[i];
                            if (ContainsAny(key, MoreThreadsStatKeys))
                                parsed.ScarabsBonus += val;
                            else if (ContainsAny(key, MoreCurrencyStatKeys))
                                parsed.CurrencyBonus += val;
                            else if (ContainsAny(key, MoreMapsStatKeys))
                                parsed.MoreMapsBonus += val;
                            else if (ContainsAny(key, AtlasDropsStatKeys))
                                parsed.AtlasDrops += val;
                            else if (ContainsAny(key, ItemQuantityStatKeys))
                                parsed.ItemQuantity += val;
                            else if (ContainsAny(key, ItemRarityStatKeys))
                                parsed.ItemRarityBonus += val;
                            else if (ContainsAny(key, PackSizeStatKeys))
                                parsed.PackSize += val;
                        }
                    }

                    // Build user-readable line (stripped of internal tags like [EvocatorKTash|Evocator of K'Tash])
                    string rawLine = !string.IsNullOrEmpty(mod.Translation) && !mod.Translation.Contains("<unknown")
                        ? mod.Translation
                        : (!string.IsNullOrEmpty(mod.DisplayName) ? mod.DisplayName : GetReadableModName(mod.RawName));

                    string lineText = MapEvaluator.CleanModLine(rawLine);
                    parsed.ModLines.Add(lineText);
                }
            }

            parsed.ModCount = parsed.PrefixCount + parsed.SuffixCount;
            if (parsed.ModCount == 0 && itemMods != null)
                parsed.ModCount = itemMods.Count;

            // Build full tooltip text matching in-game PoE 3.30 layout
            var sbTooltip = new StringBuilder();
            sbTooltip.AppendLine(baseName);
            if (parsed.Tier > 0)
                sbTooltip.AppendLine($"Map (Tier {parsed.Tier})");
            sbTooltip.AppendLine($"Rarity: {parsed.Rarity}");
            if (parsed.ItemQuantity > 0)
                sbTooltip.AppendLine($"Item Quantity: +{parsed.ItemQuantity}%");
            if (parsed.AtlasDrops > 0)
                sbTooltip.AppendLine($"Atlas Drops: +{parsed.AtlasDrops}%");
            if (parsed.ItemRarityBonus > 0)
                sbTooltip.AppendLine($"Item Rarity: +{parsed.ItemRarityBonus}%");
            if (parsed.PackSize > 0)
                sbTooltip.AppendLine($"Monster Pack Size: +{parsed.PackSize}%");
            if (parsed.MoreMapsBonus > 0)
                sbTooltip.AppendLine($"More Maps: +{parsed.MoreMapsBonus}%");
            if (parsed.CurrencyBonus > 0)
                sbTooltip.AppendLine($"More Currency: +{parsed.CurrencyBonus}%");
            if (parsed.ScarabsBonus > 0)
                sbTooltip.AppendLine($"More Threads: +{parsed.ScarabsBonus}%");
            if (parsed.Quality > 0)
                sbTooltip.AppendLine($"Quality (Quantity): +{parsed.Quality}%");

            sbTooltip.AppendLine("--- Mod Affixes ---");
            foreach (var line in parsed.ModLines)
            {
                sbTooltip.AppendLine(line);
            }

            parsed.FormattedTooltipText = sbTooltip.ToString();
            return parsed;
        }

        public static string NormalizeModString(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var clean = SuffixRegex.Replace(TierNumberRegex.Replace(UberPrefixRegex.Replace(input, "Map"), ""), "");
            if (clean.StartsWith("Map", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(3);
            return clean.TrimEnd('_').ToLowerInvariant();
        }

        public static string GetReadableModName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return string.Empty;
            var clean = rawName.StartsWith("Map", StringComparison.OrdinalIgnoreCase) ? rawName.Substring(3) : rawName;
            var split = CamelCaseSplitRegex.Replace(clean, " $1");
            return ExtraSpacesRegex.Replace(split, " ").Trim();
        }

        public static int ExtractQuality(GameController gc, NormalInventoryItem itemWrapper, Entity entity, Mods? modsComp, Quality? qualityComp)
        {
            // 1. Try qualityComp.ItemQuality
            int q = qualityComp?.ItemQuality ?? 0;
            if (q > 0 && q <= 30) return q;

            // 2. Try modsComp.AlternateQuality (Maven Chisels)
            if (modsComp != null && modsComp.AlternateQuality > 0 && modsComp.AlternateQuality <= 30)
                return modsComp.AlternateQuality;

            // 3. Scan Quality component memory offsets (0x10 to 0x38) in case PoE 3.30 moved the field offset
            if (qualityComp != null && qualityComp.Address != 0 && gc.Memory != null)
            {
                int[] probeOffsets = { 0x18, 0x14, 0x1c, 0x20, 0x24, 0x28, 0x30 };
                foreach (var off in probeOffsets)
                {
                    try
                    {
                        int val = gc.Memory.Read<int>(qualityComp.Address + off);
                        if (val > 0 && val <= 30)
                            return val;
                    }
                    catch { }
                }
            }

            // 4. Try parsing directly from item tooltip element text
            try
            {
                var tooltip = itemWrapper.Tooltip;
                if (tooltip != null)
                {
                    var lines = new List<string>();
                    ExtractTextRecursive(tooltip, lines, 0);
                    foreach (var line in lines)
                    {
                        if (line.Contains("Quality", StringComparison.OrdinalIgnoreCase))
                        {
                            var match = Regex.Match(line, @"\+?(\d+)%", RegexOptions.IgnoreCase);
                            if (match.Success && int.TryParse(match.Groups[1].Value, out var val) && val > 0 && val <= 30)
                                return val;
                        }
                    }
                }
            }
            catch { }

            return 0;
        }

        private static void ExtractTextRecursive(ExileCore.PoEMemory.Element? el, List<string> lines, int depth)
        {
            if (el == null || depth > 8) return;
            string text = el.TextNoTags ?? el.Text;
            if (!string.IsNullOrEmpty(text)) lines.Add(text);
            long count = el.ChildCount;
            for (int k = 0; k < count && k < 30; k++)
            {
                try
                {
                    ExtractTextRecursive(el.GetChildAtIndex(k), lines, depth + 1);
                }
                catch { }
            }
        }

        private static bool ContainsAny(string str, string[] candidates)
        {
            foreach (var cand in candidates)
            {
                if (str.Contains(cand, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
