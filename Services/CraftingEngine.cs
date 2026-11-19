using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;
using MapCrafter.Models;

namespace MapCrafter.Services
{
    public class CraftingEngine
    {
        private readonly InventoryTracker _tracker = new();
        private static readonly Random _rng = new();
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public InventoryTracker Tracker => _tracker;
        public bool IsRunning => _isRunning;
        public DateTime StartTime { get; private set; } = DateTime.MinValue;
        public string StatusMessage { get; private set; } = "Idle";
        public ParsedMapItem? CurrentMap { get; private set; }

        public void Start(GameController gc, CraftingProfile profile, Settings settings, HashSet<(int x, int y)> ignoredSlots)
        {
            if (_isRunning) return;

            StartTime = DateTime.UtcNow;
            CraftLogger.Step("ENGINE", $"Starting crafting session with profile '{profile.Name}'");
            _cts = new CancellationTokenSource();
            _isRunning = true;
            settings.IsRunning.Value = true;

            Task.Run(async () =>
            {
                try
                {
                    await RunCraftingSession(gc, profile, settings, _cts.Token, ignoredSlots);
                }
                catch (OperationCanceledException)
                {
                    CraftLogger.Warn("ENGINE", "Crafting session cancelled by user.");
                    StatusMessage = "Crafting stopped by user";
                }
                catch (Exception ex)
                {
                    CraftLogger.Error("ENGINE", "Unhandled exception in crafting session", ex);
                    StatusMessage = $"Error: {ex.Message}";
                }
                finally
                {
                    CrafterInput.ReleaseAll();
                    _isRunning = false;
                    settings.IsRunning.Value = false;
                    CurrentMap = null;
                    CraftLogger.Info("ENGINE", "Crafting session stopped.");
                }
            });
        }

        public void Stop()
        {
            if (!_isRunning) return;
            CraftLogger.Warn("ENGINE", "User requested Stop.");
            _cts?.Cancel();
            StatusMessage = "Stopping...";
        }

