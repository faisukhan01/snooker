// Shared deterministic GUID derivation for SnookerKit generators.
// guidFor(relPath) → stable 32-hex Unity GUID, identical across runs and machines.
// A fixed project salt keeps these GUIDs unique to this repo but reproducible.
import crypto from "node:crypto";

const SALT = "snookerkit-v1";

export function guidFor(relPath) {
  const h = crypto.createHash("sha1").update(SALT + "::" + relPath).digest("hex");
  return h.slice(0, 32);
}

/** Meta file for a folder (FolderImporter). */
export function folderMeta(guid) {
  return `fileFormatVersion: 2
guid: ${guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
`;
}

/** Meta file for a C# script (MonoImporter). */
export function scriptMeta(guid) {
  return `fileFormatVersion: 2
guid: ${guid}
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
`;
}

/** Meta file for a scene (NativeFormatImporter). */
export function sceneMeta(guid) {
  return `fileFormatVersion: 2
guid: ${guid}
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 260000000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
`;
}

/** Meta file for a WAV audio clip (AudioClipImporter) — short one-shot defaults. */
export function audioMeta(guid, loop = false) {
  return `fileFormatVersion: 2
guid: ${guid}
AudioImporter:
  externalObjects: {}
  serializedVersion: 6
  defaultSettings:
    loadType: 0
    sampleRateSetting: 0
    overrideSampleRate: 0
    compressorFormat: 1
    quality: 1
    conversionMode: 0
  platformSettingOverrides: {}
  forceToMono: 1
  normalize: 1
  preloadAudioData: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData: 
  assetBundleName: 
  assetBundleVariant: 
${loop ? "  loop: 1\n" : ""}`;
}

/** Meta for plain text/md/json/js assets (TextScriptImporter). */
export function textMeta(guid) {
  return `fileFormatVersion: 2
guid: ${guid}
TextScriptImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
`;
}

/** Meta for PNG textures (TextureImporter). */
export function textureMeta(guid) {
  return `fileFormatVersion: 2
guid: ${guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 12
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
  fadeOut: 0
  borderMipMap: 0
  isReadable: 0
  streamingMipmaps: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteMode: 0
  filterMode: 1
  aniso: 1
  textureShape: 1
  maxTextureSize: 512
  textureType: 0
  sRGBTexture: 1
  userData: 
  assetBundleName: 
  assetBundleVariant: 
`;
}
