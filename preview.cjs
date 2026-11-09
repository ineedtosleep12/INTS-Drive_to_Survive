const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, 'dist');
const types = {'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8'};
http.createServer((req,res)=>{
  const pathname = decodeURIComponent(new URL(req.url,'http://localhost').pathname);
  const file = path.resolve(root, '.'+(pathname==='/'?'/index.html':pathname));
  if(!file.startsWith(root+path.sep)){res.writeHead(403).end();return;}
  fs.readFile(file,(err,buf)=>{if(err){res.writeHead(404).end('Not found');return;}res.writeHead(200,{'content-type':types[path.extname(file)]||'application/octet-stream','cache-control':'no-store'});res.end(buf);});
}).listen(8765,'127.0.0.1',()=>console.log('Local: http://127.0.0.1:8765'));
