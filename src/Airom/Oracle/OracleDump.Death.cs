// AIrom's side of the oracle's death, sheet and score modes.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using System.Text;
using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Oracle;

/// <summary>
/// A death whose gravestone always carries the same date.
///
/// Two runs at two different moments could never agree on today's date, so the
/// harness pins it - as the C side does, by standing in for ctime().
/// </summary>
internal sealed class PinnedDeath(GameState game, Display display, GameLoop loop)
    : Death(game, display, loop)
{
    protected override string Today() => "Sat Jan  1";
}

public static partial class OracleDump
{
    /// <summary>
    /// The end of the game: the gravestone, the character sheet and the score.
    ///
    /// The score file itself is not compared. It is the one part of this that
    /// was not ported but rewritten: the original shares a file between every
    /// player on a Unix machine, with a lock over it and a user id in every
    /// entry, and none of that has any meaning here. What is compared is
    /// everything that is a port - the stone, the sheet on the screen, the
    /// sheet written to a file, the score arithmetic, and the encoding of a
    /// score record, which is the save file's own and has to be exact.
    /// </summary>
    public static void DumpDeath(TextWriter output, uint seed, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "death", seed);
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        (GameState game, MemoryScreen screen, Display display, GameLoop loop) =
            DeathSetup(seed, variation);

        game.DungeonLevel = 5 + (variation % 20);
        game.Turn = 100;
        game.TotalWinner = (variation % 5) == 4;
        game.MaxScore = (variation % 7) * 100;

        game.DiedFrom = (variation % 4) switch
        {
            0 => "a Giant Rat",
            1 => "an Ancient Dragon",
            2 => "Quitting",
            _ => "the Balrog",
        };

        var death = new PinnedDeath(game, display, loop);
        loop.Death = death;

        output.Write("points "
            + death.TotalPoints().ToString(CultureInfo.InvariantCulture) + "\n");

