using Airom.Core;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the turn and the command reader.
///
/// 168 runs of the loop and every one of the 128 keys are diffed against the C
/// oracle, which runs the real dungeon(). These pin the properties behind them.
/// </summary>
public class GameLoopTests
{
    private static (GameState Game, Player Player, Display Display, MemoryScreen Screen, GameLoop Loop)
        Fresh(string? keys = null)
    {
        var game = new GameState();
        game.InitSeeds(12345);
        game.MagicInit();

        Player player = new CharacterCreation(game)
            .Create(race: 0, characterClass: 0, male: true, name: "Oracle");
        game.Player = player;

        // A blank level with the player somewhere inside it: the lighting reads
        // the squares around wherever they stand, so there has to be a level for
        // it to read.
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();
        game.CharacterRow = 10;
        game.CharacterColumn = 10;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        if (keys is not null)
        {
            screen.SendKeys(keys);
        }

        var display = new Display(game, screen);
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);
        return (game, player, display, screen, new GameLoop(game, display));
    }

    // ------------------------------------------------------------ the keys

    /// <summary>
    /// Both key sets end up as one, which is what keeps the dispatch from having
    /// to know which set the player chose.
    /// </summary>
    [Theory]
    [InlineData('1', 'b')]
    [InlineData('5', '.')]
    [InlineData('a', 'z')]
    [InlineData('t', 'T')]
    [InlineData('L', 'W')]
    [InlineData('S', '#')]
    public void ToRogueLike_TranslatesTheOriginalKeys(char original, char expected) =>
        Assert.Equal(expected, Commands.ToRogueLike(original, () => (true, 4)));

    /// <summary>An unknown key becomes the illegal command rather than nothing.</summary>
    [Fact]
    public void ToRogueLike_MarksUnknownKeysIllegal() =>
        Assert.Equal(Commands.Illegal, Commands.ToRogueLike('%', () => (true, 4)));

    /// <summary>
    /// Run and tunnel spell the direction into the command letter, so they have
    /// to ask for one before they can be translated at all.
    /// </summary>
    [Fact]
    public void ToRogueLike_FoldsTheDirectionIntoRunAndTunnel()
    {
        Assert.Equal('H', Commands.ToRogueLike('.', () => (true, 4)));
        Assert.Equal(Keys.Control('H'), Commands.ToRogueLike('T', () => (true, 4)));

        // Refusing the direction abandons the command.
        Assert.Equal(Commands.Nothing, Commands.ToRogueLike('.', () => (false, 0)));
    }

    /// <summary>
    /// Only commands worth doing over and over take a count, so a mistyped one
    /// cannot quaff ninety-nine potions.
    /// </summary>
    [Fact]
    public void AllowsCount_CoversRepeatableCommandsOnly()
    {
        Assert.True(Commands.AllowsCount('h'));   // walk
        Assert.True(Commands.AllowsCount('S'));   // search
        Assert.True(Commands.AllowsCount('.'));   // rest
        Assert.False(Commands.AllowsCount('q'));  // quaff
        Assert.False(Commands.AllowsCount('Q'));  // quit
        Assert.False(Commands.AllowsCount('r'));  // read
    }

    // -------------------------------------------------------- reading a command

    [Fact]
    public void ReadCommand_TranslatesToTheRogueLikeSet()
    {
        (_, _, _, _, GameLoop loop) = Fresh("1");
        Assert.Equal('b', loop.ReadCommand());
    }

    /// <summary>
    /// A count is introduced with "#", and the digits that follow build it up.
    /// </summary>
    [Fact]
    public void ReadCommand_CollectsARepeatCount()
    {
        // The walk keys are digits in the original set, so a space separates the
        // count from the command it applies to.
        (_, _, Display display, _, GameLoop loop) = Fresh("#12 4");

        Assert.Equal('h', loop.ReadCommand());
        Assert.Equal(12, display.CommandCount);
    }

    /// <summary>An empty count means ninety-nine, which is as long as anything runs.</summary>
    [Fact]
    public void ReadCommand_TreatsAnEmptyCountAsNinetyNine()
    {
        (_, _, Display display, _, GameLoop loop) = Fresh("# 4");

        Assert.Equal('h', loop.ReadCommand());
        Assert.Equal(99, display.CommandCount);
    }

    /// <summary>
    /// A count on a command that cannot take one is refused out loud, because
    /// doing it once silently would be the wrong ninety-nine times in a hundred.
    /// </summary>
    [Fact]
    public void ReadCommand_RefusesACountOnTheWrongCommand()
    {
        (_, _, Display display, MemoryScreen screen, GameLoop loop) = Fresh("#5q");

        Assert.Equal(Commands.Nothing, loop.ReadCommand());
        Assert.Equal(0, display.CommandCount);
        Assert.True(loop.FreeTurn);
        Assert.Contains("Invalid command with a count.", screen.GetRow(0));
    }

    /// <summary>
    /// "^" then a letter stands in for a control key, for terminals that will not
    /// send one.
    /// </summary>
    [Fact]
    public void ReadCommand_SpellsOutAControlKey()
    {
        (_, _, _, _, GameLoop loop) = Fresh("^K");
        Assert.Equal('Q', loop.ReadCommand());

        (_, _, _, _, GameLoop lower) = Fresh("^k");
        Assert.Equal('Q', lower.ReadCommand());
    }

    /// <summary>
    /// The rogue-like set uses the digits for counts, so a digit command needs a
    /// space to separate the count from it.
    /// </summary>
    [Fact]
    public void ReadCommand_SeparatesACountFromADigitCommand()
    {
        (GameState game, _, Display display, _, GameLoop loop) = Fresh("12 h");
        game.RogueLikeCommands = true;

        Assert.Equal('h', loop.ReadCommand());
        Assert.Equal(12, display.CommandCount);
    }

    /// <summary>
    /// A counted command keeps going the way it started rather than asking for a
    /// direction again.
    /// </summary>
    [Fact]
    public void ReadDirection_RemembersTheLastDirection()
    {
        (_, _, _, _, GameLoop loop) = Fresh("4");

        Assert.Equal((true, 4), loop.ReadDirection());

        loop.DefaultDirection = true;
        Assert.Equal((true, 4), loop.ReadDirection()); // no key needed
    }

    [Fact]
    public void ReadDirection_RingsTheBellAtANonDirection()
    {
        (_, _, _, MemoryScreen screen, GameLoop loop) = Fresh("x4");

        Assert.Equal((true, 4), loop.ReadDirection());
        Assert.True(screen.BellCount > 0, "no bell for a key that is not a direction");
    }

    // ------------------------------------------------------- interruptions

    /// <summary>
    /// Searching costs a point of speed and a point of food a turn, and stopping
    /// hands both back.
    /// </summary>
    [Fact]
    public void SearchOff_GivesBackTheSpeedAndTheFood()
    {
        (_, Player player, _, _, GameLoop loop) = Fresh();

        player.Status |= PlayerStatus.Searching;
        player.Speed = 1;
        player.FoodDigested = 3;

        loop.SearchOff();

        Assert.Equal(0u, player.Status & PlayerStatus.Searching);
        Assert.Equal(0, player.Speed);
        Assert.Equal(2, player.FoodDigested);
    }

    /// <summary>Anything worth noticing stops a rest, which is what keeps one safe.</summary>
    [Fact]
    public void Disturb_EndsARest()
    {
        (_, Player player, Display display, _, GameLoop loop) = Fresh();

        player.Rest = 40;
        player.Status |= PlayerStatus.Resting;
        display.CommandCount = 7;

        loop.Disturb(false, false);

        Assert.Equal(0, player.Rest);
        Assert.Equal(0u, player.Status & PlayerStatus.Resting);
        Assert.Equal(0, display.CommandCount);
    }

    /// <summary>
    /// The player's speed change is applied to every monster instead, which is
    /// what keeps the rest of the game free of relative-speed arithmetic.
    /// </summary>
    [Fact]
    public void ChangeSpeed_MovesTheMonstersInstead()
    {
        (GameState game, Player player, _, _, GameLoop loop) = Fresh();

        game.Monsters.Reset();
        int slot = game.Monsters.Allocate();
        game.Monsters[slot].Speed = 11;

        loop.ChangeSpeed(-1);

        Assert.Equal(-1, player.Speed);
        Assert.Equal(10, game.Monsters[slot].Speed);
        Assert.NotEqual(0u, player.Status & PlayerStatus.SpeedChanged);
    }

    // -------------------------------------------------------- regeneration

    /// <summary>
    /// A character regenerates far less than a point a turn, so the remainder is
    /// carried between turns rather than lost.
    /// </summary>
    [Fact]
    public void RegenerateHitPoints_CarriesTheFraction()
    {
        (_, Player player, _, _, GameLoop loop) = Fresh();

        player.MaxHitPoints = 250;
        player.CurrentHitPoints = 1;
        player.HitPointFraction = 0;

        loop.RegenerateHitPoints(GameLoop.RegenNormal);

        Assert.Equal(1, player.CurrentHitPoints);
        Assert.True(player.HitPointFraction > 0, "the fraction was thrown away");
    }

    /// <summary>The fraction is cleared at full, so nothing banks towards nothing.</summary>
    [Fact]
    public void RegenerateHitPoints_ClearsTheFractionAtFull()
    {
        (_, Player player, _, _, GameLoop loop) = Fresh();

        player.MaxHitPoints = 10;
        player.CurrentHitPoints = 10;
        player.HitPointFraction = 5000;

        loop.RegenerateHitPoints(GameLoop.RegenNormal);

        Assert.Equal(10, player.CurrentHitPoints);
        Assert.Equal(0, player.HitPointFraction);
    }

    // ------------------------------------------------------------- the turn

    /// <summary>
    /// Being sped up burns food by the square of the speed, which is what stops a
    /// hasted character crossing the dungeon on one ration.
    /// </summary>
    [Fact]
    public void TurnUpkeep_BurnsMoreFoodWhenHasted()
    {
        (_, Player slow, _, _, GameLoop slowLoop) = Fresh();
        slow.Food = 5000;
        slow.FoodDigested = 2;
        slow.Paralysis = 1;
        slowLoop.TurnUpkeep();

        (_, Player fast, _, _, GameLoop fastLoop) = Fresh();
        fast.Food = 5000;
        fast.FoodDigested = 2;
        fast.Speed = -3;
        fast.Paralysis = 1;
        fastLoop.TurnUpkeep();

        Assert.Equal(4998, slow.Food);
        Assert.Equal(4989, fast.Food); // nine more, for a speed of three
    }

    /// <summary>
    /// Hunger is reported once as it crosses each mark, rather than every turn
    /// after it.
    /// </summary>
    [Fact]
    public void TurnUpkeep_ReportsHungerOnce()
    {
        (_, Player player, Display display, MemoryScreen screen, GameLoop loop) =
            Fresh(new string(' ', 8));

        player.Food = 1500;
        player.FoodDigested = 2;
        player.Paralysis = 10;

        loop.TurnUpkeep();
        Assert.NotEqual(0u, player.Status & PlayerStatus.Hungry);
        Assert.Contains("hungry", screen.GetRow(0), StringComparison.Ordinal);

        display.MessagePrint(null);
        loop.TurnUpkeep();
        Assert.Equal(string.Empty, screen.GetRow(0).Trim());
    }

    /// <summary>
    /// Heroism raises the maximum hit points and hands them back when it goes,
    /// trimming the current total only if it would be left above the new
    /// maximum.
    ///
    /// FAITHFUL QUIRK: a wounded hero therefore keeps the ten points the potion
    /// gave, because nothing takes them off the current total on the way out.
    /// </summary>
    [Fact]
    public void TurnUpkeep_HeroismLendsHitPointsAndTakesThemBack()
    {
        (_, Player player, _, _, GameLoop loop) = Fresh();

        player.Food = 5000;
        int maximum = player.MaxHitPoints;
        player.CurrentHitPoints = 1;
        player.Hero = 1;
        player.Paralysis = 5;

        loop.TurnUpkeep();

        Assert.Equal(maximum, player.MaxHitPoints);
        Assert.Equal(11, player.CurrentHitPoints); // the ten it gave, kept
        Assert.Equal(0u, player.Status & PlayerStatus.Heroism);
    }

    /// <summary>
    /// Heroism cancels fear outright rather than counting it down.
    ///
    /// FAITHFUL QUIRK: the counter is zeroed and then decremented anyway, so it
    /// ends up at minus one rather than nought. Nothing reads it as anything but
    /// "not afraid", so the difference never shows.
    /// </summary>
    [Fact]
    public void TurnUpkeep_HeroismEndsFear()
    {
        (_, Player player, _, _, GameLoop loop) = Fresh();

        player.Food = 5000;
        player.Afraid = 20;
        player.Hero = 5;
        player.Paralysis = 5;

        loop.TurnUpkeep();

        Assert.Equal(-1, player.Afraid);
    }

    /// <summary>
    /// Resting until healed counts up rather than down, and stops the moment
    /// everything is full.
    /// </summary>
    [Fact]
    public void TurnUpkeep_RestUntilHealedStopsWhenFull()
    {
        (_, Player player, _, _, GameLoop loop) = Fresh();

        player.Food = 5000;
        player.CurrentHitPoints = player.MaxHitPoints;
        player.MaxMana = 0;
        player.CurrentMana = 0;
        player.Rest = -100;
        player.Status |= PlayerStatus.Resting;
        player.Paralysis = 3;

        loop.TurnUpkeep();

        Assert.Equal(0, player.Rest);
        Assert.Equal(0u, player.Status & PlayerStatus.Resting);
    }

    /// <summary>
    /// Word of recall fires when its countdown reaches one, and pulls the player
    /// out of the dungeon.
    /// </summary>
    [Fact]
    public void TurnUpkeep_WordOfRecallLeavesTheLevel()
    {
        (GameState game, Player player, _, _, GameLoop loop) = Fresh();

        game.DungeonLevel = 15;
        player.Food = 5000;
        player.WordOfRecall = 1;
        player.Paralysis = 3;

        loop.TurnUpkeep();

        Assert.True(loop.NewLevel);
        Assert.Equal(0, game.DungeonLevel);
        Assert.Equal(0, player.WordOfRecall);
    }

    /// <summary>Starving takes hit points off every turn, and can kill.</summary>
    [Fact]
    public void TurnUpkeep_StarvationWounds()
    {
        (_, Player player, _, _, GameLoop loop) = Fresh();

        player.Food = -160;
        player.FoodDigested = 2;
        player.CurrentHitPoints = 20;
        player.MaxHitPoints = 20;
        player.Paralysis = 3;

        loop.TurnUpkeep();

        Assert.True(player.CurrentHitPoints < 20, "starvation did no damage");
    }

    [Fact]
    public void TakeHit_KillsAndRecordsWhatDidIt()
    {
        (_, Player player, _, _, GameLoop loop) = Fresh();

        player.CurrentHitPoints = 2;
        loop.TakeHit(5, "a fall");

        Assert.True(loop.Dead);
        Assert.True(loop.NewLevel);
        Assert.Equal("a fall", loop.DiedFrom);
    }

    /// <summary>Nothing gets through while the skin is steel.</summary>
    [Fact]
    public void TakeHit_IsHarmlessWhileInvulnerable()
    {
        (_, Player player, _, _, GameLoop loop) = Fresh();

        player.CurrentHitPoints = 2;
        player.Invulnerable = 5;
        loop.TakeHit(50, "a dragon");

        Assert.Equal(2, player.CurrentHitPoints);
        Assert.False(loop.Dead);
    }

    // ---------------------------------------------------------- the dispatch

    /// <summary>Quitting asks first, and a no leaves the game running.</summary>
    [Fact]
    public void Run_QuitAsksBeforeItEndsTheGame()
    {
        (GameState game, Player player, _, MemoryScreen screen, GameLoop loop) = Fresh();

        game.DungeonLevel = 1;
        player.Food = 5000;

        // A no to the first ask, then a yes to the second.
        screen.SendKeys(Keys.Control('K').ToString() + "n" + Keys.Control('K') + "y");

        loop.Run();

        Assert.True(loop.Dead);
        Assert.Equal("Quitting", loop.DiedFrom);
    }

    /// <summary>
    /// A command that is not ported yet says so, rather than looking like a
    /// command that did nothing.
    /// </summary>
    [Fact]
    public void DoCommand_SaysWhenACommandIsNotPorted()
    {
        (GameState game, _, Display display, MemoryScreen screen, _) = Fresh();

        var loop = new ProbeLoop(game, display);
        loop.Dispatch('i');

        Assert.Contains("not ported yet", screen.GetRow(0), StringComparison.Ordinal);
        Assert.True(loop.FreeTurn, "an unported command still took a turn");
    }

    /// <summary>Reaches the dispatch, which is protected so the loop owns it.</summary>
    private sealed class ProbeLoop(GameState game, Display display) : GameLoop(game, display)
    {
        public void Dispatch(char command) => DoCommand(command);
    }
}
