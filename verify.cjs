const fs = require('node:fs');
const path = require('node:path');
const {spawnSync} = require('node:child_process');
process.chdir(__dirname);
const html=fs.readFileSync('dist/index.html','utf8');
for(const [,file] of html.matchAll(/<script src="([^"]+)"><\/script>/g)){
  const result=spawnSync(process.execPath,['--check',path.join('dist',file)],{stdio:'inherit'});
  if(result.status!==0)process.exit(result.status||1);
}
const app=fs.readFileSync('dist/app.js','utf8');
const ids=[...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]);
if(new Set(ids).size!==ids.length)throw new Error('Duplicate HTML id');
const dynamicIds=['history-start','clear-history','retry-mission','next-mission','result-home','review-time','review-chart','review-values','review-readout','review-focus','review-panel','review-play','review-speed','review-loop','review-back','review-forward','review-all','review-window-label','replay-player','replay-reference','replay-player-speed','replay-reference-speed','replay-player-inputs','replay-reference-inputs','replay-reference-status','replay-advice','review-comparison'];
for(const [,id] of app.matchAll(/\$\('([^']+)'\)/g))if(!ids.includes(id)&&!dynamicIds.includes(id))throw new Error('Unknown element '+id);
for(const file of ['physics-test.cjs','app-test.cjs','lessons-test.cjs','transfer-test.cjs','tracks-test.cjs','guide-test.cjs','training-test.cjs','academy-test.cjs','debrief-test.cjs','replay-test.cjs','render-test.cjs']){
  if(!fs.existsSync(file))throw new Error('Missing expected test '+file);
  const result=spawnSync(process.execPath,[file],{stdio:'inherit'});
  if(result.status!==0)process.exit(result.status||1);
}
console.log('PASS: JavaScript syntax, HTML bindings, vehicle model and training flow.');
