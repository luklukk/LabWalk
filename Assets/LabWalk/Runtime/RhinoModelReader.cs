using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;

namespace LabWalk
{
    // No Unity objects here: native parsing and extraction run off the rendering thread.
    public sealed class RhinoModelData
    {
        public readonly List<RhinoMeshData> Meshes=new List<RhinoMeshData>();
        public readonly List<string> Warnings=new List<string>();
        public string Units;
        public double MetersPerUnit;
        // Skipped: surfaces/blocks that should have rendered but could not. Omitted: labels, curves and points, which never render.
        public int Skipped, Omitted, Hidden, Vertices, Triangles;
        // Alignment references from point objects named "LabWalk reference A"/"B" (any layer, even hidden),
        // in final Unity-local meters like the meshes. Null when the file does not define them.
        public float[] ReferenceA, ReferenceB;
        public const string ReferenceNameA="LabWalk reference A", ReferenceNameB="LabWalk reference B";
        // Calibration markers: points named "LabWalk marker <ID>" at the center of a printed QR code whose text is <ID>.
        public const string MarkerPrefix="LabWalk marker ";
        public readonly Dictionary<string,float[]> Markers=new Dictionary<string,float[]>();
        // The layer tree as switchable nodes ("Option: [<group> /] <name>" layers are exclusive; see LayerGroupNames).
        public readonly List<RhinoLayerGroup> Groups=new List<RhinoLayerGroup>();
        // Items to point at in the app (a tool, a duct run, a wall): a Rhino group; else all parts on one layer
        // named "<item> / <part>"; else a single top-level object (a block instance counts as one). Index 0 is
        // reserved for "no item"; every mesh and line vertex carries its item's index (ObjectIds).
        public readonly List<RhinoObjectInfo> Objects=new List<RhinoObjectInfo>{ null };
        public const string PartSeparator=" / ";
        // Feature edges (outlines and creases) of geometry on switchable layers, for their wireframe display.
        public readonly List<RhinoLineData> Lines=new List<RhinoLineData>();
        public string Summary => $"3DM: {Meshes.Count} meshes, {Triangles:N0} triangles; {Units} to meters. " +
            (Groups.Count>0 ? $"{Groups.Count} switchable layers. " : "") +
            (Skipped>0 ? $"INCOMPLETE: {Skipped} unsupported/missing items. " : "") +
            (Omitted>0 ? $"{Omitted} labels/curves/points not shown. " : "") +
            (ReferenceA!=null && ReferenceB!=null ? "Landmarks A/B read from file. " : "") +
            (Markers.Count>0 ? $"{Markers.Count} calibration markers. " : "") +
            "Basic colors; textures not imported.";
    }

    public sealed class RhinoMeshData
    {
        public string Name;
        public float[] Positions, Normals;
        public int[] Triangles;
        public float R,G,B;
        public int Group=-1; // index into RhinoModelData.Groups; -1 = always shown
        public float[] ObjectIds; // per vertex: index into RhinoModelData.Objects
    }

    public sealed class RhinoObjectInfo
    {
        public string Id;    // stable key: "group:<name>", "name:<layer path>|<item>", or the Rhino object GUID
        public string Name;  // group name, item name, object name, or block name; may be empty
        public int Group=-1; // layer node of the (first) object
        public int Parts;    // top-level Rhino objects in the item
    }

    // Line segments (pairs of indices into Positions) in final Unity-local meters.
    public sealed class RhinoLineData
    {
        public int Group;
        public float[] Positions;
        public int[] Indices;
        public float[] ObjectIds; // per vertex
    }

    // A Rhino layer whose name makes it switchable in the app. Everything on it and its sublayers belongs to it.
    public sealed class RhinoLayerGroup
    {
        public LayerGroupKind Kind;
        public string OptionGroup;  // options only: choices in the same group are mutually exclusive
        public string Name;
        public bool DefaultOn;      // the layer's visibility in the file
        public int Parent=-1;       // enclosing switchable layer, if nested (both must be on)
        public string LayerPath;
        public int Order;           // position in Rhino's layer table, to list layers as Rhino does
    }

    public enum LayerGroupKind { Toggle, Option }

