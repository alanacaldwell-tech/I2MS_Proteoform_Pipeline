# IonCounter

Counts I2MS ions in `.dmt` files against a set of reference centroid masses.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build & run

```bash
cd IonCounter
dotnet run
```

Or publish a self-contained executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

## Reference CSV format

The first row is headers (any text). Column layout:

| Column | Content |
|--------|---------|
| 0 | Centroid mass in Daltons |
| 1 | Tolerance (±Da) — ions within `[centroid − tol, centroid + tol]` are counted |

See `sample_centroids.csv` for an example.

## .dmt file format

`.dmt` files are plain-text, delimiter-separated files (tab, comma, or space).
On first run the program detects the delimiter and prints the column headers (if
present), then asks you which column holds the ion mass. The choice applies to
all files in the batch.

## Output

A new CSV is written next to the reference CSV, named `<input>_ion_counts.csv`.
Each `.dmt` file adds one column whose header is the file name and whose values
are the ion count for each centroid row.