        private async Task RunCraftingSession(GameController gc, CraftingProfile profile, Settings settings, CancellationToken ct, HashSet<(int x, int y)>? ignoredSlots)
        {
            CrafterInput.WindowRect = gc.Window.GetWindowRectangle();
            CraftLogger.Info("WINDOW", $"PoE Window bounds: X={CrafterInput.WindowRect.X}, Y={CrafterInput.WindowRect.Y}, W={CrafterInput.WindowRect.Width}, H={CrafterInput.WindowRect.Height}");

            // 1. Ensure inventory is open (either standalone or alongside open stash)
            var invPanel = gc.IngameState?.IngameUi?.InventoryPanel;
            var stashEl = gc.IngameState?.IngameUi?.StashElement;
            bool isStashOpen = stashEl != null && stashEl.IsVisible;

            CraftLogger.Info("UI", $"Initial UI state: StashOpen={isStashOpen}, InventoryOpen={invPanel?.IsVisible == true}");

            if (!isStashOpen && (invPanel == null || !invPanel.IsVisible))
            {
                CraftLogger.Step("UI", "Inventory is not visible. Pressing 'I' to open...");
                StatusMessage = "Opening inventory panel...";
                Input.KeyDown(Keys.I);
                await Task.Delay(35, ct);
                Input.KeyUp(Keys.I);
                await Task.Delay(250, ct);
            }

            invPanel = gc.IngameState?.IngameUi?.InventoryPanel;
            if (invPanel == null || !invPanel.IsVisible)
            {
                CraftLogger.Error("UI", "Failed to detect open inventory panel! Aborting.");
                StatusMessage = "Inventory panel is not visible! Please open inventory or stash.";
                return;
            }

            CrafterInput.ReleaseAll();
            _tracker.ResetLockedCurrencies();

            // 2. Scan inventory & currencies
            CraftLogger.Step("SCAN", "Scanning maps and currencies...");
            StatusMessage = "Scanning inventory for maps and currencies...";
            _tracker.ScanInventory(gc, ignoredSlots);

            var mapsSnapshot = _tracker.Maps;
            CraftLogger.Info("SCAN", $"Found {mapsSnapshot.Count} map(s) in player inventory.");
            CraftLogger.Info("SCAN", $"Currencies detected: {_tracker.GetCurrencySummary()}");

            for (int i = 0; i < mapsSnapshot.Count; i++)
            {
                var m = mapsSnapshot[i];
                CraftLogger.Info("SCAN", $"  Map #{i + 1}: ({m.SlotX},{m.SlotY}) {m.BaseName} T{m.Tier} Q:{m.Quality}% Quant:{m.ItemQuantity}% Atlas:{m.AtlasDrops}% Rarity:+{m.ItemRarityBonus}% PS:{m.PackSize}% Maps:+{m.MoreMapsBonus}% Cur:+{m.CurrencyBonus}% Threads:+{m.ScarabsBonus}%");
            }

            if (mapsSnapshot.Count == 0)
            {
                CraftLogger.Warn("SCAN", "No maps found in inventory! Stopping.");
                StatusMessage = "No maps found in inventory!";
                return;
            }

            int delay = settings.ActionDelayMs.Value;
            CraftLogger.Info("CONFIG", $"Action delay: {delay}ms | MaxAttempts: {profile.MaxAttemptsPerMap} | ChiselTarget: {profile.TargetQuality}% (Enabled:{profile.ChiselMaps})");

            // Special Mode: Unconditional Vaal all maps in inventory
            if (profile.VaalSetting == VaalMode.AllInventoryMaps)
            {
                CraftLogger.Step("VAAL_ALL", "Running 'Vaal All Maps in Inventory' mode...");
                StatusMessage = "Applying Vaal Orbs to all maps in inventory...";
                for (int i = 0; i < mapsSnapshot.Count; i++)
                {
                    var map = mapsSnapshot[i];
                    if (ct.IsCancellationRequested) break;
                    if (map.IsCorrupted || map.IsMirrored)
                    {
                        CraftLogger.Info("VAAL_ALL", $"Skipping map #{i + 1} at ({map.SlotX},{map.SlotY}): Already Corrupted/Mirrored.");
                        map.Status = CraftStatus.Skipped;
                        continue;
                    }

                    var vaal = await _tracker.GetAvailableCurrencyWithRetry(gc, CurrencyKind.Vaal, ignoredSlots, maxRetries: 3);
                    if (vaal == null)
                    {
                        CraftLogger.Error("VAAL_ALL", "Out of Vaal Orbs! Halting.");
                        StatusMessage = "Out of Vaal Orbs!";
                        map.Status = CraftStatus.OutOfCurrency;
                        return;
                    }

                    CurrentMap = map;
                    map.Status = CraftStatus.VaalPassed;
                    CraftLogger.Step("VAAL_ALL", $"Vaal slamming map #{i + 1} from {vaal.LocationDescription}...");
                    await ApplyCurrency(gc, vaal.ClickPosition, map.ClickPosition, delay, ct);
                    await Task.Delay(delay, ct);
                    map.Status = CraftStatus.Succeeded;
                }

                StatusMessage = "All eligible maps corrupted!";
                CraftLogger.Success("VAAL_ALL", "All eligible maps corrupted successfully.");
                return;
            }

            // Standard Crafting Loop
            int craftedCount = 0;
            for (int i = 0; i < mapsSnapshot.Count; i++)
            {
                var map = mapsSnapshot[i];
                if (ct.IsCancellationRequested) break;

                // Skip corrupted, mirrored, or unique maps
                if (map.IsCorrupted || map.IsMirrored || map.Rarity == ItemRarity.Unique)
                {
                    CraftLogger.Info("SESSION", $"Skipping map #{i + 1} at ({map.SlotX},{map.SlotY}): Corrupted={map.IsCorrupted}, Mirrored={map.IsMirrored}, Rarity={map.Rarity}");
                    map.Status = CraftStatus.Skipped;
                    map.StatusDetails = "Skipped (Unique / Corrupted / Mirrored)";
                    continue;
                }

                craftedCount++;
                CurrentMap = map;
                map.Status = CraftStatus.Rerolling;
                CraftLogger.Step("SESSION", $"==================================================");
                CraftLogger.Step("SESSION", $"STARTING MAP #{craftedCount}/{mapsSnapshot.Count} at ({map.SlotX},{map.SlotY}) [{map.BaseName} T{map.Tier}]");
                CraftLogger.Step("SESSION", $"Initial Stats: Quality={map.Quality}%, ItemQuant={map.ItemQuantity}%, AtlasDrops={map.AtlasDrops}%, Rarity=+{map.ItemRarityBonus}%, PackSize={map.PackSize}%" + (map.CurrencyBonus > 0 ? $", Cur=+{map.CurrencyBonus}%" : "") + (map.ScarabsBonus > 0 ? $", Threads=+{map.ScarabsBonus}%" : "") + (map.MoreMapsBonus > 0 ? $", Maps=+{map.MoreMapsBonus}%" : ""));

                bool mapFinished = await ProcessSingleMap(gc, map, profile, settings, delay, ct, ignoredSlots);
                CraftLogger.Info("SESSION", $"FINISHED MAP #{craftedCount}: Success={mapFinished}, FinalStatus={map.Status}, Details='{map.StatusDetails}'");
                CraftLogger.Step("SESSION", $"==================================================");

                if (!mapFinished && ct.IsCancellationRequested)
                    break;
            }

            // Move cursor to neutral center between stash and inventory so it's not resting on any map
            try
            {
                var winRect = gc.Window.GetWindowRectangle();
                var neutralCenter = new Vector2(winRect.X + (winRect.Width * 0.5f), winRect.Y + (winRect.Height * 0.45f));
                await CrafterInput.MoveCursorTo(neutralCenter);
                await Task.Delay(80, ct);
                Input.RightDown();
                await Task.Delay(40, ct);
                Input.RightUp();
                await Task.Delay(60, ct);
            }
            catch { }

            CrafterInput.ReleaseAll();

            StatusMessage = $"Finished crafting session! Processed {craftedCount} map(s).";
            CraftLogger.Success("SESSION", $"Crafting session complete. Processed {craftedCount} map(s).");
            PlayCompletionBeep(gc, settings);
        }

