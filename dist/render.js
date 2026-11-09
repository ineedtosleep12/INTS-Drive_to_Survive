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
    if(text)label(ctx,text,a.x+nx*12,a.y-ny*12,color);
  }
  function track(canvas,state,scene,options={}){
    const view=surface(canvas);if(!view)return;
    const {ctx,w,h}=view,s=state||{},points=(scene&&scene.points)||[],roadWidth=number(scene&&scene.width,12);
    ctx.fillStyle=C.bg;ctx.fillRect(0,0,w,h);
    let scale,cx,cy;
    if(options.follow){scale=w/90;cx=number(s.x);cy=number(s.y);}
    else{
      let minX=0,maxX=40,minY=0,maxY=20;
      if(points.length){minX=maxX=points[0].x;minY=maxY=points[0].y;for(const p of points){minX=Math.min(minX,p.x);maxX=Math.max(maxX,p.x);minY=Math.min(minY,p.y);maxY=Math.max(maxY,p.y);}}
      const pad=roadWidth+12;
      scale=Math.min(Math.max(30,w-54)/(maxX-minX+pad*2),Math.max(30,h-96)/(maxY-minY+pad*2));
      cx=(minX+maxX)/2;cy=(minY+maxY)/2;
    }
    scale=Math.max(.2,number(scale,1));
    const originX=w/2,originY=h/2+8;
    const toScreen=p=>({x:originX+(p.x-cx)*scale,y:originY-(p.y-cy)*scale});
    const x0=cx-originX/scale,x1=cx+(w-originX)/scale,y0=cy-(h-originY)/scale,y1=cy+originY/scale;
    const grid=scale>6?10:20;
    ctx.strokeStyle=C.grid;ctx.lineWidth=.6;
    ctx.beginPath();
    for(let x=Math.floor(x0/grid)*grid;x<=x1;x+=grid){const p=toScreen({x,y:0});ctx.moveTo(p.x,0);ctx.lineTo(p.x,h);}
    for(let y=Math.floor(y0/grid)*grid;y<=y1;y+=grid){const p=toScreen({x:0,y});ctx.moveTo(0,p.y);ctx.lineTo(w,p.y);}
    ctx.stroke();
    const screenPoints=points.map(toScreen);
    line(ctx,screenPoints,'#19272e',roadWidth*scale+11);
    line(ctx,screenPoints,C.edge,roadWidth*scale+2);
    line(ctx,screenPoints,C.road,roadWidth*scale);
    if(options.path!==false)line(ctx,screenPoints,'#6dd6eb65',1.3,[6,9]);
    if(points.length>1){
      gate(ctx,atDistance(points,0),roadWidth,toScreen,'#8fa7b5','START',false);
      if(scene.preset!=='drift')gate(ctx,atDistance(points,number(scene.total,Infinity)),roadWidth,toScreen,'#bdcbd4','FINISH',true);
      if(scene.preset==='corner'){
        gate(ctx,atDistance(points,45),roadWidth,toScreen,'#829ca9','진입 / 회전',false);
        gate(ctx,atDistance(points,110),roadWidth,toScreen,'#829ca9','회전 / 탈출',false);
        const board=toScreen({x:30,y:-roadWidth/2-6});label(ctx,'15 m',board.x,board.y,'#c8d5dd');
      }
    }
    if(options.ghost && options.ghost.length>1)line(ctx,options.ghost.slice(-1500).map(toScreen),'#b5f36b55',1.3,[3,4]);
    if(options.trail && options.trail.length>1)line(ctx,options.trail.slice(-1500).map(toScreen),'#77929f99',1.6);
    const p=toScreen({x:number(s.x),y:number(s.y)}),cs=Math.max(scale,3.1),heading=number(s.heading);
    // Body local x points forward; canvas local y points to the car's right.
    ctx.save();ctx.translate(p.x,p.y);ctx.rotate(-heading);
    ctx.shadowColor='#0009';ctx.shadowBlur=8;ctx.fillStyle='#9ed75e';ctx.strokeStyle='#d0ff9f';ctx.lineWidth=.8;
    ctx.beginPath();ctx.moveTo(2.12*cs,-.69*cs);ctx.lineTo(1.72*cs,-.86*cs);ctx.lineTo(-1.9*cs,-.86*cs);ctx.lineTo(-2.15*cs,-.65*cs);ctx.lineTo(-2.15*cs,.65*cs);ctx.lineTo(-1.9*cs,.86*cs);ctx.lineTo(1.72*cs,.86*cs);ctx.lineTo(2.12*cs,.69*cs);ctx.closePath();ctx.fill();ctx.stroke();ctx.shadowBlur=0;
    ctx.fillStyle='#20342d';ctx.fillRect(.30*cs,-.61*cs,.74*cs,1.22*cs);ctx.fillStyle='#3f5c3a';ctx.fillRect(-1.20*cs,-.60*cs,.62*cs,1.2*cs);
    ctx.fillStyle='#ecffd3';ctx.fillRect(1.95*cs,-.65*cs,.15*cs,.37*cs);ctx.fillRect(1.95*cs,.28*cs,.15*cs,.37*cs);
    for(const axle of [{x:1.18,front:true},{x:-1.47,front:false}])for(const side of [-1,1]){
      ctx.save();ctx.translate(axle.x*cs,side*.91*cs);if(axle.front)ctx.rotate(-number(s.steer));ctx.fillStyle='#081015';ctx.strokeStyle='#72818a';ctx.lineWidth=.7;ctx.fillRect(-.42*cs,-.18*cs,.84*cs,.36*cs);ctx.strokeRect(-.42*cs,-.18*cs,.84*cs,.36*cs);ctx.restore();
    }
    ctx.restore();
    arrow(ctx,p.x,p.y,-heading,clamp(cs*7,25,51),C.lime,1.9);
    if(number(s.speed)>.5)arrow(ctx,p.x,p.y,-heading-number(s.beta),clamp(cs*6+number(s.speed)*.3,24,62),C.cyan,1.6);
    if(p.x<0 || p.x>w || p.y<0 || p.y>h){label(ctx,'차량이 화면 밖입니다 · ⌖ 추적 카메라',w/2,h-55,C.coral);}
    const meters=scale>5?10:20,len=meters*scale,barX=w-24-len,barY=h-22;
    line(ctx,[{x:barX,y:barY-3},{x:barX,y:barY},{x:barX+len,y:barY},{x:barX+len,y:barY-3}],'#708795',1);
    ctx.fillStyle='#708795';ctx.font='9px ui-monospace, monospace';ctx.textAlign='center';ctx.fillText(`${meters} m`,barX+len/2,barY-7);
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
  function trace(canvas,samples){
    const view=surface(canvas);if(!view)return;
    const {ctx,w,h}=view,data=Array.isArray(samples)?samples:[],left=28,right=w-8,top=8,bottom=h-17,pw=right-left,ph=bottom-top;
    if(pw<20 || ph<10)return;
    const latest=data.length?number(data[data.length-1].t):0,end=Math.max(12,latest),start=end-12;
    ctx.font='8px ui-monospace, monospace';ctx.textAlign='right';ctx.textBaseline='middle';
    for(const fraction of [0,.5,1]){const y=bottom-fraction*ph;line(ctx,[{x:left,y},{x:right,y}],'#253945',.7,[2,4]);ctx.fillStyle='#6f8999';ctx.fillText(`${Math.round(fraction*100)}`,left-6,y);}
    ctx.textAlign='center';
    for(let i=0;i<=4;i++){const x=left+pw*i/4;line(ctx,[{x,y:top},{x,y:bottom}],'#1c303c',.6);ctx.fillStyle='#6f8999';ctx.fillText(`${Math.round(start+i*3)}s`,x,h-5);}
    const series=[{field:'speed',color:'#b4c1ca',value:s=>number(s.speed)*3.6/120},{field:'throttle',color:C.lime,value:s=>number(s.throttle)},{field:'brake',color:C.coral,value:s=>number(s.brake)},{field:'steer',color:C.cyan,value:s=>Math.abs(number(s.steer))}];
    const visible=data.filter(s=>Number.isFinite(s.t) && s.t>=start && s.t<=end);
    ctx.save();ctx.beginPath();ctx.rect(left,top,pw,ph);ctx.clip();
    for(const item of series){const pts=visible.map(s=>({x:left+(s.t-start)/12*pw,y:bottom-clamp(item.value(s),0,1)*ph}));line(ctx,pts,item.color,item.field==='speed'?1.1:1.5);}
    ctx.restore();
    if(!visible.length){ctx.fillStyle='#6f8999';ctx.textAlign='center';ctx.font='10px "Malgun Gothic", sans-serif';ctx.fillText('주행하면 입력 기록이 표시됩니다',left+pw/2,top+ph/2);}
  }
  root.GripRenderer=Object.freeze({track,grip,trace});
})(typeof globalThis!=='undefined'?globalThis:this);
