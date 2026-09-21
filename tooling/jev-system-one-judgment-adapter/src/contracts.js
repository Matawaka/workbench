const PROHIBITED_NORMATIVE_NAMES = new Set([
  "allow",
  "deny",
  "permit",
  "permitted",
  "authorize",
  "authorized",
  "authorization",
  "approve",
  "approved",
  "approval",
  "execute",
  "execution",
]);

const PROHIBITED_INSTRUCTION_PATTERNS = [
  /\b(is|should|may|can)\b.{0,30}\b(authori[sz]ed|permitted|allowed|approved)\b/i,
  /\b(allow|deny|permit|authorize|approve|execute)\b.{0,20}\b(action|request|operation|tool|effect)\b/i,
  /\b(issue|grant)\b.{0,20}\b(authority|permission|permit|approval)\b/i,
];

export function noul(instructions = null, criteria = undefined) {
  return criteria === undefined
    ? { type: "noul", instructions }
    : { type: "noul", instructions, criteria };
}

export function choice(instructions, criteria) {
  return { type: "choice", instructions, criteria };
}

export function score(instructions, criteria) {
  return { type: "score", instructions, criteria };
}

function assertQuestionId(value, label) {
  if (typeof value !== "string" || value.trim() === "") {
    throw new TypeError(`${label} must be a non-empty string.`);
  }
}

function assertJsonValue(value, label, { allowTopLevelScalar = false } = {}) {
  if (value === null) return;
  if (typeof value === "string") return;
  if (Array.isArray(value)) {
    value.forEach((item, index) => assertNestedJsonValue(item, `${label}[${index}]`));
    return;
  }
  if (value && typeof value === "object") {
    for (const [key, item] of Object.entries(value)) {
      assertNestedJsonValue(item, `${label}.${key}`);
    }
    return;
  }
  if (allowTopLevelScalar && (typeof value === "number" || typeof value === "boolean")) return;
  throw new TypeError(`${label} must be text, a JSON object/array, or null.`);
}

function assertNestedJsonValue(value, label) {
  if (value === null || ["string", "number", "boolean"].includes(typeof value)) return;
  if (Array.isArray(value)) {
    value.forEach((item, index) => assertNestedJsonValue(item, `${label}[${index}]`));
    return;
  }
  if (value && typeof value === "object") {
    for (const [key, item] of Object.entries(value)) assertNestedJsonValue(item, `${label}.${key}`);
    return;
  }
  throw new TypeError(`${label} is not JSON-compatible.`);
}

function textLeaves(value, path = "$", out = []) {
  if (typeof value === "string") out.push({ path, text: value });
  else if (Array.isArray(value)) value.forEach((item, index) => textLeaves(item, `${path}[${index}]`, out));
  else if (value && typeof value === "object") {
    for (const [key, item] of Object.entries(value)) textLeaves(item, `${path}.${key}`, out);
  }
  return out;
}

function assertNonNormativeName(name, label) {
  const normalized = name.trim().toLowerCase().replace(/[\s_-]+/g, "");
  for (const prohibited of PROHIBITED_NORMATIVE_NAMES) {
    if (normalized === prohibited) {
      throw new Error(
        `${label} '${name}' is normative. SystemOneJudgmentAdapter may emit evidence, never authority or execution decisions.`,
      );
    }
  }
}

function assertNonNormativeContent(value, label) {
  for (const { path, text } of textLeaves(value)) {
    for (const pattern of PROHIBITED_INSTRUCTION_PATTERNS) {
      if (pattern.test(text)) {
        throw new Error(
          `${label}${path === "$" ? "" : ` at ${path}`} asks Jev to make a normative authorization/execution decision. Ask for an atomic fact, classification, risk, ambiguity, or scope judgment instead.`,
        );
      }
    }
  }
}

function validateNoulCriteria(criteria, id) {
  if (criteria === undefined || criteria === null) return;
  if (typeof criteria !== "object" || Array.isArray(criteria)) {
    throw new TypeError(`Noul '${id}' criteria must be an object with optional true/false descriptions.`);
  }
  const keys = Object.keys(criteria);
  for (const key of keys) {
    if (key !== "true" && key !== "false") throw new Error(`Noul '${id}' has unsupported criteria key '${key}'.`);
    assertJsonValue(criteria[key], `Noul '${id}' criteria.${key}`);
    assertNonNormativeContent(criteria[key], `Noul '${id}' criteria.${key}`);
  }
}

export function validateQuestions(questions) {
  if (!questions || typeof questions !== "object" || Array.isArray(questions)) {
    throw new TypeError("questions must be a non-empty object keyed by stable question IDs.");
  }

  const entries = Object.entries(questions);
  if (entries.length === 0) throw new Error("At least one question is required.");

  for (const [id, question] of entries) {
    assertQuestionId(id, "question id");
    assertNonNormativeName(id, "question id");
    if (!question || typeof question !== "object" || Array.isArray(question)) {
      throw new TypeError(`Question '${id}' must be an object.`);
    }

    assertJsonValue(question.instructions ?? null, `Question '${id}' instructions`);
    assertNonNormativeContent(question.instructions ?? null, `Question '${id}' instructions`);

    if (question.type === "choice") {
      if (!question.criteria || typeof question.criteria !== "object" || Array.isArray(question.criteria)) {
        throw new TypeError(`Choice '${id}' criteria must be an object.`);
      }
      const options = Object.keys(question.criteria);
      if (options.length < 1 || options.length > 255) {
        throw new RangeError(`Choice '${id}' must contain 1..255 options.`);
      }
      for (const option of options) {
        assertQuestionId(option, `Choice '${id}' option`);
        assertNonNormativeName(option, `Choice '${id}' option`);
        assertJsonValue(question.criteria[option], `Choice '${id}' description '${option}'`);
        assertNonNormativeContent(question.criteria[option], `Choice '${id}' description '${option}'`);
      }
    } else if (question.type === "score") {
      if (!Array.isArray(question.criteria) || question.criteria.length < 2) {
        throw new RangeError(`Score '${id}' must contain at least 2 ordered criteria entries.`);
      }
      question.criteria.forEach((criterion, index) => {
        assertJsonValue(criterion, `Score '${id}' criterion ${index}`);
        assertNonNormativeContent(criterion, `Score '${id}' criterion ${index}`);
      });
    } else if (question.type === "noul") {
      validateNoulCriteria(question.criteria, id);
    } else {
      throw new Error(`Question '${id}' has unsupported type '${question.type}'.`);
    }
  }

  return true;
}

export const ADAPTER_INVARIANTS = Object.freeze({
  normativeEffect: "NONE",
  principle: "PROBABILISTIC_JUDGMENT_IS_NOT_AUTHORIZATION",
  authorityIssuance: "OUT_OF_SCOPE",
  sideEffects: "OUT_OF_SCOPE",
});
