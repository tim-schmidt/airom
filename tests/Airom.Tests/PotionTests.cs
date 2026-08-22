using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on drinking and eating.
///
/// Every potion and mushroom in the table is drunk or eaten on both sides and
/// the result diffed against the C oracle - 176 items across five seeds. These
/// pin the properties behind them.
/// </summary>
public class PotionTests
{
    private static (GameState Game, MemoryScreen Screen, GameLoop Loop) Fresh(uint seed = 12345)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(new string(' ', 200));

        var display = new Display(game, screen);
        display.Panel.Resize(GameState.DungeonHeight, GameState.DungeonWidth);

        var loop = new GameLoop(game, display);

        Player player = game.Player;
        player.Level = 10;
        player.ExperienceFactor = 100;
        player.MaxHitPoints = 80;
        player.CurrentHitPoints = 30;
        player.Food = 3000;

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 16;
            player.CurrentStat[i] = 12;
            player.UseStat[i] = 12;
        }

        return (game, screen, loop);
    }

    private static InvenType Potion(uint effect, byte category = ItemCategory.Potion1)
    {
        var potion = new InvenType();
        potion.CopyFrom(222);
        potion.TVal = category;
        potion.Flags = effect;
        return potion;
    }

    /// <summary>
    /// A potion identifies itself by being noticed, not by being drunk: one that
    /// cures what is not wrong teaches nothing.
    /// </summary>
    [Fact]
    public void Quaff_IsOnlyIdentifiedWhenSomethingHappens()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        // Effect 29 is cure blindness. Nothing to cure, nothing learned.
        game.Player.Blind = 0;
        Assert.False(loop.Potions.Quaff(Potion(1u << 28)));

        game.Player.Blind = 50;
        Assert.True(loop.Potions.Quaff(Potion(1u << 28)));
    }

    /// <summary>
    /// One bottle can do several things: the effects are a bit set, and every
    /// bit is applied.
    /// </summary>
    [Fact]
    public void Quaff_AppliesEveryEffectInTheSet()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        game.Player.Blind = 50;
        game.Player.Confused = 50;

        // Cure blindness and cure confusion together.
        Assert.True(loop.Potions.Quaff(Potion((1u << 28) | (1u << 29))));

        Assert.Equal(1, game.Player.Blind);
        Assert.Equal(1, game.Player.Confused);
    }

    /// <summary>
    /// The second potion table continues the first, so its effects are numbered
    /// from thirty-three - which is what tells a potion of heroism from a potion
    /// of gaining strength.
    /// </summary>
    [Fact]
    public void Quaff_TheSecondTableContinuesTheFirst()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        // Bit 4 in the second table is effect 37: heroism.
        loop.Potions.Quaff(Potion(1u << 4, ItemCategory.Potion2));
        Assert.True(game.Player.Hero > 0);

        // The same bit in the first table is effect 5: losing intelligence.
        (GameState other, _, GameLoop otherLoop) = Fresh();
        otherLoop.Potions.Quaff(Potion(1u << 4));
        Assert.True(other.Player.CurrentStat[Stat.Intelligence] < 12);
    }

    /// <summary>A sustained stat survives the potion that would drain it.</summary>
    [Fact]
    public void Quaff_SustainedStatsSurviveDraining()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        game.Player.SustainStrength = true;

        // Effect 2: lose strength.
        loop.Potions.Quaff(Potion(1u << 1));

        Assert.Equal(12, game.Player.CurrentStat[Stat.Strength]);
    }

    /// <summary>
    /// Healing is capped at the maximum, and the fraction is cleared with it so
    /// that nothing is banked towards a point that cannot be gained.
    /// </summary>
    [Fact]
    public void HealPlayer_StopsAtFull()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        game.Player.CurrentHitPoints = 75;
        game.Player.HitPointFraction = 500;

        Assert.True(loop.Spells.HealPlayer(1000));
        Assert.Equal(80, game.Player.CurrentHitPoints);
        Assert.Equal(0, game.Player.HitPointFraction);

        // Already full: nothing happens, and nothing is learned.
        Assert.False(loop.Spells.HealPlayer(1000));
    }

    /// <summary>
    /// Eating past full is possible and costs speed - a fiftieth of the excess,
    /// in turns - so gorging before a fight is a real mistake.
    /// </summary>
    [Fact]
    public void AddFood_GorgingSlowsThePlayer()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh();

        game.Player.Food = GameLoop.FoodMax - 100;
        loop.Spells.AddFood(5000);

        Assert.True(game.Player.Slowed > 0, "gorging did not slow the player");
        Assert.Contains("bloated", screen.GetText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A mushroom's badness scales with the depth it came from, which is what
    /// makes eating an unknown one deeper down a worse gamble.
    /// </summary>
    [Fact]
    public void Eat_TheDepthScalesTheHarm()
    {
        (GameState shallow, _, GameLoop shallowLoop) = Fresh();
        (GameState deep, _, GameLoop deepLoop) = Fresh();

        var mild = new InvenType();
        mild.CopyFrom(222);
        mild.TVal = ItemCategory.Food;
        mild.Flags = 1; // poison
        mild.Level = 1;

        var nasty = new InvenType();
        nasty.CopyFrom(222);
        nasty.TVal = ItemCategory.Food;
        nasty.Flags = 1;
        nasty.Level = 40;

        shallowLoop.Food.Eat(mild);
        deepLoop.Food.Eat(nasty);

        Assert.True(deep.Player.Poisoned > shallow.Player.Poisoned,
            "the deeper mushroom was no worse");
    }

    /// <summary>Curing what is not wrong teaches nothing, whatever it was.</summary>
    [Fact]
    public void Eat_CuringNothingIsNotNoticed()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        var cure = new InvenType();
        cure.CopyFrom(222);
        cure.TVal = ItemCategory.Food;
        cure.Flags = 1u << 5; // effect 6: cure poison
        cure.Level = 5;

        game.Player.Poisoned = 0;
        Assert.False(loop.Food.Eat(cure));

        game.Player.Poisoned = 30;
        Assert.True(loop.Food.Eat(cure));
        Assert.Equal(1, game.Player.Poisoned);
    }

    /// <summary>
    /// Knowing a kind is learned once and applies to every one the player finds
    /// afterwards - which is the whole point of trying the first one.
    /// </summary>
    [Fact]
    public void Knowledge_LearningOnePotionNamesThemAll()
    {
        (GameState game, _, _) = Fresh();

        InvenType first = Potion(1u << 28);
        InvenType second = Potion(1u << 28);

        Assert.False(game.Knowledge.IsKindKnown(second));
        game.Knowledge.LearnKind(first);
        Assert.True(game.Knowledge.IsKindKnown(second));
    }
}