    public static class LayerGroupNames
    {
        public const string DefaultOptionGroup="Design";
        static readonly System.Text.RegularExpressions.Regex Pattern=new System.Text.RegularExpressions.Regex(
            @"^\s*(toggle|option)\s*:\s*(.+?)\s*$",System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        // "Toggle: Tools" -> Toggle "Tools"; "Option: Renovation" -> Design/Renovation; "Option: Furniture / Layout A" -> Furniture/Layout A.
        public static bool TryParse(string layerName,out LayerGroupKind kind,out string optionGroup,out string name)
        {
            kind=LayerGroupKind.Toggle; optionGroup=null; name=null;
            var m=layerName==null ? null : Pattern.Match(layerName);
            if(m==null || !m.Success) return false;
            name=m.Groups[2].Value;
            if(m.Groups[1].Value.Equals("option",StringComparison.OrdinalIgnoreCase))
            {
                kind=LayerGroupKind.Option;
                var slash=name.IndexOf('/');
                optionGroup=slash>0 ? name.Substring(0,slash).Trim() : DefaultOptionGroup;
                if(slash>0) name=name.Substring(slash+1).Trim();
            }
            return name.Length>0 && (optionGroup==null || optionGroup.Length>0);
        }
    }

    public static class RhinoModelReader
    {
        // Source parts (Brep faces, meshes) are merged into per-color batches; MaxSourceParts bounds per-part overhead.
        const int MaxVertices=2000000, MaxTriangles=4000000, MaxSourceParts=200000, MaxBatchVertices=250000;

        public static RhinoModelData Read(byte[] bytes, double? explicitScale, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(bytes==null || bytes.Length<32 || System.Text.Encoding.ASCII.GetString(bytes,0,24)!="3D Geometry File Format ")
                throw new InvalidDataException("Expected a Rhino .3dm file.");
            using(var file=File3dm.FromByteArray(bytes))
            {
                if(file==null) throw new InvalidDataException("Rhino could not read this .3dm. Resave it in Rhino 8 or export GLB.");
                token.ThrowIfCancellationRequested();
                var units=file.Settings.ModelUnitSystem;
                var scale=explicitScale ?? RhinoMath.UnitScale(units,UnitSystem.Meters);
                // None/custom/unknown document units must never silently become meters.
                if(!explicitScale.HasValue && (units==UnitSystem.None || units==UnitSystem.CustomUnits || !Enum.IsDefined(typeof(UnitSystem),units) || (int)units==255))
                    throw new InvalidDataException("3DM has unset or custom units. In Rhino set Document Properties > Units (e.g. Feet or Millimeters) and save again.");
                if(double.IsNaN(scale) || double.IsInfinity(scale) || scale<=0)
                    throw new InvalidDataException("Invalid 3DM unit conversion.");
                var data=new RhinoModelData {Units=explicitScale.HasValue ? "explicit source units" : units.ToString(),MetersPerUnit=scale};
                var reader=new Reader(file,data,token);
                foreach(var obj in file.Objects)
                {
                    token.ThrowIfCancellationRequested();
                    if(!obj.Attributes.IsInstanceDefinitionObject)
                        reader.Object(obj,Transform.Identity,null,new HashSet<Guid>(),0,-1,0);
                }
                reader.Finish();
                if(data.Meshes.Count==0)
                    throw new InvalidDataException($"3DM contains no supported visible meshes ({data.Skipped} skipped). In Rhino use a Shaded view and Save with render meshes, or convert surfaces/SubD to meshes. Do not use Save Small.");
                return data;
            }
        }

        sealed class Reader
        {
            readonly File3dm file;
            readonly RhinoModelData data;
            readonly CancellationToken token;
            int visits, parts;

            // Merging keeps draw calls low on Quest: a lab of ~10k Brep faces becomes a few dozen meshes.
            sealed class Batch
            {
                public readonly List<float> Positions=new List<float>(), Normals=new List<float>(), ObjectIds=new List<float>();
                public readonly List<int> Triangles=new List<int>();
                public System.Drawing.Color Color;
                public bool HasNormals;
                public int Group;
                public int VertexCount => Positions.Count/3;
            }
            readonly Dictionary<(int,bool,int),Batch> open=new Dictionary<(int,bool,int),Batch>();
            readonly List<Batch> batches=new List<Batch>();

