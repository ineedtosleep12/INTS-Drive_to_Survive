'use strict';
const assert=require('node:assert/strict');
require('./dist/render.js');
const R=globalThis.GripRenderer;
function drawing(width=640,height=480){
  const calls=[],strokes=[],texts=[],fills=[];
  let path=[];
  const finite=(args)=>{for(const a of args)if(typeof a==='number')assert(Number.isFinite(a),'all Canvas coordinates must be finite');};
  const ctx={
    setTransform(...a){finite(a);},clearRect(...a){finite(a);},
    beginPath(){path=[];},moveTo(x,y){finite([x,y]);path.push({x,y});},lineTo(x,y){finite([x,y]);path.push({x,y});},
    stroke(){strokes.push({points:path.slice(),color:this.strokeStyle,width:this.lineWidth,dash:this.dash?.slice()||[]});},
    fill(){},closePath(){},setLineDash(a){finite(a);this.dash=a;},measureText(t){return{width:String(t).length*6};},
    fillText(text,x,y){finite([x,y]);texts.push({text:String(text),x,y,color:this.fillStyle,font:this.font});},
    fillRect(...a){finite(a);fills.push({args:a,color:this.fillStyle});},strokeRect(...a){finite(a);},
    arc(...a){finite(a);},rect(...a){finite(a);},clip(){},save(){},restore(){},
    rotate(value){finite([value]);calls.push({kind:'rotate',value});},translate(x,y){finite([x,y]);calls.push({kind:'translate',x,y});}
  };
  const canvas={width:0,height:0,getBoundingClientRect:()=>({width,height}),getContext:()=>ctx};
  return{canvas,ctx,calls,strokes,texts,fills};
}
function straight(heading=0,total=350){
  const points=Array.from({length:71},(_,i)=>({x:120+Math.cos(heading)*total*i/70,y:-80+Math.sin(heading)*total*i/70,s:total*i/70}));
  return{points,total,width:12,startHeading:heading,preset:'test',landmarks:[],corners:[]};
}
function stateAt(scene,index=0){const p=scene.points[index];return{x:p.x,y:p.y,heading:scene.startHeading,speed:12,beta:0,steer:.1,params:{a:1.2,b:1.4,track:1.6}};}
for(const heading of [-Math.PI,-2.37,0,Math.PI/2,2.4]){
  const scene=straight(heading),before=JSON.stringify(scene),d=drawing();R.track(d.canvas,stateAt(scene),scene);
  const road=d.strokes.find(s=>s.color==='#26343e');assert(road,'road drawn');
  assert(Math.abs(road.points.at(-1).x-road.points[0].x)<1e-8,'initial tangent has no horizontal drift');
  assert(road.points.at(-1).y<road.points[0].y,'start moves bottom to top');
  assert(Math.abs(d.calls.find(c=>c.kind==='rotate').value+Math.PI/2)<1e-8,'car front initially points up');
  const headingArrow=d.strokes.find(s=>s.color==='#b5f36b'&&s.width===1.9);assert(headingArrow.points[1].y<headingArrow.points[0].y,'heading arrow matches the car');
  assert.equal(JSON.stringify(scene),before,'drawing cannot change scene geometry');
}
const bend={points:[{x:0,y:0,s:0},{x:100,y:0,s:100},{x:100,y:-100,s:200}],width:12,total:200,startHeading:0,preset:'test'};
const bendView=drawing();R.track(bendView.canvas,stateAt(bend),bend);const road=bendView.strokes.find(s=>s.color==='#26343e').points;
assert(road[1].y<road[0].y&&road[2].x>road[1].x,'a world right turn stays visually right after rotating start up');