        private static void PlayCompletionBeep(GameController gc, Settings settings)
        {
            if (!settings.PlaySoundOnCompletion.Value) return;

            try
            {
                gc.SoundController?.PlaySound("alert");
            }
            catch { }

            try
            {
                Task.Run(() =>
                {
                    try
                    {
                        Console.Beep(880, 150);
                        System.Threading.Thread.Sleep(50);
                        Console.Beep(1320, 300);
                    }
                    catch
                    {
                        System.Media.SystemSounds.Beep.Play();
                    }
                });
            }
            catch { }
        }

        private async Task<bool> ProcessSingleMap(GameController gc, ParsedMapItem map, CraftingProfile profile, Settings settings, int delay, CancellationToken ct, HashSet<(int x, int y)>? ignoredSlots)
        {
            // Stage 1: Chisels (Continuous repeat-apply with Ctrl)
            if (profile.ChiselMaps && map.Quality < profile.TargetQuality && !map.IsCorrupted)
            {
                map.Status = CraftStatus.Chiseling;
                CraftLogger.Step("CHISEL", $"Map quality {map.Quality}% < target {profile.TargetQuality}%. Starting chiseling stage...");

                var chisel = await _tracker.GetAvailableCurrencyWithRetry(gc, CurrencyKind.ChiselNormal, ignoredSlots, maxRetries: 3);
                if (chisel == null)
                {
                    CraftLogger.Warn("CHISEL", $"Out of Cartographer's Chisels! Found 0 available chisels. Continuing with current quality ({map.Quality}%).");
                    map.StatusDetails = "Out of Chisels (continuing)";
                }
                else
                {
                    StatusMessage = $"Chiseling map ({map.Quality}% -> {profile.TargetQuality}%)...";
                    CraftLogger.Step("CHISEL", $"Selecting Cartographer's Chisel from {chisel.LocationDescription} (Stack: {chisel.StackCount})");

                    // Move to Chisel in Stash and Right-Click to pick it up with generous human settle
                    await CrafterInput.MoveCursorTo(chisel.ClickPosition);
                    await Task.Delay(Math.Max(150, (delay / 2) + _rng.Next(30)), ct);
                    Input.RightDown();
                    await Task.Delay(50 + _rng.Next(20), ct);
                    Input.RightUp();
                    await Task.Delay(Math.Max(150, (delay / 2) + _rng.Next(30)), ct);

                    // Hold Shift (native PoE repeat-apply key)
                    CrafterInput.SetShift(true);
                    await Task.Delay(80 + _rng.Next(25), ct);

                    // Move cursor over target map in player inventory
                    await CrafterInput.MoveCursorTo(map.ClickPosition);
                    await Task.Delay(Math.Max(150, (delay / 2) + _rng.Next(30)), ct);

                    int qualityAttempts = 0;
                    int maxQualityAttempts = Math.Max(8, (profile.TargetQuality - map.Quality) + 3);

                    try
                    {
                        while (map.Quality < profile.TargetQuality && qualityAttempts < maxQualityAttempts && !map.IsCorrupted)
                        {
                            if (ct.IsCancellationRequested || CrafterInput.IsEmergencyStopPressed()) break;

                            qualityAttempts++;
                            int prevQ = map.Quality;
                            CraftLogger.Step("CHISEL", $"Attempt #{qualityAttempts}: Applying Chisel to map in-place (Current Q: {prevQ}%)...");

                            // Left-click map in-place
                            await CrafterInput.LeftClickInPlace(delay);

                            // Wait for server to confirm quality change
                            var refreshed = await WaitForMapChange(gc, map.SlotX, map.SlotY, prevQ, map.Rarity, map.ModCount, timeoutMs: 750, expectedClickPos: map.ClickPosition);
                            if (refreshed != null && refreshed.Quality != prevQ)
                            {
                                map.Quality = refreshed.Quality;
                                map.Rarity = refreshed.Rarity;
                                CraftLogger.Info("CHISEL", $"Map quality updated: {prevQ}% -> {map.Quality}%");
                                if (map.Quality >= profile.TargetQuality) break;
                            }
                            else
                            {
                                CraftLogger.Warn("CHISEL", $"Could not detect quality change on map ({map.SlotX},{map.SlotY}). Stopping chiseling loop to prevent misclicks.");
                                break;
                            }

                            // Humanized wait between chisel clicks based on ActionDelayMs
                            int humanDelay = Math.Max(150, (delay / 2) + _rng.Next(30));
                            await Task.Delay(humanDelay, ct);
                        }
                    }
                    finally
                    {
                        CrafterInput.SetShift(false);
                        CrafterInput.SetCtrl(false);
                        await Task.Delay(Math.Max(80, (delay / 4) + _rng.Next(20)), ct);
                    }

                    CraftLogger.Info("CHISEL", $"Chiseling stage done. Final Quality: {map.Quality}% (Target: {profile.TargetQuality}%, Attempts used: {qualityAttempts})");
                }
            }
            else
            {
                CraftLogger.Info("CHISEL", $"Skipping chisels: ChiselMaps={profile.ChiselMaps}, Quality={map.Quality}%, Target={profile.TargetQuality}%");
            }

            // Stage 2: Upgrade Normal -> Rare
            if (map.Rarity == ItemRarity.Normal)
            {
                map.Status = CraftStatus.UpgradingRarity;
                CraftLogger.Step("UPGRADE", "Map is Normal rarity. Upgrading to Rare via Alchemy/Binding...");
                var alch = await _tracker.GetAvailableCurrencyWithRetry(gc, CurrencyKind.Alchemy, ignoredSlots, maxRetries: 3);
                if (alch == null)
                {
                    CraftLogger.Error("UPGRADE", "Out of Orb of Alchemy / Binding! Cannot upgrade Normal map.");
                    StatusMessage = "Out of Orb of Alchemy / Binding!";
                    map.Status = CraftStatus.OutOfCurrency;
                    return false;
                }

                StatusMessage = "Upgrading map to Rare (Alchemy/Binding)...";
                CraftLogger.Step("UPGRADE", $"Using {alch.Kind} from {alch.LocationDescription}...");
                await ApplyCurrency(gc, alch.ClickPosition, map.ClickPosition, delay, ct);

                var refreshed = await WaitForMapChange(gc, map.SlotX, map.SlotY, map.Quality, map.Rarity, map.ModCount, timeoutMs: 700);
                if (refreshed != null) UpdateMapData(map, refreshed);
                CraftLogger.Info("UPGRADE", $"Map upgraded to: Rarity={map.Rarity}, Mods={map.ModCount}");
            }

            // Stage 3: If Magic, scour + alch it
            if (map.Rarity == ItemRarity.Magic)
            {
                CraftLogger.Step("UPGRADE", "Map is Magic rarity. Scour + Alching to upgrade to Rare...");
                var scour = await _tracker.GetAvailableCurrencyWithRetry(gc, CurrencyKind.Scouring, ignoredSlots, maxRetries: 3);
                var alch = await _tracker.GetAvailableCurrencyWithRetry(gc, CurrencyKind.Alchemy, ignoredSlots, maxRetries: 3);

                if (scour == null || alch == null)
                {
                    CraftLogger.Error("UPGRADE", $"Missing Scour or Alch! Scour={scour != null}, Alch={alch != null}");
                    StatusMessage = "Out of Scours/Alchs to upgrade magic map!";
                    map.Status = CraftStatus.OutOfCurrency;
                    return false;
                }

                CraftLogger.Step("UPGRADE", $"Scouring magic map from {scour.LocationDescription}...");
                await ApplyCurrency(gc, scour.ClickPosition, map.ClickPosition, delay, ct);
                await Task.Delay(delay + 30, ct);

                CraftLogger.Step("UPGRADE", $"Alching map from {alch.LocationDescription}...");
                await ApplyCurrency(gc, alch.ClickPosition, map.ClickPosition, delay, ct);

                var refreshed = await WaitForMapChange(gc, map.SlotX, map.SlotY, map.Quality, map.Rarity, map.ModCount, timeoutMs: 700);
                if (refreshed != null) UpdateMapData(map, refreshed);
                CraftLogger.Info("UPGRADE", $"Magic map upgraded to: Rarity={map.Rarity}, Mods={map.ModCount}");
            }

            // Stage 4: Evaluation & Reroll Loop
            map.Status = CraftStatus.Rerolling;
            CraftLogger.Step("REROLL", $"Starting evaluation & reroll loop (Max attempts: {profile.MaxAttemptsPerMap}, Method: {profile.Method})...");

            while (map.AttemptsUsed < profile.MaxAttemptsPerMap)
            {
                if (ct.IsCancellationRequested) return false;

                // Evaluate current state
                var eval = MapEvaluator.Evaluate(map, profile);
                CraftLogger.Info("EVAL", $"Attempt #{map.AttemptsUsed} Evaluation: Passed={eval.Passed} | Reason: '{eval.Reason}'");
                CraftLogger.Info("EVAL", $"Current mods ({map.ModCount}): [{string.Join(" | ", map.ModLines)}]");

                if (eval.Passed)
                {
                    CraftLogger.Success("EVAL", $"Criteria MET for map at ({map.SlotX},{map.SlotY})!");
                    var (finalSuccess, shouldContinueRerolling) = await FinalizeCraftedMap(gc, map, profile, delay, ct, ignoredSlots);
                    if (finalSuccess) return true;
                    if (!shouldContinueRerolling) return false;
                }

                if (profile.Method == RerollMethod.ChaosOrb)
                {
                    var chaos = await _tracker.GetAvailableCurrencyWithRetry(gc, CurrencyKind.Chaos, ignoredSlots, maxRetries: 3);
                    if (chaos == null)
                    {
                        CraftLogger.Error("REROLL", "OUT OF CHAOS ORBS! Cannot continue rerolling.");
                        StatusMessage = "Out of Chaos Orbs!";
                        map.Status = CraftStatus.OutOfCurrency;
                        return false;
                    }

                    CraftLogger.Step("REROLL", $"Selecting Chaos Orb from {chaos.LocationDescription} (Stack: {chaos.StackCount})");

                    // Move to Chaos stack in Stash and Right-Click to pick it up with generous human settle
                    await CrafterInput.MoveCursorTo(chaos.ClickPosition);
                    await Task.Delay(Math.Max(150, (delay / 2) + _rng.Next(30)), ct);
                    Input.RightDown();
                    await Task.Delay(50 + _rng.Next(20), ct);
                    Input.RightUp();
                    await Task.Delay(Math.Max(150, (delay / 2) + _rng.Next(30)), ct);

                    // Hold Shift (native PoE repeat-apply key)
                    CrafterInput.SetShift(true);
                    await Task.Delay(80 + _rng.Next(25), ct);

                    // Move cursor over target map in player inventory
                    await CrafterInput.MoveCursorTo(map.ClickPosition);
                    await Task.Delay(Math.Max(150, (delay / 2) + _rng.Next(30)), ct);

                    try
                    {
                        while (map.AttemptsUsed < profile.MaxAttemptsPerMap)
                        {
                            if (ct.IsCancellationRequested || CrafterInput.IsEmergencyStopPressed()) break;

                            map.AttemptsUsed++;
                            StatusMessage = $"Rerolling map with Chaos ({map.AttemptsUsed}/{profile.MaxAttemptsPerMap})...";

                            int prevModCount = map.ModCount;
                            int prevQ = map.Quality;
                            ItemRarity prevR = map.Rarity;
                            int prevAtlas = map.AtlasDrops;
                            int prevPackSize = map.PackSize;
                            var prevMods = map.RawModNames.ToList();

                            // Left-click in place over the map
                            await CrafterInput.LeftClickInPlace(delay);

                            // Wait for map to update in memory
                            var refreshed = await WaitForMapChange(gc, map.SlotX, map.SlotY, prevQ, prevR, prevModCount, prevAtlas, prevPackSize, prevMods, timeoutMs: 800, expectedClickPos: map.ClickPosition);
                            if (refreshed != null)
                            {
                                UpdateMapData(map, refreshed);
                            }
                            else
                            {
                                CraftLogger.Warn("REROLL", $"Map at ({map.SlotX},{map.SlotY}) did not reflect changes within timeout. Stopping continuous in-place spam.");
                                break; // Break out of continuous spam to avoid clicking blindly
                            }

                            // Evaluate updated map
                            var innerEval = MapEvaluator.Evaluate(map, profile);
                            CraftLogger.Info("EVAL", $"Attempt #{map.AttemptsUsed} Evaluation: Passed={innerEval.Passed} | Reason: '{innerEval.Reason}'");
                            CraftLogger.Info("EVAL", $"Current stats: ItemQuant={map.ItemQuantity}% AtlasDrops={map.AtlasDrops}% Rarity=+{map.ItemRarityBonus}% PackSize={map.PackSize}% Maps=+{map.MoreMapsBonus}% Cur=+{map.CurrencyBonus}% Threads=+{map.ScarabsBonus}%");
                            CraftLogger.Info("EVAL", $"Current mods ({map.ModCount}): [{string.Join(" | ", map.ModLines)}]");

                            if (innerEval.Passed)
                            {
                                CraftLogger.Success("EVAL", $"Criteria MET for map at ({map.SlotX},{map.SlotY})!");
                                break;
                            }

                            map.StatusDetails = $"Attempt {map.AttemptsUsed}: {innerEval.Reason}";

                            // Full humanized ActionDelayMs wait before next roll
                            int humanDelay = Math.Max(200, delay + _rng.Next(-20, 40));
                            await Task.Delay(humanDelay, ct);
                        }
                    }
                    finally
                    {
                        // Release Shift/Ctrl - letting go of shift stops repeat crafting cleanly
                        CrafterInput.SetShift(false);
                        CrafterInput.SetCtrl(false);
                        await Task.Delay(Math.Max(80, (delay / 4) + _rng.Next(20)), ct);
                    }

                    // Check if passed criteria
                    var postEval = MapEvaluator.Evaluate(map, profile);
                    if (postEval.Passed)
                    {
                        var (finalSuccess, shouldContinueRerolling) = await FinalizeCraftedMap(gc, map, profile, delay, ct, ignoredSlots);
                        if (finalSuccess) return true;
                        if (!shouldContinueRerolling) return false;
                    }
                }
                else // Scour + Alch
                {
                    map.AttemptsUsed++;
                    StatusMessage = $"Rerolling map ({map.AttemptsUsed}/{profile.MaxAttemptsPerMap}): {eval.Reason}";
                    map.StatusDetails = $"Attempt {map.AttemptsUsed}: {eval.Reason}";

                    var scour = await _tracker.GetAvailableCurrencyWithRetry(gc, CurrencyKind.Scouring, ignoredSlots, maxRetries: 3);
                    var alch = await _tracker.GetAvailableCurrencyWithRetry(gc, CurrencyKind.Alchemy, ignoredSlots, maxRetries: 3);
                    if (scour == null)
                    {
                        CraftLogger.Error("REROLL", "OUT OF SCOURING ORBS! Cannot continue.");
                        StatusMessage = "Out of Orb of Scouring!";
                        map.Status = CraftStatus.OutOfCurrency;
                        return false;
                    }
                    if (alch == null)
                    {
                        CraftLogger.Error("REROLL", "OUT OF ALCHEMY ORBS! Cannot continue.");
                        StatusMessage = "Out of Orb of Alchemy!";
                        map.Status = CraftStatus.OutOfCurrency;
                        return false;
                    }

                    CraftLogger.Step("REROLL", $"Attempt #{map.AttemptsUsed}: Using Scour from {scour.LocationDescription}...");
                    await ApplyCurrency(gc, scour.ClickPosition, map.ClickPosition, delay, ct);
                    await Task.Delay(delay + 25, ct);

                    CraftLogger.Step("REROLL", $"Attempt #{map.AttemptsUsed}: Using Alch from {alch.LocationDescription}...");
                    await ApplyCurrency(gc, alch.ClickPosition, map.ClickPosition, delay, ct);

                    var refreshed = await WaitForMapChange(gc, map.SlotX, map.SlotY, map.Quality, map.Rarity, map.ModCount, timeoutMs: 700);
                    if (refreshed != null) UpdateMapData(map, refreshed);
                }
            }

            map.Status = CraftStatus.FailedMaxAttempts;
            map.StatusDetails = $"Hit max attempts limit ({profile.MaxAttemptsPerMap})";
            CraftLogger.Warn("SESSION", $"Map failed: Hit max attempts limit ({profile.MaxAttemptsPerMap})");
            return false;
        }

