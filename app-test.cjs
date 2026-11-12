// Dependency-free integration checks. They exercise UI state and real modules;
// a minimal DOM/Canvas adapter is NOT a substitute for browser/device QA.
'use strict';
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),assert=require('node:assert/strict');
const root=path.join(__dirname,'dist'),appSource=fs.readFileSync(path.join(root,'app.js'),'utf8'),html=fs.readFileSync(path.join(root,'index.html'),'utf8');
const Physics=require('./dist/physics.js');
function closedFixture(preset='monza'){
  const points=[];let s=0;
  const add=(x,y)=>{const last=points.at(-1);if(last)s+=Math.hypot(x-last.x,y-last.y);points.push({x,y,s});};
  for(let y=-50;y<=450;y+=10)add(100,y);
  for(let x=90;x>=-400;x-=10)add(x,450);
  for(let y=440;y>=-50;y-=10)add(-400,y);
  for(let x=-390;x<=100;x+=10)add(x,-50);
  return{preset,name:'CLOSED TEST ROUTE',points,total:s,width:12,startHeading:Math.PI/2,timeLimit:900,closed:true};
}
function pointAt(scene,distance){
  for(let i=1;i<scene.points.length;i++){const a=scene.points[i-1],b=scene.points[i];if(b.s>=distance){const f=(distance-a.s)/Math.max(1e-9,b.s-a.s);return{x:a.x+(b.x-a.x)*f,y:a.y+(b.y-a.y)*f};}}
  return scene.points.at(-1);
}
function harness(saved=[],options={}){
  const els=new Map(),frames=[],winEvents={},docEvents={},storage=new Map([['gripLabRunsV1',JSON.stringify(saved)]]),sim={last:null,calls:[],draws:0,scene:null};
  let strictDOM=false;
  const canvasContext=new Proxy({measureText:t=>({width:String(t).length*6})},{get:(o,k)=>k in o?o[k]:(...args)=>{for(const x of args)if(typeof x==='number')assert(Number.isFinite(x),'finite canvas '+k);sim.draws++;},set:(o,k,v)=>(o[k]=v,true)});
  class E{
    constructor(id='',tag='BUTTON'){this.id=id;this.tagName=tag.toUpperCase();this.value='';this.style={};this.dataset={};this.attrs={};this.events={};this.classList={toggle(){},add(){},remove(){}};this.hidden=false;this.disabled=false;this.checked=false;this.children={};this._html='';this.textContent='';this.open=false;this.width=0;this.height=0;}
    set innerHTML(value){this._html=String(value);register(this._html,this);}
    get innerHTML(){return this._html;}
    addEventListener(type,fn){(this.events[type]??=[]).push(fn);}
    removeEventListener(type,fn){this.events[type]=(this.events[type]||[]).filter(x=>x!==fn);}
    dispatchEvent(e){e.target??=this;e.preventDefault??=()=>{};(this.events[e.type]||[]).forEach(fn=>fn(e));this['on'+e.type]?.(e);return true;}
    click(){if(!this.disabled)this.dispatchEvent({type:'click',target:this});}
    querySelector(selector){if(selector.startsWith('#'))return els.get(selector.slice(1))||null;return this.children[selector]??=new E(selector);}
    querySelectorAll(){return[];}
    setAttribute(key,value){this.attrs[key]=value;}
    getAttribute(key){return this.attrs[key]??null;}
    hasAttribute(key){return key in this.attrs;}
    removeAttribute(key){delete this.attrs[key];}
    appendChild(child){if(child.id)els.set(child.id,child);return child;}
    contains(){return true;}
    closest(){return null;}
    getBoundingClientRect(){return{left:0,top:0,width:this.id==='grip'?300:this.id==='track'||this.id==='trace'?800:300,height:this.id==='grip'?145:this.id==='track'?455:this.id==='trace'?100:130};}
    getContext(){return canvasContext;}
    setPointerCapture(){}
    showModal(){this.open=true;}
    focus(){}
    scrollIntoView(){}
  }
  function register(markup,parent){
    for(const match of markup.matchAll(/<([a-z][a-z0-9-]*)\b([^>]*\bid="([^"]+)"[^>]*)>/gi)){
      const [,tag,attrs,id]=match,e=els.get(id)||new E(id,tag);e.tagName=tag.toUpperCase();e.value=/\bvalue="([^"]*)"/.exec(attrs)?.[1]??e.value;
      e.hidden=/\bhidden(?:\s|=|$)/.test(attrs);e.checked=/\bchecked(?:\s|=|$)/.test(attrs);
      for(const a of attrs.matchAll(/\bdata-([a-z-]+)="([^"]*)"/g))e.dataset[a[1].replace(/-([a-z])/g,(_,c)=>c.toUpperCase())]=a[2];
      els.set(id,e);if(parent)parent.children['#'+id]=e;
    }
    for(const m of markup.matchAll(/<select\b[^>]*id="([^"]+)"[^>]*>([\s\S]*?)<\/select>/gi)){
      const values=[...m[2].matchAll(/<option\b([^>]*)value="([^"]*)"[^>]*>/g)];
      const selected=values.find(v=>/\bselected/.test(v[0]))||values[0];
      if(selected&&els.has(m[1]))els.get(m[1]).value=selected[2];
    }
  }
  register(html);
  const el=id=>{if(!els.has(id)){if(strictDOM)return null;els.set(id,new E(id));}return els.get(id);};
  const page=['lab','practice','learn','guide','transfer','history'].map(name=>{const e=new E();e.dataset.page=name;return e;});
  const inputs=['keyboard','mouse','gamepad'].map(name=>{const e=new E();e.dataset.input=name;return e;});
  for(const [key,value] of Object.entries({preset:'corner',vehicle:'gt3',aids:'default',surface:'dry',drive:'rwd','start-speed':'70','time-scale':'1'})){el(key).value=value;el(key).tagName=key==='start-speed'?'INPUT':'SELECT';}
  el('show-path').checked=true;
  const context={
    console,Math,Date,Number,Set,Map,JSON,Array,Blob,structuredClone,Intl,
    URL:{createObjectURL:()=>'',revokeObjectURL(){}},Event:class{constructor(type){this.type=type;}},
    navigator:{getGamepads:()=>sim.pads||[]},localStorage:{getItem:key=>storage.get(key)??null,setItem:(key,value)=>storage.set(key,value)},
    location:{hash:''},setTimeout:()=>0,clearTimeout(){},requestAnimationFrame:fn=>frames.push(fn),devicePixelRatio:2,
    document:{getElementById:el,querySelectorAll:query=>query==='[data-page]'?page:query==='[data-input]'?inputs:[],addEventListener:(type,fn)=>(docEvents[type]??=[]).push(fn),createElement:tag=>new E('',tag),hidden:false,head:new E('head','head')},
    window:{history:{replaceState(a,b,url){context.location.hash=url;}},addEventListener:(type,fn)=>(winEvents[type]??=[]).push(fn),scrollTo(){}}
  };
  vm.createContext(context);
  let realPhysics=Physics,renderer={track(canvas,state,scene){sim.scene=scene;},grip(){},trace(){}};
  if(options.modules){
    strictDOM=true;
    const scripts=[...html.matchAll(/<script\s+src="([^"]+)"/g)].map(m=>m[1]).filter(x=>x!=='app.js');
    sim.pending=[];
    for(const script of scripts){const file=path.join(root,script);if(!fs.existsSync(file)){sim.pending.push(script);continue;}vm.runInContext(fs.readFileSync(file,'utf8'),context,{filename:script});}
    realPhysics=context.GripPhysics;renderer=context.GripRenderer;
  }else{
    context.GripLessons={mount(){}};context.GripGuide={mount(){}};context.GripTransfer={mount(){}};
    if(options.tracks)context.GripTracks=options.tracks;
  }
  context.GripPhysics={...realPhysics,create(opts){sim.createOptions={...opts};return sim.last=realPhysics.create(opts);},step(state,controls,dt,opts){sim.calls.push({controls:{...controls},dt,options:{...opts}});if(options.dynamics)realPhysics.step(state,controls,dt,opts);else state.time+=dt;sim.onstep?.(state,controls,dt,opts);}};
  context.GripRenderer={...renderer,track(canvas,state,scene,opts){sim.scene=scene;sim.renderOptions=opts;renderer.track(canvas,state,scene,opts);}};
  vm.runInContext(appSource,context,{filename:'app.js'});let now=1;
  const tick=(patch={},dt=50)=>{Object.assign(sim.last,patch);now+=dt;const frame=frames.shift();assert(frame,'animation frame scheduled');frame(now);};
  const navigate=name=>page.find(p=>p.dataset.page===name).click();
  const change=(id,value)=>{el(id).value=value;el(id).dispatchEvent({type:'change'});};
  const key=(type,code,target={tagName:'BODY'})=>{for(const fn of docEvents[type]||[])fn({code,target,repeat:false,preventDefault(){}});};
  const complete=()=>{el('play').click();tick();const scene=sim.scene;tick({...pointAt(scene,45),speed:30});tick({...pointAt(scene,75),speed:25});tick({...pointAt(scene,112),speed:20});tick({...pointAt(scene,scene.total),speed:20});};
  return{el,sim,context,tick,navigate,complete,change,key,frames,storage,fireWindow:type=>(winEvents[type]||[]).forEach(fn=>fn()),records:()=>JSON.parse(storage.get('gripLabRunsV1')||'[]'),input:name=>inputs.find(p=>p.dataset.input===name).click()};
}
// Existing repeat-session contract and deliberate invalid-input persistence checks.
const h=harness();assert.equal(h.el('play').textContent,'▶ 시작');h.navigate('practice');h.complete();
const good=h.records()[0];
assert.equal(good.valid,true);assert.equal(good.practice,true);assert.equal(good.minSpeed,90);
assert.equal(good.config.vehicle,'gt3');assert.equal(good.config.modelVersion,'v2');assert.equal(good.config.aids,'default');
assert.equal(good.sectors.length,3);assert(Math.abs(good.sectors.reduce((a,b)=>a+b)-good.time)<1e-8);assert.equal(h.el('attempt-count').textContent,1);
h.navigate('learn');h.navigate('practice');assert.equal(h.el('attempt-count').textContent,1);assert.equal(h.el('play').textContent,'▶ 한 번 더');
for(let i=0;i<4;i++)h.complete();
assert.equal(h.el('attempt-count').textContent,5);const completed=h.records().length;
h.el('play').click();h.tick({x:0,y:0});assert.equal(h.records().length,completed);assert.equal(h.el('play').textContent,'세트 완료');
h.navigate('history');h.navigate('practice');assert.equal(h.el('attempt-count').textContent,5);
h.el('new-session').click();assert.equal(h.el('attempt-count').textContent,'0');assert(h.el('results').hidden);
h.el('play').click();h.tick();const before=h.sim.last.time;
h.el('model-details').click();h.tick();assert.equal(h.sim.last.time,before);assert(h.el('model-dialog').open);
h.el('model-dialog').open=false;h.el('play').click();h.tick({...pointAt(h.sim.scene,45),speed:30});h.navigate('learn');const pausedTime=h.sim.last.time;
h.navigate('practice');assert.equal(h.sim.last.time,pausedTime);assert.equal(h.el('play').textContent,'▶ 계속');
h.el('play').click();h.tick({...pointAt(h.sim.scene,75),speed:25});h.tick({...pointAt(h.sim.scene,112),speed:20});h.tick({...pointAt(h.sim.scene,h.sim.scene.total),speed:20});
assert.equal(h.records().at(-1).paused,true);assert(!h.el('results').innerHTML.includes('이전 최고 대비'));
h.change('surface','wet');assert.equal(h.el('attempt-count').textContent,'0');assert.equal(h.records().length,completed+1);
const bad=structuredClone(good);bad.config.timeScale='<img src=x onerror=alert(1)>';
const badVehicle=structuredClone(good);badVehicle.config.vehicle='<img src=x onerror=alert(1)>';
const badAid=structuredClone(good);badAid.config.aids='arbitrary';
const reloaded=harness([good,bad,badVehicle,badAid,{...good,config:null},{...good,maxBeta:null}]);reloaded.navigate('history');
assert(reloaded.el('history-content').innerHTML.includes('최근 1회'));assert(!reloaded.el('history-content').innerHTML.includes('<img'));
const legacy=structuredClone(good);delete legacy.config.vehicle;delete legacy.config.aids;delete legacy.config.modelVersion;
const migrated=harness([legacy]);migrated.complete();assert(!migrated.el('results').innerHTML.includes('이전 최고 대비'),'legacy model times must not compare with v2');
// Distances must measure traveled path, not distance from start.
const b=harness();b.change('preset','brake');b.el('play').click();b.tick();b.tick({x:10,y:0,speed:8,time:.5});b.tick({x:10,y:4,speed:5,time:.8});b.tick({x:14,y:4,speed:0,time:1.1});
assert.equal(b.records()[0].reason,'stop');assert(Math.abs(b.records()[0].stopDistance-18)<1e-8);
// A selected vehicle updates defaults and creates exactly that physics profile.
const v=harness();v.navigate('practice');v.complete();v.change('vehicle','ae86');
assert.equal(v.el('attempt-count').textContent,'0');assert.equal(v.sim.last.vehicle,'ae86');assert.equal(v.el('drive').value,'rwd');assert.equal(v.el('surface').value,'dry');assert.equal(Number(v.el('start-speed').value),Physics.vehicles.ae86.startingSpeedKph);
v.complete();assert(!v.el('results').innerHTML.includes('이전 최고 대비'),'vehicle records separated');
v.change('aids','off');v.complete();assert(!v.el('results').innerHTML.includes('이전 최고 대비'),'aid records separated');
v.complete();assert(v.el('results').innerHTML.includes('이전 최고 대비'),'identical configurations compare');
v.change('vehicle','rally1');assert.equal(v.sim.last.vehicle,'rally1');assert.equal(v.el('surface').value,'gravel');assert.equal(v.el('drive').value,'awd');assert.equal(v.el('aids').value,'default');
for(const id of Object.keys(Physics.vehicles)){v.change('vehicle',id);assert.equal(v.sim.last.vehicle,id);assert(v.el('vehicle-title').textContent.includes(Physics.vehicles[id].label));}
// Held keys reach the simulation; key release, blur, and modal pause release them.
const k=harness();k.change('aids','off');k.el('play').click();k.tick();k.key('keydown','KeyE');k.key('keydown','KeyQ');k.tick();
assert.equal(k.sim.calls.at(-1).controls.handbrake,1);assert.equal(k.sim.calls.at(-1).controls.clutch,1);assert.equal(k.sim.calls.at(-1).options.aids,'off');
k.key('keyup','KeyE');k.key('keyup','KeyQ');k.tick();assert.equal(k.sim.calls.at(-1).controls.handbrake,0);assert.equal(k.sim.calls.at(-1).controls.clutch,0);
k.key('keydown','KeyE',{tagName:'INPUT'});k.tick();assert.equal(k.sim.calls.at(-1).controls.handbrake,0,'typing must not drive');
k.key('keydown','KeyW');k.fireWindow('blur');const callCount=k.sim.calls.length;k.tick();assert.equal(k.sim.calls.length,callCount,'blur pauses simulation');
k.el('play').click();k.tick();assert.equal(k.sim.calls.at(-1).controls.throttle,0,'blur clears held key');
// Mouse sliders and standard gamepad buttons share the same input contract.
const m=harness();m.input('mouse');m.el('handbrake-range').value='.7';m.el('handbrake-range').oninput();m.el('clutch-range').value='1';m.el('clutch-range').oninput();m.el('play').click();m.tick();m.tick();
assert.equal(m.sim.calls.at(-1).controls.handbrake,.7);assert.equal(m.sim.calls.at(-1).controls.clutch,1);
const pad=harness();pad.input('gamepad');pad.sim.pads=[{connected:true,mapping:'standard',axes:[.5],buttons:Array.from({length:8},(_,i)=>({value:i===1||i===4?1:i===7?.6:0}))}];pad.el('play').click();pad.tick();pad.tick();
assert.equal(pad.sim.calls.at(-1).controls.handbrake,1);assert.equal(pad.sim.calls.at(-1).controls.clutch,1);assert.equal(pad.sim.calls.at(-1).controls.throttle,.6);assert(pad.sim.calls.at(-1).controls.steer<0);
pad.sim.pads=[];pad.tick();assert.equal(pad.sim.calls.at(-1).controls.handbrake,0);assert.equal(pad.sim.calls.at(-1).controls.throttle,0);
// A closed long route begins at its authored coordinate/heading and cannot
// complete by being near the coincident finish line at the start.
const fixture=closedFixture(),long=harness([],{tracks:{get:id=>id==='monza'?fixture:null}});
long.change('preset','monza');assert.equal(long.sim.last.x,100);assert.equal(long.sim.last.y,-50);assert.equal(long.sim.last.heading,Math.PI/2);
long.el('play').click();long.tick();long.tick();assert.equal(long.records().length,0,'closed route must not finish at start');assert.equal(long.sim.renderOptions.follow,true);
for(const p of fixture.points.slice(1))long.tick({x:p.x,y:p.y,speed:30});
assert.equal(long.records().length,1);assert.equal(long.records()[0].reason,'finish');assert.equal(long.records()[0].valid,true);
const free=harness();free.change('preset','figure8');free.el('play').click();free.tick();free.tick();assert.equal(free.records().length,0,'figure8 must not finish at crossing');free.tick({time:91});assert.equal(free.records()[0].reason,'timeout');
// Real modules load together using IDs parsed from the actual HTML. No silent
// creation of missing app IDs is allowed in this smoke test.
const smoke=harness([],{modules:true,dynamics:true});smoke.tick();smoke.el('play').click();smoke.key('keydown','KeyW');for(let i=0;i<10;i++)smoke.tick();smoke.key('keyup','KeyW');
assert(smoke.sim.draws>100);assert(Number.isFinite(smoke.sim.last.x));assert(smoke.el('lessons-root').innerHTML.length>1000);
smoke.navigate('transfer');assert(smoke.el('transfer-root').innerHTML.length>1000);smoke.navigate('guide');
if(smoke.context.GripGuide)assert(smoke.el('guide-root').innerHTML.length>1000);
smoke.navigate('lab');smoke.el('reset').click();
if(smoke.context.GripTracks){
  for(const id of ['monza','nurbgp','rally_stage','playground','touge']){
    const route=smoke.context.GripTracks.get(id);assert(route&&route.points.length>2,id+' route exists');
    assert(route.points.every(p=>Number.isFinite(p.x)&&Number.isFinite(p.y)&&Number.isFinite(p.s)));assert(route.total>0);
    smoke.change('preset',id);assert.equal(smoke.sim.last.x,route.points[0].x);assert.equal(smoke.sim.last.y,route.points[0].y);smoke.el('play').click();smoke.tick();smoke.tick();assert.equal(smoke.records().length,0,id+' no instant finish');smoke.el('reset').click();
  }
}
console.log('PASS app: sessions, sectors, record validation, vehicle/aid comparison separation, all profile selectors, keyboard/mouse/gamepad handbrake & clutch, pause/release, closed-track full traversal, and real-module DOM/Canvas startup.');
if(smoke.sim.pending.length){console.log('PENDING optional module smoke: '+smoke.sim.pending.join(', '));if(process.env.APP_REQUIRE_COMPLETE==='1')throw new Error('Required modules missing: '+smoke.sim.pending.join(', '));}
else console.log('PASS all HTML script references present and loaded.');

