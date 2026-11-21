// Bounded, dependency-free MoTeC LD reader. Format interoperability references:
// https://pages.tobi.lutke.com/racingmagick/motec_format
// https://github.com/snipem/ld-cli/blob/main/parse.go
// Independently implemented; no third-party parser code or recordings bundled.
// This deliberately does NOT turn GPS into game-world coordinates, integrate
// speed into track position, or fabricate finish-line samples.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { pathToFileURL } from 'node:url';

const MAX_BYTES = 256 * 1024 * 1024;
function assert(ok, message) { if (!ok) throw new Error(message); }
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const round = value => Math.round(value * 1e8) / 1e8;

export function readLd(bytes) {
  assert(Buffer.isBuffer(bytes) && bytes.length >= 1762 && bytes.length <= MAX_BYTES, 'LD file size invalid');
  const bounds = (offset, count) => assert(Number.isSafeInteger(offset) && Number.isSafeInteger(count) && offset >= 0 && count >= 0 && offset + count <= bytes.length, 'LD pointer or length outside file');
  const text = (offset, count) => { bounds(offset, count); return bytes.subarray(offset, offset + count).toString('latin1').split('\0')[0].trim(); };
  assert(bytes.readUInt32LE(0) === 0x40, 'Not a supported LD file');
  // LMU can write UTF-8 into the venue field. Older logs use single-byte
  // names. Decode UTF-8 only when it round-trips to the exact source bytes.
  const legacyTrack = text(350,64);
  const venueBytes = Buffer.from(legacyTrack,'latin1');
  const utf8Track = venueBytes.toString('utf8');
  const track = Buffer.from(utf8Track,'utf8').equals(venueBytes) ? utf8Track : legacyTrack;
  const header = { date: text(94,16), time: text(126,16), driver: text(158,64), car: text(222,64), track };
  const event = bytes.readUInt32LE(36);
  if(event) {
    bounds(event,1154); const venue = bytes.readUInt16LE(event+1152);
    if(venue) { bounds(venue,1100); const car = bytes.readUInt16LE(venue+1098);
      if(car) { bounds(car,260); header.carClass=text(car,64); header.carLongName=text(car+64,128); }
    }
  }
  const channels = new Map(), visited = new Set();
  let offset = bytes.readUInt32LE(8);
  while(offset) {
    assert(!visited.has(offset) && visited.size < 2048, 'Invalid channel chain'); visited.add(offset); bounds(offset,124);
    const name=text(offset+32,32), unit=text(offset+72,12), count=bytes.readUInt32LE(offset+12), dataOffset=bytes.readUInt32LE(offset+8);
    const kind=bytes.readUInt16LE(offset+18), size=bytes.readUInt16LE(offset+20), hz=bytes.readUInt16LE(offset+22);
    const shift=bytes.readInt16LE(offset+24), multiplier=bytes.readInt16LE(offset+26), scale=bytes.readInt16LE(offset+28), decimals=bytes.readInt16LE(offset+30);
    assert(name && !channels.has(name), 'Missing or duplicate channel name');
    assert(count<=10_000_000 && hz>0 && scale!==0 && Math.abs(decimals)<=15, 'Invalid channel metadata');
    const integer=[0,3,5].includes(kind)&&[1,2,4].includes(size);
    const floating=(kind===7&&size===4)||(kind===8&&size===8);
    // Unknown representations remain explicit metadata, never decoded as zeros.
    const channel={name,unit,hz,count,kind,size,values:null};
    if(integer||floating) {
      bounds(dataOffset,count*size); const values=new Float64Array(count);
      for(let i=0;i<count;i++) {
        const at=dataOffset+i*size;
        const raw=integer?bytes.readIntLE(at,size):size===4?bytes.readFloatLE(at):bytes.readDoubleLE(at);
        values[i]=raw/scale*Math.pow(10,-decimals)*multiplier+shift;
        assert(Number.isFinite(values[i]), `Non-finite ${name} sample`);
      }
      channel.values=values;
    }
    channels.set(name,channel); offset=bytes.readUInt32LE(offset+4);
  }
  return {header,channels,sha256:hash(bytes)};
}

