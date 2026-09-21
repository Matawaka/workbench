import { ADAPTER_INVARIANTS, validateQuestions } from "./contracts.js";
import { sha256Json } from "./canonical-json.js";
import { validateProviderResponse } from "./validate-response.js";

export class SystemOneJudgmentAdapter {
  constructor({ provider, clock = () => new Date() }) {
    if (!provider || typeof provider.evaluate !== "function") {
      throw new TypeError("provider.evaluate(request, options) is required.");
    }
    this.provider = provider;
    this.clock = clock;
  }

  async evaluate({ state, questions, correlationId = undefined, signal = undefined }) {
    validateQuestions(questions);

    const request = {
      state,
      questions,
    };
    const requestDigest = sha256Json(request);
    const startedAt = this.clock().toISOString();

    const providerResponse = await this.provider.evaluate(request, { signal });
    validateProviderResponse(providerResponse, questions);

    const responseDigest = sha256Json(providerResponse);
    const observedAt = this.clock().toISOString();

    const judgments = Object.fromEntries(
      Object.entries(questions).map(([id, question]) => {
        const answer = providerResponse.answers[id];
        return [
          id,
          {
            primitive: question.type,
            instructions: question.instructions,
            answer,
          },
        ];
      }),
    );

    return Object.freeze({
      schema: "matawaka.judgment-evidence/v0.1",
      evidenceKind: "PROBABILISTIC_JUDGMENT",
      ...ADAPTER_INVARIANTS,
      correlationId,
      provider: providerResponse.provider ?? this.provider.name ?? "unknown",
      requestedModel: providerResponse.requestedModel ?? this.provider.model ?? null,
      observedModel: providerResponse.model ?? null,
      requestDigest,
      responseDigest,
      startedAt,
      observedAt,
      providerRequestId: providerResponse.requestId ?? null,
      judgments,
      usage: providerResponse.usage ?? null,
      provenance: {
        responseSource: "provider",
        modelAliasPinned: Boolean(this.provider.model && this.provider.model !== "jev-latest"),
      },
    });
  }
}
