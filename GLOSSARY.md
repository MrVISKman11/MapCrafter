# MapCrafter Domain Model

Domain language and game mechanics for automated map crafting in Path of Exile (PoE 3.30+).

## Language

### Map Yield & Scaling Modifiers

**Atlas Drops**:
The explicit map affix scaling drop rate for Atlas base items (maps, scarabs/threads, currency from atlas mechanics). Derived from the `map_drop_scaling` stat key on explicit mods.
_Avoid_: Item Quantity, Map Quantity

**Item Quantity**:
The map-level quantity scaling derived from map Quality via Cartographer's Chisels (`Quality (Quantity): +#%`), plus any explicit item quantity mods. Distinct from Atlas Drops.
_Avoid_: Atlas Drops, Quantity (unqualified)

**Quality**:
The base upgrade applied via Cartographer's Chisels (up to +20%) to Normal maps before rolling affixes. Directly determines the base Item Quantity bonus on the map.
_Avoid_: Chisel tier, Map grade

**Item Rarity**:
The explicit map affix increasing the chance of dropped items having higher rarity tiers. Derived from `map_item_drop_rarity` and `map_total_item_rarity`.
_Avoid_: Rarity bonus, Map magic find

**Monster Pack Size**:
The percentage increase in the number of monsters spawned in map packs. Derived from `map_pack_size`, `map_monster_pack_size`, and `map_total_pack_size`.
_Avoid_: Density, Mob count

**More Maps**:
The percentage chance increase for additional map drops from monsters in the map. Derived from `map_item_drop_chance` and `map_map_item_drop_chance`.
_Avoid_: Map drop chance, Map sustain bonus

**More Currency**:
The percentage chance increase for additional currency item drops. Found on high-tier and Originator/T17 maps via `map_currency_drop_chance`.
_Avoid_: Currency multiplier, Currency quant

**More Threads**:
The percentage chance increase for additional Thread drops (the 3.30 replacement for Scarabs). Derived from the internal stat key `map_scarab_drop_chance`.
_Avoid_: More Scarabs, Scarab chance

## Architecture & Multi-Profile Crafting

**Global Build Banned Mods**:
The set of map affixes that break or brick a character build (e.g. Elemental Reflect, Cannot Regenerate, Cannot Leech, Less Flask Effect). Evaluated as a mandatory Pass-1 hard filter before any target farming profiles are tested.
_Avoid_: Blacklist, Profile bans (when referring to universal brickers)

**Composite Profile Evaluation**:
Evaluating rolled maps against multiple active crafting profiles in a disjunctive (Match ANY / OR) manner. The map stops rerolling as soon as any active profile is satisfied.
_Avoid_: Multi-threading, Batch crafting (when referring to rule matching)

**Originator / T16.5 Memory Influence**:
The modifier `Area is Influenced by the Originator's Memories` native to T16.5 maps. This is an implicit modifier and is always present on Originator maps. It does not occupy an explicit prefix or suffix slot, does not count towards the 6-affix map ceiling, and does not block Exalt slamming.
_Avoid_: Explicit affix, Memory prefix

**Profile Validation (Empty Criteria Guard)**:
A profile with no required mods, no custom regex, and all minimum stat thresholds at 0 is treated as unconfigured. It is blocked from matching any map to prevent premature settlement on unrolled maps.
_Avoid_: Auto-pass, Default match

