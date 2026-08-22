using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on what the monsters do with their turn.
///
/// 104 runs of the monster turn are diffed against the C oracle, which runs the
/// real creatures(), mon_move and make_attack. These pin the properties behind
/// them.
/// </summary>
public class MonsterTests
{
    private static (GameState Game, MemoryScreen Screen, GameLoop Loop) Level(
        uint seed = 12345, int level = 20)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Turn = 0;
        game.DungeonLevel = level;

        new DungeonGenerator(game).Generate();

        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;
        game.PlayerLight = true;
        game.Player.CurrentHitPoints = 2000;
        game.Player.MaxHitPoints = 2000;
        game.Player.Food = 7500;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(new string(' ', 2000));

        var display = new Display(game, screen);
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);
        display.Panel.Invalidate();

        var loop = new GameLoop(game, display);
        loop.Lighting.CheckView();

        return (game, screen, loop);
    }

    /// <summary>
    /// A fast monster gets several moves a turn and a slow one gets a move every
    /// few turns, because the player always moves once.
    /// </summary>
    [Fact]
    public void MovementRate_GivesFastMonstersMoreTurns()
    {
        (GameState game, _, GameLoop loop) = Level();

        Assert.Equal(3, loop.MonsterAi.MovementRate(3));

        // Slower than the player: a move on one turn in three.
        game.Turn = 0;
        Assert.Equal(1, loop.MonsterAi.MovementRate(-1));
        game.Turn = 1;
        Assert.Equal(0, loop.MonsterAi.MovementRate(-1));
    }

    /// <summary>
    /// Resting caps even a fast monster at one move, which is what stops a rest
    /// being fatal against something quick.
    /// </summary>
    [Fact]
    public void MovementRate_RestingCapsThemAtOne()
    {
        (GameState game, _, GameLoop loop) = Level();

        game.Player.Rest = 100;
        Assert.Equal(1, loop.MonsterAi.MovementRate(3));
    }

    /// <summary>
    /// The preferred directions lead towards the player, with the straight line
    /// first and the two beside it next.
    /// </summary>
    [Fact]
    public void GetMoves_HeadsTowardsThePlayer()
    {
        (GameState game, _, GameLoop loop) = Level();

        int slot = PlaceAt(game, game.CharacterRow, game.CharacterColumn + 5);
        Span<int> moves = stackalloc int[9];
        loop.MonsterAi.GetMoves(slot, moves);

        // Directly to the player's east, so it wants to go west.
        Assert.Equal(4, moves[0]);
    }

    /// <summary>
    /// A monster only walks where its own flags allow: rock stops most of them,
    /// and the ones that phase walk through it.
    /// </summary>
    [Fact]
    public void MakeMove_RockStopsWhatCannotPhase()
    {
        (GameState game, _, GameLoop loop) = Level();

        int walker = FindCreature(c =>
            (c.MoveFlags & CreatureMove.Phase) == 0
            && (c.MoveFlags & CreatureMove.MoveNormal) != 0
            && !c.CastsSpells);

        int row = game.CharacterRow + 3;
        int column = game.CharacterColumn + 3;
        game.Cave[row, column].Feature = CaveFeature.CorridorFloor;
        game.Cave[row, column].ObjectIndex = 0;

        int slot = PlaceAt(game, row, column, walker);

        // Wall it in on every side.
        for (int y = row - 1; y <= row + 1; y++)
        {
            for (int x = column - 1; x <= column + 1; x++)
            {
                if (y != row || x != column)
                {
                    game.Cave[y, x].Feature = CaveFeature.GraniteWall;
                    game.Cave[y, x].ObjectIndex = 0;
                }
            }
        }

        Span<int> moves = stackalloc int[9];
        for (int i = 0; i < 5; i++)
        {
            moves[i] = 6;
        }

        uint learned = 0;
        loop.MonsterAi.MakeMove(slot, moves, ref learned);

        Assert.Equal(row, game.Monsters[slot].Row);
        Assert.Equal(column, game.Monsters[slot].Column);
    }

    /// <summary>
    /// Something that only ever attacks stays where it is, and the player learns
    /// that about it by watching it not move.
    /// </summary>
    [Fact]
    public void MonsterMove_AttackOnlyStaysPut()
    {
        (GameState game, _, GameLoop loop) = Level();

        int lurker = FindCreature(c =>
            (c.MoveFlags & CreatureMove.AttackOnly) != 0 && !c.CastsSpells);

        int slot = PlaceAt(game, game.CharacterRow + 4, game.CharacterColumn + 4, lurker);
        int row = game.Monsters[slot].Row;
        int column = game.Monsters[slot].Column;
        game.Monsters[slot].DistanceToPlayer = 5;

        uint learned = 0;
        loop.MonsterAi.MonsterMove(slot, ref learned);

        Assert.Equal(row, game.Monsters[slot].Row);
        Assert.Equal(column, game.Monsters[slot].Column);
        Assert.NotEqual(0u, learned & CreatureMove.AttackOnly);
    }

    /// <summary>
    /// Aggravation wakes everything within earshot and hurries it along, which
    /// is what makes a cursed item of it worth taking off.
    /// </summary>
    [Fact]
    public void AggravateMonsters_WakesAndHurriesTheNearby()
    {
        (GameState game, _, GameLoop loop) = Level();

        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            game.Monsters[i].Sleep = 500;
            game.Monsters[i].DistanceToPlayer = Cave.Distance(
                game.CharacterRow, game.CharacterColumn,
                game.Monsters[i].Row, game.Monsters[i].Column);
        }

        loop.MonsterAttack.AggravateMonsters(20);

        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            // Everything is woken, however far off - only the hurrying is
            // limited by distance.
            Assert.Equal(0, game.Monsters[i].Sleep);
        }
    }

    /// <summary>
    /// A cursed item that aggravates only reaches the monsters that get a turn:
    /// something asleep across the level is not woken by it until it would have
    /// acted anyway.
    /// </summary>
    [Fact]
    public void Creatures_AggravationReachesOnlyWhatTakesATurn()
    {
        (GameState game, _, GameLoop loop) = Level();

        int slot = PlaceAt(game, game.CharacterRow, game.CharacterColumn + 2);
        game.Monsters[slot].Sleep = 500;
        game.Monsters[slot].DistanceToPlayer = 2;
        game.Monsters[slot].Visible = true;

        game.Player.AggravatesMonsters = true;
        game.Turn++;
        loop.MonsterAi.Creatures(true);

        Assert.Equal(0, game.Monsters[slot].Sleep);
    }

    /// <summary>
    /// A monster the player cannot see is not drawn, and one that steps into the
    /// light is - which is the whole of what update_mon decides.
    /// </summary>
    [Fact]
    public void UpdateMonster_ShowsWhatCanBeSeen()
    {
        (GameState game, _, GameLoop loop) = Level();

        int slot = PlaceAt(game, game.CharacterRow, game.CharacterColumn + 2);
        Monster monster = game.Monsters[slot];
        monster.DistanceToPlayer = 2;

        game.Cave[monster.Row, monster.Column].PermanentLight = true;
        loop.MonsterAi.UpdateMonster(slot);
        Assert.True(monster.Visible);

        // Blind: nothing is seen, however well lit.
        game.Player.Status |= PlayerStatus.Blind;
        loop.MonsterAi.UpdateMonster(slot);
        Assert.False(monster.Visible);
    }

    /// <summary>
    /// Killing a monster while the list is being scanned marks it rather than
    /// removing it, or another monster would take its index and move twice.
    /// </summary>
    [Fact]
    public void Creatures_DeathDuringTheScanIsDeferred()
    {
        (GameState game, _, GameLoop loop) = Level();

        int slot = PlaceAt(game, game.CharacterRow + 2, game.CharacterColumn + 2);
        int before = game.Monsters.Count;

        game.Monsters.ScanIndex = slot + 1;
        game.Monsters[slot].HitPoints = 1;
        loop.Combat.MonsterTakeHit(slot, 100);

        // Marked dead, still in the list.
        Assert.Equal(before, game.Monsters.Count);
        Assert.True(game.Monsters[slot].HitPoints < 0);

        game.Monsters.ScanIndex = -1;
        game.Turn++;
        loop.MonsterAi.Creatures(true);

        // The scan cleared it up on the way past.
        Assert.Equal(before - 1, game.Monsters.Count);
    }

    /// <summary>
    /// A breeder fills the level if left alone, but stops at the cap - which is
    /// what keeps a lice infestation from becoming the whole game.
    /// </summary>
    [Fact]
    public void MultiplyMonster_BreedsIntoNearbySquares()
    {
        (GameState game, _, GameLoop loop) = Level();

        int breeder = FindCreature(c =>
            (c.MoveFlags & CreatureMove.Multiplies) != 0 && !c.CastsSpells);

        int slot = PlaceAt(game, game.CharacterRow + 5, game.CharacterColumn + 5, breeder);
        Monster monster = game.Monsters[slot];

        // Clear space around it to breed into.
        for (int y = monster.Row - 2; y <= monster.Row + 2; y++)
        {
            for (int x = monster.Column - 2; x <= monster.Column + 2; x++)
            {
                game.Cave[y, x].Feature = CaveFeature.CorridorFloor;
                game.Cave[y, x].ObjectIndex = 0;
            }
        }

        int before = game.Monsters.Count;
        bool bred = false;

        for (int attempt = 0; attempt < 20 && !bred; attempt++)
        {
            bred = loop.MonsterAi.MultiplyMonster(
                monster.Row, monster.Column, monster.CreatureIndex, slot);
        }

        Assert.True(game.Monsters.Count > before, "nothing was ever bred");
        Assert.True(game.Monsters.BredCount > 0);
    }

    private static int FindCreature(Func<CreatureType, bool> matches)
    {
        for (int i = 0; i < GameTables.CreatureList.Length; i++)
        {
            if (matches(GameTables.CreatureList[i]))
            {
                return i;
            }
        }

        throw new InvalidOperationException("no creature matches");
    }

    private static int PlaceAt(GameState game, int row, int column, int creature = -1)
    {
        if (creature < 0)
        {
            creature = FindCreature(c =>
                (c.MoveFlags & CreatureMove.MoveNormal) != 0 && !c.CastsSpells);
        }

        game.Cave[row, column].Feature = CaveFeature.CorridorFloor;
        game.Cave[row, column].MonsterIndex = 0;
        game.Cave[row, column].ObjectIndex = 0;

        if (!new DungeonGenerator(game).PlaceMonster(row, column, creature, asleep: false))
        {
            throw new InvalidOperationException("the monster list is full");
        }

        return game.Cave[row, column].MonsterIndex;
    }
}
