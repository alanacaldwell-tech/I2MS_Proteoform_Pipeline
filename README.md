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
cached UniProt record) with three honestly-scoped columns, placed **at the end of the
CSV** (after any per-file ion-count/mass columns) so they are easy to find:

- **Protein Disease Involvement** — diseases the protein is implicated in (UniProt
  DISEASE annotations). Protein-level context, identical for every proteoform of a protein.
- **Disease-Relevant Proteoform** — a per-proteoform label derived from the variant
  site(s) *this specific proteoform* carries:
  - `Disease variant PTM` — carries a modification at a sequence variant that is linked to
    one of the protein's documented diseases (or carries pathogenic annotation).
  - `Sequence variant PTM` — carries a modification at a variant of unspecified clinical
    significance.
  - *(blank)* — no variant-colocalized modification; consistent with the normal/reference
    form. The unmodified form, a form modified only at other positions, and a variant site
    removed by a truncation are all left blank.

  Filter the CSV on this column to isolate the disease-relevant proteoforms.
- **PTM Sites at Variants** — the specific variant-colocalized PTM site(s) the proteoform
  carries, each described against the normal reference, e.g. *"S129 Phosphoserine —
  modification disrupted by a Parkinson disease (PARK1)-associated variant (…); the site is
  intact in the normal/reference sequence"*.

These surface disease *context* for prioritisation; they do not classify a proteoform as
pathological. The proteoform-level label is a *composition* claim (this form carries a
modification at a variant-colocalized site), **not** an *abundance* claim — it does not
assert the proteoform is upregulated in, or unique to, disease. Establishing
"upregulated" / "only found in disease" requires case-vs-control quantitation of the
per-file ion counts (label your `.dmt` samples by condition); true site-level
classification requires curated data (e.g. PhosphoSitePlus).