        if (game.TotalWinner)
        {
            screen.SetKeys(new string(' ', 599));

            display.MessageWaitingFlag = false;
            game.TotalWinnerCrowned = true;
            death.Crown();

            DumpScreenRows(output, screen, "crown");

            output.Write(string.Join(
                ' ', "crowned",
                game.Player.Level.ToString(CultureInfo.InvariantCulture),
                game.Player.Gold.ToString(CultureInfo.InvariantCulture),
                game.Player.Experience.ToString(CultureInfo.InvariantCulture),
                game.DungeonLevel.ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        // The stone. Its one prompt does two jobs: a file name writes the
        // character out, an empty answer shows it on the screen instead, and an
        // escape leaves without either.
        string keys = (variation % 4) switch
        {
            0 => ((char)27).ToString(),
            1 => "\r" + (char)27,
            2 => "\r   ",
            _ => "oracle-tomb.txt\r",
        };

        // Padded with escapes: they end every prompt this can reach, so a
        // script that runs out simply stops rather than spinning.
        screen.SetKeys(keys + new string((char)27, 599 - keys.Length));

        // Cleared here rather than at the top: filling the character in prints
        // messages of its own, and a waiting message turns the first prompt
        // into a -more- that eats a scripted key.
        display.MessageWaitingFlag = false;

        string written = Path.Combine(Directory.GetCurrentDirectory(), "oracle-tomb.txt");
        Delete(written);

        Action stopTombLogging = LogKeys(output, screen);
        death.PrintTomb();
        stopTombLogging();

        DumpScreenRows(output, screen, "scr");

        if (File.Exists(written))
        {
            int row = 0;

            foreach (string line in File.ReadAllLines(written))
            {
                output.Write("file " + row.ToString(CultureInfo.InvariantCulture)
                    + " " + line.TrimEnd('\r') + "\n");

                row++;
            }

            Delete(written);
        }

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>The character sheet, on the screen and written out to a file.</summary>
    public static void DumpSheet(TextWriter output, uint seed, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "sheet", seed);
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        (GameState game, MemoryScreen screen, Display display, GameLoop loop) =
            DeathSetup(seed, variation);

        game.DungeonLevel = 5;
        game.Turn = 100;

        screen.SetKeys(new string(' ', 599));
        display.MessageWaitingFlag = false;

        Action stopLogging = LogKeys(output, screen);
        loop.CharacterSheet.DisplayAll();
        stopLogging();
        DumpScreenRows(output, screen, "scr");

        string path = Path.Combine(Directory.GetCurrentDirectory(), "oracle-sheet.txt");
        Delete(path);

        if (loop.CharacterFile.Write(path))
        {
            int row = 0;

            foreach (string line in File.ReadAllLines(path))
            {
                output.Write("file " + row.ToString(CultureInfo.InvariantCulture)
                    + " " + line.TrimEnd('\r') + "\n");

                row++;
            }
        }

        Delete(path);

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>
    /// One score record, written and read back.
    ///
    /// This is the save file's own encoding - every byte exclusive-ored with
    /// the one before it - so the bytes themselves are compared, not merely the
    /// values that come back out.
    /// </summary>
    public static void DumpScore(TextWriter output, uint seed, int variation, int count)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "score", seed);
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("count " + count.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);

        using var bytes = new MemoryStream();
        var writer = new SaveCipher(bytes);

        for (int which = 0; which < count; which++)
        {
            int v = variation + which;

            writer.WriteHighScore(new HighScore
            {
                Points = 1000 * (v + 1),
                BirthDate = 700000000 + v,
                Uid = 0,
                MaxHitPoints = 100 + v,
                CurrentHitPoints = 50 + v,
                DungeonLevel = v % 50,
                Level = 1 + (v % 40),
                MaxDungeonLevel = 10 + (v % 30),
                Sex = (v % 2) == 0 ? 'M' : 'F',
                Race = v % GameTables.Races.Length,
                Class = v % GameTables.Classes.Length,
                Name = "Player " + v.ToString(CultureInfo.InvariantCulture),
                DiedFrom = "a Giant Rat " + v.ToString(CultureInfo.InvariantCulture),
            });
        }

        byte[] written = bytes.ToArray();

        output.Write("bytes " + written.Length.ToString(CultureInfo.InvariantCulture) + "\n");

        for (int i = 0; i < written.Length; i++)
        {
            output.Write("byte " + i.ToString(CultureInfo.InvariantCulture)
                + " " + written[i].ToString(CultureInfo.InvariantCulture) + "\n");
        }

        bytes.Position = 0;
        var reader = new SaveCipher(bytes);

        for (int which = 0; which < count; which++)
        {
            HighScore back = reader.ReadHighScore();

            string N(int value) => value.ToString(CultureInfo.InvariantCulture);

            output.Write(string.Join(
                ' ', "read", N(which), N(back.Points), N(back.BirthDate),
                N(back.Uid), N(back.MaxHitPoints), N(back.CurrentHitPoints),
                N(back.DungeonLevel), N(back.Level), N(back.MaxDungeonLevel),
                back.Sex.ToString(), N(back.Race), N(back.Class),
                "[" + back.Name + "]", "[" + back.DiedFrom + "]") + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>
    /// A character worth writing down: not rolled, since the point is to
    /// compare the writing rather than the rolling, but filled in the same way
    /// every time.
    /// </summary>
    private static (GameState, MemoryScreen, Display, GameLoop) DeathSetup(
        uint seed, int variation)
    {
        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Player = new Player();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        Player player = game.Player;

        player.Name = "Alatariel";
        player.Male = (variation % 2) == 0;
        player.Race = variation % GameTables.Races.Length;
        player.Class = variation % GameTables.Classes.Length;
        player.Age = 30 + variation;
        player.Height = 70 + variation;
        player.Weight = 150 + variation;
        player.SocialClass = 40 + variation;
        player.Level = 1 + ((variation * 3) % 40);
        player.Experience = 1000 * (variation + 1);
        player.MaxExperience = player.Experience + 500;
        player.ExperienceFactor = 100;
        player.Gold = 1234 * (variation + 1);
        player.MaxHitPoints = 100 + variation;
        player.CurrentHitPoints = 50 + variation;
        player.MaxMana = 20 + variation;
        player.CurrentMana = 10 + variation;
        player.MaxDungeonLevel = 10 + variation;
        player.Search = 20 + variation;
        player.Stealth = variation % 8;
        player.SearchFrequency = 10 + variation;
        player.Disarm = 30 + variation;
        player.Save = 40 + variation;
        player.BaseToHit = 50 + variation;
        player.BaseToHitBows = 45 + variation;
        player.PlusToHit = 3;
        player.DisplayedPlusToHit = 4;
        player.DisplayedPlusToDamage = 5;
        player.DisplayedToArmourClass = 6;
        player.DisplayedArmourClass = 17;
        player.SeeInfrared = variation % 6;

        for (int i = 0; i < player.History.Length; i++)
        {
            player.History[i] = "A line of history, number "
                + i.ToString(CultureInfo.InvariantCulture) + ".";
        }

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 16 + (i % 3);
            player.CurrentStat[i] = 12 + (i % 5);
            player.ModStat[i] = 0;
            loop.Stats.SetUseStat(i);
        }

        game.CharacterGenerated = true;

        // And something for them to be carrying.
        game.Inventory.Reset();

        if (variation % 3 != 2)
        {
            int[] worn = [30, 103, 96, 365, 111];
            int[] slots =
            [
                Inventory.WieldSlot, Inventory.BodySlot, Inventory.HeadSlot,
                Inventory.LightSlot, Inventory.ArmSlot,
            ];

            for (int i = 0; i < worn.Length; i++)
            {
                game.Inventory[slots[i]].CopyFrom(worn[i]);
            }

            game.Inventory[Inventory.LightSlot].P1 = 5000;
            game.Inventory.EquipmentCount = 5;
        }

        loop.Equipment.Recalculate();

        if (variation % 3 != 1)
        {
            int[] kinds =
            [
                ItemCategory.Potion1, ItemCategory.Scroll1, ItemCategory.Food,
                ItemCategory.Wand, ItemCategory.Sword,
            ];

            foreach (int kind in kinds)
            {
                var held = new InvenType();
                held.CopyFrom(FirstOfCategory(kind));
                game.Inventory.Carry(held);
            }
        }

        return (game, screen, display, loop);
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
