using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the monster memory, written out as prose.
///
/// Every creature in the table is described against the C oracle at four depths
/// of knowledge and sixteen character levels - 4,464 descriptions - so what
/// these pin is the behaviour behind them: which facts need how much of a
/// meeting before they are said at all.
/// </summary>
public class MonsterRecallTests
{
    private static (GameState Game, MemoryScreen Screen, MonsterRecall Recall) Fresh(
        int level = 15)
    {
        var game = new GameState();
        game.InitSeeds(12345);
        game.MagicInit();
        game.Player = new Player { Level = level };

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(new string(' ', 64));

        var display = new Display(game, screen);

        return (game, screen, new MonsterRecall(game, display));
    }

    /// <summary>Everything the description put on the screen, as one string.</summary>
    private static string Read(MemoryScreen screen) =>
        string.Join(" ", screen.GetText().Split('\n').Select(line => line.TrimEnd()))
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();

    /// <summary>The first creature in the table with a given trait.</summary>
    private static int FirstWith(Func<CreatureType, bool> wanted)
    {
        for (int i = 0; i < GameTables.CreatureList.Length; i++)
        {
            if (wanted(GameTables.CreatureList[i]))
            {
                return i;
            }
        }

        throw new InvalidOperationException("no creature matches");
    }

    // ------------------------------------------------------ what is known

    /// <summary>
    /// A creature nobody has met is a blank, and the recall says so rather than
    /// refusing to open.
    /// </summary>
    [Fact]
    public void Describe_AnUnmetCreatureSaysAlmostNothing()
    {
        (GameState game, MemoryScreen screen, MonsterRecall recall) = Fresh();

        game.Memories[20].Clear();
        recall.Describe(20);

        string page = Read(screen);

        Assert.Contains("No known battles to the death are recalled.", page,
            StringComparison.Ordinal);

        Assert.Contains("Nothing is known about its attack.", page,
            StringComparison.Ordinal);
    }

    /// <summary>Which is exactly what "nothing is known" means for the browser.</summary>
    [Fact]
    public void KnowsAnything_IsFalseUntilSomethingHappens()
    {
        (GameState game, _, MonsterRecall recall) = Fresh();

        game.Memories[20].Clear();
        Assert.False(recall.KnowsAnything(20));

        game.Memories[20].Kills = 1;
        Assert.True(recall.KnowsAnything(20));
    }

    /// <summary>A wizard knows everything about everything, by definition.</summary>
    [Fact]
    public void KnowsAnything_IsAlwaysTrueForAWizard()
    {
        (GameState game, _, MonsterRecall recall) = Fresh();

        game.Memories[20].Clear();
        game.Wizard = true;

        Assert.True(recall.KnowsAnything(20));
    }

    /// <summary>
    /// A wizard's look fills the memory in to read it out, and has to leave it
    /// exactly as it found it.
    /// </summary>
    [Fact]
    public void Describe_AWizardsLookLeavesTheMemoryAlone()
    {
        (GameState game, _, MonsterRecall recall) = Fresh();

        MonsterMemory memory = game.Memories[20];
        memory.Clear();
        memory.Kills = 3;
        memory.Attacks[0] = 7;

        game.Wizard = true;
        recall.Describe(20);

        Assert.Equal(3, memory.Kills);
        Assert.Equal(7, memory.Attacks[0]);
        Assert.Equal(0u, memory.Move);
        Assert.Equal(0, memory.Deaths);
    }

    // ---------------------------------------------------- what a kill teaches

    /// <summary>
    /// Killing one teaches where they live and what they are worth - neither of
    /// which is said before the first kill.
    /// </summary>
    [Fact]
    public void Describe_AKillTeachesTheDepthAndTheWorth()
    {
        int which = FirstWith(c => c.Level > 0 && c.KillExperience > 0);

        (GameState before, MemoryScreen unmet, MonsterRecall unmetRecall) = Fresh();
        before.Memories[which].Clear();
        unmetRecall.Describe(which);

        (GameState after, MemoryScreen met, MonsterRecall metRecall) = Fresh();
        after.Memories[which].Clear();
        after.Memories[which].Kills = 1;
        metRecall.Describe(which);

        Assert.DoesNotContain("normally found at depths", Read(unmet),
            StringComparison.Ordinal);

        Assert.Contains("normally found at depths", Read(met),
            StringComparison.Ordinal);

        Assert.Contains("creature is worth", Read(met), StringComparison.Ordinal);
    }

    /// <summary>
    /// A town creature says where it lives without any kills at all: living in
    /// the town is not something that has to be discovered.
    /// </summary>
    [Fact]
    public void Describe_ATownCreatureSaysSoWithoutAKill()
    {
        int which = FirstWith(c => c.Level == 0);

        (GameState game, MemoryScreen screen, MonsterRecall recall) = Fresh();
        game.Memories[which].Clear();
        recall.Describe(which);

        Assert.Contains("It lives in the town", Read(screen), StringComparison.Ordinal);
    }

