// Dependency-free UI/state integration checks. Does not replace browser/Gamepad QA.
'use strict';
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync(path.join(__dirname,'dist/app.js'),'utf8');
function harness(saved=[]) {
  const els=new Map(),frames=[],winEvents={},docEvents={},storage=new Map([['gripLabRunsV1',JSON.stringify(saved)]]),sim={last:null};
  class E {
    constructor(id){this.id=id;this.value='';this.style={};this.dataset={};this.attrs={};this.events={};this.classList={toggle(){},add(){}};this.hidden=false;this.disabled=false;this.checked=false;this.tagName='BUTTON';this.children={};}
    addEventListener(type,fn){(this.events[type]??=[]).push(fn);}
    dispatchEvent(e){e.target??=this;(this.events[e.type]||[]).forEach(fn=>fn(e));return true;}
    click(){if(!this.disabled)this.onclick?.({target:this});}
    querySelector(selector){return this.children[selector]??=new E(selector);}
    setAttribute(key,value){this.attrs[key]=value;}
    removeAttribute(key){delete this.attrs[key];}
    getBoundingClientRect(){return{left:0,top:0,width:300,height:130};}
    setPointerCapture(){}
    showModal(){this.open=true;}
  }
  const el=id=>{if(!els.has(id))els.set(id,new E(id));return els.get(id);};
  const page=['lab','practice','learn','history'].map(name=>{const e=new E();e.dataset.page=name;return e;});
  const inputs=['keyboard','mouse','gamepad'].map(name=>{const e=new E();e.dataset.input=name;return e;});
  for(const [key,value] of Object.entries({'preset':'corner','surface':'dry','drive':'rwd','start-speed':'70','time-scale':'1'})){el(key).value=value;el(key).tagName='SELECT';}
  el('show-path').checked=true;
  const physics={
    surfaces:{dry:{label:'마른'},wet:{label:'젖은'},gravel:{label:'자갈'}},
    create(options){return sim.last={x:0,y:0,speed:options.speed,time:0,steer:0,beta:0,yawRate:0,alphaF:0,alphaR:0,utilF:0,utilR:0,loadFL:3000,loadFR:3000,loadRL:3000,loadRR:3000,ay:0};},
    step(state,controls,dt){state.time+=dt;sim.onstep?.(state,controls,dt);}
  };
  const context={
    console,Math,Date,Number,Set,JSON,Array,Blob,
    URL:{createObjectURL:()=>'',revokeObjectURL(){}},Event:class{constructor(type){this.type=type;}},
    GripPhysics:physics,GripLessons:{mount(){}},GripRenderer:{track(){},grip(){},trace(){}},navigator:{getGamepads:()=>[]},
    localStorage:{getItem:key=>storage.get(key),setItem:(key,value)=>storage.set(key,value)},location:{hash:''},
    setTimeout:()=>0,clearTimeout(){},requestAnimationFrame:fn=>frames.push(fn),
    document:{getElementById:el,querySelectorAll:query=>query==='[data-page]'?page:query==='[data-input]'?inputs:[],addEventListener:(type,fn)=>(docEvents[type]??=[]).push(fn),createElement:()=>new E(),hidden:false},
    window:{history:{replaceState(a,b,url){context.location.hash=url;}},addEventListener:(type,fn)=>(winEvents[type]??=[]).push(fn),scrollTo(){}}
  };
  vm.createContext(context);vm.runInContext(source,context);let now=1;
  const tick=(patch={},dt=50)=>{Object.assign(sim.last,patch);now+=dt;const frame=frames.shift();assert(frame,'frame scheduled');frame(now);};
  const navigate=name=>page.find(p=>p.dataset.page===name).click();
  const complete=()=>{el('play').click();tick();tick({x:45,y:0,speed:30});tick({x:75,y:4,speed:25});tick({x:92,y:33,speed:20});tick({x:92,y:98,speed:20});};
  return{el,sim,context,tick,navigate,complete,records:()=>JSON.parse(storage.get('gripLabRunsV1')),input:name=>inputs.find(p=>p.dataset.input===name).click()};
}
const h=harness();
assert.equal(h.el('play').textContent,'▶ 시작');h.navigate('practice');h.complete();
let records=h.records();
assert.equal(records.length,1);assert.equal(records[0].valid,true);assert.equal(records[0].practice,true);assert.equal(records[0].minSpeed,90);
assert.equal(records[0].sectors.length,3);assert(Math.abs(records[0].sectors.reduce((a,b)=>a+b)-records[0].time)<1e-8);assert.equal(h.el('attempt-count').textContent,1);
h.navigate('learn');h.navigate('practice');assert.equal(h.el('attempt-count').textContent,1);assert.equal(h.el('play').textContent,'▶ 한 번 더');
for(let i=0;i<4;i++)h.complete();
assert.equal(h.el('attempt-count').textContent,5);const completed=h.records().length;
h.el('play').click();h.tick({x:0,y:0});assert.equal(h.records().length,completed);assert.equal(h.el('play').textContent,'세트 완료');
h.navigate('history');h.navigate('practice');assert.equal(h.el('attempt-count').textContent,5);
h.el('new-session').click();assert.equal(h.el('attempt-count').textContent,'0');assert(h.el('results').hidden);
h.el('play').click();h.tick();const before=h.sim.last.time;
h.el('model-details').click();h.tick();assert.equal(h.sim.last.time,before);assert(h.el('model-dialog').open);
h.el('model-dialog').open=false;h.el('play').click();h.tick({x:45,y:0,speed:30});h.navigate('learn');const pausedTime=h.sim.last.time;
h.navigate('practice');assert.equal(h.sim.last.time,pausedTime);assert.equal(h.el('play').textContent,'▶ 계속');
h.el('play').click();h.tick({x:75,y:4,speed:25});h.tick({x:92,y:33,speed:20});h.tick({x:92,y:98,speed:20});
assert.equal(h.records().at(-1).paused,true);assert(!h.el('results').innerHTML.includes('이전 최고 대비'));
h.el('surface').value='wet';h.el('surface').dispatchEvent({type:'change'});assert.equal(h.el('attempt-count').textContent,'0');assert.equal(h.records().length,completed+1);
const good=records[0],bad=JSON.parse(JSON.stringify(good));bad.config.timeScale='<img src=x onerror=alert(1)>';
const reloaded=harness([good,bad,{...good,config:null},{...good,maxBeta:null}]);
reloaded.navigate('history');assert(reloaded.el('history-content').innerHTML.includes('최근 1회'));assert(!reloaded.el('history-content').innerHTML.includes('<img'));
const b=harness();b.el('preset').value='brake';b.el('preset').dispatchEvent({type:'change'});b.el('play').click();b.tick();
b.tick({x:10,y:0,speed:8,time:.5});b.tick({x:10,y:4,speed:5,time:.8});b.tick({x:14,y:4,speed:0,time:1.1});
assert.equal(b.records()[0].reason,'stop');assert(Math.abs(b.records()[0].stopDistance-18)<1e-8);
console.log('PASS app: boot, sectors, corner minimum speed, completion, five-run guard, new set, tab round-trip, modal pause, comparable runs, settings reset, corrupt storage, traveled stop distance.');
