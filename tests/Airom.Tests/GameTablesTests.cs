using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on the generated object and monster tables.
///
/// The rows themselves come from tools/gen_tables.py, so these tests are not
/// re-typing the data - they pin the things that would break silently if the
/// generator regressed: the row counts, the field ordering, and the two parsing
/// hazards in the C source that already caused wrong output once each.
/// </summary>
public class GameTablesTests
{
    [Fact]
    public void ObjectList_HasEveryRowFromTheCTable()
    {
        // MAX_OBJECTS in constant.h.
        Assert.Equal(420, GameTables.ObjectList.Length);
        Assert.All(GameTables.ObjectList, o => Assert.NotNull(o));
    }

    [Fact]
    public void CreatureList_HasEveryRowFromTheCTable()
    {
        // MAX_CREATURES in constant.h.
        Assert.Equal(279, GameTables.CreatureList.Length);
        Assert.All(GameTables.CreatureList, c => Assert.NotNull(c));
    }

    /// <summary>
    /// Row 0 of object_list, transcribed from treasure.c. Guards the field
    /// order of the whole table: the C initialiser puts p1 before cost and
    /// subval after it, which is easy to get backwards.
    /// </summary>
    [Fact]
    public void ObjectList_FirstRow_MatchesTheCSource()
    {
        TreasureType poison = GameTables.ObjectList[0];

        Assert.Equal("Poison", poison.Name);
        Assert.Equal(0x00000001u, poison.Flags);
        Assert.Equal(ItemCategory.Food, poison.TVal);
        Assert.Equal(',', poison.DisplayChar);
        Assert.Equal(500, poison.P1); // food value
        Assert.Equal(0, poison.Cost);
        Assert.Equal(64, poison.SubVal);
        Assert.Equal(1, poison.Number);
        Assert.Equal(1, poison.Weight);
        Assert.Equal(0, poison.ToHit);
        Assert.Equal(0, poison.ToDam);
        Assert.Equal(0, poison.Ac);
        Assert.Equal(0, poison.ToAc);
        Assert.Equal(0, poison.DamageDice);
        Assert.Equal(0, poison.DamageSides);
        Assert.Equal(7, poison.Level);
    }

    /// <summary>
    /// The Balrog is the only creature whose death wins the game, and it sits
    /// at the far end of the table with the largest values in several columns -
    /// which makes it a good check that nothing truncated on the way across.
    /// </summary>
    [Fact]
    public void CreatureList_Balrog_MatchesTheCSource()
    {
        CreatureType balrog = GameTables.CreatureList[^1];

        Assert.Equal("Balrog", balrog.Name);
        Assert.Equal(0xFF1F0002u, balrog.MoveFlags);
        Assert.Equal(0x0081C743u, balrog.SpellFlags);
        Assert.Equal(0x5004, balrog.DefenseFlags);
        Assert.Equal(55000, balrog.KillExperience);
        Assert.Equal(0, balrog.Sleep);
        Assert.Equal(40, balrog.AreaOfEffect);
        Assert.Equal(125, balrog.Ac);
        Assert.Equal(13, balrog.Speed); // stored +10, so three faster than normal
        Assert.Equal('B', balrog.DisplayChar);
        Assert.Equal(75, balrog.HitDiceCount);
        Assert.Equal(40, balrog.HitDiceSides);
        Assert.Equal([104, 78, 214, 0], balrog.Attacks.ToArray());
        Assert.Equal(100, balrog.Level);
    }

    [Fact]
    public void CreatureList_FirstRow_MatchesTheCSource()
    {
        CreatureType urchin = GameTables.CreatureList[0];

        Assert.Equal("Filthy Street Urchin", urchin.Name);
        Assert.Equal(0x0012000Au, urchin.MoveFlags);
        Assert.Equal(0u, urchin.SpellFlags);
        Assert.Equal(0x2034, urchin.DefenseFlags);
        Assert.Equal(0, urchin.KillExperience);
        Assert.Equal(40, urchin.Sleep);
        Assert.Equal(4, urchin.AreaOfEffect);
        Assert.Equal(1, urchin.Ac);
        Assert.Equal(11, urchin.Speed); // normal speed
        Assert.Equal('p', urchin.DisplayChar);
        Assert.Equal([72, 148, 0, 0], urchin.Attacks.ToArray());
        Assert.Equal(0, urchin.Level);
    }

