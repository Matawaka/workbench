function parseBody(text, status) {
  try {
    return text ? JSON.parse(text) : {};
  } catch {
    throw new Error(`TypeSafe returned non-JSON HTTP ${status}.`);
  }
}

export class JevHttpProvider {
  constructor({
    apiKey = process.env.TYPESAFE_API_KEY,
    baseURL = process.env.TYPESAFE_BASE_URL ?? "https://api.typesafe.ai",
    model = process.env.TYPESAFE_DEFAULT_MODEL ?? "jev-latest",
    fetchImpl = globalThis.fetch,
    timeoutMs = 10_000,
  } = {}) {
    if (!apiKey) throw new Error("TYPESAFE_API_KEY is required for JevHttpProvider.");
    if (typeof fetchImpl !== "function") throw new TypeError("fetch implementation is required.");
    this.name = "typesafe.jev";
    this.apiKey = apiKey;
    this.baseURL = baseURL.replace(/\/$/, "");
    this.model = model;
    this.fetchImpl = fetchImpl;
    this.timeoutMs = timeoutMs;
  }

  async #request(path, { method = "GET", body = undefined, signal = undefined } = {}) {
    const timeout = AbortSignal.timeout(this.timeoutMs);
    const combinedSignal = signal ? AbortSignal.any([signal, timeout]) : timeout;
    const response = await this.fetchImpl(`${this.baseURL}${path}`, {
      method,
      headers: {
        authorization: `Bearer ${this.apiKey}`,
        ...(body === undefined ? {} : { "content-type": "application/json" }),
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
      signal: combinedSignal,
    });

    const payload = parseBody(await response.text(), response.status);
    const requestId = response.headers.get("x-typesafe-request-id") ?? undefined;
    if (!response.ok) {
      const message = payload?.error?.message ?? payload?.message ?? `TypeSafe HTTP ${response.status}`;
      const error = new Error(`${response.status} ${message}${requestId ? ` (request ${requestId})` : ""}`);
      error.status = response.status;
      error.requestId = requestId;
      error.body = payload;
      throw error;
    }
    return { payload, requestId };
  }

  async listModels({ signal } = {}) {
    const { payload, requestId } = await this.#request("/v1/models", { signal });
    if (!Array.isArray(payload?.models)) throw new Error("Unexpected TypeSafe model-list response; expected { models: [...] }.");
    return { models: payload.models, requestId };
  }

  async evaluate(request, { signal } = {}) {
    const { payload, requestId } = await this.#request("/v1/systemone", {
      method: "POST",
      body: {
        model: this.model,
        state: request.state,
        questions: request.questions,
      },
      signal,
    });

    return {
      ...payload,
      provider: this.name,
      requestedModel: this.model,
      requestId,
    };
  }
}
