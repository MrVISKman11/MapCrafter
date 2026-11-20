using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MapCrafter.Models;

namespace MapCrafter.Services
{
    public class EvaluationResult
    {
        public bool Passed { get; set; }
        public string Reason { get; set; } = string.Empty;

        public static EvaluationResult Success() => new() { Passed = true, Reason = "Passed all criteria" };
        public static EvaluationResult Fail(string reason) => new() { Passed = false, Reason = reason };
    }

    public class CompositeEvaluationResult
    {
        public bool Passed { get; set; }
        public CraftingProfile? MatchedProfile { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool IsBuildBricked { get; set; }

        public static CompositeEvaluationResult Success(CraftingProfile matched) =>
            new() { Passed = true, MatchedProfile = matched, Reason = $"Matched profile '{matched.Name}'" };

        public static CompositeEvaluationResult BuildBricked(string mod) =>
            new() { Passed = false, IsBuildBricked = true, Reason = $"Contains build-bricking mod: '{mod}'" };

        public static CompositeEvaluationResult Fail(string reason) =>
            new() { Passed = false, Reason = reason };
    }

    public static class MapEvaluator
    {
        public static string? CheckGlobalBannedMods(ParsedMapItem map, IReadOnlyList<string>? globalBannedMods)
        {
            if (map == null || globalBannedMods == null) return null;

            foreach (var banned in globalBannedMods)
            {
                if (string.IsNullOrWhiteSpace(banned)) continue;
                if (MatchesModRule(map, banned.Trim()))
                {
                    return banned.Trim();
                }
            }

            return null;
        }

        public static CompositeEvaluationResult EvaluateComposite(
            ParsedMapItem map,
            IReadOnlyList<string>? globalBannedMods,
            IReadOnlyList<CraftingProfile> activeProfiles)
        {
            if (map == null)
                return CompositeEvaluationResult.Fail("No map item");

            // Pass 1: Global Build Safety Check (Hard Fail)
            var bricker = CheckGlobalBannedMods(map, globalBannedMods);
            if (bricker != null)
            {
                return CompositeEvaluationResult.BuildBricked(bricker);
            }

            if (activeProfiles == null || activeProfiles.Count == 0)
                return CompositeEvaluationResult.Fail("No active crafting profiles selected");

            // Pass 2: Disjunctive Target Profile Matching (Match ANY)
            var failureReasons = new List<string>();
            foreach (var profile in activeProfiles)
            {
                var eval = Evaluate(map, profile);
                if (eval.Passed)
                {
                    return CompositeEvaluationResult.Success(profile);
                }
                failureReasons.Add($"{profile.Name}: {eval.Reason}");
            }

            return CompositeEvaluationResult.Fail(string.Join(" | ", failureReasons));
        }
        public static bool HasAnyCriteria(CraftingProfile profile)
        {
            if (profile == null) return false;

            if (profile.RequiredMods != null && profile.RequiredMods.Any(r => !string.IsNullOrWhiteSpace(r)))
                return true;

            if (!string.IsNullOrWhiteSpace(profile.CustomRegex))
                return true;

            if (profile.MinAtlasDrops > 0 ||
                profile.MinItemRarity > 0 ||
                profile.MinPackSize > 0 ||
                profile.MinMoreMaps > 0 ||
                profile.MinMoreCurrency > 0 ||
                profile.MinMoreThreads > 0 ||
                profile.MinItemQuantity > 0)
            {
                return true;
            }

            return false;
        }

