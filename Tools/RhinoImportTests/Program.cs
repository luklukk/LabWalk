using System;
using System.IO;
using System.Linq;
using System.Threading;
using Rhino;
using Rhino.Geometry;
using Rhino.FileIO;
using Rhino.DocObjects;
using LabWalk;
using Color=System.Drawing.Color;

static class Program
{
    static string destination;
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    static void Near(double a,double b,string message) { Check(Math.Abs(a-b)<0.0001,message+$": {a} != {b}"); }
    static Mesh Box(double x,double y,double z,double sx,double sy,double sz)
    {
        var mesh=new Mesh();
        double[][] p={new[]{x,y,z},new[]{x+sx,y,z},new[]{x+sx,y+sy,z},new[]{x,y+sy,z},new[]{x,y,z+sz},new[]{x+sx,y,z+sz},new[]{x+sx,y+sy,z+sz},new[]{x,y+sy,z+sz}};
        int[][] faces={new[]{0,3,2,1},new[]{4,5,6,7},new[]{0,1,5,4},new[]{1,2,6,5},new[]{2,3,7,6},new[]{3,0,4,7}};
        foreach(var f in faces)
        {
            int n=mesh.Vertices.Count;
            foreach(var index in f) mesh.Vertices.Add(p[index][0],p[index][1],p[index][2]);
            mesh.Faces.AddFace(n,n+1,n+2,n+3);
        }
        mesh.Normals.ComputeNormals();
        return mesh;
    }
    static byte[] Save(File3dm file,string name)
    {
        string path=Path.Combine(destination,name+".3dm");
        var options=new File3dmWriteOptions {Version=8}; options.EnableRenderMeshes(ObjectType.AnyObject,true);
        Check(file.Write(path,options),"Write fixture failed");
        return File.ReadAllBytes(path);
    }
    static double Min(RhinoModelData d,int axis)=>d.Meshes.SelectMany(m=>m.Positions.Where((v,i)=>i%3==axis)).Min();
    static double Max(RhinoModelData d,int axis)=>d.Meshes.SelectMany(m=>m.Positions.Where((v,i)=>i%3==axis)).Max();
    static void Reject(Action action,string text)
    { try { action(); } catch(InvalidDataException e) { Check(e.Message.Contains(text),e.Message); return; } throw new Exception("Expected rejection: "+text); }
    static void Main(string[] args)
    {
        destination=Path.GetFullPath(args.Length>0 ? args[0] : "TestResults/RhinoFixtures");
        Directory.CreateDirectory(destination);
        using(var file=new File3dm())
        {
            file.Settings.ModelUnitSystem=UnitSystem.Millimeters;
            file.Objects.AddMesh(Box(1000,2000,3000,1000,2000,3000),new ObjectAttributes {Name="Asymmetric box",ColorSource=ObjectColorSource.ColorFromObject,ObjectColor=Color.Red});
            var bytes=Save(file,"millimeter-box");
            var d=RhinoModelReader.Read(bytes,null,CancellationToken.None);
            Near(Min(d,0),1,"X origin"); Near(Min(d,1),3,"Z becomes Y"); Near(Min(d,2),2,"Y becomes Z");
            Near(Max(d,0)-Min(d,0),1,"Width"); Near(Max(d,1)-Min(d,1),3,"Height"); Near(Max(d,2)-Min(d,2),2,"Depth");
            Check(d.Triangles==12 && d.Meshes.Count==1,"Quad triangulation");
            Near(d.Meshes[0].R,1,"Object color"); Near(d.Meshes[0].G,0,"Object color green");
            foreach(var m in d.Meshes)
            for(int i=0;i<m.Triangles.Length;i+=3)
            {
                Point3d V(int index)=>new Point3d(m.Positions[index*3],m.Positions[index*3+1],m.Positions[index*3+2]);
                var a=m.Triangles[i]; var b=m.Triangles[i+1]; var c=m.Triangles[i+2];
                var n=Vector3d.CrossProduct(V(b)-V(a),V(c)-V(a));
                Check(n.X*m.Normals[a*3]+n.Y*m.Normals[a*3+1]+n.Z*m.Normals[a*3+2]>0,"Winding agrees with outward normal");
            }
            using(var cancelled=new CancellationTokenSource())
            {
                cancelled.Cancel(); bool caught=false;
                try { RhinoModelReader.Read(bytes,null,cancelled.Token); } catch(OperationCanceledException) { caught=true; }
                Check(caught,"Cancellation");
            }
            file.Settings.ModelUnitSystem=UnitSystem.Feet;
            var feet=RhinoModelReader.Read(Save(file,"feet-box"),null,CancellationToken.None);
            Near(Min(feet,0),304.8,"Feet conversion");
            file.Settings.ModelUnitSystem=UnitSystem.None;
            var unset=Save(file,"unset-units");
            Reject(()=>RhinoModelReader.Read(unset,null,CancellationToken.None),"unset or custom units");
            Near(Min(RhinoModelReader.Read(unset,0.001,CancellationToken.None),0),1,"Explicit override");
        }
        using(var file=new File3dm())
        {
            file.Settings.ModelUnitSystem=UnitSystem.Meters;
            file.AllLayers.Add(new Layer {Name="Default",IsVisible=true});
            int definition=file.AllInstanceDefinitions.Add("block","",Point3d.Origin,new GeometryBase[]{Box(0,0,0,1,2,3)},new[]{new ObjectAttributes {MaterialSource=ObjectMaterialSource.MaterialFromParent}});
            var id=file.AllInstanceDefinitions.First(d=>d.Name=="block").Id;
            var mirror=Transform.Scale(Plane.WorldXY,-2,3,1);
            file.Objects.AddInstanceObject(new InstanceReferenceGeometry(id,Transform.Translation(5,6,7)*mirror),new ObjectAttributes {ColorSource=ObjectColorSource.ColorFromObject,ObjectColor=Color.Blue});
            var parent=new Layer {Id=Guid.NewGuid(),Name="Hidden parent",IsVisible=false}; file.AllLayers.Add(parent);
            var child=new Layer {Id=Guid.NewGuid(),Name="Child",ParentLayerId=parent.Id}; file.AllLayers.Add(child); int layer=file.AllLayers.FindId(child.Id).Index;
            file.Objects.AddMesh(Box(100,100,100,10,10,10),new ObjectAttributes {LayerIndex=layer});
            var d=RhinoModelReader.Read(Save(file,"mirrored-block-hidden-layer"),null,CancellationToken.None);
            Check(d.Meshes.Count==1 && d.Hidden==1,"Block definitions not rendered twice; inherited layer visibility");
            Near(Min(d,0),3,"Mirrored block min"); Near(Max(d,0),5,"Mirrored block max");
            Near(Min(d,1),7,"Block vertical translation"); Near(Max(d,2),12,"Nonuniform block scale");
            Near(d.Meshes[0].B,1,"Inherited block color");
            var part=d.Meshes[0];
            for(int i=0;i<part.Triangles.Length;i+=3)
            {
                Point3d V(int j)=>new Point3d(part.Positions[j*3],part.Positions[j*3+1],part.Positions[j*3+2]);
                int a=part.Triangles[i],b=part.Triangles[i+1],c=part.Triangles[i+2];
                var normal=Vector3d.CrossProduct(V(b)-V(a),V(c)-V(a));
                Check(normal.X*part.Normals[a*3]+normal.Y*part.Normals[a*3+1]+normal.Z*part.Normals[a*3+2]>0,"Mirrored winding/normals");
            }
        }
        using(var file=new File3dm())
        {
            file.Settings.ModelUnitSystem=UnitSystem.Meters;
            var surface=new PlaneSurface(Plane.WorldXY,new Interval(0,2),new Interval(0,3)).ToBrep();
            using(var mesh=new Mesh())
            {
                mesh.Vertices.Add(0,0,0); mesh.Vertices.Add(2,0,0); mesh.Vertices.Add(2,3,0); mesh.Vertices.Add(0,3,0);
                mesh.Faces.AddFace(0,1,2,3); mesh.Normals.ComputeNormals();
                Check(surface.Faces[0].SetMesh(MeshType.Render,mesh),"Set render mesh");
            }
            file.Objects.AddBrep(surface);
            file.Objects.AddBrep(new Sphere(Point3d.Origin,2).ToBrep());
            file.Objects.AddCurve(new LineCurve(Point3d.Origin,new Point3d(1,2,3)));
            var d=RhinoModelReader.Read(Save(file,"cached-surface-and-missing"),null,CancellationToken.None);
            Check(d.Meshes.Count==1 && d.Skipped>=1 && d.Omitted==1,"Cached Brep + missing meshes counted apart from omitted curves");
            Check(d.Summary.Contains("INCOMPLETE"),"Persistent diagnostic");
        }
        Reject(()=>RhinoModelReader.Read(new byte[64],null,CancellationToken.None),"Expected a Rhino");
        using(var file=new File3dm())
        {
            file.Settings.ModelUnitSystem=UnitSystem.Meters;
            using(var profile=new Rectangle3d(Plane.WorldXY,2,3).ToNurbsCurve())
            using(var extrusion=Extrusion.Create(profile,4,true))
            using(var mesh=Box(0,0,0,2,3,4))
            {
                Check(extrusion.SetMesh(mesh,MeshType.Render),"Set extrusion render mesh");
                file.Objects.AddExtrusion(extrusion);
            }
            var d=RhinoModelReader.Read(Save(file,"cached-extrusion"),null,CancellationToken.None);
            Check(d.Meshes.Count==1 && d.Skipped==0,"Cached extrusion import");
            Near(Max(d,1),4,"Extrusion height");
        }
        using(var file=new File3dm())
        {
            // Many same-color parts (e.g. thousands of Brep faces) must merge, with index offsets and winding intact.
            file.Settings.ModelUnitSystem=UnitSystem.Meters;
            for(int i=0;i<24000;i++)
                file.Objects.AddMesh(Box(i%100*2,i/100*2,0,1,1,1),new ObjectAttributes {ColorSource=ObjectColorSource.ColorFromObject,ObjectColor=i%2==0 ? Color.Red : Color.Green});
            var d=RhinoModelReader.Read(Save(file,"many-parts"),null,CancellationToken.None);
            Check(d.Triangles==24000*12 && d.Vertices==24000*24,"All merged parts kept");
            Check(d.Meshes.Count==4 && d.Meshes.All(m=>m.Positions.Length/3<=250000),"Per-color batches split at vertex cap");
            foreach(var m in d.Meshes)
            {
                Check(m.Triangles.All(t=>t>=0 && t<m.Positions.Length/3),"Merged indices in range");
                for(int i=0;i<m.Triangles.Length;i+=3)
                {
                    Point3d V(int j)=>new Point3d(m.Positions[j*3],m.Positions[j*3+1],m.Positions[j*3+2]);
                    int a=m.Triangles[i],b=m.Triangles[i+1],c=m.Triangles[i+2];
                    var n=Vector3d.CrossProduct(V(b)-V(a),V(c)-V(a));
                    Check(n.X*m.Normals[a*3]+n.Y*m.Normals[a*3+1]+n.Z*m.Normals[a*3+2]>0,"Merged winding agrees with normals");
                }
            }
        }
        using(var file=new File3dm())
        {
            // Landmark points are read even on hidden layers, converted like geometry, and not counted as omitted.
            file.Settings.ModelUnitSystem=UnitSystem.Millimeters;
            file.AllLayers.Add(new Layer {Name="Default",IsVisible=true});
            var hidden=new Layer {Id=Guid.NewGuid(),Name="Landmarks",IsVisible=false}; file.AllLayers.Add(hidden);
            int layer=file.AllLayers.FindId(hidden.Id).Index;
            file.Objects.AddMesh(Box(0,0,0,1000,1000,1000));
            file.Objects.AddPoint(new Point3d(1000,2000,0),new ObjectAttributes {Name="LabWalk reference A",LayerIndex=layer});
            file.Objects.AddPoint(new Point3d(3000,2000,0),new ObjectAttributes {Name="LabWalk reference B",LayerIndex=layer});
            file.Objects.AddPoint(new Point3d(5,5,5),new ObjectAttributes {Name="Unrelated point"});
            var d=RhinoModelReader.Read(Save(file,"landmark-points"),null,CancellationToken.None);
            Check(d.ReferenceA!=null && d.ReferenceB!=null,"Landmark points found");
            Near(d.ReferenceA[0],1,"Landmark A x"); Near(d.ReferenceA[1],0,"Landmark A height"); Near(d.ReferenceA[2],2,"Landmark A Rhino Y becomes Z");
            Near(d.ReferenceB[0],3,"Landmark B x");
            Check(d.Omitted==1 && d.Summary.Contains("Landmarks A/B"),"Landmarks not counted as omitted; other points are");
            file.Objects.AddPoint(new Point3d(500,6000,1500),new ObjectAttributes {Name="LabWalk marker LW1",LayerIndex=layer});
            file.Objects.AddPoint(new Point3d(4000,6000,1200),new ObjectAttributes {Name="LabWalk marker LW2"});
            var withMarkers=RhinoModelReader.Read(Save(file,"marker-points"),null,CancellationToken.None);
            Check(withMarkers.Markers.Count==2 && withMarkers.Omitted==1 && withMarkers.Summary.Contains("2 calibration markers"),"Marker points read, not omitted");
            Near(withMarkers.Markers["LW1"][0],0.5,"Marker x"); Near(withMarkers.Markers["LW1"][1],1.5,"Marker height (Rhino Z)"); Near(withMarkers.Markers["LW1"][2],6,"Marker depth (Rhino Y)");
            file.Objects.AddPoint(new Point3d(0,0,1000),new ObjectAttributes {Name="LabWalk marker LW2"});
            Reject(()=>RhinoModelReader.Read(Save(file,"duplicate-marker"),null,CancellationToken.None),"more than one marker");
            file.Objects.AddPoint(new Point3d(0,0,0),new ObjectAttributes {Name="LabWalk reference A"});
            Reject(()=>RhinoModelReader.Read(Save(file,"duplicate-landmark"),null,CancellationToken.None),"more than one");
        }
        SampleRoom();
        Console.WriteLine("PASS: actual 3DM round trips, units/axes, quads/normals/colors, mirrored blocks, hidden layers, cached Breps, missing geometry, invalid file, cancellation, per-color merging, landmark and marker points.");
    }
    static void SampleRoom()
    {
        using(var file=new File3dm())
        {
            file.Settings.ModelUnitSystem=UnitSystem.Millimeters;
            void Add(string name,double x,double y,double z,double sx,double sy,double sz,Color color)
            {
                // Inputs are Unity-style meters; sample 3DM is Rhino Z-up millimeters.
                using(var mesh=Box((x-sx/2)*1000,(z-sz/2)*1000,(y-sy/2)*1000,sx*1000,sz*1000,sy*1000))
                    file.Objects.AddMesh(mesh,new ObjectAttributes {Name=name,ColorSource=ObjectColorSource.ColorFromObject,ObjectColor=color});
            }
            Add("Floor",0,-0.05,3,6,0.1,8,Color.SlateGray);
            Add("West wall",-2.95,1.5,3,0.1,3,8,Color.LightGray);
            Add("East wall",2.95,1.5,3,0.1,3,8,Color.LightGray);
            Add("Back wall",0,1.5,6.95,6,3,0.1,Color.LightGray);
            Add("Entry left",-1.8,1.5,-0.95,2.4,3,0.1,Color.LightGray);
            Add("Entry right",1.8,1.5,-0.95,2.4,3,0.1,Color.LightGray);
            Add("Door lintel",0,2.6,-0.95,1.2,0.8,0.1,Color.LightGray);
            Add("Desk top",1.4,0.75,4.5,1.8,0.08,0.9,Color.Tan);
            foreach(double x in new[]{0.6,2.2}) foreach(double z in new[]{4.15,4.85}) Add("Desk leg",x,0.35,z,0.07,0.7,0.07,Color.Tan);
            Add("1 m cube",-1.7,0.5,3.5,1,1,1,Color.Teal);
            Add("Reference A",0,0.008,0,0.12,0.016,0.12,Color.Gold);
            Add("Reference B",0,0.008,2,0.12,0.016,0.12,Color.Gold);
            var d=RhinoModelReader.Read(Save(file,"sample-room"),null,CancellationToken.None);
            Check(d.Meshes.Count==5 && d.Triangles==180,"Sample merged into one mesh per color");
            Near(Max(d,0)-Min(d,0),6,"Sample width"); Near(Max(d,1)-Min(d,1),3.1,"Sample height"); Near(Max(d,2)-Min(d,2),8,"Sample depth");
        }
    }
}
