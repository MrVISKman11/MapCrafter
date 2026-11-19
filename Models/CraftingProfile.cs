using System;
using System.Collections.Generic;

namespace MapCrafter.Models
{
    public class CraftingProfile
    {
        public string Name { get; set; } = "Default Profile";
        
        // Quality
        public bool ChiselMaps { get; set; } = true;
        public bool ChiselNormal { get => ChiselMaps; set => ChiselMaps = value; }
        public int TargetQuality { get; set; } = 20;

        // Reroll & Modifying
        public RerollMethod Method { get; set; } = RerollMethod.ChaosOrb;
        public bool ExaltSlam { get; set; } = false;
        public int MaxExaltsToApply { get; set; } = 2; // up to 6 total affixes
        public VaalMode VaalSetting { get; set; } = VaalMode.Never;
        public int MaxAttemptsPerMap { get; set; } = 80;

        // Core 3.30 Minimum Stat Thresholds (0 = disabled / any) - NO max caps!
        public int MinAtlasDrops { get; set; } = 0;
        public int MinItemRarity { get; set; } = 0;
        public int MinPackSize { get; set; } = 0;
        public int MinMoreMaps { get; set; } = 0;
        public int MinMoreCurrency { get; set; } = 0;
        public int MinMoreThreads { get; set; } = 0;
        public int MinItemQuantity { get; set; } = 0;

        // Backwards compatibility aliases for existing profile JSONs
        public int MinQuantity { get => MinAtlasDrops > 0 ? MinAtlasDrops : MinItemQuantity; set => MinAtlasDrops = value; }
        public int MinCurrencyStat { get => MinMoreCurrency; set => MinMoreCurrency = value; }
        public int MinScarabsStat { get => MinMoreThreads; set => MinMoreThreads = value; }
        public int MinMapsBonus { get => MinMoreMaps; set => MinMoreMaps = value; }

        // Regex Support (poe.re compatible)
        public string CustomRegex { get; set; } = string.Empty;

        // Mod Lists (Case-insensitive substring or regex matches)
        public List<string> BannedMods { get; set; } = new();
        public List<string> RequiredMods { get; set; } = new();
        public bool RequireAllRequiredMods { get; set; } = false; // false = require at least one if list not empty

        public CraftingProfile Clone()
        {
            return new CraftingProfile
            {
                Name = this.Name + " (Copy)",
                ChiselMaps = this.ChiselMaps,
                TargetQuality = this.TargetQuality,
                Method = this.Method,
                ExaltSlam = this.ExaltSlam,
                MaxExaltsToApply = this.MaxExaltsToApply,
                VaalSetting = this.VaalSetting,
                MaxAttemptsPerMap = this.MaxAttemptsPerMap,
                MinAtlasDrops = this.MinAtlasDrops,
                MinItemRarity = this.MinItemRarity,
                MinPackSize = this.MinPackSize,
                MinMoreMaps = this.MinMoreMaps,
                MinMoreCurrency = this.MinMoreCurrency,
                MinMoreThreads = this.MinMoreThreads,
                MinItemQuantity = this.MinItemQuantity,
                CustomRegex = this.CustomRegex,
                BannedMods = new List<string>(this.BannedMods),
                RequiredMods = new List<string>(this.RequiredMods),
                RequireAllRequiredMods = this.RequireAllRequiredMods
            };
        }
    }
}