        private async Task<(bool success, bool continueRerolling)> FinalizeCraftedMap(GameController gc, ParsedMapItem map, CraftingProfile profile, int delay, CancellationToken ct, HashSet<(int x, int y)>? ignoredSlots)
        {
            // Check Exalt Slam option
            bool passedAfterExalts = true;
            if (profile.ExaltSlam && map.HasModSpace && map.ExaltsUsed < profile.MaxExaltsToApply)
            {
                passedAfterExalts = await HandleExaltSlamming(gc, map, profile, delay, ct, ignoredSlots);
            }

            if (passedAfterExalts)
            {
                // Successfully crafted! Check Vaal option
                if (profile.VaalSetting == VaalMode.OnCraftSuccess && !map.IsCorrupted)
                {
                    var vaal = await _tracker.GetAvailableCurrencyWithRetry(gc, CurrencyKind.Vaal, ignoredSlots, maxRetries: 3);
                    if (vaal != null)
                    {
                        CraftLogger.Step("VAAL", $"Vaal corruption enabled. Applying Vaal Orb from {vaal.LocationDescription}...");
                        StatusMessage = "Applying Vaal Orb to crafted map...";
                        await ApplyCurrency(gc, vaal.ClickPosition, map.ClickPosition, delay, ct);
                        await Task.Delay(delay + 40, ct);
                        map.IsCorrupted = true;
                    }
                    else
                    {
                        CraftLogger.Warn("VAAL", "Vaal on success enabled, but no Vaal Orbs found in Stash or Bag!");
                    }
                }

                map.Status = CraftStatus.Succeeded;
                map.StatusDetails = $"Passed in {map.AttemptsUsed} attempts (Q:{map.Quality}% Quant:{map.ItemQuantity}% Atlas:{map.AtlasDrops}% Rar:+{map.ItemRarityBonus}% PS:{map.PackSize}%)";
                CraftLogger.Success("SESSION", $"Map crafting SUCCESS: {map.StatusDetails}");
                return (true, false);
            }
            else
            {
                if (map.Status == CraftStatus.Failed || map.Status == CraftStatus.OutOfCurrency)
                {
                    CraftLogger.Error("EXALT", $"Exalt slam aborted with status {map.Status}: '{map.StatusDetails}'. Halting this map.");
                    return (false, false);
                }

                CraftLogger.Warn("EXALT", "Exalt slam broke criteria or slammed banned mod! Resuming rerolling...");
                StatusMessage = "Exalt slammed banned mod! Resuming reroll...";
                return (false, true);
            }
        }

