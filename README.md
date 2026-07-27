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

## Focusing on a sequence region

In interactive mode you can restrict the analysis to part of a protein — useful for a known
cleavage product. After a sequence/accession is loaded you're prompted for a region:

```
Restrict analysis to a sequence region? Examples: "1098-1255", "1098-" (to the C-terminus),
"-500" (from the N-terminus). Press Enter for the whole sequence (1-1255):
```

The sequence is sliced to the chosen residues, PTMs are kept only if they fall inside the region
(remapped to the fragment), and proteoforms — including *further* truncations of the fragment —
are built from it. Truncation ranges are reported in the original UniProt coordinates (e.g. a
truncation of the MUC1 C-terminus shows `1099-1255`, not `1-158`), and the region is recorded in
the protein label and default output filename (`<id>:1098-1255`).

## Recombinant / tagged constructs

If you measured a recombinant construct (e.g. a His-tagged protein), enter the **construct
sequence**, then supply a UniProt accession when prompted:

```
Map known PTMs from a UniProt reference (e.g. a tagged construct)? Enter accession or press Enter to skip:
```

The reference sequence is aligned to the construct (`SequenceAligner`; a substring fast path for
clean terminal tags, otherwise a local Smith–Waterman alignment). Known PTMs annotated against the
reference are remapped onto the construct's coordinates; residues that fall in tags, linkers, or
substituted positions are not mapped, so their PTMs are dropped.

## Point mutations (known variants)

When a UniProt reference is available, you can include known single-residue substitutions
("Natural variant" features). Each is turned into a position-independent mass shift (the
average-mass difference of the substituted residue) and used to generate variant proteoforms.

Bounds (to keep the database tractable):

- **At most one substitution per proteoform** — proteoforms are reference *or* single-variant.
- Variants are applied to the **intact form and the single-PTM forms only**, not to the
  cross-family combinations or truncations, so they add a bounded multiplier rather than
  multiplying the whole space.
- Residue-level interplay (e.g. a variant that removes a phosphosite) is **not** modelled — the
  shift is treated as independent of any co-occurring PTM.

## Truncations and internal fragments

N- and C-terminal truncations are generated as before. Two options bound the cost and add
internal fragments:

- **Max residues per terminus** — caps how deep single-ended truncations go (default: no limit).
- **Internal fragments** (opt-in) — fragments truncated at *both* termini, for proteins with
  internal cleavage. This is O(depth²), so it requires the per-terminus cap (defaulted to 50 when
  enabled) and a **minimum fragment length** (default 20 aa) to stay bounded.

Truncation/fragment ranges are reported in the original full-length coordinates (respecting any
region restriction or recombinant alignment offset).

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
