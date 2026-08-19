using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on the tables.c data and the sets.c predicates.
///
/// The predicates are a hand port rather than generated, so these tests carry
/// more weight than the table ones: they pin the asymmetries that look like
/// transcription slips but are not.
/// </summary>
public class SetsAndTablesTests
{
    private sealed record Item(byte TVal, uint Flags = 0, ushort Weight = 0) : IItemAttributes;

    // ------------------------------------------------------------ name tables

    [Fact]
    public void Colors_HasEveryEntry() =>
        Assert.Equal(49, GameTables.Colors.Length); // MAX_COLORS

    [Fact]
    public void NameTables_MatchTheirDeclaredSizes()
    {
        Assert.Equal(22, GameTables.Mushrooms.Length);   // MAX_MUSH
        Assert.Equal(25, GameTables.Woods.Length);       // MAX_WOODS
        Assert.Equal(25, GameTables.Metals.Length);      // MAX_METALS
        Assert.Equal(32, GameTables.Rocks.Length);       // MAX_ROCKS
        Assert.Equal(11, GameTables.Amulets.Length);     // MAX_AMULETS
        Assert.Equal(153, GameTables.Syllables.Length);  // MAX_SYLLABLES
    }

    /// <summary>
    /// tables.c marks the first three colours "Do not move the first three":
    /// magic_init() starts shuffling at index 3 so that slime mould juice,
    /// apple juice and water keep fixed appearances.
    /// </summary>
    [Fact]
    public void Colors_FirstThreeAreTheFixedOnes()
    {
        Assert.Equal("Icky Green", GameTables.Colors[0]);
        Assert.Equal("Light Brown", GameTables.Colors[1]);
        Assert.Equal("Clear", GameTables.Colors[2]);
    }

    /// <summary>
    /// The appearance tables are shuffled in place at game start, so they must
    /// not be frozen into something that cannot be reordered.
    /// </summary>
    [Fact]
    public void NameTables_AreMutableInPlace()
    {
        string original = GameTables.Woods[0];
        try
        {
            GameTables.Woods[0] = "Testwood";
            Assert.Equal("Testwood", GameTables.Woods[0]);
        }
        finally
        {
            GameTables.Woods[0] = original;
        }
    }

    // ------------------------------------------------------------ misc tables

    [Fact]
    public void Owners_MatchTheCSource()
    {
        Assert.Equal(18, GameTables.Owners.Length); // MAX_OWNERS

        OwnerType erick = GameTables.Owners[0];
        Assert.StartsWith("Erick the Honest", erick.Name, StringComparison.Ordinal);
        Assert.Equal(250, erick.MaxCost);
        Assert.Equal(175, erick.MaxInflate);
        Assert.Equal(108, erick.MinInflate);
        Assert.Equal(4, erick.HagglePercent);
        Assert.Equal(0, erick.OwnerRace);
        Assert.Equal(12, erick.InsultMax);

        OwnerType mauglin = GameTables.Owners[1];
        Assert.StartsWith("Mauglin the Grumpy", mauglin.Name, StringComparison.Ordinal);
        Assert.Equal(32000, mauglin.MaxCost);
        Assert.Equal(5, mauglin.OwnerRace); // Dwarf
    }

    /// <summary>
    /// Owners are stored in groups of one per store, so owner N serves store
    /// N % MAX_STORES. Three full groups fill the table.
    /// </summary>
    [Fact]
    public void Owners_AreGroupedOnePerStore()
    {
        Assert.Equal(0, GameTables.Owners.Length % StoreSets.StoreCount);
    }

    [Fact]
    public void RaceGoldAdjust_IsSquareAndFavoursYourOwnKind()
    {
        Assert.Equal(8, GameTables.RaceGoldAdjust.Length); // MAX_RACES
        Assert.All(GameTables.RaceGoldAdjust, row => Assert.Equal(8, row.Length));

        // Humans pay list price to humans and a premium to half-trolls.
        Assert.Equal(100, GameTables.RaceGoldAdjust[0][0]);
        Assert.Equal(125, GameTables.RaceGoldAdjust[0][7]);

        // Dwarves give their own kind the best rate in the table.
        Assert.Equal(95, GameTables.RaceGoldAdjust[5][5]);
        Assert.Equal(135, GameTables.RaceGoldAdjust[5][7]);
    }

    [Fact]
    public void BlowsTable_IsSevenBySixAndMonotonic()
    {
        Assert.Equal(7, GameTables.BlowsTable.Length);
        Assert.All(GameTables.BlowsTable, row => Assert.Equal(6, row.Length));

        // More strength never gives fewer blows at the same dexterity.
        for (int dex = 0; dex < 6; dex++)
        {
            for (int str = 1; str < 7; str++)
            {
                Assert.True(
                    GameTables.BlowsTable[str][dex] >= GameTables.BlowsTable[str - 1][dex],
                    $"blows fell going from strength band {str - 1} to {str} at dexterity {dex}");
            }
        }

        Assert.Equal(1, GameTables.BlowsTable[0][0]);
        Assert.Equal(4, GameTables.BlowsTable[6][5]);
    }

