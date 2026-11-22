# Eval report: brand-safety ad checker

> **Analyzer: `fake`**. These are results from the deterministic keyword-rule analyzer, NOT from the real AI service.
> They show how the eval harness works and where simple rules fail. They say nothing about the quality of Azure AI Content Understanding.

| | |
|---|---|
| Result | PASS: no must-pass failures |
| Test set | `evals/testset.json` (30 text cases, 18 must-pass) |
| Runs per case | 3 (90 runs in total) |
| Review threshold | confidence below 0.70 goes to human review |
| Generated | 2026-11-22 18:29 UTC, took 0.0 s |

## Summary

| Metric | Value |
|---|---|
| Overall pass rate (all runs) | **73.3%** (66 / 90) |
| Cases that passed in every run | 22 / 30 |
| Must-pass cases that failed | 0 / 18 |
| Risky-ad runs approved (let through) | 9 |
| Clean-ad runs wrongly rejected | 3 |
| Clean-ad runs sent to human review | 6 |
| Analyzer errors | 0 |

## Per category

Counted per run (cases x runs). **Caught** = the ad was not approved and the expected category was flagged. The last two columns count clean ads that were flagged with this category.

| Category | Risky runs | Caught | Missed | Clean ads wrongly rejected | Clean ads sent to review |
|---|---:|---:|---:|---:|---:|
| violence | 9 | 6 | 3 | 0 | 3 |
| adult | 9 | 9 | 0 | 0 | 0 |
| hate | 9 | 6 | 3 | 0 | 0 |
| weapons | 9 | 9 | 0 | 3 | 0 |
| alcohol | 9 | 9 | 0 | 0 | 3 |
| gambling | 12 | 9 | 3 | 0 | 0 |
| misleading_claims | 12 | 9 | 3 | 0 | 0 |

## Must-pass failures

None. Every must-pass case passed in every run.

## All failed cases

| Case | Must pass | Expected | Got (per run) | Passed runs | Ad text | Why it is tricky |
|---|---|---|---|---:|---|---|
| clean-05 | no | approved | needs_review (violence) x3 | 0/3 | Shoot sharp photos at night with our new phone camera. | 'Shoot' is about photography. |
| clean-06 | no | approved | needs_review (alcohol) x3 | 0/3 | Happy hour at our cafe: two coffees for the price of one. | 'Happy hour' with coffee, not alcohol. |
| clean-07 | no | approved | rejected (weapons) x3 | 0/3 | No guns, no violence: a family-friendly laser tag arena for birthdays. | Negation: the ad says there are no guns. |
| violence-03 | no | rejected (violence) | approved x3 | 0/3 | Teach your ex a lesson they will never forget. We show you how to m... | Implied harm with no obvious violent keyword. |
| hate-03 | no | rejected (hate) | approved x3 | 0/3 | Our neighbourhood is being overrun. Keep them out before it is too ... | Coded exclusionary language, no explicit keyword. |
| alcohol-03 | no | needs_review (alcohol) | rejected (alcohol) x3 | 0/3 | Try our new alcohol-free beer: all the taste, 0.0% alcohol. | Brand extension of an alcohol product. Rules differ by market. |
| gambling-03 | no | rejected (gambling) | needs_review (misleading_claims) x3 | 0/3 | Spin to win tonight at our c-a-s-i-n-o. Jackp0t guaranteed. | Disguised spelling to dodge filters. |
| misleading-03 | no | rejected (misleading_claims) | approved x3 | 0/3 | Turn 100 euros into 10,000 in one week. Thousands have already quit... | Unrealistic income promise with no keyword. |

## Run-to-run variance

| Metric | Value |
|---|---|
| Cases whose decision changed between runs | 0 / 30 |
| Mean decision agreement (share of runs with the most common decision) | 100.0% |
| Mean confidence std dev per case | 0.000 |
| Max confidence std dev per case | 0.000 |

The fake analyzer is deterministic, so zero variance is expected here. With a real AI model, expect some cases to change between runs.

## How a run is scored

- A run **passes** when the decision equals `expectedDecision` and, for risky cases, `expectedCategory` is one of the returned categories.
- A case with `mustPass: true` must pass in **every** run. If one fails, the eval runner exits with code 1 and CI fails.
- An analyzer error counts as a failed run.
