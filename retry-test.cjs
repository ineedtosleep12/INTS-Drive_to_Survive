'use strict';
const assert=require('node:assert/strict'),Physics=require('./dist/physics.js');
require('./dist/replay.js');
const Retry=require('./dist/retry.js');
const config={preset:'corner',missionId:'test_corner',vehicle:'gt3',surface:'dry',drive:'rwd',aids:'default',modelVersion:'v2',startSpeed:90,timeScale:1,input:'keyboard'};
const scene={id:'test_corner',preset:'corner',trainingId:'test_corner',name:'테스트 코너',total:400,width:12,gates:{entry:250,exit:350},points:[{x:0,y:0,s:0},{x:400,y:0,s:400}]};
const controls={steer:.14,throttle:.3,brake:.1,handbrake:0,clutch:0};
const physical=Physics.create({vehicle:'gt3',surface:'dry',speed:25});
for(let i=0;i<20;i++)Physics.step(physical,controls,1/60,{surface:'dry',drive:'rwd'});
const captured=Retry.capture(physical,controls,100,false),saved=JSON.stringify(captured);
physical.vx=1;controls.brake=.8;
assert.equal(JSON.stringify(captured),saved,'snapshot detached from mutable state and input');
assert.equal(captured.state.params,Physics.vehicles.gt3,'immutable profile may be shared while dynamic states are detached');
const restoredPlan={checkpoint:captured};
const restored=Retry.restore(restoredPlan),restoredAgain=Retry.restore(restoredPlan);
assert.equal(restored.state.time,0);assert.equal(restored.progress,100);assert.equal(restored.controls.brake,.1);
for(const key of ['vx','vy','yawRate','steer','_loadAx','_loadAy','_tcFactor','loadFL','fxR','beta'])assert.equal(restored.state[key],captured.state[key],key+' restored exactly');
const stepInput={steer:.1,throttle:.4,brake:0};
for(let i=0;i<60;i++){Physics.step(restored.state,stepInput,1/60);Physics.step(restoredAgain.state,stepInput,1/60);}
assert.deepEqual(restored.state,restoredAgain.state,'same checkpoint and inputs produce identical dynamics');
assert.equal(JSON.stringify(captured),saved,'restore and stepping do not mutate checkpoint');
restored.controls.brake=1;assert.equal(restoredAgain.controls.brake,.1);
const snapSample=Retry.sample(captured);assert.equal(snapSample.s,100);assert.equal(snapSample.speed,captured.state.speed*3.6);assert.equal(snapSample.wheelSteer,captured.state.steer);assert.equal(snapSample.steer,.14);
assert.equal(Retry.capture(null,{},0),null);assert.equal(Retry.restore(null),null);

function fixture({end=380,speed=90,offFrom=Infinity,brakeFrom=150,release=240,turnFrom=245,slip=3,front=.8,paused=false,missingOff=false}={}){
  const trace=[],snapshots=[];const ms=speed/3.6,step=ms*.05;
  for(let d=0;d<end+step*.5;d+=step){
    const s=Math.min(end,d),t=s/ms,brake=s>=brakeFrom&&s<release?.8:0,steer=s>=turnFrom?.25:0;
    const state=Physics.create({vehicle:'gt3',surface:'dry',speed:ms});Object.assign(state,{x:s,y:0,time:t,heading:0,beta:slip*Math.PI/180,utilF:front,utilR:.7,steer:steer*.47});
    const cp=Retry.capture(state,{steer,brake,throttle:brake?0:.3,handbrake:0,clutch:0},s,s>=offFrom);
    const p=Retry.sample(cp);if(missingOff)delete p.off;trace.push(p);snapshots.push(cp);if(s===end)break;
  }
  if(trace.at(-1).s<end){const last=trace.at(-1);trace.push({...last,s:end,x:end,t:end/ms});}
  return {trace,snapshots,paused};
}
const original=fixture({end:290,offFrom:270,slip:10,front:1.2});
const plan=Retry.plan({...original,scene,focusTime:10.8,config});
assert.ok(plan);assert.ok(plan.startS<150,'start before previous braking action');assert.equal(plan.entryS,250);assert.equal(plan.endS,350);assert.ok(plan.checkpoint.t<10.8);
assert.deepEqual(plan,Retry.plan({...original,scene,focusTime:10.8,config}),'checkpoint selection deterministic');
const planJSON=JSON.stringify(plan);const planState=Retry.restore(plan);planState.state.x=1e6;assert.equal(JSON.stringify(plan),planJSON,'repeat leaves stored plan fixed');
const alteredLoad=fixture({end:290,offFrom:270,slip:10,front:1.2});for(const snap of alteredLoad.snapshots)snap.state._loadAx+=.5;
const otherLoad=Retry.plan({...alteredLoad,scene,focusTime:10.8,config});assert.notEqual(otherLoad.conditionKey,plan.conditionKey,'internal load state participates in identity');
const otherConfig=Retry.plan({...original,scene,focusTime:10.8,config:{...config,surface:'wet'}});assert.notEqual(otherConfig.conditionKey,plan.conditionKey);
assert.equal(Retry.plan({...original,snapshots:original.snapshots.filter(s=>s.s>260),scene,focusTime:10.8,config}),null,'discarded old buffer cannot select late failed checkpoint');
assert.equal(Retry.plan({...original,snapshots:original.snapshots.filter(s=>s.s>145),scene,focusTime:10.8,config}),null,'buffer cannot silently start after intended pre-braking checkpoint');
assert.equal(Retry.plan({...original,scene:{...scene,freePractice:true},focusTime:10.8,config}),null);
assert.equal(Retry.plan({...original,scene:{...scene,trainingId:'intro_brake'},focusTime:10.8,config}),null);
assert.equal(Retry.plan({trace:[],snapshots:[],scene,focusTime:1,config}),null);
const short=fixture({end:30,brakeFrom:5}),shortScene={...scene,gates:{entry:25,exit:80},total:100};
assert.equal(Retry.plan({...short,scene:shortScene,focusTime:1,config}).startS,0,'short approach falls back to original start, not post-braking state');

