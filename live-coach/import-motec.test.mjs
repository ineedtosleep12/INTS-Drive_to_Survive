import test from 'node:test';
import assert from 'node:assert/strict';
import {readLd,convert} from './import-motec.mjs';

test('LD fixed-point scale and shift are decoded; cyclic and out-of-bounds pointers rejected',()=>{
  const b=Buffer.alloc(1890); b.writeUInt32LE(0x40); b.writeUInt32LE(1762,8);
  const at=1762; b.writeUInt32LE(1886,at+8);b.writeUInt32LE(2,at+12);b.writeUInt16LE(3,at+18);b.writeUInt16LE(2,at+20);b.writeUInt16LE(10,at+22);b.writeInt16LE(50,at+24);b.writeInt16LE(2,at+26);b.writeInt16LE(1,at+28);b.writeInt16LE(3,at+30);b.write('Brake Pos',at+32);b.write('%',at+72);b.writeInt16LE(-25000,1886);b.writeInt16LE(25000,1888);
  assert.deepEqual(Array.from(readLd(b).channels.get('Brake Pos').values),[0,100]);
  b.writeUInt32LE(at,at+4);assert.throws(()=>readLd(b),/chain/);
  b.writeUInt32LE(0,at+4);b.writeUInt32LE(1890,at+8);assert.throws(()=>readLd(b),/outside/);
});

function syntheticRecording(){
  const channels=new Map(), count=61;
  const add=(name,unit,fn)=>channels.set(name,{name,unit,hz:1,count,values:Float64Array.from({length:count},(_,i)=>fn(i))});
  add('Lap Number','',i=>i<10?1:i<30?2:i<50?3:4);
  add('Lap Start Elapsed Time','s',i=>i<10?80:i<30?100:i<50?120:140);
  add('Lap Distance','m',i=>i<10?1000+i*100:((i-10)%20)*100);
  add('Last Laptime','s',()=>20);add('Best Laptime','s',()=>20);
  add('Ground Speed','km/h',()=>180);add('Brake Pos','%',i=>i%20<3?60:0);
  add('Throttle Pos','%',()=>80);add('Steering','%',()=>-15);add('Path Lateral','m',()=>2);
  return {header:{date:'01/01/2000',time:'00:00:00',track:'SYNTHETIC TEST',carLongName:'SYNTHETIC TEST'},channels,sha256:'synthetic'};
}
const options={game:'LMU',car:'synthetic',sourceUrl:'local:synthetic',author:'test',distanceBasis:'lmu-motec-lap-distance-m',lapNumber:2,expectedLapTime:20};
function fiveHzRecording(){
  const r=syntheticRecording();
  for(const c of r.channels.values()) {const original=c.values;c.values=Float64Array.from({length:(original.length-1)*5+1},(_,i)=>{
    const index=Math.floor(i/5),fraction=(i%5)/5;
    if(c.name==='Lap Distance'&&original[index+1]>=original[index])return original[index]+(original[index+1]-original[index])*fraction;
    return original[index];
  });c.hz=5;c.count=c.values.length;}
  return r;
}
test('only bounded leading pre-start negatives are omitted with real samples and origin preserved',()=>{
  const r=fiveHzRecording();r.channels.get('Lap Distance').values[50]=-0.9;
  const result=convert(r,options);
  assert.equal(result.samples.length,99);assert.equal(result.samples[0].distanceM,20);assert.equal(result.samples[0].time,0);
  assert.equal(result.samples[1].time,0.2);assert.equal(result.conditions.omittedPreStartSamples,'1');
  assert.match(result.provenance,/Omitted 1 leading/);assert.equal(result.complete,false);assert.equal(result.trackLengthM,null);
  assert.equal(result.conditions.recordedLapTimeSeconds,'20');
});
test('pre-start omission rejects excessive distance, excessive duration and negative interior samples',()=>{
  let r=fiveHzRecording();r.channels.get('Lap Distance').values[50]=-5.1;assert.throws(()=>convert(r,options),/omission bound/);
  r=fiveHzRecording();r.channels.get('Lap Distance').values.set([-3,-2,-1],50);assert.throws(()=>convert(r,options),/omission bound/);
  r=fiveHzRecording();r.channels.get('Lap Distance').values[60]=-1;assert.throws(()=>convert(r,options),/Invalid normalized sample/);
  r=fiveHzRecording();r.channels.get('Lap Distance').values.set([-1,-2],50);assert.throws(()=>convert(r,options),/backwards/);
});
test('sampled lap stays partial, raw distance and lateral preserved, no fake world coordinates',()=>{
  const result=convert(syntheticRecording(),options);
  assert.equal(result.complete,false);assert.equal(result.trackLengthM,null);assert.equal(result.samples.length,20);
  assert.equal(result.samples[0].distanceM,0);assert.equal(result.samples.at(-1).distanceM,1900);
  assert.equal(result.samples[0].pathLateralM,2);assert.equal(result.samples[0].x,null);assert.equal(result.samples[0].z,null);
  assert.equal(result.samples[0].steering,-0.15);assert.equal(result.conditions.recordedLapTimeSeconds,'20');
  assert.equal(result.lateralPositionBasis,'lmu-motec-path-lateral-m');assert.equal(result.validity,'unknown');
});
test('wrong units, wrong lap target, backwards distance and invalid game timing reject',()=>{
  let r=syntheticRecording();r.channels.get('Brake Pos').unit='bar';assert.throws(()=>convert(r,options),/unit/);
  assert.throws(()=>convert(syntheticRecording(),{...options,expectedLapTime:21}),/expected/);
  r=syntheticRecording();r.channels.get('Lap Distance').values[15]=300;assert.throws(()=>convert(r,options),/backwards/);
  r=syntheticRecording();r.channels.get('Last Laptime').values.fill(-1);assert.throws(()=>convert(r,options),/last-lap/);
  assert.throws(()=>convert(syntheticRecording(),{...options,distanceBasis:'distance-travelled-m'}),/basis/);
  assert.throws(()=>convert(syntheticRecording(),{...options,distanceBasis:'lmu-scoring-distance-m'}),/mapping is unverified/);
});