        public static EvaluationResult Evaluate(ParsedMapItem map, CraftingProfile profile)
        {
            if (map == null)
                return EvaluationResult.Fail("No map item");

            // Guardrail: A profile with no active criteria (all 0 min stats, no required mods, no regex) must never match
            if (!HasAnyCriteria(profile))
                return EvaluationResult.Fail($"Profile '{profile.Name}' has no criteria defined (configure minimum stats or required mods)");

            // 1. Check Banned Mods
            foreach (var banned in profile.BannedMods)
            {
                if (string.IsNullOrWhiteSpace(banned)) continue;

                if (MatchesModRule(map, banned.Trim()))
                {
                    return EvaluationResult.Fail($"Contains banned mod: '{banned.Trim()}'");
                }
            }

            // 2. Check Required Mods
            if (profile.RequiredMods != null && profile.RequiredMods.Count > 0)
            {
                if (profile.RequireAllRequiredMods)
                {
                    foreach (var req in profile.RequiredMods)
                    {
                        if (string.IsNullOrWhiteSpace(req)) continue;
                        if (!MatchesModRule(map, req.Trim()))
                            return EvaluationResult.Fail($"Missing required mod: '{req.Trim()}'");
                    }
                }
                else
                {
                    bool foundAny = false;
                    foreach (var req in profile.RequiredMods)
                    {
                        if (string.IsNullOrWhiteSpace(req)) continue;
                        if (MatchesModRule(map, req.Trim()))
                        {
                            foundAny = true;
                            break;
                        }
                    }

                    if (!foundAny)
                        return EvaluationResult.Fail("Missing any of the required mods");
                }
            }

            // 3. Minimum Stat Threshold Filters (0 = disabled / any)
            if (profile.MinAtlasDrops > 0 && map.AtlasDrops < profile.MinAtlasDrops)
                return EvaluationResult.Fail($"Atlas Drops {map.AtlasDrops}% < min {profile.MinAtlasDrops}%");

            if (profile.MinPackSize > 0 && map.PackSize < profile.MinPackSize)
                return EvaluationResult.Fail($"Monster Pack Size {map.PackSize}% < min {profile.MinPackSize}%");

            if (profile.MinItemRarity > 0 && map.ItemRarityBonus < profile.MinItemRarity)
                return EvaluationResult.Fail($"Item Rarity {map.ItemRarityBonus}% < min {profile.MinItemRarity}%");

            if (profile.MinMoreMaps > 0 && map.MoreMapsBonus < profile.MinMoreMaps)
                return EvaluationResult.Fail($"More Maps {map.MoreMapsBonus}% < min {profile.MinMoreMaps}%");

            if (profile.MinMoreCurrency > 0 && map.CurrencyBonus < profile.MinMoreCurrency)
                return EvaluationResult.Fail($"More Currency {map.CurrencyBonus}% < min {profile.MinMoreCurrency}%");

            if (profile.MinMoreThreads > 0 && map.ScarabsBonus < profile.MinMoreThreads)
                return EvaluationResult.Fail($"More Threads {map.ScarabsBonus}% < min {profile.MinMoreThreads}%");

            if (profile.MinItemQuantity > 0 && map.ItemQuantity < profile.MinItemQuantity)
                return EvaluationResult.Fail($"Item Quantity {map.ItemQuantity}% < min {profile.MinItemQuantity}%");

            // 4. Custom Regex check (poe.re style)
            if (!string.IsNullOrWhiteSpace(profile.CustomRegex))
            {
                var regexCheck = EvaluatePoeRegex(map.FormattedTooltipText, profile.CustomRegex);
                if (!regexCheck.Passed)
                    return regexCheck;
            }

            return EvaluationResult.Success();
        }

