using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on the CM_/CS_/CD_ flag vocabularies and on the monster table columns
/// they decode.
///
/// These are the first tests that read the generated monster table as data
/// rather than checking its shape, so a wrong flag column would surface here.
/// </summary>
public class CreatureFlagsTests
{
    private static CreatureType ByName(string name) =>
        GameTables.CreatureList.Single(c => c.Name == name);

    // ------------------------------------------------------------ masks

    /// <summary>
    /// Each named bit has to sit inside the group mask that claims it, or the
    /// code that tests the group will disagree with the code that tests the bit.
    /// </summary>
    [Fact]
    public void GroupMasks_CoverExactlyTheirMembers()
    {
        Assert.Equal(
            CreatureMove.RandomMove,
            CreatureMove.Move20PercentRandom
                | CreatureMove.Move40PercentRandom
                | CreatureMove.Move75PercentRandom);

        Assert.Equal(
            CreatureMove.Special,
            CreatureMove.Invisible
                | CreatureMove.OpensDoors
                | CreatureMove.Phase
                | CreatureMove.EatsOtherMonsters
                | CreatureMove.PicksUpObjects
                | CreatureMove.Multiplies);

        Assert.Equal(
            CreatureMove.Treasure,
            CreatureMove.Drop60Percent
                | CreatureMove.Drop90Percent
                | CreatureMove.Drop1d2Objects
                | CreatureMove.Drop2d2Objects
                | CreatureMove.Drop4d2Objects);

        Assert.Equal(
            CreatureSpell.Breathe,
            CreatureSpell.BreatheLightning
                | CreatureSpell.BreatheGas
                | CreatureSpell.BreatheAcid
                | CreatureSpell.BreatheFrost
                | CreatureSpell.BreatheFire);

        Assert.Equal(
            CreatureDefense.Weakness,
            (ushort)(CreatureDefense.HurtByFrost
                | CreatureDefense.HurtByFire
                | CreatureDefense.HurtByPoison
                | CreatureDefense.HurtByAcid
                | CreatureDefense.HurtByLight
                | CreatureDefense.HurtByStone));
    }

    /// <summary>
    /// The two "RANDOM" families must not collide. Bits 3-5 are how erratically
    /// the monster walks; bits 26-27 are extra treasure drops. Reading one as
    /// the other is the easiest mistake to make in this vocabulary.
    /// </summary>
    [Fact]
    public void MovementRandomness_AndTreasureDrops_DoNotOverlap()
    {
        Assert.Equal(0u, CreatureMove.RandomMove & CreatureMove.Treasure);

        Assert.Equal(0u, CreatureMove.Drop60Percent & CreatureMove.RandomMove);
        Assert.Equal(0u, CreatureMove.Drop90Percent & CreatureMove.RandomMove);

        // But the drop flags do sit inside the treasure field, on purpose.
        Assert.Equal(CreatureMove.Drop60Percent, CreatureMove.Drop60Percent & CreatureMove.Treasure);
        Assert.Equal(CreatureMove.Drop90Percent, CreatureMove.Drop90Percent & CreatureMove.Treasure);
    }

    [Fact]
    public void TreasureShift_TurnsTheFieldIntoASmallNumber()
    {
        Assert.Equal(31u, CreatureMove.Treasure >> CreatureMove.TreasureShift);
        Assert.Equal(1u, CreatureMove.Drop60Percent >> CreatureMove.TreasureShift);
        Assert.Equal(16u, CreatureMove.Drop4d2Objects >> CreatureMove.TreasureShift);
    }

    [Fact]
    public void SpellFrequency_IsTheLowNibbleAndDisjointFromTheSpellBits()
    {
        Assert.Equal(0u, CreatureSpell.Frequency & CreatureSpell.Spells);
        Assert.Equal(0u, CreatureSpell.Frequency & CreatureSpell.Breathe);
        Assert.Equal(15u, CreatureSpell.Frequency); // holds 0..15, not a flag
    }

