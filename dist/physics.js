/* Grip Lab: deliberately simplified educational planar vehicle model.
 * Axes: body +x forward / +y left; world +x east / +y north.
 * Inputs: steer +left, throttle/brake [0,1]. SI units throughout.
 * Not fitted to ACC/LMU/AMS2/WRC/AC, and not a setup or lap-time predictor.
 */
(function (root) {
  'use strict';
  const surfaces = Object.freeze({
    dry: Object.freeze({ label: '드라이 아스팔트', mu: 1.08, stiffness: 1, rolling: 0.014 }),
    wet: Object.freeze({ label: '젖은 아스팔트', mu: 0.65, stiffness: 0.76, rolling: 0.014 }),
    gravel: Object.freeze({ label: '그래블', mu: 0.49, stiffness: 0.47, rolling: 0.026 })
  });
  const P = Object.freeze({ mass: 1320, gravity: 9.81, a: 1.18, b: 1.47, wheelbase: 2.65, height: 0.52, track: 1.60, inertia: 2220 });
  const W = P.mass * P.gravity;
  const NF0 = W * P.b / P.wheelbase;
  const NR0 = W - NF0;
  const clamp = (x, a, b) => Math.max(a, Math.min(b, x));
  const finite = (x, fallback = 0) => Number.isFinite(x) ? x : fallback;
  const wrap = x => Math.atan2(Math.sin(x), Math.cos(x));

  function create(options = {}) {
    const v = clamp(finite(options.speed, 60 / 3.6), 0, 65);
    const surface = surfaces[options.surface] ? options.surface : 'dry';
    return {
      x: 0, y: 0, heading: 0, vx: v, vy: 0, yawRate: 0,
      speed: v, beta: 0, ax: 0, ay: 0, alphaF: 0, alphaR: 0,
      utilF: 0, utilR: 0, fxF: 0, fxR: 0, fyF: 0, fyR: 0,
      Nf: NF0, Nr: NR0, loadFL: NF0 / 2, loadFR: NF0 / 2,
      loadRL: NR0 / 2, loadRR: NR0 / 2,
      capF: surfaces[surface].mu * NF0, capR: surfaces[surface].mu * NR0,
      mu: surfaces[surface].mu, time: 0, steer: 0, surface,
      throttle: 0, brake: 0, drive: 'rwd', _loadAx: 0, _loadAy: 0,
      model: 'educational-bicycle-v1'
    };
  }

  // Smooth brush-like lateral curve. At very large slip it falls gently, so
  // countersteering and reducing rear longitudinal demand can recover a slide.
  function tire(alpha, fxRequest, cap, stiffness) {
    const shape = 1.35;
    const fyLinear = stiffness * alpha;
    const fyPure = cap * Math.sin(shape * Math.atan(fyLinear / (shape * cap)));
    const demand = Math.hypot(fxRequest, fyPure);
    const scale = demand > cap ? cap / demand : 1;
    return { fx: fxRequest * scale, fy: fyPure * scale,
      util: Math.hypot(fxRequest, fyLinear) / cap };
  }

  function integrate(s, input, h, surface, drive) {
    // Steering angle has a finite actuator rate. This makes keyboard steps
    // usable but does not model steering-wheel force feedback.
    const targetSteer = input.steer * 0.53;
    s.steer += clamp(targetSteer - s.steer, -2.1 * h, 2.1 * h);
    const c = Math.cos(s.steer), sn = Math.sin(s.steer);

    // Filtered acceleration gives a modest suspension-response time, without
    // implying that this 2D model solves pitch, roll or dampers.
    const response = 1 - Math.exp(-h / 0.11);
    s._loadAx += (s.ax - s._loadAx) * response;
    s._loadAy += (s.ay - s._loadAy) * response;
    s.Nf = clamp(NF0 - P.mass * P.height * s._loadAx / P.wheelbase, W * 0.12, W * 0.88);
    s.Nr = W - s.Nf;
    const lateralTransfer = P.mass * P.height * s._loadAy / P.track;
    const tf = clamp(lateralTransfer * 0.55, -0.46 * s.Nf, 0.46 * s.Nf);
    const tr = clamp(lateralTransfer * 0.45, -0.46 * s.Nr, 0.46 * s.Nr);
    s.loadFL = s.Nf / 2 - tf; s.loadFR = s.Nf / 2 + tf;
    s.loadRL = s.Nr / 2 - tr; s.loadRR = s.Nr / 2 + tr;
    // Small axle capacity loss from unequal left/right load. No claim to an
    // individual game's compound, tire pressure, temperature or wear behavior.
    s.capF = surface.mu * s.Nf * (1 - 0.08 * Math.pow(2 * tf / s.Nf, 2));
    s.capR = surface.mu * s.Nr * (1 - 0.08 * Math.pow(2 * tr / s.Nr, 2));

    const forward = Math.max(s.vx, 2.0);
    s.alphaF = wrap(s.steer - Math.atan2(s.vy + P.a * s.yawRate, forward));
    s.alphaR = wrap(-Math.atan2(s.vy - P.b * s.yawRate, forward));
    // Linearized tire stiffness is softened near rest to avoid the zero-speed
    // singularity of a dynamic bicycle model. Reverse is outside this lesson.
    const lowSpeed = clamp(Math.hypot(s.vx, s.vy) / 2.0, 0.12, 1);
    const cf = 76000 * surface.stiffness * Math.pow(s.Nf / NF0, 0.90) * lowSpeed;
    const cr = 80000 * surface.stiffness * Math.pow(s.Nr / NR0, 0.90) * lowSpeed;
    const engine = input.throttle * Math.min(8400, 165000 / Math.max(s.vx, 1));
    const braking = input.brake * W * 1.05;
    // A brake held at rest does not create inertial load transfer. Treat this
    // as a static hold; do not display a fictitious -1 g at zero velocity.
    if (s.speed < 0.07 && Math.abs(s.vy) < 0.07 && engine <= braking) {
      s.vx = 0; s.vy = 0; s.yawRate = 0; s.speed = 0; s.beta = 0;
      s.ax = 0; s.ay = 0; s.alphaF = 0; s.alphaR = 0;
      s.fxF = 0; s.fxR = 0; s.fyF = 0; s.fyR = 0;
      s.utilF = 0; s.utilR = 0; s.time += h;
      return;
    }
    const frontDrive = drive === 'fwd' ? 1 : drive === 'awd' ? 0.44 : 0;
    // Brakes oppose forward travel. The no-reverse clamp below provides a
    // static stop, rather than accelerating the car backward under the brake.
    const brakeDirection = s.vx >= 0 ? 1 : -1;
    const f = tire(s.alphaF, engine * frontDrive - braking * 0.64 * brakeDirection, s.capF, cf);
    const r = tire(s.alphaR, engine * (1 - frontDrive) - braking * 0.36 * brakeDirection, s.capR, cr);
    s.fxF = f.fx; s.fyF = f.fy; s.fxR = r.fx; s.fyR = r.fy;
    s.utilF = f.util; s.utilR = r.util;
    const resistance = 0.44 * s.vx * Math.abs(s.vx) + surface.rolling * W * Math.tanh(s.vx / 0.4);
    const fx = f.fx * c - f.fy * sn + r.fx - resistance;
    const fy = f.fy * c + f.fx * sn + r.fy;
    s.ax = fx / P.mass;
    s.ay = fy / P.mass;
    const oldVx = s.vx, oldVy = s.vy, oldYaw = s.yawRate;
    s.vx += (s.ax + oldYaw * oldVy) * h;
    s.vy += (s.ay - oldYaw * oldVx) * h;
    s.yawRate += (P.a * (f.fy * c + f.fx * sn) - P.b * r.fy) / P.inertia * h;

    if (s.vx < 0) s.vx = 0; // no reverse gear in this training model
    if (s.vx < 0.08 && input.throttle < 0.02) {
      s.vy *= Math.exp(-h * 9);
      s.yawRate *= Math.exp(-h * 9);
      if (input.brake > 0.01 || s.vx < 0.01) s.vx = 0;
    }
    s.heading = wrap(s.heading + s.yawRate * h);
    const ch = Math.cos(s.heading), sh = Math.sin(s.heading);
    s.x += (s.vx * ch - s.vy * sh) * h;
    s.y += (s.vx * sh + s.vy * ch) * h;
    s.speed = Math.hypot(s.vx, s.vy);
    s.beta = s.speed > 0.25 ? Math.atan2(s.vy, s.vx) : 0;
    s.time += h;
  }

  function step(state, controls = {}, dt = 1 / 60, options = {}) {
    const s = state;
    const seconds = clamp(finite(dt), 0, 0.25);
    if (!seconds) return s;
    const surfaceKey = surfaces[options.surface] ? options.surface : (surfaces[s.surface] ? s.surface : 'dry');
    const surface = surfaces[surfaceKey];
    const drive = ['rwd', 'fwd', 'awd'].includes(options.drive) ? options.drive : (s.drive || 'rwd');
    const input = { steer: clamp(finite(controls.steer), -1, 1), throttle: clamp(finite(controls.throttle), 0, 1), brake: clamp(finite(controls.brake), 0, 1) };
    s.surface = surfaceKey; s.mu = surface.mu; s.drive = drive;
    s.throttle = input.throttle; s.brake = input.brake;
    const n = Math.ceil(seconds / (1 / 240));
    const h = seconds / n;
    for (let i = 0; i < n; i++) integrate(s, input, h, surface, drive);
    return s;
  }

  const api = Object.freeze({ create, step, surfaces, constants: P });
  root.GripPhysics = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
