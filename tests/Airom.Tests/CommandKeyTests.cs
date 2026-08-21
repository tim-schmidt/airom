using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks that each key reaches the command it is meant to.
///
/// The dispatch speaks one vocabulary - the rogue-like one - and the original
/// set is translated into it on the way in. Binding an original key in the
/// dispatch therefore does two kinds of damage at once: the rogue-like player
/// loses the command, and both players lose whatever the translation produces
/// for that letter. That is how "b" came to open a spell book instead of
/// walking down and left, in both command sets.
///
/// The oracle's dispatch mode presses all 127 keys through both sets and
/// compares every one against the original; these pin the handful that were
/// actually wrong, so the regression has a name.
/// </summary>
public class CommandKeyTests
{
    private static (GameState, MemoryScreen, GameLoop) Fresh(bool rogue, uint seed = 1)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.RogueLikeCommands = rogue;
        game.DungeonLevel = 1;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        new DungeonGenerator(game, display).Generate();
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);
        display.Panel.Follow(game.CharacterRow, game.CharacterColumn, force: true);
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

        return (game, screen, loop);
    }

    /// <summary>
    /// Types one key the way a player would - through the translation - and
    /// reports what moved and what was said.
    /// </summary>
    private static (int Row, int Column, string Said) Press(
        string keys, bool rogue, uint seed = 1)
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh(rogue, seed);

        screen.SetKeys(keys + new string(Keys.Escape, 80));

        int row = game.CharacterRow;
        int column = game.CharacterColumn;

        loop.DispatchForOracle(loop.ReadCommand());

        return (game.CharacterRow - row, game.CharacterColumn - column,
            screen.GetRow(0).Trim());
    }

    /// <summary>
    /// The eight ways of walking, in both sets. Down-left and up-right are the
    /// two that were shadowed - "b" by browsing a book and "u" by using a
    /// staff - so a player could not walk into either corner.
    /// </summary>
    [Theory]
    [InlineData("b", "1", 1, -1)]
    [InlineData("j", "2", 1, 0)]
    [InlineData("n", "3", 1, 1)]
    [InlineData("h", "4", 0, -1)]
    [InlineData("l", "6", 0, 1)]
    [InlineData("y", "7", -1, -1)]
    [InlineData("k", "8", -1, 0)]
    [InlineData("u", "9", -1, 1)]
    public void Walking_WorksInBothCommandSets(
        string rogueKey, string originalKey, int rows, int columns)
    {
        // A wall may be in the way, so what is checked is the attempt: either
        // the player moved as asked, or something stopped them - never a
        // prompt about books or staffs.
        foreach ((string key, bool rogue) in new[] { (rogueKey, true), (originalKey, false) })
        {
            for (uint seed = 1; seed <= 8; seed++)
            {
                (int movedRows, int movedColumns, string said) = Press(key, rogue, seed);

                Assert.DoesNotContain("book", said, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("staff", said, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("wand", said, StringComparison.OrdinalIgnoreCase);

                if (movedRows != 0 || movedColumns != 0)
                {
                    Assert.Equal((rows, columns), (movedRows, movedColumns));
                    return;
                }
            }
        }

        Assert.Fail("the player never moved in eight tries, in either command set");
    }

    /// <summary>Stairs are taken with the same two keys in both sets.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stairs_AreTakenWithAngleBrackets(bool rogue)
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh(rogue);

        screen.SetKeys(">" + new string(Keys.Escape, 40));

        int slot = game.Objects.Allocate();
        game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = slot;
        game.Objects[slot].CopyFrom(371);

        int was = game.DungeonLevel;
        loop.DispatchForOracle(loop.ReadCommand());

        Assert.Equal(was + 1, game.DungeonLevel);
        Assert.True(loop.NewLevel);
        Assert.Contains("maze of down staircases", screen.GetRow(0), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stairs_SayWhenThereAreNone(bool rogue)
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh(rogue);

        screen.SetKeys("<" + new string(Keys.Escape, 40));
        game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = 0;

        loop.DispatchForOracle(loop.ReadCommand());

        Assert.Contains("no up staircase", screen.GetRow(0), StringComparison.Ordinal);
        Assert.True(loop.FreeTurn, "looking for stairs that are not there took a turn");
    }

    /// <summary>
    /// The commands whose keys differ between the sets, each typed the way its
    /// own player types it, and each landing in the same place.
    /// </summary>
    [Theory]
    [InlineData("P", "b")]      // browse a book
    [InlineData("z", "a")]      // aim a wand
    [InlineData("Z", "u")]      // use a staff
    [InlineData("t", "f")]      // throw something
    [InlineData("T", "t")]      // take something off
    [InlineData("X", "x")]      // exchange weapons
    [InlineData("x", "l")]      // look around
    public void Commands_ReachTheSamePlaceFromEitherSet(string rogueKey, string originalKey)
    {
        string fromRogue = Press(rogueKey, rogue: true).Said;
        string fromOriginal = Press(originalKey, rogue: false).Said;

        Assert.Equal(fromRogue, fromOriginal);

        // And the command was reached rather than refused, which is the whole
        // failure being guarded against.
        Assert.DoesNotContain("Type '?'", fromRogue, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reading a book over is free, whatever the browse itself did: the
    /// dispatch hands the turn back afterwards.
    /// </summary>
    [Fact]
    public void Browsing_TakesNoTurn()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh(rogue: true);

        screen.SetKeys("P" + new string(Keys.Escape, 40));
        loop.DispatchForOracle(loop.ReadCommand());

        Assert.True(loop.FreeTurn, "browsing a book took a turn");
    }

    /// <summary>
    /// Searching is a state, not an act: the toggle turns it on and off, and
    /// costs a point of speed and a point of food while it is on.
    /// </summary>
    [Fact]
    public void SearchToggle_TurnsSearchingOnAndOff()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh(rogue: true);

        screen.SetKeys("##" + new string(Keys.Escape, 40));

        int speed = game.Player.Speed;
        int digested = game.Player.FoodDigested;

        loop.DispatchForOracle(loop.ReadCommand());

        Assert.NotEqual(0u, game.Player.Status & PlayerStatus.Searching);
        Assert.Equal(speed + 1, game.Player.Speed);
        Assert.Equal(digested + 1, game.Player.FoodDigested);

        loop.DispatchForOracle(loop.ReadCommand());

        Assert.Equal(0u, game.Player.Status & PlayerStatus.Searching);
        Assert.Equal(speed, game.Player.Speed);
        Assert.Equal(digested, game.Player.FoodDigested);
    }

    /// <summary>A rest is asked for, counted down, and stopped by anything.</summary>
    [Fact]
    public void Rest_TakesACountAndSetsTheState()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh(rogue: true);

        screen.SetKeys("R20\r" + new string(Keys.Escape, 40));
        loop.DispatchForOracle(loop.ReadCommand());

        Assert.Equal(20, game.Player.Rest);
        Assert.NotEqual(0u, game.Player.Status & PlayerStatus.Resting);
    }

    /// <summary>A star rests until something happens, which is the short way to say forever.</summary>
    [Fact]
    public void Rest_TakesAStarForAsLongAsItTakes()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh(rogue: true);

        screen.SetKeys("R*\r" + new string(Keys.Escape, 40));
        loop.DispatchForOracle(loop.ReadCommand());

        Assert.Equal(-GameLoop.MaxShort, game.Player.Rest);
    }

    [Fact]
    public void Rest_RefusesANonsenseCount()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh(rogue: true);

        screen.SetKeys("R-5\r" + new string(Keys.Escape, 40));
        loop.DispatchForOracle(loop.ReadCommand());

        Assert.Equal(0, game.Player.Rest);
        Assert.True(loop.FreeTurn, "a refused rest took a turn");
    }

    /// <summary>
    /// A lamp takes a flask of oil, and says how full it is afterwards.
    /// </summary>
    [Fact]
    public void RefillLamp_PoursAFlaskIn()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh(rogue: true);

        screen.SetKeys("F" + new string(Keys.Escape, 40));

        // A lamp, which is the light source with a subvalue of nought.
        game.Inventory[Inventory.LightSlot].CopyFrom(FirstOfKind(ItemCategory.Light, 0));
        game.Inventory[Inventory.LightSlot].P1 = 100;

        var flask = new InvenType();
        flask.CopyFrom(FirstOfKind(ItemCategory.Flask, null));
        game.Inventory.Carry(flask);

        loop.DispatchForOracle(loop.ReadCommand());

        Assert.True(game.Inventory[Inventory.LightSlot].P1 > 100, "the lamp was not filled");
        Assert.Contains("lamp is", screen.GetRow(0), StringComparison.Ordinal);
    }

    [Fact]
    public void RefillLamp_SaysWhenThereIsNoLamp()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh(rogue: true);

        screen.SetKeys("F" + new string(Keys.Escape, 40));

        // A torch, which is a light source with a subvalue that is not nought.
        game.Inventory[Inventory.LightSlot].CopyFrom(365);

        loop.DispatchForOracle(loop.ReadCommand());

        Assert.Contains("not using a lamp", screen.GetRow(0), StringComparison.Ordinal);
        Assert.True(loop.FreeTurn);
    }

    /// <summary>Finds the first row of the object table of a kind.</summary>
    private static int FirstOfKind(int category, int? subValue)
    {
        for (int i = 0; i < GameTables.ObjectList.Length; i++)
        {
            if (GameTables.ObjectList[i].TVal == category
                && (subValue is null || GameTables.ObjectList[i].SubVal == subValue))
            {
                return i;
            }
        }

        throw new InvalidOperationException("no such object");
    }
}
