// Ported from Umoria 5.6 source/save.c - sv_write, _save_char and get_char.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The saved game.
///
/// A whole game goes into one file: the character, what they know, what they
/// are carrying, the shops, and - unless they are dead - the level they are
/// standing on. Every byte is exclusive-ored with the one before it, so the
/// file is not quite readable and a single lost byte spoils the rest.
///
/// A dead character's file stops after the shops. That is on purpose: it is
/// enough to bring the monster memory and the options forward into the next
/// game, and enough for a wizard to attempt a resurrection, but there is no
/// level to come back to.
///
/// The format was frozen at 5.2.2 and this reads every version from 5.0.14 on,
/// which is what lets a game saved by the original be picked up here.
/// </summary>
public class SaveFile
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public SaveFile(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    private Player Player => _game.Player;

    /// <summary>Where the saved game lives, unless told otherwise.</summary>
    public static string DefaultPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIrom", "game.sav");

    /// <summary>
    /// The file this game saves to. Umoria's savefile, which is a global the
    /// player can be asked to change when writing fails.
    /// </summary>
    public string CurrentPath { get; set; } = DefaultPath;

    /// <summary>
    /// The version this game writes. Mirrors CUR_VERSION_MAJ, CUR_VERSION_MIN
    /// and PATCH_LEVEL, which the 5.6 sources still leave at 5.5.2 - the file
    /// format was frozen at 5.2.2 and the numbers were never moved on.
    /// </summary>
    public const int VersionMajor = 5;

    /// <inheritdoc cref="VersionMajor"/>
    public const int VersionMinor = 5;

    /// <inheritdoc cref="VersionMajor"/>
    public const int PatchLevel = 2;

    /// <summary>Umoria's MAX_SAVE_MSG.</summary>
    private const int SavedMessages = Display.SavedMessageCount;

    /// <summary>
    /// Whether the game came from a saved file, and so may overwrite it without
    /// asking. Umoria's from_savefile.
    /// </summary>
    public bool FromSaveFile { get; set; }

    /// <summary>
    /// When play started, in seconds. Used to work out how many days a shop has
    /// had to restock while the game was put away. Umoria's start_time.
    /// </summary>
    public uint StartTime { get; set; } = NowStatic();

    /// <summary>The clock, as the original reads it.</summary>
    protected virtual uint Now() => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static uint NowStatic() => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ------------------------------------------------------------------ save

    /// <summary>
    /// Writes the game out, asking for somewhere else if it cannot. Mirrors
    /// save_char().
    ///
    /// A failure is not the end of the game: the player is offered the chance
    /// to delete whatever is in the way, or to name another file, and only an
    /// escape gives up - at which point a dying character loses the record of
    /// what they learned, which is the worst this can cost.
    /// </summary>
    /// <returns>Whether the game was written somewhere.</returns>
    public bool SaveWithRetry()
    {
        while (!Save(CurrentPath))
        {
            _display.MessagePrint("Savefile '" + CurrentPath + "' fails.");

            bool deleted = false;

            if (File.Exists(CurrentPath)
                && _display.GetCheck("File exists. Delete old savefile?"))
            {
                try
                {
                    File.Delete(CurrentPath);
                    deleted = true;
                }
                catch (IOException)
                {
                    _display.MessagePrint("Can't delete '" + CurrentPath + "'");
                }
                catch (UnauthorizedAccessException)
                {
                    _display.MessagePrint("Can't delete '" + CurrentPath + "'");
                }
            }

            if (!deleted)
            {
                _display.Print("New Savefile [ESC to give up]:", 0, 0);

                if (!_display.GetString(0, 31, 45, out string named))
                {
                    return false;
                }

                if (named.Length > 0)
                {
                    CurrentPath = named;
                }
            }

            _display.Print("Saving with " + CurrentPath + "...", 0, 0);
        }

        return true;
    }

    /// <summary>
    /// Writes the game out. Mirrors _save_char().
    ///
    /// Saving stops resting and searching and puts the speed back, because both
    /// are things the pack is doing to the player rather than states of their
    /// own: restored without that, the character would keep the penalty and
    /// gain it again.
    /// </summary>
    /// <returns>Whether the game was written.</returns>
    public bool Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_game.CharacterSaved)
        {
            // Nothing to save.
            return true;
        }

        _loop.Disturb(true, false);
        _loop.ChangeSpeed(-_game.Inventory.PackBurden);
        _game.Inventory.PackBurden = 0;

        bool existed = File.Exists(path);

        if (existed && !FromSaveFile && _game.Wizard
            && !_display.GetCheck("Can't make new savefile. Overwrite old?"))
        {
            _display.MessagePrint("Can't create new file " + path);
            return false;
        }

        bool ok;

        try
        {
            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using FileStream file = File.Create(path);
            var cipher = new SaveCipher(file);

            // Each of the three version bytes is written from a cleared chain,
            // so they can be read without knowing the key; the fourth byte is
            // the key itself, and after writing it the chain holds it.
            cipher.Key = 0;
            cipher.WriteByte(VersionMajor);
            cipher.Key = 0;
            cipher.WriteByte(VersionMinor);
            cipher.Key = 0;
            cipher.WriteByte(PatchLevel);
            cipher.Key = 0;
            cipher.WriteByte((byte)(_game.Rng.RandInt(256) - 1));

            ok = Write(cipher);
        }
        catch (IOException)
        {
            ok = false;
        }
        catch (UnauthorizedAccessException)
        {
            ok = false;
        }

        if (!ok)
        {
            try
            {
                if (!existed && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Nothing more to be done about it than has been said.
            }

            _display.MessagePrint("Error writing to file " + path);
            return false;
        }

        _game.CharacterSaved = true;

        // A saved game is over as far as this session is concerned, and a turn
        // of -1 is what stops the tomb being printed on the way out.
        _game.Turn = -1;

        return true;
    }

    /// <summary>Writes everything but the file's own header. Mirrors sv_write().</summary>
    private bool Write(SaveCipher cipher)
    {
        // What has been learned about each kind of creature. Only the ones with
        // something recorded are written, ended by a sentinel.
        for (int i = 0; i < _game.Memories.Count; i++)
        {
            MonsterMemory memory = _game.Memories[i];

            if (memory.Move == 0 && memory.Defense == 0 && memory.Kills == 0
                && memory.Spells == 0 && memory.Deaths == 0
                && memory.Attacks[0] == 0 && memory.Attacks[1] == 0
                && memory.Attacks[2] == 0 && memory.Attacks[3] == 0)
            {
                continue;
            }

            cipher.WriteShort((ushort)i);
            cipher.WriteLong(memory.Move);
            cipher.WriteLong(memory.Spells);
            cipher.WriteShort((ushort)memory.Kills);
            cipher.WriteShort((ushort)memory.Deaths);
            cipher.WriteShort(memory.Defense);
            cipher.WriteByte(memory.Wake);
            cipher.WriteByte(memory.Ignore);
            cipher.WriteBytes(memory.Attacks, MonsterMemory.MaxAttacks);
        }

        cipher.WriteShort(0xFFFF);

        cipher.WriteLong(PackOptions());

        cipher.WriteString(Player.Name);
        cipher.WriteByte(Player.Male ? (byte)1 : (byte)0);
        cipher.WriteLong((uint)Player.Gold);
        cipher.WriteLong((uint)Player.MaxExperience);
        cipher.WriteLong((uint)Player.Experience);
        cipher.WriteShort((ushort)Player.ExperienceFraction);
        cipher.WriteShort((ushort)Player.Age);
        cipher.WriteShort((ushort)Player.Height);
        cipher.WriteShort((ushort)Player.Weight);
        cipher.WriteShort((ushort)Player.Level);
        cipher.WriteShort((ushort)Player.MaxDungeonLevel);
        cipher.WriteShort((ushort)Player.Search);
        cipher.WriteShort((ushort)Player.SearchFrequency);
        cipher.WriteShort((ushort)Player.BaseToHit);
        cipher.WriteShort((ushort)Player.BaseToHitBows);
        cipher.WriteShort((ushort)Player.MaxMana);
        cipher.WriteShort((ushort)Player.MaxHitPoints);
        cipher.WriteShort((ushort)Player.PlusToHit);
        cipher.WriteShort((ushort)Player.PlusToDamage);
        cipher.WriteShort((ushort)Player.ArmourClass);
        cipher.WriteShort((ushort)Player.PlusToArmourClass);
        cipher.WriteShort((ushort)Player.DisplayedPlusToHit);
        cipher.WriteShort((ushort)Player.DisplayedPlusToDamage);
        cipher.WriteShort((ushort)Player.DisplayedArmourClass);
        cipher.WriteShort((ushort)Player.DisplayedToArmourClass);
        cipher.WriteShort((ushort)Player.Disarm);
        cipher.WriteShort((ushort)Player.Save);
        cipher.WriteShort((ushort)Player.SocialClass);
        cipher.WriteShort((ushort)Player.Stealth);
        cipher.WriteByte((byte)Player.Class);
        cipher.WriteByte((byte)Player.Race);
        cipher.WriteByte((byte)Player.HitDie);
        cipher.WriteByte((byte)Player.ExperienceFactor);
        cipher.WriteShort((ushort)Player.CurrentMana);
        cipher.WriteShort((ushort)Player.ManaFraction);
        cipher.WriteShort((ushort)Player.CurrentHitPoints);
        cipher.WriteShort((ushort)Player.HitPointFraction);

        for (int i = 0; i < 4; i++)
        {
            cipher.WriteString(Player.History[i]);
        }

        WriteStats(cipher);
        WriteFlags(cipher);

        cipher.WriteShort((ushort)_game.MissileCounter);
        cipher.WriteLong((uint)_game.Turn);
        cipher.WriteShort((ushort)_game.Inventory.Count);

        for (int i = 0; i < _game.Inventory.Count; i++)
        {
            cipher.WriteItem(_game.Inventory[i]);
        }

        for (int i = Inventory.WieldSlot; i < Inventory.Size; i++)
        {
            cipher.WriteItem(_game.Inventory[i]);
        }

        cipher.WriteShort((ushort)_game.Inventory.Weight);
        cipher.WriteShort((ushort)_game.Inventory.EquipmentCount);
        cipher.WriteLong(Player.SpellLearned);
        cipher.WriteLong(Player.SpellWorked);
        cipher.WriteLong(Player.SpellForgotten);
        cipher.WriteBytes(Player.SpellOrder, 32);
        cipher.WriteBytes(_game.Knowledge.Flags, ItemKnowledge.FlagCount);
        cipher.WriteLong(_game.RandesSeed);
        cipher.WriteLong(_game.TownSeed);
        cipher.WriteShort((ushort)_display.LastMessageIndex);

        for (int i = 0; i < SavedMessages; i++)
        {
            cipher.WriteString(_display.RecentMessages[i] ?? string.Empty);
        }

        cipher.WriteShort(_game.PanicSaved ? (ushort)1 : (ushort)0);
        cipher.WriteShort(_game.TotalWinner ? (ushort)1 : (ushort)0);
        cipher.WriteShort((ushort)_game.NoScore);
        cipher.WriteShorts(Player.HitPointsByLevel, Player.MaxLevel);

        for (int i = 0; i < Stores.StoreCount; i++)
        {
            Store store = _game.Stores.All[i];

            cipher.WriteLong((uint)store.OpenAgainAt);
            cipher.WriteShort((ushort)store.InsultsThisVisit);
            cipher.WriteByte((byte)store.Owner);
            cipher.WriteByte((byte)store.StockCount);
            cipher.WriteShort((ushort)store.GoodBuys);
            cipher.WriteShort((ushort)store.BadBuys);

            for (int j = 0; j < store.StockCount; j++)
            {
                cipher.WriteLong((uint)store.Stock[j].Cost);
                cipher.WriteItem(store.Stock[j].Item);
            }
        }

        // When the file was written, so the shops can be restocked for the days
        // that passed. A clock that has gone backwards is taken to mean a day.
        uint saved = Now();

        if (saved < StartTime)
        {
            saved = StartTime + 86400;
        }

        cipher.WriteLong(saved);

        cipher.WriteString(_game.DiedFrom);
        cipher.WriteLong((uint)_loop.Death.TotalPoints());
        cipher.WriteLong((uint)_game.BirthDate);

        // Only the level follows, and a dead character has none. Stopping here
        // is what allows a dead character to be resurrected: everything needed
        // for that has already been written.
        if (_loop.Dead)
        {
            return true;
        }

        cipher.WriteShort((ushort)_game.DungeonLevel);
        cipher.WriteShort((ushort)_game.CharacterRow);
        cipher.WriteShort((ushort)_game.CharacterColumn);
        cipher.WriteShort((ushort)_game.Monsters.BredCount);
        cipher.WriteShort((ushort)_game.Cave.Height);
        cipher.WriteShort((ushort)_game.Cave.Width);
        cipher.WriteShort((ushort)_display.Panel.SavedMaxRow);
        cipher.WriteShort((ushort)_display.Panel.SavedMaxColumn);

        WriteCave(cipher);

        cipher.WriteShort((ushort)_game.Objects.Count);

        for (int i = ObjectPool.FirstIndex; i < _game.Objects.Count; i++)
        {
            cipher.WriteItem(_game.Objects[i]);
        }

        cipher.WriteShort((ushort)_game.Monsters.Count);

        for (int i = MonsterPool.FirstIndex; i < _game.Monsters.Count; i++)
        {
            cipher.WriteMonster(_game.Monsters[i]);
        }

        return true;
    }

    /// <summary>
    /// The level, three times over: what stands where, what lies where, and
    /// then the ground itself run-length encoded.
    /// </summary>
    private void WriteCave(SaveCipher cipher)
    {
        for (int row = 0; row < GameState.DungeonHeight; row++)
        {
            for (int column = 0; column < GameState.DungeonWidth; column++)
            {
                if (_game.Cave[row, column].MonsterIndex != 0)
                {
                    cipher.WriteByte((byte)row);
                    cipher.WriteByte((byte)column);
                    cipher.WriteByte((byte)_game.Cave[row, column].MonsterIndex);
                }
            }
        }

        cipher.WriteByte(0xFF);

        for (int row = 0; row < GameState.DungeonHeight; row++)
        {
            for (int column = 0; column < GameState.DungeonWidth; column++)
            {
                if (_game.Cave[row, column].ObjectIndex != 0)
                {
                    cipher.WriteByte((byte)row);
                    cipher.WriteByte((byte)column);
                    cipher.WriteByte((byte)_game.Cave[row, column].ObjectIndex);
                }
            }
        }

        cipher.WriteByte(0xFF);

        // FAITHFUL QUIRK: the run starts against a previous character of nought
        // and a count of nought, so the first pair written is always a run of
        // no squares - two bytes the reader hands straight back. The original
        // says as much: "note that code may write out two bytes unnecessarily".
        int count = 0;
        byte previous = 0;

        for (int row = 0; row < GameState.DungeonHeight; row++)
        {
            for (int column = 0; column < GameState.DungeonWidth; column++)
            {
                CaveSquare square = _game.Cave[row, column];

                byte packed = (byte)(square.Feature
                    | ((square.LitRoom ? 1 : 0) << 4)
                    | ((square.FieldMark ? 1 : 0) << 5)
                    | ((square.PermanentLight ? 1 : 0) << 6)
                    | ((square.TemporaryLight ? 1 : 0) << 7));

                if (packed != previous || count == 255)
                {
                    cipher.WriteByte((byte)count);
                    cipher.WriteByte(previous);
                    previous = packed;
                    count = 1;
                }
                else
                {
                    count++;
                }
            }
        }

        cipher.WriteByte((byte)count);
        cipher.WriteByte(previous);
    }

    private void WriteStats(SaveCipher cipher)
    {
        for (int i = 0; i < Stat.Count; i++)
        {
            cipher.WriteByte((byte)Player.MaxStat[i]);
        }

        for (int i = 0; i < Stat.Count; i++)
        {
            cipher.WriteByte((byte)Player.CurrentStat[i]);
        }

        cipher.WriteShorts(Player.ModStat, Stat.Count);

        for (int i = 0; i < Stat.Count; i++)
        {
            cipher.WriteByte((byte)Player.UseStat[i]);
        }
    }

    private void WriteFlags(SaveCipher cipher)
    {
        cipher.WriteLong(Player.Status);
        cipher.WriteShort((ushort)Player.Rest);
        cipher.WriteShort((ushort)Player.Blind);
        cipher.WriteShort((ushort)Player.Paralysis);
        cipher.WriteShort((ushort)Player.Confused);
        cipher.WriteShort((ushort)Player.Food);
        cipher.WriteShort((ushort)Player.FoodDigested);
        cipher.WriteShort((ushort)Player.Protection);
        cipher.WriteShort((ushort)Player.Speed);
        cipher.WriteShort((ushort)Player.Hasted);
        cipher.WriteShort((ushort)Player.Slowed);
        cipher.WriteShort((ushort)Player.Afraid);
        cipher.WriteShort((ushort)Player.Poisoned);
        cipher.WriteShort((ushort)Player.Hallucinating);
        cipher.WriteShort((ushort)Player.ProtectionFromEvil);
        cipher.WriteShort((ushort)Player.Invulnerable);
        cipher.WriteShort((ushort)Player.Hero);
        cipher.WriteShort((ushort)Player.SuperHero);
        cipher.WriteShort((ushort)Player.Blessed);
        cipher.WriteShort((ushort)Player.ResistHeat);
        cipher.WriteShort((ushort)Player.ResistCold);
        cipher.WriteShort((ushort)Player.DetectInvisible);
        cipher.WriteShort((ushort)Player.WordOfRecall);
        cipher.WriteShort((ushort)Player.SeeInfrared);
        cipher.WriteShort((ushort)Player.TimedInfravision);
        cipher.WriteByte(Player.SeeInvisible ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.RandomTeleport ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.FreeAction ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.SlowDigestion ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.AggravatesMonsters ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.FireResistant ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.ColdResistant ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.AcidResistant ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.Regenerates ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.LightResistant ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.FeatherFall ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.SustainStrength ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.SustainIntelligence ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.SustainWisdom ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.SustainConstitution ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.SustainDexterity ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.SustainCharisma ? (byte)1 : (byte)0);
        cipher.WriteByte(Player.ConfusingTouch ? (byte)1 : (byte)0);
        cipher.WriteByte((byte)Player.NewSpells);
    }

    /// <summary>
    /// Every option in one long. Mirrors the bit-packing at the top of
    /// sv_write(), including the two flags kept in the sign bits: whether the
    /// character died, and whether they won.
    /// </summary>
    private uint PackOptions()
    {
        uint packed = 0;

        if (_game.CutCorners) packed |= 0x1;
        if (_game.ExamineCorners) packed |= 0x2;
        if (_game.ShowSelfWhileRunning) packed |= 0x4;
        if (_game.StopAtLevelBounds) packed |= 0x8;
        if (_game.PromptBeforeCarrying) packed |= 0x10;
        if (_game.RogueLikeCommands) packed |= 0x20;
        if (_game.ShowWeights) packed |= 0x40;
        if (_game.HighlightSeams) packed |= 0x80;
        if (_game.IgnoreDoorsWhileRunning) packed |= 0x100;
        if (_game.SoundEnabled) packed |= 0x200;
        if (_game.DisplayCounts) packed |= 0x400;
        if (_loop.Dead) packed |= 0x80000000;
        if (_game.TotalWinner) packed |= 0x40000000;

        return packed;
    }

    // --------------------------------------------------------------- restore

    /// <summary>
    /// Reads a game back. Mirrors get_char().
    ///
    /// Three answers, not two. A full restoration puts the player back where
    /// they were and returns true. A dead character's file gives back the
    /// monster memory and the options and returns false, so the caller rolls a
    /// new character who remembers what the last one learned. Anything
    /// unreadable is said aloud and also returns false.
    /// </summary>
    /// <param name="generate">Set when a level still has to be made.</param>
    public bool Restore(string path, out bool generate)
    {
        ArgumentNullException.ThrowIfNull(path);

        generate = true;

        if (!File.Exists(path))
        {
            _display.MessagePrint("Savefile does not exist.");
            return false;
        }

        _display.ClearScreen();
        _display.PutBuffer("Savefile " + path + " present. Attempting restore.", 23, 0);

        if (_game.Turn >= 0)
        {
            _display.MessagePrint("IMPOSSIBLE! Attempt to restore while still alive!");
            return Failed();
        }

        _game.Turn = -1;

        FileStream file;

        try
        {
            file = File.OpenRead(path);
        }
        catch (IOException)
        {
            _display.MessagePrint("Can't open file for reading.");
            return Failed();
        }
        catch (UnauthorizedAccessException)
        {
            _display.MessagePrint("Can't open file for reading.");
            return Failed();
        }

        using (file)
        {
            var cipher = new SaveCipher(file);

            _display.Print("Restoring Memory...", 0, 0);

            cipher.Key = 0;
            byte major = cipher.ReadByte();
            cipher.Key = 0;
            byte minor = cipher.ReadByte();
            cipher.Key = 0;
            byte patch = cipher.ReadByte();
            cipher.Key = 0;
            cipher.Key = cipher.ReadByte();

            // Anything from 5.0.14 on. Later versions are accepted too, since
            // the format was frozen at 5.2.2.
            if (major != VersionMajor || (minor == 0 && patch < 14))
            {
                _display.Print(
                    "Sorry. This savefile is from a different version of umoria.", 2, 0);

                return Failed();
            }

            bool alive;

            try
            {
                alive = Read(cipher, minor, patch, ref generate);
            }
            catch (SaveFileException)
            {
                _display.MessagePrint("Error during reading of file.");
                return Failed();
            }

            if (!alive)
            {
                return false;
            }
        }

        // The old file may be overwritten from now on: it is this game's own.
        FromSaveFile = true;

        if (_game.PanicSaved)
        {
            _display.MessagePrint(
                "This game is from a panic save.  Score will not be added to scoreboard.");
        }
        else if (NotAlreadyScored() && new ScoreFile(_game, _display).IsDuplicate())
        {
            _display.MessagePrint(
                "This character is already on the scoreboard; "
                + "it will not be scored again.");

            _game.NoScore |= 0x4;
        }

        if (_game.NoScore != 0)
        {
            _display.MessagePrint(
                "This save file cannot be used to get on the score board.");
        }

        if (_game.Turn >= 0)
        {
            _game.Inventory.WeaponTooHeavy = false;
            _game.Inventory.PackBurden = 0;
            _loop.Equipment.CheckStrength();

            // The shops restock for every day the game was put away, rounded to
            // the nearest, and never more than ten days' worth.
            StartTime = Now();

            uint age = StartTime < _timeSaved ? 0 : StartTime - _timeSaved;
            age = (age + 43200) / 86400;

            if (age > 10)
            {
                age = 10;
            }

            for (int i = 0; i < age; i++)
            {
                _game.Stores.Maintain();
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// FAITHFUL QUIRK: never. The original writes "(!noscore &amp; 0x04)", which
    /// takes the logical not of the whole word first - nought or one - and then
    /// masks it with four, so the answer is always nought. It plainly means
    /// "the already-scored bit is not set", and it has never once asked. The
    /// bug is harmless: the check it guards only saves the player a message,
    /// since a duplicate is caught again when the score is recorded.
    /// </summary>
    private bool NotAlreadyScored() => ((_game.NoScore == 0 ? 1 : 0) & 0x04) != 0;

    private uint _timeSaved;

    /// <summary>
    /// Reads the body of the file. Mirrors the middle of get_char().
    /// </summary>
    /// <returns>Whether a living character came back.</returns>
    private bool Read(SaveCipher cipher, byte minor, byte patch, ref bool generate)
    {
        bool before522 = minor < 2 || (minor == 2 && patch < 2);

        // The monster memory, which is kept even for a dead character.
        ushort which = cipher.ReadShort();

        while (which != 0xFFFF)
        {
            if (which >= _game.Memories.Count)
            {
                throw new SaveFileException();
            }

            MonsterMemory memory = _game.Memories[which];

            memory.Move = cipher.ReadLong();
            memory.Spells = cipher.ReadLong();
            memory.Kills = cipher.ReadShort();
            memory.Deaths = cipher.ReadShort();
            memory.Defense = cipher.ReadShort();
            memory.Wake = cipher.ReadByte();
            memory.Ignore = cipher.ReadByte();
            cipher.ReadBytes(memory.Attacks, MonsterMemory.MaxAttacks);

            which = cipher.ReadShort();
        }

        // Files before 5.2.2 carry a log index here that nothing reads.
        if (before522)
        {
            cipher.ReadShort();
        }

        uint options = cipher.ReadLong();

        UnpackOptions(options, before522);

        // A character who won is out of the game for good: their level is past
        // what the tables allow, so bringing them back would break things.
        if (_game.Wizard && (options & 0x40000000) != 0)
        {
            _display.MessagePrint("Sorry, this character is retired from moria.");
            _display.MessagePrint("You can not resurrect a retired character.");
        }
        else if (_game.Wizard && (options & 0x80000000) != 0
            && _display.GetCheck("Resurrect a dead character?"))
        {
            options &= ~0x80000000u;
        }

        if ((options & 0x80000000) == 0)
        {
            ReadCharacter(cipher, minor, patch);
        }

        // A dead character's file stops after the shops; so does a file being
        // read only for its memory.
        if (!cipher.HasMore || (options & 0x80000000) != 0)
        {
            return FinishWithoutLevel(options);
        }

        _display.Print("Restoring Character...", 0, 0);

        _game.DungeonLevel = (short)cipher.ReadShort();
        _game.CharacterRow = (short)cipher.ReadShort();
        _game.CharacterColumn = (short)cipher.ReadShort();
        _game.Monsters.BredCount = (short)cipher.ReadShort();

        int height = (short)cipher.ReadShort();
        int width = (short)cipher.ReadShort();

        _game.Cave.Resize(height, width);

        int maxPanelRows = (short)cipher.ReadShort();
        int maxPanelColumns = (short)cipher.ReadShort();

        _display.Panel.RestoreBounds(maxPanelRows, maxPanelColumns, height, width);

        ReadCave(cipher);

        int objects = (short)cipher.ReadShort();

        if (objects > ObjectPool.Capacity)
        {
            throw new SaveFileException();
        }

        for (int i = ObjectPool.FirstIndex; i < objects; i++)
        {
            cipher.ReadItem(_game.Objects[i]);
        }

        _game.Objects.SetCount(objects);

        int monsters = (short)cipher.ReadShort();

        if (monsters > MonsterPool.Capacity)
        {
            throw new SaveFileException();
        }

        for (int i = MonsterPool.FirstIndex; i < monsters; i++)
        {
            cipher.ReadMonster(_game.Monsters[i]);
        }

        _game.Monsters.SetCount(monsters);

        // A level has been restored, so there is nothing to generate.
        generate = false;

        // Shops moved to the end of the file in 5.1.3; before that they came
        // after the level.
        if (minor == 0 || (minor == 1 && patch < 3))
        {
            ReadStores(cipher);
        }

        if (minor == 0 && patch < 16)
        {
            _timeSaved = 0;
        }
        else if (minor == 1 && patch < 3)
        {
            _timeSaved = cipher.ReadLong();
        }

        if (cipher.AtEnd || _game.Turn < 0)
        {
            throw new SaveFileException();
        }

        // The killed-by string is only overwritten for someone still standing.
        if (Player.CurrentHitPoints >= 0)
        {
            _game.DiedFrom = "(alive and well)";
        }

        _game.CharacterGenerated = true;
        return true;
    }

    /// <summary>
    /// What to do when the file ends before the level does: either a dead
    /// character whose memory is all that is wanted, or a resurrection.
    /// </summary>
    private bool FinishWithoutLevel(uint options)
    {
        if ((options & 0x80000000) == 0)
        {
            // The file ran out before the level, which is only allowed for a
            // wizard bringing a character back.
            if (!_game.Wizard || _game.Turn < 0)
            {
                throw new SaveFileException();
            }

            _display.Print("Attempting a resurrection!", 0, 0);

            if (Player.CurrentHitPoints < 0)
            {
                Player.CurrentHitPoints = 0;
                Player.HitPointFraction = 0;
            }

            // Not starved to death, nor poisoned to death, the moment they are
            // back on their feet.
            if (Player.Food < 0)
            {
                Player.Food = 0;
            }

            if (Player.Poisoned > 1)
            {
                Player.Poisoned = 1;
            }

            _game.DungeonLevel = 0;
            _game.CharacterGenerated = true;

            // A resurrection is recorded rather than being a wizard game, so
            // the score board knows about it.
            _game.Wizard = false;
            _game.NoScore |= 0x1;
        }
        else
        {
            _display.MessagePrint("Restoring Memory of a departed spirit...");
            _game.Turn = -1;
        }

        return _game.Turn >= 0;
    }

    private void ReadCharacter(SaveCipher cipher, byte minor, byte patch)
    {
        Player.Name = cipher.ReadString();
        Player.Male = cipher.ReadByte() != 0;
        Player.Gold = (int)cipher.ReadLong();
        Player.MaxExperience = (int)cipher.ReadLong();
        Player.Experience = (int)cipher.ReadLong();
        Player.ExperienceFraction = cipher.ReadShort();
        Player.Age = cipher.ReadShort();
        Player.Height = cipher.ReadShort();
        Player.Weight = cipher.ReadShort();
        Player.Level = cipher.ReadShort();
        Player.MaxDungeonLevel = cipher.ReadShort();
        Player.Search = (short)cipher.ReadShort();
        Player.SearchFrequency = (short)cipher.ReadShort();
        Player.BaseToHit = (short)cipher.ReadShort();
        Player.BaseToHitBows = (short)cipher.ReadShort();
        Player.MaxMana = (short)cipher.ReadShort();
        Player.MaxHitPoints = (short)cipher.ReadShort();
        Player.PlusToHit = (short)cipher.ReadShort();
        Player.PlusToDamage = (short)cipher.ReadShort();
        Player.ArmourClass = (short)cipher.ReadShort();
        Player.PlusToArmourClass = (short)cipher.ReadShort();
        Player.DisplayedPlusToHit = (short)cipher.ReadShort();
        Player.DisplayedPlusToDamage = (short)cipher.ReadShort();
        Player.DisplayedArmourClass = (short)cipher.ReadShort();
        Player.DisplayedToArmourClass = (short)cipher.ReadShort();
        Player.Disarm = (short)cipher.ReadShort();
        Player.Save = (short)cipher.ReadShort();
        Player.SocialClass = (short)cipher.ReadShort();
        Player.Stealth = (short)cipher.ReadShort();
        Player.Class = cipher.ReadByte();
        Player.Race = cipher.ReadByte();
        Player.HitDie = cipher.ReadByte();
        Player.ExperienceFactor = cipher.ReadByte();
        Player.CurrentMana = (short)cipher.ReadShort();
        Player.ManaFraction = cipher.ReadShort();
        Player.CurrentHitPoints = (short)cipher.ReadShort();
        Player.HitPointFraction = cipher.ReadShort();

        for (int i = 0; i < 4; i++)
        {
            Player.History[i] = cipher.ReadString();
        }

        ReadStats(cipher);
        ReadFlags(cipher);

        _game.MissileCounter = (short)cipher.ReadShort();
        _game.Turn = (int)cipher.ReadLong();

        int carried = (short)cipher.ReadShort();

        if (carried > Inventory.WieldSlot)
        {
            throw new SaveFileException();
        }

        for (int i = 0; i < carried; i++)
        {
            cipher.ReadItem(_game.Inventory[i]);
        }

        for (int i = Inventory.WieldSlot; i < Inventory.Size; i++)
        {
            cipher.ReadItem(_game.Inventory[i]);
        }

        int weight = (short)cipher.ReadShort();
        int worn = (short)cipher.ReadShort();

        _game.Inventory.SetCounts(carried, weight, worn);

        Player.SpellLearned = cipher.ReadLong();
        Player.SpellWorked = cipher.ReadLong();
        Player.SpellForgotten = cipher.ReadLong();
        cipher.ReadBytes(Player.SpellOrder, 32);
        cipher.ReadBytes(_game.Knowledge.Flags, ItemKnowledge.FlagCount);

        _game.SetSeeds(cipher.ReadLong(), cipher.ReadLong());

        int lastMessage = (short)cipher.ReadShort();
        var messages = new string[SavedMessages];

        for (int i = 0; i < SavedMessages; i++)
        {
            messages[i] = cipher.ReadString();
        }

        _display.RestoreMessages(messages, lastMessage);

        _game.PanicSaved = cipher.ReadShort() != 0;
        _game.TotalWinner = cipher.ReadShort() != 0;
        _game.NoScore = (short)cipher.ReadShort();
        cipher.ReadShorts(Player.HitPointsByLevel, Player.MaxLevel);

        // Shops came to the end of the file in 5.1.3.
        if (minor >= 2 || (minor == 1 && patch >= 3))
        {
            ReadStores(cipher);
            _timeSaved = cipher.ReadLong();
        }

        if (minor >= 2)
        {
            _game.DiedFrom = cipher.ReadString();
        }

        if (minor >= 3 || (minor == 2 && patch >= 2))
        {
            _game.MaxScore = (int)cipher.ReadLong();
            _game.BirthDate = (int)cipher.ReadLong();
        }
        else
        {
            _game.MaxScore = 0;
            _game.BirthDate = (int)NowStatic();
        }
    }

    private void ReadStores(SaveCipher cipher)
    {
        for (int i = 0; i < Stores.StoreCount; i++)
        {
            Store store = _game.Stores.All[i];

            store.OpenAgainAt = (int)cipher.ReadLong();
            store.InsultsThisVisit = (short)cipher.ReadShort();
            store.Owner = cipher.ReadByte();
            store.StockCount = cipher.ReadByte();
            store.GoodBuys = cipher.ReadShort();
            store.BadBuys = cipher.ReadShort();

            if (store.StockCount > Store.MaxStock)
            {
                throw new SaveFileException();
            }

            for (int j = 0; j < store.StockCount; j++)
            {
                store.Stock[j].Cost = (int)cipher.ReadLong();
                cipher.ReadItem(store.Stock[j].Item);
            }
        }
    }

    private void ReadCave(SaveCipher cipher)
    {
        byte value = cipher.ReadByte();

        while (value != 0xFF)
        {
            int row = value;
            int column = cipher.ReadByte();
            value = cipher.ReadByte();

            if (column > GameState.DungeonWidth || row > GameState.DungeonHeight)
            {
                throw new SaveFileException();
            }

            _game.Cave[row, column].MonsterIndex = value;
            value = cipher.ReadByte();
        }

        value = cipher.ReadByte();

        while (value != 0xFF)
        {
            int row = value;
            int column = cipher.ReadByte();
            value = cipher.ReadByte();

            if (column > GameState.DungeonWidth || row > GameState.DungeonHeight)
            {
                throw new SaveFileException();
            }

            _game.Cave[row, column].ObjectIndex = value;
            value = cipher.ReadByte();
        }

        // The ground itself, run-length encoded across the whole grid rather
        // than row by row, so a run may cross the end of a row.
        int total = 0;
        int at = 0;

        while (total != GameState.DungeonHeight * GameState.DungeonWidth)
        {
            int count = cipher.ReadByte();
            byte packed = cipher.ReadByte();

            if (cipher.AtEnd)
            {
                throw new SaveFileException();
            }

            for (int i = count; i > 0; i--)
            {
                if (at >= GameState.DungeonHeight * GameState.DungeonWidth)
                {
                    throw new SaveFileException();
                }

                CaveSquare square =
                    _game.Cave[at / GameState.DungeonWidth, at % GameState.DungeonWidth];

                square.Feature = (byte)(packed & 0xF);
                square.LitRoom = ((packed >> 4) & 0x1) != 0;
                square.FieldMark = ((packed >> 5) & 0x1) != 0;
                square.PermanentLight = ((packed >> 6) & 0x1) != 0;
                square.TemporaryLight = ((packed >> 7) & 0x1) != 0;

                at++;
            }

            total += count;
        }
    }

    private void ReadStats(SaveCipher cipher)
    {
        for (int i = 0; i < Stat.Count; i++)
        {
            Player.MaxStat[i] = cipher.ReadByte();
        }

        for (int i = 0; i < Stat.Count; i++)
        {
            Player.CurrentStat[i] = cipher.ReadByte();
        }

        cipher.ReadShorts(Player.ModStat, Stat.Count);

        for (int i = 0; i < Stat.Count; i++)
        {
            Player.UseStat[i] = cipher.ReadByte();
        }

        // The modifiers are signed and the file is not, so they have to be
        // brought back into range by hand.
        for (int i = 0; i < Stat.Count; i++)
        {
            Player.ModStat[i] = (short)Player.ModStat[i];
        }
    }

    private void ReadFlags(SaveCipher cipher)
    {
        Player.Status = cipher.ReadLong();
        Player.Rest = (short)cipher.ReadShort();
        Player.Blind = (short)cipher.ReadShort();
        Player.Paralysis = (short)cipher.ReadShort();
        Player.Confused = (short)cipher.ReadShort();
        Player.Food = (short)cipher.ReadShort();
        Player.FoodDigested = (short)cipher.ReadShort();
        Player.Protection = (short)cipher.ReadShort();
        Player.Speed = (short)cipher.ReadShort();
        Player.Hasted = (short)cipher.ReadShort();
        Player.Slowed = (short)cipher.ReadShort();
        Player.Afraid = (short)cipher.ReadShort();
        Player.Poisoned = (short)cipher.ReadShort();
        Player.Hallucinating = (short)cipher.ReadShort();
        Player.ProtectionFromEvil = (short)cipher.ReadShort();
        Player.Invulnerable = (short)cipher.ReadShort();
        Player.Hero = (short)cipher.ReadShort();
        Player.SuperHero = (short)cipher.ReadShort();
        Player.Blessed = (short)cipher.ReadShort();
        Player.ResistHeat = (short)cipher.ReadShort();
        Player.ResistCold = (short)cipher.ReadShort();
        Player.DetectInvisible = (short)cipher.ReadShort();
        Player.WordOfRecall = (short)cipher.ReadShort();
        Player.SeeInfrared = (short)cipher.ReadShort();
        Player.TimedInfravision = (short)cipher.ReadShort();
        Player.SeeInvisible = cipher.ReadByte() != 0;
        Player.RandomTeleport = cipher.ReadByte() != 0;
        Player.FreeAction = cipher.ReadByte() != 0;
        Player.SlowDigestion = cipher.ReadByte() != 0;
        Player.AggravatesMonsters = cipher.ReadByte() != 0;
        Player.FireResistant = cipher.ReadByte() != 0;
        Player.ColdResistant = cipher.ReadByte() != 0;
        Player.AcidResistant = cipher.ReadByte() != 0;
        Player.Regenerates = cipher.ReadByte() != 0;
        Player.LightResistant = cipher.ReadByte() != 0;
        Player.FeatherFall = cipher.ReadByte() != 0;
        Player.SustainStrength = cipher.ReadByte() != 0;
        Player.SustainIntelligence = cipher.ReadByte() != 0;
        Player.SustainWisdom = cipher.ReadByte() != 0;
        Player.SustainConstitution = cipher.ReadByte() != 0;
        Player.SustainDexterity = cipher.ReadByte() != 0;
        Player.SustainCharisma = cipher.ReadByte() != 0;
        Player.ConfusingTouch = cipher.ReadByte() != 0;
        Player.NewSpells = cipher.ReadByte();
    }

    private void UnpackOptions(uint packed, bool before522)
    {
        _game.CutCorners = (packed & 0x1) != 0;
        _game.ExamineCorners = (packed & 0x2) != 0;
        _game.ShowSelfWhileRunning = (packed & 0x4) != 0;
        _game.StopAtLevelBounds = (packed & 0x8) != 0;
        _game.PromptBeforeCarrying = (packed & 0x10) != 0;
        _game.RogueLikeCommands = (packed & 0x20) != 0;
        _game.ShowWeights = (packed & 0x40) != 0;
        _game.HighlightSeams = (packed & 0x80) != 0;
        _game.IgnoreDoorsWhileRunning = (packed & 0x100) != 0;

        // Files older than 5.2.2 have neither of these, and both are turned on
        // rather than off, since that is how those games were played.
        _game.SoundEnabled = before522 || (packed & 0x200) != 0;
        _game.DisplayCounts = before522 || (packed & 0x400) != 0;
    }

    /// <summary>
    /// What the original does with a savefile it cannot use: says so and leaves
    /// the game. Here it only says so - leaving is the caller's business.
    /// </summary>
    private bool Failed()
    {
        _game.Turn = -1;
        _display.Print("Please try again without that savefile.", 1, 0);
        return false;
    }
}

/// <summary>
/// Thrown where the original does "goto error": the file does not hold what it
/// claims to, and nothing further should be believed.
/// </summary>
public sealed class SaveFileException : Exception
{
    public SaveFileException()
        : base("The saved game could not be read.")
    {
    }

    public SaveFileException(string message)
        : base(message)
    {
    }

    public SaveFileException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
