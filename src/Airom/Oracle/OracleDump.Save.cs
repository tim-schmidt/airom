// AIrom's side of the oracle's save mode.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Oracle;

/// <summary>
/// A save file stamped with a fixed moment.
///
/// The original stamps the file with the time it was written, which two runs
/// at two different moments could never agree on, so both sides pin it - the C
/// side by standing in for time(), this side by overriding one method.
/// </summary>
internal sealed class PinnedSaveFile(GameState game, Display display, GameLoop loop)
    : SaveFile(game, display, loop)
{
    /// <summary>
    /// The start of the epoch, which is what the C harness's clock returns:
    /// the value matters not at all as long as both sides read the same one.
    /// </summary>
    public const uint Pinned = 0;

    protected override uint Now() => Pinned;
}

public static partial class OracleDump
{
    /// <summary>
    /// A whole saved game, written and read back.
    ///
    /// The comparison is the file itself: every byte of it, which is the only
    /// way to know that two savefiles are the same savefile. Then it is read
    /// back and written out a second time, so that the reading half is compared
    /// as well - if either side read a field into the wrong place, the second
    /// file would not match the other side's second file.
    /// </summary>
    public static void DumpSave(TextWriter output, uint seed, int level, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "save", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        (GameState game, MemoryScreen screen, Display display, GameLoop loop) =
            DeathSetup(seed, variation);

        // Shops, so the file carries stock as well as a character.
        game.Stores.Initialise();

        for (int i = 0; i <= variation % 3; i++)
        {
            game.Stores.Maintain();
        }

        // Something learned about a handful of creatures.
        for (int i = 0; i < 12; i++)
        {
            MonsterMemory memory = game.Memories[((i * 7) + variation) % game.Memories.Count];

            memory.Move = (uint)(i + 1) * 0x10001;
            memory.Spells = (uint)(i + 3);
            memory.Kills = i + 1;
            memory.Deaths = i % 3;
            memory.Defense = (ushort)(i * 3);
            memory.Wake = (byte)i;
            memory.Ignore = (byte)(i % 5);
            memory.Attacks[0] = (byte)(i + 1);
            memory.Attacks[1] = (byte)(i % 2);
        }

        // Every option, so each bit of the packed long is exercised.
        game.CutCorners = (variation & 0x1) != 0;
        game.ExamineCorners = (variation & 0x2) != 0;
        game.ShowSelfWhileRunning = (variation & 0x4) != 0;
        game.StopAtLevelBounds = (variation & 0x8) != 0;
        game.PromptBeforeCarrying = (variation & 0x10) != 0;
        game.RogueLikeCommands = (variation & 0x20) != 0;
        game.ShowWeights = (variation & 0x40) != 0;
        game.HighlightSeams = (variation & 0x1) == 0;
        game.IgnoreDoorsWhileRunning = (variation & 0x2) == 0;
        game.SoundEnabled = (variation & 0x4) == 0;
        game.DisplayCounts = (variation & 0x8) == 0;

        // And messages, which are kept across a save so the player can still
        // ask what was said before they put the game away.
        var messages = new string[Display.SavedMessageCount];

        for (int i = 0; i < messages.Length; i++)
        {
            messages[i] = "message number "
                + (i + variation).ToString(CultureInfo.InvariantCulture);
        }

        display.RestoreMessages(messages, variation % Display.SavedMessageCount);

        game.PanicSaved = false;
        game.TotalWinner = false;
        game.NoScore = 0;
        game.BirthDate = 700000000 + variation;
        game.DiedFrom = "a Giant Rat";
        game.MaxScore = 0;
        game.MissileCounter = variation;

        game.DungeonLevel = level;
        new DungeonGenerator(game, display).Generate();
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        game.Turn = 500 + variation;
        game.CharacterGenerated = true;
        game.CharacterSaved = false;

        // A dead character's file stops after the shops, and is read back for
        // its memory alone - unless a wizard asks for a resurrection, which is
        // the only way the rest of it is ever used.
        loop.Dead = (variation % 5) == 4;
        game.Wizard = (variation % 10) == 9;

        display.MessageWaitingFlag = false;

        // Yes to the resurrection, if it is asked.
        screen.SetKeys("y" + new string(' ', 598));

        var saves = new PinnedSaveFile(game, display, loop)
        {
            StartTime = PinnedSaveFile.Pinned,
        };
        loop.SaveFile = saves;

        string first = Path.Combine(Directory.GetCurrentDirectory(), "airom-save.dat");
        string second = Path.Combine(Directory.GetCurrentDirectory(), "airom-save2.dat");

        Delete(first);
        Delete(second);

        output.Write("saved " + (saves.Save(first) ? "1" : "0") + "\n");
        DumpBytes(output, "first", first);

        // Now read it back. The game is left standing as it is: what matters is
        // that both sides put the same thing back into the same places, and the
        // second file says whether they did.
        bool restored = saves.Restore(first, out bool generate);

        output.Write("restored " + (restored ? "1" : "0")
            + " generate " + (generate ? "1" : "0") + "\n");

        DumpState(output, game, display, loop.Dead);

        game.CharacterSaved = false;
        output.Write("saved-again " + (saves.Save(second) ? "1" : "0") + "\n");
        DumpBytes(output, "second", second);

        Delete(first);
        Delete(second);

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>
    /// Prints a file byte by byte, so a diff points at the first byte that
    /// differs rather than at a wall of hex.
    /// </summary>
    private static void DumpBytes(TextWriter output, string tag, string path)
    {
        if (!File.Exists(path))
        {
            output.Write(tag + " missing\n");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);

        output.Write(tag + " bytes "
            + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\n");

        for (int i = 0; i < bytes.Length; i++)
        {
            output.Write(tag + " byte " + i.ToString(CultureInfo.InvariantCulture)
                + " " + bytes[i].ToString(CultureInfo.InvariantCulture) + "\n");
        }
    }

    /// <summary>
    /// What came back, in the terms a player would recognise. The bytes prove
    /// the two files are the same; this proves the game inside them is.
    /// </summary>
    private static void DumpState(
        TextWriter output, GameState game, Display display, bool dead)
    {
        static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
        static string L(long value) => value.ToString(CultureInfo.InvariantCulture);

        Player player = game.Player;

        output.Write("turn " + N(game.Turn) + " dun-level " + N(game.DungeonLevel)
            + " row " + N(game.CharacterRow) + " col " + N(game.CharacterColumn) + "\n");

        output.Write("standing dead " + (dead ? "1" : "0")
            + " wizard " + (game.Wizard ? "1" : "0")
            + " generated " + (game.CharacterGenerated ? "1" : "0")
            + " saved " + (game.CharacterSaved ? "1" : "0") + "\n");

        output.Write("who [" + player.Name + "] male " + (player.Male ? "1" : "0")
            + " race " + N(player.Race) + " class " + N(player.Class)
            + " level " + N(player.Level) + " gold " + N(player.Gold)
            + " exp " + N(player.Experience) + "\n");

        output.Write("body " + string.Join(
            ' ', Enumerable.Range(0, Stat.Count).Select(i => N(player.UseStat[i])))
            + " hp " + N(player.CurrentHitPoints) + "/" + N(player.MaxHitPoints)
            + " mana " + N(player.CurrentMana) + "/" + N(player.MaxMana) + "\n");

        output.Write("flags " + N((int)player.Status) + " food " + N(player.Food)
            + " speed " + N(player.Speed) + " see-infra " + N(player.SeeInfrared)
            + " new-spells " + N(player.NewSpells) + "\n");

        output.Write("options " + string.Join(' ', new[]
        {
            game.CutCorners, game.ExamineCorners, game.ShowSelfWhileRunning,
            game.StopAtLevelBounds, game.PromptBeforeCarrying, game.RogueLikeCommands,
            game.ShowWeights, game.HighlightSeams, game.IgnoreDoorsWhileRunning,
            game.SoundEnabled, game.DisplayCounts,
        }.Select(flag => flag ? "1" : "0")) + "\n");

        output.Write("pack " + N(game.Inventory.Count)
            + " weight " + N(game.Inventory.Weight)
            + " worn " + N(game.Inventory.EquipmentCount) + "\n");

        for (int i = 0; i < game.Inventory.Count; i++)
        {
            DumpItem(output, "carried", i, game.Inventory[i]);
        }

        for (int i = Inventory.WieldSlot; i < Inventory.Size; i++)
        {
            DumpItem(output, "worn", i, game.Inventory[i]);
        }

        output.Write("spells " + L(player.SpellLearned) + " " + L(player.SpellWorked)
            + " " + L(player.SpellForgotten) + " order " + N(player.SpellOrder[0])
            + " " + N(player.SpellOrder[1]) + " " + N(player.SpellOrder[2]) + "\n");

        output.Write("seeds " + L(game.RandesSeed) + " " + L(game.TownSeed) + "\n");

        output.Write("scoring panic " + (game.PanicSaved ? "1" : "0")
            + " winner " + (game.TotalWinner ? "1" : "0")
            + " noscore " + N(game.NoScore) + " max-score " + N(game.MaxScore)
            + " birth " + N(game.BirthDate) + "\n");

        output.Write("died-from [" + game.DiedFrom + "]\n");

        output.Write("last-msg " + N(display.LastMessageIndex)
            + " [" + display.RecentMessages[0] + "]\n");

        for (int i = 0; i < Stores.StoreCount; i++)
        {
            Store store = game.Stores.All[i];

            output.Write("store " + N(i) + " owner " + N(store.Owner)
                + " stock " + N(store.StockCount) + " open " + N(store.OpenAgainAt)
                + " good " + N(store.GoodBuys) + " bad " + N(store.BadBuys)
                + " insult " + N(store.InsultsThisVisit) + "\n");

            for (int j = 0; j < store.StockCount; j++)
            {
                StockLine line = store.Stock[j];

                output.Write("stock " + N(i) + " " + N(j)
                    + " cost " + N(line.Cost) + "\n");

                DumpItem(output, "stock-item", j, line.Item);
            }
        }

        for (int i = 0; i < game.Memories.Count; i++)
        {
            MonsterMemory memory = game.Memories[i];

            if (memory.Kills == 0 && memory.Move == 0 && memory.Spells == 0)
            {
                continue;
            }

            output.Write("recall " + N(i) + " move " + L(memory.Move)
                + " spells " + L(memory.Spells) + " kills " + N(memory.Kills)
                + " deaths " + N(memory.Deaths) + " defense " + N(memory.Defense)
                + " wake " + N(memory.Wake) + " ignore " + N(memory.Ignore)
                + " attacks " + string.Join(
                    ' ', memory.Attacks.Select(a => N(a))) + "\n");
        }

        // The level itself, summed rather than printed: a divergence anywhere
        // in twelve thousand squares moves the total.
        long features = 0;
        int standing = 0;
        int lying = 0;

        for (int row = 0; row < GameState.DungeonHeight; row++)
        {
            for (int column = 0; column < GameState.DungeonWidth; column++)
            {
                CaveSquare square = game.Cave[row, column];

                int packed = square.Feature
                    | ((square.LitRoom ? 1 : 0) << 4)
                    | ((square.FieldMark ? 1 : 0) << 5)
                    | ((square.PermanentLight ? 1 : 0) << 6)
                    | ((square.TemporaryLight ? 1 : 0) << 7);

                features += (long)(row + 1) * packed;

                if (square.MonsterIndex != 0)
                {
                    standing++;
                }

                if (square.ObjectIndex != 0)
                {
                    lying++;
                }
            }
        }

        output.Write("cave " + N(game.Cave.Height) + " " + N(game.Cave.Width)
            + " features " + L(features) + " standing " + N(standing)
            + " lying " + N(lying) + "\n");

        output.Write("lists objects " + N(game.Objects.Count)
            + " monsters " + N(game.Monsters.Count)
            + " panel " + N(display.Panel.MaxRow) + " " + N(display.Panel.MaxColumn)
            + " breeding " + N(game.Monsters.BredCount) + "\n");

        for (int i = ObjectPool.FirstIndex; i < game.Objects.Count; i++)
        {
            DumpItem(output, "object", i, game.Objects[i]);
        }

        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            Monster monster = game.Monsters[i];

            output.Write("monster " + N(i) + " index " + N(monster.CreatureIndex)
                + " hp " + N(monster.HitPoints) + " sleep " + N(monster.Sleep)
                + " speed " + N(monster.Speed) + " at " + N(monster.Row)
                + " " + N(monster.Column) + " distance " + N(monster.DistanceToPlayer)
                + " seen " + (monster.Visible ? "1" : "0")
                + " stunned " + N(monster.Stunned)
                + " confused " + N(monster.Confused) + "\n");
        }
    }

    /// <summary>
    /// One item, every field of it: a savefile carries more than a player ever
    /// sees, and a field put back in the wrong place has to show up here.
    /// </summary>
    private static void DumpItem(TextWriter output, string tag, int at, InvenType item)
    {
        static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        output.Write(tag + " " + N(at) + " index " + N(item.Index)
            + " name2 " + N(item.SpecialName) + " [" + item.Inscription + "]"
            + " flags " + item.Flags.ToString(CultureInfo.InvariantCulture)
            + " tval " + N(item.TVal) + " tchar " + N(item.DisplayChar)
            + " p1 " + N(item.P1) + " cost " + N(item.Cost)
            + " subval " + N(item.SubVal) + " number " + N(item.Number)
            + " weight " + N(item.Weight) + " tohit " + N(item.ToHit)
            + " todam " + N(item.ToDam) + " ac " + N(item.Ac)
            + " toac " + N(item.ToAc) + " damage " + N(item.DamageDice)
            + " " + N(item.DamageSides) + " level " + N(item.Level)
            + " ident " + N(item.Identification) + "\n");
    }
}