function binaryChannel(kind,size,values,{shift=0,mul=1,scale=1,dec=0}={}) {
  const meta=1762,data=1886,b=Buffer.alloc(data+size*values.length);
  b.writeUInt32LE(0x40);b.writeUInt32LE(meta,8);b.writeUInt32LE(data,meta+8);b.writeUInt32LE(values.length,meta+12);
  b.writeUInt16LE(kind,meta+18);b.writeUInt16LE(size,meta+20);b.writeUInt16LE(5,meta+22);
  b.writeInt16LE(shift,meta+24);b.writeInt16LE(mul,meta+26);b.writeInt16LE(scale,meta+28);b.writeInt16LE(dec,meta+30);b.write('test',meta+32);
  for(let i=0;i<values.length;i++) {
    if(kind===7&&size===4)b.writeFloatLE(values[i],data+i*size);
    else if(kind===8&&size===8)b.writeDoubleLE(values[i],data+i*size);
    else b.writeIntLE(values[i],data+i*size,size);
  }
  return b;
}
test('signed 8/16/32-bit and 32/64-bit floating samples decode with declared transforms',()=>{
  for(const [kind,size,raw] of [[3,1,[-128,127]],[0,2,[-32768,32767]],[5,4,[-2147483648,2147483647]],[7,4,[-2.5,4.25]],[8,8,[-2.125,4.0625]]]) {
    const result=readLd(binaryChannel(kind,size,raw,{shift:7,mul:3,scale:2,dec:1})).channels.get('test').values;
    raw.forEach((v,i)=>assert.ok(Math.abs(result[i]-(v/2*0.1*3+7))<1e-7));
  }
});
test('UTF-8 and single-byte venue names both preserve the accented game label',()=>{
  for (const encoding of ['utf8','latin1']) {
    const bytes=binaryChannel(3,2,[1,2]);
    bytes.write('Autódromo José Carlos Pace',350,64,encoding);
    assert.equal(readLd(bytes).header.track,'Autódromo José Carlos Pace');
  }
});
test('unsupported representation remains unknown; zero scale, missing data and nonfinite reject',()=>{
  const unknown=binaryChannel(99,2,[12,13]);assert.equal(readLd(unknown).channels.get('test').values,null);
  assert.throws(()=>readLd(binaryChannel(3,2,[1,2],{scale:0})),/metadata/);
  const truncated=binaryChannel(5,4,[1,2]);assert.throws(()=>readLd(truncated.subarray(0,truncated.length-1)),/outside/);
  assert.throws(()=>readLd(binaryChannel(7,4,[NaN,2])),/Non-finite/);
  const invalid=binaryChannel(3,2,[1,2]);invalid.writeUInt32LE(0,0);assert.throws(()=>readLd(invalid),/supported LD/);
});
test('5Hz distance aligns to corresponding 10Hz input samples on the file time axis',()=>{
  const r=syntheticRecording();
  function replace(name,hz,fn){const c=r.channels.get(name);c.hz=hz;c.count=61*hz;c.values=Float64Array.from({length:c.count},(_,i)=>fn(i/hz));}
  replace('Lap Distance',5,t=>t<10?1000+t*100:((t-10)%20)*100);
  replace('Ground Speed',10,t=>180+t);
  replace('Brake Pos',10,t=>t>=10.2&&t<10.3?60:0);
  const result=convert(r,options);
  assert.equal(result.samples.length,100);
  assert.deepEqual(result.samples.slice(0,3).map(s=>[s.time,s.distanceM,s.speedKph,s.brake]),[[0,0,190,0],[0.2,20,190.2,0.6],[0.4,40,190.4,0]]);
});
test('missing lateral channel does not invent lateral positions or basis',()=>{
  const r=syntheticRecording();r.channels.delete('Path Lateral');const result=convert(r,options);
  assert.equal(result.lateralPositionBasis,'unknown');assert.ok(result.samples.every(s=>!Object.hasOwn(s,'pathLateralM')));
});