const scene=straight();scene.brakingBoards=[200,150,100,50].map(distance=>({s:250-distance,distance,label:distance+' m',targetS:250,cornerName:'test'}));
const boards=drawing();R.track(boards.canvas,stateAt(scene),scene);
assert.deepEqual(boards.texts.filter(t=>['200','150','100','50'].includes(t.text)).map(t=>t.text),['200','150','100','50'],'all four legible signs in a short scene');
for(const sign of boards.texts.filter(t=>['200','150','100','50'].includes(t.text))){assert.equal(sign.color,'#111b1b');assert.match(sign.font,/bold 14px/);assert(sign.x>=0&&sign.x<=640&&sign.y>=0&&sign.y<=480);}
assert.equal(boards.fills.filter(f=>f.color==='#f0f3eb').length,4,'signs have high-contrast white boards');
const long={...scene,total:2000},overview=drawing();R.track(overview.canvas,stateAt(scene),long);assert(!overview.texts.some(t=>t.color==='#111b1b'),'long-course overview suppresses sign clutter');
const follow=drawing();R.track(follow.canvas,stateAt(scene,30),long,{follow:true,progress:150});assert(follow.texts.some(t=>t.color==='#111b1b'),'follow camera shows local boards');
const circuit=straight(0,500),wideFollow=drawing(900,455);R.track(wideFollow.canvas,{...stateAt(circuit),speed:250/3.6},circuit,{follow:true});
const wideScale=wideFollow.strokes.find(s=>s.color==='#26343e').width/circuit.width,verticalSpan=455/wideScale;
assert.equal(verticalSpan,340,'circuit follow view has a fixed 340 m vertical range');
assert(wideFollow.calls.find(c=>c.kind==='translate').y/wideScale>=250,'at least 250 m are visible ahead at the initial heading');
for(const speed of [0,5,70/3.6,250/3.6,350/3.6]){
  const speedView=drawing(900,455);R.track(speedView.canvas,{...stateAt(circuit),speed,beta:.4},circuit,{follow:true});
  assert.deepEqual(speedView.strokes.find(s=>s.color==='#26343e'),wideFollow.strokes.find(s=>s.color==='#26343e'),'speed and slip cannot change camera road size or position');
  assert.deepEqual(speedView.calls.filter(c=>c.kind==='translate'),wideFollow.calls.filter(c=>c.kind==='translate'),'car body and wheels keep a fixed screen size while braking');
}
const shortFollow=drawing();R.track(shortFollow.canvas,stateAt(scene),scene,{follow:true});
assert(shortFollow.texts.some(t=>t.text==='MAP'),'short guided sections also provide a separate minimap');
const smallReplay=drawing(168,210);R.track(smallReplay.canvas,stateAt(scene),scene,{follow:true,minimap:false,review:{t:3}});
assert(!smallReplay.texts.some(t=>t.text==='MAP'),'compact replay panes can omit their minimap to keep the car clear');
const customSpan={...circuit,cameraSpan:200},customView=drawing();R.track(customView.canvas,stateAt(customSpan),customSpan,{follow:true});
assert.equal(480/(customView.strokes.find(s=>s.color==='#26343e').width/customSpan.width),200,'scene span can be chosen before driving');

const player=stateAt(scene,30),reference={...player,x:player.x+3,y:player.y-2},own=drawing(),ref=drawing();
R.track(own.canvas,player,scene,{follow:true,cameraState:player,review:{t:4},reviewLabel:'내 주행'});
R.track(ref.canvas,reference,scene,{follow:true,cameraState:player,review:{t:8},reviewLabel:'시범',vehicleColor:'#6dd6eb'});
assert.deepEqual(own.strokes.find(s=>s.color==='#26343e').points,ref.strokes.find(s=>s.color==='#26343e').points,'comparison panes share camera, scale and road position');
assert(ref.texts.some(t=>t.text==='시범 · 8.00 s'));assert(own.texts.some(t=>t.text==='내 주행 · 4.00 s'));
assert(ref.fills.some(f=>f.color==='#6dd6eb')||ref.strokes.some(s=>s.color==='#6dd6eb'),'reference color used');

