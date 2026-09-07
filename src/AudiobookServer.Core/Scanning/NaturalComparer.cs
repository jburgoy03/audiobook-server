namespace AudiobookServer.Core.Scanning;

/// <summary>
/// Orders strings so embedded numbers compare numerically: "Chapter 2" before "Chapter 10".
/// Plain lexicographic ordering gets that backwards whenever filenames aren't zero-padded.
/// </summary>
public sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;

        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                // Compare the full numeric runs, ignoring leading zeros.
                int si = i, sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;

                var nx = x.AsSpan(si, i - si).TrimStart('0');
                var ny = y.AsSpan(sj, j - sj).TrimStart('0');

                if (nx.Length != ny.Length)
                    return nx.Length - ny.Length;

                var numeric = nx.SequenceCompareTo(ny);
                if (numeric != 0) return numeric;
            }
            else
            {
                var c = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
        }

        return (x.Length - i) - (y.Length - j);
    }
}