export class FixtureProvider {
  constructor({ fixture, name = "fixture.system-one", model = "fixture-v0" }) {
    if (!fixture || typeof fixture !== "object") throw new TypeError("fixture is required.");
    this.name = name;
    this.model = model;
    this.fixture = fixture;
  }

  async evaluate() {
    return structuredClone({
      provider: this.name,
      requestedModel: this.model,
      model: this.model,
      ...this.fixture,
    });
  }
}
