# Qualification oracle discipline

Synthetic qualification fixtures are not automatically ground truth.

A question becomes an asserted expectation only when the fixture establishes a defensible answer or threshold independently of Jev's output. Otherwise the judgment is still collected as evidence but is marked **observation-only**.

This prevents three common errors:

1. tuning prompts or thresholds until the model agrees with the harness;
2. counting uncertain fixture semantics as model failures;
3. reporting an expectation-pass ratio as if it were model accuracy.

The distinction is represented directly in each qualification case:

```json
{
  "oracle": {
    "assertedQuestionIds": ["scopeExpansion", "causesExternalMutation"],
    "observationOnlyQuestionIds": ["operationalSpecificity", "ambiguity"],
    "assertedQuestions": 2,
    "observationOnlyQuestions": 2,
    "totalQuestions": 4,
    "coverage": 0.5
  }
}
```

Observation-only signals can later become asserted only after an independent labeling protocol supplies a defensible oracle.

This does not weaken fail-closed authority handling. Jev remains non-normative evidence; absence of a model oracle never creates authority.