        private async Task<bool> HandleExaltSlamming(GameController gc, ParsedMapItem map, CraftingProfile profile, int delay, CancellationToken ct, HashSet<(int x, int y)>? ignoredSlots)
        {
            CraftLogger.Step("EXALT", $"Starting Exalt Slam stage. Mod space: {map.ModCount}/6, Allowed slams: {profile.MaxExaltsToApply}");
            while (map.HasModSpace && map.ExaltsUsed < profile.MaxExaltsToApply)
            {
                if (ct.IsCancellationRequested || CrafterInput.IsEmergencyStopPressed()) return false;

                var exalt = await _tracker.GetAvailableCurrencyWithRetry(gc, CurrencyKind.Exalted, ignoredSlots, maxRetries: 3);
                if (exalt == null)
                {
                    CraftLogger.Warn("EXALT", "Out of Exalted Orbs in Stash / Bag! Finishing without more slams.");
                    StatusMessage = "Out of Exalted Orbs (finishing without more slams)";
                    return true;
                }

                map.Status = CraftStatus.Exalting;
                StatusMessage = $"Exalting map (mod space {map.ModCount}/6)...";
                CraftLogger.Step("EXALT", $"Slam #{map.ExaltsUsed + 1}: Using Exalted Orb from {exalt.LocationDescription}...");
                CraftLogger.Info("EXALT", $"Coordinates: Exalt=({exalt.ClickPosition.X:F0}, {exalt.ClickPosition.Y:F0}), Map=({map.ClickPosition.X:F0}, {map.ClickPosition.Y:F0})");
                CraftLogger.Info("EXALT", $"Pre-slam map state: ModCount={map.ModCount}, Mods: [{string.Join(" | ", map.ModLines)}]");

                int prevModCount = map.ModCount;
                var prevMods = map.RawModNames.ToList();

                bool applied = await ApplyCurrency(gc, exalt.ClickPosition, map.ClickPosition, delay, ct);
                if (!applied)
                {
                    CraftLogger.Error("EXALT", "ApplyCurrency failed during Exalt slam! Aborting this map.");
                    map.Status = CraftStatus.Failed;
                    map.StatusDetails = "Failed to apply Exalted Orb";
                    return false;
                }

                map.ExaltsUsed++;

                var refreshed = await WaitForMapChange(gc, map.SlotX, map.SlotY, map.Quality, map.Rarity, prevModCount, map.AtlasDrops, map.PackSize, prevMods, timeoutMs: 1000);
                if (refreshed != null)
                {
                    UpdateMapData(map, refreshed);
                }
                else
                {
                    CraftLogger.Warn("EXALT", "Could not detect map change after Exalt slam.");
                }

                // Re-evaluate immediately
                var eval = MapEvaluator.Evaluate(map, profile);
                CraftLogger.Info("EXALT", $"Post-slam evaluation: Passed={eval.Passed}, Reason: '{eval.Reason}', ModCount={map.ModCount}, Mods: [{string.Join(" | ", map.ModLines)}]");
                if (!eval.Passed)
                {
                    map.StatusDetails = $"Exalt failed: {eval.Reason}";
                    CraftLogger.Warn("EXALT", $"Exalt slam violated rule: {eval.Reason}");
                    return false;
                }
            }

            return true;
        }