const previous=Retry.summarize(plan,original.trace,'offtrack'),newFixture=fixture({end:380,brakeFrom:130,release:220,turnFrom:240,slip:3,front:.8}),current=Retry.summarize(plan,newFixture.trace,'finish');
assert.equal(previous.completed,false);assert.equal(previous.clean,false);assert.equal(current.completed,true);assert.equal(current.clean,true);assert.equal(current.coverageEndS,350);
const compared=Retry.compare(plan,previous,current);assert.equal(compared.available,true);assert.match(compared.headline,/통과했습니다/);
assert.equal(compared.commonFromS,plan.startS);assert.equal(compared.commonToS,290);
assert.ok(compared.items.some(i=>i.key==='offTime'&&i.assessment==='better'));assert.ok(compared.items.some(i=>i.key==='peakSlip'&&i.assessment==='changed'));
assert.ok(compared.items.some(i=>i.key==='brakeStartS'&&i.assessment==='changed'&&i.delta<0));assert.match(compared.items.find(i=>i.key==='brakeStartS').text,/앞당김/);
assert.ok(compared.items.some(i=>i.key==='steerAtEntry'));assert.equal(compared.items.some(i=>i.key==='elapsed'),false,'unfinished attempt is never ranked as a faster time');
assert.match(compared.note,/원인을 확정하지 않습니다/);
const goodEarlier=Retry.summarize(plan,fixture({end:380,speed:85}).trace,'finish'),goodLater=Retry.summarize(plan,fixture({end:380,speed:90}).trace,'finish');
const both=Retry.compare(plan,goodEarlier,goodLater);assert.equal(both.items.find(i=>i.key==='elapsed').assessment,'better');assert.ok(both.items.find(i=>i.key==='exitSpeed'));
const paused=Retry.summarize(plan,newFixture.trace,'finish',{paused:true});assert.equal(Retry.compare(plan,current,paused).items.some(i=>i.key==='elapsed'),false,'pause excludes time ranking');
assert.equal(Retry.compare(plan,{...previous,conditionKey:'different'},current).available,false);
assert.equal(Retry.compare(plan,Retry.summarize(plan,fixture({end:plan.startS+2}).trace,'user'),current).available,false,'less than 5m is insufficient');
const unknown=Retry.summarize(plan,fixture({end:380,missingOff:true}).trace,'finish');assert.equal(unknown.metrics.offTime,null);assert.equal(unknown.clean,false,'missing off-track signal is not a clean run');
const gaps=newFixture.trace.filter((p,i)=>i<90||i>110),gapped=Retry.summarize(plan,gaps,'finish');assert.equal(gapped.metrics.offTime,null);assert.equal(gapped.clean,false,'telemetry gap cannot silently count as clean');
const matchedA=Retry.summarize(plan,fixture({end:280,offFrom:270}).trace,'offtrack'),matchedB=Retry.summarize(plan,fixture({end:320,offFrom:270}).trace,'offtrack');
assert.equal(Retry.compare(plan,matchedA,matchedB).items.find(i=>i.key==='offTime').delta,0,'different finish positions use only common distance for off time');
const rotated=Retry.summarize(plan,fixture({end:280,slip:10,front:1.2}).trace,'user'),gentle=Retry.summarize(plan,fixture({end:280,slip:2,front:.7}).trace,'user'),neutral=Retry.compare(plan,rotated,gentle);
assert.equal(neutral.items.some(i=>i.assessment==='better'),false,'lower slip/load demand alone is not universal improvement');assert.doesNotMatch(neutral.headline,/개선|나아졌/);
const fullScene={...scene,trainingId:'whole_monza',total:1600,gates:{entry:200,exit:1500},turnInReferences:[{s:200,cornerName:'이전 코너'},{s:900,cornerName:'다음 코너'}]};
const nextApproach=fixture({end:820,offFrom:800,brakeFrom:760});
const upcomingPlan=Retry.plan({...nextApproach,scene:fullScene,focusTime:32,config});assert.equal(upcomingPlan.label,'다음 코너','next braking approach selects upcoming corner rather than the old exit');assert.equal(upcomingPlan.entryS,900);
console.log('retry: exact physical restores, safe checkpoint selection, bounded comparisons and incomplete-run fairness passed');
