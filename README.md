# MapCrafter - ExileApi Plugin

High-performance, memory-driven automated Map Crafter for ExileApi (.NET 10). Replaces legacy external clicker tools (`AutoCraft` and `Crafter`) with instantaneous in-memory evaluation and humanized mouse interaction.

---

## Features

### 1. Multi-Stage Crafting Flow
1. **Chisel Phase**:
   - Automatically detects maps below target quality (regardless of whether the map is Normal, Magic, or Rare).
   - Applies Cartographer's Chisels or Maven's Chisels (Rarity, Pack Size, Scarabs/Threads, Currency, Divination) up to target quality (default 20%).
2. **Upgrade Phase**:
   - Applies Orb of Alchemy (or Orb of Binding) to upgrade Normal maps to Rare.
   - If a map is Magic, automatically scours and alchemies to Rare.
3. **Evaluation & Reroll Phase**:
   - Evaluates map stats and affixes in microseconds directly from memory.
   - Supports **Chaos Spam** or **Scour + Alch** rerolling.
   - Loops until all criteria are met or maximum attempts / currency are reached.
4. **Exalt Slam Phase (Optional)**:
   - If enabled and the map meets all conditions with mod space remaining (< 6 mods), applies an Exalted Orb.
   - **Re-evaluation Safeguard**: If an Exalted Orb rolls a banned or invalid modifier, MapCrafter detects it immediately and resumes rerolling!
5. **Vaal Phase (Optional)**:
   - **On Craft Success**: Applies a Vaal Orb once a map satisfies all rules.
   - **All Inventory Maps**: Corrupts all eligible maps currently in the player inventory.

### 2. Powerful Rule Engine & In-Game Mod Browser
- **Current Game Mod Database (224 Mods)**:
  - Bundled with the active database of all 224 map modifiers (81 T1-T16 Generic mods, 28 T17/Uber mods, 13 Expedition logbook mods, and Valdo puzzle mods).
  - Search any mod by keyword in the HUD and add it directly with **`+ Ban`** or **`+ Req`**.
- **Up-to-Date Presets**:
  - **T1-T16 Lethal Presets**: Current wording for Reflect Phys/Ele, Cannot Leech, Cannot Regenerate, -Max Resists, Less Recovery, Hexproof, Ground Effects.
  - **T17 / Uber Lethal Presets**: Meteor on Flask, Bloodstained Sawblades, Drowning Orbs, Searing Exarch Runes, Unstable Tentacles, Volatile Cores, Less Defences, Less Flask Effect, Enemy Aura Sharing.
- **Banned / Forbidden Mods**: Immediate rejection and reroll if any banned affix is detected. Supports `#` wildcard placeholders (e.g., `reflect #% of Elemental Damage`).
- **Required / Desired Mods**: Must contain specific desired modifiers.
- **Stat Thresholds**:
  - Min Quantity (Atlas Drops) %
  - Min Pack Size %
  - Currency stat limit (e.g., `Currency < 50` or `Min Currency >= 100` for T17 maps)
  - Min Scarabs / Threads %
- **PoE Regex Support**: Paste regex directly from [poe.re](https://poe.re/maps) (supports positive tokens and `!` negated tokens).

### 3. Visual Inventory Grid & Controls
- **12×5 Interactive Inventory HUD**: Color-coded view of every slot (Blue = Currency, Orange = Pending Map, Yellow = In Progress, Green = Succeeded, Red = Failed, Purple = Ignored).
- **Stash & Bag Integration**: Automatically crafts maps in player inventory using currency from an open Stash tab (standard Currency tab) or inventory bag.
- **Slot Ignore**: Click any slot to toggle it as Ignored (e.g., keep portal scrolls, wisdom scrolls, or valuable maps safe).
- **Audio Completion Alert**: Plays an in-game notification sound and audible two-tone chime upon finishing all maps.
- **Safety Parking**: Automatically parks the cursor in neutral space when crafting stops so maps are never accidentally picked up.

---

## How to Use

1. Launch ExileApi HUD (`Loader.exe`).
2. Press **F12** to open the ExileApi Main Menu and verify **MapCrafter** is enabled under the Plugins tab.
3. Open your in-game Stash to your Currency tab (and inventory `I`).
4. Place the maps you wish to craft in your player inventory.
5. In the **MapCrafter Control Panel** window:
   - Select your desired profile (e.g., *Test Profile*, *T16 Standard*, or your own custom profile).
   - Tweak your target quality, reroll method, rules, and max attempts.
6. Press **F8** (or click **START CRAFTING**) to begin!
7. Press **F8** (or **Escape**) at any time to pause or emergency stop.
