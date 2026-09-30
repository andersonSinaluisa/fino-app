import { writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const outPath = resolve(__dirname, "../assets/nexo-bgm.wav");

const sampleRate = 44100;
const channels = 2;
const duration = 66;
const totalSamples = sampleRate * duration;
const bpm = 96;
const beat = 60 / bpm;
const twoPi = Math.PI * 2;

const chords = [
  [110.0, 164.81, 220.0, 329.63],
  [130.81, 196.0, 261.63, 392.0],
  [98.0, 146.83, 196.0, 293.66],
  [146.83, 220.0, 293.66, 440.0],
];

function env(t, start, attack, release, length) {
  const local = t - start;
  if (local < 0 || local > length) return 0;
  if (local < attack) return local / attack;
  const tail = Math.max(0, length - local);
  return Math.min(1, tail / release);
}

function tone(freq, t, shape = "sine") {
  const phase = (freq * t) % 1;
  if (shape === "tri") return 4 * Math.abs(phase - 0.5) - 1;
  return Math.sin(twoPi * phase);
}

function softClip(v) {
  return Math.tanh(v * 1.25) * 0.82;
}

function writeWav(samples) {
  const bytesPerSample = 2;
  const dataBytes = samples.length * bytesPerSample;
  const buffer = Buffer.alloc(44 + dataBytes);

  buffer.write("RIFF", 0);
  buffer.writeUInt32LE(36 + dataBytes, 4);
  buffer.write("WAVE", 8);
  buffer.write("fmt ", 12);
  buffer.writeUInt32LE(16, 16);
  buffer.writeUInt16LE(1, 20);
  buffer.writeUInt16LE(channels, 22);
  buffer.writeUInt32LE(sampleRate, 24);
  buffer.writeUInt32LE(sampleRate * channels * bytesPerSample, 28);
  buffer.writeUInt16LE(channels * bytesPerSample, 32);
  buffer.writeUInt16LE(16, 34);
  buffer.write("data", 36);
  buffer.writeUInt32LE(dataBytes, 40);

  let offset = 44;
  for (const sample of samples) {
    const clipped = Math.max(-1, Math.min(1, sample));
    buffer.writeInt16LE(Math.round(clipped * 32767), offset);
    offset += 2;
  }

  writeFileSync(outPath, buffer);
}

const interleaved = new Float32Array(totalSamples * channels);

for (let i = 0; i < totalSamples; i += 1) {
  const t = i / sampleRate;
  const bar = Math.floor(t / (beat * 4));
  const chord = chords[bar % chords.length];
  const beatIndex = Math.floor(t / beat);
  const beatStart = beatIndex * beat;
  const eighthIndex = Math.floor(t / (beat / 2));
  const eighthStart = eighthIndex * (beat / 2);

  let mix = 0;

  for (let n = 0; n < chord.length; n += 1) {
    const amp = 0.075 / (n + 1);
    mix += tone(chord[n], t, n % 2 ? "tri" : "sine") * amp;
    mix += tone(chord[n] * 2.01, t, "sine") * amp * 0.28;
  }

  const pluckFreq = chord[(beatIndex + bar) % chord.length] * 2;
  const pluckEnv = env(t, eighthStart, 0.008, 0.18, 0.28);
  mix += tone(pluckFreq, t, "tri") * pluckEnv * 0.15;

  const kickEnv = env(t, beatStart, 0.004, 0.22, 0.32);
  mix += Math.sin(twoPi * (54 + kickEnv * 36) * t) * kickEnv * 0.26;

  const hatEnv = env(t, eighthStart, 0.002, 0.055, 0.08);
  const metallic = tone(7120, t, "tri") * 0.5 + tone(9460, t, "sine") * 0.5;
  mix += metallic * hatEnv * 0.04;

  const sweep = Math.sin(twoPi * 0.04 * t) * 0.04;
  const left = softClip(mix * (0.92 + sweep));
  const right = softClip(mix * (0.92 - sweep));

  interleaved[i * 2] = left;
  interleaved[i * 2 + 1] = right;
}

writeWav(interleaved);
console.log(`wrote ${outPath}`);