function required(recording,name,unit) {
  const channel=recording.channels.get(name);
  assert(channel?.values?.length>1, `Missing supported channel: ${name}`);
  if(unit!==undefined)assert(channel.unit===unit,`${name}: expected unit ${unit}, got ${channel.unit}`);
  return channel;
}
function at(channel,time) {
  const pos=time*channel.hz, index=Math.floor(pos+1e-8), f=pos-index;
  assert(index>=0&&index<channel.count,'Channel does not cover selected lap');
  return channel.values[index]*(1-f)+(channel.values[Math.min(index+1,channel.count-1)])*f;
}
function transitions(channel) {
  const result=[];
  for(let i=1;i<channel.count;i++)if(channel.values[i]!==channel.values[i-1])result.push({time:i/channel.hz,value:channel.values[i]});
  return result;
}

export function convert(recording, options) {
  assert(options.game==='LMU', 'Only explicitly identified LMU recordings supported');
  assert(options.car&&options.sourceUrl&&options.author,'Car, sourceUrl and author are required');
  assert(options.distanceBasis==='lmu-motec-lap-distance-m','Raw MoTeC distance basis required; native scoring mapping is unverified');
  const number=required(recording,'Lap Number',''), start=required(recording,'Lap Start Elapsed Time','s');
  const distance=required(recording,'Lap Distance','m'), last=required(recording,'Last Laptime','s'), best=required(recording,'Best Laptime','s');
  const speed=required(recording,'Ground Speed','km/h'), brake=required(recording,'Brake Pos','%'), throttle=required(recording,'Throttle Pos','%'), steering=required(recording,'Steering','%');
  const epochs=transitions(number), selected=epochs.findIndex(e=>e.value===options.lapNumber);
  assert(selected>=0&&selected+1<epochs.length,'Selected lap must have both recorded boundaries');
  const begin=epochs[selected], end=epochs[selected+1];
  const scoredLapTime=at(start,end.time)-at(start,begin.time);
  assert(scoredLapTime>10&&scoredLapTime<7200,'Invalid lap-clock difference');
  if(options.expectedLapTime!==undefined)assert(Math.abs(scoredLapTime-options.expectedLapTime)<0.002,'Lap time does not match expected source');
  const scoredLast=at(last,end.time+0.2), scoredBest=at(best,end.time+0.2);
  assert(Math.abs(scoredLast-scoredLapTime)<0.002,'Game last-lap time does not match the chosen lap');
  // Scoring distance can reset before the faster telemetry lap-number epoch.
  // Select its own two nearby wraps; never mix the epochs across the finish.
  const wraps=[];
  for(let i=1;i<distance.count;i++)if(distance.values[i-1]-distance.values[i]>1000)wraps.push(i);
  const closest=time=>wraps.reduce((a,b)=>Math.abs(b/distance.hz-time)<Math.abs(a/distance.hz-time)?b:a,wraps[0]);
  const from=closest(begin.time), to=closest(end.time);
  assert(from!==undefined&&to>from&&Math.abs(from/distance.hz-begin.time)<0.5&&Math.abs(to/distance.hz-end.time)<0.5,'Scoring/telemetry lap epochs disagree');
  // Some LMU logs reset scoring distance a fraction before crossing the line.
  // Omit only a bounded leading pre-start span; never clamp it to an invented
  // zero-distance sample or silently remove negative/backwards interior data.
  let first=from;
  while(first<to&&distance.values[first]<0) {
    assert(distance.values[first]>=-5&&(first+1-from)/distance.hz<=0.40000001,'Leading pre-start distance exceeds 5 m / 0.4 s omission bound');
    assert(first===from||distance.values[first]>=distance.values[first-1],'Distance moves backwards before lap start');
    first++;
  }
  assert(first<to,'No nonnegative lap samples after the pre-start span');
  const omittedPreStartSamples=first-from,origin=first/distance.hz, samples=[], lateral=recording.channels.get('Path Lateral');
  if(lateral)assert(lateral.unit==='m','Path Lateral unit must be m');
  for(let i=first;i<to;i++) {
    const time=i/distance.hz;
    const sample={time:round(time-origin),distanceM:round(distance.values[i]),speedKph:round(at(speed,time)),brake:round(at(brake,time)/100),throttle:round(at(throttle,time)/100),steering:round(at(steering,time)/100),x:null,z:null};
    if(lateral?.values)sample.pathLateralM=round(at(lateral,time));
    assert(sample.distanceM>=0&&sample.speedKph>=0&&sample.speedKph<=550&&sample.brake>=0&&sample.brake<=1&&sample.throttle>=0&&sample.throttle<=1&&Math.abs(sample.steering)<=1,'Invalid normalized sample');
    assert(!samples.length||sample.distanceM>=samples.at(-1).distanceM,'Distance moves backwards inside lap');
    samples.push(sample);
  }
  const conditions={...(options.conditions??{}),recordedLapTimeSeconds:String(round(scoredLapTime)),recordedLapNumber:String(options.lapNumber),recordedVehicle:recording.header.carLongName??recording.header.car,sourceDistanceHz:String(distance.hz),sourceInputsHz:String(brake.hz),sampleTimeOrigin:'First recorded scoring-distance sample after lap wrap, not exact start/finish crossing',boundaryCoverage:'Partial: no synthetic start/finish samples. Whole-lap delta must be withheld.',coordinateLimit:'GPS available but mapping to LMU world axes unverified; world line coordinates omitted.',validityEvidence:'No independent game invalid-lap/track-limit flag decoded. Timing-channel agreement does not prove validity.',bestLapEvidence:Math.abs(scoredBest-scoredLapTime)<0.002?'Game Best Laptime and Last Laptime match Lap Start Elapsed Time difference':'Last Laptime matches Lap Start Elapsed Time difference'};
  if(omittedPreStartSamples) {
    conditions.omittedPreStartSamples=String(omittedPreStartSamples);
    conditions.sampleTimeOrigin='First retained nonnegative scoring-distance sample; bounded leading pre-start samples omitted, never clamped to zero';
  }
  for(const [name,key] of [['Ambient Temperature','measuredAmbientCelsius'],['Track Temperature','measuredTrackCelsius'],['Fuel Level','measuredFuelLitres']]) {
    const c=recording.channels.get(name); if(c?.values) { const values=Array.from(c.values).filter((_,i)=>i/c.hz>=origin&&i/c.hz<to/distance.hz); conditions[key]=`${round(Math.min(...values))}..${round(Math.max(...values))}`; }
  }
  const omission=omittedPreStartSamples?` Omitted ${omittedPreStartSamples} leading negative-distance pre-start sample(s), each within 5 m and total span within 0.4 s. No zero-distance point synthesized.`:'';
  const result={schemaVersion:1,game:'LMU',car:options.car,track:recording.header.track,layout:options.layout??'unknown',gameVersion:options.gameVersion??'unknown',trackLengthM:null,distanceBasis:options.distanceBasis,coordinateFrame:'unknown',lateralPositionBasis:lateral?.values?'lmu-motec-path-lateral-m':'unknown',sourceUrl:options.sourceUrl,author:options.author,recordedAt:`${recording.header.date} ${recording.header.time} (local timezone unknown)`,licence:options.licence??'Personal download only; redistribution permission unverified',provenance:`Original LD SHA256 ${recording.sha256}. ${options.provenance??''} Stored Lap Distance channel used directly; no speed integration or GPS-to-world inference. ${samples.length} samples at ${distance.hz} Hz; ${samples[0].distanceM}..${samples.at(-1).distanceM} m.${omission}`,validity:'unknown',complete:false,conditions,samples};
  return result;
}

export function main(argv) {
  assert(argv.length===3,'Usage: node import-motec.mjs INPUT.ld OPTIONS.json OUTPUT.json');
  const [input,config,output]=argv;
  assert(path.resolve(input)!==path.resolve(output)&&path.resolve(config)!==path.resolve(output),'Output must not overwrite inputs');
  assert(!fs.existsSync(output),'Output already exists; choose a new filename');
  assert(fs.statSync(input).size<=MAX_BYTES,'Input exceeds 256 MiB limit');
  const recording=readLd(fs.readFileSync(input)), options=JSON.parse(fs.readFileSync(config,'utf8').replace(/^\uFEFF/,''));
  const reference=convert(recording,options);
  fs.writeFileSync(output,JSON.stringify(reference,null,2),{flag:'wx'});
  console.log(JSON.stringify({output,samples:reference.samples.length,recordedLapTime:reference.conditions.recordedLapTimeSeconds,partial:true,worldLine:false,sha256:recording.sha256}));
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href) {
  try{main(process.argv.slice(2));}catch(error){console.error(error.message);process.exitCode=1;}
}