            public void Finish()
            {
                foreach(var b in batches)
                {
                    token.ThrowIfCancellationRequested();
                    data.Meshes.Add(new RhinoMeshData {
                        Name=$"Rhino #{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2} ({data.Meshes.Count+1})",
                        Positions=b.Positions.ToArray(),Normals=b.HasNormals ? b.Normals.ToArray() : null,Triangles=b.Triangles.ToArray(),
                        R=b.Color.R/255f,G=b.Color.G/255f,B=b.Color.B/255f,Group=b.Group,ObjectIds=b.ObjectIds.ToArray()});
                }
                batches.Clear(); open.Clear();
                foreach(var l in lineBatches)
                    if(l.Indices.Count>0) data.Lines.Add(new RhinoLineData {Group=l.Group,Positions=l.Positions.ToArray(),Indices=l.Indices.ToArray(),ObjectIds=l.ObjectIds.ToArray()});
                lineBatches.Clear(); openLines.Clear();
            }
            public Reader(File3dm file,RhinoModelData data,CancellationToken token)
            { this.file=file; this.data=data; this.token=token; }

            void Skip(string message)
            {
                data.Skipped++;
                if(data.Warnings.Count<32) data.Warnings.Add(message);
            }

            readonly Dictionary<int,(bool visible,int group,bool marked)> layerCache=new Dictionary<int,(bool,int,bool)>();
            readonly Dictionary<Guid,int> groupByLayer=new Dictionary<Guid,int>();

            // Returns whether an object is imported, its layer node, and whether its layer path has a marked
            // ("Option:"/"Toggle:") layer. A marked layer's own on/off state is its default in the app, not a
            // filter, so hidden ones are still imported. Below it, a sublayer turned off by the user stays
            // excluded; a sublayer that is only off because its marked parent is off keeps its remembered
            // (persistent) visibility.
            (bool visible,int group,bool marked) Resolve(ObjectAttributes attributes)
            {
                if(!attributes.Visible) return (false,-1,false);
                if(layerCache.TryGetValue(attributes.LayerIndex,out var cached)) return cached;
                var chain=new List<Layer>();
                var seen=new HashSet<Guid>();
                for(var layer=file.AllLayers.FindIndex(attributes.LayerIndex); layer!=null; layer=layer.ParentLayerId==Guid.Empty ? null : file.AllLayers.FindId(layer.ParentLayerId))
                {
                    if(!seen.Add(layer.Id)) throw new InvalidDataException("3DM has cyclic layer parents.");
                    chain.Add(layer); // leaf first
                }
                bool visible=true, insideMarked=false; int group=-1;
                // Walk root to leaf. Every layer on the way becomes a node of the app's view tree, mirroring the
                // Rhino layer structure; "Option:" layers are exclusive choices, all others (with or without the
                // "Toggle:" prefix) switch on their own.
                for(int i=chain.Count-1;i>=0;i--)
                {
                    var layer=chain[i];
                    var parentLayer=i+1<chain.Count ? chain[i+1] : null;
                    var ownOn=parentLayer==null || parentLayer.IsVisible ? layer.IsVisible : layer.GetPersistentVisibility();
                    var marked=LayerGroupNames.TryParse(layer.Name,out var kind,out var optionGroup,out var name);
                    if(!marked)
                    {
                        // Unmarked layers turned off in Rhino stay excluded (outside marked layers, every layer must be visible).
                        if(!(insideMarked ? ownOn : layer.IsVisible)) { visible=false; break; }
                        kind=LayerGroupKind.Toggle; optionGroup=null; name=layer.Name;
                    }
                    if(!groupByLayer.TryGetValue(layer.Id,out var index))
                    {
                        index=data.Groups.Count;
                        data.Groups.Add(new RhinoLayerGroup {Kind=kind,OptionGroup=optionGroup,Name=name,DefaultOn=!marked || ownOn,Parent=group,LayerPath=layer.FullPath,Order=layer.Index});
                        groupByLayer.Add(layer.Id,index);
                    }
                    group=index;
                    insideMarked|=marked;
                }
                var result=(visible,visible ? group : -1,insideMarked);
                layerCache[attributes.LayerIndex]=result;
                return result;
            }

            readonly Dictionary<string,int> itemByKey=new Dictionary<string,int>();

