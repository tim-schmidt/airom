using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the light the player carries.
///
/// Whether the player has one, and how much of it is left, are the same thing
/// asked two ways: the fuel is a number on the item in the light slot, and the
/// light is on when that number is above nought. Keeping the fuel anywhere else
/// means a lamp that lights nothing and never burns down.
/// </summary>
public class LightSourceTests
{
    /// <summary>
    /// A Brass Lantern, which burns oil and can be refilled.
    ///
    /// The table holds two of them: this one, which a player finds, and one
    /// with a subvalue of one that stocks the shops. Only a subvalue of nought
    /// counts as a lamp to the command that fills it.
    /// </summary>
    private const int Lantern = 364;

    private static (GameState, Display, GameLoop, MemoryScreen) Game(uint seed = 1)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = 1;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SetKeys(new string(Keys.Escape, 200));

        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        new DungeonGenerator(game, display).Generate();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

        return (game, display, loop, screen);
    }

    private static void Equip(GameState game, int kind, int fuel)
    {
        game.Inventory[Inventory.LightSlot].CopyFrom(kind);
        game.Inventory[Inventory.LightSlot].P1 = (short)fuel;
    }

    /// <summary>
    /// A lantern with oil in it lights the way from the moment the level is
    /// entered. The loop works this out from the item rather than being told.
    /// </summary>
    [Fact]
    public void ArrivingWithALantern_LightsTheWay()
    {
        (GameState game, _, GameLoop loop, _) = Game();

        Equip(game, Lantern, 7500);
        game.PlayerLight = false;

        loop.EnterLevel();

        Assert.True(game.PlayerLight, "the lantern lit nothing");
    }

    /// <summary>An empty lantern is no light at all.</summary>
    [Fact]
    public void ArrivingWithAnEmptyLantern_LightsNothing()
    {
        (GameState game, _, GameLoop loop, _) = Game();

        Equip(game, Lantern, 0);
        game.PlayerLight = true;

        loop.EnterLevel();

        Assert.False(game.PlayerLight, "an empty lantern lit the way");
    }

    /// <summary>
    /// Standing still still burns oil: a turn spent doing nothing is a turn.
    /// </summary>
    [Fact]
    public void StandingStill_BurnsTheOil()
    {
        (GameState game, _, GameLoop loop, _) = Game();

        Equip(game, Lantern, 7500);
        loop.EnterLevel();

        for (int i = 0; i < 5; i++)
        {
            loop.TurnUpkeep();
        }

        Assert.Equal(7495, game.Inventory[Inventory.LightSlot].P1);
        Assert.True(game.PlayerLight);
    }

    /// <summary>
    /// A lamp that burns out says so, and the player is left in the dark.
    /// </summary>
    [Fact]
    public void RunningOut_SaysSoAndLeavesTheDark()
    {
        (GameState game, _, GameLoop loop, MemoryScreen screen) = Game();

        Equip(game, Lantern, 1);
        loop.EnterLevel();

        loop.TurnUpkeep();

        Assert.Equal(0, game.Inventory[Inventory.LightSlot].P1);
        Assert.False(game.PlayerLight);
        Assert.Contains("light has gone out", screen.GetRow(0), StringComparison.Ordinal);
    }

    /// <summary>
    /// The fuel a command changes is the fuel a turn burns. Filling a lamp and
    /// then standing about should show the one in the other.
    /// </summary>
    [Fact]
    public void FillingALamp_AddsToWhatTheTurnBurns()
    {
        (GameState game, _, GameLoop loop, MemoryScreen screen) = Game();

        Equip(game, Lantern, 100);

        var flask = new InvenType();
        flask.CopyFrom(FirstOfCategory(ItemCategory.Flask));
        int inFlask = flask.P1;
        game.Inventory.Carry(flask);

        screen.SetKeys("F" + new string(Keys.Escape, 40));
        loop.DispatchForOracle(loop.ReadCommand());

        Assert.Equal(100 + inFlask, game.Inventory[Inventory.LightSlot].P1);

        loop.EnterLevel();
        loop.TurnUpkeep();

        Assert.Equal(100 + inFlask - 1, game.Inventory[Inventory.LightSlot].P1);
    }

    /// <summary>
    /// Something that drinks the lamp leaves a mouthful rather than blinding
    /// the player outright.
    /// </summary>
    [Fact]
    public void ADrainedLamp_IsNeverQuiteEmptied()
    {
        (GameState game, _, GameLoop loop, _) = Game();

        Equip(game, Lantern, 100);

        // The drain takes between 250 and 500, which is more than is there.
        loop.EatLightForTest();

        Assert.Equal(1, game.Inventory[Inventory.LightSlot].P1);
    }

    private static int FirstOfCategory(int category)
    {
        for (int i = 0; i < GameTables.ObjectList.Length; i++)
        {
            if (GameTables.ObjectList[i].TVal == category)
            {
                return i;
            }
        }

        throw new InvalidOperationException("no such object");
    }
}
