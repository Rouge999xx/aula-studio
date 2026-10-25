namespace AulaStudio.Protocol;

/// <summary>One layer of the key matrix: 128 slots × 4 bytes. Slot 127 holds the <c>00 00 5A A5</c> trailer.</summary>
public sealed class KeyMatrix
{
    public const int Slots = 128;
    public const int Size = Slots * 4;
    public const int FnSlot = 53;
    public const int KnobPressSlot = 84;
    public const int TrailerSlot = 127;

    readonly byte[] data;

    public KeyMatrix(byte[] bytes)
    {
        if (bytes.Length != Size) throw new ArgumentException($"A key table is {Size} bytes, got {bytes.Length}.");
        data = (byte[])bytes.Clone();
    }

    public KeyEntry this[int slot]
    {
        get => KeyEntry.FromBytes(data.AsSpan(slot * 4, 4));
        set
        {
            data[slot * 4] = value.B0;
            data[slot * 4 + 1] = value.B1;
            data[slot * 4 + 2] = value.B2;
            data[slot * 4 + 3] = value.B3;
        }
    }

    public bool HasTrailer => this[TrailerSlot] == KeyEntry.Trailer;

    /// <summary>Slots that only change with "Advanced" switched on (the trailer never changes).</summary>
    public static bool IsProtected(int slot) => slot is FnSlot or KnobPressSlot or TrailerSlot;

    public byte[] ToArray() => (byte[])data.Clone();
    public KeyMatrix Clone() => new(data);

    public IEnumerable<int> DifferencesFrom(KeyMatrix other)
    {
        for (int i = 0; i < Slots; i++)
            if (this[i] != other[i]) yield return i;
    }

    public bool SameAs(KeyMatrix other) => data.AsSpan().SequenceEqual(other.data);

    public static KeyMatrix FromHex(string hex) => new(Convert.FromHexString(hex));
    public string ToHex() => Convert.ToHexString(data);
}
