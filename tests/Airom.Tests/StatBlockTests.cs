using Airom.Core;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the status sidebar.
///
/// 84 sidebar screens are diffed against the C oracle, which runs the real
/// prt_stat_block() against a recording curses. These pin the properties behind
/// them.
/// </summary>
public class StatBlockTests
{
    private static (GameState Game, Player Player, Display Display, MemoryScreen Screen) Rolled()
    {
        var game = new GameState();
        game.InitSeeds(12345);
        game.MagicInit();

        Player player = new CharacterCreation(game)
            .Create(race: 0, characterClass: 0, male: true, name: "Oracle");

        var screen = new MemoryScreen();
        return (game, player, new Display(game, screen), screen);
    }

    /// <summary>
    /// Stats above 18 are written as a percentile, which is how Umoria fits a
    /// wider range into the same six columns.
    /// </summary>
    [Theory]
    [InlineData(3, "     3")]
    [InlineData(18, "    18")]
    [InlineData(19, " 18/01")]
    [InlineData(18 + 22, " 18/22")]
    [InlineData(18 + 99, " 18/99")]
    [InlineData(18 + 100, "18/100")]
    public void FormatStat_WritesThePercentileRange(int stat, string expected) =>
        Assert.Equal(expected, Display.FormatStat(stat));

    /// <summary>Every stat display is exactly six columns, or the sidebar shifts.</summary>
    [Fact]
    public void FormatStat_AlwaysFillsSixColumns()
    {
        for (int stat = 3; stat <= 18 + 100; stat++)
        {
            Assert.Equal(6, Display.FormatStat(stat).Length);
        }
    }

    [Fact]
    public void TitleFor_CoversBothEndsOfTheRange()
    {
        (_, Player player, _, _) = Rolled();

        player.Level = 0;
        Assert.Equal("Babe in arms", Display.TitleFor(player));

        player.Level = 1;
        Assert.Equal("Rookie", Display.TitleFor(player));

        player.Level = Player.MaxLevel + 1;
        Assert.Equal("**KING**", Display.TitleFor(player));

        player.Male = false;
        Assert.Equal("**QUEEN**", Display.TitleFor(player));
    }

    [Fact]
    public void PrintStatBlock_DrawsTheIdentityAndTheNumbers()
    {
        (_, Player player, Display display, MemoryScreen screen) = Rolled();

        player.Experience = 12345;
        player.CurrentMana = 7;
        player.DisplayedArmourClass = 14;
        player.Gold = 4321;

        display.PrintStatBlock(player);

        Assert.Equal("Human", screen.GetRow(2).TrimEnd());
        Assert.Equal("Warrior", screen.GetRow(3).TrimEnd());
        Assert.Equal("Rookie", screen.GetRow(4).TrimEnd());
        Assert.Equal("EXP :  12345", screen.GetRow(14).TrimEnd());
        Assert.Equal("MANA:      7", screen.GetRow(15).TrimEnd());
        Assert.Equal("AC  :     14", screen.GetRow(19).TrimEnd());
        Assert.Equal("GOLD:   4321", screen.GetRow(20).TrimEnd());
    }

    /// <summary>
    /// A longer field has to be blanked before the shorter one is written, or
    /// the tail of the old title survives beside the new one.
    /// </summary>
    [Fact]
    public void PrintStatBlock_ClearsTheFieldBeforeWriting()
    {
        (_, Player player, Display display, MemoryScreen screen) = Rolled();

        display.PutBuffer("XXXXXXXXXXXXX", 4, 0);
        display.PrintStatBlock(player);

        Assert.Equal("Rookie", screen.GetRow(4).TrimEnd());
    }

    /// <summary>Weak outranks merely hungry, so the worse news is the one shown.</summary>
    [Fact]
    public void PrintHunger_PrefersWeakOverHungry()
    {
        (_, Player player, Display display, MemoryScreen screen) = Rolled();

        player.Status |= PlayerStatus.Hungry;
        display.PrintHunger(player);
        Assert.Equal("Hungry", screen.GetRow(23)[..6]);

        player.Status |= PlayerStatus.Weak;
        display.PrintHunger(player);
        Assert.Equal("Weak", screen.GetRow(23).TrimEnd());
    }

