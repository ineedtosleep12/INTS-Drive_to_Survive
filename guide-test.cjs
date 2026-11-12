// Dependency-free data and event integration tests; parent app verifies browser layout.
'use strict';
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const assert=require('node:assert/strict');
const storage=new Map();
const ctx={localStorage:{getItem:key=>storage.get(key),setItem:(key,value)=>storage.set(key,value)},document:{getElementById:()=>false,createElement:()=>({}),head:{appendChild(){}}}};
vm.createContext(ctx);
vm.runInContext(fs.readFileSync(path.join(__dirname,'dist/guide.js'),'utf8'),ctx);
const G=ctx.GripGuide, data=G.data, key='intsGuideV1';
const plain=value=>JSON.parse(JSON.stringify(value));
assert.equal(data.lessons.length,24);
assert.equal(new Set(data.lessons.map(item=>item.id)).size,24);
assert.deepEqual(plain(data.categories.map(cat=>data.lessons.filter(l=>l.category===cat.id).length)),[4,8,4,6,2]);
assert.equal(data.plan.length,6);
assert.deepEqual(new Set(data.plan.flatMap(week=>plain(week.lessons))),new Set(data.lessons.map(l=>l.id)));
const presets=new Set(['corner','brake','slalom','hairpin','drift','figure8','monza','nurbgp','rally_stage','playground','touge']);
const vehicles=new Set(['gt3','rally1','ae86','hypercar','formula','touring_fwd','midship','rearengine']);
data.sources.forEach(source=>{assert.equal(new URL(source.url).protocol,'https:');assert(source.note);});
data.lessons.forEach((lesson,index)=>{
  assert(lesson.title && lesson.summary && lesson.practice && lesson.pass && lesson.scope);
  assert.equal(lesson.paragraphs.length,2);
  assert.equal(lesson.steps.length,3);
  assert.equal(lesson.signs.length,2);
  assert.equal(lesson.errors.length,2);
  assert(presets.has(lesson.preset));assert(vehicles.has(lesson.vehicle));
  lesson.prerequisites.forEach(id=>assert(data.lessons.findIndex(l=>l.id===id)>=0 && data.lessons.findIndex(l=>l.id===id)<index));
  assert(lesson.sourceIds.length);
  lesson.sourceIds.forEach(id=>assert(data.sources.some(source=>source.id===id)));
});
assert(data.lessons.find(l=>l.id==='clutch-handbrake').scope.includes('클러치킥을 재현하지 않습니다'));
assert(data.lessons.find(l=>l.id==='rally-handbrake').scope.includes('바퀴 잠김을 재현하지 않습니다'));
assert(data.lessons.find(l=>l.id==='long-run').scope.includes('타이어 온도'));
assert.deepEqual(plain(G.validateState({version:1,selected:'bad',read:['load-grip','load-grip','<script>',1],practiced:['donut',{},'unknown']})),{version:1,selected:'load-grip',read:['load-grip'],practiced:['donut']});
assert.equal(G.validateState({version:2,selected:'donut',read:['donut']}).read.length,0);
assert.equal(G.validateState(null).selected,'load-grip');