    // ------------------------------------------------------------ table reads

    /// <summary>
    /// constant.h says only one creature carries the win bit, and monsters.c
    /// comments that Evil Iggy is deliberately not one despite being late-game.
    /// </summary>
    [Fact]
    public void ExactlyOneCreatureWinsTheGame()
    {
        CreatureType[] winners =
            [.. GameTables.CreatureList.Where(c => c.WinsGameWhenKilled)];

        CreatureType winner = Assert.Single(winners);
        Assert.Equal("Balrog", winner.Name);

        Assert.False(ByName("Evil Iggy").WinsGameWhenKilled);
    }

    [Fact]
    public void Balrog_DecodesAsTheEndBoss()
    {
        CreatureType balrog = ByName("Balrog");

        Assert.True(balrog.WinsGameWhenKilled);
        Assert.True(balrog.CastsSpells);
        Assert.True(balrog.Breathes);
        Assert.True(balrog.HasMove(CreatureMove.MoveNormal));
        Assert.True(balrog.HasMove(CreatureMove.OpensDoors));
        Assert.True(balrog.HasDefense(CreatureDefense.Evil));
        Assert.True(balrog.HasDefense(CreatureDefense.NeverSleeps));
        Assert.True(balrog.HasDefense(CreatureDefense.MaxHitPoints));
    }

    /// <summary>
    /// The town's opening monsters should decode as harmless: no spells, no
    /// treasure, ordinary movement.
    /// </summary>
    [Fact]
    public void FilthyStreetUrchin_DecodesAsHarmless()
    {
        CreatureType urchin = GameTables.CreatureList[0];

        Assert.False(urchin.CastsSpells);
        Assert.False(urchin.Breathes);
        Assert.False(urchin.WinsGameWhenKilled);
        Assert.Equal(0, urchin.TreasureRating);
        Assert.True(urchin.HasMove(CreatureMove.MoveNormal));
    }

    /// <summary>
    /// The distinction the constant.h comment warns about: breath bits with no
    /// spell frequency are resistances, not a breath attack. If no creature in
    /// the table is in that state the accessor is untested, so assert the shape
    /// of the data too.
    /// </summary>
    [Fact]
    public void BreathBitsWithoutFrequency_DoNotCountAsBreathing()
    {
        CreatureType[] resistOnly =
        [
            .. GameTables.CreatureList.Where(c =>
                !c.CastsSpells && (c.SpellFlags & CreatureSpell.Breathe) != 0),
        ];

        Assert.NotEmpty(resistOnly);
        Assert.All(resistOnly, c => Assert.False(c.Breathes));
    }

    [Fact]
    public void Breathers_AllHaveASpellFrequency()
    {
        foreach (CreatureType c in GameTables.CreatureList.Where(c => c.Breathes))
        {
            Assert.InRange(c.SpellFrequency, 1, 15);
        }
    }

    /// <summary>
    /// Breeders are the monsters that overrun a level if ignored; the classic
    /// one should still be flagged.
    /// </summary>
    [Fact]
    public void GiantWhiteMouse_Multiplies()
    {
        Assert.True(ByName("Giant White Mouse").HasMove(CreatureMove.Multiplies));
    }

    [Fact]
    public void DefenseKinds_AreUsedByTheTable()
    {
        // Every slay category a weapon can target must actually match something.
        foreach (ushort kind in new[]
        {
            CreatureDefense.Dragon,
            CreatureDefense.Animal,
            CreatureDefense.Evil,
            CreatureDefense.Undead,
        })
        {
            Assert.Contains(GameTables.CreatureList, c => c.HasDefense(kind));
        }
    }

    [Fact]
    public void EveryCreatureHasAMovementStyle()
    {
        // A monster with no movement bits at all would never act.
        Assert.All(
            GameTables.CreatureList,
            c => Assert.NotEqual(0u, c.MoveFlags & CreatureMove.AllMoveFlags));
    }
}
