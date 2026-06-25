# I2MS_Proteoform_Pipeline

A console tool that builds a predicted proteoform database for a protein (UniProt +
PRIDE + PTMeXchange annotations, PTM combinations, and truncations) and matches it
against Individual-Ion Mass Spectrometry (I2MS) `.dmt` files.

## Build & test

```bash
dotnet build ProteoformAnalyzer/ProteoformAnalyzer.csproj
dotnet test  ProteoformAnalyzer.Tests/ProteoformAnalyzer.Tests.csproj
```

CI (`.github/workflows/ci.yml`) builds and runs the test suite on every push and PR.

## Robustness features

- **Tests + CI** — unit/integration tests in `ProteoformAnalyzer.Tests` cover the mass
  calculations, proteoform construction (including PTM site-collision feasibility),
  truncations, decoy/FDR logic, `.dmt` parsing/validation, and an end-to-end spectrum
  match; GitHub Actions runs them automatically.
- **Network resilience & reproducibility** — database queries go through
  `ResilientJson` (retry with exponential backoff, honoring `Retry-After`) and a local
  `ResponseCache`. Each run writes a `<output>.manifest.txt` sidecar (`RunManifest`)
  recording the tool version, parameters, and every source queried (or served from
  cache) with status and time.
- **False-discovery rate** — an optional target–decoy estimate (`DecoyGenerator` +
  `FdrEstimator`) reports the chance-match rate; decoys are excluded from the output CSV.
- **Input hardening** — `.dmt` files are validated up front (readable SQLite, an `Ion`
  table with `Mz`/`Charge` columns) so malformed inputs fail with a clear message.

## Disease context

When a UniProt accession is used, each proteoform is annotated (free, from the same
cached UniProt record) with two honestly-scoped columns:

- **Protein Disease Involvement** — diseases the protein is implicated in (UniProt
  DISEASE annotations).
- **PTM Sites at Variants** — PTM sites that colocalize with an annotated sequence
  variant (candidate "PTM-disrupting variant" sites).

These surface disease *context* for prioritisation; they do not classify a proteoform as
pathological. True disease-vs-healthy discrimination requires site-level curated data
(e.g. PhosphoSitePlus) and/or case-vs-control quantitation of the ion counts.
