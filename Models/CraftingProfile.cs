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

        // Stat Filters
        public int MinQuantity { get; set; } = 0;
        public int MinPackSize { get; set; } = 0;
        
        // Specific Stat Filters (e.g. T17 maps or special implicits/explicits)
        public int MaxCurrencyStat { get; set; } = 0; // 0 = disabled. If > 0, e.g. currency < 50
        public int MinCurrencyStat { get; set; } = 0;
        public int MinScarabsStat { get; set; } = 0;

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
                MinQuantity = this.MinQuantity,
                MinPackSize = this.MinPackSize,
                MaxCurrencyStat = this.MaxCurrencyStat,
                MinCurrencyStat = this.MinCurrencyStat,
                MinScarabsStat = this.MinScarabsStat,
                CustomRegex = this.CustomRegex,
                BannedMods = new List<string>(this.BannedMods),
                RequiredMods = new List<string>(this.RequiredMods),
                RequireAllRequiredMods = this.RequireAllRequiredMods
            };
        }
    }
}
