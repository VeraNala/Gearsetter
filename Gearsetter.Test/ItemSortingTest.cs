using System;
using System.Collections.Generic;
using System.Linq;
using Gearsetter.GameData;
using Gearsetter.Model;
using LLib.GameData;
using LLib.Gear;
using Lumina.Excel.Sheets;
using Xunit;

namespace Gearsetter.Test;

public sealed class ItemSortingTest
{
    Lumina.GameData _lumina = new("C:/Program Files (x86)/steam/steamapps/common/FINAL FANTASY XIV Online/game/sqpack");

    [Fact]
    public void Test1()
    {
        var gearStatsCalculator = new GearStatsCalculator(_lumina.GetExcelSheet<ItemLevel>()!,
            _lumina.GetExcelSheet<ExtendedBaseParam>()!,
            _lumina.GetExcelSheet<Materia>()!,
            _lumina.GetExcelSheet<Item>()!);
        var items = _lumina.GetExcelSheet<Item>()!;
        List<uint> initialItemIds =
        [
            11851,
            11853,
            14447,
            15131,
            16039,
            16240,
            17436,
            25928,
            32558,
        ];


        var itemList = new ItemList
        {
            ClassJob = EClassJob.Marauder,
            EquipSlotCategory = EEquipSlotCategory.Ears,
            ItemUiCategory = 41,
            Items = initialItemIds.Select(rowId => new EquipmentItem(items.GetRow(rowId), false, gearStatsCalculator.CalculateGearStats(items.GetRow(rowId), false, []), null))
                .Cast<BaseItem>()
                .ToList(),
        };

        var primaryStats = _lumina.GetExcelSheet<ClassJob>()!
            .Where(x => x.RowId > 0 && Enum.IsDefined(typeof(EClassJob), x.RowId))
            .Where(x => x.PrimaryStat > 0)
            .ToDictionary(x => (EClassJob)x.RowId, x => (EBaseParam)x.PrimaryStat);

        itemList.UpdateStats(primaryStats, new Configuration());
        itemList.Sort();

        List<uint> expectedItems =
        [
            32558,
            25928,
            11851,
            17436,
            16240,
            14447,
            11853,
            16039,
            15131, // weathered earrings benefit from having primary stats
        ];
        Assert.Equal(expectedItems, itemList.Items.Select(x => x.ItemId).ToList());
    }

    [Fact]
    public void HalonesEarringBeatsEverythingBelowLevel100()
    {
        var gearStatsCalculator = new GearStatsCalculator(_lumina.GetExcelSheet<ItemLevel>()!,
            _lumina.GetExcelSheet<ExtendedBaseParam>()!,
            _lumina.GetExcelSheet<Materia>()!,
            _lumina.GetExcelSheet<Item>()!);
        var items = _lumina.GetExcelSheet<Item>()!;
        List<uint> initialItemIds =
        [
            41081, // Azeyma's Earrings (EXP bonus to 90)
            42509, // Black Star Ear Cuff of Fending (lv 98)
            42529, // Origenics Earrings of Fending (lv 99)
            49638, // Augmented Bygone Brass Earrings of Fending (lv 100)
            52439, // Halone's Earring (EXP bonus to 100)
        ];

        var itemList = new ItemList
        {
            ClassJob = EClassJob.Marauder,
            EquipSlotCategory = EEquipSlotCategory.Ears,
            ItemUiCategory = 41,
            Items = initialItemIds.Select(rowId => new EquipmentItem(items.GetRow(rowId), false, gearStatsCalculator.CalculateGearStats(items.GetRow(rowId), false, []), null))
                .Cast<BaseItem>()
                .ToList(),
        };

        var primaryStats = _lumina.GetExcelSheet<ClassJob>()!
            .Where(x => x.RowId > 0 && Enum.IsDefined(typeof(EClassJob), x.RowId))
            .Where(x => x.PrimaryStat > 0)
            .ToDictionary(x => (EClassJob)x.RowId, x => (EBaseParam)x.PrimaryStat);

        itemList.UpdateStats(primaryStats, new Configuration());
        itemList.Sort();

        List<uint> expectedItems =
        [
            49638, // only actual level 100 gear replaces it
            52439,
            42529,
            42509,
            41081,
        ];
        Assert.Equal(expectedItems, itemList.Items.Select(x => x.ItemId).ToList());
    }
}
