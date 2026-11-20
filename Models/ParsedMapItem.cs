using System.Collections.Generic;
using System.Numerics;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;

namespace MapCrafter.Models
{
    public enum CraftStatus
    {
        Pending,
        Chiseling,
        UpgradingRarity,
        Rerolling,
        Exalting,
        VaalPassed,
        Succeeded,
        Failed,
        FailedMaxAttempts,
        OutOfCurrency,
        Skipped
    }

    public class ParsedMapItem
    {
        public NormalInventoryItem ItemWrapper { get; set; } = null!;
        public Entity Entity { get; set; } = null!;
        public int SlotX { get; set; }
        public int SlotY { get; set; }
        public Vector2 ClickPosition { get; set; }
        public SharpDX.RectangleF ClientRect { get; set; }

        public string BaseName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public ItemRarity Rarity { get; set; }
        public int Quality { get; set; }
        public int Tier { get; set; }
        public bool IsCorrupted { get; set; }
        public bool IsMirrored { get; set; }

        public int ModCount { get; set; }
        public int PrefixCount { get; set; }
        public int SuffixCount { get; set; }

        // Core 3.30 Map Yield & Scaling Modifiers
        public int ItemQuantity { get; set; }
        public int Quantity { get => ItemQuantity; set => ItemQuantity = value; } // Alias for backwards compatibility
        public int AtlasDrops { get; set; }
        public int ItemRarityBonus { get; set; }
        public int PackSize { get; set; }
        public int MoreMapsBonus { get; set; }
        public int CurrencyBonus { get; set; }
        public int ScarabsBonus { get; set; }
        public int MoreThreadsBonus { get => ScarabsBonus; set => ScarabsBonus = value; }

        public List<string> ModLines { get; set; } = new();
        public List<string> RawModNames { get; set; } = new();
        public string FormattedTooltipText { get; set; } = string.Empty;

        // Crafting tracking
        public CraftStatus Status { get; set; } = CraftStatus.Pending;
        public string StatusDetails { get; set; } = "Pending";
        public string? MatchedProfileName { get; set; }
        public int AttemptsUsed { get; set; }
        public int ExaltsUsed { get; set; }

        public bool HasModSpace => (PrefixCount < 3 || SuffixCount < 3) && ModCount < 6;
    }
}
