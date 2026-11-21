// Independent converter for the public ACCReplay single-lap chart contract.
// A distance-indexed chart is NOT a sampled clock. Preserve missing time.
import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
const requireValue=(ok,message)=>{if(!ok)throw new Error(message);};
const finite=(v,min,max)=>typeof v==='number'&&Number.isFinite(v)&&v>=min&&v<=max;
const sha=bytes=>crypto.createHash('sha256').update(bytes).digest('hex');
function axis(values,label) {
 requireValue(Array.isArray(values)&&values.length>=20&&values.length<=100000,`${label}: invalid count`);
 requireValue(values.every((v,i)=>finite(v,0,100000)&&(i===0||v>values[i-1])&&(i===0||v-values[i-1]<=20)),`${label}: nonmonotonic or sparse distance`);
}
export function convertAccReplay(data,details,options) {
 requireValue(options.game==='ACC'&&options.sourceUrl===`https://www.accreplay.com/laps/${options.lapId}`,'Explicit ACC public source required');
 requireValue(options.units?.speed==='km/h'&&options.units?.pedals==='percent'&&options.units?.steering==='degrees','Confirm observed chart units');
 requireValue(details?.lap?.id===options.lapId&&details?.car?.model===options.expectedCar&&details?.session?.trackShortName===options.expectedTrack&&details?.replay?.version===options.expectedVersion,'Source identity or version mismatch');
 requireValue(details.driver?.fullName===options.expectedDriver&&details.lap.lapTime===options.expectedLapTime,'Driver or lap time mismatch');
 requireValue(data.channels?.length===1&&data.positions?.length===1,'Only a single-lap response is supported');
 const channel=data.channels[0],positions=data.positions[0],distance=data.distances;
 requireValue(channel.lapId===options.lapId&&positions.lapId===options.lapId,'Channel lap identity mismatch');
 axis(distance,'inputs');axis(positions.distances,'positions');
 for(const name of ['speed','throttle','brake','steering','deltaT'])requireValue(Array.isArray(channel[name])&&channel[name].length===distance.length,`Missing or unequal ${name} channel`);
 requireValue(channel.deltaT.every(v=>v===0),'Comparison delta is not an elapsed-time channel');
 requireValue(Array.isArray(positions.x)&&Array.isArray(positions.y)&&positions.x.length===positions.distances.length&&positions.y.length===positions.distances.length,'Missing or unequal map coordinates');
 requireValue(positions.x.every(v=>finite(v,-1e7,1e7))&&positions.y.every(v=>finite(v,-1e7,1e7)),'Invalid map coordinate');
 requireValue(distance[0]>=positions.distances[0]&&distance.at(-1)<=positions.distances.at(-1),'Inputs outside observed coordinate coverage');
 const lapMatch=/^(\d{2}):(\d{2})\.(\d{3})$/.exec(details.lap.lapTime);
 requireValue(lapMatch&&Number(lapMatch[2])<60,'Unsupported aggregate lap time');
 const aggregate=Number(lapMatch[1])*60+Number(lapMatch[2])+Number(lapMatch[3])/1000;
 let cursor=0;
 const samples=distance.map((d,i)=>{
  requireValue(finite(channel.speed[i],0,550)&&finite(channel.brake[i],0,100)&&finite(channel.throttle[i],0,100)&&finite(channel.steering[i],-2000,2000),'Invalid input units or values');
  while(cursor<positions.distances.length-2&&positions.distances[cursor+1]<d)cursor++;
  const a=positions.distances[cursor],b=positions.distances[cursor+1],t=(d-a)/(b-a);
  requireValue(b>a&&b-a<=20&&t>=0&&t<=1,'Unobserved coordinate interpolation');
  return {time:null,distanceM:d,speedKph:channel.speed[i],brake:channel.brake[i]/100,throttle:channel.throttle[i]/100,steering:null,steeringDegrees:channel.steering[i],x:positions.x[cursor]+(positions.x[cursor+1]-positions.x[cursor])*t,z:positions.y[cursor]+(positions.y[cursor+1]-positions.y[cursor])*t,pathLateralM:null};
 });
 return {schemaVersion:1,timeBasis:'unavailable-distance-only',game:'ACC',car:details.car.model,track:details.session.trackShortName,layout:'GP',gameVersion:details.replay.version,trackLengthM:null,distanceBasis:'acc-replay-spline-distance-m',coordinateFrame:'acc-replay-map-xy-m',lateralPositionBasis:'unknown',sourceUrl:options.sourceUrl,author:details.driver.fullName,recordedAt:details.replay.originalFileDate+' (timezone unknown)',licence:'Public viewing; redistribution permission unverified; personal use only',validity:'unknown',complete:false,
  provenance:`Public ACCReplay chart response for lap ${options.lapId}; no account or paid source accessed. Distance-indexed inputs and recorded map positions, not a generated line. Original map X/Y stored as X/Z under a distinct frame. Coordinates interpolated only between original positions <=20 m apart. No absolute sample times: null time is preserved; zero deltaT is not elapsed time. Native course-distance and coordinate correspondence require separate validation. Raw telemetry SHA256 ${options.telemetrySha256??'synthetic-test'}. Details SHA256 ${options.detailsSha256??'synthetic-test'}.`,
  conditions:{recordedLapTimeSeconds:String(aggregate),sourceReportedValid:String(details.lap.isValid),sourceComputedValid:String(details.lap.isValidComputed),sourceValidity:'Provider reported flags; independent/native game lap validity unverified',sourceLapNumber:String(details.lap.lapNumber),sourceSessionType:details.session.type,sourceInputCount:String(distance.length),sourcePositionCount:String(positions.distances.length),sourceSampleTime:'No sample timestamps supplied. Single-lap deltaT contains zeros; never interpreted as elapsed time.',sourceCoordinateFrame:'Original public map X/Y; native X/Z axis, origin and metre scale unverified until complete-lap geometry check',sourceDistance:'Public chart distances correspond to section splineStart/splineEnd; equivalence to native normalizedCarPosition remains unverified',sourceSteeringUnit:'degrees; no guessed steering-lock normalization',sourceDownloadUrl:`https://www.accreplay.com/telemetry/${options.lapId}`,sessionAmbient:details.session.ambientTemperature,sessionRoad:details.session.roadTemperature,lapAverageAmbientCelsius:String(details.lap.averageAmbientTemperatureC),lapAverageRoadCelsius:String(details.lap.averageRoadTemperatureC),fuel:'unknown',tyres:'unknown',setup:'unknown'},samples};
}
export function main(args) {
 requireValue(args.length===4,'Usage: node import-accreplay.mjs TELEMETRY.json DETAILS.json OPTIONS.json NEW-REFERENCE.json');
 const [input,detailsPath,configPath,output]=args;
 requireValue(!fs.existsSync(output)&&[input,detailsPath,configPath].every(p=>path.resolve(p)!==path.resolve(output)),'Choose a new output path');
 const read=(file,max)=>{requireValue(fs.statSync(file).size<=max,'Input too large');return fs.readFileSync(file);};
 const bytes=read(input,8*1024*1024),details=read(detailsPath,1024*1024),options=JSON.parse(read(configPath,65536));
 requireValue(typeof options.telemetrySha256==='string'&&sha(bytes)===options.telemetrySha256&&typeof options.detailsSha256==='string'&&sha(details)===options.detailsSha256,'Source checksum mismatch; inspect changed public source before conversion');
 const result=convertAccReplay(JSON.parse(bytes),JSON.parse(details),options);
 fs.writeFileSync(output,JSON.stringify(result,null,2),{flag:'wx'});
 console.log(JSON.stringify({output,track:result.track,car:result.car,samples:result.samples.length,timeBasis:result.timeBasis,coordinateFrame:result.coordinateFrame}));
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href)main(process.argv.slice(2));
