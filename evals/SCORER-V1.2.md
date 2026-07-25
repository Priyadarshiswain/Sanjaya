# Scorer v1.2 methodology

Status: additive versioned methodology. Independent fixture review is required
before use. It does not replace the frozen v1.0 or v1.1 scores or authorize
another model run.

## Problem being corrected

Five required claims in the frozen task set encode a multi-word natural-language
phrase as their only accepted value: `SJ-EVAL-0007:hit_guard`,
`SJ-EVAL-0007:produce_failure`, `SJ-EVAL-0008:overlapped_work`,
`SJ-EVAL-0008:failure_policy`, and `SJ-EVAL-0010:child_cleanup`. Scorer 1.1
accepts a canonical value inside explanatory text, but it cannot accept a
correct answer that restructures a phrase ("importing the environment package"
for "environment package loading"; "swallowed with `.catch(() => {})`" for
"ignored"). Across the pilot, guided, and evidence-first studies these three
tasks recorded zero strict successes in every arm and repetition, under both
frozen scorers.

An arm-blind extraction of all 63 completed supplied values for these five
claims ([`fixtures/scorer-v1.2-review.json`](fixtures/scorer-v1.2-review.json),
sorted with run identities and arm labels removed) found every supplied value
semantically correct. The defect is in the accepted values, not the agents.

## Deterministic v1.2 rule

Scorer 1.2 is scorer 1.1 plus two additive mechanisms:

1. A new `all_of` match mode. `acceptedValues` becomes a list of groups; every
   group must match, and a group matches when any of its interchangeable
   variants matches under the unchanged v1.1 canonical-token rules (boundaries,
   wrappers, negation rejection). This expresses two-part claims such as
   "end stdin AND kill the child" without accepting either half alone.
2. A frozen claim-amendment table (`CLAIM_AMENDMENTS_V1_2`) applied by
   `amendTaskV1_2`. It never edits `tasks/pilot.json`; it extends accepted
   values, or converts a phrase claim to `all_of`, for exactly the five claims
   above. Every original frozen phrase still matches after amendment, so no
   result that passed scorer 1.1 can fail scorer 1.2.

All other claims, all citation checks, forbidden-claim checks, duplicate-claim
rejection, and path containment are byte-identical to scorer 1.1.

## Departure from the v1.1 non-goals

Scorer 1.1 ruled out task-specific post-hoc rules. Scorer 1.2 introduces
claim-specific amendments and therefore must satisfy stricter conditions:

- alternatives were derived only from the arm-blind review file, in which
  every value was judged correct before any arm label was consulted;
- the matcher itself remains deterministic: no edit distance, embeddings,
  stemming, language-model judgment, or per-answer discretion;
- amendments are frozen in code, versioned, and applied symmetrically to
  every arm; and
- the overfitting risk (accepted alternatives tuned to observed answers) is
  disclosed wherever v1.2 results are reported, and v1.2 results are never
  presented as preregistered evidence.

## Review and versioning

The fixture set (`fixtures/scorer-v1.2/cases.json`) contains observed
arm-hidden answer shapes and semantic holdouts, including a negation case and
single-half `all_of` rejections. The test suite additionally proves
frozen-phrase additivity for every amended claim and that unamended claims are
untouched.

Scorer 1.2 is additive. The published v0.1.2 run records retain their original
scores. Any reanalysis must rescore every completed record with exactly
`1.2.0`, publish old and new scores side by side, reproduce the frozen scores
first, preserve failures and planned denominators, and identify itself as
post-study methodology repair.