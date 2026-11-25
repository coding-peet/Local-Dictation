// Fish and anemone drawing derived from the approved Canvas storyboard.
const black = '#000';
const white = '#fff';
const CRUISE = { x: 131, y: 98, scale: .88 };
// Keep the eye in the opening between the foreground tentacles.
const PEEK = { ...CRUISE, x: 72, y: 98, clip: 79, glance: .9 };
const TORCH_PEEK = { ...PEEK, x: 84, clip: 94 };

const clamp = t => Math.min(1, Math.max(0, t));
const mix = (a, b, t) => a + (b - a) * t;
const ease = t => (1 - Math.cos(Math.PI * clamp(t))) / 2;
const pulse = t => Math.sin(Math.PI * clamp(t));
const point = (x, y) => ({ x, y });

function shape(draw, fill = black, stroke = null, width = 1.8) {
  ctx.beginPath();
  draw(ctx);
  if (fill) { ctx.fillStyle = fill; ctx.fill(); }
  if (stroke) { ctx.strokeStyle = stroke; ctx.lineWidth = width; ctx.stroke(); }
}

function oval(x, y, rx, ry, fill, outline = null, width = 1.5) {
  shape(p => p.ellipse(x, y, rx, ry, 0, 0, 2 * Math.PI), fill, outline, width);
}

function fishBody(p, bend) {
  p.moveTo(35, -1);
  p.bezierCurveTo(34, -12, 25, -18, 14, -20);
  p.bezierCurveTo(-3, -24, -18, -15 + bend * .6, -27, -8 + bend);
  p.bezierCurveTo(-30, -4 + bend, -31, 3 + bend, -27, 8 + bend);
  p.bezierCurveTo(-17, 15 + bend * .6, -4, 21, 11, 19);
  p.bezierCurveTo(24, 18, 33, 11, 35, 1);
  p.closePath();
}

function drawFish({ x, y, angle = 0, face = 1, phase = 0, kick = 0, scale = 1, yaw = 1,
                    gulp = 0, glance = 0, clip }) {
  ctx.save();
  if (clip !== undefined) {
    ctx.beginPath();
    ctx.rect(clip, 0, SIZE - clip, SIZE);
    ctx.clip();
  }
  ctx.translate(x, y);
  ctx.rotate(angle);
  ctx.scale(face * scale * 1.06 * yaw, scale);
  const tailFlex = Math.sin(phase) * kick * 8;
  const bodyFlex = Math.sin(phase - .65) * kick * 4;
  const finFlex = Math.sin(phase - 1.1) * kick * 4;

  shape(p => {
    p.moveTo(-26, -5 + bodyFlex); p.bezierCurveTo(-34, -6 + bodyFlex, -39, -13 + tailFlex, -46, -12 + tailFlex);
    p.bezierCurveTo(-50, -9 + tailFlex, -49, 12 + tailFlex, -43, 14 + tailFlex);
    p.bezierCurveTo(-37, 14 + tailFlex, -32, 6 + bodyFlex, -27, 5 + bodyFlex); p.closePath();
  });
  shape(p => {
    p.moveTo(-34, 0); p.quadraticCurveTo(-40, -8 + tailFlex, -44, -8 + tailFlex);
    p.moveTo(-35, 2); p.quadraticCurveTo(-40, 8 + tailFlex, -44, 10 + tailFlex);
  }, null, white, 1.5);
  shape(p => {
    p.moveTo(-22, -11); p.quadraticCurveTo(-18, -23, -9, -24);
    p.quadraticCurveTo(-1, -29, 9, -26); p.quadraticCurveTo(16, -28, 24, -17); p.closePath();
  }, white, black, 2.2);
  shape(p => {
    p.moveTo(-16, 12); p.quadraticCurveTo(-10, 22, -4, 24);
    p.quadraticCurveTo(2, 24, 8, 17); p.closePath();
  }, white, black, 2);
  shape(p => fishBody(p, bodyFlex));

  ctx.save();
  ctx.beginPath();
  fishBody(ctx, bodyFlex);
  ctx.clip();
  shape(p => {
    p.moveTo(16, -25); p.bezierCurveTo(7, -10, 10, 9, 18, 23);
    p.lineTo(25, 23); p.bezierCurveTo(17, 8, 14, -10, 24, -24); p.closePath();
  }, white);
  shape(p => {
    p.moveTo(-6, -26); p.bezierCurveTo(-14, -13, -10, 7, -11, 24);
    p.lineTo(-2, 24); p.bezierCurveTo(-3, 12, 5, 5, 2, -8);
    p.quadraticCurveTo(-1, -16, 2, -26); p.closePath();
  }, white);
  shape(p => {
    p.moveTo(-27, -15); p.quadraticCurveTo(-31, 1, -24, 15);
    p.lineTo(-18, 16); p.quadraticCurveTo(-24, 0, -21, -14); p.closePath();
  }, white);
  ctx.restore();
  shape(p => fishBody(p, bodyFlex), null, black, 1.9);

  shape(p => {
    p.moveTo(9, 4); p.bezierCurveTo(8, 10, 3, 16 + finFlex, -6, 15 + finFlex);
    p.quadraticCurveTo(-3, 9, 6, 4); p.closePath();
  }, white, black, 1.6);
  oval(27, -5, 4.1, 4.6, white);
  oval(28.1 + glance, -4.6, 2.4, 2.7, black);
  if (gulp > .25) oval(33, 3, 2.5, gulp * 2.7, white);
  else shape(p => {
    p.moveTo(31, 4); p.quadraticCurveTo(34, 5, 36, 2.6);
  }, null, white, 1.3);
  ctx.restore();
}