            // The item a top-level object belongs to (see RhinoModelData.Objects), created on first use.
            int ItemIndex(File3dmObject obj,int group)
            {
                var name=obj.Attributes.Name;
                string key, label;
                var groups=obj.Attributes.GroupCount>0 ? obj.Attributes.GetGroupList() : null;
                var split=string.IsNullOrEmpty(name) ? -1 : name.IndexOf(RhinoModelData.PartSeparator,StringComparison.Ordinal);
                if(groups!=null && groups.Length>0)
                {
                    var first=groups[0]; foreach(var g in groups) first=Math.Min(first,g);
                    var rhinoGroup=file.AllGroups.FindIndex(first);
                    label=string.IsNullOrEmpty(rhinoGroup?.Name) ? (split>0 ? name.Substring(0,split).Trim() : name) : rhinoGroup.Name;
                    key="group:"+(rhinoGroup?.Id.ToString() ?? first.ToString());
                }
                else if(split>0)
                {
                    label=name.Substring(0,split).Trim();
                    key="name:"+(group>=0 ? data.Groups[group].LayerPath : "")+"|"+label;
                }
                else
                {
                    label=name;
                    if(string.IsNullOrEmpty(label) && obj.Geometry is InstanceReferenceGeometry reference) label=file.AllInstanceDefinitions.FindId(reference.ParentIdefId)?.Name;
                    key=obj.Id.ToString();
                }
                if(!itemByKey.TryGetValue(key,out var index))
                {
                    index=data.Objects.Count;
                    data.Objects.Add(new RhinoObjectInfo {Id=key,Name=label ?? "",Group=group});
                    itemByKey.Add(key,index);
                }
                data.Objects[index].Parts++;
                return index;
            }

            System.Drawing.Color Color(ObjectAttributes a,System.Drawing.Color? parent)
            {
                if(a.MaterialSource==ObjectMaterialSource.MaterialFromParent && parent.HasValue) return parent.Value;
                var layer=file.AllLayers.FindIndex(a.LayerIndex);
                int index=a.MaterialSource==ObjectMaterialSource.MaterialFromObject ? a.MaterialIndex : layer?.RenderMaterialIndex ?? -1;
                var material=index>=0 ? file.AllMaterials.FindIndex(index) : null;
                if(material!=null) return material.DiffuseColor;
                if(a.ColorSource==ObjectColorSource.ColorFromParent && parent.HasValue) return parent.Value;
                return a.ColorSource==ObjectColorSource.ColorFromObject ? a.ObjectColor : layer?.Color ?? System.Drawing.Color.LightGray;
            }

