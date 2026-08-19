using Airom.Core;
using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on magic_treasure, the largest function in Umoria.
///
/// 70,800 generated items are diffed against the C oracle across 74 seed and
/// depth combinations, reaching every item category and 53 of the 56 special
/// names. These pin the invariants behind those numbers.
/// </summary>
public class EnchantmentTests
{
    private static GameState Game(uint seed, int level)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;
        game.Objects.Reset();
        return game;
    }

    /// <summary>Generates items the way the oracle mode does, reusing one slot.</summary>
    private static IEnumerable<InvenType> Generate(uint seed, int level, int count)
    {
        GameState game = Game(seed, level);
        var generator = new DungeonGenerator(game);
        InvenType item = game.Objects[ObjectPool.FirstIndex];

        for (int i = 0; i < count; i++)
        {
            int pick = generator.GetObjectNumber(level, mustBeSmall: false);
            item.CopyFrom(ObjectLevels.Sorted[pick]);
            game.Enchantment.Apply(item, level);
            yield return item;
        }
    }

    [Fact]
    public void Apply_LeavesTheItemIntact()
    {
        foreach (InvenType item in Generate(12345, 20, 500))
        {
            Assert.InRange(item.SpecialName, (byte)0, SpecialName.Count);
            Assert.InRange(item.Index, 0, GameTables.ObjectList.Length - 1);
        }
    }

    /// <summary>
    /// A curse either zeroes the price or inverts it. Rings and amulets negate,
    /// so a negative cost is meaningful rather than a bug - it marks something
    /// that will cost the player to be rid of.
    /// </summary>
    [Fact]
    public void CursedItems_AreWorthlessOrWorseThanNothing()
    {
        int cursed = 0;
        foreach (InvenType item in Generate(999, 40, 3000))
        {
            if ((item.Flags & ItemFlags.Cursed) == 0)
            {
                continue;
            }

            cursed++;
            Assert.True(
                item.Cost <= 0,
                $"cursed {item.TVal} kept a positive cost of {item.Cost}");
        }

        Assert.True(cursed > 0, "no cursed items appeared in 3000");
    }

    /// <summary>
    /// Only rings and amulets negate their price; everything else is zeroed. A
    /// negative cost anywhere else would mean the wrong branch ran.
    /// </summary>
    [Fact]
    public void OnlyRingsAndAmulets_TakeANegativeCost()
    {
        foreach (InvenType item in Generate(31337, 50, 3000))
        {
            if (item.Cost < 0)
            {
                Assert.Contains(
                    item.TVal,
                    new[] { ItemCategory.Ring, ItemCategory.Amulet });
            }
        }
    }

    /// <summary>
    /// Wands and staves come charged. A zero-charge wand would be useless the
    /// moment it was found.
    /// </summary>
    [Fact]
    public void WandsAndStaves_ArriveCharged()
    {
        int charged = 0;
        foreach (InvenType item in Generate(7, 30, 4000))
        {
            if (item.TVal is not (ItemCategory.Wand or ItemCategory.Staff))
            {
                continue;
            }

            charged++;
            Assert.True(item.P1 > 0, $"a {item.TVal} arrived with {item.P1} charges");
        }

        Assert.True(charged > 0, "no wands or staves appeared");
    }

    /// <summary>
    /// Missiles arrive as a pile of seven dice, so between 7 and 42, and each
    /// pile carries a serial number so two otherwise identical piles do not
    /// merge in the inventory.
    /// </summary>
    [Fact]
    public void Missiles_ArriveInPilesWithDistinctSerialNumbers()
    {
        GameState game = Game(42, 25);
        var generator = new DungeonGenerator(game);
        InvenType item = game.Objects[ObjectPool.FirstIndex];

        var serials = new List<short>();
        for (int i = 0; i < 4000; i++)
        {
            int pick = generator.GetObjectNumber(25, mustBeSmall: false);
            item.CopyFrom(ObjectLevels.Sorted[pick]);
            game.Enchantment.Apply(item, 25);

            if (item.TVal is ItemCategory.SlingAmmo or ItemCategory.Bolt
                or ItemCategory.Arrow or ItemCategory.Spike)
            {
                Assert.InRange(item.Number, (byte)7, (byte)42);
                serials.Add(item.P1);
            }
        }

        Assert.True(serials.Count > 0, "no missiles appeared");
        Assert.Equal(serials.Count, serials.Distinct().Count());
        Assert.Equal(serials.Count, game.MissileCounter);
    }

    /// <summary>
    /// The missile serial wraps rather than saturating, which is why it is
    /// signed. Reaching the wrap in normal play would take a very long game, but
    /// the behaviour is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void MissileCounter_WrapsToNegativeRatherThanSaturating()
    {
        GameState game = Game(1, 10);
        game.MissileCounter = 32767; // MAX_SHORT

        var item = new InvenType();
        item.CopyFrom(ObjectLevels.Sorted[0]);
        item.TVal = ItemCategory.Arrow;
        game.Enchantment.Apply(item, 10);

        Assert.Equal(-32768, game.MissileCounter);
    }

    /// <summary>
    /// A chest is always locked unless it came up empty, since every other
    /// branch of its table sets the lock alongside its trap.
    /// </summary>
    [Fact]
    public void Chests_AreLockedUnlessEmpty()
    {
        int chests = 0;
        foreach (InvenType item in Generate(2024, 30, 6000))
        {
            if (item.TVal != ItemCategory.Chest)
            {
                continue;
            }

            chests++;
            if (item.SpecialName == SpecialName.Empty)
            {
                Assert.Equal(0u, item.Flags);
            }
            else
            {
                Assert.True(
                    (item.Flags & ChestFlags.Locked) != 0,
                    $"a chest with name {item.SpecialName} was not locked");
            }
        }

        Assert.True(chests > 0, "no chests appeared");
    }

    /// <summary>
    /// Enchantment gets more likely with depth, up to the cap. Comparing the
    /// shallowest against a deep level is coarse but points the right way.
    /// </summary>
    [Fact]
    public void DeeperItems_AreMoreOftenEnchanted()
    {
        int shallow = Generate(555, 1, 3000).Count(i => i.SpecialName != SpecialName.None);
        int deep = Generate(555, 50, 3000).Count(i => i.SpecialName != SpecialName.None);

        Assert.True(deep > shallow, $"depth 50 gave {deep} named items, depth 1 gave {shallow}");
    }

    [Fact]
    public void Enchantment_IsReproducibleForASeed()
    {
        InvenType[] first = [.. Generate(777, 25, 50).Select(Clone)];
        InvenType[] second = [.. Generate(777, 25, 50).Select(Clone)];

        for (int i = 0; i < first.Length; i++)
        {
            Assert.Equal(first[i].Index, second[i].Index);
            Assert.Equal(first[i].Cost, second[i].Cost);
            Assert.Equal(first[i].Flags, second[i].Flags);
            Assert.Equal(first[i].SpecialName, second[i].SpecialName);
            Assert.Equal(first[i].P1, second[i].P1);
        }
    }

    private static InvenType Clone(InvenType source)
    {
        var copy = new InvenType();
        copy.CopyFrom(source.Index);
        copy.Cost = source.Cost;
        copy.Flags = source.Flags;
        copy.SpecialName = source.SpecialName;
        copy.P1 = source.P1;
        return copy;
    }

    /// <summary>
    /// A dungeon-found light source is partly used and renumbered to the store's
    /// even sub-value, so the two stack in the inventory.
    /// </summary>
    [Fact]
    public void DungeonLightSources_AreRenumberedToTheStoreVersion()
    {
        int lights = 0;
        foreach (InvenType item in Generate(11, 15, 4000))
        {
            if (item.TVal != ItemCategory.Light)
            {
                continue;
            }

            lights++;
            Assert.Equal(0, item.SubVal % 2);
        }

        Assert.True(lights > 0, "no light sources appeared");
    }
}
