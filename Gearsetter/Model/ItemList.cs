using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Gearsetter.GameData;
using LLib.GameData;
using LLib.Gear;

namespace Gearsetter.Model;

internal sealed class ItemList
{
    private static readonly ReadOnlyDictionary<uint, byte> PreferredItems = new Dictionary<uint, byte>()
    {
        { 16039, 50 },
        { 24589, 70 },
        { 33648, 80 },
        { 41081, 90 },
        { 44410, 60 },
        { 52439, 100 }, // Halone's Earring: EXP bonus through level 99
    }.AsReadOnly();

    public required EClassJob ClassJob { get; init; }
    public required EEquipSlotCategory EquipSlotCategory { get; init; }
    public required uint ItemUiCategory { get; init; }
    public required List<BaseItem> Items { get; set; }
    public EBaseParam PrimaryStat { get; set; }
    public IReadOnlyList<EBaseParam> SubstatPriorities { get; private set; } = new List<EBaseParam>();

    public void Sort()
    {
        Items = Items
            .OrderDescending(new ItemComparer(SubstatPriorities))
            .ToList();
    }

    public void UpdateStats(Dictionary<EClassJob, EBaseParam> primaryStats, Configuration configuration)
    {
        if (ClassJob.IsTank())
            SubstatPriorities = configuration.StatPriorityTanks;
        else if (ClassJob.IsHealer())
            SubstatPriorities = configuration.StatPriorityHealer;
        else if (ClassJob.IsMelee())
            SubstatPriorities = configuration.StatPriorityMelee;
        else if (ClassJob.IsPhysicalRanged())
            SubstatPriorities = configuration.StatPriorityPhysicalRanged;
        else if (ClassJob.IsCaster())
            SubstatPriorities = configuration.StatPriorityCaster;
        else if (ClassJob.IsCrafter())
            SubstatPriorities = configuration.StatPriorityCrafter;
        else if (ClassJob.IsGatherer())
            SubstatPriorities = configuration.StatPriorityGatherer;
        else
            SubstatPriorities = [];

        if (primaryStats.TryGetValue(ClassJob, out EBaseParam primaryStat))
        {
            PrimaryStat = primaryStat;
            Items = Items
                .Where(x => x is EquipmentItem)
                .Cast<EquipmentItem>()
                .Select(x => x with { PrimaryStat = x.Stats.Get(primaryStat) })
                .Cast<BaseItem>()
                .ToList();
        }
    }

    public void ApplyFromInventory(Dictionary<(uint ItemId, bool Hq), List<EquipmentStats>> inventoryItems,
        bool includeWithoutMateria)
    {
        foreach (var inventoryItem in inventoryItems)
        {
            var basicItem = Items.SingleOrDefault(x =>
                x.ItemId == inventoryItem.Key.ItemId && x.Hq == inventoryItem.Key.Hq);
            if (basicItem == null)
                continue;

            foreach (var inventoryStats in inventoryItem.Value)
            {
                if (includeWithoutMateria || inventoryStats.HasMateria())
                    Items.Add(
                        new InventoryItem(basicItem.Item, basicItem.Hq, inventoryStats, basicItem.ClassJob, basicItem.SourceIds)
                        {
                            PrimaryStat = basicItem.Stats.Get(PrimaryStat)
                        });
            }
        }

        Sort();
    }

    public void ClearFromInventory()
    {
        Items.RemoveAll(x => x is InventoryItem);
    }

    private sealed class ItemComparer(IReadOnlyList<EBaseParam> substatPriorities) : IComparer<BaseItem>
    {
        public int Compare(BaseItem? a, BaseItem? b)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);

            // weapons: most damage wins
            int damageA = a.Damage;
            int damageB = b.Damage;
            if (damageA != damageB)
                return damageA.CompareTo(damageB);

            bool hasPriorityA = TryGetPreferredItemPriority(a, b, out byte priorityA);
            bool hasPriorityB = TryGetPreferredItemPriority(b, a, out byte priorityB);
            if ((hasPriorityA || hasPriorityB) && priorityA != priorityB)
                return priorityA.CompareTo(priorityB);

            // gear: primary stat wins
            //
            // we pretend every gear item has at least 1 primary stat to ensure weathered items are sorted last(ish),
            // where they would otherwise get sorted as better-than-shire items (while that may be correct, it's also
            // stupid)
            int primaryStatA = Math.Max(1, a.PrimaryStat);
            int primaryStatB = Math.Max(1, b.PrimaryStat);
            if (primaryStatA != primaryStatB)
                return primaryStatA.CompareTo(primaryStatB);

            // gear: vitality wins
            int vitalityA = a.Stats.Get(EBaseParam.Vitality);
            int vitalityB = b.Stats.Get(EBaseParam.Vitality);
            if (vitalityA != vitalityB)
                return vitalityA.CompareTo(vitalityB);

            // sum of relevant substats
            int sumOfSubstatsA = substatPriorities.Sum(x => a.Stats.Get(x));
            int sumOfSubstatsB = substatPriorities.Sum(x => b.Stats.Get(x));

            // some relics have no substats in the sheets, since they can be allocated dynamically
            // they are -generally- better/equal to any other weapon on that ilvl
            if (sumOfSubstatsA == 0 && a.IsCombatRelicWithoutSubstats())
                sumOfSubstatsA = int.MaxValue;
            if (sumOfSubstatsB == 0 && b.IsCombatRelicWithoutSubstats())
                sumOfSubstatsB = int.MaxValue;

            if (sumOfSubstatsA != sumOfSubstatsB)
                return sumOfSubstatsA.CompareTo(sumOfSubstatsB);

            // level-based sorting
            if (a.Level != b.Level)
                return a.Level.CompareTo(b.Level);
            if (a.ItemLevel != b.ItemLevel)
                return a.ItemLevel.CompareTo(b.ItemLevel);
            if (a.Rarity != b.Rarity)
            {
                // aetherial items aren't "special" enough to be sorted higher than normal gear
                int rarityA = a.Rarity;
                int rarityB = b.Rarity;

                if (rarityA == 7)
                    rarityA = 1;

                if (rarityB == 7)
                    rarityB = 1;

                return rarityA.CompareTo(rarityB);
            }

            // individual substats
            foreach (EBaseParam substat in substatPriorities)
            {
                int substatA = a.Stats.Get(substat);
                int substatB = b.Stats.Get(substat);
                if (substatA != substatB)
                    return substatA.CompareTo(substatB);
            }

            // fallback
            return string.CompareOrdinal(a.Name, b.Name);
        }

        private static bool TryGetPreferredItemPriority(BaseItem self, BaseItem other, out byte priority)
        {
            if (PreferredItems.TryGetValue(self.ItemId, out byte levelSelf))
            {
                // both items are preferred, sort by level only
                if (PreferredItems.TryGetValue(other.ItemId, out byte _))
                {
                    priority = levelSelf;
                    return true;
                }

                // if they're the same level, place the preferred item last
                if (levelSelf == other.Level)
                {
                    priority = (byte)(levelSelf - 1);
                    return true;
                }

                priority = levelSelf;
                return true;
            }
            else
            {
                priority = self.Level;
                return false;
            }
        }
    }
}