            // group: the switchable layer of the enclosing block instance, inherited unless this object's own layer has one.
            // objectIndex: the top-level object this geometry belongs to (0: this is a top-level object, assign one).
            public void Object(File3dmObject obj,Transform transform,System.Drawing.Color? parent,HashSet<Guid> stack,int depth,int group,int objectIndex)
            {
                token.ThrowIfCancellationRequested();
                if(++visits>100000) throw new InvalidDataException("3DM exceeds the 100,000 object/instance limit. Simplify the model.");
                var geometry=obj.Geometry;
                if(geometry is Point marker && (obj.Attributes.Name==RhinoModelData.ReferenceNameA || obj.Attributes.Name==RhinoModelData.ReferenceNameB))
                {
                    var p=marker.Location; p.Transform(transform);
                    var value=new float[3];
                    Set(value,0,p.X*data.MetersPerUnit,p.Z*data.MetersPerUnit,p.Y*data.MetersPerUnit);
                    bool isA=obj.Attributes.Name==RhinoModelData.ReferenceNameA;
                    if((isA ? data.ReferenceA : data.ReferenceB)!=null) throw new InvalidDataException($"3DM has more than one point named \"{obj.Attributes.Name}\".");
                    if(isA) data.ReferenceA=value; else data.ReferenceB=value;
                    return;
                }
                if(geometry is Point markerPoint && obj.Attributes.Name!=null && obj.Attributes.Name.StartsWith(RhinoModelData.MarkerPrefix,StringComparison.Ordinal))
                {
                    var id=obj.Attributes.Name.Substring(RhinoModelData.MarkerPrefix.Length).Trim();
                    if(id.Length==0) throw new InvalidDataException("3DM marker point needs an ID after \""+RhinoModelData.MarkerPrefix+"\".");
                    if(data.Markers.ContainsKey(id)) throw new InvalidDataException($"3DM has more than one marker point with ID \"{id}\".");
                    var p=markerPoint.Location; p.Transform(transform);
                    var value=new float[3];
                    Set(value,0,p.X*data.MetersPerUnit,p.Z*data.MetersPerUnit,p.Y*data.MetersPerUnit);
                    data.Markers.Add(id,value);
                    return;
                }
                var (visible,ownGroup,ownMarked)=Resolve(obj.Attributes);
                if(!visible) { data.Hidden++; return; }
                // Top-level objects belong to their own layer. Parts of a block follow the block instance's layer,
                // unless they sit on a marked layer themselves (as before every layer became a node).
                if(ownGroup>=0 && (depth==0 || ownMarked)) group=ownGroup;
                var color=Color(obj.Attributes,parent);
                var name=string.IsNullOrEmpty(obj.Attributes.Name) ? obj.Id.ToString() : obj.Attributes.Name;
                if(objectIndex==0) objectIndex=ItemIndex(obj,group);
                if(geometry is InstanceReferenceGeometry instance)
                {
                    if(depth>=32 || !stack.Add(instance.ParentIdefId))
                        throw new InvalidDataException("3DM blocks are cyclic or nested more than 32 levels.");
                    try
                    {
                        var definition=file.AllInstanceDefinitions.FindId(instance.ParentIdefId);
                        if(definition==null) { Skip(name+": missing block definition"); return; }
                        var ids=definition.GetObjectIds();
                        if(ids.Length==0) { Skip(name+": empty or external linked block; embed it in Rhino"); return; }
                        foreach(var id in ids)
                        {
                            var child=file.Objects.FindId(id);
                            if(child==null) Skip(name+": missing block object");
                            else Object(child,transform*instance.Xform,color,stack,depth+1,group,objectIndex);
                        }
                    }
                    finally { stack.Remove(instance.ParentIdefId); }
                }
                else if(geometry is Mesh mesh) AddMesh(mesh,transform,color,name,group,objectIndex);
                else if(geometry is Brep brep)
                {
                    foreach(var face in brep.Faces)
                    {
                        using(var cached=face.GetMesh(MeshType.Render))
                        {
                            if(cached==null) Skip(name+": surface face has no saved render mesh");
                            else AddMesh(cached,transform,color,name,group,objectIndex);
                        }
                    }
                }
                else if(geometry is Extrusion extrusion)
                {
                    using(var cached=extrusion.GetMesh(MeshType.Render))
                    {
                        if(cached==null) Skip(name+": extrusion has no saved render mesh");
                        else AddMesh(cached,transform,color,name,group,objectIndex);
                    }
                }
                else if(geometry is Curve || geometry is Point || geometry is PointCloud || geometry is TextDot || geometry is AnnotationBase) data.Omitted++;
                else Skip(name+": unsupported "+geometry?.GetType().Name+" (convert to mesh in Rhino)");
            }

