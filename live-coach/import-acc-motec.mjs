// ACC LD/LDX time-series extraction and optional view-only INTS graph wrapper.
// These exports have no native course position or world line; the optional
// speed-integral axis is explicitly excluded from distance/live comparisons.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {pathToFileURL} from 'node:url';
import {readLd} from './import-motec.mjs';

const assert=(ok,message)=>{if(!ok)throw new Error(message);};
const rounded=value=>Math.round(value*1e8)/1e8;
function channel(recording,name,unit) {
  const result=recording.channels.get(name);
  assert(result?.values?.length>1,`Missing decoded ${name} channel`);
  assert(result.unit===unit,`${name} must use ${unit}`);
  return result;
}
function sampleAt(c,time) {
  const pos=time*c.hz,i=Math.floor(pos),fraction=pos-i;
  assert(i>=0&&i+1<c.count,'Lap lies outside channel coverage');
  return c.values[i]*(1-fraction)+c.values[i+1]*fraction;
}
export function beaconsFromLdx(xml) {
  assert(typeof xml==='string'&&xml.length<2*1024*1024&&!/<!DOCTYPE|<!ENTITY/i.test(xml),'Unsupported LDX document');
  const group=xml.match(/<MarkerGroup\s+[^>]*Name="Beacons"[^>]*>([\s\S]*?)<\/MarkerGroup>/);
  assert(group,'LDX has no Beacons marker group');
  const result=Array.from(group[1].matchAll(/<Marker\s+[^>]*Time="([0-9.eE+\-]+)"[^>]*\/>/g),m=>Number(m[1])/1e6);
  assert(result.length>=2&&result.length<=10000&&result.every((t,i)=>Number.isFinite(t)&&t>=0&&(i===0||t>result[i-1])),'Invalid LDX beacon times');
  return result;
}
export function convertAcc(recording,ldx,options) {
  assert(options.game==='ACC'&&options.car&&options.sourceUrl&&options.author,'Explicit ACC source identity is required');
  assert(Number.isInteger(options.startBeaconIndex)&&options.startBeaconIndex>=0,'A zero-based start beacon index is required');
  const beacons=beaconsFromLdx(ldx),begin=beacons[options.startBeaconIndex],end=beacons[options.startBeaconIndex+1];
  assert(Number.isFinite(begin)&&Number.isFinite(end)&&end-begin>10&&end-begin<7200,'Select two valid consecutive beacon boundaries');
  assert(Number.isFinite(options.expectedLapTime)&&Math.abs(end-begin-options.expectedLapTime)<=.002,'Beacon interval does not match expected source lap');
  const speed=channel(recording,'SPEED','m/s'),brake=channel(recording,'BRAKE','%'),throttle=channel(recording,'THROTTLE','%'),steering=channel(recording,'STEERANGLE','deg');
  const hz=Math.min(60,speed.hz,brake.hz,throttle.hz,steering.hz);
  assert(hz>=5,'Input frequency too low for this extractor');
  const samples=[];
  // Preserve original samples inside observed boundaries; never synthesize line crossings.
  for(let i=Math.ceil(begin*hz);i/hz<end;i++) {
    const clock=i/hz;
    const sample={time:rounded(clock-begin),speedKph:rounded(sampleAt(speed,clock)*3.6),brake:rounded(sampleAt(brake,clock)/100),throttle:rounded(sampleAt(throttle,clock)/100),steeringDeg:rounded(sampleAt(steering,clock))};
    assert(sample.speedKph>=0&&sample.speedKph<=550&&sample.brake>=0&&sample.brake<=1&&sample.throttle>=0&&sample.throttle<=1&&Math.abs(sample.steeringDeg)<=2000,'Invalid ACC input values or units');
    samples.push(sample);
  }
  assert(samples.length>20&&samples.length<=100000,'Selected trace size is unsupported');
  return {recordingSchemaVersion:1,kind:'time-series-only',game:'ACC',car:options.car,track:recording.header.track,layout:options.layout??'unknown',gameVersion:options.gameVersion??'unknown',sourceUrl:options.sourceUrl,author:options.author,recordedAt:`${recording.header.date} ${recording.header.time} (timezone unknown)`,licence:options.licence??'Personal download only; redistribution permission unverified',validity:'unknown',recordedLapTimeSeconds:rounded(end-begin),distanceBasis:null,coordinateFrame:null,
    conditions:{...(options.conditions??{}),sourceCarClass:recording.header.carClass??'unknown',sourceSampleHz:String(hz),sourceBeginClockSeconds:String(begin),sourceEndClockSeconds:String(end),lapValidity:'No track-limit validity channel decoded; matching beacon interval confirms timing only'},
    provenance:`Original LD SHA256 ${recording.sha256}. Consecutive LDX beacon times interpreted as microseconds and matched to the explicitly requested lap time. Inputs use the LD time axis. Steering is recorded degrees, not normalized native steering. No course distance, GPS or world coordinate channel exists in the selected public export; speed has not been integrated or relabelled as course position. This file is not compatible with INTS live distance coaching. ${options.provenance??''}`,
    samples};
}
export function asViewOnlyReference(recording) {
  assert(recording.game==='ACC'&&recording.kind==='time-series-only'&&recording.samples?.length>20,'ACC time-series recording required');
  let distance=0,previous=null;
  const samples=recording.samples.map(s=>{
    if(previous) {
      assert(s.time>previous.time&&s.time-previous.time<1,'Time-series has a discontinuity');
      distance+=(previous.speedKph+s.speedKph)/7.2*(s.time-previous.time);
    }
    previous=s;
    return {time:s.time,distanceM:rounded(distance),speedKph:s.speedKph,brake:s.brake,throttle:s.throttle,steering:null,steeringDegrees:s.steeringDeg,x:null,z:null,pathLateralM:null};
  });
  return {schemaVersion:1,game:'ACC',car:recording.car,track:recording.track,layout:recording.layout,gameVersion:recording.gameVersion,trackLengthM:null,distanceBasis:'acc-integrated-travel-distance-m',coordinateFrame:'unknown',lateralPositionBasis:'unknown',sourceUrl:recording.sourceUrl,author:recording.author,recordedAt:recording.recordedAt,licence:recording.licence,validity:'unknown',complete:false,
    conditions:{...recording.conditions,recordedLapTimeSeconds:String(recording.recordedLapTimeSeconds),sourceTimeAxis:'LDX beacon interval, original LD sample clock',distanceLimit:'VIEW ONLY. Trapezoidal integral of recorded speed over time; travelled path distance, NOT native course position. Origin is first retained sample, not an invented start-line crossing. Live alignment and same-course time-loss comparisons are disabled.',sourceSteeringUnit:'STEERANGLE degrees retained in companion time-series recording. No guessed normalized steering conversion.',sourceNativePosition:'No course-position, GPS or world coordinate channel in this export'},
    provenance:recording.provenance+' View-only graph axis derives travelled distance by trapezoidal integration of recorded SPEED. It must never be relabelled as ACC normalized track position or LMU scoring distance.',samples};
}
export function main(argv) {
  assert(argv.length===4,'Usage: node import-acc-motec.mjs INPUT.ld INPUT.ldx OPTIONS.json OUTPUT.recording.json');
  const [input,markers,config,output]=argv;
  assert([input,markers,config].every(file=>path.resolve(file)!==path.resolve(output))&&!fs.existsSync(output),'Choose a new output path');
  assert(fs.statSync(input).size<=256*1024*1024&&fs.statSync(markers).size<=2*1024*1024&&fs.statSync(config).size<65536,'Input size limit exceeded');
  const raw=fs.readFileSync(input),xml=fs.readFileSync(markers,'utf8'),options=JSON.parse(fs.readFileSync(config,'utf8').replace(/^\uFEFF/,''));
  const recording=convertAcc(readLd(raw),xml,options);
  recording.conditions.sourceLdxSha256=crypto.createHash('sha256').update(xml).digest('hex');
  const data=options.outputFormat==='view-only-reference'?asViewOnlyReference(recording):recording;
  assert(options.outputFormat===undefined||['time-series-only','view-only-reference'].includes(options.outputFormat),'Unsupported output format');
  const companion=path.join(path.dirname(output),path.basename(output,'.json')+'.raw-recording.json');
  if(options.outputFormat==='view-only-reference')assert(!fs.existsSync(companion)&&[input,markers,config].every(p=>path.resolve(p)!==path.resolve(companion)),'Companion output already exists or would overwrite input');
  fs.writeFileSync(output,JSON.stringify(data,null,2),{flag:'wx'});
  if(options.outputFormat==='view-only-reference')fs.writeFileSync(companion,JSON.stringify(recording,null,2),{flag:'wx'});
  console.log(JSON.stringify({output,game:data.game,track:data.track,samples:data.samples.length,recordedLapTime:recording.recordedLapTimeSeconds,liveCourseComparison:false,companion:options.outputFormat==='view-only-reference'?companion:null}));
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href)try{main(process.argv.slice(2));}catch(error){console.error(error.message);process.exitCode=1;}
