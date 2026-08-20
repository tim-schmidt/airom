// AIrom's side of the oracle's character mode.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;

namespace Airom.Oracle;

public static partial class OracleDump
{
    /// <summary>
    /// A rolled character.
    ///
    /// The C side drives create_character() by feeding it keystrokes, so the
    /// whole of create.c runs: the stat roll and its re-roll band, the race and
    /// class adjustments, the history walk, age and build, the hit point curve
    /// and the starting money. This side takes the same choices as arguments,
    /// since the prompting belongs with the interface rather than the rolling.
    /// </summary>
    public static void DumpCharacter(TextWriter output, uint seed, int race, int sex, int pclass)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "character", seed);
        output.Write("race " + race.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("sex " + sex.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("class " + pclass.ToString(CultureInfo.InvariantCulture) + "\n");

        if (!CharacterCreation.AllowedClasses(race).Contains(pclass))
        {
            output.Write("invalid race and class combination\n");
            return;
        }

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();

        // The C falls back to user_name() when no name is typed, which the
        // oracle stubs fix at "Oracle".
        Player player = new CharacterCreation(game)
            .Create(race, pclass, male: sex != 0, name: "Oracle");

        void Line(string key, int value) =>
            output.Write(key + " " + value.ToString(CultureInfo.InvariantCulture) + "\n");

        output.Write("name " + player.Name + "\n");
        Line("male", player.Male ? 1 : 0);
        Line("prace", player.Race);
        Line("pclass", player.Class);
        Line("age", player.Age);
        Line("height", player.Height);
        Line("weight", player.Weight);
        Line("social", player.SocialClass);
        Line("gold", player.Gold);
        Line("hitdie", player.HitDie);
        Line("mhp", player.MaxHitPoints);
        Line("expfact", player.ExperienceFactor);
        Line("srh", player.Search);
        Line("fos", player.SearchFrequency);
        Line("bth", player.BaseToHit);
        Line("bthb", player.BaseToHitBows);
        Line("stl", player.Stealth);
        Line("save", player.Save);
        Line("disarm", player.Disarm);
        Line("ptohit", player.PlusToHit);
        Line("ptodam", player.PlusToDamage);
        Line("ptoac", player.PlusToArmourClass);
        Line("pac", player.ArmourClass);
        Line("infra", player.Infravision);

        for (int i = 0; i < player.MaxStat.Length; i++)
        {
            output.Write(string.Join(
                ' ',
                "stat",
                i.ToString(CultureInfo.InvariantCulture),
                player.MaxStat[i].ToString(CultureInfo.InvariantCulture),
                player.CurrentStat[i].ToString(CultureInfo.InvariantCulture),
                player.UseStat[i].ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        for (int i = 0; i < player.History.Length; i++)
        {
            output.Write(
                "history " + i.ToString(CultureInfo.InvariantCulture)
                + " " + player.History[i] + "\n");
        }

        for (int i = 0; i < Player.MaxLevel; i++)
        {
            output.Write(
                "hp " + i.ToString(CultureInfo.InvariantCulture)
                + " " + player.HitPointsByLevel[i].ToString(CultureInfo.InvariantCulture) + "\n");
        }

        OracleDump.Line(output, "final-state", game.Rng.State);
    }
}
