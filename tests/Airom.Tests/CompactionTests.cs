using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on what happens when a level holds all it can.
///
/// Compaction is diffed against the C oracle across 60 runs - five depths of
/// level packed to both limits, and every surviving object and monster
/// compared. What these hold up is the parts the oracle cannot reach: the
/// refusal, which needs a level packed with creatures that may not be thrown
/// away, and the half-deletion, which only happens when the monster loop is
/// part way through the list.
/// </summary>
public class CompactionTests
{
    private static (GameState, Display, GameLoop, MemoryScreen) Game(uint seed = 1)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SetKeys(new string(' ', 400));

        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);

        for (int row = 1; row < game.Cave.Height - 1; row++)
        {
            for (int column = 1; column < game.Cave.Width - 1; column++)
            {
                game.Cave[row, column].Feature = CaveFeature.LightFloor;
            }
        }

        // In the far corner, so everything packed into the other one is beyond
        // the sixty-six squares the first pass of the search looks past.
        game.CharacterRow = game.Cave.Height - 2;
        game.CharacterColumn = game.Cave.Width - 2;

        return (game, display, loop, screen);
    }

    /// <summary>An ordinary thing to lie about: a mushroom, which may be thrown away.</summary>
    private const int OrdinaryItem = 0;

    /// <summary>Umoria's OBJ_DOWN_STAIR, which may not be.</summary>
    private const int DownStairObject = 371;

    /// <summary>Fills the object list, leaving one slot free.</summary>
    private static void PackObjects(GameState game, int kind)
    {
        for (int row = 3; row < game.Cave.Height - 1; row++)
        {
            for (int column = 1; column < game.Cave.Width - 1; column++)
            {
                if (game.Objects.Count >= ObjectPool.Capacity)
                {
                    return;
                }

                int slot = game.Objects.Allocate();
                game.Cave[row, column].ObjectIndex = slot;
                game.Objects[slot].CopyFrom(kind);
            }
        }
    }

    /// <summary>
    /// A full list is not the end of the level: distant objects are thrown away
    /// until there is room, and the slot that comes back is one of theirs.
    /// </summary>
    [Fact]
    public void Allocate_CompactsWhenTheObjectListIsFull()
    {
        (GameState game, _, _, _) = Game();

        PackObjects(game, OrdinaryItem);
        Assert.Equal(ObjectPool.Capacity, game.Objects.Count);

        int slot = game.Objects.Allocate();

        Assert.InRange(slot, ObjectPool.FirstIndex, ObjectPool.Capacity - 1);
        Assert.True(game.Objects.Count <= ObjectPool.Capacity);
    }

    /// <summary>
    /// Stairs and shop doors are never thrown away: losing them would strand
    /// the player or unmake the town.
    /// </summary>
    [Fact]
    public void CompactObjects_NeverThrowsAwayAStaircase()
    {
        (GameState game, Display display, _, _) = Game();

        // A handful of staircases among ordinary items: the ordinary ones go.
        PackObjects(game, OrdinaryItem);

        // Turn five of the packed items into staircases. They are on the first
        // packed row, which the packing certainly reached.
        var stairs = new List<(int Row, int Column)>();

        for (int column = 5; column < 10; column++)
        {
            int slot = game.Cave[3, column].ObjectIndex;

            Assert.NotEqual(0, slot);

            game.Objects[slot].CopyFrom(DownStairObject);
            stairs.Add((3, column));
        }

        new Compaction(game, display, null).CompactObjects();

        foreach ((int row, int column) in stairs)
        {
            int slot = game.Cave[row, column].ObjectIndex;

            Assert.NotEqual(0, slot);
            Assert.Equal(ItemCategory.DownStair, game.Objects[slot].TVal);
        }

        // And something ordinary did go, or the rule was never tested.
        Assert.True(game.Objects.Count < ObjectPool.Capacity);
    }

    /// <summary>
    /// Compaction can fail. The Balrog is never thrown away, so a list full of
    /// them has nothing to spare, and the monster that wanted a slot simply
    /// does not arrive.
    /// </summary>
    [Fact]
    public void Allocate_ReturnsNothingWhenNoMonsterCanBeSpared()
    {
        (GameState game, _, _, _) = Game();

        int balrog = GameTables.CreatureList.Length - 1;
        Assert.True(GameTables.CreatureList[balrog].WinsGameWhenKilled);

        while (game.Monsters.Count < MonsterPool.Capacity)
        {
            int slot = game.Monsters.Allocate();

            Monster monster = game.Monsters[slot];
            monster.CreatureIndex = balrog;
            monster.Row = 40;
            monster.Column = 40 + (slot % 100);
            monster.DistanceToPlayer = 200;
            game.Cave[monster.Row, monster.Column].MonsterIndex = slot;
        }

        Assert.Equal(-1, game.Monsters.Allocate());
        Assert.Equal(MonsterPool.Capacity, game.Monsters.Count);
    }

    /// <summary>
    /// A monster the loop is part way through is taken off the map but left in
    /// the list: closing the hole under the loop would give another monster two
    /// turns. That also means it does not count as having made room.
    /// </summary>
    [Fact]
    public void CompactMonsters_LeavesTheOneTheLoopIsOnInTheList()
    {
        (GameState game, Display display, _, _) = Game();

        int ordinary = 20;

        while (game.Monsters.Count < MonsterPool.Capacity)
        {
            int slot = game.Monsters.Allocate();

            Monster monster = game.Monsters[slot];
            monster.CreatureIndex = ordinary;
            monster.Row = 40 + (slot % 20);
            monster.Column = 40 + (slot % 100);
            monster.DistanceToPlayer = 200;
            game.Cave[monster.Row, monster.Column].MonsterIndex = slot;
        }

        // The scan is past everything, so nothing may be removed outright.
        game.Monsters.ScanIndex = MonsterPool.Capacity;

        int before = game.Monsters.Count;
        new Compaction(game, display, null).CompactMonsters();

        Assert.Equal(before, game.Monsters.Count);

        // But some of them are off the map, ready to be swept up when the loop
        // is over.
        Assert.Contains(
            Enumerable.Range(MonsterPool.FirstIndex, before - MonsterPool.FirstIndex),
            i => game.Monsters[i].HitPoints < 0);
    }

    /// <summary>
    /// A list with nothing attached to compact it says so, rather than handing
    /// out a slot that is already in use.
    /// </summary>
    [Fact]
    public void Allocate_SaysSoWhenNothingCanCompact()
    {
        var objects = new ObjectPool();
        var monsters = new MonsterPool();

        while (objects.Count < ObjectPool.Capacity)
        {
            objects.Allocate();
        }

        while (monsters.Count < MonsterPool.Capacity)
        {
            monsters.Allocate();
        }

        Assert.Throws<InvalidOperationException>(() => objects.Allocate());
        Assert.Throws<InvalidOperationException>(() => monsters.Allocate());
    }
}
