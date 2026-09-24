import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Original sample geometry. glTF is right handed, Y up, meters; glTFast reflects X on import.
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const target=path.join(root,'Assets/StreamingAssets/Models/sample-room.glb');
const json={asset:{version:'2.0',generator:'LabWalk sample generator'},scene:0,scenes:[{nodes:[]}],nodes:[],meshes:[],materials:[],accessors:[],bufferViews:[],buffers:[]};
const chunks=[]; let length=0;
function buffer(values,type,target) {
  const data=type===5126?Buffer.alloc(values.length*4):Buffer.alloc(values.length*2);
  values.forEach((v,i)=>type===5126?data.writeFloatLE(v,i*4):data.writeUInt16LE(v,i*2));
  const index=json.bufferViews.length;
  json.bufferViews.push({buffer:0,byteOffset:length,byteLength:data.length,target});
  chunks.push(data); length+=data.length;
  const pad=(4-length%4)%4; if(pad) { chunks.push(Buffer.alloc(pad)); length+=pad; }
  return index;
}
function accessor(values,componentType,type,count,target,min,max) {
  const index=json.accessors.length;
  json.accessors.push({bufferView:buffer(values,componentType,target),componentType,type,count,...(min?{min,max}:{})});
  return index;
}
const faces=[
  [[1,0,0],[[.5,-.5,-.5],[.5,.5,-.5],[.5,.5,.5],[.5,-.5,.5]]],
  [[-1,0,0],[[-.5,-.5,.5],[-.5,.5,.5],[-.5,.5,-.5],[-.5,-.5,-.5]]],
  [[0,1,0],[[-.5,.5,-.5],[-.5,.5,.5],[.5,.5,.5],[.5,.5,-.5]]],
  [[0,-1,0],[[-.5,-.5,.5],[-.5,-.5,-.5],[.5,-.5,-.5],[.5,-.5,.5]]],
  [[0,0,1],[[.5,-.5,.5],[.5,.5,.5],[-.5,.5,.5],[-.5,-.5,.5]]],
  [[0,0,-1],[[-.5,-.5,-.5],[-.5,.5,-.5],[.5,.5,-.5],[.5,-.5,-.5]]]
];
const positions=[],normals=[],indices=[];
for(const [normal,vertices] of faces) {
  const offset=positions.length/3;
  for(const v of vertices) { positions.push(...v); normals.push(...normal); }
  indices.push(offset,offset+1,offset+2,offset,offset+2,offset+3);
}
const pos=accessor(positions,5126,'VEC3',24,34962,[-.5,-.5,-.5],[.5,.5,.5]);
const normal=accessor(normals,5126,'VEC3',24,34962);
const idx=accessor(indices,5123,'SCALAR',36,34963);
const colors={floor:[.23,.27,.3,1],wall:[.72,.76,.78,1],wood:[.48,.28,.13,1],yellow:[1,.72,.03,1],teal:[.04,.5,.52,1]};
const meshIds={};
for(const [name,color] of Object.entries(colors)) {
  const material=json.materials.length;
  json.materials.push({name,pbrMetallicRoughness:{baseColorFactor:color,metallicFactor:0,roughnessFactor:.85}});
  meshIds[name]=json.meshes.length;
  json.meshes.push({name:`box-${name}`,primitives:[{attributes:{POSITION:pos,NORMAL:normal},indices:idx,material}]});
}
function box(name,translation,scale,material) {
  const index=json.nodes.length;
  json.nodes.push({name,translation,scale,mesh:meshIds[material]}); json.scenes[0].nodes.push(index);
}
box('Floor: 6 x 8 m',[0,-.05,3],[6,.1,8],'floor');
box('West wall',[-2.95,1.5,3],[.1,3,8],'wall');
box('East wall',[2.95,1.5,3],[.1,3,8],'wall');
box('Back wall',[0,1.5,6.95],[6,3,.1],'wall');
box('Entry wall left',[-1.8,1.5,-.95],[2.4,3,.1],'wall');
box('Entry wall right',[1.8,1.5,-.95],[2.4,3,.1],'wall');
box('Door lintel',[0,2.6,-.95],[1.2,.8,.1],'wall');
box('Desk top',[1.4,.75,4.5],[1.8,.08,.9],'wood');
for(const x of [.6,2.2]) for(const z of [4.15,4.85]) box('Desk leg',[x,.35,z],[.07,.7,.07],'wood');
box('One meter calibration cube',[-1.7,.5,3.5],[1,1,1],'teal');
box('Floor reference A',[0,.008,0],[.12,.016,.12],'yellow');
box('Floor reference B, 2 m from A',[0,.008,2],[.12,.016,.12],'yellow');
json.buffers.push({byteLength:length});
const binary=Buffer.concat(chunks);
const jsonRaw=Buffer.from(JSON.stringify(json));
const jsonPadded=Buffer.concat([jsonRaw,Buffer.alloc((4-jsonRaw.length%4)%4,0x20)]);
const total=12+8+jsonPadded.length+8+binary.length;
const header=Buffer.alloc(12); header.writeUInt32LE(0x46546c67,0); header.writeUInt32LE(2,4); header.writeUInt32LE(total,8);
function chunkHeader(len,type) { const b=Buffer.alloc(8); b.writeUInt32LE(len,0); b.writeUInt32LE(type,4); return b; }
fs.writeFileSync(target,Buffer.concat([header,chunkHeader(jsonPadded.length,0x4e4f534a),jsonPadded,chunkHeader(binary.length,0x004e4942),binary]));
console.log(`Generated ${target} (${total} bytes, ${json.nodes.length} boxes)`);