    /// <summary>
    /// Cross-table integrity: every stocking slot has to name a real object.
    /// A generator that shifted a column would show up here rather than as a
    /// crash the first time the player walked into a shop.
    /// </summary>
    [Fact]
    public void StoreChoice_OnlyReferencesRealObjects()
    {
        Assert.Equal(6, GameTables.StoreChoice.Length);      // MAX_STORES
        Assert.All(GameTables.StoreChoice, row => Assert.Equal(26, row.Length)); // STORE_CHOICES

        foreach (ushort[] row in GameTables.StoreChoice)
        {
            foreach (ushort index in row)
            {
                Assert.InRange(index, 0, GameTables.ObjectList.Length - 1);
            }
        }
    }

    /// <summary>
    /// Each store should only stock things it is willing to buy back.
    /// </summary>
    [Fact]
    public void StoreChoice_StocksOnlyWhatTheStoreBuys()
    {
        for (int store = 0; store < GameTables.StoreChoice.Length; store++)
        {
            foreach (ushort index in GameTables.StoreChoice[store])
            {
                TreasureType item = GameTables.ObjectList[index];
                Assert.True(
                    StoreSets.Buys(store, item.TVal),
                    $"store {store} stocks {item.Name} (tval {item.TVal}) but will not buy it");
            }
        }
    }

    // ------------------------------------------------------------ item sets

    /// <summary>
    /// Arrows burn, bolts do not - bolts are metal. Acid affects both. This
    /// asymmetry reads like a transcription slip and is not one.
    /// </summary>
    [Fact]
    public void Fire_BurnsArrowsButNotBolts()
    {
        Assert.True(ItemSets.IsFlammable(new Item(ItemCategory.Arrow)));
        Assert.False(ItemSets.IsFlammable(new Item(ItemCategory.Bolt)));

        Assert.True(ItemSets.AffectedByAcid(new Item(ItemCategory.Arrow)));
        Assert.True(ItemSets.AffectedByAcid(new Item(ItemCategory.Bolt)));
    }

    /// <summary>Fire destroys potions; acid leaves them alone.</summary>
    [Fact]
    public void Potions_BurnAndFreezeButDoNotDissolve()
    {
        var potion = new Item(ItemCategory.Potion1);

        Assert.True(ItemSets.DestroyedByFire(potion));
        Assert.True(ItemSets.DestroyedByFrost(potion));
        Assert.False(ItemSets.DestroyedByAcid(potion));
    }

    [Theory]
    [InlineData(ItemCategory.Arrow)]
    [InlineData(ItemCategory.Bow)]
    [InlineData(ItemCategory.SoftArmor)]
    public void ResistFire_ProtectsTheItem(byte tval)
    {
        Assert.True(ItemSets.IsFlammable(new Item(tval)));
        Assert.False(ItemSets.IsFlammable(new Item(tval, ItemFlags.ResistFire)));

        Assert.True(ItemSets.DestroyedByFire(new Item(tval)));
        Assert.False(ItemSets.DestroyedByFire(new Item(tval, ItemFlags.ResistFire)));
    }

    [Fact]
    public void ResistAcid_ProtectsTheItem()
    {
        var plain = new Item(ItemCategory.HardArmor);
        var resistant = new Item(ItemCategory.HardArmor, ItemFlags.ResistAcid);

        Assert.True(ItemSets.DestroyedByAcid(plain));
        Assert.False(ItemSets.DestroyedByAcid(resistant));
    }

    /// <summary>
    /// Resistance only guards the categories that test for it. Scrolls and
    /// staves burn regardless, because the C returns TRUE for them outright.
    /// </summary>
    [Fact]
    public void ResistFire_DoesNotSaveScrollsOrStaves()
    {
        Assert.True(ItemSets.IsFlammable(new Item(ItemCategory.Scroll1, ItemFlags.ResistFire)));
        Assert.True(ItemSets.IsFlammable(new Item(ItemCategory.Staff, ItemFlags.ResistFire)));
    }

    [Fact]
    public void Corrodes_CoversMetalGearOnly()
    {
        Assert.True(ItemSets.Corrodes(new Item(ItemCategory.Sword)));
        Assert.True(ItemSets.Corrodes(new Item(ItemCategory.Wand)));
        Assert.False(ItemSets.Corrodes(new Item(ItemCategory.SoftArmor)));
    }