        private static async Task<bool> ApplyCurrency(GameController gc, Vector2 currencyPos, Vector2 itemPos, int delayMs, CancellationToken ct)
        {
            CraftLogger.Info("APPLY", $"Right-clicking currency at ({currencyPos.X:F0}, {currencyPos.Y:F0}) -> Left-clicking target item at ({itemPos.X:F0}, {itemPos.Y:F0})");

            // Right click currency
            await CrafterInput.RightClickAt(currencyPos, delayMs);
            if (ct.IsCancellationRequested || CrafterInput.IsEmergencyStopPressed()) return false;

            // Move to target item with settle
            await CrafterInput.MoveCursorTo(itemPos);
            int preDelay = Math.Max(150, (delayMs / 2) + _rng.Next(25));
            await Task.Delay(preDelay, ct);
            if (ct.IsCancellationRequested || CrafterInput.IsEmergencyStopPressed()) return false;

            // Left click target item to apply currency
            Input.LeftDown();
            await Task.Delay(50 + _rng.Next(20), ct);
            Input.LeftUp();
            int postDelay = Math.Max(150, (delayMs / 2) + _rng.Next(25));
            await Task.Delay(postDelay, ct);

            // ANTI-PICKUP SAFETY GUARD: Verify map was NOT accidentally picked up onto cursor
            var cursor = gc.IngameState?.IngameUi?.Cursor;
            if (cursor != null && cursor.ChildCount > 0)
            {
                CraftLogger.Error("SAFETY", $"CRITICAL: Item was picked up onto cursor at ({itemPos.X:F0}, {itemPos.Y:F0})! Immediately dropping back into slot.");
                Input.SetCursorPos(itemPos);
                await Task.Delay(60, ct);
                Input.LeftDown();
                await Task.Delay(45, ct);
                Input.LeftUp();
                await Task.Delay(150, ct);
                CrafterInput.ReleaseAll();
                return false;
            }

            return true;
        }

