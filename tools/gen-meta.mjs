// Generates the deterministic .meta set for every asset under Assets/ (skips existing .meta
// files and hidden entries so re-runs are idempotent — never duplicates *.meta.meta).
// Run: node tools/gen-meta.mjs
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { guidFor, folderMeta, scriptMeta, sceneMeta, audioMeta, textMeta, textureMeta } from "./guid.mjs";

const ROOT = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const ASSETS = path.join(ROOT, "Assets");

const LOOPING_CLIPS = new Set(["ambience_loop.wav"]);

let created = 0;
let present = 0;

function walk(dir) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return;
  }
  for (const entry of entries) {
    if (entry.name.startsWith(".")) continue;          // hidden — never touched
    if (entry.name.endsWith(".meta")) continue;         // descriptors are not assets
    const full = path.join(dir, entry.name);
    const rel = path.relative(ASSETS, full).split(path.sep).join("/");
    if (entry.isDirectory()) {
      ensure(full + ".meta", folderMeta(guidFor(rel)));
      walk(full);
    } else {
      const ext = path.extname(entry.name).toLowerCase();
      const guid = guidFor(rel);
      let meta;
      if (ext === ".cs") meta = scriptMeta(guid);
      else if (ext === ".unity") meta = sceneMeta(guid);
      else if (ext === ".wav") meta = audioMeta(guid, LOOPING_CLIPS.has(entry.name.toLowerCase()));
      else if (ext === ".png" || ext === ".jpg") meta = textureMeta(guid);
      else meta = textMeta(guid); // .md, .txt, .json, .mjs-style readmes, OFL, etc.
      ensure(full + ".meta", meta);
    }
  }
}

function ensure(metaPath, content) {
  if (fs.existsSync(metaPath)) {
    present += 1;
    return;
  }
  fs.writeFileSync(metaPath, content);
  created += 1;
}

walk(ASSETS);
console.log(`gen-meta: ${created} created, ${present} already present (idempotent).`);