            void AddMesh(Mesh source,Transform transform,System.Drawing.Color color,string name,int group,int objectIndex)
            {
                token.ThrowIfCancellationRequested();
                int count=source.Vertices.Count, faces=source.Faces.Count;
                if(count==0 || faces==0) { Skip(name+": empty mesh"); return; }
                if(++parts>MaxSourceParts || count>MaxVertices-data.Vertices || faces>(MaxTriangles-data.Triangles)/2)
                    throw new InvalidDataException("3DM exceeds import limits (2M vertices / 4M triangles / 200k surface faces or meshes). Simplify it in Rhino.");
                if(!transform.TryGetInverse(out var inverse) || Math.Abs(transform.Determinant)<1e-15)
                    throw new InvalidDataException("3DM block has a singular transform.");
                var positions=new float[count*3];
                var normals=source.Normals.Count==count ? new float[count*3] : null;
                for(int i=0;i<count;i++)
                {
                    if((i&4095)==0) token.ThrowIfCancellationRequested();
                    var p=source.Vertices.Point3dAt(i);
                    p.Transform(transform);
                    // Rhino (right-handed, Z-up) -> Unity (left-handed, Y-up).
                    Set(positions,i,p.X*data.MetersPerUnit,p.Z*data.MetersPerUnit,p.Y*data.MetersPerUnit);
                    if(normals!=null)
                    {
                        var n=source.Normals[i];
                        var v=new Vector3d(inverse.M00*n.X+inverse.M10*n.Y+inverse.M20*n.Z,
                            inverse.M01*n.X+inverse.M11*n.Y+inverse.M21*n.Z,
                            inverse.M02*n.X+inverse.M12*n.Y+inverse.M22*n.Z);
                        if(!v.Unitize()) normals=null; // Unity recalculates normals for this part's batch.
                        else Set(normals,i,v.X,v.Z,v.Y);
                    }
                }
                var batch=BatchFor(color,normals!=null,group,count);
                int offset=batch.VertexCount, before=batch.Triangles.Count;
                bool flip=transform.Determinant>=0; // Axis swap reflects; mirrored blocks reflect a second time.
                for(int i=0;i<faces;i++)
                {
                    if((i&4095)==0) token.ThrowIfCancellationRequested();
                    var f=source.Faces[i];
                    if(!f.IsValid(count)) throw new InvalidDataException(name+": invalid mesh face index.");
                    Triangle(batch.Triangles,offset,f.A,f.B,f.C,flip);
                    if(f.IsQuad) Triangle(batch.Triangles,offset,f.A,f.C,f.D,flip);
                }
                if(group>=0) AddEdges(positions,batch.Triangles,before,offset,group,objectIndex);
                batch.Positions.AddRange(positions);
                for(int i=0;i<count;i++) batch.ObjectIds.Add(objectIndex);
                if(normals!=null) batch.Normals.AddRange(normals);
                data.Vertices+=count; data.Triangles+=(batch.Triangles.Count-before)/3;
            }

            // One batch per color and switchable layer, so each layer can be shown or hidden on its own.
            Batch BatchFor(System.Drawing.Color color,bool normals,int group,int incoming)
            {
                var key=(color.ToArgb(),normals,group);
                if(!open.TryGetValue(key,out var batch) || batch.VertexCount+incoming>MaxBatchVertices)
                {
                    batch=new Batch {Color=color,HasNormals=normals,Group=group};
                    open[key]=batch; batches.Add(batch);
                }
                return batch;
            }

            // ---- Feature edges for wireframe display ----
            const int MaxLineVertices=2000000, MaxLineBatchVertices=250000;
            const float CreaseCos=0.9397f; // edges between faces more than 20 degrees apart are drawn
            sealed class LineBatch { public readonly List<float> Positions=new List<float>(), ObjectIds=new List<float>(); public readonly List<int> Indices=new List<int>(); public int Group; }
            readonly Dictionary<int,LineBatch> openLines=new Dictionary<int,LineBatch>();
            readonly List<LineBatch> lineBatches=new List<LineBatch>();
            int lineVertices;
            bool lineLimitWarned;

