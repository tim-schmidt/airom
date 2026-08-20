using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on fighting, traps and doors.
///
/// 64 fights and 15 sweeps of every trap in the table are diffed against the C
/// oracle, which runs the real py_attack, monster_death and hit_trap. These pin
/// the properties behind them.
/// </summary>
public class CombatTests
{
    private static (GameState Game, MemoryScreen Screen, GameLoop Loop) Fresh(uint seed = 12345)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Turn = 0;
        game.DungeonLevel = 20;

        game.Player = new CharacterCreation(game)
            .Create(race: 0, characterClass: 0, male: true, name: "Oracle");
        game.Player.Food = 7500;

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
        screen.SendKeys(new string(' ', 500));

        var display = new Display(game, screen);
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);
        display.Panel.Invalidate();

        var loop = new GameLoop(game, display);
        display.ClearScreen();
        loop.Lighting.CheckView();

        return (game, screen, loop);
    }

    // ------------------------------------------------------------- hitting

    /// <summary>
    /// One roll in twenty always misses and one always hits, whatever the
    /// numbers say - so no armour makes a creature untouchable.
    /// </summary>
    [Fact]
    public void TestHit_AlwaysMissesAndHitsSometimes()
    {
        (_, _, GameLoop loop) = Fresh();

        int hits = 0;
        for (int i = 0; i < 400; i++)
        {
            // Hopeless odds against enormous armour: only the natural twenty
            // can land.
            if (loop.Combat.TestHit(0, 0, 0, 10000, LevelSkill.Fighting))
            {
                hits++;
            }
        }

        Assert.True(hits > 5, $"the automatic hit never landed in 400 tries ({hits})");
        Assert.True(hits < 60, $"far too many landed against impossible armour ({hits})");
    }

    /// <summary>
    /// A weapon too heavy to swing gives one clumsy blow, and the penalty is the
    /// weight the arm is short by.
    /// </summary>
    [Fact]
    public void AttackBlows_PunishesAWeaponTooHeavy()
    {
        (GameState game, _, GameLoop loop) = Fresh();
        game.Player.UseStat[Stat.Strength] = 10;

        int blows = loop.Combat.AttackBlows(500, out int penalty);

        Assert.Equal(1, blows);
        Assert.Equal((10 * 15) - 500, penalty);
    }

    /// <summary>A light weapon in a strong, nimble hand swings more than once.</summary>
    [Fact]
    public void AttackBlows_RewardsStrengthAndDexterity()
    {
        (GameState game, _, GameLoop loop) = Fresh();
        game.Player.UseStat[Stat.Strength] = 118;
        game.Player.UseStat[Stat.Dexterity] = 118;

        Assert.True(loop.Combat.AttackBlows(30, out _) > 1);
    }

    /// <summary>
    /// A weapon that slays a kind of creature does so only against that kind,
    /// and using it teaches the player what the creature is.
    /// </summary>
    [Fact]
    public void TotalDamage_MultipliesAgainstTheRightCreature()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        var sword = new InvenType();
        sword.CopyFrom(34);
        sword.Flags |= ItemFlags.SlayDragon;

        int dragon = FindCreature(c => (c.DefenseFlags & CreatureDefense.Dragon) != 0);
        int other = FindCreature(c => (c.DefenseFlags & CreatureDefense.Dragon) == 0);

        Assert.Equal(40, loop.Combat.TotalDamage(sword, 10, dragon));
        Assert.Equal(10, loop.Combat.TotalDamage(sword, 10, other));

        // Killing it with that sword is how the player learns it is a dragon.
        Assert.NotEqual(0, game.Memories[dragon].Defense & CreatureDefense.Dragon);
    }

    /// <summary>
    /// Killing something is worth less the stronger the killer, and the
    /// remainder is carried rather than dropped.
    /// </summary>
    [Fact]
    public void MonsterTakeHit_DividesExperienceByLevel()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        int creature = FindCreature(c => c.KillExperience > 0 && c.Level > 0);
        int slot = PlaceBeside(game, creature);

        game.Player.Level = 1;
        game.Monsters[slot].HitPoints = 1;
        loop.Combat.MonsterTakeHit(slot, 100);
        int atLevelOne = game.Player.Experience;

        (GameState later, _, GameLoop laterLoop) = Fresh();
        int laterSlot = PlaceBeside(later, creature);
        later.Player.Level = 10;
        later.Monsters[laterSlot].HitPoints = 1;
        laterLoop.Combat.MonsterTakeHit(laterSlot, 100);

        Assert.True(later.Player.Experience < atLevelOne,
            "the same kill was worth as much at level ten");
    }

    /// <summary>A killed monster is taken off the map as well as out of the list.</summary>
    [Fact]
    public void MonsterTakeHit_RemovesTheBody()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        int creature = FindCreature(c => c.Level > 0);
        int slot = PlaceBeside(game, creature);
        int row = game.Monsters[slot].Row;
        int column = game.Monsters[slot].Column;

        game.Monsters[slot].HitPoints = 1;
        Assert.True(loop.Combat.MonsterTakeHit(slot, 500) >= 0);

        Assert.Equal(0, game.Cave[row, column].MonsterIndex);
        Assert.Equal(MonsterPool.FirstIndex, game.Monsters.Count);
    }

    /// <summary>Killing this kind wins the game, which nothing else does.</summary>
    [Fact]
    public void MonsterDeath_WinningIsAnnounced()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh();

        loop.Combat.MonsterDeath(
            game.CharacterRow, game.CharacterColumn, CreatureMove.Win);

        Assert.True(game.TotalWinner);

        // Two messages, and the second has pushed the first off the line - so
        // the one still showing is the retirement notice.
        Assert.Contains("may retire when ready", screen.GetText(), StringComparison.Ordinal);
    }

    // --------------------------------------------------------------- traps

    /// <summary>
    /// A sprung trap is revealed whether or not it did anything, so the player
    /// learns where it was.
    /// </summary>
    [Fact]
    public void HitTrap_RevealsTheTrap()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        int slot = game.Objects.Allocate();
        game.Objects[slot].CopyFrom(379); // an arrow trap, which starts hidden
        game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = slot;

        loop.Traps.HitTrap(game.CharacterRow, game.CharacterColumn);

        Assert.Equal(ItemCategory.VisibleTrap, game.Objects[slot].TVal);
    }

    /// <summary>Feather fall turns a fall into a gentle landing, and costs nothing.</summary>
    [Fact]
    public void HitTrap_FeatherFallSavesTheFall()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh();

        game.Player.FeatherFall = true;
        game.Player.CurrentHitPoints = 100;

        int slot = game.Objects.Allocate();
        game.Objects[slot].CopyFrom(378); // an open pit
        game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = slot;

        loop.Traps.HitTrap(game.CharacterRow, game.CharacterColumn);

        Assert.Equal(100, game.Player.CurrentHitPoints);
        Assert.Contains("gently float down", screen.GetText(), StringComparison.Ordinal);
    }

    /// <summary>A trap door drops the player a level, whether they like it or not.</summary>
    [Fact]
    public void HitTrap_TrapDoorTakesThePlayerDown()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        int depth = game.DungeonLevel;
        int slot = game.Objects.Allocate();
        game.Objects[slot].CopyFrom(381); // a trap door
        game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = slot;

        loop.Traps.HitTrap(game.CharacterRow, game.CharacterColumn);

        Assert.True(loop.NewLevel);
        Assert.Equal(depth + 1, game.DungeonLevel);
    }

    /// <summary>Free action saves the holds: sleep gas does nothing to it.</summary>
    [Fact]
    public void HitTrap_FreeActionSavesTheHolds()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh();

        game.Player.FreeAction = true;

        int slot = game.Objects.Allocate();
        game.Objects[slot].CopyFrom(382); // sleep gas
        game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = slot;

        loop.Traps.HitTrap(game.CharacterRow, game.CharacterColumn);

        Assert.Equal(0, game.Player.Paralysis);
        Assert.Contains("unaffected", screen.GetText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A chest can carry several traps at once, and all of them go off - which
    /// is what makes disarming one worth the turn.
    /// </summary>
    [Fact]
    public void ChestTrap_EveryTrapOnItFires()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        game.Player.CurrentHitPoints = 200;
        game.Player.MaxHitPoints = 200;

        int slot = game.Objects.Allocate();
        game.Objects[slot].CopyFrom(367);
        game.Objects[slot].TVal = ItemCategory.Chest;
        game.Objects[slot].Flags = ChestFlags.LoseStrength | ChestFlags.Poison;
        game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = slot;

        int strength = game.Player.CurrentStat[Stat.Strength];
        loop.Traps.ChestTrap(game.CharacterRow, game.CharacterColumn);

        Assert.True(game.Player.CurrentStat[Stat.Strength] < strength, "the needle did nothing");
        Assert.True(game.Player.Poisoned > 0, "the poison did nothing");
    }

    // --------------------------------------------------------------- doors

    /// <summary>
    /// A wall dug out of a room takes on the room's floor and light, so a hole
    /// dug into a lit room is lit.
    /// </summary>
    [Fact]
    public void TunnelWall_TakesTheRoomWithIt()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        int row = game.CharacterRow;
        int column = game.CharacterColumn + 1;

        game.Cave[row, column].Feature = CaveFeature.GraniteWall;
        game.Cave[row, column].LitRoom = true;
        game.Cave[row, column - 1].Feature = CaveFeature.LightFloor;
        game.Cave[row, column - 1].PermanentLight = true;

        Assert.True(loop.Doors.TunnelWall(row, column, 10, 5));
        Assert.Equal(CaveFeature.LightFloor, game.Cave[row, column].Feature);
        Assert.True(game.Cave[row, column].PermanentLight);
    }

    /// <summary>Digging that is not good enough leaves the wall where it was.</summary>
    [Fact]
    public void TunnelWall_FailsWhenTheRockIsTooHard()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        int row = game.CharacterRow;
        int column = game.CharacterColumn + 1;
        game.Cave[row, column].Feature = CaveFeature.GraniteWall;

        Assert.False(loop.Doors.TunnelWall(row, column, 3, 10));
        Assert.Equal(CaveFeature.GraniteWall, game.Cave[row, column].Feature);
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

        return 0;
    }

    private static int PlaceBeside(GameState game, int creature)
    {
        var generator = new DungeonGenerator(game);

        for (int direction = 1; direction <= 9; direction++)
        {
            if (direction == 5)
            {
                continue;
            }

            int row = game.CharacterRow;
            int column = game.CharacterColumn;

            if (!game.Cave.Move(direction, ref row, ref column))
            {
                continue;
            }

            if (game.Cave[row, column].Feature > CaveFeature.MaxOpenSpace
                || game.Cave[row, column].MonsterIndex != 0)
            {
                continue;
            }

            if (generator.PlaceMonster(row, column, creature, asleep: false))
            {
                game.Monsters[game.Cave[row, column].MonsterIndex].Visible = true;
                return game.Cave[row, column].MonsterIndex;
            }
        }

        throw new InvalidOperationException("nowhere to put a monster");
    }
}
