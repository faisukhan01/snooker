// Synthesizes the 10 SnookerKit WAV cues into Assets/Resources/Audio (deterministic —
// fixed sample rate, seeded noise, no external deps). Restrained/realistic per PDF §19:
// short percussive impacts, a soft two-tone pot chime, a muted foul buzz, a small win
// arpeggio, UI ticks and a seamless low room-ambience loop.
// Run: node tools/gen-audio.mjs
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const OUT = path.join(ROOT, "Assets", "Resources", "Audio");
const SR = 22050;

/** mulberry32 — tiny deterministic PRNG so output is byte-stable across runs. */
function mulberry32(seed) {
  let a = seed >>> 0;
  return function () {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function seconds(n) { return Math.round(n * SR); }

/** Envelope helper: exponential decay with attack. */
function env(t, dur, attack = 0.002, decayPow = 4) {
  if (t < attack) return t / attack;
  return Math.pow(1 - (t - attack) / (dur - attack), decayPow);
}

function render(dur, fn) {
  const n = seconds(dur);
  const s = new Float64Array(n);
  for (let i = 0; i < n; i++) s[i] = fn(i / SR, i, n);
  return s;
}

/* -------------------------------------------------------------- clip recipes */

const clips = {};

// Cue strike: felt-tip thump — filtered noise burst + 180 Hz body, ~90 ms.
clips.cue_strike = render(0.09, (t, i) => {
  const r = mulberry32(101);
  void r;
  const noise = (Math.sin(i * 12.9898) * 43758.5453 % 1) * 0.6 + (Math.sin(i * 78.233) * 12543.21 % 1) * 0.4;
  const body = Math.sin(2 * Math.PI * 180 * t) * 0.7 + Math.sin(2 * Math.PI * 320 * t) * 0.25;
  return (noise * 0.45 + body) * env(t, 0.09, 0.001, 5) * 0.8;
});

// Ball-ball click: hard ivory click — 1.6 kHz ping + wideband snap, ~45 ms.
clips.ball_ball = render(0.045, (t, i) => {
  const ping = Math.sin(2 * Math.PI * 1600 * t) * 0.6 + Math.sin(2 * Math.PI * 2400 * t) * 0.3;
  const snap = ((Math.sin(i * 91.7) * 33333.3 % 1)) * 0.5;
  return (ping + snap) * env(t, 0.045, 0.0005, 6) * 0.85;
});

// Cushion: soft rubber thud — 140 Hz + dampened noise, ~80 ms.
clips.cushion = render(0.08, (t, i) => {
  const thud = Math.sin(2 * Math.PI * 140 * t) * 0.8 + Math.sin(2 * Math.PI * 90 * t) * 0.35;
  const noise = ((Math.sin(i * 45.123) * 22222.2 % 1)) * 0.25;
  return (thud + noise) * env(t, 0.08, 0.0015, 4) * 0.75;
});

// Pocket drop: ball drops into the net — descending knock + short rattle, ~300 ms.
clips.pocket_drop = render(0.3, (t, i, n) => {
  const knock = Math.sin(2 * Math.PI * (220 - 120 * t / 0.3) * t) * Math.exp(-t * 18) * 0.8;
  const rattlePhase = Math.max(0, t - 0.08);
  const rattle = rattlePhase > 0
    ? Math.sin(2 * Math.PI * 900 * rattlePhase) * Math.exp(-rattlePhase * 14) * (0.5 + 0.5 * Math.sin(i * 31.7)) * 0.4
    : 0;
  return (knock + rattle) * 0.9;
});

// Pot chime: restrained two-tone confirmation (E5→B5), ~450 ms.
clips.pot_chime = render(0.45, (t) => {
  const a = Math.sin(2 * Math.PI * 659.26 * t) * Math.exp(-t * 7) * 0.5;
  const t2 = Math.max(0, t - 0.12);
  const b = t2 > 0 ? Math.sin(2 * Math.PI * 987.77 * t2) * Math.exp(-t2 * 6) * 0.45 : 0;
  return (a + b) * 0.85;
});

// Foul: muted double buzz (mined from a referee signal, not arcade), ~350 ms.
clips.foul = render(0.35, (t) => {
  const buzz = Math.sign(Math.sin(2 * Math.PI * 196 * t)) * 0.18 + Math.sin(2 * Math.PI * 196 * t) * 0.25;
  const gate = (Math.sin(2 * Math.PI * 6 * t) > 0.2 ? 1 : 0.12);
  return buzz * gate * env(t, 0.35, 0.004, 1.2) * 0.8;
});

// Frame win: short warm arpeggio A4→C#5→E5, ~700 ms.
clips.frame_win = render(0.7, (t) => {
  const notes = [[440, 0.0], [554.37, 0.14], [659.26, 0.28]];
  let v = 0;
  for (const [f, start] of notes) {
    const lt = t - start;
    if (lt >= 0) v += Math.sin(2 * Math.PI * f * lt) * Math.exp(-lt * 5) * 0.32;
  }
  return v * 0.95;
});

// UI click: 4 ms glass tick, ~30 ms.
clips.ui_click = render(0.03, (t, i) => {
  const tick = Math.sin(2 * Math.PI * 2400 * t) * Math.exp(-t * 220) * 0.7
    + ((Math.sin(i * 77.7) * 15555.5 % 1)) * Math.exp(-t * 260) * 0.25;
  return tick;
});

// UI back: slightly lower, softer tick, ~35 ms.
clips.ui_back = render(0.035, (t, i) => {
  const tick = Math.sin(2 * Math.PI * 1500 * t) * Math.exp(-t * 180) * 0.6
    + ((Math.sin(i * 55.1) * 12222.2 % 1)) * Math.exp(-t * 220) * 0.2;
  return tick;
});

// Ambience loop: 4 s seamless low room tone — two detuned sines + slow filtered noise,
// crossfaded head-to-tail for a click-free loop.
{
  const dur = 4.0;
  const n = seconds(dur);
  const raw = new Float64Array(n);
  const rnd = mulberry32(77);
  let lp = 0;
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    const hum = Math.sin(2 * Math.PI * 55 * t) * 0.10 + Math.sin(2 * Math.PI * 55.35 * t) * 0.08
      + Math.sin(2 * Math.PI * 110 * t) * 0.04;
    const airN = rnd() * 2 - 1;
    lp += 0.02 * (airN - lp);
    raw[i] = hum + lp * 0.22;
  }
  const fade = seconds(0.25);
  for (let i = 0; i < fade; i++) {
    const k = i / fade;
    raw[i] = raw[i] * k + raw[n - fade + i] * (1 - k);
  }
  clips.ambience_loop = raw.subarray(0, n - fade);
}

/* -------------------------------------------------------------- WAV writer */

function writeWav(name, samples) {
  const dataLen = samples.length * 2;
  const buf = Buffer.alloc(44 + dataLen);
  buf.write("RIFF", 0);
  buf.writeUInt32LE(36 + dataLen, 4);
  buf.write("WAVE", 8);
  buf.write("fmt ", 12);
  buf.writeUInt32LE(16, 16);
  buf.writeUInt16LE(1, 20);          // PCM
  buf.writeUInt16LE(1, 22);          // mono
  buf.writeUInt32LE(SR, 24);
  buf.writeUInt32LE(SR * 2, 28);     // byte rate
  buf.writeUInt16LE(2, 32);          // block align
  buf.writeUInt16LE(16, 34);         // bits
  buf.write("data", 36);
  buf.writeUInt32LE(dataLen, 40);
  for (let i = 0; i < samples.length; i++) {
    let v = Math.max(-1, Math.min(1, samples[i]));
    buf.writeInt16LE(Math.round(v * 32767), 44 + i * 2);
  }
  fs.writeFileSync(path.join(OUT, name + ".wav"), buf);
  return buf.length;
}

fs.mkdirSync(OUT, { recursive: true });
let total = 0;
for (const [name, samples] of Object.entries(clips)) {
  const bytes = writeWav(name, samples);
  total += bytes;
  console.log(`wav  ${name}.wav  ${bytes} B  ${samples.length} samples @ ${SR} Hz`);
}
console.log(`gen-audio: 10 clips, ${total} bytes total (deterministic).`);
