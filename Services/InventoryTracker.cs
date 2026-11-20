using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;
using MapCrafter.Models;

namespace MapCrafter.Services
{
    public class InventoryTracker
    {
        private readonly object _lock = new();
        private readonly List<ParsedMapItem> _maps = new();
        private readonly Dictionary<CurrencyKind, List<InventoryCurrencyItem>> _currencies = new();
        private readonly Dictionary<CurrencyKind, Vector2> _lockedStackPositions = new();

        public void ResetLockedCurrencies()
        {
            lock (_lock)
            {
                _lockedStackPositions.Clear();
            }
        }

        /// <summary>
        /// Returns a thread-safe snapshot copy of tracked maps.
        /// </summary>
        public List<ParsedMapItem> Maps
        {
            get
            {
                lock (_lock)
                {
                    return _maps.ToList();
                }
            }
        }

        /// <summary>
        /// Returns a thread-safe snapshot copy of tracked currencies.
        /// </summary>
        public Dictionary<CurrencyKind, List<InventoryCurrencyItem>> Currencies
        {
            get
            {
                lock (_lock)
                {
                    return _currencies.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
                }
            }
        }

        public int MapCount
        {
            get
            {
                lock (_lock)
                {
                    return _maps.Count;
                }
            }
        }

        public string GetCurrencySummary()
        {
            lock (_lock)
            {
                if (_currencies.Count == 0)
                    return "None detected (open Stash Tab with currency or place in bag)";

                int stashCount = _currencies.Values.SelectMany(l => l).Count(c => c.IsInStash);
                int invCount = _currencies.Values.SelectMany(l => l).Count(c => !c.IsInStash);

                var parts = new List<string>();
                foreach (var kvp in _currencies)
                {
                    int total = kvp.Value.Sum(c => c.StackCount);
                    if (total > 0)
                    {
                        string name = kvp.Key switch
                        {
                            CurrencyKind.Chaos => "Chaos",
                            CurrencyKind.Scouring => "Scour",
                            CurrencyKind.Alchemy => "Alch",
                            CurrencyKind.Binding => "Binding",
                            CurrencyKind.ChiselNormal => "Chisel",
                            CurrencyKind.Exalted => "Exalt",
                            CurrencyKind.Vaal => "Vaal",
                            _ => kvp.Key.ToString()
                        };
                        parts.Add($"{name} x{total}");
                    }
                }

                string sourceTag = stashCount > 0 ? (invCount > 0 ? "[Stash + Bag]" : "[Stash]") : "[Bag]";
                return parts.Count > 0 ? $"{sourceTag} {string.Join(", ", parts)}" : "None detected";
            }
        }