const backTentacles = [
  [25, 79, -3], [33, 67, 5], [43, 77, -5], [51, 59, -4],
  [60, 70, 4], [70, 66, 8], [80, 81, 8], [87, 91, 6]
];
const frontTentacles = [
  [19, 102, -4], [27, 91, -8], [40, 88, 2], [52, 90, 6],
  [65, 89, -1], [76, 98, 1], [86, 116, 5], [94, 125, 4]
];

function tentacles(layer, gust, phase, front) {
  ctx.lineCap = 'round';
  for (const [i, [x, y, curve]] of layer.entries()) {
    const rootX = mix(x, 55, .53);
    const rootY = 146 - (i % 3) * 2;
    const bend = gust * (23 + (i % 4) * 2.5);
    const tip = x + bend + Math.sin(phase + i * .6) * gust * 1.8;
    const tipY = mix(rootY, y, .84) + gust * (6 + (i % 3) * 2);
    const trail = p => {
      p.moveTo(rootX, rootY);
      p.bezierCurveTo(rootX + curve + 4 + bend * .2, rootY - 21,
        tip + curve - bend * .37, tipY + 23, tip, tipY);
    };
    shape(trail, null, black, front ? 5.1 : 4.5);
    shape(trail, null, white, front ? 2.2 : 1.7);
    oval(tip, tipY, front ? 3 : 2.6, front ? 3.2 : 2.8, white, black, 1.5);
  }
}

function floor() {
  shape(p => {
    p.moveTo(0, 162); p.bezierCurveTo(39, 159, 61, 160, 91, 162);
    p.bezierCurveTo(134, 165, 172, 164, 200, 158);
  }, null, black, 1.5);
  shape(p => {
    p.moveTo(17, 159); p.quadraticCurveTo(21, 151, 35, 149);
    p.quadraticCurveTo(46, 140, 61, 143); p.quadraticCurveTo(79, 145, 96, 160);
    p.quadraticCurveTo(55, 164, 17, 159);
  }, white, black, 1.7);
  shape(p => {
    p.moveTo(40, 154); p.quadraticCurveTo(51, 149, 65, 153);
    p.moveTo(23, 171); p.lineTo(34, 171);
    p.moveTo(115, 173); p.lineTo(126, 173);
    p.moveTo(144, 158); p.quadraticCurveTo(149, 151, 157, 155); p.lineTo(161, 159);
  }, null, black, 1.2);
  for (const [x, y] of [[26,167],[36,174],[51,169],[67,171],[77,165],[98,171],[122,166],[143,168],[149,170],[175,168],[184,162]]) {
    ctx.fillStyle = black;
    ctx.fillRect(x, y, 1, 1);
  }
}