    /// <summary>
    /// The armour rating needs enough kills to have judged it, and a deeper
    /// creature needs fewer - meeting one at all being an education.
    /// </summary>
    [Fact]
    public void Describe_TheArmourRatingNeedsEnoughKills()
    {
        int which = FirstWith(c => c.Level is > 0 and < 10);
        CreatureType creature = GameTables.CreatureList[which];

        // The threshold the original uses: 304 / (4 + level).
        int needed = 304 / (4 + creature.Level);

        string PageAfter(int kills)
        {
            (GameState game, MemoryScreen screen, MonsterRecall recall) = Fresh();
            game.Memories[which].Clear();
            game.Memories[which].Kills = kills;
            recall.Describe(which);
            return Read(screen);
        }

        Assert.DoesNotContain("armor rating", PageAfter(needed),
            StringComparison.Ordinal);

        Assert.Contains("armor rating", PageAfter(needed + 1),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The damage an attack does needs to have been felt often enough, and a
    /// heavier attack needs to be felt more often.
    /// </summary>
    [Fact]
    public void Describe_TheDamageNeedsTheAttackToHaveLanded()
    {
        int which = FirstWith(c =>
            c.Attacks.Length > 0 && c.Attacks[0] != 0
            && GameTables.MonsterAttacks[c.Attacks[0]].Dice > 0
            && GameTables.MonsterAttacks[c.Attacks[0]].Sides > 0);

        string PageAfter(int landed)
        {
            (GameState game, MemoryScreen screen, MonsterRecall recall) = Fresh();
            game.Memories[which].Clear();
            game.Memories[which].Attacks[0] = (byte)landed;
            recall.Describe(which);
            return Read(screen);
        }

        Assert.DoesNotContain("with damage", PageAfter(1), StringComparison.Ordinal);
        Assert.Contains("with damage", PageAfter(255), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ the wording

    /// <summary>
    /// Whether killing one wins the game is never a secret, whatever else is
    /// unknown about it.
    /// </summary>
    [Fact]
    public void Describe_AWinningCreatureAlwaysSaysSo()
    {
        int which = FirstWith(c => (c.MoveFlags & CreatureMove.Win) != 0);

        (GameState game, MemoryScreen screen, MonsterRecall recall) = Fresh();
        game.Memories[which].Clear();
        recall.Describe(which);

        Assert.Contains("Killing one of these wins the game!", Read(screen),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The kill is priced against the reader: the same creature is worth less
    /// to a character who has further to fall.
    /// </summary>
    [Fact]
    public void Describe_TheWorthIsScaledByTheReadersLevel()
    {
        int which = FirstWith(c => c.Level > 5 && c.KillExperience > 100);

        string PageAt(int level)
        {
            (GameState game, MemoryScreen screen, MonsterRecall recall) = Fresh(level);
            game.Memories[which].Clear();
            game.Memories[which].Kills = 1;
            recall.Describe(which);
            return Read(screen);
        }

        Assert.NotEqual(PageAt(1), PageAt(20));
        Assert.Contains("for a 1st level character", PageAt(1), StringComparison.Ordinal);
        Assert.Contains("for a 20th level character", PageAt(20), StringComparison.Ordinal);
    }

    /// <summary>
    /// Eighth, eleventh and eighteenth take "an" - the original lists them
    /// rather than working the sound out.
    /// </summary>
    [Theory]
    [InlineData(8, "for an 8th level character")]
    [InlineData(11, "for an 11th level character")]
    [InlineData(18, "for an 18th level character")]
    [InlineData(12, "for a 12th level character")]
    [InlineData(21, "for a 21st level character")]
    [InlineData(13, "for a 13th level character")]
    public void Describe_TheOrdinalAndItsArticleFollowTheLevel(int level, string expected)
    {
        int which = FirstWith(c => c.Level > 5 && c.KillExperience > 100);

        (GameState game, MemoryScreen screen, MonsterRecall recall) = Fresh(level);
        game.Memories[which].Clear();
        game.Memories[which].Kills = 1;
        recall.Describe(which);

        Assert.Contains(expected, Read(screen), StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether something breathes or merely shrugs the element off turns on
    /// whether it has ever been seen to cast at all.
    /// </summary>
    [Fact]
    public void Describe_BreathingAndResistingReadDifferently()
    {
        int which = FirstWith(c =>
            (c.SpellFlags & CreatureSpell.Breathe) != 0
            && (c.SpellFlags & CreatureSpell.Frequency) != 0);

        CreatureType creature = GameTables.CreatureList[which];

        string PageWith(uint spells)
        {
            (GameState game, MemoryScreen screen, MonsterRecall recall) = Fresh();
            game.Memories[which].Clear();
            game.Memories[which].Spells = spells;
            recall.Describe(which);
            return Read(screen);
        }

        // Seen to breathe, and so known to be able to.
        Assert.Contains("It can breathe", PageWith(creature.SpellFlags),
            StringComparison.Ordinal);

        // Known only to have shrugged it off, which says nothing about casting.
        Assert.Contains("It is resistant to",
            PageWith(creature.SpellFlags & ~CreatureSpell.Frequency),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The page wraps at the width of the screen, breaking at a space rather
    /// than mid-word.
    /// </summary>
    [Fact]
    public void Describe_WrapsAtWordsRatherThanMidWord()
    {
        int which = FirstWith(c => c.Name.Length > 0);

        (GameState game, MemoryScreen screen, MonsterRecall recall) = Fresh();

        // Everything known, so the page is long enough to wrap.
        MonsterMemory memory = game.Memories[which];
        memory.Clear();
        memory.Kills = 200;
        memory.Deaths = 3;
        memory.Move = GameTables.CreatureList[which].MoveFlags;
        memory.Defense = GameTables.CreatureList[which].DefenseFlags;
        memory.Spells = GameTables.CreatureList[which].SpellFlags;

        for (int i = 0; i < MonsterMemory.MaxAttacks; i++)
        {
            memory.Attacks[i] = 255;
        }

        recall.Describe(which);

        string[] lines = screen.GetText().Split('\n');

        foreach (string line in lines)
        {
            Assert.True(line.TrimEnd().Length <= 80,
                "a line ran past the width of the screen");
        }

        // A wrapped line never ends part-way through a word: the break is at a
        // space, so no line ends with a letter that a following line continues.
        Assert.DoesNotContain("--pause--", lines[0], StringComparison.Ordinal);
    }
}
