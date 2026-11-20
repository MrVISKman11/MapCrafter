using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;

namespace MapCrafter
{
    public class Settings : ISettings
    {
        public ToggleNode Enable { get; set; } = new(false);

        [Menu("Start/Stop Hotkey", "Key to start or stop automated map crafting")]
        public HotkeyNodeV2 ToggleCraftingHotkey { get; set; } = new(Keys.F8);

        [Menu("Status: Crafting Active", "Whether the crafter is currently running")]
        public ToggleNode IsRunning { get; set; } = new(false);

        [Menu("Timing & Delay", 100)]
        public EmptyNode TimingHeader { get; set; } = new();

        [Menu("Action Delay (ms)", "Delay between currency click and map click", 101, 100)]
        public RangeNode<int> ActionDelayMs { get; set; } = new(130, 40, 600);

        [Menu("Play Sound On Completion", "Play sound alert and system beep when crafting finishes", 102, 100)]
        public ToggleNode PlaySoundOnCompletion { get; set; } = new(true);

        [Menu("Enable File Logging", "Write detailed crafting logs to crafting.log", 103, 100)]
        public ToggleNode EnableFileLogging { get; set; } = new(true);

        [Menu("Interface & Visuals", 200)]
        public EmptyNode VisualsHeader { get; set; } = new();

        [Menu("Show HUD Window", "Display the MapCrafter ImGui control window", 201, 200)]
        public ToggleNode ShowHud { get; set; } = new(true);

        [Menu("Draw Inventory Status Grid", "Render color-coded overlay on inventory slots showing map status", 202, 200)]
        public ToggleNode DrawInventoryGrid { get; set; } = new(true);

        [Menu("Active Profile Name", "Last selected crafting profile", 203, 200)]
        public TextNode ActiveProfileName { get; set; } = new("Currency+Rarity");

        // Multi-Profile Selection & Global Build Safety
        public System.Collections.Generic.List<string> ActiveProfileNames { get; set; } = new();
        public System.Collections.Generic.List<string> GlobalBannedMods { get; set; } = new()
        {
            "cannot Regenerate",
            "less effect of flasks"
        };
    }
}
