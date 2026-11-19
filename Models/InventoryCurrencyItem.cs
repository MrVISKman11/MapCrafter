using System.Numerics;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;

namespace MapCrafter.Models
{
    public class InventoryCurrencyItem
    {
        public CurrencyKind Kind { get; set; }
        public NormalInventoryItem ItemWrapper { get; set; } = null!;
        public Entity Entity { get; set; } = null!;
        public Vector2 ClickPosition { get; set; }
        public int SlotX { get; set; }
        public int SlotY { get; set; }
        public int StackCount { get; set; }
        public string BaseName { get; set; } = string.Empty;
        public bool IsInStash { get; set; }
        public string LocationDescription => IsInStash ? "Stash" : $"Inv ({SlotX},{SlotY})";
    }
}