        /// <summary>
        /// Safely retrieves visible items using indexer to prevent 'Collection was modified' exceptions from background memory updates.
        /// </summary>
        public static List<NormalInventoryItem> SafeGetItems(IList<NormalInventoryItem>? items)
        {
            if (items == null) return new List<NormalInventoryItem>();
            try
            {
                int count = items.Count;
                var list = new List<NormalInventoryItem>(count);
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var it = items[i];
                        if (it != null) list.Add(it);
                    }
                    catch
                    {
                        break;
                    }
                }
                return list;
            }
            catch
            {
                return new List<NormalInventoryItem>();
            }
        }

        private void ScanCurrencyList(GameController gc, IEnumerable<NormalInventoryItem>? items, Dictionary<CurrencyKind, List<InventoryCurrencyItem>> target, bool isStash, HashSet<(int x, int y)>? ignoredSlots = null)
        {
            if (items == null) return;

            foreach (var item in items)
            {
                if (item == null) continue;
                var entity = item.Item ?? item.Entity;
                if (entity == null) continue;

                int slotX = (int)item.InventPosX;
                int slotY = (int)item.InventPosY;

                if (!isStash && ignoredSlots != null && ignoredSlots.Contains((slotX, slotY)))
                    continue;

                string path = entity.Path ?? string.Empty;
                var baseItem = gc.Files?.BaseItemTypes?.Translate(path);
                string baseName = baseItem?.BaseName ?? entity.GetComponent<Base>()?.Name ?? string.Empty;
                string className = baseItem?.ClassName ?? string.Empty;

                var currencyKind = ClassifyCurrency(path, baseName, className);
                if (currencyKind != CurrencyKind.Unknown)
                {
                    var rect = item.GetClientRect();
                    if (rect.Width <= 0 || rect.Height <= 0) continue;

                    var windowRect = gc.Window.GetWindowRectangle();
                    var absCenter = new Vector2(windowRect.X + rect.Center.X, windowRect.Y + rect.Center.Y);
                    int stackCount = entity.GetComponent<Stack>()?.Size ?? 1;

                    var curr = new InventoryCurrencyItem
                    {
                        Kind = currencyKind,
                        ItemWrapper = item,
                        Entity = entity,
                        ClickPosition = absCenter,
                        SlotX = slotX,
                        SlotY = slotY,
                        StackCount = stackCount,
                        BaseName = baseName,
                        IsInStash = isStash
                    };

                    if (!target.TryGetValue(currencyKind, out var list))
                    {
                        list = new List<InventoryCurrencyItem>();
                        target[currencyKind] = list;
                    }
                    list.Add(curr);
                }
            }
        }

        /// <summary>
        /// Scans player inventory for maps, and scans both open Stash Tab and player inventory for currencies.
        /// Stash currency is prioritized over player inventory currency.
        /// </summary>
        public void ScanInventory(GameController gc, HashSet<(int x, int y)>? ignoredSlots = null)
        {
            var invPanel = gc.IngameState?.IngameUi?.InventoryPanel;
            if (invPanel == null || !invPanel.IsVisible)
                return;

            var playerInv = invPanel[InventoryIndex.PlayerInventory];
            if (playerInv == null)
                return;

            var items = SafeGetItems(playerInv.VisibleInventoryItems);
            if (items.Count == 0)
                return;

            var newMaps = new List<ParsedMapItem>();
            var newCurrencies = new Dictionary<CurrencyKind, List<InventoryCurrencyItem>>();

            // 1. Scan open Stash Tab for currencies (if Stash is open)
            var stashEl = gc.IngameState?.IngameUi?.StashElement;
            if (stashEl != null && stashEl.IsVisible)
            {
                try
                {
                    var stashItems = SafeGetItems(stashEl.VisibleStash?.VisibleInventoryItems);
                    ScanCurrencyList(gc, stashItems, newCurrencies, isStash: true);
                }
                catch (Exception ex)
                {
                    CraftLogger.Warn("SCAN", $"Could not read stash items: {ex.Message}");
                }
            }

            // 2. Scan Player Inventory for maps and currencies
            var invRect = playerInv.GetClientRect();
            float cellW = invRect.Width / 12f;
            float cellH = invRect.Height / 5f;

            foreach (var item in items)
            {
                if (item == null) continue;
                var entity = item.Item ?? item.Entity;
                if (entity == null) continue;

                var rect = item.GetClientRect();
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                // Derive true 12x5 grid slot geometrically from screen bounds (InventPosX/Y is broken in PoE 3.30 offsets)
                int slotX = (cellW > 10f)
                    ? Math.Clamp((int)((rect.Center.X - invRect.Left) / cellW), 0, 11)
                    : (int)item.InventPosX;
                int slotY = (cellH > 10f)
                    ? Math.Clamp((int)((rect.Center.Y - invRect.Top) / cellH), 0, 4)
                    : (int)item.InventPosY;

                if (ignoredSlots != null && ignoredSlots.Contains((slotX, slotY)))
                    continue;

                string path = entity.Path ?? string.Empty;
                var baseItem = gc.Files?.BaseItemTypes?.Translate(path);
                string baseName = baseItem?.BaseName ?? entity.GetComponent<Base>()?.Name ?? string.Empty;
                string className = baseItem?.ClassName ?? string.Empty;

                var currencyKind = ClassifyCurrency(path, baseName, className);
                if (currencyKind != CurrencyKind.Unknown)
                {
                    var windowRect = gc.Window.GetWindowRectangle();
                    var absCenter = new Vector2(windowRect.X + rect.Center.X, windowRect.Y + rect.Center.Y);
                    int stackCount = entity.GetComponent<Stack>()?.Size ?? 1;

                    var curr = new InventoryCurrencyItem
                    {
                        Kind = currencyKind,
                        ItemWrapper = item,
                        Entity = entity,
                        ClickPosition = absCenter,
                        SlotX = slotX,
                        SlotY = slotY,
                        StackCount = stackCount,
                        BaseName = baseName,
                        IsInStash = false
                    };

                    if (!newCurrencies.TryGetValue(currencyKind, out var list))
                    {
                        list = new List<InventoryCurrencyItem>();
                        newCurrencies[currencyKind] = list;
                    }
                    list.Add(curr);
                    continue;
                }

                // Check if map
                var parsedMap = ItemParser.ParseMap(gc, item, slotX, slotY);
                if (parsedMap != null)
                {
                    newMaps.Add(parsedMap);
                }
            }

            // Preserve existing status / attempts if already tracking this slot
            Dictionary<(int, int), ParsedMapItem> existingMapStatus;
            lock (_lock)
            {
                existingMapStatus = new Dictionary<(int, int), ParsedMapItem>();
                foreach (var m in _maps)
                {
                    existingMapStatus[(m.SlotX, m.SlotY)] = m;
                }
            }

            foreach (var map in newMaps)
            {
                if (existingMapStatus.TryGetValue((map.SlotX, map.SlotY), out var existing))
                {
                    map.Status = existing.Status;
                    map.StatusDetails = existing.StatusDetails;
                    map.AttemptsUsed = existing.AttemptsUsed;
                    map.ExaltsUsed = existing.ExaltsUsed;
                }
            }

            // Order maps row by row (Y then X)
            newMaps.Sort((a, b) => a.SlotY != b.SlotY ? a.SlotY.CompareTo(b.SlotY) : a.SlotX.CompareTo(b.SlotX));

            lock (_lock)
            {
                _maps.Clear();
                _maps.AddRange(newMaps);

                _currencies.Clear();
                foreach (var kvp in newCurrencies)
                {
                    _currencies[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// Fast scan that refreshes currency stacks from open Stash Tab and player inventory without touching maps.
        /// </summary>
        public void ScanCurrenciesOnly(GameController gc, HashSet<(int x, int y)>? ignoredSlots = null)
        {
            var newCurrencies = new Dictionary<CurrencyKind, List<InventoryCurrencyItem>>();
            bool stashVisible = false;
            int stashItemCount = 0;

            // 1. Scan open Stash Tab (Priority)
            var stashEl = gc.IngameState?.IngameUi?.StashElement;
            if (stashEl != null && stashEl.IsVisible)
            {
                stashVisible = true;
                try
                {
                    var stashItems = SafeGetItems(stashEl.VisibleStash?.VisibleInventoryItems);
                    stashItemCount = stashItems.Count;
                    if (stashItemCount > 0)
                    {
                        ScanCurrencyList(gc, stashItems, newCurrencies, isStash: true);
                    }
                }
                catch (Exception ex)
                {
                    CraftLogger.Warn("SCAN", $"Could not read stash currency items: {ex.Message}");
                }
            }

            // 2. Scan Player Inventory
            var invPanel = gc.IngameState?.IngameUi?.InventoryPanel;
            if (invPanel != null && invPanel.IsVisible)
            {
                var playerInv = invPanel[InventoryIndex.PlayerInventory];
                var invItems = SafeGetItems(playerInv?.VisibleInventoryItems);
                ScanCurrencyList(gc, invItems, newCurrencies, isStash: false, ignoredSlots);
            }

            lock (_lock)
            {
                // Transient frame protection: If stash was visible but returned 0 items,
                // do NOT wipe existing stash currencies if we previously had them!
                if (stashVisible && stashItemCount == 0 && _currencies.Values.Any(l => l.Any(c => c.IsInStash)))
                {
                    CraftLogger.Debug("SCAN", "Stash returned 0 items mid-sync; preserving previous stash currencies.");
                    // Only update inventory currencies if any, keep stash currencies
                    foreach (var kvp in newCurrencies.Where(k => k.Value.Any(c => !c.IsInStash)))
                    {
                        if (_currencies.TryGetValue(kvp.Key, out var existingList))
                        {
                            existingList.RemoveAll(c => !c.IsInStash);
                            existingList.AddRange(kvp.Value.Where(c => !c.IsInStash));
                        }
                    }
                    return;
                }

                _currencies.Clear();
                foreach (var kvp in newCurrencies)
                {
                    _currencies[kvp.Key] = kvp.Value;
                }
            }
        }

        public async Task<InventoryCurrencyItem?> GetAvailableCurrencyWithRetry(GameController gc, CurrencyKind kind, HashSet<(int x, int y)>? ignoredSlots = null, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                var item = GetAvailableCurrency(kind);
                if (item != null) return item;

                if (attempt < maxRetries)
                {
                    CraftLogger.Debug("CURRENCY", $"Currency {kind} not found on attempt #{attempt}, re-scanning in 100ms...");
                    await Task.Delay(100);
                    ScanCurrenciesOnly(gc, ignoredSlots);
                }
            }
            return null;
        }

        public InventoryCurrencyItem? GetAvailableCurrency(CurrencyKind kind)
        {
            lock (_lock)
            {
                var cur = GetAvailableCurrencyInternal(kind);
                if (cur != null) return cur;

                // Fallback for Alchemy -> Binding if Alchemy is missing
                if (kind == CurrencyKind.Alchemy)
                {
                    return GetAvailableCurrencyInternal(CurrencyKind.Binding);
                }

                return null;
            }
        }

        private InventoryCurrencyItem? GetAvailableCurrencyInternal(CurrencyKind kind)
        {
            if (!_currencies.TryGetValue(kind, out var list) || list.Count == 0)
                return null;

            // 1. Check if we already have a locked stack position for this currency
            if (_lockedStackPositions.TryGetValue(kind, out var lockedPos))
            {
                // Find matching item near the locked position
                var existing = list.FirstOrDefault(c => Vector2.DistanceSquared(c.ClickPosition, lockedPos) < 400f);
                if (existing != null && existing.StackCount > 0)
                {
                    return existing;
                }
                // Locked stack was exhausted or moved
                _lockedStackPositions.Remove(kind);
            }

            // 2. Select new stack: Stash first, dedicated central slots first (lower Y), then highest stack
            var stashItems = list.Where(c => c.IsInStash && c.StackCount > 0).ToList();
            if (stashItems.Count > 0)
            {
                // In Currency Tab: dedicated slots have lower Y than wildcard bottom slots.
                // Sort by Y ascending (dedicated slots first), then stack count descending
                var chosen = stashItems.OrderBy(c => c.ClickPosition.Y).ThenByDescending(c => c.StackCount).First();
                _lockedStackPositions[kind] = chosen.ClickPosition;
                CraftLogger.Info("CURRENCY", $"Locked onto {kind} stack at screen pos ({chosen.ClickPosition.X:F0}, {chosen.ClickPosition.Y:F0}) [Stack: {chosen.StackCount}, Stash: true]");
                return chosen;
            }

            // Fallback to player bag items
            var invItems = list.Where(c => !c.IsInStash && c.StackCount > 0).ToList();
            if (invItems.Count > 0)
            {
                var chosen = invItems.OrderByDescending(c => c.StackCount).First();
                _lockedStackPositions[kind] = chosen.ClickPosition;
                CraftLogger.Info("CURRENCY", $"Locked onto {kind} stack at screen pos ({chosen.ClickPosition.X:F0}, {chosen.ClickPosition.Y:F0}) [Stack: {chosen.StackCount}, Stash: false]");
                return chosen;
            }

            return null;
        }

        public static CurrencyKind ClassifyCurrency(string path, string baseName, string className)
        {
            if (!path.Contains("Metadata/Items/Currency", StringComparison.OrdinalIgnoreCase) && className != "StackableCurrency")
                return CurrencyKind.Unknown;

            // Strict exclusion: NEVER match shards or fragments as craftable orbs
            if (path.Contains("Shard", StringComparison.OrdinalIgnoreCase) || 
                baseName.Contains("Shard", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("Shard", StringComparison.OrdinalIgnoreCase))
            {
                return CurrencyKind.Unknown;
            }

            // Cartographer's Chisel
            if (path.Contains("CurrencyMapQuality", StringComparison.OrdinalIgnoreCase) || 
                path.Contains("CurrencyItemQuality", StringComparison.OrdinalIgnoreCase) || 
                baseName.Contains("Cartographer's Chisel", StringComparison.OrdinalIgnoreCase))
            {
                return CurrencyKind.ChiselNormal;
            }

            // Rarity Upgrades
            if (path.Contains("CurrencyUpgradeToRareEquipment", StringComparison.OrdinalIgnoreCase) || baseName.Contains("Orb of Binding", StringComparison.OrdinalIgnoreCase))
                return CurrencyKind.Binding;
            if (path.Contains("CurrencyUpgradeToRare", StringComparison.OrdinalIgnoreCase) || baseName.Contains("Orb of Alchemy", StringComparison.OrdinalIgnoreCase))
                return CurrencyKind.Alchemy;

            // Scour
            if (path.Contains("CurrencyConvertToNormal", StringComparison.OrdinalIgnoreCase) || baseName.Contains("Orb of Scouring", StringComparison.OrdinalIgnoreCase))
                return CurrencyKind.Scouring;

            // Chaos
            if (path.Contains("CurrencyRerollRare", StringComparison.OrdinalIgnoreCase) || baseName.Contains("Chaos Orb", StringComparison.OrdinalIgnoreCase))
                return CurrencyKind.Chaos;

            // Exalt: Standard Exalted Orb only (exclude Eldritch / Conqueror variants)
            if ((path.Contains("CurrencyAddModToRare", StringComparison.OrdinalIgnoreCase) || 
                 baseName.Contains("Exalted Orb", StringComparison.OrdinalIgnoreCase)) &&
                !path.Contains("Eldritch", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("Hunter", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("Crusader", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("Warlord", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("Redeemer", StringComparison.OrdinalIgnoreCase) &&
                !baseName.Contains("Eldritch", StringComparison.OrdinalIgnoreCase) &&
                !baseName.Contains("Hunter", StringComparison.OrdinalIgnoreCase) &&
                !baseName.Contains("Crusader", StringComparison.OrdinalIgnoreCase) &&
                !baseName.Contains("Warlord", StringComparison.OrdinalIgnoreCase) &&
                !baseName.Contains("Redeemer", StringComparison.OrdinalIgnoreCase))
            {
                return CurrencyKind.Exalted;
            }

            // Vaal
            if (path.Contains("CurrencyVaal", StringComparison.OrdinalIgnoreCase) || baseName.Contains("Vaal Orb", StringComparison.OrdinalIgnoreCase))
                return CurrencyKind.Vaal;

            // Transmute / Alt / Aug
            if (path.Contains("CurrencyUpgradeRandomly", StringComparison.OrdinalIgnoreCase) || baseName.Contains("Orb of Transmutation", StringComparison.OrdinalIgnoreCase))
                return CurrencyKind.Transmutation;
            if (path.Contains("CurrencyRerollMagic", StringComparison.OrdinalIgnoreCase) || baseName.Contains("Orb of Alteration", StringComparison.OrdinalIgnoreCase))
                return CurrencyKind.Alteration;
            if (path.Contains("CurrencyAddModToMagic", StringComparison.OrdinalIgnoreCase) || baseName.Contains("Orb of Augmentation", StringComparison.OrdinalIgnoreCase))
                return CurrencyKind.Augmentation;

            return CurrencyKind.Unknown;
        }

        /// <summary>
        /// Finds a guaranteed safe empty slot in the player inventory (or safe UI margin)
        /// where right-clicking will cancel/drop held currency without interacting with any item or UI button.
        /// </summary>
        public static Vector2 GetSafeEmptyInventoryPosition(GameController gc)
        {
            var invPanel = gc.IngameState?.IngameUi?.InventoryPanel;
            var windowRect = gc.Window.GetWindowRectangle();

            if (invPanel != null && invPanel.IsVisible)
            {
                var playerInv = invPanel[InventoryIndex.PlayerInventory];
                if (playerInv != null)
                {
                    var invRect = playerInv.GetClientRect();
                    float cellW = invRect.Width / 12f;
                    float cellH = invRect.Height / 5f;

                    var items = SafeGetItems(playerInv.VisibleInventoryItems);
                    var occupied = new HashSet<(int, int)>();
                    foreach (var item in items)
                    {
                        if (item == null) continue;
                        var r = item.GetClientRect();
                        if (r.Width <= 0 || r.Height <= 0) continue;
                        int sx = (cellW > 10f) ? Math.Clamp((int)((r.Center.X - invRect.Left) / cellW), 0, 11) : (int)item.InventPosX;
                        int sy = (cellH > 10f) ? Math.Clamp((int)((r.Center.Y - invRect.Top) / cellH), 0, 4) : (int)item.InventPosY;
                        occupied.Add((sx, sy));
                    }

                    // Search from bottom-right (11,4) backwards
                    for (int x = 11; x >= 1; x--)
                    {
                        for (int y = 4; y >= 0; y--)
                        {
                            if (!occupied.Contains((x, y)))
                            {
                                float cx = invRect.Left + (x + 0.5f) * cellW;
                                float cy = invRect.Top + (y + 0.5f) * cellH;
                                return new Vector2(windowRect.X + cx, windowRect.Y + cy);
                            }
                        }
                    }

                    // Margin at top right of inventory
                    return new Vector2(windowRect.X + invRect.Right - 20, windowRect.Y + invRect.Top - 15);
                }
            }

            // Safe neutral middle between stash and inventory
            return new Vector2(windowRect.X + (windowRect.Width * 0.5f), windowRect.Y + (windowRect.Height * 0.45f));
        }
    }
}
