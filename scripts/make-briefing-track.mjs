// =============================================================================
//  Generates the Briefing page's audio asset (Phase 1129).
//
//  The Briefing exhibit shows `Media`'s caption / chapter / description tracks
//  and its transcript slot. Those need a real, playable media element to hang
//  off — and this site ships nothing it fetches off-origin, which rules out
//  every stock clip on the internet. So the asset is SYNTHESISED here: one
//  low-amplitude sine tone per chapter at a distinct pitch, with a short gap
//  between, so the chapter marks are audibly where the chapter track says they
//  are. Nothing is claimed about it that is not true, and the page says on its
//  face what it is.
//
//  8 kHz / 8-bit / mono PCM WAV, stdlib-only, no encoder dependency. The file is
//  large in bytes and tiny in the repository: a steady tone is highly periodic,
//  so git's zlib compression takes it to a few kilobytes.
//
//  Run: node scripts/make-briefing-track.mjs
//  The output is COMMITTED; re-run it only when the chapter marks change, and
//  update public/briefing/chapters.vtt in the same change.
// =============================================================================

import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const RATE = 8000; // Hz
const OUT = join(
  dirname(fileURLToPath(import.meta.url)),
  '..',
  'public',
  'briefing',
  'briefing.wav',
);

/** One chapter: a pitch and a duration in seconds. Must match chapters.vtt. */
const chapters = [
  { hz: 392.0, seconds: 6 }, // What changed
  { hz: 440.0, seconds: 6 }, // Where it lands
  { hz: 523.25, seconds: 6 }, // What to check
  { hz: 349.23, seconds: 6 }, // Close
];

const samples = [];
let t = 0;
for (const { hz, seconds } of chapters) {
  const n = Math.round(seconds * RATE);
  for (let i = 0; i < n; i++) {
    // A short silent gap at the head of each chapter makes the boundary audible
    // without a click, and an envelope over the last 0.4 s avoids the pop a hard
    // cut leaves on a square edge.
    const gap = i < RATE * 0.35 ? 0 : 1;
    const tail = i > n - RATE * 0.4 ? (n - i) / (RATE * 0.4) : 1;
    const v = Math.sin((2 * Math.PI * hz * t) / RATE) * 0.22 * gap * tail;
    samples.push(Math.max(0, Math.min(255, Math.round(128 + v * 127))));
    t++;
  }
}

const data = Buffer.from(samples);
const header = Buffer.alloc(44);
header.write('RIFF', 0);
header.writeUInt32LE(36 + data.length, 4);
header.write('WAVE', 8);
header.write('fmt ', 12);
header.writeUInt32LE(16, 16); // PCM chunk size
header.writeUInt16LE(1, 20); // format = PCM
header.writeUInt16LE(1, 22); // channels
header.writeUInt32LE(RATE, 24);
header.writeUInt32LE(RATE, 28); // byte rate (8-bit mono)
header.writeUInt16LE(1, 32); // block align
header.writeUInt16LE(8, 34); // bits per sample
header.write('data', 36);
header.writeUInt32LE(data.length, 40);

mkdirSync(dirname(OUT), { recursive: true });
writeFileSync(OUT, Buffer.concat([header, data]));
console.log(`wrote ${OUT} (${header.length + data.length} bytes, ${t / RATE}s)`);