        private static async Task<ParsedMapItem?> WaitForMapChange(
            GameController gc,
            int slotX,
            int slotY,
            int prevQuality,
            ItemRarity prevRarity,
            int prevModCount,
            int prevAtlasDrops = -1,
            int prevPackSize = -1,
            List<string>? prevModNames = null,
            int timeoutMs = 700,
            Vector2? expectedClickPos = null)
        {
            var sw = Stopwatch.StartNew();
            ParsedMapItem? last = null;

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(35);
                last = RefreshMap(gc, slotX, slotY, expectedClickPos);
                if (last != null)
                {
                    bool changed = last.Quality != prevQuality ||
                                   last.Rarity != prevRarity ||
                                   last.ModCount != prevModCount ||
                                   (prevAtlasDrops >= 0 && last.AtlasDrops != prevAtlasDrops) ||
                                   (prevPackSize >= 0 && last.PackSize != prevPackSize) ||
                                   (prevModNames != null && !last.RawModNames.SequenceEqual(prevModNames));

                    if (changed)
                    {
                        CraftLogger.Info("CONFIRM", $"Confirmed change in {sw.ElapsedMilliseconds}ms (Q:{prevQuality}->{last.Quality}, R:{prevRarity}->{last.Rarity}, Mods:{prevModCount}->{last.ModCount}, Atlas:{last.AtlasDrops}%, PS:{last.PackSize}%, Rar:+{last.ItemRarityBonus}%)");
                        return last;
                    }
                }
            }