function element() {
  const handlers={},nodes={'[data-guide-list]':{innerHTML:''},'[data-guide-count]':{textContent:''},'[data-guide-selected-note]':{textContent:'',hidden:true}};
  const root={innerHTML:'',classList:{add(){}},contains:()=>true,querySelector:selector=>nodes[selector]||null,
    addEventListener:(name,handler)=>{assert(!handlers[name],`duplicate ${name}`);handlers[name]=handler;},
    removeEventListener:name=>{delete handlers[name];}};
  return {root,handlers,nodes,
    click:(action,extra={})=>handlers.click({target:{closest:()=>({dataset:{guide:action,...extra},disabled:false})}}),
    input:value=>handlers.input({target:{value,hasAttribute:name=>name==='data-guide-search'}}),
    category:value=>handlers.change({target:{value,hasAttribute:name=>name==='data-guide-category'}}),
    toggle:(which,open)=>handlers.toggle({target:{open,hasAttribute:name=>name===which}})};
}
const el=element();let launched=null;
const app=G.mount(el.root,{onLaunch:(preset,vehicle)=>{launched={preset,vehicle};}});
assert(el.root.innerHTML.includes('24 LESSONS'));
assert(el.root.innerHTML.includes('6주 연습 계획'));
assert(el.root.innerHTML.includes('상대와의 경기 결과'));
assert(el.root.innerHTML.includes('이 조건으로 개념 실험'));
assert(!el.root.innerHTML.includes('data-guide-plan open'));
el.toggle('data-guide-plan',true);el.click('read');
assert(el.root.innerHTML.includes('data-guide-plan open'));
assert.deepEqual(plain(app.getProgress()),{read:1,practiced:0,total:24});
el.click('read');assert.equal(app.getProgress().read,0);
el.click('practice');assert.equal(app.getProgress().practiced,1);
el.click('launch');assert.deepEqual(launched,{preset:'brake',vehicle:'gt3'});
assert.equal(app.getProgress().practiced,1,'launch does not complete lesson');
el.click('select',{id:'clutch-handbrake'});el.click('launch');assert.deepEqual(launched,{preset:'playground',vehicle:'ae86'});
assert(el.root.innerHTML.includes('클러치킥을 재현하지 않습니다'));
el.toggle('data-guide-sources',true);el.click('read');assert(el.root.innerHTML.includes('data-guide-sources open'));
el.click('select',{id:'formula-adaptation'});assert(!el.root.innerHTML.includes('data-guide-sources open'));
el.click('next');assert.equal(app.getState().selected,'formula-adaptation');
el.click('previous');assert.equal(app.getState().selected,'aero-release');
el.click('select',{id:'unknown'});assert.equal(app.getState().selected,'aero-release');
el.input('클러치');assert(el.nodes['[data-guide-list]'].innerHTML.includes('클러치킥'));
el.category('drift');assert(!el.nodes['[data-guide-list]'].innerHTML.includes('랠리 핸드브레이크'));
el.input('nothing matches this query');assert(el.nodes['[data-guide-list]'].innerHTML.includes('일치하는 수업이 없습니다'));
assert.equal(el.nodes['[data-guide-count]'].textContent,'0개 수업');
assert.equal(el.nodes['[data-guide-selected-note]'].hidden,false);
el.click('clear');assert(el.root.innerHTML.includes('24개 수업'));
el.input('\" autofocus onfocus=alert(1) <script>');el.click('read');
assert(!el.root.innerHTML.includes('value="" autofocus'));
assert(el.root.innerHTML.includes('&lt;script&gt;'));
const saved=JSON.parse(storage.get(key));assert.equal(saved.selected,'aero-release');
const app2=G.mount(el.root,{});assert.equal(app2.getState().selected,'aero-release');assert.equal(app2.getProgress().practiced,1);
el.click('reset');assert.equal(app2.getProgress().practiced,1);
el.click('cancel-reset');assert.equal(app2.getProgress().practiced,1);
el.click('reset');el.click('reset');assert.equal(app2.getProgress().practiced,0);assert.equal(app2.getProgress().read,0);
el.click('select',{id:'donut'});const copy=app2.getState();copy.read.push('donut');assert.equal(app2.getProgress().read,0,'returned state is a copy');
ctx.localStorage.setItem=()=>{throw new Error('blocked');};el.click('read');assert.equal(app2.getProgress().read,1);assert(el.root.innerHTML.includes('현재 화면을 사용하는 동안만'));
app2.dispose();assert.equal(Object.keys(el.handlers).length,0);
storage.set(key,'not valid json');const bad=element();G.mount(bad.root,{});assert(!bad.root.innerHTML.includes('현재 화면을 사용하는 동안만'),'invalid JSON does not mean storage is blocked');
ctx.localStorage.getItem=()=>{throw new Error('disabled');};const blocked=element(),blockedApp=G.mount(blocked.root,{});blocked.click('practice');assert.equal(blockedApp.getProgress().practiced,1);assert(blocked.root.innerHTML.includes('현재 화면을 사용하는 동안만'));
console.log('PASS guide: 24 complete lessons, prerequisite/source mappings, 6-week plan, scoped limits, search/filter, launch profiles, self-reported progress, persistence, reset, remount, malformed/blocked storage.');
