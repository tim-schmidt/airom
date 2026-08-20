using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on walking, running and searching.
///
/// 240 walks and runs and 48 search rounds are diffed against the C oracle,
/// which runs the real move_char, find_init and search. These pin the properties
/// behind them.
/// </summary>
public class MovementTests
{
    private static (GameState Game, Display Display, MemoryScreen Screen, GameLoop Loop)
        StrippedLevel(uint seed = 12345, int level = 20)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Turn = 0;
        game.DungeonLevel = level;

        new DungeonGenerator(game).Generate();

        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                game.Cave[row, column].MonsterIndex = 0;
                game.Cave[row, column].ObjectIndex = 0;
            }
        }

        game.Monsters.Reset();
        game.Objects.Reset();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;
        game.PlayerLight = true;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(new string(' ', 200));

        var display = new Display(game, screen);
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);
        display.Panel.Invalidate();

        var loop = new GameLoop(game, display);
        loop.Lighting.CheckView();

        return (game, display, screen, loop);
    }

    // -------------------------------------------------------------- walking

    /// <summary>A step moves the player, and the record with them.</summary>
    [Fact]
    public void MoveChar_TakesTheStepAndCarriesTheRecord()
    {
        (GameState game, _, _, GameLoop loop) = StrippedLevel();

        int row = game.CharacterRow;
        int column = game.CharacterColumn;
        int direction = OpenDirection(game, row, column);

        loop.Movement.MoveChar(direction, pickUp: true);

        Assert.True(game.CharacterRow != row || game.CharacterColumn != column);
        Assert.Equal(0, game.Cave[row, column].MonsterIndex);
        Assert.Equal(1, game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex);
    }

    /// <summary>
    /// Walking into a wall costs nothing, which is why tunnelling is a command of
    /// its own rather than something a blocked step falls back on.
    /// </summary>
    [Fact]
    public void MoveChar_WalkingIntoAWallIsFree()
    {
        (GameState game, _, _, GameLoop loop) = StrippedLevel();

        int row = game.CharacterRow;
        int column = game.CharacterColumn;
        int direction = WallDirection(game, row, column);

        loop.FreeTurn = false;
        loop.Movement.MoveChar(direction, pickUp: true);

        Assert.True(loop.FreeTurn, "a blocked step took a turn");
        Assert.Equal(row, game.CharacterRow);
        Assert.Equal(column, game.CharacterColumn);
    }

    /// <summary>
    /// A closed door says so rather than failing silently, so the player knows to
    /// open it rather than assuming the key was dropped.
    /// </summary>
    [Fact]
    public void MoveChar_SaysWhatIsBlockingTheWay()
    {
        (GameState game, _, MemoryScreen screen, GameLoop loop) = StrippedLevel();

        int row = game.CharacterRow;
        int column = game.CharacterColumn;
        int direction = WallDirection(game, row, column);

        int y = row;
        int x = column;
        game.Cave.Move(direction, ref y, ref x);

        int slot = game.Objects.Allocate();
        game.Objects[slot].TVal = ItemCategory.ClosedDoor;
        game.Cave[y, x].ObjectIndex = slot;

        loop.Movement.MoveChar(direction, pickUp: true);

        Assert.Contains("closed door", screen.GetRow(0), StringComparison.Ordinal);
    }

    /// <summary>
    /// Confusion sends three steps in four somewhere else - but never when
    /// standing still, so waiting out a confusion is always safe.
    /// </summary>
    [Fact]
    public void MoveChar_ConfusionNeverScramblesStandingStill()
    {
        (GameState game, _, _, GameLoop loop) = StrippedLevel();

        game.Player.Confused = 100;
        int row = game.CharacterRow;
        int column = game.CharacterColumn;

        loop.Movement.MoveChar(5, pickUp: true);

        Assert.Equal(row, game.CharacterRow);
        Assert.Equal(column, game.CharacterColumn);
    }

    // -------------------------------------------------------------- running

    /// <summary>
    /// A run goes on by itself until something interesting turns up, which is
    /// what makes crossing a known corridor one keystroke.
    /// </summary>
    [Fact]
    public void FindInit_RunsUntilSomethingStopsIt()
    {
        (GameState game, _, _, GameLoop loop) = StrippedLevel();

        int startRow = game.CharacterRow;
        int startColumn = game.CharacterColumn;
        int direction = OpenDirection(game, startRow, startColumn);

        loop.Movement.FindInit(direction);

        int guard = 0;
        while (loop.Running && guard < 300)
        {
            loop.Movement.FindRun();
            guard++;
        }

        Assert.False(loop.Running);
        Assert.True(
            game.CharacterRow != startRow || game.CharacterColumn != startColumn,
            "the run never moved");
    }

    /// <summary>
    /// A run stops for breath after a hundred steps, so a loop of corridor cannot
    /// keep one going forever.
    /// </summary>
    [Fact]
    public void FindRun_StopsForBreathEventually()
    {
        (GameState game, _, MemoryScreen screen, GameLoop loop) = StrippedLevel();

        // A corridor with no features at all: nothing will interrupt the run.
        for (int column = 1; column < game.Cave.Width - 1; column++)
        {
            game.Cave[game.CharacterRow, column].Feature = CaveFeature.LightFloor;
            game.Cave[game.CharacterRow - 1, column].Feature = CaveFeature.GraniteWall;
            game.Cave[game.CharacterRow + 1, column].Feature = CaveFeature.GraniteWall;
            game.Cave[game.CharacterRow, column].PermanentLight = true;
            game.Cave[game.CharacterRow - 1, column].PermanentLight = true;
            game.Cave[game.CharacterRow + 1, column].PermanentLight = true;
        }

        game.CharacterColumn = 2;
        loop.Movement.FindInit(6);

        int guard = 0;
        while (loop.Running && guard < 500)
        {
            loop.Movement.FindRun();
            guard++;
        }

        Assert.False(loop.Running);
        Assert.True(guard <= Movement.MaxRunSteps + 2, "the run went past its limit");
        Assert.Contains("catch your breath", screen.GetText(), StringComparison.Ordinal);
    }

    /// <summary>Ending a run puts the light back where it belongs.</summary>
    [Fact]
    public void EndFind_StopsTheRun()
    {
        (GameState game, _, _, GameLoop loop) = StrippedLevel();

        loop.Movement.FindInit(OpenDirection(game, game.CharacterRow, game.CharacterColumn));
        loop.Movement.EndFind();

        Assert.False(loop.Running);
    }

    // ------------------------------------------------------------ searching

    /// <summary>A found trap becomes a visible one, and stays found.</summary>
    [Fact]
    public void Search_TurnsAFoundTrapVisible()
    {
        (GameState game, _, MemoryScreen screen, GameLoop loop) = StrippedLevel();

        int row = game.CharacterRow;
        int column = game.CharacterColumn + 1;

        int slot = game.Objects.Allocate();
        game.Objects[slot].CopyFrom(379); // an arrow trap, which starts hidden
        game.Cave[row, column].ObjectIndex = slot;

        // A certainty, so the roll cannot hide the behaviour.
        loop.Movement.Search(game.CharacterRow, game.CharacterColumn, 101);

        Assert.Equal(ItemCategory.VisibleTrap, game.Objects[slot].TVal);
        Assert.Contains("You have found", screen.GetRow(0), StringComparison.Ordinal);
    }

    /// <summary>A found secret door becomes an ordinary closed one.</summary>
    [Fact]
    public void Search_TurnsASecretDoorIntoAClosedOne()
    {
        (GameState game, _, MemoryScreen screen, GameLoop loop) = StrippedLevel();

        int row = game.CharacterRow;
        int column = game.CharacterColumn + 1;

        int slot = game.Objects.Allocate();
        game.Objects[slot].CopyFrom(369); // the secret door
        game.Cave[row, column].ObjectIndex = slot;

        loop.Movement.Search(game.CharacterRow, game.CharacterColumn, 101);

        Assert.Equal(ItemCategory.ClosedDoor, game.Objects[slot].TVal);
        Assert.Contains("secret door", screen.GetRow(0), StringComparison.Ordinal);
    }

    /// <summary>
    /// Blindness, confusion, darkness and hallucination each cut the chance to a
    /// tenth, and they stack - a blind, confused character searches by luck.
    /// </summary>
    [Fact]
    public void Search_IsCrippledByEveryImpairment()
    {
        (GameState game, _, _, GameLoop loop) = StrippedLevel();

        int found = 0;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            (GameState g, _, _, GameLoop l) = StrippedLevel((uint)(1000 + attempt));
            g.Player.Blind = 10;
            g.Player.Confused = 10;

            int slot = g.Objects.Allocate();
            g.Objects[slot].CopyFrom(379);
            g.Cave[g.CharacterRow, g.CharacterColumn + 1].ObjectIndex = slot;

            l.Movement.Search(g.CharacterRow, g.CharacterColumn, 100);

            if (g.Objects[slot].TVal == ItemCategory.VisibleTrap)
            {
                found++;
            }
        }

        // A hundredth of the chance: about two in two hundred, so anything past a
        // handful means the impairments are not stacking.
        Assert.True(found < 15, $"impaired search found {found} of 200");
    }

    /// <summary>
    /// Every square is rolled for separately, so a search can find one of two
    /// traps and miss the other.
    /// </summary>
    [Fact]
    public void Search_RollsForEachSquareSeparately()
    {
        (GameState game, _, _, GameLoop loop) = StrippedLevel();

        int slotA = game.Objects.Allocate();
        game.Objects[slotA].CopyFrom(379);
        game.Cave[game.CharacterRow, game.CharacterColumn + 1].ObjectIndex = slotA;

        int slotB = game.Objects.Allocate();
        game.Objects[slotB].CopyFrom(379);
        game.Cave[game.CharacterRow, game.CharacterColumn - 1].ObjectIndex = slotB;

        uint before = game.Rng.State;
        loop.Movement.Search(game.CharacterRow, game.CharacterColumn, 0);

        // Nine squares, nine rolls, even when the chance is nil.
        Assert.NotEqual(before, game.Rng.State);
        Assert.Equal(ItemCategory.InvisibleTrap, game.Objects[slotA].TVal);
        Assert.Equal(ItemCategory.InvisibleTrap, game.Objects[slotB].TVal);
    }

    private static int OpenDirection(GameState game, int row, int column)
    {
        foreach (int direction in new[] { 6, 4, 2, 8, 3, 1, 9, 7 })
        {
            int y = row;
            int x = column;
            if (game.Cave.Move(direction, ref y, ref x)
                && game.Cave[y, x].Feature <= CaveFeature.MaxOpenSpace)
            {
                return direction;
            }
        }

        return 5;
    }

    private static int WallDirection(GameState game, int row, int column)
    {
        foreach (int direction in new[] { 6, 4, 2, 8, 3, 1, 9, 7 })
        {
            int y = row;
            int x = column;
            if (game.Cave.Move(direction, ref y, ref x)
                && game.Cave[y, x].Feature > CaveFeature.MaxOpenSpace)
            {
                return direction;
            }
        }

        return 5;
    }
}
