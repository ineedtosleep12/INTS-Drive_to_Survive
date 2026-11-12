/* Canvas views for Grip Lab. All drawing units below are CSS pixels. */
(function(root){
  'use strict';
  const C={bg:'#0d151b',grid:'#1b2932',road:'#26343e',edge:'#52636d',text:'#a6bac8',lime:'#b5f36b',cyan:'#6dd6eb',coral:'#ff907e',grey:'#c1cbd1'};
  const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
  const number=(v,f=0)=>Number.isFinite(v)?v:f;
  function surface(canvas){
    if(!canvas || !canvas.getContext) return null;
    const rect=canvas.getBoundingClientRect(),w=rect.width,h=rect.height;
    if(w<2 || h<2) return null;
    const dpr=clamp(number(root.devicePixelRatio,1),1,3);
    const pw=Math.round(w*dpr),ph=Math.round(h*dpr);
    if(canvas.width!==pw || canvas.height!==ph){canvas.width=pw;canvas.height=ph;}
    const ctx=canvas.getContext('2d');
    if(!ctx)return null;
    ctx.setTransform(dpr,0,0,dpr,0,0);
    ctx.clearRect(0,0,w,h);
    ctx.lineCap='round';ctx.lineJoin='round';ctx.font='10px "Malgun Gothic", sans-serif';
    return {ctx,w,h};
  }
  function line(ctx,points,color,width,dash=[]){
    if(!points || points.length<2)return;
    ctx.beginPath();ctx.moveTo(points[0].x,points[0].y);
    for(let i=1;i<points.length;i++)ctx.lineTo(points[i].x,points[i].y);
    ctx.strokeStyle=color;ctx.lineWidth=width;ctx.setLineDash(dash);ctx.stroke();ctx.setLineDash([]);
  }
  function arrow(ctx,x,y,angle,length,color,width=1.8){
    if(!Number.isFinite(angle))return;
    const ex=x+Math.cos(angle)*length,ey=y+Math.sin(angle)*length;
    const head=clamp(length*.18,5,8);
    ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(ex,ey);
    ctx.moveTo(ex-head*Math.cos(angle-.48),ey-head*Math.sin(angle-.48));ctx.lineTo(ex,ey);
    ctx.lineTo(ex-head*Math.cos(angle+.48),ey-head*Math.sin(angle+.48));
    ctx.strokeStyle=color;ctx.lineWidth=width;ctx.stroke();
  }
  function label(ctx,text,x,y,color=C.text,align='center'){
    ctx.font='9px "Malgun Gothic", sans-serif';ctx.textAlign=align;ctx.textBaseline='middle';
    const tw=ctx.measureText(text).width;
    const left=align==='center'?x-tw/2:align==='right'?x-tw:x;
    ctx.fillStyle='#0d151bdc';ctx.fillRect(left-5,y-7,tw+10,14);
    ctx.fillStyle=color;ctx.fillText(text,x,y);
  }
  function atDistance(points,d){
    if(!points.length)return null;
    let previous=points[0],previousS=number(previous.s),acc=previousS;
    for(let i=1;i<points.length;i++){
      const p=points[i],seg=Math.hypot(p.x-previous.x,p.y-previous.y);
      const curS=Number.isFinite(p.s)?p.s:acc+seg;
      if(curS>=d){const t=clamp((d-previousS)/Math.max(curS-previousS,1e-9),0,1);return {x:previous.x+(p.x-previous.x)*t,y:previous.y+(p.y-previous.y)*t,angle:Math.atan2(p.y-previous.y,p.x-previous.x)};}
      previous=p;previousS=curS;acc=curS;
    }
    const last=points[points.length-1],prev=points[Math.max(0,points.length-2)];
    return {x:last.x,y:last.y,angle:Math.atan2(last.y-prev.y,last.x-prev.x)};
  }
  function gate(ctx,pos,width,toScreen,color,text,checkered){
    if(!pos)return;
    const nx=-Math.sin(pos.angle),ny=Math.cos(pos.angle),a=toScreen({x:pos.x+nx*width/2,y:pos.y+ny*width/2}),b=toScreen({x:pos.x-nx*width/2,y:pos.y-ny*width/2});
    line(ctx,[a,b],color,1.4,checkered?[]:[4,4]);
    if(checkered){
      for(let i=0;i<10;i++){const t=i/10,u=(i+1)/10;line(ctx,[{x:a.x+(b.x-a.x)*t,y:a.y+(b.y-a.y)*t},{x:a.x+(b.x-a.x)*u,y:a.y+(b.y-a.y)*u}],i%2?'#101b23':'#d0dce3',3);}
    }
    if(text){const length=Math.max(1,Math.hypot(a.x-b.x,a.y-b.y));label(ctx,text,a.x+(a.x-b.x)/length*12,a.y+(a.y-b.y)/length*12,color);}
  }
  function brakeBoards(ctx,scene,toScreen,w,h,options){
    // A full-course overview has too little space for legible roadside signs.
    if(scene.total>700&&!options.follow)return;
    const occupied=[];
    const boards=(scene.brakingBoards||[]).filter(b=>Number.isFinite(b.s)&&Number.isFinite(b.distance)&&b.s>=0&&b.s<=scene.total);
    if(options.follow)boards.sort((a,b)=>Math.abs(a.s-number(options.progress))-Math.abs(b.s-number(options.progress)));
    for(const board of boards){
      const pos=atDistance(scene.points,board.s);if(!pos)continue;
      const normal={x:-Math.sin(pos.angle),y:Math.cos(pos.angle)},middle=toScreen(pos),roadWidth=number(scene.width,12),edge=toScreen({x:pos.x+normal.x*roadWidth/2,y:pos.y+normal.y*roadWidth/2});
      const length=Math.max(1e-6,Math.hypot(edge.x-middle.x,edge.y-middle.y)),nx=(edge.x-middle.x)/length,ny=(edge.y-middle.y)/length;
      const x=edge.x+nx*23,y=edge.y+ny*23,bw=34,bh=23;
      if(x-bw/2<6||x+bw/2>w-6||y-bh/2<42||y+bh/2>h-42)continue;
      if(occupied.some(p=>Math.abs(p.x-x)<bw+6&&Math.abs(p.y-y)<bh+6))continue;
      occupied.push({x,y});
      line(ctx,[edge,{x,y}],'#aab8bf',1.2);
      ctx.fillStyle='#f0f3eb';ctx.fillRect(x-bw/2,y-bh/2,bw,bh);ctx.strokeStyle='#7d8c8d';ctx.lineWidth=1;ctx.strokeRect(x-bw/2,y-bh/2,bw,bh);
      ctx.font='bold 14px ui-monospace, monospace';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillStyle='#111b1b';ctx.fillText(String(Math.round(board.distance)),x,y+.5);
    }
  }
  function track(canvas,state,scene,options={}){
    const view=surface(canvas);if(!view)return;
    const {ctx,w,h}=view,s=state||{},points=(scene&&scene.points)||[],roadWidth=number(scene&&scene.width,12);
    ctx.fillStyle=C.bg;ctx.fillRect(0,0,w,h);
    // Keep the initial tangent pointing up. Rotation is a view transform only;
    // physics, turn direction, replay progress and course geometry stay unchanged.
    const rotation=Math.PI/2-number(scene?.startHeading),cos=Math.cos(rotation),sin=Math.sin(rotation);
    const oriented=p=>({x:number(p.x)*cos-number(p.y)*sin,y:number(p.x)*sin+number(p.y)*cos});
    const orientedPoints=points.map(oriented),camera=options.cameraState||s;
    let scale,cx,cy;
    if(options.follow){scale=Math.min(w,h*1.25)/Math.max(100,number(camera.speed)*7);const lead=Math.min(65,number(camera.speed)*1.2),direction=number(camera.heading)+number(camera.beta),center=oriented({x:number(camera.x)+Math.cos(direction)*lead,y:number(camera.y)+Math.sin(direction)*lead});cx=center.x;cy=center.y;}
    else{
      let minX=0,maxX=40,minY=0,maxY=20;
      if(orientedPoints.length){minX=maxX=orientedPoints[0].x;minY=maxY=orientedPoints[0].y;for(const p of orientedPoints){minX=Math.min(minX,p.x);maxX=Math.max(maxX,p.x);minY=Math.min(minY,p.y);maxY=Math.max(maxY,p.y);}}
      const pad=roadWidth+12;
      scale=Math.min(Math.max(30,w-54)/(maxX-minX+pad*2),Math.max(30,h-96)/(maxY-minY+pad*2));
      cx=(minX+maxX)/2;cy=(minY+maxY)/2;
    }
    scale=Math.max(.02,number(scale,1));
    const originX=w/2,originY=h/2+8;
    const screenOriented=p=>({x:originX+(p.x-cx)*scale,y:originY-(p.y-cy)*scale});
    const toScreen=p=>screenOriented(oriented(p));
    const x0=cx-originX/scale,x1=cx+(w-originX)/scale,y0=cy-(h-originY)/scale,y1=cy+originY/scale;
    const grid=scale>6?10:scale>1?20:scale>.2?100:500;
    ctx.strokeStyle=C.grid;ctx.lineWidth=.6;
    ctx.beginPath();
    for(let x=Math.floor(x0/grid)*grid;x<=x1;x+=grid){const p=screenOriented({x,y:0});ctx.moveTo(p.x,0);ctx.lineTo(p.x,h);}
    for(let y=Math.floor(y0/grid)*grid;y<=y1;y+=grid){const p=screenOriented({x:0,y});ctx.moveTo(0,p.y);ctx.lineTo(w,p.y);}
    ctx.stroke();
    const screenPoints=points.map(toScreen);
    line(ctx,screenPoints,'#19272e',roadWidth*scale+11);
    line(ctx,screenPoints,C.edge,roadWidth*scale+2);
    line(ctx,screenPoints,C.road,roadWidth*scale);
    if(options.path!==false)line(ctx,screenPoints,'#6dd6eb65',1.3,[6,9]);
    if(points.length>1){
      gate(ctx,atDistance(points,0),roadWidth,toScreen,'#8fa7b5','START',false);
      if(!['drift','figure8','playground'].includes(scene.preset))gate(ctx,atDistance(points,number(scene.total,Infinity)),roadWidth,toScreen,'#bdcbd4','FINISH',true);
      if(scene.preset==='corner'){
        gate(ctx,atDistance(points,45),roadWidth,toScreen,'#829ca9','진입 / 회전',false);
        gate(ctx,atDistance(points,110),roadWidth,toScreen,'#829ca9','회전 / 탈출',false);
        const board=toScreen({x:30,y:-roadWidth/2-6});label(ctx,'15 m',board.x,board.y,'#c8d5dd');
      }
      for(const marker of scene.landmarks||[]){const color=marker.type==='brake'?C.coral:marker.type==='exit'?C.lime:C.cyan;gate(ctx,atDistance(points,marker.s),roadWidth,toScreen,color,marker.label,false);}
      brakeBoards(ctx,scene,toScreen,w,h,options);
    }
    if(options.ghost && options.ghost.length>1)line(ctx,options.ghost.slice(-1500).map(toScreen),'#b5f36b55',1.3,[3,4]);
    if(options.trail && options.trail.length>1)line(ctx,options.trail.slice(-1500).map(toScreen),'#77929f99',1.6);
    const p=toScreen({x:number(s.x),y:number(s.y)}),cs=Math.max(scale,3.1),heading=number(s.heading);
    if(options.review){ctx.beginPath();ctx.arc(p.x,p.y,22,0,Math.PI*2);ctx.strokeStyle=options.vehicleColor||C.coral;ctx.lineWidth=2;ctx.stroke();label(ctx,(options.reviewLabel||'기록 위치')+' · '+number(options.review.t).toFixed(2)+' s',w/2,23,options.vehicleColor||C.coral);}
    // Body local x points forward; canvas local y points to the car's right.
    ctx.save();ctx.translate(p.x,p.y);ctx.rotate(-heading-rotation);
    ctx.shadowColor='#0009';ctx.shadowBlur=8;ctx.fillStyle=options.vehicleColor||'#9ed75e';ctx.strokeStyle=options.vehicleColor||'#d0ff9f';ctx.lineWidth=.8;
    ctx.beginPath();ctx.moveTo(2.12*cs,-.69*cs);ctx.lineTo(1.72*cs,-.86*cs);ctx.lineTo(-1.9*cs,-.86*cs);ctx.lineTo(-2.15*cs,-.65*cs);ctx.lineTo(-2.15*cs,.65*cs);ctx.lineTo(-1.9*cs,.86*cs);ctx.lineTo(1.72*cs,.86*cs);ctx.lineTo(2.12*cs,.69*cs);ctx.closePath();ctx.fill();ctx.stroke();ctx.shadowBlur=0;
    ctx.fillStyle='#20342d';ctx.fillRect(.30*cs,-.61*cs,.74*cs,1.22*cs);ctx.fillStyle='#3f5c3a';ctx.fillRect(-1.20*cs,-.60*cs,.62*cs,1.2*cs);
    ctx.fillStyle='#ecffd3';ctx.fillRect(1.95*cs,-.65*cs,.15*cs,.37*cs);ctx.fillRect(1.95*cs,.28*cs,.15*cs,.37*cs);
    for(const axle of [{x:number(s.params?.a,1.18),front:true},{x:-number(s.params?.b,1.47),front:false}])for(const side of [-1,1]){
      ctx.save();ctx.translate(axle.x*cs,side*(number(s.params?.track,1.6)/2+.1)*cs);if(axle.front)ctx.rotate(-number(s.steer));ctx.fillStyle='#081015';ctx.strokeStyle='#72818a';ctx.lineWidth=.7;ctx.fillRect(-.42*cs,-.18*cs,.84*cs,.36*cs);ctx.strokeRect(-.42*cs,-.18*cs,.84*cs,.36*cs);ctx.restore();
    }
    ctx.restore();
    arrow(ctx,p.x,p.y,-heading-rotation,clamp(cs*7,25,51),options.vehicleColor||C.lime,1.9);
    if(number(s.speed)>.5)arrow(ctx,p.x,p.y,-heading-rotation-number(s.beta),clamp(cs*6+number(s.speed)*.3,24,62),C.cyan,1.6);
    if(p.x<0 || p.x>w || p.y<0 || p.y>h){label(ctx,'차량이 화면 밖입니다 · ⌖ 추적 카메라',w/2,h-55,C.coral);}
    const meters=scale>5?10:scale>1?20:scale>.2?100:500,len=meters*scale,barX=w-24-len,barY=h-22;
    line(ctx,[{x:barX,y:barY-3},{x:barX,y:barY},{x:barX+len,y:barY},{x:barX+len,y:barY-3}],'#708795',1);
    ctx.fillStyle='#708795';ctx.font='9px ui-monospace, monospace';ctx.textAlign='center';ctx.fillText(`${meters} m`,barX+len/2,barY-7);
    if(scene.total>700&&options.follow&&points.length>2){
      const mw=140,mh=92,mx=w-mw-16,my=h-mh-48;
      let loX=Infinity,hiX=-Infinity,loY=Infinity,hiY=-Infinity;for(const t of orientedPoints){loX=Math.min(loX,t.x);hiX=Math.max(hiX,t.x);loY=Math.min(loY,t.y);hiY=Math.max(hiY,t.y);}
      const ms=Math.min((mw-16)/Math.max(1,hiX-loX),(mh-16)/Math.max(1,hiY-loY)),ox=mx+mw/2,oy=my+mh/2;
      const mini=t=>{const p=oriented(t);return{x:ox+(p.x-(loX+hiX)/2)*ms,y:oy-(p.y-(loY+hiY)/2)*ms};};
      ctx.fillStyle='#081117dc';ctx.fillRect(mx,my,mw,mh);ctx.strokeStyle='#344550';ctx.lineWidth=.7;ctx.strokeRect(mx,my,mw,mh);line(ctx,points.map(mini),'#829ba9',1.3);
      const mp=mini(s);ctx.beginPath();ctx.arc(mp.x,mp.y,3,0,Math.PI*2);ctx.fillStyle=C.lime;ctx.fill();
      const next=(scene.corners||[]).find(c=>c.s>number(options.progress)+5);if(next&&!options.review)label(ctx,next.name+' · 모델 거리 '+Math.round(next.s-number(options.progress))+' m',w/2,22,C.cyan);
    }
  }
  function grip(canvas,state){
    const view=surface(canvas);if(!view)return;
    const {ctx,w,h}=view,s=state||{},cy=h*.51,r=Math.min(w*.155,h*.30),centers=[w*.25,w*.75];
    ctx.font='9px "Malgun Gothic", sans-serif';ctx.textAlign='center';ctx.textBaseline='middle';
    ctx.fillStyle='#6f8999';ctx.fillText('가속 ↑',w/2,9);ctx.fillText('제동 ↓',w/2,h-8);
    centers.forEach((cx,i)=>{
      const cap=Math.max(1,number(i?s.capR:s.capF,1)),fx=number(i?s.fxR:s.fxF),fy=number(i?s.fyR:s.fyF),usage=clamp(Math.hypot(fx,fy)/cap,0,1),color=usage>.96?C.coral:i?C.cyan:C.lime;
      ctx.fillStyle='#0b151c';ctx.beginPath();ctx.arc(cx,cy,r,0,Math.PI*2);ctx.fill();ctx.strokeStyle='#3b5260';ctx.lineWidth=1;ctx.stroke();
      ctx.beginPath();ctx.arc(cx,cy,r*.5,0,Math.PI*2);ctx.strokeStyle='#243b47';ctx.setLineDash([2,4]);ctx.stroke();ctx.setLineDash([]);
      line(ctx,[{x:cx-r,y:cy},{x:cx+r,y:cy}],'#334b59',.7);line(ctx,[{x:cx,y:cy-r},{x:cx,y:cy+r}],'#334b59',.7);
      // Positive Fy is leftward; positive longitudinal Fx is upward.
      const dx=-clamp(fy/cap,-1,1)*r,dy=-clamp(fx/cap,-1,1)*r;
      line(ctx,[{x:cx,y:cy},{x:cx+dx,y:cy+dy}],color,1.6);
      ctx.beginPath();ctx.arc(cx+dx,cy+dy,4,0,Math.PI*2);ctx.fillStyle=color;ctx.fill();ctx.strokeStyle='#d8e7ee';ctx.lineWidth=.7;ctx.stroke();
      ctx.fillStyle=C.text;ctx.font='10px "Malgun Gothic", sans-serif';ctx.fillText(i?'뒤축':'앞축',cx,cy+r+15);
      ctx.fillStyle='#6f8999';ctx.font='8px "Malgun Gothic", sans-serif';ctx.fillText('← 횡력 →',cx,cy-r-12);
    });
  }
  function trace(canvas,samples,options={}){
    const view=surface(canvas);if(!view)return;
    const {ctx,w,h}=view,data=Array.isArray(samples)?samples:[],left=28,right=w-8,top=8,bottom=h-17,pw=right-left,ph=bottom-top;
    if(pw<20 || ph<10)return;
    const latest=data.length?number(data[data.length-1].t):0,end=Math.max(12,latest),start=end-12;
    ctx.font='8px ui-monospace, monospace';ctx.textAlign='right';ctx.textBaseline='middle';
    for(const fraction of [0,.5,1]){const y=bottom-fraction*ph;line(ctx,[{x:left,y},{x:right,y}],'#253945',.7,[2,4]);ctx.fillStyle='#6f8999';ctx.fillText(`${Math.round(fraction*100)}`,left-6,y);}
    ctx.textAlign='center';
    for(let i=0;i<=4;i++){const x=left+pw*i/4;line(ctx,[{x,y:top},{x,y:bottom}],'#1c303c',.6);ctx.fillStyle='#6f8999';ctx.fillText(`${Math.round(start+i*3)}s`,x,h-5);}
    const series=[{field:'speed',color:'#b4c1ca',value:s=>number(s.speed)*3.6/240},{field:'throttle',color:C.lime,value:s=>number(s.throttle)},{field:'brake',color:C.coral,value:s=>number(s.brake)},{field:'steer',color:C.cyan,value:s=>Math.abs(number(s.steer))}];
    const visible=data.filter(s=>Number.isFinite(s.t) && s.t>=start && s.t<=end);
    ctx.save();ctx.beginPath();ctx.rect(left,top,pw,ph);ctx.clip();
    for(const item of series){const pts=visible.map(s=>({x:left+(s.t-start)/12*pw,y:bottom-clamp(item.value(s),0,1)*ph}));line(ctx,pts,item.color,item.field==='speed'?1.1:1.5);}
    if(Number.isFinite(options.cursor)){const x=left+(options.cursor-start)/12*pw;line(ctx,[{x,y:top},{x,y:bottom}],C.coral,1.4,[3,3]);}
    ctx.restore();
    if(!visible.length){ctx.fillStyle='#6f8999';ctx.textAlign='center';ctx.font='10px "Malgun Gothic", sans-serif';ctx.fillText('주행하면 입력 기록이 표시됩니다',left+pw/2,top+ph/2);}
  }
  function comparison(canvas,playerTrace,referenceTrace,options={}){
    const view=surface(canvas);if(!view)return;
    const {ctx,w,h}=view,left=56,right=w-12,top=30,bottom=h-23,gap=32,rowHeight=(bottom-top-gap)/2;
    if(right-left<40||rowHeight<16)return;
    const player=Array.isArray(playerTrace)?playerTrace:[],reference=Array.isArray(referenceTrace)?referenceTrace:[];
    const all=player.concat(reference).filter(s=>s&&Number.isFinite(s.s));
    const from=Number.isFinite(options.fromS)?options.fromS:Math.min(...all.map(s=>s.s),0);
    const to=Number.isFinite(options.toS)&&options.toS>from?options.toS:Math.max(from+1,...all.map(s=>s.s));
    const plotWidth=right-left,atX=s=>left+(s-from)/(to-from)*plotWidth;
    ctx.fillStyle=C.bg;ctx.fillRect(0,0,w,h);ctx.textAlign='left';ctx.textBaseline='middle';ctx.font='10px "Malgun Gothic", sans-serif';
    line(ctx,[{x:left,y:11},{x:left+20,y:11}],C.lime,2);ctx.fillStyle=C.lime;ctx.fillText('내 입력',left+27,11);
    line(ctx,[{x:left+93,y:11},{x:left+113,y:11}],C.cyan,2,[5,4]);ctx.fillStyle=C.cyan;ctx.fillText('시범',left+120,11);
    const rows=[{field:'brake',title:'브레이크',y:top,lo:0,hi:1,ticks:[[0,'0%'],[.5,'50%'],[1,'100%']]},{field:'steer',title:'조향 방향',y:top+rowHeight+gap,lo:-1,hi:1,ticks:[[-1,'→ 100%'],[0,'중립'],[1,'← 100%']]}];
    for(const row of rows){
      const atY=value=>row.y+rowHeight-(value-row.lo)/(row.hi-row.lo)*rowHeight;
      ctx.fillStyle=C.text;ctx.font='10px "Malgun Gothic", sans-serif';ctx.textAlign='left';ctx.fillText(row.title,left,row.y-10);
      for(const [value,text] of row.ticks){const y=atY(value);line(ctx,[{x:left,y},{x:right,y}],'#293e4b',value===0?1:.6,value===0?[]:[2,4]);ctx.textAlign='right';ctx.fillStyle='#91a7b4';ctx.font='9px "Malgun Gothic", sans-serif';ctx.fillText(text,left-7,y);}
      ctx.save();ctx.beginPath();ctx.rect(left,row.y,plotWidth,rowHeight);ctx.clip();
      for(const [data,color,dash] of [[reference,C.cyan,[5,4]],[player,C.lime,[]]]){
        let segment=[];
        const flush=()=>{line(ctx,segment,color,1.8,dash);segment=[];};
        for(const sample of data){
          if(!sample||!Number.isFinite(sample.s)||!Number.isFinite(sample[row.field])||sample.s<from||sample.s>to){flush();continue;}
          segment.push({x:atX(sample.s),y:atY(clamp(sample[row.field],row.lo,row.hi))});
        }
        flush();
      }
      if(Number.isFinite(options.progress)&&options.progress>=from&&options.progress<=to){const x=atX(options.progress);line(ctx,[{x,y:row.y},{x,y:row.y+rowHeight}],C.coral,1.2,[3,3]);}
      ctx.restore();
    }
    ctx.font='9px ui-monospace, monospace';ctx.textAlign='center';ctx.fillStyle='#91a7b4';
    for(let i=0;i<=4;i++){const value=from+(to-from)*i/4,x=atX(value);ctx.fillText(Math.round(value)+' m',x,h-9);}
    if(!all.length)label(ctx,'비교할 입력 기록이 없습니다',w/2,h/2,C.text);
  }
  root.GripRenderer=Object.freeze({track,grip,trace,comparison});
})(typeof globalThis!=='undefined'?globalThis:this);
