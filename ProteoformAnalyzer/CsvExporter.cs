namespace ProteoformAnalyzer;

public static class CsvExporter
{
    /// <summary>
    /// Exports the proteoform database without experimental data (no .dmt files processed).
    /// Columns: Protein, Modification Name, Alternative Name, Predicted Centroid Mass (Da),
    ///          Tolerance Range, Envelope Sigma (Da), Protein Disease Involvement,
    ///          Disease-Relevant Proteoform, PTM Sites at Variants
    /// </summary>
    public static void ExportDatabase(List<ProteoformEntry> entries, string path)
    {
        using var w = new StreamWriter(path);
        w.WriteLine("Protein,Modification Name,Alternative Name,Predicted Centroid Mass (Da),Tolerance Range,Envelope Sigma (Da)," +
                    "Protein Disease Involvement,Disease-Relevant Proteoform,PTM Sites at Variants");

        foreach (var e in entries)
        {
            w.WriteLine($"{Csv(e.ProteinLabel)},{Csv(e.ModificationName)},{Csv(e.AlternativeName)}," +
                        $"{e.CentroidMass:F4},+/- {e.Tolerance:F1} Da," +
                        $"{(e.Envelope is not null ? e.Envelope.Sigma.ToString("F3") : "")}," +
                        $"{Csv(string.Join("; ", e.ProteinDiseaseInvolvement))},{Csv(e.DiseaseRelevance)},{Csv(string.Join("; ", e.PtmVariantSites))}");
        }
    }

    /// <summary>
    /// Exports matched proteoforms with match-quality columns, per-file ion counts, and
    /// per-file experimental masses. Each database proteoform is one row: slightly different
    /// experimental masses that track to the same proteoform are merged, with the single
    /// "Mean Experimental Mass" column holding the ion-count-weighted representative and the
    /// per-file experimental masses preserved in trailing columns.
    /// Column order: Protein | Modification Name | Alternative Name | Predicted Mass (Da) |
    ///          Mean Experimental Mass (Da) | Mass Error (Da) | Charge States Observed |
    ///          # Charge States | Match Rank |
    ///          [file1] Ion Count | … | [file1] Exp Mass (Da) | … |
    ///          Protein Disease Involvement | Disease-Relevant Proteoform | PTM Sites at Variants
    /// The per-file columns stay together in the middle (ion counts first, then experimental masses)
    /// and the disease-context columns are placed last so they are easy to find regardless of how
    /// many .dmt files were processed. Only proteoforms matched in at least one file are included.
    /// </summary>
    public static void ExportResults(
        List<AnalysisResult> results,
        IReadOnlyList<string> fileNames,
        string path)
    {
        using var w = new StreamWriter(path);

        // Header — fixed match columns, then per-file ion counts, then per-file experimental masses,
        // then the disease-context columns last.
        var header = "Protein,Modification Name,Alternative Name,Predicted Centroid Mass (Da),Mean Experimental Mass (Da)," +
                     "Mass Error (Da),Charge States Observed,# Charge States,Match Rank";
        foreach (var fn in fileNames)
            header += $",{Csv(fn)} Ion Count";
        foreach (var fn in fileNames)
            header += $",{Csv(fn)} Exp Mass (Da)";
        header += ",Protein Disease Involvement,Disease-Relevant Proteoform,PTM Sites at Variants";
        w.WriteLine(header);

        foreach (var r in results)
        {
            string protein   = Csv(r.DatabaseEntry.ProteinLabel);
            string name      = Csv(r.DatabaseEntry.ModificationName);
            string altName   = Csv(r.DatabaseEntry.AlternativeName);
            string pred      = r.DatabaseEntry.CentroidMass.ToString("F4");
            string expt      = r.ExperimentalCentroid.ToString("F4");
            string massErr   = r.MassErrorDa.ToString("F4");
            string charges   = Csv(string.Join(";", r.ChargeStatesObserved));
            string nCharges  = r.ChargeStatesObserved.Count.ToString();
            string rank      = r.RankWithinPeak.ToString();
            string row       = $"{protein},{name},{altName},{pred},{expt},{massErr},{charges},{nCharges},{rank}";

            // Per-file ion counts (0 where undetected)
            foreach (var fn in fileNames)
            {
                long cnt = r.IonCountsPerFile.TryGetValue(fn, out long c) ? c : 0;
                row += $",{cnt}";
            }

            // Per-file experimental masses (blank where undetected)
            foreach (var fn in fileNames)
            {
                row += r.ExperimentalMassPerFile.TryGetValue(fn, out double mass)
                    ? $",{mass:F4}"
                    : ",";
            }

            // Disease-context columns last.
            string disease   = Csv(string.Join("; ", r.DatabaseEntry.ProteinDiseaseInvolvement));
            string relevant  = Csv(r.DatabaseEntry.DiseaseRelevance);
            string variants  = Csv(string.Join("; ", r.DatabaseEntry.PtmVariantSites));
            row += $",{disease},{relevant},{variants}";

            w.WriteLine(row);
        }
    }

    private static string Csv(string v) =>
        v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? $"\"{v.Replace("\"", "\"\"")}\""
            : v;
}
