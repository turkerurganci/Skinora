/**
 * Thin OpenAI chat wrapper — the client/usage handling of gpt-review.mjs
 * (lines 41, 93-105) behind one call, so gpt-ask.mjs can use the API as a
 * fallback transport without duplicating it.
 *
 * gpt-review.mjs is deliberately NOT refactored onto this yet: the API path's
 * quota status on this machine is unknown, so a refactor there could not be
 * exercised. That is a separate chore.
 */

/**
 * @returns {Promise<{text: string, usage: string, model: string}>}
 * @throws when the key is missing or the request fails (caller falls through
 *         to the next transport).
 */
export async function askOpenAI({ system, user, model }) {
  if (!process.env.OPENAI_API_KEY) {
    throw new Error("OPENAI_API_KEY tanimli degil");
  }
  const { default: OpenAI } = await import("openai");
  const client = new OpenAI();
  const modelName = model || process.env.REVIEW_MODEL || "o3";

  const response = await client.chat.completions.create({
    model: modelName,
    messages: [
      { role: "system", content: system },
      { role: "user", content: user },
    ],
  });

  const text = response.choices?.[0]?.message?.content;
  if (!text) throw new Error("Bos cevap dondu");

  const u = response.usage;
  const usage = u
    ? `input: ${u.prompt_tokens}, output: ${u.completion_tokens}, total: ${u.total_tokens}`
    : "N/A";

  return { text, usage, model: modelName };
}
