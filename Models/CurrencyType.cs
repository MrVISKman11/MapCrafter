namespace MapCrafter.Models
{
    public enum CurrencyKind
    {
        Unknown = 0,
        ChiselNormal,
        Alchemy,
        Binding,
        Scouring,
        Chaos,
        Exalted,
        Vaal,
        Transmutation,
        Alteration,
        Augmentation
    }

    public enum RerollMethod
    {
        ChaosOrb = 0,
        ScourAlch = 1
    }

    public enum VaalMode
    {
        Never = 0,
        OnCraftSuccess = 1,
        AllInventoryMaps = 2
    }
}
