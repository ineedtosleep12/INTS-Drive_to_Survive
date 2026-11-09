'use strict';
const assert = require('node:assert/strict');
const P = require('./dist/physics.js');
const fields = ['x','y','heading','vx','vy','yawRate','speed','beta','ax','ay','alphaF','alphaR','utilF','utilR','fxF','fxR','fyF','fyR','Nf','Nr','loadFL','loadFR','loadRL','loadRR','time','steer'];
let checks = 0;
function invariant(s) {
  for (const key of fields) assert(Number.isFinite(s[key]), `${key} must be finite`);
  const mg = P.constants.mass * P.constants.gravity;
  assert(Math.abs(s.Nf + s.Nr - mg) < 1e-7);
  assert(Math.abs(s.loadFL+s.loadFR+s.loadRL+s.loadRR-mg) < 1e-7);
  for (const key of ['loadFL','loadFR','loadRL','loadRR']) assert(s[key] > 0);
  assert(Math.hypot(s.fxF,s.fyF) <= s.mu * s.Nf + 1e-7);
  assert(Math.hypot(s.fxR,s.fyR) <= s.mu * s.Nr + 1e-7);
  assert(Math.hypot(s.fxF,s.fyF) <= s.capF + 1e-7);
  assert(Math.hypot(s.fxR,s.fyR) <= s.capR + 1e-7);
  checks++;
}
function run(s, input, duration, options, dt=1/60) {
  for(let i=0;i<Math.round(duration/dt);i++) { P.step(s,typeof input==='function'?input(s.time):input,dt,options); invariant(s); }
  return s;
}
const straight = run(P.create({speed:25}),{},8);
assert.equal(straight.y,0); assert.equal(straight.vy,0); assert.equal(straight.heading,0); assert(straight.x>100);
const left = run(P.create({speed:18}),{steer:.07,throttle:.06},3);
assert(left.yawRate>0.05); assert(left.y>0); assert(left.heading>0); assert(left.ay>0);
assert(left.loadFR>left.loadFL); assert(left.loadRR>left.loadRL);
const brake = run(P.create({speed:25}),{brake:.8},2);
assert(brake.speed<12); assert(brake.Nf>P.create().Nf); assert(brake.Nr<P.create().Nr);
const stop = run(P.create({speed:4}),{brake:1},20);
assert(stop.speed<.001); assert(stop.vx>=0);
assert.equal(stop.ax,0); assert(Math.abs(stop.Nf-P.create().Nf)<1e-7);
const dryStop = run(P.create({speed:25,surface:'dry'}),{brake:1},1.5);
const gravelStop = run(P.create({speed:25,surface:'gravel'}),{brake:1},1.5);
assert(gravelStop.speed>dryStop.speed+4,'less grip should lengthen braking');
const dryTurn = run(P.create({speed:25,surface:'dry'}),{steer:.32},.8);
const gravelTurn = run(P.create({speed:25,surface:'gravel'}),{steer:.32},.8);
assert(Math.abs(dryTurn.ay)>Math.abs(gravelTurn.ay)+1,'less grip should reduce lateral acceleration');
const pull = run(P.create({speed:8}),{throttle:1},2);
assert(pull.speed>14); assert(pull.Nr>P.create().Nr);
// Independently repeat input sequence: exact same stepping is deterministic.
function sequence(t) { return {steer:Math.sin(t*1.3)*.5,throttle:(Math.sin(t*.8)+1)/2,brake:t%7>5?.7:0}; }
const repeatA = run(P.create(),sequence,25,{surface:'wet'});
const repeatB = run(P.create(),sequence,25,{surface:'wet'});
assert.deepEqual(repeatA,repeatB);
// Different render rates still use the same <=1/240 s integration subdivision.
const fps30 = run(P.create({speed:20}),{steer:.10,throttle:.10},3,{},1/30);
const fps60 = run(P.create({speed:20}),{steer:.10,throttle:.10},3,{},1/60);
assert(Math.abs(fps30.x-fps60.x)<1e-6); assert(Math.abs(fps30.y-fps60.y)<1e-6);
for(const surface of Object.keys(P.surfaces)) {
  for(const drive of ['fwd','rwd','awd']) {
    run(P.create({speed:27,surface}),t=>({steer:Math.sin(t*1.8),throttle:t%6<4?1:0,brake:t%6>=4?1:0}),60,{surface,drive});
  }
}
const invalidInput = run(P.create({speed:NaN}),{steer:NaN,throttle:Infinity,brake:-1},1);
assert(invalidInput.speed>=0);
const noTime=P.create(); const initial=structuredClone(noTime); P.step(noTime,{},NaN); assert.deepEqual(noTime,initial);
console.log(`PASS: ${checks} states; finite dynamics, force/load bounds, signs, braking, surfaces, repeatability, render-rate stability.`);
console.log(JSON.stringify({straightDistance:straight.x,leftYaw:left.yawRate,brakeSpeed:brake.speed,dryBrakeSpeed:dryStop.speed,gravelBrakeSpeed:gravelStop.speed,dryLateralG:dryTurn.ay/9.81,gravelLateralG:gravelTurn.ay/9.81},null,2));
