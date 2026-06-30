namespace ProteoformAnalyzer;

/// <summary>
/// Maps annotation coordinates from a reference sequence (e.g. the UniProt canonical sequence)
/// onto a query sequence (e.g. a recombinant / tagged construct), so known PTMs annotated against
/// the reference can be placed on the construct that was actually measured.
///
/// Strategy:
///   1. Fast path — if the reference is a contiguous substring of the query (a clean N-/C-terminal
///      tag) or the query is a contiguous substring of the reference (the construct is a native
///      fragment), the mapping is a simple offset.
///   2. Otherwise a local (Smith–Waterman) alignment is run; only exact-match columns are mapped,
///      so tags, linkers and substitutions fall outside the mapping and their PTMs are dropped.
///
/// The Smith–Waterman matrix is O(n·m); for very large sequences it is skipped (returns an empty
/// map) rather than risk exhausting memory — the substring fast path still covers the common cases.
/// </summary>
public static class SequenceAligner
{
    private const int MatchScore = 2;
    private const int MismatchScore = -1;
    private const int GapScore = -2;

    // Skip the full O(n·m) matrix above this many cells (~64M ints ≈ 256 MB) to avoid OOM.
    private const long MaxMatrixCells = 64_000_000;

    /// <summary>
    /// Returns a map from each 1-based reference position to the 1-based query position it aligns to,
    /// for exactly-matching residues only. Reference positions with no confident mapping are omitted.
    /// </summary>
    public static Dictionary<int, int> MapReferenceToQuery(string reference, string query)
    {
        var map = new Dictionary<int, int>();
        int n = reference.Length, m = query.Length;
        if (n == 0 || m == 0) return map;

        // Fast path 1: reference fully contained in query (e.g. tagged construct).
        int idx = query.IndexOf(reference, StringComparison.Ordinal);
        if (idx >= 0)
        {
            for (int i = 1; i <= n; i++) map[i] = idx + i;
            return map;
        }

        // Fast path 2: query fully contained in reference (construct is a native sub-fragment).
        int idx2 = reference.IndexOf(query, StringComparison.Ordinal);
        if (idx2 >= 0)
        {
            for (int j = 1; j <= m; j++) map[idx2 + j] = j;
            return map;
        }

        // General case: local alignment, unless the matrix would be too large.
        if ((long)n * m > MaxMatrixCells) return map;

        var h = new int[n + 1, m + 1];
        var trace = new byte[n + 1, m + 1];   // 0=stop, 1=diagonal, 2=up (ref gap), 3=left (query gap)
        int best = 0, bi = 0, bj = 0;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int diag = h[i - 1, j - 1] + (reference[i - 1] == query[j - 1] ? MatchScore : MismatchScore);
                int up = h[i - 1, j] + GapScore;
                int left = h[i, j - 1] + GapScore;

                int s = 0; byte t = 0;
                if (diag > s) { s = diag; t = 1; }
                if (up > s) { s = up; t = 2; }
                if (left > s) { s = left; t = 3; }

                h[i, j] = s;
                trace[i, j] = t;
                if (s > best) { best = s; bi = i; bj = j; }
            }
        }

        int ci = bi, cj = bj;
        while (ci > 0 && cj > 0 && trace[ci, cj] != 0)
        {
            switch (trace[ci, cj])
            {
                case 1:
                    if (reference[ci - 1] == query[cj - 1]) map[ci] = cj;   // record exact matches only
                    ci--; cj--;
                    break;
                case 2:
                    ci--;
                    break;
                default:
                    cj--;
                    break;
            }
        }
        return map;
    }
}
