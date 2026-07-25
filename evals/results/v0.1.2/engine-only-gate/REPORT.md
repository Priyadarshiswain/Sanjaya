# Engine-only hypothesis gate

Status: **stop_current_product_hypothesis**

This model-free gate compares one frozen native ripgrep route with one frozen
Sanjaya route for each of 15 structural evidence targets.
It does not measure answer writing, implicit skill activation, or model quality.
Candidate sets are compared without presentation order because parallel
ripgrep traversal can reorder otherwise identical results.

## Decision

| Criterion | Result |
|---|---|
| Every index ready | yes |
| Target recall non-inferior | yes |
| Precision gain >= 0.15 | no |
| Median response bytes <= 75% of native | no |
| Material benefit | no |
| Proceed | no |

## Aggregate comparison

| Measure | Native | Sanjaya |
|---|---:|---:|
| Mean target recall | 1.000 | 1.000 |
| Mean candidate precision | 0.800 | 0.714 |
| Median response bytes | 217 | 1588 |
| Median query duration (ms) | 11.456 | 197.417 |
| Tasks with full recall | 15/15 | 15/15 |

## Index-build amortization

The measured repetitions stabilize query timing only. Index cost is amortized
over each repository's five unique queries, never over repetitions.

| Repository | Native precision | Sanjaya precision | Native median bytes | Sanjaya median bytes | Index build ms | Index bytes | Native five-query ms | Sanjaya query-only ms | Sanjaya including index ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| fastendpoints | 0.517 | 0.943 | 357 | 1477 | 1807.430 | 14018617 | 54.592 | 1116.658 | 2924.088 |
| vitest | 0.950 | 0.385 | 164 | 8875 | 1604.176 | 8544541 | 59.287 | 1003.719 | 2607.895 |
| kiota | 0.933 | 0.813 | 242 | 1588 | 2435.605 | 16957414 | 55.906 | 1004.763 | 3440.368 |

## Task detail

| Task | Native recall | Sanjaya recall | Native precision | Sanjaya precision | Native bytes | Sanjaya bytes | Native ms | Sanjaya ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| EO-FAST-001 | 1.000 | 1.000 | 1.000 | 1.000 | 104 | 1477 | 18.988 | 198.022 |
| EO-FAST-002 | 1.000 | 1.000 | 0.250 | 1.000 | 546 | 1512 | 10.802 | 181.557 |
| EO-FAST-003 | 1.000 | 1.000 | 0.333 | 1.000 | 357 | 1430 | 8.496 | 197.417 |
| EO-FAST-004 | 1.000 | 1.000 | 0.500 | 1.000 | 199 | 1430 | 8.098 | 188.395 |
| EO-FAST-005 | 1.000 | 1.000 | 0.500 | 0.714 | 1159 | 4037 | 8.208 | 351.267 |
| EO-VITEST-001 | 1.000 | 1.000 | 1.000 | 0.059 | 73 | 10263 | 14.549 | 201.902 |
| EO-VITEST-002 | 1.000 | 1.000 | 1.000 | 0.300 | 184 | 8875 | 12.676 | 211.917 |
| EO-VITEST-003 | 1.000 | 1.000 | 1.000 | 0.167 | 164 | 4418 | 12.061 | 193.887 |
| EO-VITEST-004 | 1.000 | 1.000 | 1.000 | 1.000 | 71 | 1392 | 8.545 | 194.869 |
| EO-VITEST-005 | 1.000 | 1.000 | 0.750 | 0.400 | 482 | 9003 | 11.456 | 201.144 |
| EO-KIOTA-001 | 1.000 | 1.000 | 1.000 | 1.000 | 217 | 1550 | 11.209 | 192.957 |
| EO-KIOTA-002 | 1.000 | 1.000 | 1.000 | 1.000 | 1198 | 8062 | 14.166 | 230.128 |
| EO-KIOTA-003 | 1.000 | 1.000 | 1.000 | 1.000 | 149 | 1588 | 6.274 | 190.306 |
| EO-KIOTA-004 | 1.000 | 1.000 | 1.000 | 0.067 | 242 | 13913 | 11.859 | 180.173 |
| EO-KIOTA-005 | 1.000 | 1.000 | 0.667 | 1.000 | 266 | 1383 | 12.398 | 211.199 |

## Interpretation boundary

The result applies only to the pinned repositories, frozen queries, public
package version, current index envelope, and recorded machine. Response bytes
are a token-pressure proxy, not model-token measurements. A proceed result
authorizes designing a separate agent study; it does not establish a public
product-benefit claim. A stop result rejects further routing work for this
specific product hypothesis until the engine or delivery shape changes.
