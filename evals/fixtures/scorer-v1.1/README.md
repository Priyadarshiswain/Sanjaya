# Scorer v1.1 review fixtures

These fixtures test the deterministic boundary between formatting variation
and semantic interpretation.

The observed cases were selected from completed v0.1.2 pilot answers across
both headline arms. Run IDs, repetitions, and arm labels were removed before
the expected labels were recorded. The cases are therefore suitable for an
arm-blind maintainer review, but they are not a statistical sample and do not
measure reviewer agreement.

The synthetic adversarial cases guard against the main false-positive risks:
substrings inside larger identifiers or numbers, explicit negation, duplicate
claims, and extra set members. The semantic holdout proves that v1.1 does not
award credit merely because a paraphrase appears plausible.

Before merging a scorer change, a reviewer who did not implement its matching
logic should inspect every `expectedMatch` label without consulting source run
arm labels. If a label changes, record the reason in the pull request and
change the scorer version.
