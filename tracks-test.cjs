'use strict';
const assert=require('node:assert/strict');
require('./dist/tracks.js');
const tracks=globalThis.GripTracks;
assert.deepEqual(tracks.catalog.map(t=>t.id),['monza','nurbgp','rally_stage','playground','touge']);
assert.equal(tracks.get('missing'),null);
assert.equal(tracks.get('__proto__'),null);assert.equal(tracks.get('constructor'),null);
for(const {id} of tracks.catalog){
  const t=tracks.get(id),first=t.points[0],last=t.points.at(-1);
  assert.equal(first.x,0,id+' origin x');assert.equal(first.y,0,id+' origin y');assert.equal(first.s,0);
  let distance=0;
  for(let i=0;i<t.points.length;i++){
    const p=t.points[i];assert.ok([p.x,p.y,p.s].every(Number.isFinite),id+' finite geometry');
    if(i){const prev=t.points[i-1],ds=Math.hypot(p.x-prev.x,p.y-prev.y);assert.ok(ds>0&&ds<=8,id+' spacing');distance+=ds;assert.ok(p.s>prev.s,id+' increasing progress');assert.ok(Math.abs(p.s-distance)<1e-7,id+' cumulative distance');}
  }
  assert.ok(Math.abs(last.s-t.total)<1e-7,id+' exact total');
  assert.ok(t.total>100);assert.ok(Number.isFinite(t.startHeading));
  if(t.closed){assert.equal(last.x,first.x);assert.equal(last.y,first.y);assert.ok(Math.abs(t.startHeading)<1e-8,id+' straight spawn');}
  for(const c of t.corners)assert.ok(c.name&&c.s>0&&c.s<t.total,id+' corner lies on route');
  assert.ok(Array.isArray(t.brakingBoards)&&Array.isArray(t.turnInReferences));
  if(t.type!=='circuit'){assert.deepEqual(t.brakingBoards,[]);assert.deepEqual(t.turnInReferences,[]);}
  const again=tracks.get(id);t.points[0].x=999;t.source.notes='changed';t.corners.push({});
  assert.equal(again.points[0].x,0);assert.notEqual(again.source.notes,'changed');assert.notEqual(again.corners.length,t.corners.length);
  if(t.brakingBoards.length){t.brakingBoards[0].label='changed';t.turnInReferences[0].s=-1;assert.notEqual(again.brakingBoards[0].label,'changed');assert.ok(again.turnInReferences[0].s>0);}
}
function at(points,s){const i=points.findIndex(p=>p.s>=s),b=points[i],a=points[Math.max(0,i-1)],f=(s-a.s)/(b.s-a.s||1);return{x:a.x+(b.x-a.x)*f,y:a.y+(b.y-a.y)*f};}
function routeDistance(points,start,end){const route=[at(points,start),...points.filter(p=>p.s>start&&p.s<end),at(points,end)];return route.slice(1).reduce((n,p,i)=>n+Math.hypot(p.x-route[i].x,p.y-route[i].y),0);}
for(const [id,length,domain] of [['monza',5793,'www.monzanet.it'],['nurbgp',5148,'www.nuerburgring.de']]){
  const t=tracks.get(id);assert.ok(Math.abs(t.total-length)<1e-6);assert.equal(t.source.officialLengthM,length);assert.equal(t.source.approximation,true);assert.equal(new URL(t.source.url).hostname,domain);assert.equal(t.width,12);assert.equal(t.timeLimit,600);assert.ok(t.description.includes('공식 레이아웃 참고·평면 근사'));assert.match(t.source.notes,/고저차.*캠버.*제동/);
  assert.equal(t.brakingBoards.length,t.corners.length*4);assert.equal(t.turnInReferences.length,t.corners.length);
  assert.deepEqual(t.source.turnInReferences,t.turnInReferences);assert.match(t.brakingBoardNote,/회전 기준선까지 모델 거리/);assert.equal(t.source.brakingBoardNote,t.brakingBoardNote);assert.match(t.source.turnInReferenceNote,/모델 중심선.*실제/);
  for(const ref of t.turnInReferences){
    assert.equal(ref.modelOnly,true);assert.equal(ref.label,'회전 기준선');
    const c=t.corners.find(c=>c.id===ref.cornerId);assert.equal(ref.cornerName,c.name);assert.ok(ref.s<c.s&&ref.s>=200);
    assert.ok(Math.abs(c.s-ref.s-ref.offsetBeforeCornerM)<1e-7);
    const boards=t.brakingBoards.filter(b=>b.targetS===ref.s);
    assert.deepEqual(boards.map(b=>b.distance),[200,150,100,50]);
    for(const b of boards){
      assert.ok(b.s>=0&&b.s<b.targetS&&b.targetS<t.total);assert.equal(b.cornerName,ref.cornerName);assert.equal(b.label,b.distance+' m');
      assert.ok(Math.abs(b.targetS-b.s-b.distance)<1e-7);assert.ok(Math.abs(routeDistance(t.points,b.s,b.targetS)-b.distance)<1e-7,id+' boards measure along the route');
    }
  }
  for(let i=1;i<t.brakingBoards.length;i++)assert.ok(t.brakingBoards[i].s>=t.brakingBoards[i-1].s);
  assert.ok(t.brakingBoards.some(b=>{const a=at(t.points,b.s),c=at(t.points,b.targetS);return b.distance-Math.hypot(a.x-c.x,a.y-c.y)>1;}),id+' curved approaches cannot use straight-line distance');
  // A self-crossing original centerline would make nearest-road lap progress ambiguous.
  function side(a,b,c){return (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);}
  for(let i=1;i<t.points.length;i++)for(let j=i+2;j<t.points.length;j++){
    if(i===1&&j===t.points.length-1)continue;
    const a=t.points[i-1],b=t.points[i],c=t.points[j-1],d=t.points[j];
    assert.ok(!(side(a,b,c)*side(a,b,d)<-1e-7&&side(c,d,a)*side(c,d,b)<-1e-7),id+' route must not cross itself');
  }
}
assert.equal(tracks.get('rally_stage').total.toFixed(0),'680');
assert.equal(tracks.get('touge').total.toFixed(0),'780');
assert.equal(tracks.get('rally_stage').width,10);assert.equal(tracks.get('touge').width,8);
assert.equal(tracks.get('playground').width,80);assert.equal(tracks.get('playground').closed,false);assert.equal(tracks.get('playground').freePractice,true);assert.equal(tracks.get('playground').timeLimit,90);
console.log('Track checks passed: 5 fresh scenes, measured model-distance boards, official-length scaling, spacing, closure, source limits, no circuit crossings.');
