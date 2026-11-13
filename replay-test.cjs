'use strict';
const assert=require('node:assert/strict');
const Physics=require('./dist/physics.js');
require('./dist/tracks.js');require('./dist/training.js');
const Replay=require('./dist/replay.js'),T=globalThis.GripTraining;
function close(a,b,eps=1e-9){assert.ok(Math.abs(a-b)<=eps,a+' ≈ '+b);}
const stopScene=T.scene('intro_brake'),stopMission=T.get('intro_brake');
const originalScene=JSON.stringify(stopScene),originalMission=JSON.stringify(stopMission);
const config={vehicle:'ae86',surface:'wet',drive:'fwd',aids:'off',startSpeed:44,timeScale:.5};
const a=Replay.generate(stopScene,stopMission,config),b=Replay.generate(stopScene,stopMission,config);
assert.deepEqual(a,b,'same config produces identical physical reference');
assert.equal(a.reason,'stop');assert.equal(a.complete,true);assert.ok(a.trace.at(-1).speed<.36);
assert.equal(a.config.vehicle,'ae86');assert.equal(a.config.surface,'wet');assert.equal(a.config.drive,'fwd');assert.equal(a.config.aids,'off');
close(a.trace[0].speed,44);assert.equal(a.trace[0].t,0);assert.equal(a.trace[0].x,stopScene.points[0].x);
assert.equal(a.trace[0].heading,stopScene.startHeading);assert.ok(a.trace.at(-1).t<stopScene.timeLimit);
assert.equal(JSON.stringify(stopScene),originalScene,'generation never edits scene');assert.equal(JSON.stringify(stopMission),originalMission);
assert.equal(config.timeScale,.5);assert.equal(config.startSpeed,44);
assert.deepEqual(a,Replay.generate(stopScene,stopMission,{...config,timeScale:1}),'slow motion changes playback, not physical reference');
const fast=Replay.generate(stopScene,stopMission,{...config,startSpeed:80});assert.ok(fast.trace.at(-1).s>a.trace.at(-1).s+5,'start speed affects stop distance');
assert.notDeepEqual(a.trace,Replay.generate(stopScene,stopMission,{...config,surface:'dry'}).trace,'surface affects dynamics');
const live=Physics.create({vehicle:'gt3',speed:20,surface:'dry'}),snapshot=JSON.stringify(live),previous={steer:.2,throttle:.3,brake:.1,handbrake:0,clutch:0};
const previousCopy={...previous};
const returned=Replay.controls(live,T.scene('corner_release'),T.get('corner_release'),0,previous,1/60);
assert.equal(JSON.stringify(live),snapshot);assert.deepEqual(previous,previousCopy);assert.notEqual(returned,previous);
assert.equal(Replay.generate(null,stopMission,config).reason,'invalid_scene');
assert.equal(Replay.generate({...stopScene,timeLimit:.2},stopMission,config).reason,'timeout');
assert.ok(Replay.generate({...stopScene,timeLimit:.2},stopMission,config).trace.at(-1).t<=.2+1e-9);
const bounded=Replay.generate({...stopScene,total:1e6,timeLimit:10000,points:[{x:0,y:0,s:0},{x:1e6,y:0,s:1e6}]},{id:'bounded_fixture',demoSpeedKph:1},{startSpeed:0});
assert.equal(bounded.reason,'timeout');assert.equal(bounded.complete,false);assert.ok(bounded.trace.at(-1).t<=900+1e-7);assert.ok(bounded.trace.length<=18002,'900s/20Hz sample bound');
for(let i=1;i<a.trace.length;i++){assert.ok(a.trace[i].t>a.trace[i-1].t);assert.ok(a.trace[i].s>=a.trace[i-1].s);assert.ok(a.trace[i].t-a.trace[i-1].t<=.05+1e-8);}
assert.equal(a.events.filter(e=>e.type==='brake_start').length,1);
assert.equal(a.events.filter(e=>e.type==='brake_release').length,0,'stopping while holding brake is not a release');
assert.ok(a.trace.every(p=>['t','s','x','y','heading','speed','steer','wheelSteer','beta','ax','ay','loadFL','capR'].every(k=>Number.isFinite(p[k]))));
const missing=Replay.atTime([{t:0,s:0,speed:20,brake:.5},{t:1,s:10,speed:30}],.5);
assert.equal(missing.brake,undefined,'missing metric does not become zero or a copied measurement');
const angles=[{t:0,s:0,heading:179*Math.PI/180,beta:179,alphaF:179*Math.PI/180,steer:.6,brake:1,off:false},{t:2,s:20,heading:-179*Math.PI/180,beta:-179,alphaF:-179*Math.PI/180,steer:-.6,brake:0,off:true}];
const mid=Replay.atTime(angles,1);close(Math.abs(mid.heading),Math.PI);close(Math.abs(mid.beta),180);close(Math.abs(mid.alphaF),Math.PI);close(mid.steer,0);close(mid.brake,.5);
assert.equal(Replay.atTime(angles,-1),null);assert.equal(Replay.atTime(angles,3),null);assert.equal(Replay.atTime([],0),null);
assert.equal(Replay.atTime([{t:1},{t:0}],.5),null);assert.equal(Replay.atTime(angles,NaN),null);
const stationary=[{t:0,s:0,speed:0},{t:2,s:0,speed:0},{t:4,s:20,speed:36},{t:7,s:20,speed:0},{t:9,s:40,speed:36}];
assert.equal(Replay.atDistance(stationary,0).t,2,'stationary prefix uses departure sample');
close(Replay.atDistance(stationary,10).t,3);close(Replay.atDistance(stationary,30).t,8);
assert.equal(Replay.atDistance(stationary,41),null);assert.equal(Replay.atDistance(stationary,-1),null);assert.equal(Replay.atDistance([],0),null);
assert.equal(Replay.atDistance([{s:10},{s:5}],7),null);assert.equal(Replay.atDistance([{t:0,s:0}],1),null);
const player=[{t:0,s:0,brake:1,steer:0},{t:2,s:10,brake:.8,steer:.4},{t:4,s:20,brake:0,steer:.5}];
const reference=[{t:0,s:0,brake:1,steer:0},{t:1,s:10,brake:0,steer:-.4},{t:2,s:20,brake:0,steer:-.5}];
const mine=Replay.atTime(player,2),sameDistance=Replay.atDistance(reference,mine.s),sameClock=Replay.atTime(reference,2);
assert.equal(sameDistance.t,1);assert.equal(sameDistance.s,mine.s);assert.notEqual(sameClock.s,mine.s,'same clock is not the same road location');
const compared=Replay.compare(mine,sameDistance,[{type:'brake_release',t:.8,s:8,label:'브레이크 놓기',detail:'test'}]);
assert.equal(compared.available,true);assert.match(compared.headline,/조향 방향이 다릅니다/);assert.match(compared.explanation,/왼쪽 40%.*오른쪽 40%/);assert.match(compared.explanation,/8.0 m/);
close(compared.differences.find(d=>d.key==='steer').delta,.8);assert.match(compared.caveat,/최적.*확정하지/);
assert.equal(Replay.compare(mine,sameClock,[]).available,false,'unaligned samples never compared');
assert.equal(Replay.compare(mine,null).available,false);assert.equal(Replay.compare({s:1},{s:1}).available,false);
assert.match(Replay.compare({...mine,steer:0},{...sameDistance,steer:0}).headline,/브레이크를 놓았습니다/);
assert.deepEqual(Replay.failureWindow([{t:1},{t:10}],2),{from:1,to:4,focus:2});
assert.deepEqual(Replay.failureWindow([{t:1},{t:10}],50),{from:6,to:10,focus:10});
assert.equal(Replay.failureWindow([],5),null);
const noisy=[];
for(let i=0;i<=50;i++){
  const t=i/20;
  noisy.push({t,s:t*10,brake:t<.3?(i%2?.25:0):t<.8?.5:t<1.1?.12:0,
    steer:t<.3?(i%2?.2:0):t<.9?.3:t<1.2?.06:t<1.8?-.3:0});
}
const detected=Replay.events(noisy),brakes=detected.filter(e=>e.type.startsWith('brake')),turns=detected.filter(e=>e.type==='turn_direction');
assert.deepEqual(brakes.map(e=>e.type),['brake_start','brake_release'],'noise is suppressed and brake hysteresis sustained');
assert.ok(brakes[0].t>=.25);assert.ok(brakes[1].t>=1.1);assert.deepEqual(turns.map(e=>e.direction),[1,-1,0],'signed sustained direction changes');
assert.ok(turns[0].t>=.25);assert.equal(Replay.events([]).length,0);
assert.deepEqual(Replay.events([{t:0,s:0,brake:.5,steer:.3},{t:1,s:10,brake:.5,steer:.3}]),[],'gaps do not prove sustained inputs');
// Measure the live-controller path independently at both reference and coarse
// display-frame steps. A completed route alone is not a successful example.
function measuredDemo(mission,dt){
  const scene=T.scene(mission.id),state=Physics.create({vehicle:mission.vehicle,surface:mission.surface,aids:mission.aids,speed:mission.startSpeedKph/3.6});
  state.x=scene.points[0].x;state.y=scene.points[0].y;state.heading=scene.startHeading;
  let input={steer:0,throttle:0,brake:0,handbrake:0,clutch:0},progress=0,reason='timeout',offTime=0,spinTime=0,maxBeta=0,entrySpeed=null,exitSpeed=null,minSpeed=Infinity;
  const tm={brakeTime:0,brakeAtEntry:null,overlapTime:0,turnBrakeTime:0,travelDistance:0,headingTurnDeg:0,peakSpeed:state.speed*3.6},trace=[];
  for(let tick=0;tick<Math.ceil(scene.timeLimit/dt);tick++){
    input=Replay.controls(state,scene,mission,progress,input,dt);
    const old={x:state.x,y:state.y,heading:state.heading},previous=progress;
    Physics.step(state,input,dt,{surface:mission.surface,drive:Physics.vehicles[mission.vehicle].drive,aids:mission.aids});
    let near={distance:Infinity,s:0};
    for(let i=1;i<scene.points.length;i++){
      const a=scene.points[i-1],b=scene.points[i];if(scene.total>700&&(b.s<progress-45||a.s>progress+100))continue;
      const dx=b.x-a.x,dy=b.y-a.y,l2=dx*dx+dy*dy,f=Math.max(0,Math.min(1,((state.x-a.x)*dx+(state.y-a.y)*dy)/l2));
      const distance=Math.hypot(state.x-a.x-f*dx,state.y-a.y-f*dy);if(distance<near.distance)near={distance,s:a.s+f*Math.sqrt(l2)};
    }
    progress=Math.max(progress,near.s);const off=near.distance>scene.width/2-1;
    offTime+=off?dt:0;maxBeta=Math.max(maxBeta,Math.abs(state.beta)*180/Math.PI);spinTime=Math.abs(state.beta)*180/Math.PI>65&&state.speed>3?spinTime+dt:0;
    tm.brakeTime+=input.brake>.1?dt:0;tm.overlapTime+=input.throttle>.1&&input.brake>.1?dt:0;tm.peakSpeed=Math.max(tm.peakSpeed,state.speed*3.6);
    tm.travelDistance+=Math.hypot(state.x-old.x,state.y-old.y);tm.headingTurnDeg+=Math.atan2(Math.sin(state.heading-old.heading),Math.cos(state.heading-old.heading))*180/Math.PI;
    if(entrySpeed===null&&previous<scene.gates.entry&&progress>=scene.gates.entry){entrySpeed=state.speed*3.6;tm.brakeAtEntry=input.brake;}
    if(exitSpeed===null&&previous<scene.gates.exit&&progress>=scene.gates.exit)exitSpeed=state.speed*3.6;
    if(progress>=scene.gates.entry&&progress<=scene.gates.exit){minSpeed=Math.min(minSpeed,state.speed*3.6);tm.turnBrakeTime+=input.brake>.2?dt:0;}
    trace.push({t:state.time,s:progress,speed:state.speed*3.6,brake:input.brake,steer:input.steer});
    if(mission.id==='intro_brake'&&state.speed<.1&&state.time>1){reason='stop';break;}
    if(progress>=scene.total-2&&near.distance<scene.width){reason='finish';break;}
    if(offTime>2){reason='offtrack';break;}if(spinTime>1){reason='spin';break;}if(near.distance>70){reason='far';break;}
  }
  return {scene,trace,events:Replay.events(trace),summary:{reason,time:state.time,offTime,maxBeta,entrySpeed,exitSpeed,minSpeed:Number.isFinite(minSpeed)?minSpeed:null,endSpeed:state.speed*3.6,trainingMetrics:tm}};
}
// Every mission uses its current scene, including adjusted circuit approaches.
for(const mission of T.missions){
  const scene=T.scene(mission.id),demo=Replay.generate(scene,mission,{vehicle:mission.vehicle,surface:mission.surface,aids:mission.aids,startSpeed:mission.startSpeedKph});
  assert.ok(demo.trace.length>2,mission.id+' trace');assert.ok(demo.trace.at(-1).t<=Math.min(scene.timeLimit,900)+1e-7,mission.id+' bounded duration');
  assert.ok(demo.complete,mission.id+' reference completes: '+demo.reason);
  assert.ok(demo.trace.every(p=>p.s>=0&&p.s<=scene.total+1e-7),mission.id+' bounded progress');
  assert.ok(demo.trace.length<=Math.ceil(scene.timeLimit*20)+10,mission.id+' bounded samples');
  if(mission.id==='whole_monza')assert.ok(demo.trace.at(-1).s>5000,'closed route does not jump from spawn to end');
  for(const dt of [1/60,.05]){
    const measured=measuredDemo(mission,dt),verdict=T.evaluate(mission.id,measured.summary);
    assert.ok(verdict.passed,mission.id+' '+dt+'s must meet measured criteria: '+JSON.stringify(verdict.checks.filter(c=>!c.pass)));
    if(dt===1/60){close(measured.summary.time,demo.trace.at(-1).t,1e-7);close(measured.trace.at(-1).s,demo.trace.at(-1).s,1e-7);}
    if(mission.entrySpeedRange){
      const entry=scene.gates.entry;
      const releases=measured.events.filter(e=>e.type==='brake_release'&&e.s>=Math.max(0,entry-100)&&e.s<entry);
      assert.ok(releases.length,mission.id+' '+dt+'s has a sustained brake release in the last 100 m before entry');
      const release=releases.at(-1),start=measured.events.find(e=>e.type==='brake_start'&&e.s>=Math.max(0,entry-(mission.category==='circuit'?250:100))&&e.s<release.s);
      assert.ok(start&&start.brake>=.18&&release.brake<=.08,mission.id+' actual significant braking precedes release');
      assert.ok(release.s>entry-35,mission.id+' release is near turn-in, not a whole-approach slow cruise');
    }
  }
}
// High-speed guided approaches must actually start at the requested speed and
// brake before the distance boards, rather than silently becoming low-speed laps.
const highScene=T.scene('monza_t1'),high=Replay.generate(highScene,T.get('monza_t1'));
close(high.trace[0].speed,250);assert.equal(T.get('nurb_t1').startSpeedKph,230);
const firstBraking=high.events.find(e=>e.type==='brake_start');
assert.ok(firstBraking&&highScene.gates.entry-firstBraking.s>=100&&highScene.gates.entry-firstBraking.s<=210,'sustained high-speed braking occurs in the approach, around the 150 m reference');
assert.ok(highScene.landmarks[0].s<firstBraking.s,'preparation cue precedes actual example braking');
assert.ok(firstBraking.t>=2.5,'high-speed start provides time to see the road before braking');
const nurbHigh=Replay.generate(T.scene('nurb_t1'),T.get('nurb_t1'));
assert.ok(nurbHigh.events.find(e=>e.type==='brake_start').t>=2.5,'Nurburgring high-speed start also provides a preparation runway');
// Every circuit corner region gets measured approach/entry/minimum/exit values;
// these are generated physical runs, not hard-coded real-world or fastest speeds.
for(const trackId of ['monza','nurbgp']){
  const scene=GripTracks.get(trackId);let gt3=null,ae86=null;
  for(const vehicle of Object.keys(Physics.vehicles)){
    const run=Replay.generate(scene,null,{vehicle,surface:'dry',startSpeed:vehicle==='gt3'?250:80});
    assert.ok(run.complete,trackId+' '+vehicle+' reference completes');
    assert.ok(run.trace.every(p=>!p.off),trackId+' '+vehicle+' stays within road');
    const guide=Replay.cornerGuide(scene,run);
    assert.equal(guide.length,scene.corners.length,trackId+' all named corner regions covered');
    assert.deepEqual(guide.map(r=>r.id),scene.corners.map(c=>c.id));
    for(const row of guide){
      assert.equal(row.available,true);assert.equal(row.modelOnly,true);assert.match(row.definition,/400 m.*120 m.*연결 코너/);assert.match(row.note,/실제 게임.*보정하지/);
      for(const key of ['approachSpeed','entrySpeed','minSpeed','exitSpeed'])assert.ok(Number.isFinite(row[key])&&row[key]>=0&&row[key]<=270,trackId+' '+vehicle+' '+row.id+' '+key);
      assert.ok(row.approachS<row.entryS&&row.entryS<row.exitS&&row.exitS<=scene.total);
      close(row.entrySpeed,Replay.atDistance(run.trace,row.entryS).speed);
      close(row.exitSpeed,Replay.atDistance(run.trace,row.exitS).speed);
      assert.ok(row.minSpeed<=row.entrySpeed&&row.minSpeed<=row.exitSpeed);
      assert.ok(row.brakeDistance===null||row.brakeDistance>=0&&row.brakeDistance<=400);
    }
    if(vehicle==='gt3')gt3=guide;if(vehicle==='ae86')ae86=guide;
  }
  assert.notDeepEqual(gt3.map(r=>r.approachSpeed),ae86.map(r=>r.approachSpeed),'different vehicle dynamics produce different measured guides');
  const unavailable=Replay.cornerGuide(scene,{complete:false,trace:high.trace,events:high.events});
  assert.ok(unavailable.every(r=>!r.available&&r.entrySpeed===null&&r.exitSpeed===null),'failed/incomplete examples never supply purported complete corner targets');
}
assert.deepEqual(Replay.cornerGuide(T.scene('rally_hairpin'),high),[],'circuit guide is not fabricated for unrelated paths');
const lesmoScene=T.scene('monza_lesmo'),lesmoGuide=Replay.cornerGuide(lesmoScene,Replay.generate(lesmoScene,T.get('monza_lesmo')));
assert.equal(lesmoGuide.find(r=>r.id==='roggia').available,false,'a clipped upstream corner without its entry reference is not presented as a measured full region');
assert.equal(lesmoGuide.find(r=>r.id==='lesmo1').exitS,lesmoScene.gates.exit,'individual mission guide uses the same exit as mission scoring');
console.log('Replay checks passed: all 8 measured objectives at 16.7/50ms, 250 km/h approach, 16 vehicle/circuit runs without departures, measured corner-region guides, deterministic isolation and distance-aligned comparisons.');