            CraftLogger.Warn("CONFIRM", $"Timeout ({timeoutMs}ms) waiting for map state change at ({slotX},{slotY}).");
            return last;
        }

        private static ParsedMapItem? RefreshMap(GameController gc, int slotX, int slotY, Vector2? expectedClickPos = null)
        {
            var invPanel = gc.IngameState?.IngameUi?.InventoryPanel;
            if (invPanel == null) return null;

            var playerInv = invPanel[InventoryIndex.PlayerInventory];
            if (playerInv == null) return null;

            var items = InventoryTracker.SafeGetItems(playerInv.VisibleInventoryItems);
            if (items.Count == 0) return null;

            var invRect = playerInv.GetClientRect();
            float cellW = invRect.Width / 12f;
            float cellH = invRect.Height / 5f;
            var windowRect = gc.Window.GetWindowRectangle();

            NormalInventoryItem? bestItem = null;
            float bestDistSq = float.MaxValue;

            foreach (var it in items)
            {
                if (it == null) continue;
                var rect = it.GetClientRect();
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                int sx = (cellW > 10f) ? Math.Clamp((int)((rect.Center.X - invRect.Left) / cellW), 0, 11) : (int)it.InventPosX;
                int sy = (cellH > 10f) ? Math.Clamp((int)((rect.Center.Y - invRect.Top) / cellH), 0, 4) : (int)it.InventPosY;

                if (sx == slotX && sy == slotY)
                {
                    bestItem = it;
                    break;
                }

                if (expectedClickPos.HasValue)
                {
                    var absCenter = new Vector2(windowRect.X + rect.Center.X, windowRect.Y + rect.Center.Y);
                    float distSq = Vector2.DistanceSquared(absCenter, expectedClickPos.Value);
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestItem = it;
                    }
                }
            }

            if (bestItem == null) return null;
            return ItemParser.ParseMap(gc, bestItem, slotX, slotY);
        }

        private static void UpdateMapData(ParsedMapItem target, ParsedMapItem source)
        {
            target.Rarity = source.Rarity;
            target.Quality = source.Quality;
            target.Tier = source.Tier;
            target.IsCorrupted = source.IsCorrupted;
            target.ModCount = source.ModCount;
            target.PrefixCount = source.PrefixCount;
            target.SuffixCount = source.SuffixCount;
            target.ItemQuantity = source.ItemQuantity;
            target.AtlasDrops = source.AtlasDrops;
            target.ItemRarityBonus = source.ItemRarityBonus;
            target.PackSize = source.PackSize;
            target.MoreMapsBonus = source.MoreMapsBonus;
            target.CurrencyBonus = source.CurrencyBonus;
            target.ScarabsBonus = source.ScarabsBonus;
            target.ModLines = source.ModLines;
            target.RawModNames = source.RawModNames;
            target.FormattedTooltipText = source.FormattedTooltipText;
        }
    }
}
