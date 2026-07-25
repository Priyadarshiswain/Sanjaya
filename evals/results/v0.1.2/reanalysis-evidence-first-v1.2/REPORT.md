# Evidence-first skill study scorer v1.2 reanalysis

Status: post-study, additive methodology repair; not a preregistered result.

## Guardrails

- No model was called and no answer was regenerated.
- All 72 original run records were read unchanged.
- The 2 original harness failures remain in their planned denominators.
- Every completed run first reproduced its frozen scorer 1.1.0 result.
- Scorer 1.2.0 was then applied symmetrically to both arms.
- Accepted alternatives were derived only from the arm-blind review recorded in
  [SCORER-V1.2.md](../../../SCORER-V1.2.md); the overfitting risk disclosed
  there applies to every number below.

Input fingerprint: `b8afaff8d3a81f2ae888aa7abd4475fb8e3b1d458e58e4c07b8f6608a8483543`

## Verdict

The three tasks with zero strict successes under scorer 1.1 were limited by
over-rigid accepted phrases, not by agent comprehension. Scorer 1.2 raises
absolute strict success in both arms symmetrically
(native 25/36 to 34/36;
skill 22/36 to 30/36).
0 previously
successful records lost strict success, confirming additivity.

Across 34 completed pairs, scorer 1.2 finds
1 skill-favoring pairs,
5 native-favoring pairs,
and 28 ties. The paired mean
claim-F1 delta (skill minus native) is
-0.029.
Only 4 completed skill sessions used any
Sanjaya tool, so this remains primarily a routing observation rather than a
test of active Sanjaya use.

## Comparison

| Measure | Native 1.1 | Native 1.2 | Skill 1.1 | Skill 1.2 |
|---|---:|---:|---:|---:|
| Strict success / planned | 25/36 | 34/36 | 22/36 | 30/36 |
| Mean claim F1 / completed | 0.893 | 1.000 | 0.867 | 0.971 |
| Mean citation validity / completed | 0.787 | 0.787 | 0.801 | 0.801 |

### Task-level strict results

| Task | Native 1.1 | Native 1.2 | Skill 1.1 | Skill 1.2 |
|---|---:|---:|---:|---:|
| SJ-EVAL-0001 | 2/3 | 2/3 | 3/3 | 3/3 |
| SJ-EVAL-0002 | 3/3 | 3/3 | 3/3 | 3/3 |
| SJ-EVAL-0003 | 3/3 | 3/3 | 2/3 | 2/3 |
| SJ-EVAL-0004 | 3/3 | 3/3 | 2/3 | 2/3 |
| SJ-EVAL-0005 | 3/3 | 3/3 | 2/3 | 2/3 |
| SJ-EVAL-0006 | 3/3 | 3/3 | 3/3 | 3/3 |
| SJ-EVAL-0007 | 0/3 | 3/3 | 0/3 | 3/3 |
| SJ-EVAL-0008 | 0/3 | 3/3 | 0/3 | 2/3 |
| SJ-EVAL-0009 | 3/3 | 3/3 | 2/3 | 2/3 |
| SJ-EVAL-0010 | 0/3 | 3/3 | 0/3 | 3/3 |
| SJ-EVAL-0011 | 2/3 | 2/3 | 2/3 | 2/3 |
| SJ-EVAL-0012 | 3/3 | 3/3 | 3/3 | 3/3 |

## Interpretation boundary

This repair improves the measurement contract, not the product. It does not
change routing, token, latency, or side-effect measurements, and it does not
convert the completed study into evidence for a marketplace benefit claim.
