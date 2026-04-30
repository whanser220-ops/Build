import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const OPENAI_IMAGES_ENDPOINT = "https://api.openai.com/v1/images/generations";
const IMAGE_MODEL = "gpt-image-2";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, "..");
const outputPath = path.join(repoRoot, "public", "generated", "image.png");

function usage() {
  return [
    "Usage:",
    '  npm run generate:image -- "your image prompt"',
    '  npm run generate:image -- --prompt "your image prompt"',
  ].join("\n");
}

function readPrompt(args) {
  const promptFlagIndex = args.findIndex((arg) => arg === "--prompt" || arg === "-p");

  if (promptFlagIndex >= 0) {
    return (args[promptFlagIndex + 1] ?? "").trim();
  }

  return args.join(" ").trim();
}

async function readResponseJson(response) {
  const text = await response.text();

  if (!text) {
    return {};
  }

  try {
    return JSON.parse(text);
  } catch {
    return { raw: text };
  }
}

function formatApiError(status, payload) {
  const detail = payload?.error?.message ?? payload?.message ?? payload?.raw ?? "Unknown error";
  return `OpenAI Images API request failed (${status}): ${detail}`;
}

async function generateImage(prompt, apiKey) {
  const response = await fetch(OPENAI_IMAGES_ENDPOINT, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${apiKey}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      model: IMAGE_MODEL,
      prompt,
      output_format: "png",
    }),
  });

  const payload = await readResponseJson(response);

  if (!response.ok) {
    throw new Error(formatApiError(response.status, payload));
  }

  const imageBase64 = payload?.data?.[0]?.b64_json;

  if (!imageBase64) {
    throw new Error("OpenAI Images API response did not include data[0].b64_json.");
  }

  return Buffer.from(imageBase64, "base64");
}

async function main() {
  const prompt = readPrompt(process.argv.slice(2));

  if (!prompt) {
    throw new Error(`Missing prompt.\n\n${usage()}`);
  }

  const apiKey = process.env.OPENAI_API_KEY;

  if (!apiKey) {
    throw new Error("Missing OPENAI_API_KEY environment variable.");
  }

  const imageBytes = await generateImage(prompt, apiKey);

  await mkdir(path.dirname(outputPath), { recursive: true });
  await writeFile(outputPath, imageBytes);

  console.log(`Generated image saved to ${outputPath}`);
}

main().catch((error) => {
  console.error(error.message);
  process.exitCode = 1;
});