            // One source mesh (already in meters): weld split vertices, then keep outline, crease and non-manifold
            // edges. Coplanar triangulation diagonals and the facets of smooth curved surfaces are dropped.
            void AddEdges(float[] positions,List<int> triangles,int start,int offset,int group,int objectIndex)
            {
                int n=positions.Length/3;
                var weld=new int[n];
                var ids=new Dictionary<(long,long,long),int>();
                var firstVertex=new List<int>();
                for(int i=0;i<n;i++)
                {
                    var key=((long)Math.Round(positions[i*3]*1e4),(long)Math.Round(positions[i*3+1]*1e4),(long)Math.Round(positions[i*3+2]*1e4));
                    if(!ids.TryGetValue(key,out var id)) { id=firstVertex.Count; ids.Add(key,id); firstVertex.Add(i); }
                    weld[i]=id;
                }
                var edges=new Dictionary<long,(int count,float nx,float ny,float nz,bool crease)>();
                for(int t=start;t+2<triangles.Count;t+=3)
                {
                    if(((t-start)&16383)==0) token.ThrowIfCancellationRequested();
                    int i0=triangles[t]-offset, i1=triangles[t+1]-offset, i2=triangles[t+2]-offset;
                    float ax=positions[i1*3]-positions[i0*3], ay=positions[i1*3+1]-positions[i0*3+1], az=positions[i1*3+2]-positions[i0*3+2];
                    float bx=positions[i2*3]-positions[i0*3], by=positions[i2*3+1]-positions[i0*3+1], bz=positions[i2*3+2]-positions[i0*3+2];
                    float nx=ay*bz-az*by, ny=az*bx-ax*bz, nz=ax*by-ay*bx;
                    var length=(float)Math.Sqrt(nx*nx+ny*ny+nz*nz);
                    if(length<1e-12f) continue; // degenerate sliver
                    nx/=length; ny/=length; nz/=length;
                    int a=weld[i0], b=weld[i1], c=weld[i2];
                    Edge(edges,a,b,nx,ny,nz); Edge(edges,b,c,nx,ny,nz); Edge(edges,c,a,nx,ny,nz);
                }
                LineBatch current=null;
                var lineIndex=new Dictionary<int,int>(); // welded vertex -> index in the current line batch
                foreach(var e in edges)
                {
                    if(e.Value.count==2 && !e.Value.crease) continue;
                    if(lineVertices+2>MaxLineVertices)
                    {
                        if(!lineLimitWarned && data.Warnings.Count<32) data.Warnings.Add("Wireframe edge limit reached; some layers show partial wireframes.");
                        lineLimitWarned=true; return;
                    }
                    var line=LineBatchFor(group,2);
                    if(line!=current) { current=line; lineIndex.Clear(); }
                    line.Indices.Add(LineVertex(line,lineIndex,(int)(e.Key>>32),firstVertex,positions,objectIndex));
                    line.Indices.Add(LineVertex(line,lineIndex,(int)(e.Key&0xffffffff),firstVertex,positions,objectIndex));
                }
            }

            static void Edge(Dictionary<long,(int count,float nx,float ny,float nz,bool crease)> edges,int a,int b,float nx,float ny,float nz)
            {
                if(a==b) return;
                var key=a<b ? ((long)a<<32)|(uint)b : ((long)b<<32)|(uint)a;
                if(!edges.TryGetValue(key,out var e)) { edges[key]=(1,nx,ny,nz,false); return; }
                // Winding is consistent within a mesh, so the two faces' normals compare directly.
                // A third face on the same edge (non-manifold) always marks it.
                var dot=e.nx*nx+e.ny*ny+e.nz*nz;
                edges[key]=(e.count+1,e.nx,e.ny,e.nz,e.crease || e.count>=2 || dot<CreaseCos);
            }

            int LineVertex(LineBatch line,Dictionary<int,int> lineIndex,int welded,List<int> firstVertex,float[] positions,int objectIndex)
            {
                if(lineIndex.TryGetValue(welded,out var index)) return index;
                var vertex=firstVertex[welded];
                index=line.Positions.Count/3;
                line.Positions.Add(positions[vertex*3]); line.Positions.Add(positions[vertex*3+1]); line.Positions.Add(positions[vertex*3+2]);
                line.ObjectIds.Add(objectIndex);
                lineIndex[welded]=index; lineVertices++;
                return index;
            }

            LineBatch LineBatchFor(int group,int incoming)
            {
                if(!openLines.TryGetValue(group,out var line) || line.Positions.Count/3+incoming>MaxLineBatchVertices)
                {
                    line=new LineBatch {Group=group};
                    openLines[group]=line; lineBatches.Add(line);
                }
                return line;
            }

            static void Triangle(List<int> output,int offset,int a,int b,int c,bool flip)
            { output.Add(offset+a); output.Add(offset+(flip ? c : b)); output.Add(offset+(flip ? b : c)); }
            static void Set(float[] target,int i,double x,double y,double z)
            {
                if(!Finite(x)||!Finite(y)||!Finite(z) || Math.Abs(x)>100000 || Math.Abs(y)>100000 || Math.Abs(z)>100000)
                    throw new InvalidDataException("3DM contains invalid coordinates or is over 100 km from the origin. Move it near the Rhino world origin.");
                target[i*3]=(float)x; target[i*3+1]=(float)y; target[i*3+2]=(float)z;
            }
            static bool Finite(double v) { return !double.IsNaN(v)&&!double.IsInfinity(v); }
        }
    }
}
