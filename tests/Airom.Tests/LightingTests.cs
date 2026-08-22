using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on what the player can see.
///
/// 176 walks are diffed against the C oracle, which runs the real move_light and
/// light_room. These pin the properties behind them.
/// </summary>
public class LightingTests
{
    private static (GameState Game, Display Display, MemoryScreen Screen, Lighting Lighting)
        Level(uint seed, int level)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Turn = 0;
        game.DungeonLevel = level;

        new DungeonGenerator(game).Generate();

        var screen = new MemoryScreen();
        var display = new Display(game, screen);
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);
        display.Panel.Invalidate();

        return (game, display, screen, new Lighting(game, display));
    }

    /// <summary>
    /// The town is one screen and the dungeon is nine, and which builder runs is
    /// decided by the depth rather than by the caller.
    /// </summary>
    [Fact]
    public void Generate_SizesTheLevelForTheDepth()
    {
        (GameState town, _, _, _) = Level(12345, 0);
        Assert.Equal(DungeonGenerator.TownHeight, town.Cave.Height);
        Assert.Equal(DungeonGenerator.TownWidth, town.Cave.Width);

        (GameState dungeon, _, _, _) = Level(12345, 20);
        Assert.Equal(GameState.DungeonHeight, dungeon.Cave.Height);
        Assert.Equal(GameState.DungeonWidth, dungeon.Cave.Width);
    }

    /// <summary>
    /// A step lights the three-by-three block the player moves into, and the one
    /// they left goes dark behind them.
    /// </summary>
    [Fact]
    public void MoveLight_CarriesTheLampWithThePlayer()
    {
        (GameState game, _, _, Lighting lighting) = Level(12345, 20);
        game.PlayerLight = true;

        int row = game.CharacterRow;
        int column = game.CharacterColumn;

        lighting.MoveLight(row, column, row, column);
        Assert.True(game.Cave[row, column].TemporaryLight);
        Assert.True(game.TemporaryLightOn);

        // Step one square along, wherever there is floor to step to.
        (int toRow, int toColumn) = FindStep(game, row, column);
        lighting.MoveLight(row, column, toRow, toColumn);

        Assert.True(game.Cave[toRow, toColumn].TemporaryLight);

        // The square two behind the player is out of the new block entirely.
        int behindRow = row - (toRow - row);
        int behindColumn = column - (toColumn - column);
        Assert.False(game.Cave[behindRow, behindColumn].TemporaryLight);
    }

    /// <summary>
    /// A wall stays known once it has been lit, because a wall does not change.
    /// A floor does not, which is what keeps the dungeon dark behind the player.
    /// </summary>
    [Fact]
    public void MoveLight_RemembersWallsButNotFloors()
    {
        (GameState game, _, _, Lighting lighting) = Level(12345, 20);
        game.PlayerLight = true;

        int row = game.CharacterRow;
        int column = game.CharacterColumn;

        // Put a wall and a dark floor beside the player.
        game.Cave[row - 1, column].Feature = CaveFeature.GraniteWall;
        game.Cave[row - 1, column].PermanentLight = false;
        game.Cave[row + 1, column].Feature = CaveFeature.DarkFloor;
        game.Cave[row + 1, column].PermanentLight = false;

        lighting.MoveLight(row, column, row, column);

        Assert.True(game.Cave[row - 1, column].PermanentLight);
        Assert.False(game.Cave[row + 1, column].PermanentLight);
    }

    /// <summary>
    /// Blind, nothing is revealed: the lamp goes out and only the player's own
    /// symbol moves.
    /// </summary>
    [Fact]
    public void MoveLight_RevealsNothingWhileBlind()
    {
        (GameState game, _, _, Lighting lighting) = Level(12345, 20);
        game.PlayerLight = true;

        int row = game.CharacterRow;
        int column = game.CharacterColumn;

        lighting.MoveLight(row, column, row, column);
        Assert.True(game.TemporaryLightOn);

        game.Player.Blind = 10;
        (int toRow, int toColumn) = FindStep(game, row, column);
        lighting.MoveLight(row, column, toRow, toColumn);

        Assert.False(game.TemporaryLightOn);
        Assert.False(game.Cave[toRow, toColumn].TemporaryLight);
    }

    /// <summary>
    /// With no lamp at all the player is in the same case as being blind, which
    /// is why running out of oil is worth avoiding.
    /// </summary>
    [Fact]
    public void MoveLight_WithoutALampRevealsNothing()
    {
        (GameState game, _, _, Lighting lighting) = Level(12345, 20);
        game.PlayerLight = false;

        int row = game.CharacterRow;
        int column = game.CharacterColumn;

        lighting.MoveLight(row, column, row, column);

        Assert.False(game.Cave[row, column].TemporaryLight);
    }

    /// <summary>
    /// While running the lamp is switched off, so a long run does not repaint
    /// the same nine squares at every step - unless the player has asked to be
    /// drawn.
    /// </summary>
    [Fact]
    public void MoveLight_PutsTheLampOutWhileRunning()
    {
        (GameState game, _, _, Lighting lighting) = Level(12345, 20);
        game.PlayerLight = true;

        int row = game.CharacterRow;
        int column = game.CharacterColumn;

        lighting.MoveLight(row, column, row, column);
        Assert.True(game.TemporaryLightOn);

        game.Running = true;
        (int toRow, int toColumn) = FindStep(game, row, column);
        lighting.MoveLight(row, column, toRow, toColumn);

        Assert.False(game.TemporaryLightOn);
    }

    /// <summary>
    /// A lit room is revealed in one go rather than square by square, which is
    /// what makes stepping through a door show the whole room.
    /// </summary>
    [Fact]
    public void LightRoom_RevealsTheWholeRoomAtOnce()
    {
        (GameState game, _, _, Lighting lighting) = Level(12345, 20);

        (int row, int column) = FindLitRoomSquare(game);
        Assert.True(row >= 0, "no lit room on this level");

        int litBefore = CountLit(game);
        lighting.LightRoom(row, column);
        int litAfter = CountLit(game);

        Assert.True(litAfter > litBefore + 4, "the room was not revealed in one go");
        Assert.True(game.Cave[row, column].PermanentLight);
    }

    /// <summary>Lighting a dark floor in a lit room turns it into a lit one.</summary>
    [Fact]
    public void LightRoom_PromotesDarkFloorToLit()
    {
        (GameState game, _, _, Lighting lighting) = Level(12345, 20);

        (int row, int column) = FindLitRoomSquare(game);
        game.Cave[row, column].Feature = CaveFeature.DarkFloor;
        game.Cave[row, column].PermanentLight = false;

        lighting.LightRoom(row, column);

        Assert.Equal(CaveFeature.LightFloor, game.Cave[row, column].Feature);
    }

    /// <summary>
    /// An invisible trap is not noticed by the light passing over it, which is
    /// the whole point of one.
    /// </summary>
    [Fact]
    public void MoveLight_LeavesAnInvisibleTrapUnnoticed()
    {
        (GameState game, _, _, Lighting lighting) = Level(12345, 20);
        game.PlayerLight = true;

        int row = game.CharacterRow;
        int column = game.CharacterColumn + 1;

        int slot = game.Objects.Allocate();
        game.Objects[slot].TVal = ItemCategory.InvisibleTrap;
        game.Cave[row, column].ObjectIndex = slot;
        game.Cave[row, column].FieldMark = false;
        game.Cave[row, column].Feature = CaveFeature.LightFloor;

        lighting.MoveLight(
            game.CharacterRow, game.CharacterColumn, game.CharacterRow, game.CharacterColumn);

        Assert.False(game.Cave[row, column].FieldMark);
    }

    /// <summary>
    /// Whatever occupies a square moves with the player record, and moving a
    /// square onto itself is harmless.
    /// </summary>
    [Fact]
    public void MoveRecord_MovesTheOccupantAndSurvivesANullMove()
    {
        (GameState game, _, _, Lighting lighting) = Level(12345, 20);

        game.Cave[5, 5].MonsterIndex = 1;
        lighting.MoveRecord(5, 5, 6, 6);

        Assert.Equal(0, game.Cave[5, 5].MonsterIndex);
        Assert.Equal(1, game.Cave[6, 6].MonsterIndex);

        lighting.MoveRecord(6, 6, 6, 6);
        Assert.Equal(1, game.Cave[6, 6].MonsterIndex);
    }

    [Fact]
    public void NoLight_ReportsWhetherTheSquareIsDark()
    {
        (GameState game, _, _, Lighting lighting) = Level(12345, 20);

        game.Cave[game.CharacterRow, game.CharacterColumn].TemporaryLight = false;
        game.Cave[game.CharacterRow, game.CharacterColumn].PermanentLight = false;
        Assert.True(lighting.NoLight());

        game.Cave[game.CharacterRow, game.CharacterColumn].TemporaryLight = true;
        Assert.False(lighting.NoLight());
    }

    /// <summary>Only squares on screen are redrawn; the rest are pointless work.</summary>
    [Fact]
    public void LightSpot_DrawsOnlyWhatIsOnScreen()
    {
        (GameState game, Display display, MemoryScreen screen, Lighting lighting) =
            Level(12345, 20);

        display.Panel.Follow(game.CharacterRow, game.CharacterColumn, force: true);
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

        lighting.LightSpot(game.CharacterRow, game.CharacterColumn);

        Assert.Equal(
            '@',
            screen.GetRow(game.CharacterRow - display.Panel.RowOffset)[
                game.CharacterColumn - display.Panel.ColumnOffset]);

        // Off the panel entirely: nothing is drawn and nothing throws.
        lighting.LightSpot(display.Panel.RowMax + 5, display.Panel.ColumnMax + 5);
    }

    private static (int Row, int Column) FindStep(GameState game, int row, int column)
    {
        for (int y = row - 1; y <= row + 1; y++)
        {
            for (int x = column - 1; x <= column + 1; x++)
            {
                if ((y != row || x != column)
                    && game.Cave[y, x].Feature <= CaveFeature.MaxOpenSpace)
                {
                    return (y, x);
                }
            }
        }

        return (row, column);
    }

    private static (int Row, int Column) FindLitRoomSquare(GameState game)
    {
        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                if (game.Cave[row, column].LitRoom)
                {
                    return (row, column);
                }
            }
        }

        return (-1, -1);
    }

    private static int CountLit(GameState game)
    {
        int lit = 0;
        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                if (game.Cave[row, column].PermanentLight)
                {
                    lit++;
                }
            }
        }

        return lit;
    }
}
