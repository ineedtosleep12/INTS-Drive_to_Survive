'use strict';
const assert=require('node:assert/strict');
require('./dist/tracks.js');
require('./dist/training.js');
const T=globalThis.GripTraining;
const ids=['intro_brake','corner_release','monza_t1','monza_lesmo','nurb_t1','rally_hairpin','drift_circle_intro','whole_monza'];
assert.deepEqual(T.missions.map(m=>m.id),ids);
assert.equal(T.get('missing'),null);assert.equal(T.scene('__proto__'),null);assert.equal(T.evaluate('constructor',{}),null);
assert.ok(Object.isFrozen(T.missions)&&Object.isFrozen(T.get('intro_brake').steps));
for(const m of T.missions){
  for(const key of ['id','title','shortGoal','why','vehicle','preset','inputHint','successText','focusMetric','nextId','category','targetNote','recordLabel','metricKey'])assert.ok(m[key],m.id+' '+key);
  assert.equal(m.steps.length,3);assert.equal(m.runningHints.length,3);assert.ok(T.get(m.nextId));
  const s=T.scene(m.id);assert.equal(s.trainingId,m.id);assert.match(s.description,/교육용.*실제 게임/);
  assert.ok(s.points.length>10&&s.width>0&&s.timeLimit>0);assert.equal(s.points[0].s,0);
  assert.ok(s.gates.entry>=0&&s.gates.entry<s.gates.exit&&s.gates.exit<s.total);
  assert.ok(s.landmarks.every(l=>l.s>=0&&l.s<=s.total&&['brake','turn','exit'].includes(l.type)));
  assert.ok(Array.isArray(s.brakingBoards)&&Array.isArray(s.turnInReferences));
  if(m.category!=='circuit'){assert.deepEqual(s.brakingBoards,[]);assert.deepEqual(s.turnInReferences,[]);}
  assert.equal(s.startHeading,Math.atan2(s.points[1].y-s.points[0].y,s.points[1].x-s.points[0].x));
  let length=0;
  for(let i=1;i<s.points.length;i++){
    const a=s.points[i-1],b=s.points[i],ds=Math.hypot(b.x-a.x,b.y-a.y);length+=ds;
    assert.ok(ds>0&&ds<7,m.id+' no repeated / long segments');assert.ok(Math.abs(b.s-length)<1e-7,m.id+' measured length');
  }
  assert.ok(Math.abs(length-s.total)<1e-7);
  const fresh=T.scene(m.id);s.points[0].x=1e9;s.landmarks[0].label='mutated';
  assert.notEqual(fresh.points[0].x,1e9);assert.notEqual(fresh.landmarks[0].label,'mutated');
  for(const bad of [undefined,{}, {reason:'finish'}, {reason:'finish',time:0,offTime:0}, {reason:'finish',time:10,offTime:NaN}])assert.equal(T.evaluate(m.id,bad).passed,false,m.id+' requires measured evidence');
}
function at(points,d){const i=points.findIndex(p=>p.s>=d),b=points[i],a=points[Math.max(0,i-1)],f=(d-a.s)/(b.s-a.s||1);return{x:a.x+(b.x-a.x)*f,y:a.y+(b.y-a.y)*f};}
for(const [id,track,corner,before,after,entry,exit] of [['monza_t1','monza','rettifilo',300,165,220,420],['monza_lesmo','monza','lesmo1',320,145,210,380],['nurb_t1','nurbgp','t1',300,170,225,415]]){
  const parent=GripTracks.get(track),c=parent.corners.find(c=>c.id===corner),s=T.scene(id);
  assert.equal(s.parentTrack,track);assert.equal(s.parentStartS,c.s-before);assert.equal(s.parentEndS,c.s+after);
  assert.equal(s.closed,false);assert.ok(Math.abs(s.total-before-after)<1e-7);assert.ok(s.corners.some(c=>c.id===corner),id+' includes the target corner');
  assert.equal(s.gates.entry,entry);assert.equal(s.gates.exit,exit);assert.equal(s.turnInReferences.length,1);
  const ref=s.turnInReferences[0],parentRef=parent.turnInReferences.find(ref=>ref.cornerId===corner);
  assert.equal(ref.s,entry);assert.equal(ref.cornerId,corner);assert.equal(ref.cornerName,c.name);assert.equal(ref.modelOnly,true);
  assert.ok(Math.abs(s.parentStartS+ref.s-parentRef.s)<1e-7,id+' same full-course turn reference');assert.deepEqual(s.source.turnInReferences,s.turnInReferences);
  assert.match(s.brakingBoardNote,/회전 기준선까지 모델 거리/);assert.equal(s.source.brakingBoardNote,s.brakingBoardNote);
  assert.deepEqual(s.brakingBoards.map(b=>b.distance),[200,150,100,50]);
  for(const b of s.brakingBoards){
    assert.equal(b.targetS,entry);assert.equal(b.cornerName,c.name);assert.equal(b.label,b.distance+' m');assert.ok(b.s>=0&&b.s<entry);assert.equal(entry-b.s,b.distance);
    const route=[at(s.points,b.s),...s.points.filter(p=>p.s>b.s&&p.s<entry),at(s.points,entry)];
    const distance=route.slice(1).reduce((n,p,i)=>n+Math.hypot(p.x-route[i].x,p.y-route[i].y),0);assert.ok(Math.abs(distance-b.distance)<1e-7,id+' along-route board distance');
  }
  assert.ok(s.landmarks.some(l=>l.s===entry&&l.label==='회전 기준선'));
  const fresh=T.scene(id);s.brakingBoards[0].label='changed';s.turnInReferences[0].s=-1;assert.notEqual(fresh.brakingBoards[0].label,'changed');assert.equal(fresh.turnInReferences[0].s,entry);
  for(const [p,d] of [[s.points[0],c.s-before],[s.points.at(-1),c.s+after]]){const expected=at(parent.points,d);assert.ok(Math.hypot(p.x-expected.x,p.y-expected.y)<1e-7,id+' exact parent boundary');}
  // Off-origin starts must face the local tangent, never the vector from world origin.
  assert.notEqual(s.startHeading,Math.atan2(s.points[1].y,s.points[1].x));
}
assert.ok(T.scene('monza_lesmo').corners.some(c=>c.id==='roggia'),'longer Lesmo approach retains upstream road');assert.match(T.get('monza_lesmo').why,/로지아/);
const circle=T.scene('drift_circle_intro');assert.equal(circle.closed,false);assert.equal(circle.freePractice,false);assert.equal(circle.preset,'drift_circle_intro');
assert.ok(Math.hypot(circle.points.at(-1).x-circle.points[0].x,circle.points.at(-1).y-circle.points[0].y)>10,'open arc avoids spawn/end overlap');
for(const p of circle.points)assert.ok(Math.abs(Math.hypot(p.x-circle.circleCenter.x,p.y-circle.circleCenter.y)-circle.circleRadius)<1e-8);
const full=T.scene('whole_monza');assert.equal(full.closed,true);assert.equal(full.total,GripTracks.get('monza').total);
assert.deepEqual(full.brakingBoards,GripTracks.get('monza').brakingBoards);assert.equal(full.gates.entry,full.turnInReferences[0].s);
for(const ref of full.turnInReferences)assert.ok(full.landmarks.some(l=>l.type==='turn'&&l.s===ref.s&&l.label.includes('회전 기준선')));
const base={reason:'finish',time:25,offTime:0,entrySpeed:35,exitSpeed:55,minSpeed:25,endSpeed:0,maxBeta:6,trainingMetrics:{brakeTime:1,brakeAtEntry:.1,overlapTime:.1,travelDistance:160,headingTurnDeg:315,peakSpeed:33}};
const examples={intro_brake:{...base,reason:'stop'},corner_release:base,monza_t1:base,monza_lesmo:{...base,entrySpeed:45},nurb_t1:base,rally_hairpin:{...base,entrySpeed:23},drift_circle_intro:base,whole_monza:base};
for(const id of ids){
  const pass=T.evaluate(id,examples[id]);assert.equal(pass.passed,true,id+' valid measurable objective');assert.ok(pass.checks.every(c=>typeof c.pass==='boolean'&&c.label&&c.value));
  assert.equal(T.evaluate(id,{...examples[id],offTime:2}).passed,false,id+' measured road departure');
  assert.equal(T.evaluate(id,{...examples[id],reason:'spin'}).passed,false,id+' no success on spin');
  assert.equal(T.evaluate(id,{...examples[id],time:undefined}).passed,false,id+' unknown duration');
}
assert.equal(T.evaluate('intro_brake',{...examples.intro_brake,endSpeed:null}).passed,false);
assert.equal(T.evaluate('intro_brake',{...examples.intro_brake,trainingMetrics:{brakeTime:0}}).passed,false,'coasting to stop is not braking practice');
assert.equal(T.evaluate('corner_release',{...base,trainingMetrics:{brakeTime:1}}).passed,false,'unknown release cannot pass');
assert.equal(T.evaluate('corner_release',{...base,trainingMetrics:{...base.trainingMetrics,brakeAtEntry:.9}}).passed,false);
assert.equal(T.evaluate('monza_t1',{...base,entrySpeed:70}).passed,false);
assert.equal(T.evaluate('monza_lesmo',{...examples.monza_lesmo,exitSpeed:null}).passed,false);
assert.equal(T.evaluate('monza_lesmo',{...examples.monza_lesmo,exitSpeed:40}).passed,false);
assert.equal(T.evaluate('monza_lesmo',{...examples.monza_lesmo,trainingMetrics:{brakeTime:1,overlapTime:1}}).passed,false);
assert.equal(T.evaluate('rally_hairpin',{...examples.rally_hairpin,entrySpeed:35}).passed,false);
for(const key of ['travelDistance','headingTurnDeg','peakSpeed'])assert.equal(T.evaluate('drift_circle_intro',{...base,trainingMetrics:{...base.trainingMetrics,[key]:undefined}}).passed,false,'circle needs '+key);
assert.equal(T.evaluate('drift_circle_intro',{...base,trainingMetrics:{...base.trainingMetrics,headingTurnDeg:10}}).passed,false,'short movement is not a circle');
assert.equal(T.evaluate('drift_circle_intro',{...base,maxBeta:undefined}).passed,false,'unknown slip is not zero');
assert.equal(T.evaluate('drift_circle_intro',{...base,trainingMetrics:{...base.trainingMetrics,peakSpeed:50}}).passed,false);
assert.equal(T.evaluate('whole_monza',{reason:'finish',time:300,offTime:0}).passed,true,'whole lap evaluates completion only');
assert.equal(T.evaluate('whole_monza',{reason:'finish',time:NaN,offTime:0}).passed,false);
console.log('Training checks passed: 8 coherent missions, exact fresh segments and model-distance boards, local spawn headings, open circle, measured-only objectives and actionable failure checks.');
