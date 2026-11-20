import test from 'node:test';
import assert from 'node:assert/strict';
import {beaconsFromLdx,convertAcc,asViewOnlyReference} from './import-acc-motec.mjs';
const ldx='<LDXFile><MarkerGroup Name="Beacons"><Marker Time="1e6"/><Marker Time="21e6"/><Marker Time="42e6"/></MarkerGroup></LDXFile>';
const options={game:'ACC',car:'SYNTHETIC',author:'SYNTHETIC',sourceUrl:'local:synthetic-test',startBeaconIndex:0,expectedLapTime:20};
function fixture(){const channels=new Map();for(const [name,unit,value] of [['SPEED','m/s',50],['BRAKE','%',30],['THROTTLE','%',70],['STEERANGLE','deg',-20]])channels.set(name,{name,unit,hz:60,count:3000,values:Float64Array.from({length:3000},()=>value)});return {channels,header:{track:'SYNTHETIC',date:'2000-01-01',time:'00:00:00'},sha256:'synthetic'};}
test('LDX beacon microseconds produce distinct measured lap intervals',()=>{assert.deepEqual(beaconsFromLdx(ldx),[1,21,42]);assert.throws(()=>beaconsFromLdx('<!DOCTYPE bad>'+ldx),/Unsupported/);assert.throws(()=>beaconsFromLdx(ldx.replace('42e6','20e6')),/Invalid/);});
test('ACC time-series retains degrees and actual samples without fake course position',()=>{const r=convertAcc(fixture(),ldx,options);assert.equal(r.samples.length,1200);assert.equal(r.recordedLapTimeSeconds,20);assert.equal(r.samples[0].speedKph,180);assert.equal(r.samples[0].steeringDeg,-20);assert.equal(r.samples[0].brake,.3);assert.equal(r.distanceBasis,null);assert.equal(r.coordinateFrame,null);assert.equal(r.samples[0].distanceM,undefined);assert.equal(r.schemaVersion,undefined);assert.equal(r.validity,'unknown');});
test('wrong game, mismatched lap, unknown units and missing channels fail closed',()=>{assert.throws(()=>convertAcc(fixture(),ldx,{...options,game:'LMU'}),/ACC/);assert.throws(()=>convertAcc(fixture(),ldx,{...options,expectedLapTime:21}),/match/);let r=fixture();r.channels.get('STEERANGLE').unit='%';assert.throws(()=>convertAcc(r,ldx,options),/deg/);r=fixture();r.channels.delete('SPEED');assert.throws(()=>convertAcc(r,ldx,options),/Missing/);});
test('view-only integration is travelled distance and never normalizes steering or fabricates track length',()=>{
 const raw=convertAcc(fixture(),ldx,options),r=asViewOnlyReference(raw);
 assert.equal(r.samples[0].steeringDegrees,-20);
 assert.equal(r.distanceBasis,'acc-integrated-travel-distance-m');assert.equal(r.trackLengthM,null);assert.equal(r.complete,false);assert.equal(r.samples[0].distanceM,0);assert.equal(r.samples[0].steering,null);assert.equal(raw.samples[0].steeringDeg,-20);assert.equal(r.samples[0].x,null);assert.ok(Math.abs(r.samples.at(-1).distanceM-raw.samples.at(-1).time*50)<1e-5);assert.match(r.conditions.distanceLimit,/NOT native course position/);
 raw.samples[20].time=raw.samples[19].time;assert.throws(()=>asViewOnlyReference(raw),/discontinuity/);
});
