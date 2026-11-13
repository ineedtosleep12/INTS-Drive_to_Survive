const assert=require('node:assert/strict');
const {parseLaps,stats,lapTime,validNote,validSession,parseNoteSpeeds,mount}=require('./dist/transfer.js');
const p=parseLaps('1:49.321\n1:49.550\n!1:52.100\n109.700');
assert.equal(p.errors.length,0);assert.equal(p.laps.length,4);assert.equal(p.laps[2].valid,false);
const s=stats(p.laps);assert.equal(s.valid,3);assert.equal(s.validRate,.75);assert.equal(s.median,109.55);assert(Math.abs(s.range-.379)<1e-10);assert.equal(s.fade,null);
assert.equal(parseLaps('1:61.000\n-5\n1:2:3\nNaN').errors.length,4);assert.equal(parseLaps('1:00\n! 61.1').errors.length,0);assert.equal(parseLaps(Array(201).fill('100').join('\n')).errors.length,1);
assert.equal(stats([{time:100,valid:false}]).median,null);assert.equal(stats([{time:100,valid:true}]).sd,null);assert.equal(stats([100,100,100,101,101,101].map(time=>({time,valid:true}))).fade,1);assert.equal(lapTime(109.321),'1:49.321');
const note={id:'a',at:new Date().toISOString(),trackId:'monza',vehicle:'gt3',game:'LMU',trackName:'Monza',car:'BMW',corner:'T1',reference:'Marker',gear:'2',conditions:'dry',warning:'',status:'hypothesis'};assert(validNote(note));assert(!validNote({...note,vehicle:'<img src=x>'}));assert(!validNote({...note,status:'official'}));
const session={id:'s',at:new Date().toISOString(),game:'LMU',car:'BMW',track:'Monza',conditions:'dry',focus:'release',review:'',incidents:0,laps:p.laps};assert(validSession(session));assert(!validSession({...session,laps:[{time:Infinity,valid:true}]}));
assert.deepEqual(parseNoteSpeeds({approachSpeed:'250',entrySpeed:'76.5',minSpeed:'',exitSpeed:'98'}),{speeds:{approachSpeed:250,entrySpeed:76.5,minSpeed:null,exitSpeed:98},errors:[]});
assert.deepEqual(parseNoteSpeeds({}).speeds,{approachSpeed:null,entrySpeed:null,minSpeed:null,exitSpeed:null},'missing measurements remain unknown, never estimated');
for(const bad of [-1,501,NaN,Infinity,'bad'])assert(parseNoteSpeeds({entrySpeed:bad}).errors.length);
for(const key of ['approachSpeed','entrySpeed','minSpeed','exitSpeed']){
  assert(validNote({...note,[key]:null}));assert(validNote({...note,[key]:0}));assert(validNote({...note,[key]:500}));
  for(const value of [-1,501,Infinity,'250',{}])assert(!validNote({...note,[key]:value}),'stored speed must be a bounded number or explicitly unknown');
}
assert(validNote({...note,evidence:'My replay at 00:43'}));assert(!validNote({...note,evidence:123}));

// Exercise the real form and storage boundary with a small DOM adapter.
const saved=new Map([['intsCornerNotesV1',JSON.stringify([note,{...note,id:'bad',entrySpeed:999}])]]),fields=new Map();
globalThis.localStorage={getItem:key=>saved.get(key)||null,setItem:(key,value)=>saved.set(key,value)};
globalThis.document={getElementById:()=>({})};
globalThis.GripTracks={get:id=>id==='custom'?null:{name:id==='monza'?'Monza GP':'Nürburgring GP',corners:[{name:'T1'}]}};
class Field{constructor(){this.value='';this.handlers={};this.innerHTML='';}addEventListener(name,fn){this.handlers[name]=fn;}}
const element={html:'',handlers:{},addEventListener(name,fn){this.handlers[name]=fn;},querySelector:id=>fields.get(id.slice(1)),contains:()=>true,
  set innerHTML(html){this.html=html;fields.clear();for(const match of html.matchAll(/id="([^"]+)"/g))fields.set(match[1],new Field());for(const match of html.matchAll(/<select id="([^"]+)">([\s\S]*?)<\/select>/g))fields.get(match[1]).value=match[2].match(/value="([^"]+)"/)?.[1]||'';},get innerHTML(){return this.html;}};
const mounted=mount(element);assert.equal(mounted.getData().notes.length,1,'legacy valid notes survive while corrupt speed values are rejected');
mounted.openNotes({trackId:'nurbgp',vehicle:'formula',corner:'T1'});
assert.equal(fields.get('tr-note-track').value,'nurbgp');assert.equal(fields.get('tr-note-vehicle').value,'formula');assert.equal(fields.get('tr-note-corner').value,'T1');assert.equal(fields.get('tr-note-trackname').value,'Nürburgring GP');
for(const [id,value] of Object.entries({'tr-note-car':'Formula test','tr-note-reference':'150 m board / exit curb','tr-note-conditions':'Game patch A / dry / 30 L','tr-note-evidence':'My replay 00:43','tr-note-approachSpeed':'250','tr-note-entrySpeed':'76.5','tr-note-minSpeed':'68','tr-note-exitSpeed':'-5'}))fields.get(id).value=value;
fields.get('tr-save-note').handlers.click();assert.equal(mounted.getData().notes.length,1);assert.match(fields.get('tr-error').textContent,/0–500/);
fields.get('tr-note-exitSpeed').value='98';fields.get('tr-save-note').handlers.click();
const current=mounted.getData().notes.at(-1);assert.equal(current.approachSpeed,250);assert.equal(current.entrySpeed,76.5);assert.equal(current.exitSpeed,98);assert.equal(current.evidence,'My replay 00:43');assert.equal(current.game,'LMU');assert.equal(current.conditions,'Game patch A / dry / 30 L');
assert(element.innerHTML.includes('250.0 km/h')&&element.innerHTML.includes('76.5 km/h'),'saved manual measurements appear in the corner card');
assert.equal(JSON.parse(saved.get('intsCornerNotesV1')).at(-1).minSpeed,68,'manual measurements persist with the notes');
mounted.openNotes({trackId:'invalid',vehicle:'invalid',corner:'<unsafe>'});assert.equal(fields.get('tr-note-track').value,'monza');assert.equal(fields.get('tr-note-vehicle').value,'gt3');assert.equal(fields.get('tr-note-corner').value,'<unsafe>','preset corner is assigned as input value rather than HTML');
console.log('PASS transfer: lap statistics, legacy note migration, optional measured corner speeds, invalid-value rejection, evidence storage, note cards and direct contextual navigation.');
