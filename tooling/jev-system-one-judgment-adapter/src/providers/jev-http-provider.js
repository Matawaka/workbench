export class JevHttpProvider {
  constructor({
    apiKey = process.env.TYPESAFE_API_KEY,
    endpoint = "https://api.typesafe.ai/v1/systemone",
    model = "jev-latest",
    fetchImpl = globalThis.fetch,
    timeoutMs = 10_000,
  } = {}) {
    if (!apiKey) throw new Error("TYPESAFE_API_KEY is required for JevHttpProvider.");
    if (typeof fetchImpl !== "function") throw new TypeError("fetch implementation is required.");
    this.name = "typesafe.jev";
    this.apiKey = apiKey;
    this.endpoint = endpoint;
    this.model = model;
    this.fetchImpl = fetchImpl;
    this.timeoutMs = timeoutMs;
  }

  async evaluate(request, { signal } = {}) {
    const timeout = AbortSignal.timeout(this.timeoutMs);
    const combinedSignal = signal ? AbortSignal.any([signal, timeout]) : timeout;

    const response = await this.fetchImpl(this.endpoint, {
      method: "POST",
      headers: {
        authorization: `Bearer ${this.apiKey}`,
        "content-type": "application/json",
      },
      body: JSON.stringify({
        model: this.model,
        state: request.state,
        questions: request.questions,
      }),
      signal: combinedSignal,
    });

    const bodyText = await response.text();
    let body;
    try {
      body = bodyText ? JSON.parse(bodyText) : {};
    } catch {
      throw new Error(`TypeSafe returned non-JSON HTTP ${response.status}.`);
    }

    if (!response.ok) {
      const message = body?.error?.message ?? body?.message ?? `TypeSafe HTTP ${response.status}`;
      throw new Error(message);
    }

    return {
      ...body,
      provider: this.name,
      requestedModel: this.model,
    };
  }
}
