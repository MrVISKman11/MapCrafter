using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using ExileCore;
using Hexa.NET.ImGui;
using MapCrafter.Models;
using MapCrafter.Services;

namespace MapCrafter.UI
{
    public class MapCrafterUi
    {
        private int _selectedProfileIndex = 0;
        private bool _profileIndexInitialized = false;
        private string _newBannedModInput = string.Empty;
        private string _newRequiredModInput = string.Empty;
        private string _newProfileNameInput = "My Custom Profile";
        private int _selectedPresetIndex = 0;
        private int _selectedPresetCategory = 0; // 0 = T16, 1 = T17
        private string _modSearchQuery = string.Empty;
        private int _searchCategoryIdx = 0; // 0 = All, 1 = Generic (T16), 2 = Uber (T17)

        private readonly string[] _presetCategories = { "Current T1-T16 Presets", "Current T17 / Uber Presets" };
        private readonly string[] _searchCategories = { "All", "Generic (T16)", "Uber (T17)" };

        private readonly string[] _methodOptions = { "Chaos Orb", "Scour + Alch" };
        private readonly string[] _vaalOptions = { "Never", "On Craft Success", "All Inventory Maps (Unconditional)" };

        private static string Escape(string text) => text?.Replace("%", "%%") ?? string.Empty;

        public void Render(GameController gc, CraftingEngine engine, List<CraftingProfile> profiles, Settings settings, HashSet<(int x, int y)> ignoredSlots, string pluginDir)
        {
            if (!settings.ShowHud.Value) return;

            // Sync active profile index on start or change
            if (!_profileIndexInitialized && profiles.Count > 0)
            {
                int idx = profiles.FindIndex(p => p.Name.Equals(settings.ActiveProfileName.Value, StringComparison.OrdinalIgnoreCase));
                _selectedProfileIndex = idx >= 0 ? idx : 0;
                _profileIndexInitialized = true;
            }

            var winBounds = gc.Window.GetWindowRectangle();
            float defaultX = Math.Max(1050f, (winBounds.Width - 620f) / 2f);
            ImGui.SetNextWindowPos(new Vector2(defaultX, 60f), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(600, 700), ImGuiCond.FirstUseEver);
            bool isOpen = settings.ShowHud.Value;

            if (ImGui.Begin("MapCrafter Control Panel", ref isOpen))
            {
                settings.ShowHud.Value = isOpen;

                // Status Badge & Controls Header
                RenderHeader(gc, engine, profiles, settings, ignoredSlots);

                ImGui.Separator();

                if (ImGui.BeginTabBar("MapCrafterTabs"))
                {
                    if (ImGui.BeginTabItem("Strategy & Profiles"))
                    {
                        RenderStrategyTab(engine, profiles, settings, pluginDir);
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Rules & Criteria"))
                    {
                        RenderRulesTab(profiles);
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Inventory & Slots"))
                    {
                        RenderInventoryTab(gc, engine, ignoredSlots);
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Live Log"))
                    {
                        RenderLiveLogTab();
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }
            }
            ImGui.End();
        }

        private void RenderHeader(GameController gc, CraftingEngine engine, List<CraftingProfile> profiles, Settings settings, HashSet<(int x, int y)> ignoredSlots)
        {
            var statusColor = engine.IsRunning ? new Vector4(0.2f, 1f, 0.2f, 1f) : new Vector4(1f, 0.4f, 0.4f, 1f);
            ImGui.TextColored(statusColor, engine.IsRunning ? "[RUNNING] Crafting Active" : "[IDLE] Ready");

            ImGui.SameLine();
            ImGui.TextDisabled($"| Status: {engine.StatusMessage}");

            // Persistent Profile Switcher in the Header (visible across all tabs!)
            if (profiles.Count > 0)
            {
                var profileNames = profiles.Select(p => p.Name).ToArray();
                if (_selectedProfileIndex >= profileNames.Length) _selectedProfileIndex = 0;

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.2f, 1f), "Profile:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(320);
                if (ImGui.Combo("##HeaderProfileSelector", ref _selectedProfileIndex, profileNames, profileNames.Length))
                {
                    settings.ActiveProfileName.Value = profileNames[_selectedProfileIndex];
                }
                ImGui.SameLine();
                ImGui.TextDisabled($"({_selectedProfileIndex + 1}/{profiles.Count})");
            }

            ImGui.Spacing();

            if (!engine.IsRunning)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.65f, 0.15f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.8f, 0.2f, 1f));
                if (ImGui.Button("START CRAFTING (F8)", new Vector2(200, 34)))
                {
                    if (profiles.Count > 0 && _selectedProfileIndex < profiles.Count)
                    {
                        var activeProf = profiles[_selectedProfileIndex];
                        engine.Start(gc, activeProf, settings, ignoredSlots);
                    }
                }
                ImGui.PopStyleColor(2);

                ImGui.SameLine();
                if (ImGui.Button("Scan Inventory", new Vector2(140, 34)))
                {
                    engine.Tracker.ScanInventory(gc, ignoredSlots);
                }
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.95f, 0.3f, 0.3f, 1f));
                if (ImGui.Button("STOP CRAFTING (F8)", new Vector2(200, 34)))
                {
                    engine.Stop();
                }
                ImGui.PopStyleColor(2);
            }

