const fs = require('node:fs');
const path = require('node:path');
const destination=path.resolve(process.argv[2]||path.join(__dirname,'build','INTS-Drive_to_Survive.html'));
let html=fs.readFileSync(path.join(__dirname,'dist','index.html'),'utf8');
html=html.replace('<link rel="stylesheet" href="style.css">',()=>'<style>'+fs.readFileSync(path.join(__dirname,'dist','style.css'),'utf8')+'</style>');
html=html.replace(/<script src="([^"]+)"><\/script>/g,(_,file)=>'<script>\n'+fs.readFileSync(path.join(__dirname,'dist',file),'utf8').replace(/<\/script/gi,'<\\/script')+'\n</script>');
fs.mkdirSync(path.dirname(destination),{recursive:true});
fs.writeFileSync(destination,html,'utf8');
console.log('Offline HTML: '+destination+' ('+Buffer.byteLength(html)+' bytes)');