function flowLines(gust, phase) {
  if (gust < .18) return;
  const offset = (phase * 6) % 19;
  for (let row = 0; row < 4; row++) {
    const x = 108 + row * 12 + offset;
    const y = 45 + row * 30;
    shape(p => {
      p.moveTo(x, y); p.quadraticCurveTo(x + 8, y - 3, x + 21, y - 1);
      p.moveTo(x + 25, y - 1); p.lineTo(x + 30, y);
    }, null, black, 1.2);
  }
}

function flake(x, y, spin) {
  ctx.save();
  ctx.translate(x, y); ctx.rotate(spin);
  shape(p => {
    p.moveTo(-3, -1); p.lineTo(-1, -3); p.lineTo(3, -2);
    p.lineTo(2, 2); p.lineTo(-2, 3); p.closePath();
  });
  ctx.restore();
}

function arcPath(points, t) {
  const at = clamp(t) * (points.length - 1);
  const i = Math.min(points.length - 2, Math.floor(at));
  const a = points[Math.max(0, i - 1)], b = points[i], c = points[i + 1];
  const d = points[Math.min(points.length - 1, i + 2)];
  const u = at - i;
  function spline(p, q, r, s) {
    return .5 * ((2 * q) + (-p + r) * u + (2 * p - 5 * q + 4 * r - s) * u * u
      + (-p + 3 * q - 3 * r + s) * u * u * u);
  }
  return point(spline(a.x, b.x, c.x, d.x), spline(a.y, b.y, c.y, d.y));
}
const swimLoop = [point(CRUISE.x, CRUISE.y), point(109,64), point(62,48), point(47,82),
  point(55,121), point(93,130), point(CRUISE.x, CRUISE.y)];

function cruisingPose(elapsed) {
  const drift = elapsed * Math.PI * 2 / 5600;
  return { ...CRUISE, x: CRUISE.x + Math.sin(drift) * 6,
    y: CRUISE.y + Math.sin(drift * 1.6) * 3, angle: Math.sin(drift + .6) * .07,
    phase: elapsed * Math.PI * 2 / 1800, kick: .42 };
}

