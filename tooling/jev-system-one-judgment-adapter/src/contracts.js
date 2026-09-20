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

export function noul(instructions, criteria = undefined) {
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

function assertText(value, label) {
  if (typeof value !== "string" || value.trim() === "") {
    throw new TypeError(`${label} must be a non-empty string.`);
  }
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

function assertNonNormativeInstruction(instructions, label) {
  for (const pattern of PROHIBITED_INSTRUCTION_PATTERNS) {
    if (pattern.test(instructions)) {
      throw new Error(
        `${label} asks Jev to make a normative authorization/execution decision. Ask for an atomic fact, classification, risk, ambiguity, or scope judgment instead.`,
      );
    }
  }
}

export function validateQuestions(questions) {
  if (!questions || typeof questions !== "object" || Array.isArray(questions)) {
    throw new TypeError("questions must be a non-empty object keyed by stable question IDs.");
  }

  const entries = Object.entries(questions);
  if (entries.length === 0) throw new Error("At least one question is required.");

  for (const [id, question] of entries) {
    assertText(id, "question id");
    assertNonNormativeName(id, "question id");
    if (!question || typeof question !== "object") throw new TypeError(`Question '${id}' must be an object.`);
    assertText(question.instructions, `Question '${id}' instructions`);
    assertNonNormativeInstruction(question.instructions, `Question '${id}'`);

    if (question.type === "choice") {
      if (!question.criteria || typeof question.criteria !== "object" || Array.isArray(question.criteria)) {
        throw new TypeError(`Choice '${id}' criteria must be an object.`);
      }
      const options = Object.keys(question.criteria);
      if (options.length < 1 || options.length > 255) {
        throw new RangeError(`Choice '${id}' must contain 1..255 options.`);
      }
      for (const option of options) assertNonNormativeName(option, `Choice '${id}' option`);
    } else if (question.type === "score") {
      if (!Array.isArray(question.criteria) || question.criteria.length < 2 || question.criteria.length > 10) {
        throw new RangeError(`Score '${id}' must contain 2..10 ordered criteria labels.`);
      }
      question.criteria.forEach((level, index) => assertText(level, `Score '${id}' criterion ${index}`));
    } else if (question.type === "noul") {
      // Nothing else required. Noul returns only p(statement=true).
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
