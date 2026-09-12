# Vitara health — superseded

This document described the health analysis layer before it was split into its own
process, and it no longer matches the code. It does not cover Vitara Insight, manual
measurements, correlations, the Apple Health XML import, or the separation of Apple's
SDNN from Oura's RMSSD.

**The current specification is [`VITARA-SPEC.html`](VITARA-SPEC.html).** Open it in a
browser — it covers:

- what is pulled from Oura, endpoint by endpoint and field by field
- all 28 SQLite tables, and which process writes each
- every algorithm by name — MAD, Theil–Sen, Mann–Kendall, Spearman, Fisher z,
  Benjamini–Hochberg — and why each was chosen over the obvious alternative
- every detector, every threshold and its environment variable
- the finding lifecycle and how findings reach the user
- the failures that shaped the design, and what is not yet built

Kept rather than deleted so that older links into the repo still land somewhere that
says where to go.