function aquarium(name, progress, origin = CRUISE, reef = false) {
  const t = clamp(progress);
  const phase = t * (name === 'flow' ? 64 : 27);
  const peekPose = reef ? TORCH_PEEK : PEEK;
  let pet = { phase, kick: 0, ...origin };
  let gust = 0;
  let food = null;
  let inFront = false;
  if (name === 'open') {
    if (t < .34) {
      const reveal = ease(t / .22);
      pet = { ...PEEK, x: mix(47, PEEK.x, reveal), y: mix(111, PEEK.y, reveal),
        angle: -.08 * reveal, glance: mix(-1.1, PEEK.glance, ease((t - .16) / .18)) };
    } else {
      const swim = ease((t - .34) / .66);
      pet = { ...CRUISE, x: mix(PEEK.x, CRUISE.x, swim),
        y: mix(PEEK.y, CRUISE.y, swim) - pulse(swim) * 12,
        angle: -.15 * pulse(swim), phase, kick: .75, clip: t < .48 ? PEEK.clip : undefined };
    }
  } else if (name === 'feed') {
    const rise = ease((t - .33) / .36);
    const settle = ease((t - .74) / .26);
    pet = { ...CRUISE, x: mix(mix(origin.x, 146, rise), CRUISE.x, settle),
      y: mix(mix(origin.y, 91, rise), CRUISE.y, settle),
      angle: -.18 * pulse((t - .32) / .4), phase, kick: .25 + .73 * pulse((t - .32) / .38),
      gulp: pulse((t - .58) / .24) };
    if (t < .7) food = { x: 177 + 2 * Math.sin(t * 12), y: mix(29, 86, t / .7), spin: t * 4.5 };
  } else if (name === 'flow') {
    gust = ease(t / .13) * (1 - ease((t - .48) / .15));
    if (t < .57) {
      const start = ease(t / .18);
      const turn = ease((t - .07) / .14);
      pet = { ...CRUISE, x: mix(origin.x, CRUISE.x, start) + 2 * Math.sin(phase * .65) * gust,
        y: mix(origin.y, CRUISE.y, start) + Math.sin(phase * .4) * gust,
        phase, face: turn < .5 ? 1 : -1, yaw: Math.max(.45, Math.abs(1 - 2 * turn)),
        kick: .2 + .7 * gust, clip: origin.clip !== undefined && t < .07 ? PEEK.clip : undefined };
    } else {
      const loop = (t - .57) / .43;
      const here = arcPath(swimLoop, loop);
      const ahead = arcPath(swimLoop, Math.min(.9999, loop + .02));
      const dx = ahead.x - here.x, dy = ahead.y - here.y;
      inFront = loop > .63;
      const arc = inFront ? (loop - .63) / .37 : loop / .63;
      const depth = pulse(arc) ** 2;
      const scale = CRUISE.scale * (1 + (inFront ? .05 : -.10) * depth);
      pet = { ...here, scale, phase, kick: .64, face: dx < -.25 ? -1 : 1,
        angle: Math.max(-.31, Math.min(.31, Math.atan2(dy, Math.max(11, Math.abs(dx))) * (dx < -.25 ? -1 : 1))) };
    }
  } else if (name === 'tuck') {
    if (t < .76) {
      const swim = ease(t / .76);
      pet = { ...CRUISE, x: mix(origin.x, 20, swim), y: mix(origin.y, 111, swim),
        face: -1, angle: -.15 * pulse(swim), phase, kick: .8, clip: t > .19 ? peekPose.clip : undefined };
    } else {
      const peek = ease((t - .81) / .19);
      pet = { ...peekPose, x: mix(40, peekPose.x, peek), y: mix(109, peekPose.y, peek),
        glance: mix(-1, peekPose.glance, peek) };
    }
  } else if (name === 'peek') {
    pet = peekPose;
  }
  const showTorchEye = reef && (name === 'peek' || (name === 'tuck' && t >= .76 && pet.x >= 68));
  const coralPhase = reef && name === 'rest' ? origin.phase : phase;
  floor();
  flowLines(gust, phase);
  habitatLayer(reef, gust, coralPhase, false);
  if (!inFront) drawFish(pet);
  habitatLayer(reef, gust, coralPhase, true, showTorchEye);
  if (inFront) drawFish(pet);
  if (food) flake(food.x, food.y, food.spin);
}


// Torch coral (Euphyllia glabrescens). Each cluster has long, unbranched
// tentacles with a single pale knob, above a short projecting corallite.
const torchRearHeads = [
  [39, 119, [[13, 92], [21, 72], [31, 58], [43, 43], [55, 55], [62, 66], [65, 86]]],
  [66, 108, [[37, 75], [50, 63], [58, 49], [70, 31], [84, 37], [96, 46],
             [107, 61], [111, 75], [114, 93]]],
];
// This foreground tentacle crosses the fish's peeking eye.
const torchEyeTentacle = [103, 92];
const torchFrontHeads = [
  [50, 133, [[21, 119], [26, 105], [35, 92], [48, 84], [59, 90], [65, 105], [68, 120]]],
  [77, 131, [[54, 118], [65, 105], [77, 94], [91, 82], torchEyeTentacle, [109, 105], [113, 118]]],
];

