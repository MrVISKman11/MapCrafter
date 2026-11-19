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
