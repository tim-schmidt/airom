using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the spell engine and on monsters casting.
///
/// 24 runs with the player ringed by spellcasters are diffed against the C
/// oracle, which runs the real mon_cast_spell, breath and fire_bolt. These pin
/// the properties behind them.
/// </summary>
public class SpellTests
{
    private static (GameState Game, MemoryScreen Screen, GameLoop Loop) Level(
        uint seed = 12345, int level = 30)
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
            }
        }

        game.Monsters.Reset();
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
    /// What a creature is made of decides what hurts it: a weakness doubles the
    /// damage and a matching breath cuts it to a quarter.
    /// </summary>
    [Fact]
    public void FireBolt_DamageDependsOnWhatTheTargetIsMadeOf()
    {
        int vulnerable = FindCreature(c =>
            (c.DefenseFlags & CreatureDefense.HurtByFire) != 0 && c.HitDiceCount > 5);

        int resistant = FindCreature(c =>
            (c.SpellFlags & CreatureSpell.BreatheFire) != 0
            && (c.DefenseFlags & CreatureDefense.HurtByFire) == 0
            && c.HitDiceCount > 5);

        int hurtBurned = DamageDealt(vulnerable, SpellElement.Fire);
        int hurtResisted = DamageDealt(resistant, SpellElement.Fire);

        Assert.True(hurtBurned > hurtResisted,
            $"burning {hurtBurned} was no worse than resisted {hurtResisted}");
    }

    private static int DamageDealt(int creature, int element)
    {
        (GameState game, _, GameLoop loop) = Level();

        int row = game.CharacterRow;
        int column = game.CharacterColumn + 1;
        game.Cave[row, column].Feature = CaveFeature.CorridorFloor;

        if (!new DungeonGenerator(game).PlaceMonster(row, column, creature, asleep: true))
        {
            throw new InvalidOperationException("could not place the target");
        }

        int slot = game.Cave[row, column].MonsterIndex;
        game.Monsters[slot].HitPoints = 5000;

        loop.Spells.FireBolt(element, 6, game.CharacterRow, game.CharacterColumn, 100, "fire");

        return 5000 - game.Monsters[slot].HitPoints;
    }

    /// <summary>
    /// A ball bursts over everything within two squares, and the damage falls
    /// off with distance from the middle.
    /// </summary>
    [Fact]
    public void FireBall_HitsEverythingNearby()
    {
        (GameState game, _, GameLoop loop) = Level();

        int kind = FindCreature(c => c.Level > 0 && c.HitDiceCount > 4);
        var slots = new List<int>();

        // A short line of monsters, so the ball reaches more than one.
        for (int offset = 3; offset <= 5; offset++)
        {
            int row = game.CharacterRow;
            int column = game.CharacterColumn + offset;
            game.Cave[row, column].Feature = CaveFeature.CorridorFloor;
            game.Cave[row, column].ObjectIndex = 0;

            if (new DungeonGenerator(game).PlaceMonster(row, column, kind, asleep: true))
            {
                int slot = game.Cave[row, column].MonsterIndex;
                game.Monsters[slot].HitPoints = 5000;
                slots.Add(slot);
            }
        }

        loop.Spells.FireBall(
            SpellElement.Fire, 6, game.CharacterRow, game.CharacterColumn, 200, "fire");

        int hurt = slots.Count(slot => game.Monsters[slot].HitPoints < 5000);
        Assert.True(hurt > 1, $"the ball only reached {hurt} of {slots.Count}");
    }

    /// <summary>
    /// A breath is a ball centred on the breather, and it hurts the player
    /// rather than earning them anything.
    /// </summary>
    [Fact]
    public void Breath_HurtsThePlayerWithoutGivingExperience()
    {
        (GameState game, _, GameLoop loop) = Level();

        int before = game.Player.CurrentHitPoints;
        int experience = game.Player.Experience;

        loop.Spells.Breath(
            SpellElement.Fire, game.CharacterRow, game.CharacterColumn, 90, "a dragon", 2);

        Assert.True(game.Player.CurrentHitPoints < before, "the breath did nothing");
        Assert.Equal(experience, game.Player.Experience);
    }

    /// <summary>
    /// The undead have no life to drain, and finding that out is worth knowing.
    /// </summary>
    [Fact]
    public void DrainLife_DoesNothingToTheUndead()
    {
        (GameState game, _, GameLoop loop) = Level();

        int undead = FindCreature(c =>
            (c.DefenseFlags & CreatureDefense.Undead) != 0 && c.HitDiceCount > 4);

        int row = game.CharacterRow;
        int column = game.CharacterColumn + 1;
        game.Cave[row, column].Feature = CaveFeature.CorridorFloor;
        new DungeonGenerator(game).PlaceMonster(row, column, undead, asleep: true);

        int slot = game.Cave[row, column].MonsterIndex;
        game.Monsters[slot].HitPoints = 500;

        Assert.False(loop.Spells.DrainLife(6, game.CharacterRow, game.CharacterColumn));
        Assert.Equal(500, game.Monsters[slot].HitPoints);
        Assert.NotEqual(0, game.Memories[undead].Defense & CreatureDefense.Undead);
    }

    /// <summary>
    /// Hurrying a monster always works; slowing it is resisted by anything
    /// strong enough. Either way it wakes up.
    /// </summary>
    [Fact]
    public void SpeedMonster_HurryingAlwaysWorks()
    {
        (GameState game, _, GameLoop loop) = Level();

        int kind = FindCreature(c => c.Level > 0);
        int row = game.CharacterRow;
        int column = game.CharacterColumn + 1;
        game.Cave[row, column].Feature = CaveFeature.CorridorFloor;
        new DungeonGenerator(game).PlaceMonster(row, column, kind, asleep: true);

        int slot = game.Cave[row, column].MonsterIndex;
        game.Monsters[slot].Sleep = 500;
        int speed = game.Monsters[slot].Speed;

        Assert.True(loop.Spells.SpeedMonster(6, game.CharacterRow, game.CharacterColumn, 1));
        Assert.Equal(speed + 1, game.Monsters[slot].Speed);
        Assert.Equal(0, game.Monsters[slot].Sleep);
    }

    /// <summary>
    /// Something that never sleeps cannot be put to sleep, and watching it shrug
    /// the spell off is how the player learns that.
    /// </summary>
    [Fact]
    public void SleepMonster_TeachesWhatCannotSleep()
    {
        (GameState game, _, GameLoop loop) = Level();

        int sleepless = FindCreature(c =>
            (c.DefenseFlags & CreatureDefense.NeverSleeps) != 0 && c.Level > 0);

        int row = game.CharacterRow;
        int column = game.CharacterColumn + 1;
        game.Cave[row, column].Feature = CaveFeature.CorridorFloor;
        new DungeonGenerator(game).PlaceMonster(row, column, sleepless, asleep: false);

        int slot = game.Cave[row, column].MonsterIndex;
        game.Monsters[slot].Visible = true;

        Assert.False(loop.Spells.SleepMonster(6, game.CharacterRow, game.CharacterColumn));
        Assert.Equal(0, game.Monsters[slot].Sleep);
        Assert.NotEqual(0, game.Memories[sleepless].Defense & CreatureDefense.NeverSleeps);
    }

    /// <summary>
    /// A monster's spell needs three things: the frequency roll, a range it can
    /// reach and a clear line of sight. Out of range, nothing is cast.
    /// </summary>
    [Fact]
    public void CastSpell_NeedsRangeAndSight()
    {
        (GameState game, _, GameLoop loop) = Level();

        int caster = FindCreature(c => c.CastsSpells && c.Level > 0);

        int row = game.CharacterRow;
        int column = game.CharacterColumn + 1;
        game.Cave[row, column].Feature = CaveFeature.CorridorFloor;
        new DungeonGenerator(game).PlaceMonster(row, column, caster, asleep: false);

        int slot = game.Cave[row, column].MonsterIndex;
        game.Monsters[slot].Visible = true;

        // Far out of range: nothing is cast, whatever the frequency roll says.
        game.Monsters[slot].DistanceToPlayer = MonsterAttack.MaxSpellDistance + 5;

        for (int attempt = 0; attempt < 50; attempt++)
        {
            Assert.False(loop.MonsterAttack.CastSpell(slot));
        }
    }

    /// <summary>
    /// Casting is loud: it interrupts a rest, which is what stops a player
    /// sleeping through a bombardment.
    /// </summary>
    [Fact]
    public void CastSpell_InterruptsARest()
    {
        (GameState game, _, GameLoop loop) = Level();

        int caster = FindCreature(c =>
            c.CastsSpells && c.Level > 0
            && (c.SpellFlags & CreatureSpell.Breathe) != 0);

        int row = game.CharacterRow;
        int column = game.CharacterColumn + 1;
        game.Cave[row, column].Feature = CaveFeature.CorridorFloor;
        new DungeonGenerator(game).PlaceMonster(row, column, caster, asleep: false);

        int slot = game.Cave[row, column].MonsterIndex;
        game.Monsters[slot].Visible = true;
        game.Monsters[slot].DistanceToPlayer = 1;

        game.Player.Rest = 100;
        game.Player.Status |= PlayerStatus.Resting;

        bool cast = false;
        for (int attempt = 0; attempt < 200 && !cast; attempt++)
        {
            cast = loop.MonsterAttack.CastSpell(slot);
        }

        Assert.True(cast, "nothing was ever cast in two hundred tries");
        Assert.Equal(0, game.Player.Rest);
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
}
