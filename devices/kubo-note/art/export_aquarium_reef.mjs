import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { deflateSync } from 'node:zlib';

const [edge, output] = process.argv.slice(2);
if (!edge || !output) throw new Error('Usage: node export_aquarium_reef.mjs <edge.exe> <output-dir>');

const sourceDir = dirname(fileURLToPath(import.meta.url));
const art = readFileSync(join(sourceDir, 'aquarium_reef.js'), 'utf8');
const out = resolve(output);
mkdirSync(out, { recursive: true });
const root = realpathSync(out);
const scratch = mkdtempSync(join(root, 'edge-atlas-'));
const input = join(scratch, 'atlas.html');

const exportFrames = String.raw`
const scenes = [
  ['Opening', 12, (i, n) => aquarium('open', i / (n - 1))],
  ['Ready', 16, (i) => aquarium('rest', 0, cruisingPose(i * 563))],
  ['Feeding', 20, (i, n) => aquarium('feed', i / (n - 1))],
  ['Current', 30, (i, n) => aquarium('flow', i / (n - 1))],
  ['Hiding', 14, (i, n) => aquarium('tuck', i / (n - 1))],
  ['Peek', 1, () => aquarium('peek', 1, PEEK)],
  ['ReefReady', 16, (i) => aquarium('rest', 0, cruisingPose(i * 563), true)],
  ['ReefFeeding', 20, (i, n) => aquarium('feed', i / (n - 1), CRUISE, true)],
  ['ReefCurrent', 30, (i, n) => aquarium('flow', i / (n - 1), CRUISE, true)],
  ['ReefHiding', 14, (i, n) => aquarium('tuck', i / (n - 1), CRUISE, true)],
  ['ReefPeek', 1, () => aquarium('peek', 1, PEEK, true)],
  ['TravelToCoral', 14, (i, n) => travel(i / (n - 1), false)],
  ['TravelToAnemone', 14, (i, n) => travel(i / (n - 1), true)],
];

const picks = [
  ['TravelToCoral:0', 'TravelToCoral:3', 'TravelToCoral:6', 'TravelToCoral:10', 'TravelToCoral:13'],
  ['ReefReady:0', 'ReefFeeding:10', 'ReefCurrent:10', 'ReefHiding:8', 'ReefPeek:0'],
].flat();
const preview = document.createElement('canvas');
preview.width = SIZE * 5;
preview.height = SIZE * 2;
const previewContext = preview.getContext('2d');
const atlas = [];
const ranges = {};
const paintFish = drawFish;
let scales = [];
drawFish = pose => {
  scales.push(pose.scale);
  paintFish(pose);
};

for (const [name, count, render] of scenes) {
  scales = [];
  ranges[name] = [atlas.length, count];
  for (let i = 0; i < count; i++) {
    ctx.save();
    ctx.fillStyle = white;
    ctx.fillRect(0, 0, SIZE, SIZE);
    render(i, count);
    ctx.restore();

    const image = ctx.getImageData(0, 0, SIZE, SIZE);
    const packed = new Uint8Array(SIZE * SIZE / 8);
    for (let p = 0; p < SIZE * SIZE; p++) {
      const ink = image.data[p * 4] < 142 ? 0 : 255;
      if (ink) packed[p >> 3] |= 0x80 >> (p & 7);
      image.data[p * 4] = image.data[p * 4 + 1] = image.data[p * 4 + 2] = ink;
    }
    atlas.push(btoa(String.fromCharCode(...packed)));
    const position = picks.indexOf(name + ':' + i);
    if (position >= 0) previewContext.putImageData(image, (position % 5) * SIZE, Math.floor(position / 5) * SIZE);
  }
  if (scales.length !== count) throw new Error(name + ': expected one fish per frame');
  for (let i = 0; i < count; ++i) {
    const scale = scales[i];
    const t = count > 1 ? i / (count - 1) : 0;
    const orbit = (name === 'Current' || name === 'ReefCurrent') && t > .57 && i < count - 1;
    if (!Number.isFinite(scale) || (orbit ? (scale < .78 || scale > .93) : scale !== CRUISE.scale))
      throw new Error(name + ' frame ' + i + ': unexpected body scale ' + scale);
    if (i && Math.abs(scale - scales[i-1]) > .045)
      throw new Error(name + ' frame ' + i + ': abrupt change of scale');
  }
  if ((name === 'Current' || name === 'ReefCurrent') &&
      !(Math.min(...scales) < CRUISE.scale && Math.max(...scales) > CRUISE.scale))
    throw new Error(name + ': expected both the far and near parts of the orbit');
}
document.getElementById('result').textContent = JSON.stringify({
  ranges, atlas, preview: preview.toDataURL('image/png').split(',')[1]
});
`;

