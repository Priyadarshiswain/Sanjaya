# Scorer v1.1 methodology

Status: additive versioned methodology. Independent fixture review is required
before use. It does not replace the frozen v1.0 scores or authorize another
model run.

## Problem being corrected

Scorer 1.0 required a claim value to equal a terse canonical string after
Unicode normalization, trimming, and whitespace collapse. The answer contract
required keyed claims but did not require terse values. Correct answers such
as a type followed by “(sealed class),” a method inside its signature, or a
commit subject after its SHA therefore received no claim credit.

## Deterministic v1.1 rule

For `exact` and `one_of` claims, v1.1:

1. applies Unicode NFC, trims, and collapses whitespace;
2. preserves case for exact values and sets, while treating only registered
   boolean alternatives (`yes`, `no`, `true`, and `false`) case-insensitively;
3. accepts harmless outer Markdown, quote, and trailing-punctuation wrappers;
4. accepts a canonical value as a bounded token inside explanatory text; and
5. rejects a token embedded in a larger word or number, a dotted canonical
   prefix extended by another member, or a mechanically visible negation.

`set_exact` remains conservative: values must be pipe-delimited, with the same
members and no extras. `citation_only` ignores claim text and still requires
the normal evidence checks. Duplicate answers for one required key receive no
credit because their intended value is ambiguous.

Repository-backed citation checks resolve real paths before reading. This
keeps a symlinked citation from escaping the repository root.

## Explicit non-goals

The scorer does not use edit distance, embeddings, a language model, stemming,
or task-specific post-hoc rules. It does not treat a semantic paraphrase as
correct unless that wording was preregistered as an accepted value. Claims that
need semantic judgment must use blinded human review or a separately
calibrated judge, as required by the evaluation specification.

This limitation is intentional. A deterministic scorer that guesses meaning
after arm labels are visible can bias the comparison more severely than an
under-crediting scorer.

## Review and versioning

The fixture set contains observed answer shapes drawn from both headline arms
with run identities and arm labels removed, plus adversarial false-positive
cases and a semantic holdout. A maintainer who did not implement the matcher
should review the expected labels before merge.

Scorer 1.1 is additive. The published v0.1.2 run records and reports retain
their original `1.0.0` scores. Any reanalysis must:

- rescore every completed arm with exactly `1.1.0`;
- publish the old and new scores side by side;
- identify the reanalysis as post-pilot methodology repair;
- preserve failures and planned denominators; and
- avoid presenting the repaired score as preregistered evidence.

No new paid run should begin until the fixtures and implementation pass
independent review.
