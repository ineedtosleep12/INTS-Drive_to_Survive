'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const D = require('./dist/debrief.js');
let checks = 0;
function test(name, fn) { fn(); checks++; }
function trace(seconds = 5, patch = () => ({})) {
  return Array.from({length: Math.round(seconds * 10) + 1}, (_, i) => {
    const t = i / 10;
    return {t, s: t * 10, x: t * 10, y: 0, heading: 0, speed: 60, steer: .2, brake: 0,
      throttle: .2, handbrake: 0, clutch: 0, utilF: .7, utilR: .7, beta: 3, off: false, ...patch(t)};
  });
}
const failure = {reason: 'finish', time: 5, offTime: 1.1};
const firstOff = t => t >= 4;
function curveScene(sign = 1) {
  const points = [];
  for(let s=0;s<=20;s+=2)points.push({s,x:s,y:0});
  for(let i=1;i<=20;i++){const angle=i/20*Math.PI/2;points.push({s:20+angle*20,x:20+20*Math.sin(angle),y:sign*20*(1-Math.cos(angle))});}
  return {gates:{entry:20,exit:51},points};
}
test('combined brake and steering precede first off-track event', () => {
  const input = trace(6, t => ({off: firstOff(t), ...(t >= 2.8 && t < 4 ? {brake: .6, utilF: 1.3} : {})}));
  const copy = JSON.stringify(input), result = D.analyze({record: failure, trace: input});
  assert.equal(result.certainty, 'likely'); assert.equal(result.metrics.evidenceKind, 'combined');
  assert.equal(result.focusTime, 4); assert(result.events[1].t < 4);
  assert.deepEqual(result.events.map(e => e.t), result.events.map(e => e.t).sort((a,b) => a-b));
  assert.match(result.events[0].detail, /제동 60%/); assert.match(result.events[1].detail, /130%/);
  assert.equal(JSON.stringify(input), copy, 'analysis must not mutate input');
});
test('late saturation cannot explain earlier off-track', () => {
  const result = D.analyze({record: failure, trace: trace(6, t => ({off: t >= 2, ...(t >= 2.2 ? {utilF: 4, brake: 1} : {})}))});
  assert.equal(result.certainty, 'unknown'); assert.equal(result.focusTime, 2);
  assert(result.events.every(e => e.t <= 2)); assert.equal(result.metrics.evidenceKind, undefined);
});
test('one saturation spike is not causal evidence', () => {
  const result = D.analyze({record: failure, trace: trace(5, t => ({off: firstOff(t), ...(t === 3.8 ? {utilF: 3, brake: 1} : {})}))});
  assert.equal(result.certainty, 'unknown');
});
test('missing samples cannot form a continuous saturation interval', () => {
  const input = trace(5, t => ({off: firstOff(t), ...(t >= 2.8 ? {utilF: 1.4, brake: .6} : {})})).filter(s => s.t < 2.8 || [2.8,3.2,3.6,4,4.5,5].includes(s.t));
  assert.equal(D.analyze({record: failure, trace: input}).certainty, 'unknown');
});
test('rear throttle sequence and spin explain a different candidate', () => {
  const result = D.analyze({record: {reason:'spin',time:5,offTime:0}, trace: trace(5, t => ({...(t >= 2.5 ? {throttle:.8,utilR:1.5,beta:t >= 4 ? 75 : 22} : {})}))});
  assert.equal(result.certainty, 'likely'); assert.equal(result.metrics.evidenceKind, 'traction'); assert.equal(result.focusTime, 4);
  assert.match(result.events.at(-1).label, /슬립/);
});
test('handbrake evidence is distinct from power over-demand', () => {
  const result = D.analyze({record: failure, trace: trace(5, t => ({off:firstOff(t), ...(t >= 2.8 ? {throttle:.8,handbrake:1,utilR:1.6,beta:25} : {})}))});
  assert.equal(result.metrics.evidenceKind, 'handbrake'); assert.match(result.events[0].detail, /핸드브레이크 100%/);
});
test('steering increase is observed input change, not guaranteed turning', () => {
  const result = D.analyze({record: failure, trace: trace(5, t => ({off:firstOff(t), ...(t >= 2.8 ? {utilF:1.3,steer:Math.min(.8,.2+(t-2.8)*.3)} : {})}))});
  assert.equal(result.metrics.evidenceKind, 'front'); assert.match(result.explanation, /조향 입력 크기도/); assert.match(result.explanation, /가능성/);
});
test('earliest response is selected instead of later stronger response', () => {
  const result = D.analyze({record: failure, trace: trace(5, t => ({off:firstOff(t), ...(t >= 2.5 ? {utilF:1.2} : {}), ...(t >= 3.1 ? {utilR:2,throttle:1,beta:30} : {})}))});
  assert.equal(result.metrics.evidenceKind, 'front');
});
test('a prior window of at most two seconds limits cause selection', () => {
  const result = D.analyze({record: failure, trace: trace(6, t => ({off:t >= 5, ...(t >= 1 && t < 2 ? {utilF:1.5,brake:.8} : {})}))});
  assert.equal(result.certainty, 'unknown'); assert.equal(result.focusTime, 5);
});
test('missing trace, malformed rows and sparse data remain unknown', () => {
  for (const input of [undefined, null, {}, [], [{t:1}], [{t:NaN},{t:Infinity},{t:-1}], [{t:0},{t:20}]]) {
    assert.equal(D.analyze({record:failure,trace:input}).certainty, 'unknown');
  }
});
test('missing channels are never interpreted as observed zeros', () => {
  const input = trace(5, t => ({off:firstOff(t), ...(t >= 2.8 ? {brake:.8,utilF:1.5,steer:undefined} : {})}));
  const result = D.analyze({record:failure,trace:input});
  assert.equal(result.certainty, 'unknown'); assert.match(result.events[0].detail, /조향 입력 미기록/);
});
test('corrupt finite input channels do not manufacture a cause', () => {
  const input = trace(5, t => ({off:firstOff(t), ...(t >= 2.8 ? {brake:99,utilF:NaN,steer:Infinity} : {})}));
  assert.equal(D.analyze({record:failure,trace:input}).certainty, 'unknown');
});
test('duplicate unordered trace sorts safely with deterministic first sample', () => {
  const input = trace(5, t => ({off:firstOff(t), ...(t >= 2.8 && t < 4 ? {brake:.6,utilF:1.3} : {})}));
  input.push({...input[0]}); input.reverse();
  const result = D.analyze({record:failure,trace:input});
  assert.equal(result.certainty, 'likely'); assert.equal(result.focusTime, 4);
});
test('recorded off-track without matching sample is unknown', () => {
  assert.equal(D.analyze({record:failure,trace:trace()}).certainty, 'unknown');
});
test('success is observed completion, not optimal pace or mastery', () => {
  const result = D.analyze({record:{reason:'finish',offTime:0,time:5},trace:trace()});
  assert.equal(result.certainty, 'observed'); assert.match(result.headline, /이탈 없이/); assert.match(result.explanation, /최적 라인/);
});
test('mission entry-band miss is an observation, not a real braking recommendation', () => {
  const result = D.analyze({record:{reason:'finish',offTime:0,time:5,entrySpeed:60},trace:trace(),scene:{gates:{entry:25}},mission:{entrySpeedRange:[32,45]}});
  assert.equal(result.certainty,'observed'); assert.equal(result.metrics.evidenceKind,'entryRange'); assert.equal(result.focusTime,2.5);
  assert.match(result.events[0].detail,/60.0 km\/h/); assert.match(result.explanation,/교육용 모델/); assert.match(result.explanation,/시간 손실을 판단하지/);
});
test('entry range is not inferred without a recorded crossing', () => {
  const result = D.analyze({record:{reason:'finish',offTime:0,time:5,entrySpeed:60},trace:trace(),scene:{gates:{entry:90}},mission:{entrySpeedRange:[32,45]}});
  assert.notEqual(result.metrics.evidenceKind,'entryRange');
});
test('later fast entry cannot become cause of an earlier incident', () => {
  const result = D.analyze({record:{...failure,entrySpeed:60},trace:trace(5,t=>({off:t>=2,utilF:t>=1?1.4:.7})),scene:{gates:{entry:30}},mission:{entrySpeedRange:[32,45]}});
  assert.equal(result.focusTime,2); assert(result.events.every(e=>e.t<=2));
});
test('no steering on a locally curved in-gate segment is an observed fact', () => {
  const result=D.analyze({record:{...failure,reason:'offtrack'},trace:trace(5,t=>({off:firstOff(t),steer:0})),scene:curveScene(-1)});
  assert.equal(result.certainty,'observed');assert.equal(result.metrics.evidenceKind,'noSteering');assert.equal(result.focusTime,4);
  assert.match(result.headline,/조향 입력이 거의/);assert.match(result.nextAction,/오른쪽 조향/);assert.match(result.explanation,/최적 조향량/);
  assert(result.events.every(e=>e.t<=4));
});
test('geometry signs select left steering without prescribing an optimal amount', () => {
  const result=D.analyze({record:failure,trace:trace(5,t=>({off:firstOff(t),steer:.01})),scene:curveScene(1)});
  assert.equal(result.metrics.curveDirection,'왼쪽');assert.match(result.nextAction,/왼쪽 조향/);
});
test('nearby direction changes do not invent a single steering direction', () => {
  const points=[{s:0,x:0,y:0},{s:20,x:20,y:0},{s:32,x:32,y:0},{s:36,x:35,y:3},{s:40,x:38,y:6},{s:44,x:41,y:3},{s:48,x:44,y:0},{s:60,x:56,y:0}];
  const result=D.analyze({record:failure,trace:trace(5,t=>({off:firstOff(t),steer:0})),scene:{gates:{entry:20,exit:55},points}});
  assert.equal(result.metrics.evidenceKind,'noSteering');assert.equal(result.metrics.curveDirection,'방향 전환');assert.match(result.nextAction,/코스가 휘는 방향/);
});
test('straight braking cannot be diagnosed as missing corner steering', () => {
  const scene={gates:{entry:20,exit:50},points:Array.from({length:31},(_,i)=>({s:i*2,x:i*2,y:0}))};
  const result=D.analyze({record:failure,trace:trace(5,t=>({off:firstOff(t),steer:0,brake:.6})),scene});
  assert.equal(result.certainty,'unknown');assert.notEqual(result.metrics.evidenceKind,'noSteering');
});
test('out-of-gate or missing geometry cannot imply missing corner steering', () => {
  const scene=curveScene();scene.gates.entry=42;
  for(const s of [scene,{gates:{entry:20,exit:51}},{...curveScene(),points:[{s:0,x:0,y:0},{s:NaN,x:3,y:4}]}]){
    assert.notEqual(D.analyze({record:failure,trace:trace(5,t=>({off:firstOff(t),steer:0})),scene:s}).metrics.evidenceKind,'noSteering');
  }
});
test('missing or short near-zero steering evidence is not a no-steering verdict', () => {
  for(const patch of [t=>({steer:undefined}),t=>({steer:t>=3.6?0:.2}),t=>({steer:t<3.5?0:.2}),t=>({steer:t>=3.8?undefined:0})]){
    const result=D.analyze({record:failure,trace:trace(5,t=>({off:firstOff(t),...patch(t)})),scene:curveScene()});
    assert.notEqual(result.metrics.evidenceKind,'noSteering');
  }
});
test('entry speed above range is recorded before failure without saturation evidence', () => {
  const result=D.analyze({record:{...failure,entrySpeed:65},trace:trace(6,t=>({off:t>=5})),scene:{gates:{entry:20,exit:55}},mission:{entrySpeedRange:[24,38]}});
  assert.equal(result.certainty,'observed');assert.equal(result.metrics.evidenceKind,'entryRange');assert.equal(result.focusTime,5);
  assert.equal(result.events[0].t,2);assert.match(result.explanation,/65.0 km\/h/);assert.match(result.explanation,/유일한 원인/);
});
test('no-steering observation retains prior high entry as a separate fact', () => {
  const result=D.analyze({record:{...failure,entrySpeed:65},trace:trace(5,t=>({off:firstOff(t),steer:0})),scene:curveScene(-1),mission:{entrySpeedRange:[24,38]}});
  assert.equal(result.metrics.evidenceKind,'noSteering');assert.equal(result.events.length,4);assert(result.events.some(e=>e.label==='과제 진입 속도 초과'));
});
test('manual end is not scored as a failed completion', () => {
  const result=D.analyze({record:{reason:'user',offTime:0,time:5},trace:trace()});
  assert.equal(result.certainty,'observed');assert.match(result.headline,/직접 종료/);assert.match(result.explanation,/완주 또는 과제 성공을 판정/);
});
test('automatic off-track end without a matching trace event stays unknown', () => {
  const result=D.analyze({record:{reason:'offtrack',offTime:0,time:5},trace:trace()});
  assert.equal(result.certainty,'unknown');assert.match(result.headline,/해당 순간의 기록이 없습니다/);
});
test('missing off channel cannot claim clean completion', () => {
  const result = D.analyze({record:{reason:'finish',offTime:0,time:5},trace:trace(5,()=>({off:undefined}))});
  assert.equal(result.certainty, 'unknown');
});
test('stopping is linked to actual brake evidence', () => {
  const result = D.analyze({record:{reason:'stop',offTime:0,time:5,stopDistance:42},trace:trace(5,t=>({brake:t>=1?.6:0,speed:Math.max(0,50-t*10)}))});
  assert.equal(result.certainty, 'observed'); assert.match(result.events.at(-1).detail, /42.0 m/);
  assert.equal(D.analyze({record:{reason:'stop',offTime:0},trace:trace()}).certainty, 'unknown');
});
test('timeout can observe prolonged stationary braking without inferring lap cause', () => {
  const result = D.analyze({record:{reason:'timeout',offTime:0,time:5},trace:trace(5,t=>t>=3?{speed:0,brake:.7,throttle:0}:{})});
  assert.equal(result.certainty, 'observed'); assert.match(result.headline, /제동한 채/);
});
test('first issue takes priority over a later spin', () => {
  const result = D.analyze({record:{reason:'spin',offTime:2,time:6},trace:trace(6,t=>({off:t>=2,...(t>=4?{beta:80,utilR:3,throttle:1}:{})}))});
  assert.equal(result.focusTime, 2); assert.equal(result.certainty,'unknown');
});
test('an initial off-track row cannot inherit later input causes', () => {
  const result = D.analyze({record:failure,trace:trace(5,()=>({off:true,utilF:2,brake:1}))});
  assert.equal(result.certainty,'unknown'); assert.equal(result.focusTime,0); assert.equal(result.events.length,1);
});
test('browser global has the same pure public API', () => {
  const context = {}; vm.createContext(context); vm.runInContext(fs.readFileSync('./dist/debrief.js','utf8'),context);
  assert.equal(typeof context.GripDebrief.analyze,'function'); assert.equal(context.GripDebrief.analyze().certainty,'unknown');
});
console.log('PASS: '+checks+' debrief checks (temporal evidence, missing channels, incident selection, confidence and browser export).');
