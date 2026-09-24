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
        public string Summary => $"3DM: {Meshes.Count} meshes, {Triangles:N0} triangles; {Units} to meters. " +
            (Skipped>0 ? $"INCOMPLETE: {Skipped} unsupported/missing items. " : "") +
            (Omitted>0 ? $"{Omitted} labels/curves/points not shown. " : "") +
            (ReferenceA!=null && ReferenceB!=null ? "Landmarks A/B read from file. " : "") +
            "Basic colors; textures not imported.";
    }

    public sealed class RhinoMeshData
    {
        public string Name;
        public float[] Positions, Normals;
        public int[] Triangles;
        public float R,G,B;
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
                        reader.Object(obj,Transform.Identity,null,new HashSet<Guid>(),0);
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
                public readonly List<float> Positions=new List<float>(), Normals=new List<float>();
                public readonly List<int> Triangles=new List<int>();
                public System.Drawing.Color Color;
                public bool HasNormals;
                public int VertexCount => Positions.Count/3;
            }
            readonly Dictionary<(int,bool),Batch> open=new Dictionary<(int,bool),Batch>();
            readonly List<Batch> batches=new List<Batch>();

            public void Finish()
            {
                foreach(var b in batches)
                {
                    token.ThrowIfCancellationRequested();
                    data.Meshes.Add(new RhinoMeshData {
                        Name=$"Rhino #{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2} ({data.Meshes.Count+1})",
                        Positions=b.Positions.ToArray(),Normals=b.HasNormals ? b.Normals.ToArray() : null,Triangles=b.Triangles.ToArray(),
                        R=b.Color.R/255f,G=b.Color.G/255f,B=b.Color.B/255f});
                }
                batches.Clear(); open.Clear();
            }
            public Reader(File3dm file,RhinoModelData data,CancellationToken token)
            { this.file=file; this.data=data; this.token=token; }

            void Skip(string message)
            {
                data.Skipped++;
                if(data.Warnings.Count<32) data.Warnings.Add(message);
            }

            bool Visible(ObjectAttributes attributes)
            {
                if(!attributes.Visible) return false;
                var layer=file.AllLayers.FindIndex(attributes.LayerIndex);
                var seen=new HashSet<Guid>();
                while(layer!=null)
                {
                    if(!seen.Add(layer.Id)) throw new InvalidDataException("3DM has cyclic layer parents.");
                    if(!layer.IsVisible) return false;
                    if(layer.ParentLayerId==Guid.Empty) break;
                    layer=file.AllLayers.FindId(layer.ParentLayerId);
                }
                return true;
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

            public void Object(File3dmObject obj,Transform transform,System.Drawing.Color? parent,HashSet<Guid> stack,int depth)
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
                if(!Visible(obj.Attributes)) { data.Hidden++; return; }
                var color=Color(obj.Attributes,parent);
                var name=string.IsNullOrEmpty(obj.Attributes.Name) ? obj.Id.ToString() : obj.Attributes.Name;
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
                            else Object(child,transform*instance.Xform,color,stack,depth+1);
                        }
                    }
                    finally { stack.Remove(instance.ParentIdefId); }
                }
                else if(geometry is Mesh mesh) AddMesh(mesh,transform,color,name);
                else if(geometry is Brep brep)
                {
                    foreach(var face in brep.Faces)
                    {
                        using(var cached=face.GetMesh(MeshType.Render))
                        {
                            if(cached==null) Skip(name+": surface face has no saved render mesh");
                            else AddMesh(cached,transform,color,name);
                        }
                    }
                }
                else if(geometry is Extrusion extrusion)
                {
                    using(var cached=extrusion.GetMesh(MeshType.Render))
                    {
                        if(cached==null) Skip(name+": extrusion has no saved render mesh");
                        else AddMesh(cached,transform,color,name);
                    }
                }
                else if(geometry is Curve || geometry is Point || geometry is PointCloud || geometry is TextDot || geometry is AnnotationBase) data.Omitted++;
                else Skip(name+": unsupported "+geometry?.GetType().Name+" (convert to mesh in Rhino)");
            }

            void AddMesh(Mesh source,Transform transform,System.Drawing.Color color,string name)
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
                var batch=BatchFor(color,normals!=null,count);
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
                batch.Positions.AddRange(positions);
                if(normals!=null) batch.Normals.AddRange(normals);
                data.Vertices+=count; data.Triangles+=(batch.Triangles.Count-before)/3;
            }

            Batch BatchFor(System.Drawing.Color color,bool normals,int incoming)
            {
                var key=(color.ToArgb(),normals);
                if(!open.TryGetValue(key,out var batch) || batch.VertexCount+incoming>MaxBatchVertices)
                {
                    batch=new Batch {Color=color,HasNormals=normals};
                    open[key]=batch; batches.Add(batch);
                }
                return batch;
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