    /// <summary>
    /// REGRESSION: bows are drawn as '}' and ammunition as '{'. A brace counter
    /// that does not skip character literals treats those glyphs as structure
    /// and truncates the table at row 74, which is exactly what the first
    /// version of the generator did.
    /// </summary>
    [Fact]
    public void ObjectList_BraceGlyphs_DidNotTerminateTheTable()
    {
        Assert.Equal('}', GameTables.ObjectList[74].DisplayChar); // & Short Bow
        Assert.Equal(ItemCategory.Bow, GameTables.ObjectList[74].TVal);

        Assert.Equal('{', GameTables.ObjectList[80].DisplayChar); // & Arrow~
        Assert.Equal(ItemCategory.Arrow, GameTables.ObjectList[80].TVal);

        // The table has to survive well past them.
        Assert.Equal(420, GameTables.ObjectList.Length);
    }

    /// <summary>
    /// REGRESSION: treasure.c defines the secret door row twice, once under
    /// #ifdef ATARI_ST and once otherwise. The generator has to resolve the
    /// conditional the way a compiler would and take the portable branch,
    /// whose glyph is '#'.
    /// </summary>
    [Fact]
    public void ObjectList_SecretDoor_UsesThePortableBranch()
    {
        TreasureType secretDoor = GameTables.ObjectList[369]; // OBJ_SECRET_DOOR

        Assert.Equal("& secret door", secretDoor.Name);
        Assert.Equal(ItemCategory.SecretDoor, secretDoor.TVal);
        Assert.Equal('#', secretDoor.DisplayChar);
    }

    /// <summary>
    /// The OBJ_* constants in constant.h address rows by position, so these
    /// indices are part of the data contract rather than incidental.
    /// </summary>
    [Theory]
    [InlineData(367, ItemCategory.OpenDoor)]
    [InlineData(368, ItemCategory.ClosedDoor)]
    [InlineData(369, ItemCategory.SecretDoor)]
    [InlineData(370, ItemCategory.UpStair)]
    [InlineData(371, ItemCategory.DownStair)]
    [InlineData(372, ItemCategory.StoreDoor)]
    public void ObjectList_WellKnownIndices_HoldTheExpectedCategory(int index, byte category)
    {
        Assert.Equal(category, GameTables.ObjectList[index].TVal);
    }

    [Fact]
    public void ObjectList_EveryRowHasAName()
    {
        // Row 419 is a deliberate empty placeholder; everything else is named.
        for (int i = 0; i < GameTables.ObjectList.Length - 1; i++)
        {
            Assert.False(
                string.IsNullOrEmpty(GameTables.ObjectList[i].Name),
                $"object {i} has no name");
        }

        Assert.Equal(string.Empty, GameTables.ObjectList[419].Name);
    }

    [Fact]
    public void CreatureList_EveryRowIsPlausible()
    {
        foreach (CreatureType creature in GameTables.CreatureList)
        {
            Assert.False(string.IsNullOrWhiteSpace(creature.Name));
            Assert.NotEqual('\0', creature.DisplayChar);
            Assert.Equal(4, creature.Attacks.Length);
            Assert.True(creature.HitDiceCount > 0, $"{creature.Name} has no hit dice");
            Assert.True(creature.HitDiceSides > 0, $"{creature.Name} has zero-sided hit dice");
        }
    }

    /// <summary>
    /// Umoria stores speed with an offset of +10 so it fits an unsigned byte.
    /// Nothing in the table should sit outside a sane band around normal speed.
    /// </summary>
    [Fact]
    public void CreatureList_SpeedsAreWithinTheOffsetRange()
    {
        Assert.All(GameTables.CreatureList, c => Assert.InRange(c.Speed, 9, 14));
    }
}