function drawTorchSkeleton() {
  shape(p => {
    p.moveTo(35, 149);
    p.quadraticCurveTo(43, 143, 33, 120);
    p.quadraticCurveTo(38, 114, 45, 119);
    p.quadraticCurveTo(43, 136, 54, 138);
    p.quadraticCurveTo(60, 126, 59, 109);
    p.quadraticCurveTo(66, 102, 73, 108);
    p.quadraticCurveTo(69, 125, 65, 137);
    p.quadraticCurveTo(74, 134, 72, 131);
    p.quadraticCurveTo(77, 125, 83, 130);
    p.quadraticCurveTo(79, 144, 69, 151);
    p.closePath();
  }, white, black, 1.9);
  shape(p => {
    p.moveTo(40, 125); p.quadraticCurveTo(40, 137, 46, 143);
    p.moveTo(65, 115); p.quadraticCurveTo(64, 128, 61, 138);
    p.moveTo(77, 136); p.quadraticCurveTo(75, 145, 69, 149);
  }, null, black, 1.2);
}

function drawTorchTentacles(heads, gust, phase, front, showTorchEye) {
  ctx.lineCap = 'round';
  const tips = [];
  for (const [x, y, endpoints] of heads) {
    for (const [i, tip] of endpoints.entries()) {
      if (showTorchEye && tip === torchEyeTentacle) continue;
      const [tipX, tipY] = tip;
      const rise = y - tipY;
      const side = Math.sign(tipX - x);
      const wave = Math.sin(phase * .65 + i * .84 + x * .12);
      const drift = gust * (16 + rise * .21) + wave * (1.6 + rise * .041);
      const tx = tipX + drift;
      const ty = tipY + gust * (4 + rise * .12) + wave * 1.45;
      const width = (front ? 3.6 : 3.1) + (i % 3) * .32;
      shape(p => {
        p.moveTo(x + (i % 4 - 1.5) * 2, y - (i % 3) * 2);
        p.bezierCurveTo(x - side * 8 + 8 * gust, y - rise * .52,
          mix(x, tx, .88) - 7 * wave, mix(y, ty, .62) - 8, tx, ty);
      }, null, black, width);
      tips.push([tx, ty, front ? 3.3 : 3.5]);
    }
  }
  for (const [x, y, radius] of tips) oval(x, y, radius, radius * .92, white, black, 1.5);
}

function habitatLayer(reef, gust, phase, front, showTorchEye = false) {
  if (reef) {
    if (!front) drawTorchSkeleton();
    drawTorchTentacles(front ? torchFrontHeads : torchRearHeads, gust, phase, front, showTorchEye);
  } else {
    tentacles(front ? frontTentacles : backTentacles, gust, phase, front);
  }
}

function travel(progress, fromReef) {
  const t = clamp(progress);
  if (t === 0 || t === 1) {
    aquarium('rest', 0, cruisingPose(0), t === 0 ? fromReef : !fromReef);
    return;
  }
  const direction = fromReef ? -1 : 1;
  const camera = direction * SIZE * ease(t);
  const locations = [[fromReef, -camera], [!fromReef, direction * SIZE - camera]];
  function paintReef(reef, x, front) {
    ctx.save();
    ctx.translate(x, 0);
    if (!front) floor();
    habitatLayer(reef, 0, 0, front);
    ctx.restore();
  }
  for (const [reef, x] of locations) paintReef(reef, x, false);
  const seam = direction === 1 ? SIZE - camera : -camera;
  shape(p => {
    p.moveTo(seam - 2, 158);
    p.lineTo(seam + 2, 162);
  }, null, black, 1.4);
  const turn = direction < 0 ? Math.min(ease(t / .17), ease((1 - t) / .18)) : 0;
  const idle = cruisingPose(0);
  drawFish({ ...idle, x: idle.x + direction * pulse(t) * 28,
    y: idle.y - pulse(t) * 12 + Math.sin(t * Math.PI * 4) * pulse(t) * 4,
    angle: idle.angle + Math.sin(t * Math.PI * 2) * pulse(t) * .12,
    phase: t * 33, kick: idle.kick + .48 * pulse(t), face: turn < .5 ? 1 : -1,
    yaw: Math.max(.34, Math.abs(1 - 2 * turn)) });
  for (const [reef, x] of locations) paintReef(reef, x, true);
}