let rendered;
try {
  writeFileSync(input,
    '<!doctype html><meta charset=utf-8><canvas id=screen width=200 height=200></canvas><pre id=result></pre>' +
    '<script>try { const SIZE=200; const ctx=document.getElementById("screen").getContext("2d");\n' +
    art + '\n' + exportFrames +
    '\n} catch (err) { document.getElementById("result").textContent="ERROR: " + err.stack; }</script>', 'utf8');

  const dom = execFileSync(edge, [
    '--headless', '--disable-gpu', '--no-first-run', '--disable-background-networking',
    '--disable-extensions', '--disable-sync', '--disable-component-update', '--no-default-browser-check',
    '--user-data-dir=' + join(scratch, 'profile'), '--dump-dom', pathToFileURL(input).href,
  ], { encoding: 'utf8', maxBuffer: 8 * 1024 * 1024, timeout: 45000 });
  const match = /<pre id="result">([^<]*)<\/pre>/.exec(dom);
  assert(match?.[1], 'Edge did not export the aquarium frames.');
  rendered = JSON.parse(match[1]);
} finally {
  const location = relative(root, realpathSync(scratch));
  assert(/^edge-atlas-[A-Za-z0-9]+$/.test(location), 'Unexpected temporary renderer path.');
  rmSync(scratch, { recursive: true, force: true, maxRetries: 4, retryDelay: 150 });
}

const { ranges, atlas, preview } = rendered;
const [anemoneReady] = ranges.Ready;
const [reefReady] = ranges.ReefReady;
const [toCoral, coralCount] = ranges.TravelToCoral;
const [toAnemone, anemoneCount] = ranges.TravelToAnemone;
assert(atlas[toCoral + coralCount - 1] === atlas[reefReady], 'Arrival must match the reef ready frame.');
assert(atlas[toAnemone] === atlas[reefReady], 'Departure must match the reef ready frame.');
assert(atlas[toCoral] === atlas[anemoneReady], 'Departure must match the anemone ready frame.');
assert(atlas[toAnemone + anemoneCount - 1] === atlas[anemoneReady], 'Return must match the anemone ready frame.');
assert(atlas[reefReady] !== atlas[anemoneReady], 'The two resting reefs must be different.');

const sourceHash = createHash('sha256').update(art).digest('hex');

function writeAtlas(filename, symbol, firstFrame, endFrame, frameBase) {
  let offset = 0;
  const table = [];
  const streams = [];
  for (const pixels of atlas.slice(firstFrame, endFrame)) {
    const raw = Buffer.from(pixels, 'base64');
    assert.equal(raw.length, 5000);
    const packed = deflateSync(raw, { level: 9 });
    table.push('    {' + offset + ', ' + packed.length + '},');
    streams.push(packed);
    offset += packed.length;
  }
  const bytes = Buffer.concat(streams);
  const hexLines = [];
  for (let i = 0; i < bytes.length; i += 16) {
    hexLines.push('    ' + Array.from(bytes.subarray(i, i + 16),
      byte => '0x' + byte.toString(16).padStart(2, '0')).join(', ') + ',');
  }
  const frameRanges = Object.entries(ranges).filter(([, [first]]) => first >= firstFrame && first < endFrame);
  const header = [
    '// Generated by art/export_aquarium_reef.mjs from the approved fish drawing and Torch coral scene.',
    '// Each independent zlib stream decodes into 5,000 packed panel bytes.',
    '// Artwork SHA-256: ' + sourceHash,
    ...frameRanges.map(([name, [first, count]]) =>
      'constexpr FrameRange k' + name + 'Frames{' + frameBase + (first - firstFrame) + ', ' + count + '};'),
    'constexpr PackedFrame ' + symbol + 'Frames[] = {', ...table, '};',
    'constexpr uint8_t ' + symbol + 'Pixels[] = {', ...hexLines, '};', '',
  ].join('\n');
  writeFileSync(join(out, filename), header, 'utf8');
  console.log(filename + ': ' + table.length + ' frames, ' + bytes.length + ' compressed bytes.');
}

writeAtlas('aquarium_frames.inc', 'kAquarium', 0, reefReady, '');
writeAtlas('aquarium_reef_frames.inc', 'kReef', reefReady, atlas.length, 'kAnemoneFrameCount + ');
writeFileSync(join(out, 'aquarium-reef-preview.png'), Buffer.from(preview, 'base64'));
console.log('Verified departure and arrival frames at both sites.');
