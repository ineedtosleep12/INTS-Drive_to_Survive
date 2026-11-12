'use strict';
const assert = require('node:assert/strict');
const P = require('./dist/physics.js');
const fields = ['x','y','heading','vx','vy','yawRate','speed','beta','ax','ay','alphaF','alphaR','utilF','utilR','fxF','fxR','fyF','fyR','Nf','Nr','loadFL','loadFR','loadRL','loadRR','time','steer','downforce','normalTotal','capF','capR','mu','longitudinalG','lateralG','powerDelivered','powerRequested','tcReduction','engineBrakeForce','handbrakeForce','driveForce'];
let checks=0;
function invariant(s){
  for(const key of fields)assert(Number.isFinite(s[key]),s.vehicle+' '+key+' must be finite');
  const normal=s.params.mass*9.81+s.downforce;
  assert(Math.abs(s.Nf+s.Nr-normal)<1e-6,'total load includes aero');
  assert(Math.abs(s.loadFL+s.loadFR+s.loadRL+s.loadRR-normal)<1e-6,'contact loads sum to total');
  for(const key of ['loadFL','loadFR','loadRL','loadRR'])assert(s[key]>0,'contact positive');
  assert(Math.hypot(s.fxF,s.fyF)<=s.capF+1e-6,'front friction capacity');
  assert(Math.hypot(s.fxR,s.fyR)<=s.capR+1e-6,'rear friction capacity');
  assert(s.powerDelivered<=s.params.power*s.params.powerEfficiency+1e-6,'power limited');
  assert(s.tcReduction>=0&&s.tcReduction<=1);
  assert(s.vx>=0); assert(s.speed<180,'bounded education model speed');
  checks++;
}
function run(s,input,duration,options={},dt=1/60){
  for(let i=0;i<Math.round(duration/dt);i++){P.step(s,typeof input==='function'?input(s.time):input,dt,options);invariant(s);}
  return s;
}
const expected=['gt3','rally1','ae86','hypercar','formula','touring_fwd','midship','rearengine'];
assert.deepEqual(Object.keys(P.vehicles),expected);
for(const id of expected){
  const p=P.vehicles[id],s=P.create({vehicle:id,speed:20});
  assert.equal(s.vehicle,id);assert.equal(s.drive,p.drive);
  assert(Math.abs(p.a+p.b-p.wheelbase)<1e-12);
  assert(s.params.assumptions.length>0);invariant(s);
  const straight=run(P.create({vehicle:id,speed:25}),{clutch:1},8);
  assert.equal(straight.y,0);assert.equal(straight.vy,0);assert.equal(straight.heading,0);assert(straight.x>100);
  const left=run(P.create({vehicle:id,speed:18,surface:'dry'}),{steer:.07,throttle:.04},2);
  assert(left.yawRate>.04,id+' left yaw');assert(left.y>0);assert(left.heading>0);assert(left.ay>0);
  assert(left.loadFR>left.loadFL);assert(left.loadRR>left.loadRL);
  const brake=run(P.create({vehicle:id,speed:25,surface:'dry'}),{brake:.8},1);
  assert(brake.speed<20,id+' deceleration');assert(brake.Nf>brake.staticNf);
  const stop=run(P.create({vehicle:id,speed:4}),{brake:1},20);
  assert(stop.speed<.001);assert.equal(stop.ax,0);assert(Math.abs(stop.Nf-P.create({vehicle:id,speed:0}).Nf)<1e-6);
  const dry=run(P.create({vehicle:id,speed:25,surface:'dry'}),{brake:1},1.5);
  const gravel=run(P.create({vehicle:id,speed:25,surface:'gravel'}),{brake:1},1.5);
  assert(gravel.speed>dry.speed+3,id+' low friction braking distance');
  const noClutch=run(P.create({vehicle:id,speed:18}),{},2);
  const clutch=run(P.create({vehicle:id,speed:18}),{clutch:1},2);
  assert(clutch.speed>noClutch.speed+.1,id+' clutch removes engine brake');
  assert.equal(clutch.engineBrakeForce,0);
  const disconnected=run(P.create({vehicle:id,speed:18}),{clutch:1,throttle:1},.25);
  assert.equal(disconnected.driveForce,0);assert.equal(disconnected.powerDelivered,0);assert.equal(disconnected.drivelineEngaged,false);
  const pull=run(P.create({vehicle:id,speed:12,surface:'dry'}),{throttle:.5},1);
  assert(pull.speed>12,id+' positive throttle');
  // Pure handbrake input acts on the rear, with no fictitious direct yaw impulse.
  const hand=run(P.create({vehicle:id,speed:18,surface:'dry'}),{handbrake:.5,clutch:1},.2);
  assert.equal(hand.fxF,0);assert(hand.fxR<0);assert.equal(hand.yawRate,0);assert(hand.speed<18);
  // Aero is squared speed, absent entirely from the AE86 education profile.
  const slow=P.create({vehicle:id,speed:20}),fast=P.create({vehicle:id,speed:40});
  if(p.downforceCoefficient){assert(Math.abs(fast.downforce-4*slow.downforce)<1e-7);assert(fast.capF+fast.capR>slow.capF+slow.capR);}
  else{assert.equal(slow.downforce,0);assert.equal(fast.downforce,0);}
  for(const surface of Object.keys(P.surfaces)){
    for(const drive of ['fwd','rwd','awd']){
      run(P.create({vehicle:id,speed:55,surface}),t=>({steer:Math.sin(t*1.8),throttle:t%6<4?1:0,brake:t%6>=4?1:0,handbrake:t%9>8?.8:0,clutch:t%5>4?1:0}),30,{surface,drive,aids:'off'});
    }
    run(P.create({vehicle:id,speed:55,surface}),t=>({steer:Math.sin(t)*.7,throttle:1,handbrake:t%9>8?1:0}),15,{surface,aids:'on'});
  }
}
// Equal stepping is deterministic, and divisible render rates share substeps.
function sequence(t){return {steer:Math.sin(t*1.3)*.5,throttle:(Math.sin(t*.8)+1)/2,brake:t%7>5?.7:0,handbrake:t%9>8?.7:0,clutch:t%5>4?1:0};}
assert.deepEqual(run(P.create(),sequence,25,{surface:'wet'}),run(P.create(),sequence,25,{surface:'wet'}));
const fps30=run(P.create({speed:20}),{steer:.10,throttle:.10},3,{},1/30);
const fps60=run(P.create({speed:20}),{steer:.10,throttle:.10},3,{},1/60);
assert(Math.abs(fps30.x-fps60.x)<1e-6);assert(Math.abs(fps30.y-fps60.y)<1e-6);
// Load sensitivity: at equal axle load, unequal wheel loads reduce total grip.
const balanced=P.create({vehicle:'ae86',speed:0}),loaded=P.create({vehicle:'ae86',speed:0});
loaded._loadAy=6;loaded.ay=6;P.step(balanced,{clutch:1},1/240);P.step(loaded,{clutch:1},1/240);
assert(Math.abs(balanced.Nf-loaded.Nf)<1e-6);assert(loaded.capF<balanced.capF);assert(loaded.capR<balanced.capR);
// Higher GT3 output/grip should make faster straight acceleration than AE86.
const gt3Pull=run(P.create({vehicle:'gt3',speed:20}),{throttle:1},3);
const ae86Pull=run(P.create({vehicle:'ae86',speed:20}),{throttle:1},3);
assert(gt3Pull.speed>ae86Pull.speed+5);
assert(P.create({vehicle:'formula',speed:40}).downforce>P.create({vehicle:'gt3',speed:40}).downforce);
// The training aid changes drive demand, never the tire's capacity.
const aidOn=P.create({vehicle:'gt3',speed:20,aids:'on'});
const aidOff=P.create({vehicle:'gt3',speed:20,aids:'off'});
for(const s of [aidOn,aidOff]){s.vy=2;s.speed=Math.hypot(s.vx,s.vy);}
P.step(aidOn,{throttle:1},1/240);P.step(aidOff,{throttle:1},1/240);
assert.equal(aidOn.capR,aidOff.capR);assert(aidOn.driveForce<aidOff.driveForce);assert(aidOn.tcReduction>0);assert.equal(aidOff.tcActive,false);
assert.equal(P.create({vehicle:'ae86',aids:'default'}).aids,false);
assert.equal(P.create({vehicle:'gt3',aids:'default'}).aids,true);
P.step(aidOn,{},1/60,{aids:'off'});assert.equal(aidOn.aids,false);assert.equal(aidOn.tcReduction,0);
// A clutch cycle has no hidden energy reservoir and cannot create a kick.
const clutchCycle=P.create({vehicle:'ae86',speed:18}),zeroDrive=P.create({vehicle:'ae86',speed:18});
run(clutchCycle,{throttle:1,clutch:1},.5);run(zeroDrive,{throttle:0,clutch:1},.5);
assert.deepEqual(clutchCycle.x,zeroDrive.x);assert.deepEqual(clutchCycle.speed,zeroDrive.speed);
P.step(clutchCycle,{throttle:1},1/60);P.step(zeroDrive,{throttle:1},1/60);
assert.equal(clutchCycle.driveForce,zeroDrive.driveForce);assert.equal(clutchCycle.yawRate,zeroDrive.yawRate);
// In an established turn, rear braking consumes lateral reserve and rotates
// this rear-drive teaching car more. The straight-line case above stays straight.
const turnBase=run(P.create({vehicle:'ae86',speed:18,surface:'dry',aids:'off'}),{steer:.09,throttle:.1},1);
const turnHand=structuredClone(turnBase);
run(turnBase,{steer:.09,clutch:1},.3);
run(turnHand,{steer:.09,clutch:1,handbrake:.6},.3);
assert(turnHand.yawRate>turnBase.yawRate+.1);
assert(Math.abs(turnHand.fyR)<Math.abs(turnBase.fyR));
// Fifteen-minute time limit remains stable after coasting to a stop.
for(const id of expected)run(P.create({vehicle:id,speed:55}),{},900);
const invalid=run(P.create({speed:NaN}),{steer:NaN,throttle:Infinity,brake:-1,handbrake:NaN,clutch:NaN},1);
assert(invalid.speed>=0);
const noTime=P.create(),initial=structuredClone(noTime);P.step(noTime,{},NaN);
assert.deepEqual(JSON.parse(JSON.stringify(noTime)),JSON.parse(JSON.stringify(initial)));
assert.equal(P.capabilities.clutchKick,false);assert.equal(P.capabilities.wheelRotation,false);assert.equal(P.capabilities.realABS,false);
console.log('PASS: '+checks+' states, 8 vehicle profiles. Finite motion, force/load/power bounds, aero, braking, surfaces, clutch, handbrake, load sensitivity, aid fairness, deterministic/rate stability, 900-second sessions.');
console.log(JSON.stringify({gt3ThreeSecondSpeedKph:gt3Pull.speed*3.6,ae86ThreeSecondSpeedKph:ae86Pull.speed*3.6,gt3DownforceAt144Kph:P.create({vehicle:'gt3',speed:40}).downforce,formulaDownforceAt144Kph:P.create({vehicle:'formula',speed:40}).downforce},null,2));

