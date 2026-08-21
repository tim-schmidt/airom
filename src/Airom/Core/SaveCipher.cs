// Ported from the byte primitives and the high-score record in Umoria 5.6
// source/save.c - wr_byte, wr_short, wr_long, wr_bytes, wr_string, their
// reading halves, and wr_highscore/rd_highscore.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Text;

namespace Airom.Core;

/// <summary>
/// One line of the score file. Mirrors Umoria's high_scores.
/// </summary>
public sealed class HighScore
{
    /// <summary>How long a name may be. Umoria's PLAYER_NAME_SIZE.</summary>
    public const int NameLength = 27;

    /// <summary>How long the cause of death may be.</summary>
    public const int DiedFromLength = 25;

    public int Points { get; set; }

    /// <summary>
    /// When the character was rolled. On a single-user machine this is what
    /// tells two characters apart, since there are no user ids to do it.
    /// </summary>
    public int BirthDate { get; set; }

    /// <summary>
    /// Which player owns the entry. Always nought here: it is the user id of a
    /// shared Unix score file, and this is a single-user game.
    /// </summary>
    public int Uid { get; set; }

    public int MaxHitPoints { get; set; }

    public int CurrentHitPoints { get; set; }

    public int DungeonLevel { get; set; }

    public int Level { get; set; }

    public int MaxDungeonLevel { get; set; }

    /// <summary>'M' or 'F'.</summary>
    public char Sex { get; set; }

    public int Race { get; set; }

    public int Class { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DiedFrom { get; set; } = string.Empty;
}

/// <summary>
/// The save file's own encoding, as a stream of bytes.
///
/// Every byte is exclusive-ored with the one before it, so a saved file is not
/// quite readable and a single corrupt byte spoils everything after it. That is
/// not security - the chain starts from a known value - it is only enough to
/// stop a player editing their own score with a text editor.
///
/// The chain is carried in <see cref="Key"/> and runs from the first byte
/// written, which is why each high-score record starts by writing the key: it
/// exclusive-ors with itself, leaving a nought that both sides then chain from.
/// </summary>
public sealed class SaveCipher
{
    private readonly Stream _stream;

    public SaveCipher(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
    }

    /// <summary>The running exclusive-or. Umoria's xor_byte.</summary>
    public byte Key { get; set; }

    /// <summary>Whether the last read ran off the end of the file.</summary>
    public bool AtEnd { get; private set; }

    /// <summary>Where in the file the next byte goes. Umoria's ftell().</summary>
    public long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    public void WriteByte(byte value)
    {
        Key ^= value;
        _stream.WriteByte(Key);
    }

    public void WriteShort(ushort value)
    {
        WriteByte((byte)(value & 0xFF));
        WriteByte((byte)((value >> 8) & 0xFF));
    }

    public void WriteLong(uint value)
    {
        WriteByte((byte)(value & 0xFF));
        WriteByte((byte)((value >> 8) & 0xFF));
        WriteByte((byte)((value >> 16) & 0xFF));
        WriteByte((byte)((value >> 24) & 0xFF));
    }

    /// <summary>
    /// A fixed-width run of bytes. Text shorter than the width is padded with
    /// noughts, as C's fixed-size character arrays are.
    /// </summary>
    public void WriteFixed(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (int i = 0; i < width; i++)
        {
            WriteByte(i < text.Length ? (byte)text[i] : (byte)0);
        }
    }

    /// <summary>
    /// Reads one byte, undoing the chain. Sets <see cref="AtEnd"/> and returns
    /// nought at the end of the file, which is how the original's feof() is
    /// noticed after the fact.
    /// </summary>
    public byte ReadByte()
    {
        int raw = _stream.ReadByte();

        if (raw < 0)
        {
            AtEnd = true;
            return 0;
        }

        byte value = (byte)(raw ^ Key);
        Key = (byte)raw;
        return value;
    }

    public ushort ReadShort()
    {
        byte low = ReadByte();
        byte high = ReadByte();
        return (ushort)(low | (high << 8));
    }

    public uint ReadLong()
    {
        uint value = ReadByte();
        value |= (uint)ReadByte() << 8;
        value |= (uint)ReadByte() << 16;
        value |= (uint)ReadByte() << 24;
        return value;
    }

    /// <summary>A fixed-width run of bytes, cut at the first nought.</summary>
    public string ReadFixed(int width)
    {
        var text = new StringBuilder();
        bool ended = false;

        for (int i = 0; i < width; i++)
        {
            byte value = ReadByte();

            if (value == 0)
            {
                ended = true;
            }

            if (!ended)
            {
                text.Append((char)value);
            }
        }

        return text.ToString();
    }

    /// <summary>Writes one score. Mirrors wr_highscore().</summary>
    public void WriteHighScore(HighScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        // The key first, which exclusive-ors with itself: every record starts
        // its chain from a nought, so one can be read without the last.
        WriteByte(Key);

        WriteLong((uint)score.Points);
        WriteLong((uint)score.BirthDate);
        WriteShort((ushort)score.Uid);
        WriteShort((ushort)score.MaxHitPoints);
        WriteShort((ushort)score.CurrentHitPoints);
        WriteByte((byte)score.DungeonLevel);
        WriteByte((byte)score.Level);
        WriteByte((byte)score.MaxDungeonLevel);
        WriteByte((byte)score.Sex);
        WriteByte((byte)score.Race);
        WriteByte((byte)score.Class);
        WriteFixed(score.Name, HighScore.NameLength);
        WriteFixed(score.DiedFrom, HighScore.DiedFromLength);
    }

    /// <summary>Reads one score. Mirrors rd_highscore().</summary>
    public HighScore ReadHighScore()
    {
        // Reading the key sets the chain from the raw byte, which is the
        // nought the writer put there.
        ReadByte();

        return new HighScore
        {
            Points = (int)ReadLong(),
            BirthDate = (int)ReadLong(),
            Uid = ReadShort(),
            MaxHitPoints = ReadShort(),
            CurrentHitPoints = ReadShort(),
            DungeonLevel = ReadByte(),
            Level = ReadByte(),
            MaxDungeonLevel = ReadByte(),
            Sex = (char)ReadByte(),
            Race = ReadByte(),
            Class = ReadByte(),
            Name = ReadFixed(HighScore.NameLength),
            DiedFrom = ReadFixed(HighScore.DiedFromLength),
        };
    }

    /// <summary>How many bytes one score takes up, header byte included.</summary>
    public static int HighScoreLength =>
        1 + 4 + 4 + 2 + 2 + 2 + 6 + HighScore.NameLength + HighScore.DiedFromLength;
}