    [Fact]
    public void Lightning_DestroysRingsWandsAndSpikes()
    {
        Assert.True(ItemSets.DestroyedByLightning(new Item(ItemCategory.Ring)));
        Assert.True(ItemSets.DestroyedByLightning(new Item(ItemCategory.Wand)));
        Assert.True(ItemSets.DestroyedByLightning(new Item(ItemCategory.Spike)));
        Assert.False(ItemSets.DestroyedByLightning(new Item(ItemCategory.Sword)));
    }

    [Fact]
    public void Never_IsFalseForEveryCategory()
    {
        for (int tval = 0; tval <= byte.MaxValue; tval++)
        {
            Assert.False(ItemSets.Never(new Item((byte)tval)));
        }
    }

    /// <summary>
    /// Weapons count as too large for a chest only above 150 tenths of a pound,
    /// so the threshold itself is worth pinning.
    /// </summary>
    [Fact]
    public void TooLargeForChest_UsesTheWeightThresholdOnWeapons()
    {
        Assert.False(ItemSets.IsTooLargeForChest(new Item(ItemCategory.Sword, Weight: 150)));
        Assert.True(ItemSets.IsTooLargeForChest(new Item(ItemCategory.Sword, Weight: 151)));

        // Bulky categories qualify whatever they weigh.
        Assert.True(ItemSets.IsTooLargeForChest(new Item(ItemCategory.Bow, Weight: 1)));
        Assert.True(ItemSets.IsTooLargeForChest(new Item(ItemCategory.HardArmor, Weight: 1)));

        Assert.False(ItemSets.IsTooLargeForChest(new Item(ItemCategory.Ring, Weight: 9999)));
    }

    // ------------------------------------------------------------ cave sets

    [Fact]
    public void CaveSets_ClassifySquaresAsTheCDoes()
    {
        Assert.True(CaveSets.IsRoom(CaveFeature.DarkFloor));
        Assert.True(CaveSets.IsRoom(CaveFeature.LightFloor));
        Assert.False(CaveSets.IsRoom(CaveFeature.CorridorFloor));

        Assert.True(CaveSets.IsCorridor(CaveFeature.CorridorFloor));
        Assert.True(CaveSets.IsCorridor(CaveFeature.BlockedFloor));
        Assert.False(CaveSets.IsCorridor(CaveFeature.DarkFloor));

        // set_floor() is a range test, so every room and corridor value passes.
        Assert.True(CaveSets.IsFloor(CaveFeature.DarkFloor));
        Assert.True(CaveSets.IsFloor(CaveFeature.BlockedFloor));
        Assert.False(CaveSets.IsFloor(CaveFeature.GraniteWall));
        Assert.False(CaveSets.IsFloor(CaveFeature.BoundaryWall));
    }

    // ------------------------------------------------------------ store sets

    [Fact]
    public void Buys_DispatchesToTheMatchingPredicate()
    {
        for (int tval = 0; tval <= byte.MaxValue; tval++)
        {
            byte t = (byte)tval;
            Assert.Equal(StoreSets.GeneralStoreBuys(t), StoreSets.Buys(StoreSets.GeneralStore, t));
            Assert.Equal(StoreSets.ArmoryBuys(t), StoreSets.Buys(StoreSets.Armory, t));
            Assert.Equal(StoreSets.WeaponsmithBuys(t), StoreSets.Buys(StoreSets.Weaponsmith, t));
            Assert.Equal(StoreSets.TempleBuys(t), StoreSets.Buys(StoreSets.Temple, t));
            Assert.Equal(StoreSets.AlchemistBuys(t), StoreSets.Buys(StoreSets.Alchemist, t));
            Assert.Equal(StoreSets.MagicShopBuys(t), StoreSets.Buys(StoreSets.MagicShop, t));
        }
    }

    [Fact]
    public void Buys_ReturnsFalseForAnUnknownStore() =>
        Assert.False(StoreSets.Buys(99, ItemCategory.Food));

    /// <summary>
    /// The temple takes blunt weapons but no edged ones - the priestly
    /// convention, and the only place a weapon category splits across stores.
    /// </summary>
    [Fact]
    public void Temple_TakesHaftedWeaponsButNotSwords()
    {
        Assert.True(StoreSets.TempleBuys(ItemCategory.Hafted));
        Assert.False(StoreSets.TempleBuys(ItemCategory.Sword));
        Assert.False(StoreSets.TempleBuys(ItemCategory.Polearm));

        Assert.True(StoreSets.WeaponsmithBuys(ItemCategory.Sword));
    }

    [Fact]
    public void Armory_TakesNoWeapons()
    {
        Assert.False(StoreSets.ArmoryBuys(ItemCategory.Sword));
        Assert.False(StoreSets.ArmoryBuys(ItemCategory.Bow));
        Assert.True(StoreSets.ArmoryBuys(ItemCategory.Shield));
    }
}