const guide=scene.points.slice(5,55).map((p,i)=>({...p,y:p.y+2,brake:i<15?.5:0,throttle:i>30?.7:0})),guideBefore=JSON.stringify(guide),guideView=drawing();
R.track(guideView.canvas,player,scene,{follow:true,referenceTrace:guide});
assert(guideView.texts.some(t=>t.text==='참고 주행선'),'guide is explicitly a reference, never a claimed fastest line');
for(const color of ['#ff907e','#6dd6eb','#b5f36b'])assert(guideView.strokes.some(s=>s.color===color&&s.width===2.6&&s.points.length>2),'guide separates brake, neutral and acceleration sections');
assert(guideView.strokes.some(s=>s.width===1.5&&s.points.length===5),'reference path has forward direction arrows');
const guideScale=guideView.strokes.find(s=>s.color==='#26343e').width/scene.width,guideStart=guideView.strokes.find(s=>s.color==='#ff907e'&&s.width===2.6).points[0],centerStart=guideView.strokes.find(s=>s.color==='#26343e').points[5];
assert(Math.abs(guideStart.x-centerStart.x+2*guideScale)<1e-8,'reference trajectory is transformed in the same world coordinates as the road');
assert.equal(JSON.stringify(guide),guideBefore,'reference rendering cannot modify recorded physics');
const hiddenGuide=drawing();R.track(hiddenGuide.canvas,player,scene,{follow:true,referenceTrace:guide,referenceLine:false});
assert(!hiddenGuide.texts.some(t=>t.text==='참고 주행선'),'reference overlay can be disabled independently');
const guideReplay=drawing();R.track(guideReplay.canvas,player,scene,{follow:true,referenceTrace:guide,cameraState:player,review:{t:4}});
assert.deepEqual(guideReplay.strokes.filter(s=>s.width===2.6&&s.points.length>2),guideView.strokes.filter(s=>s.width===2.6&&s.points.length>2),'replay and live views align the same guide trajectory');

const data=[{s:0,brake:1,steer:0},{s:25,brake:.7,steer:.5},{s:50,brake:0,steer:1},{s:75,brake:0,steer:-1},{s:100,brake:0,steer:0}],chart=drawing(640,230);
R.comparison(chart.canvas,data,data.map(s=>({...s,brake:s.brake*.5,steer:-s.steer})),{fromS:0,toS:100,progress:50});
assert(chart.texts.some(t=>t.text==='브레이크')&&chart.texts.some(t=>t.text==='조향 방향'));
assert(chart.texts.some(t=>t.text==='← 100%')&&chart.texts.some(t=>t.text==='→ 100%'),'signed steering is labeled');
assert(chart.strokes.some(s=>s.color==='#6dd6eb'&&s.dash.length),'reference is dashed');
assert(chart.strokes.some(s=>s.color==='#b5f36b'&&!s.dash.length),'user is solid');
const steering=chart.strokes.filter(s=>s.color==='#b5f36b'&&s.points.length===data.length).at(-1).points;
assert(steering[2].y<steering[0].y&&steering[3].y>steering[0].y,'positive left and negative right inputs are not absolute-valued');
assert.equal(chart.strokes.filter(s=>s.color==='#ff907e').length,2,'one shared distance cursor in each row');
const previousChart=drawing(640,230);R.comparison(previousChart.canvas,data,data,{playerLabel:'이번 시도',referenceLabel:'직전 시도'});
assert(previousChart.texts.some(t=>t.text==='이번 시도')&&previousChart.texts.some(t=>t.text==='직전 시도')&&!previousChart.texts.some(t=>t.text==='시범'),'previous attempts are not mislabeled as a reference demonstration');
const missing=drawing(640,230);R.comparison(missing.canvas,[{s:0,brake:0,steer:0},{s:20,brake:.4,steer:.4},null,{s:60,brake:NaN,steer:NaN},{s:80,brake:.1,steer:.1}],[],{fromS:0,toS:100});
assert(missing.strokes.filter(s=>s.color==='#b5f36b').every(s=>s.points.length<=2),'missing data is a gap rather than a fabricated bridge');
R.comparison(drawing().canvas,[],[]);R.track(drawing(1,1).canvas,stateAt(scene),scene);R.comparison(drawing(1,1).canvas,data,data);
console.log('PASS renderer: fixed scale across 0–350 km/h and replay, 250 m circuit lookahead, minimaps, physical reference line and arrows, start-up orientation, boards, signed comparisons and missing-data gaps.');
