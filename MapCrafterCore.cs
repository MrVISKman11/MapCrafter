using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using MapCrafter.Models;
using MapCrafter.Services;
using MapCrafter.UI;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace MapCrafter
{
    public class MapCrafterCore : BaseSettingsPlugin<Settings>
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private CraftingEngine _engine = null!;
        private MapCrafterUi _ui = null!;
        private List<CraftingProfile> _profiles = new();
        private readonly HashSet<(int x, int y)> _ignoredSlots = new();
        private bool _lastToggleKeyDown;
        private DateTime _lastHotkeyActionTime = DateTime.MinValue;

        private bool IsToggleHotkeyPressed()
        {
            var key = Settings.ToggleCraftingHotkey.Value.Key;
            if (key == Keys.None) return false;
            bool isDown = (GetAsyncKeyState((int)key) & 0x8000) != 0;
            bool triggered = isDown && !_lastToggleKeyDown;
            _lastToggleKeyDown = isDown;
            return triggered;
        }

        private static bool IsEscapePressed()
        {
            return (GetAsyncKeyState((int)Keys.Escape) & 0x8000) != 0;
        }

        public override bool Initialise()
        {
            Name = "MapCrafter";

            // Initialize up-to-date in-game mod database
            ModDatabase.Initialize(DirectoryFullName, GameController);

            _engine = new CraftingEngine();
            _ui = new MapCrafterUi();
            CraftLogger.Initialize(DirectoryFullName, Settings.EnableFileLogging.Value);

            // Load profiles from local Profiles folder
            _profiles = ProfileManager.LoadProfiles(DirectoryFullName);

            // Restore active profile if found
            if (!string.IsNullOrEmpty(Settings.ActiveProfileName.Value))
            {
                var matched = _profiles.FirstOrDefault(p => p.Name.Equals(Settings.ActiveProfileName.Value, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    // Move to front or select
                    _profiles.Remove(matched);
                    _profiles.Insert(0, matched);
                }
            }

            return true;
        }

        public override void AreaChange(AreaInstance area)
        {
            if (_engine.IsRunning)
            {
                _engine.Stop();
            }
            CrafterInput.ReleaseAll();
        }

        public override Job? Tick()
        {
            CraftLogger.EnableFileLogging = Settings.EnableFileLogging.Value;

            // Emergency stop via Escape key: ALWAYS stops immediately
            if (_engine.IsRunning && IsEscapePressed())
            {
                CraftLogger.Warn("CORE", "Emergency stop requested via Escape.");
                _engine.Stop();
                CrafterInput.ReleaseAll();
                _lastHotkeyActionTime = DateTime.UtcNow;
                return null;
            }

            // Start/Stop Toggle via configured hotkey (e.g. F8) with edge detection and 600ms debounce
            bool toggleTriggered = IsToggleHotkeyPressed();
            var now = DateTime.UtcNow;

            if (toggleTriggered)
            {
                var elapsedSinceHotkey = (now - _lastHotkeyActionTime).TotalMilliseconds;
                var elapsedSinceStart = (now - _engine.StartTime).TotalMilliseconds;

                if (_engine.IsRunning)
                {
                    // Debounce stop: require at least 600ms from start to prevent bounce stop
                    if (elapsedSinceHotkey >= 600 && elapsedSinceStart >= 600)
                    {
                        _lastHotkeyActionTime = now;
                        CraftLogger.Warn("CORE", "Stop requested via Hotkey.");
                        _engine.Stop();
                        CrafterInput.ReleaseAll();
                    }
                }
                else
                {
                    // Debounce start: require at least 400ms from last hotkey action
                    if (elapsedSinceHotkey >= 400)
                    {
                        _lastHotkeyActionTime = now;
                        if (_profiles.Count > 0)
                        {
                            var activeProfile = _profiles.FirstOrDefault(p => p.Name.Equals(Settings.ActiveProfileName.Value, StringComparison.OrdinalIgnoreCase)) ?? _profiles[0];
                            CraftLogger.Info("CORE", $"Start requested via Hotkey with profile '{activeProfile.Name}'.");
                            _engine.Start(GameController, activeProfile, Settings, _ignoredSlots);
                        }
                    }
                }
            }

            return null;
        }

        public override void Render()
        {
            if (GameController?.Player == null) return;

            // Render Main ImGui Window
            _ui.Render(GameController, _engine, _profiles, Settings, _ignoredSlots, DirectoryFullName);

            // Render in-game inventory slot overlays
            if (Settings.DrawInventoryGrid.Value)
            {
                RenderInGameInventoryOverlay();
            }
        }

        private void RenderInGameInventoryOverlay()
        {
            var invPanel = GameController.IngameState?.IngameUi?.InventoryPanel;
            if (invPanel == null || !invPanel.IsVisible) return;

            var playerInv = invPanel[InventoryIndex.PlayerInventory];
            if (playerInv == null) return;

            foreach (var map in _engine.Tracker.Maps)
            {
                var rect = map.ClientRect;
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                var color = map.Status switch
                {
                    CraftStatus.Succeeded or CraftStatus.VaalPassed => SharpDX.Color.LimeGreen,
                    CraftStatus.Chiseling or CraftStatus.UpgradingRarity or CraftStatus.Rerolling or CraftStatus.Exalting => SharpDX.Color.Yellow,
                    CraftStatus.FailedMaxAttempts or CraftStatus.OutOfCurrency => SharpDX.Color.Red,
                    CraftStatus.Skipped => SharpDX.Color.Gray,
                    _ => SharpDX.Color.Orange
                };

                Graphics.DrawFrame(rect, color, 2);

                if (map == _engine.CurrentMap)
                {
                    Graphics.DrawText($"CRAFTING: {map.Status}", new Vector2(rect.Left + 2, rect.Top + 2), SharpDX.Color.White);
                }
            }
        }

        public override void OnClose()
        {
            if (_engine != null && _engine.IsRunning)
            {
                _engine.Stop();
            }
            CrafterInput.ReleaseAll();
        }
    }
}
