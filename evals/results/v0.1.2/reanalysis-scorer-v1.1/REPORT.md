# Sanjaya v0.1.2 scorer v1.1 reanalysis

Status: post-pilot, additive methodology repair; not a preregistered result.

## Guardrails

- No model was called and no answer was regenerated.
- All 90 original run records were read unchanged.
- The 3 original harness failures remain in their planned denominators.
- Every completed run first reproduced its frozen scorer 1.0.0 result.
- Scorer 1.1.0 was then applied symmetrically to every arm.
- The original [pilot](../pilot/REPORT.md) and
  [guided](../guided/REPORT.md) artifacts remain unchanged.
- This deterministic scorer does not use an LLM judge or infer paraphrases.

Input fingerprint: `c3d4fd3526b32cb050ef151227955ce4bfd77168697ea04b0b34e3e15597ef0b`

## Verdict

Scorer 1.0.0 materially under-counted answers that included a canonical value
inside ordinary explanatory formatting. Scorer 1.1.0 raises measured absolute
accuracy in both headline arms, but it does not create evidence that simply
making Sanjaya available improved performance.

The headline native arm changes from 6/36 to 25/36 strict successes.
The Sanjaya-available arm changes from 6/36 to 24/36.
None of the 35 completed availability sessions called Sanjaya,
so this remains a zero-uptake comparison rather than a test of active Sanjaya use.

Across 33 completed headline pairs, scorer 1.1.0 finds
1 availability-favoring pairs,
1 native-favoring pairs, and
31 ties. The paired mean claim-F1 delta
(available minus native) is
+0.018.

## Headline availability comparison

| Measure | Native 1.0 | Native 1.1 | Available 1.0 | Available 1.1 |
|---|---:|---:|---:|---:|
| Strict success / planned | 6/36 | 25/36 | 6/36 | 24/36 |
| Mean claim F1 / completed | 0.255 | 0.904 | 0.326 | 0.910 |
| Mean citation validity / completed | 0.780 | 0.780 | 0.800 | 0.800 |

Planned/completed/failure accounting is 36/34/2
for native and 36/35/1
for available.

### Task-level strict results

| Task | Native 1.0 | Native 1.1 | Available 1.0 | Available 1.1 |
|---|---:|---:|---:|---:|
| SJ-EVAL-0001 | 3/3 | 3/3 | 3/3 | 3/3 |
| SJ-EVAL-0002 | 3/3 | 3/3 | 3/3 | 3/3 |
| SJ-EVAL-0003 | 0/3 | 3/3 | 0/3 | 3/3 |
| SJ-EVAL-0004 | 0/3 | 1/3 | 0/3 | 1/3 |
| SJ-EVAL-0005 | 0/3 | 3/3 | 0/3 | 2/3 |
| SJ-EVAL-0006 | 0/3 | 3/3 | 0/3 | 3/3 |
| SJ-EVAL-0007 | 0/3 | 0/3 | 0/3 | 0/3 |
| SJ-EVAL-0008 | 0/3 | 0/3 | 0/3 | 0/3 |
| SJ-EVAL-0009 | 0/3 | 3/3 | 0/3 | 3/3 |
| SJ-EVAL-0010 | 0/3 | 0/3 | 0/3 | 0/3 |
| SJ-EVAL-0011 | 0/3 | 3/3 | 0/3 | 3/3 |
| SJ-EVAL-0012 | 0/3 | 3/3 | 0/3 | 3/3 |

## Guided diagnostic

The separate guided diagnostic successfully caused all
18 treatment sessions to use Sanjaya.
Its matching native records change from 6/18 to
15/18 strict successes, while guided records change from
3/18 to 14/18.

Across 17 completed guided pairs, scorer 1.1.0 finds
0 guided-favoring pairs,
1 native-favoring pairs, and
16 ties. The paired mean claim-F1
delta (guided minus native) is
-0.029.

| Measure | Native 1.0 | Native 1.1 | Guided 1.0 | Guided 1.1 |
|---|---:|---:|---:|---:|
| Strict success / planned | 6/18 | 15/18 | 3/18 | 14/18 |
| Mean claim F1 / completed | 0.480 | 0.956 | 0.384 | 0.917 |
| Mean citation validity / completed | 0.876 | 0.876 | 0.927 | 0.927 |

### Task-level strict results

| Task | Native 1.0 | Native 1.1 | Guided 1.0 | Guided 1.1 |
|---|---:|---:|---:|---:|
| SJ-EVAL-0001 | 3/3 | 3/3 | 3/3 | 3/3 |
| SJ-EVAL-0002 | 3/3 | 3/3 | 0/3 | 3/3 |
| SJ-EVAL-0003 | 0/3 | 3/3 | 0/3 | 3/3 |
| SJ-EVAL-0005 | 0/3 | 3/3 | 0/3 | 2/3 |
| SJ-EVAL-0008 | 0/3 | 0/3 | 0/3 | 0/3 |
| SJ-EVAL-0009 | 0/3 | 3/3 | 0/3 | 3/3 |

## Interpretation boundary

This repair improves the measurement contract, not the product. It supports
three narrow conclusions:

1. scorer 1.0.0 was too brittle for explanatory structured answers;
2. the corrected headline scores remain nearly symmetric and still contain
   zero Sanjaya adoption; and
3. the guided diagnostic shows adoption, but does not establish a broad
   accuracy or efficiency advantage.

The next product experiment should improve capability-aware orchestration
before spending money on another model run.