    /// <summary>
    /// Each condition owns a column range, which is what lets them come and go
    /// independently without redrawing the whole line.
    /// </summary>
    [Fact]
    public void PrintStatBlock_GivesEachConditionItsOwnColumns()
    {
        (_, Player player, Display display, MemoryScreen screen) = Rolled();

        player.Status |= PlayerStatus.Blind | PlayerStatus.Confused
            | PlayerStatus.Afraid | PlayerStatus.Poisoned;
        display.PrintStatBlock(player);

        string row = screen.GetRow(23);
        Assert.Equal("Blind", row.Substring(7, 5));
        Assert.Equal("Confused", row.Substring(13, 8));
        Assert.Equal("Afraid", row.Substring(22, 6));
        Assert.Equal("Poisoned", row.Substring(29, 8));
    }

    /// <summary>
    /// Paralysis outranks resting, so a held player is not reported as merely
    /// taking a break.
    /// </summary>
    [Fact]
    public void PrintState_ParalysisOutranksResting()
    {
        (_, Player player, Display display, MemoryScreen screen) = Rolled();

        player.Status |= PlayerStatus.Resting;
        player.Rest = 42;
        player.Paralysis = 5;
        display.PrintState(player);

        Assert.Equal("Paralysed", screen.GetRow(23)[38..].TrimEnd());
    }

    /// <summary>
    /// Resting until something happens shows a star rather than a count.
    ///
    /// FAITHFUL QUIRK: the star form is six characters and nothing blanks what
    /// was there, so a digit of the previous count survives beside it.
    /// </summary>
    [Fact]
    public void PrintState_ShowsRestCountOrStar()
    {
        (_, Player player, Display display, MemoryScreen screen) = Rolled();

        player.Status |= PlayerStatus.Resting;
        player.Rest = 42;
        display.PrintState(player);
        Assert.Equal("Rest 42", screen.GetRow(23)[38..].TrimEnd());

        player.Rest = -1;
        display.PrintState(player);
        Assert.Equal("Rest *2", screen.GetRow(23)[38..].TrimEnd());
    }

    /// <summary>
    /// FAITHFUL QUIRK: a repeating command writes its count and then searching
    /// overwrites the first six columns, so the display reads "Search" followed
    /// by the tail of the count.
    /// </summary>
    [Fact]
    public void PrintState_SearchOverwritesTheRepeatCount()
    {
        (_, Player player, Display display, MemoryScreen screen) = Rolled();

        display.CommandCount = 17;
        player.Status |= PlayerStatus.Searching;
        display.PrintState(player);

        Assert.Equal("Search 17", screen.GetRow(23)[38..].TrimEnd());
        Assert.True((player.Status & PlayerStatus.Repeating) != 0);
    }

    /// <summary>
    /// Searching costs a point of speed, which is why searching at a normal pace
    /// reports nothing rather than "Slow".
    /// </summary>
    [Fact]
    public void PrintSpeed_DiscountsSearching()
    {
        (_, Player player, Display display, MemoryScreen screen) = Rolled();

        player.Speed = 1;
        player.Status |= PlayerStatus.Searching;
        display.PrintSpeed(player);
        Assert.Equal(string.Empty, screen.GetRow(23)[49..].TrimEnd());

        player.Speed = 2;
        display.PrintSpeed(player);
        Assert.Equal("Slow", screen.GetRow(23)[49..].TrimEnd());
    }

    [Theory]
    [InlineData(2, "Very Slow")]
    [InlineData(1, "Slow")]
    [InlineData(0, "")]
    [InlineData(-1, "Fast")]
    [InlineData(-3, "Very Fast")]
    public void PrintSpeed_NamesEachBand(int speed, string expected)
    {
        (_, Player player, Display display, MemoryScreen screen) = Rolled();

        player.Speed = speed;
        display.PrintSpeed(player);

        Assert.Equal(expected, screen.GetRow(23)[49..].TrimEnd());
    }

    /// <summary>Drawing the study reminder clears the flag that asked for it.</summary>
    [Fact]
    public void PrintStudy_ClearsTheFlagItReports()
    {
        (_, Player player, Display display, MemoryScreen screen) = Rolled();

        player.NewSpells = 2;
        player.Status |= PlayerStatus.CanStudy;
        display.PrintStudy(player);

        Assert.Equal("Study", screen.GetRow(23)[59..].TrimEnd());
        Assert.Equal(0u, player.Status & PlayerStatus.CanStudy);
    }

    [Fact]
    public void PrintWinner_ReportsWizardAheadOfWinning()
    {
        (GameState game, Player player, Display display, MemoryScreen screen) = Rolled();

        game.TotalWinner = true;
        display.PrintWinner(player);
        Assert.Equal("*Winner*", screen.GetRow(22).TrimEnd());

        game.NoScore |= 0x2;
        game.Wizard = true;
        display.PrintWinner(player);
        Assert.Equal("Is wizard", screen.GetRow(22).TrimEnd());
    }
}
