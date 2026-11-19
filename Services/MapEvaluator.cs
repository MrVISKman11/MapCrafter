using System;
using System.Collections.Generic;
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

    public static class MapEvaluator
    {
        public static EvaluationResult Evaluate(ParsedMapItem map, CraftingProfile profile)
        {
            if (map == null)
                return EvaluationResult.Fail("No map item");

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

            // 3. Stat Filters
            if (profile.MinQuantity > 0 && map.Quantity < profile.MinQuantity)
                return EvaluationResult.Fail($"Quantity {map.Quantity}% < {profile.MinQuantity}%");

            if (profile.MinPackSize > 0 && map.PackSize < profile.MinPackSize)
                return EvaluationResult.Fail($"Pack Size {map.PackSize}% < {profile.MinPackSize}%");

            // Currency stat limits (e.g. currency < 50)
            if (profile.MaxCurrencyStat > 0 && map.CurrencyBonus >= profile.MaxCurrencyStat)
                return EvaluationResult.Fail($"Currency bonus {map.CurrencyBonus}% >= max {profile.MaxCurrencyStat}%");

            if (profile.MinCurrencyStat > 0 && map.CurrencyBonus < profile.MinCurrencyStat)
                return EvaluationResult.Fail($"Currency bonus {map.CurrencyBonus}% < min {profile.MinCurrencyStat}%");

            if (profile.MinScarabsStat > 0 && map.ScarabsBonus < profile.MinScarabsStat)
                return EvaluationResult.Fail($"Scarabs bonus {map.ScarabsBonus}% < min {profile.MinScarabsStat}%");

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