            ImGui.SameLine();
            ImGui.TextDisabled($"(Found {engine.Tracker.MapCount} maps in inv)");

            var curSummary = engine.Tracker.GetCurrencySummary();
            ImGui.TextColored(new Vector4(0.35f, 0.75f, 1f, 1f), $"Currencies: {curSummary}");
        }

        private void RenderStrategyTab(CraftingEngine engine, List<CraftingProfile> profiles, Settings settings, string pluginDir)
        {
            if (profiles.Count == 0)
            {
                profiles.AddRange(ProfileManager.LoadProfiles(pluginDir));
                if (profiles.Count == 0)
                {
                    profiles.Add(ProfileManager.CreateTestProfile());
                }
            }

            // Profile Selector Dropdown
            var profileNames = profiles.Select(p => p.Name).ToArray();
            if (_selectedProfileIndex >= profileNames.Length) _selectedProfileIndex = 0;

            ImGui.Text("Active Profile:");
            ImGui.SameLine();
            if (ImGui.Combo("##ProfileSelector", ref _selectedProfileIndex, profileNames, profileNames.Length))
            {
                settings.ActiveProfileName.Value = profileNames[_selectedProfileIndex];
            }

            var current = profiles[_selectedProfileIndex];

            // Profile Creation / Management
            ImGui.Spacing();
            ImGui.InputTextWithHint("##NewProfName", "New profile name...", ref _newProfileNameInput, 80);
            ImGui.SameLine();
            if (ImGui.Button("Create / Duplicate Profile"))
            {
                if (!string.IsNullOrWhiteSpace(_newProfileNameInput))
                {
                    var clone = current.Clone();
                    clone.Name = _newProfileNameInput.Trim();
                    profiles.Add(clone);
                    _selectedProfileIndex = profiles.Count - 1;
                    ProfileManager.SaveProfile(clone, pluginDir);
                }
            }

            ImGui.SameLine();
            if (profiles.Count > 1 && ImGui.Button("Delete Profile"))
            {
                var toDelete = profiles[_selectedProfileIndex];
                ProfileManager.DeleteProfile(toDelete, pluginDir);
                profiles.RemoveAt(_selectedProfileIndex);
                _selectedProfileIndex = Math.Max(0, _selectedProfileIndex - 1);
            }

            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 1f, 1f), "Crafting Strategy & Options");

            // Method
            int methodIdx = (int)current.Method;
            ImGui.Text("Reroll Method:");
            ImGui.SameLine();
            if (ImGui.Combo("##MethodCombo", ref methodIdx, _methodOptions, _methodOptions.Length))
            {
                current.Method = (RerollMethod)methodIdx;
            }

            // Chisels
            bool chiselMaps = current.ChiselMaps;
            if (ImGui.Checkbox("Apply Cartographer's Chisels (if Quality < Target %)", ref chiselMaps))
            {
                current.ChiselMaps = chiselMaps;
            }

            if (current.ChiselMaps)
            {
                int targetQ = current.TargetQuality;
                if (ImGui.SliderInt("Target Quality %", ref targetQ, 1, 20))
                {
                    current.TargetQuality = targetQ;
                }
            }

            ImGui.Spacing();

            // Exalting
            bool exaltSlam = current.ExaltSlam;
            if (ImGui.Checkbox("Exalt Slam Maps (if criteria met & open affix space)", ref exaltSlam))
            {
                current.ExaltSlam = exaltSlam;
            }
            if (current.ExaltSlam)
            {
                int maxEx = current.MaxExaltsToApply;
                if (ImGui.SliderInt("Max Exalts Per Map", ref maxEx, 1, 3))
                {
                    current.MaxExaltsToApply = maxEx;
                }
                ImGui.TextDisabled("Note: If Exalting hits a banned mod, MapCrafter restarts rerolling automatically!");
            }

            ImGui.Spacing();

            // Vaal Orb
            int vaalIdx = (int)current.VaalSetting;
            ImGui.Text("Vaal Orb Mode:");
            ImGui.SameLine();
            if (ImGui.Combo("##VaalCombo", ref vaalIdx, _vaalOptions, _vaalOptions.Length))
            {
                current.VaalSetting = (VaalMode)vaalIdx;
            }

            // Max attempts
            int maxAtt = current.MaxAttemptsPerMap;
            if (ImGui.SliderInt("Max Attempts / Currency Used Per Map", ref maxAtt, 10, 1000))
            {
                current.MaxAttemptsPerMap = maxAtt;
            }

            bool sound = settings.PlaySoundOnCompletion.Value;
            if (ImGui.Checkbox("Play Completion Beep / Sound Alert", ref sound))
            {
                settings.PlaySoundOnCompletion.Value = sound;
            }

            ImGui.Spacing();
            if (ImGui.Button("Save Active Profile to Disk"))
            {
                ProfileManager.SaveProfile(current, pluginDir);
            }
        }

        private void RenderRulesTab(List<CraftingProfile> profiles)
        {
            if (profiles.Count == 0 || _selectedProfileIndex >= profiles.Count) return;
            var current = profiles[_selectedProfileIndex];

            ImGui.TextColored(new Vector4(0.4f, 0.9f, 1f, 1f), "Stat Thresholds");

            int minQ = current.MinQuantity;
            if (ImGui.SliderInt("Min Quantity (Atlas Drops) %", ref minQ, 0, 300))
                current.MinQuantity = minQ;

            int minPs = current.MinPackSize;
            if (ImGui.SliderInt("Min Pack Size %", ref minPs, 0, 100))
                current.MinPackSize = minPs;

            int maxCur = current.MaxCurrencyStat;
            if (ImGui.SliderInt("Max Currency % (e.g. 50 for currency < 50, 0 = disabled)", ref maxCur, 0, 500))
                current.MaxCurrencyStat = maxCur;

            int minCur = current.MinCurrencyStat;
            if (ImGui.SliderInt("Min Currency % (0 = disabled)", ref minCur, 0, 500))
                current.MinCurrencyStat = minCur;

            int minScarabs = current.MinScarabsStat;
            if (ImGui.SliderInt("Min Scarabs / Threads % (0 = disabled)", ref minScarabs, 0, 500))
                current.MinScarabsStat = minScarabs;

            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 1f, 1f), "PoE Regex (poe.re compatible)");
            string reg = current.CustomRegex;
            if (ImGui.InputTextWithHint("##CustomRegexInput", "Paste regex from poe.re here (e.g. \"!gen|ref|avoid\")", ref reg, 250))
            {
                current.CustomRegex = reg;
            }

            ImGui.Separator();
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "Banned / Forbidden Mods (Auto-Reroll if found)");

            // Quick add preset with category selector
            ImGui.Combo("##PresetCategory", ref _selectedPresetCategory, _presetCategories, _presetCategories.Length);
            ImGui.SameLine();

            string[] activePresets = _selectedPresetCategory == 1
                ? ModDatabase.UpToDateT17BannedPresets
                : ModDatabase.UpToDateT16BannedPresets;

            if (_selectedPresetIndex >= activePresets.Length) _selectedPresetIndex = 0;
            ImGui.Combo("##PresetCombo", ref _selectedPresetIndex, activePresets, activePresets.Length);
            ImGui.SameLine();
            if (ImGui.Button("Add Preset"))
            {
                string preset = activePresets[_selectedPresetIndex];
                if (!current.BannedMods.Contains(preset))
                    current.BannedMods.Add(preset);
            }

            // Custom banned mod input
            ImGui.InputTextWithHint("##NewBannedMod", "Enter custom banned mod string / fragment...", ref _newBannedModInput, 150);
            ImGui.SameLine();
            if (ImGui.Button("Add Custom Banned Mod"))
            {
                if (!string.IsNullOrWhiteSpace(_newBannedModInput))
                {
                    current.BannedMods.Add(_newBannedModInput.Trim());
                    _newBannedModInput = string.Empty;
                }
            }

            // Render list of banned mods with safe EndChild
            if (ImGui.BeginChild("BannedModsList", new Vector2(0, 130), ImGuiChildFlags.Borders))
            {
                for (int i = 0; i < current.BannedMods.Count; i++)
                {
                    ImGui.PushID($"BannedMod_{i}");
                    bool removed = false;
                    try
                    {
                        if (ImGui.Button("X", new Vector2(24, 20)))
                        {
                            current.BannedMods.RemoveAt(i);
                            removed = true;
                        }
                        else
                        {
                            ImGui.SameLine();
                            ImGui.TextUnformatted(current.BannedMods[i]);
                        }
                    }
                    finally
                    {
                        ImGui.PopID();
                    }
                    if (removed) break;
                }
            }
            ImGui.EndChild(); // ALWAYS called outside if(BeginChild)

            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "Required / Desired Mods (Map must contain these)");

            bool reqAll = current.RequireAllRequiredMods;
            if (ImGui.Checkbox("Require ALL required mods (uncheck to require at least ONE)", ref reqAll))
            {
                current.RequireAllRequiredMods = reqAll;
            }

            ImGui.InputTextWithHint("##NewReqMod", "Enter required mod string / fragment...", ref _newRequiredModInput, 150);
            ImGui.SameLine();
            if (ImGui.Button("Add Required Mod"))
            {
                if (!string.IsNullOrWhiteSpace(_newRequiredModInput))
                {
                    current.RequiredMods.Add(_newRequiredModInput.Trim());
                    _newRequiredModInput = string.Empty;
                }
            }

            // Render list of required mods with safe EndChild
            if (ImGui.BeginChild("ReqModsList", new Vector2(0, 90), ImGuiChildFlags.Borders))
            {
                for (int i = 0; i < current.RequiredMods.Count; i++)
                {
                    ImGui.PushID($"ReqMod_{i}");
                    bool removed = false;
                    try
                    {
                        if (ImGui.Button("X", new Vector2(24, 20)))
                        {
                            current.RequiredMods.RemoveAt(i);
                            removed = true;
                        }
                        else
                        {
                            ImGui.SameLine();
                            ImGui.TextUnformatted(current.RequiredMods[i]);
                        }
                    }
                    finally
                    {
                        ImGui.PopID();
                    }
                    if (removed) break;
                }
            }
            ImGui.EndChild(); // ALWAYS called outside if(BeginChild)

            ImGui.Separator();
            ImGui.TextColored(new Vector4(1f, 0.9f, 0.3f, 1f), $"In-Game Mod Browser ({ModDatabase.AllMods.Count} Live 3.30 Mods from poe.re & poedb)");
            ImGui.TextDisabled("Search by keyword (e.g. 'mirage', 'wildwood', 'beyond', 'reflect', 'regen', 'flask')");

            ImGui.SetNextItemWidth(120);
            ImGui.Combo("##SearchCatCombo", ref _searchCategoryIdx, _searchCategories, _searchCategories.Length);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(300);
            ImGui.InputTextWithHint("##ModSearchQuery", "Search all active map mods...", ref _modSearchQuery, 100);

            string searchCat = _searchCategories[_searchCategoryIdx];
            var searchResults = ModDatabase.Search(_modSearchQuery, searchCat);

            // Safe child window for Mod Browser
            if (ImGui.BeginChild("ModBrowserChild", new Vector2(0, 140), ImGuiChildFlags.Borders))
            {
                for (int i = 0; i < searchResults.Count; i++)
                {
                    var mod = searchResults[i];
                    ImGui.PushID($"DbMod_{i}");
                    try
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.2f, 0.2f, 1f));
                        if (ImGui.SmallButton("+ Ban"))
                        {
                            if (!current.BannedMods.Contains(mod.Name))
                                current.BannedMods.Add(mod.Name);
                        }
                        ImGui.PopStyleColor(1);

                        ImGui.SameLine();
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 1f));
                        if (ImGui.SmallButton("+ Req"))
                        {
                            if (!current.RequiredMods.Contains(mod.Name))
                                current.RequiredMods.Add(mod.Name);
                        }
                        ImGui.PopStyleColor(1);

                        ImGui.SameLine();
                        var catColor = mod.Category == "Uber" ? new Vector4(1f, 0.5f, 0.2f, 1f) : new Vector4(0.8f, 0.8f, 0.8f, 1f);
                        ImGui.TextColored(catColor, $"[{mod.Category}] ");

                        if (!string.IsNullOrEmpty(mod.Regex))
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), $"[{mod.Regex}] ");
                        }

                        ImGui.SameLine();
                        ImGui.TextUnformatted(mod.Name);

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            if (!string.IsNullOrEmpty(mod.Regex))
                                ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), $"poe.re Regex: \"{mod.Regex}\"");
                            if (mod.Descriptions.Count > 0)
                            {
                                ImGui.TextUnformatted("Descriptions / Stats:");
                                foreach (var d in mod.Descriptions)
                                    ImGui.TextDisabled($" * {Escape(d)}");
                            }
                            ImGui.EndTooltip();
                        }
                    }
                    finally
                    {
                        ImGui.PopID();
                    }
                }
            }
            ImGui.EndChild(); // ALWAYS called outside if(BeginChild)
        }

        private void RenderInventoryTab(GameController gc, CraftingEngine engine, HashSet<(int x, int y)> ignoredSlots)
        {
            ImGui.Text("Player Inventory Slots (12 x 5 Grid)");
            ImGui.TextDisabled("Click any slot to toggle IGNORED (Purple). Crafter will skip ignored slots.");

            // Legend
            ImGui.Text("Legend: ");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.3f, 0.6f, 1f, 1f), "[Currency]");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.7f, 0.2f, 1f), "[Pending Map]");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 1f, 0.2f, 1f), "[Crafting]");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.2f, 1f, 0.2f, 1f), "[Success]");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "[Failed]");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.8f, 0.3f, 0.8f, 1f), "[Ignored]");

            ImGui.Spacing();

            var maps = engine.Tracker.Maps;
            var currencies = engine.Tracker.Currencies;

            var mapsBySlot = new Dictionary<(int x, int y), ParsedMapItem>();
            foreach (var m in maps)
            {
                mapsBySlot[(m.SlotX, m.SlotY)] = m;
            }

            var currencyBySlot = new Dictionary<(int x, int y), InventoryCurrencyItem>();
            foreach (var kvp in currencies)
            {
                foreach (var cur in kvp.Value)
                {
                    if (cur.IsInStash) continue; // Only player inventory currencies belong in the inventory grid!
                    currencyBySlot[(cur.SlotX, cur.SlotY)] = cur;
                }
            }

            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 12; x++)
                {
                    if (x > 0) ImGui.SameLine();

                    bool isIgnored = ignoredSlots.Contains((x, y));
                    Vector4 btnColor = new Vector4(0.25f, 0.25f, 0.25f, 1f);
                    string label = $"{x},{y}";

                    if (isIgnored)
                    {
                        btnColor = new Vector4(0.55f, 0.2f, 0.55f, 1f);
                    }
                    else if (currencyBySlot.TryGetValue((x, y), out var curItem))
                    {
                        btnColor = new Vector4(0.2f, 0.45f, 0.8f, 1f);
                        label = curItem.Kind.ToString().Replace("Chisel", "Ch").Replace("Alchemy", "Alch");
                    }
                    else if (mapsBySlot.TryGetValue((x, y), out var mapItem))
                    {
                        btnColor = mapItem.Status switch
                        {
                            CraftStatus.Chiseling or CraftStatus.UpgradingRarity or CraftStatus.Rerolling or CraftStatus.Exalting => new Vector4(0.9f, 0.85f, 0.2f, 1f),
                            CraftStatus.Succeeded or CraftStatus.VaalPassed => new Vector4(0.2f, 0.8f, 0.2f, 1f),
                            CraftStatus.Failed or CraftStatus.FailedMaxAttempts or CraftStatus.OutOfCurrency => new Vector4(0.85f, 0.2f, 0.2f, 1f),
                            CraftStatus.Skipped => new Vector4(0.4f, 0.4f, 0.4f, 1f),
                            _ => new Vector4(0.9f, 0.55f, 0.15f, 1f)
                        };
                        label = $"T{mapItem.Tier}";
                    }

                    ImGui.PushID($"Slot_{x}_{y}");
                    ImGui.PushStyleColor(ImGuiCol.Button, btnColor);
                    if (ImGui.Button(label, new Vector2(38, 30)))
                    {
                        if (ignoredSlots.Contains((x, y)))
                            ignoredSlots.Remove((x, y));
                        else
                            ignoredSlots.Add((x, y));
                    }
                    ImGui.PopStyleColor(1);

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"Slot ({x}, {y})");
                        if (isIgnored)
                            ImGui.TextColored(new Vector4(0.8f, 0.3f, 0.8f, 1f), "IGNORED (Click to unignore)");
                        else if (currencyBySlot.TryGetValue((x, y), out var c))
                            ImGui.Text($"Currency: {c.BaseName} (x{c.StackCount})");
                        else if (mapsBySlot.TryGetValue((x, y), out var m))
                        {
                            ImGui.Text($"Map: {m.BaseName} [Tier {m.Tier}]");
                            ImGui.Text($"Status: {m.Status} - {m.StatusDetails}");
                            ImGui.Text($"Quality: {m.Quality}% | Quant: {m.Quantity}% | Pack Size: {m.PackSize}%");
                            if (m.CurrencyBonus > 0) ImGui.Text($"Currency Bonus: +{m.CurrencyBonus}%");
                            if (m.ScarabsBonus > 0) ImGui.Text($"Scarabs Bonus: +{m.ScarabsBonus}%");
                            ImGui.Separator();
                            foreach (var line in m.ModLines)
                                ImGui.TextDisabled(Escape(line));
                        }
                        else
                            ImGui.TextDisabled("Empty or non-craftable item");
                        ImGui.EndTooltip();
                    }

                    ImGui.PopID();
                }
            }
        }

        private bool _autoScrollLog = true;
        private string _logFilter = string.Empty;

        private void RenderLiveLogTab()
        {
            if (ImGui.Button("Clear Log"))
            {
                CraftLogger.Clear();
            }

            ImGui.SameLine();
            if (ImGui.Button("Copy to Clipboard"))
            {
                var entries = CraftLogger.GetEntriesSnapshot();
                var text = string.Join(Environment.NewLine, entries.Select(e => e.Formatted));
                try { Clipboard.SetText(text); } catch { }
            }

            ImGui.SameLine();
            ImGui.Checkbox("Auto-scroll", ref _autoScrollLog);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(250);
            ImGui.InputTextWithHint("##LogFilter", "Filter log...", ref _logFilter, 50);

            ImGui.Separator();

            if (ImGui.BeginChild("LogOutputChild", new Vector2(0, 0), ImGuiChildFlags.Borders))
            {
                var entries = CraftLogger.GetEntriesSnapshot();
                foreach (var entry in entries)
                {
                    if (!string.IsNullOrWhiteSpace(_logFilter) &&
                        !entry.Formatted.Contains(_logFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var color = entry.Level switch
                    {
                        LogLevel.Error => new Vector4(1f, 0.3f, 0.3f, 1f),
                        LogLevel.Warning => new Vector4(1f, 0.85f, 0.2f, 1f),
                        LogLevel.Success => new Vector4(0.2f, 1f, 0.2f, 1f),
                        LogLevel.Step => new Vector4(0.3f, 0.8f, 1f, 1f),
                        _ => new Vector4(0.85f, 0.85f, 0.85f, 1f)
                    };

                    ImGui.TextColored(color, entry.Formatted);
                }

                if (_autoScrollLog && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 20)
                {
                    ImGui.SetScrollHereY(1.0f);
                }
            }
            ImGui.EndChild();
        }
    }
}
