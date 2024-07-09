﻿using CombatOverhaul.DamageSystems;
using CombatOverhaul.Utils;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;
using static OpenTK.Graphics.OpenGL.GL;

namespace CombatOverhaul.Armor;

[Flags]
public enum ArmorLayers
{
    None = 0,
    Skin = 1,
    Middle = 2,
    Outer = 4
}

public sealed class ArmorTypeJson
{
    public string[] Layers { get; set; } = Array.Empty<string>();
    public string[] Slots { get; set; } = Array.Empty<string>();

    public ArmorType ToArmorType()
    {
        return new(
            Layers.Select(Enum.Parse<ArmorLayers>).Aggregate((first, second) => first | second),
            Slots.Select(Enum.Parse<DamageZone>).Aggregate((first, second) => first | second)
            );
    }
}

public readonly struct ArmorType
{
    public readonly ArmorLayers Layers;
    public readonly DamageZone Slots;

    public ArmorType(ArmorLayers layers, DamageZone slots)
    {
        Layers = layers;
        Slots = slots;
    }

    public bool Intersect(ArmorLayers layer, DamageZone slot) => (Layers & layer) != 0 && (Slots & slot) != 0;
    public bool Intersect(ArmorType type) => (Layers & type.Layers) != 0 && (Slots & type.Slots) != 0;

    public static ArmorType Combine(ArmorType first, ArmorType second) => new(first.Layers | second.Layers, first.Slots | second.Slots);
    public static ArmorType Combine(IEnumerable<ArmorType> armorTypes) => armorTypes.Aggregate(Combine);
    public static ArmorType Empty => new ArmorType(ArmorLayers.None, DamageZone.None);

    public override string ToString()
    {
        ArmorLayers layersValue = Layers;
        string layers = Enum.GetValues<ArmorLayers>().Where(value => (value | layersValue) != 0).Select(value => value.ToString()).Aggregate((first, second) => $"{first}, {second}");

        DamageZone slotsValue = Slots;
        string slots = Enum.GetValues<DamageZone>().Where(value => (value | slotsValue) != 0).Select(value => value.ToString()).Aggregate((first, second) => $"{first}, {second}");

        return $"({layers}|{slots})";
    }
}

public interface IArmor
{
    public ArmorType ArmorType { get; }
    public DamageResistData Resists { get; }
}

public sealed class ArmorStatsJson
{
    public string[] Layers { get; set; } = Array.Empty<string>();
    public string[] Slots { get; set; } = Array.Empty<string>();
    public Dictionary<string, float> Resists { get; set; } = new();
}

public class ArmorBehavior : CollectibleBehavior, IArmor
{
    public ArmorBehavior(CollectibleObject collObj) : base(collObj)
    {
    }

    public ArmorType ArmorType { get; protected set; } = new(ArmorLayers.None, DamageZone.None);
    public DamageResistData Resists { get; protected set; } = new(new Dictionary<EnumDamageType, float>());

    public override void Initialize(JsonObject properties)
    {
        if (!properties.KeyExists("stats"))
        {
            return;
        }

        ArmorStatsJson stats = properties["stats"].AsObject<ArmorStatsJson>();

        ArmorType = new(stats.Layers.Select(Enum.Parse<ArmorLayers>).Aggregate((first, second) => first | second), stats.Layers.Select(Enum.Parse<DamageZone>).Aggregate((first, second) => first | second));
        Resists = new(stats.Resists.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value));
    }
}

public class ArmorItem : Item, IArmor
{
    public ArmorType ArmorType { get; protected set; } = new(ArmorLayers.None, DamageZone.None);
    public DamageResistData Resists { get; protected set; } = new(new Dictionary<EnumDamageType, float>());

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (!Attributes.KeyExists("stats"))
        {
            LoggerUtil.Error(api, this, $"Armor item '{Code}' does not have stats attribute");
            return;
        }

        ArmorStatsJson stats = Attributes["stats"].AsObject<ArmorStatsJson>();

        ArmorType = new(stats.Layers.Select(Enum.Parse<ArmorLayers>).Aggregate((first, second) => first | second), stats.Layers.Select(Enum.Parse<DamageZone>).Aggregate((first, second) => first | second));
        Resists = new(stats.Resists.ToDictionary(entry => Enum.Parse<EnumDamageType>(entry.Key), entry => entry.Value));
    }
}