        public static bool MatchesModRule(ParsedMapItem map, string rulePattern)
        {
            if (string.IsNullOrWhiteSpace(rulePattern)) return false;

            string trimmedRule = rulePattern.Trim();
            string cleanRule = CleanModPattern(trimmedRule);

            // 1. Check formatted ModLines directly with markup stripped (e.g. [EvocatorKTash|Evocator of K'Tash] -> Evocator of K'Tash)
            foreach (var line in map.ModLines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string cleanLine = CleanModLine(line);

                // Substring checks
                if (cleanLine.Contains(cleanRule, StringComparison.OrdinalIgnoreCase) ||
                    cleanLine.Contains(trimmedRule, StringComparison.OrdinalIgnoreCase) ||
                    line.Contains(trimmedRule, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // 2. Check full formatted tooltip text
            if (!string.IsNullOrEmpty(map.FormattedTooltipText))
            {
                string cleanTooltip = CleanModLine(map.FormattedTooltipText);
                if (cleanTooltip.Contains(cleanRule, StringComparison.OrdinalIgnoreCase) ||
                    cleanTooltip.Contains(trimmedRule, StringComparison.OrdinalIgnoreCase) ||
                    map.FormattedTooltipText.Contains(trimmedRule, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // 3. Regex matching on formatted lines (supporting '#' and '%' wildcards)
            try
            {
                string regexPattern = Regex.Escape(trimmedRule);
                regexPattern = regexPattern.Replace(@"\#", @"\d+");
                regexPattern = Regex.Replace(regexPattern, @"(?<!\\d\+)\\%", @"(?:\d+)?%");
                regexPattern = Regex.Replace(regexPattern, @"\\\s+", @"\s+");

                var rx = new Regex(regexPattern, RegexOptions.IgnoreCase);
                foreach (var line in map.ModLines)
                {
                    string cleanLine = CleanModLine(line);
                    if (rx.IsMatch(line) || rx.IsMatch(cleanLine))
                        return true;
                }

                if (!string.IsNullOrEmpty(map.FormattedTooltipText) && rx.IsMatch(CleanModLine(map.FormattedTooltipText)))
                    return true;
            }
            catch { }

            // 4. Normalized line match (strips punctuation/spaces for slightly modified wordings)
            var normRule = ItemParser.NormalizeModString(cleanRule);
            if (!string.IsNullOrEmpty(normRule) && normRule.Length >= 4)
            {
                foreach (var line in map.ModLines)
                {
                    var normLine = ItemParser.NormalizeModString(CleanModLine(line));
                    if (normLine.Contains(normRule, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            // 5. Internal RawModNames: ONLY if the internal name CONTAINS the rule (NEVER normRule.Contains(normRaw)!)
            if (!string.IsNullOrEmpty(normRule) && normRule.Length >= 6)
            {
                foreach (var raw in map.RawModNames)
                {
                    var normRaw = ItemParser.NormalizeModString(raw);
                    if (normRaw.Contains(normRule, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        public static string CleanModLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;
            // Replace [tag|Display Text] with Display Text, and [Text] with Text
            string stripped = Regex.Replace(line, @"\[(?:[^\|\]]+\|)?([^\]]+)\]", "$1");
            return Regex.Replace(stripped, @"\s+", " ").Trim();
        }

        public static string CleanModPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return string.Empty;
            string cleaned = pattern.Replace("#", "").Replace("%", "").Trim();
            return Regex.Replace(cleaned, @"\s+", " ");
        }

        public static EvaluationResult EvaluatePoeRegex(string itemText, string regexInput)
        {
            if (string.IsNullOrWhiteSpace(regexInput))
                return EvaluationResult.Success();

            // Extract terms: quoted strings or space-separated tokens
            var matches = Regex.Matches(regexInput, @"(?<q>""[^""]+"")|(?<w>[^\s""]+)");
            foreach (Match m in matches)
            {
                var term = m.Value.Trim('"', ' ');
                if (string.IsNullOrWhiteSpace(term)) continue;

                bool isNegated = term.StartsWith('!');
                string pat = isNegated ? term[1..] : term;

                try
                {
                    bool isMatch = Regex.IsMatch(itemText, pat, RegexOptions.IgnoreCase);
                    if (isNegated && isMatch)
                    {
                        return EvaluationResult.Fail($"Failed negated regex: '{term}'");
                    }
                    if (!isNegated && !isMatch)
                    {
                        return EvaluationResult.Fail($"Failed positive regex: '{term}'");
                    }
                }
                catch (Exception ex)
                {
                    return EvaluationResult.Fail($"Invalid regex '{pat}': {ex.Message}");
                }
            }

            return EvaluationResult.Success();
        }
    }
}
