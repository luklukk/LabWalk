// Camera QR marker tests without a headset: renders synthetic passthrough-camera images of the real marker
// codes (same generator settings as Tools/MarkerSheet) at known 3D poses through a pinhole camera, then runs
// the headset's decoder (QrFrameDecoder) and locator (QrPose) and checks the recovered code centers.
// Camera space matches Meta's PassthroughCameraAccess: x right, y up, z forward; viewport origin bottom-left.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LabWalk;
using LabWalk.Core;
using QRCoder;

static class Program
{
    const int Width=1280, Height=960;
    const double Focal=870, CodeSize=0.15; // focal length in pixels (about 72 degrees wide), printed code edge in meters

    sealed class Code { public string Text; public bool[,] Modules; public Point3 Center, Right, Up; }

    static readonly PinholeCamera Camera=new PinholeCamera {Width=Width,Height=Height,Fx=Focal,Fy=Focal,Cx=Width/2.0,Cy=Height/2.0};

    static int Main()
    {
        var failures=0;
        var cases=new List<(string name,Code[] codes,double tolerance)>
        {
            ("LW1 facing, 1.0 m",new[]{Make("LW1",new Point3(0,0,1.0),0,0,0)},0.01),
            ("LW2 facing, 0.6 m, off center",new[]{Make("LW2",new Point3(0.12,-0.08,0.6),0,0,0)},0.01),
            ("LW2 turned 20 deg, 0.5 m",new[]{Make("LW2",new Point3(0.05,0.03,0.5),20,0,0)},0.01),
            ("LW3 turned 25 deg, off to the side, 1.2 m",new[]{Make("LW3",new Point3(-0.25,0.1,1.2),25,0,0)},0.015),
            ("LW1 turned -30 deg, tilted 15, 1.5 m",new[]{Make("LW1",new Point3(0.3,0.2,1.5),-30,15,0)},0.025),
            ("LW2 rotated 90 deg in plane, 1.0 m",new[]{Make("LW2",new Point3(0,0,1.0),10,0,90)},0.01),
            ("LW3 upside down, 0.9 m",new[]{Make("LW3",new Point3(-0.1,0,0.9),0,-10,180)},0.01),
            ("LW1 facing, 2.0 m",new[]{Make("LW1",new Point3(0,0.1,2.0),0,0,0)},0.03),
            ("LW1 + LW3 in one frame, 1.3 m",new[]{Make("LW1",new Point3(-0.3,0.1,1.3),20,0,0),Make("LW3",new Point3(0.3,-0.2,1.4),-5,0,0)},0.02),
        };
        // Geometry alone: exact finder centers projected through the camera, no image decoding.
        foreach(var (name,codes,_) in cases)
        foreach(var code in codes)
        {
            var n=code.Modules.GetLength(0); var half=CodeSize/2-3.5*CodeSize/n;
            Point3 At(double a,double b) => code.Center+Scale(code.Right,a)+Scale(code.Up,b);
            // Ideal apparent module size at each finder: focal length x module size / depth.
            double Size(Point3 p) => Focal*(CodeSize/n)/Length(p);
            var bl=At(-half,-half); var tl=At(-half,half); var tr=At(half,half);
            var candidates=QrPose.SolveAll(new Point3(0,0,0),bl,tl,tr,QrPose.FinderSpacing(CodeSize,1)).Count;
            var ok=QrPose.TrySolve(new Point3(0,0,0),bl,tl,tr,QrPose.FinderSpacing(CodeSize,1),new[]{Size(bl),Size(tl),Size(tr)},out var exact);
            var error=ok ? Length(exact.Center-code.Center) : double.NaN;
            if(!(error<0.0005)) failures++;
            Console.WriteLine($"{(error<0.0005 ? "PASS" : "FAIL")} geometry {name} {code.Text}: {candidates} poses fit, chosen center error {error*1000:F2} mm");
        }
        byte[] luminance=null;
        var dump=Environment.GetEnvironmentVariable("SCANTESTS_DUMP");
        foreach(var (name,codes,tolerance) in cases)
        {
            var image=Render(codes);
            if(!string.IsNullOrEmpty(dump)) SavePng(System.IO.Path.Combine(dump,string.Concat(name.Where(char.IsLetterOrDigit))+".png"),image);
            var timer=Stopwatch.StartNew();
            var found=QrFrameDecoder.Decode(image,Camera,CodeSize,ref luminance);
            var ms=timer.Elapsed.TotalMilliseconds;
            foreach(var code in codes)
            {
                var match=found.Where(f=>f.Text==code.Text).ToList();
                if(match.Count!=1) { Console.WriteLine($"FAIL {name}: {code.Text} decoded {match.Count} times ({found.Count} codes, {ms:F0} ms)"); failures++; continue; }
                var f=match[0];
                var error=Length(f.Pose.Center-code.Center);
                var pass=f.Version==1 && error<=tolerance;
                if(!pass) failures++;
                Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}: {code.Text} v{f.Version}, {f.Candidates} poses fit, center error {error*1000:F1} mm (limit {tolerance*1000:F0}), distance {f.Pose.Distance:F3} m, decode {ms:F0} ms");
            }
        }
        // Working range (reported, not pass/fail): which distances and angles decode, and the center error.
        Console.WriteLine("Range (code turned about the vertical axis): center error in mm by distance and angle, '--' = not decoded. Face the codes: finders are not found beyond about 40 degrees.");
        var angles=new[]{0,20,35,50,60,70};
        Console.WriteLine("        "+string.Join("",angles.Select(a=>$"{a,6}deg")));
        foreach(var distance in new[]{0.5,0.8,1.0,1.2,1.5,2.0,2.5})
        {
            var line=$"{distance,5:F1} m ";
            foreach(var angle in angles)
            {
                var code=Make("LW2",new Point3(0.05,0.03,distance),angle,0,0);
                var f=QrFrameDecoder.Decode(Render(new[]{code}),Camera,CodeSize,ref luminance).FirstOrDefault(x=>x.Text=="LW2");
                line+=f.Text==null ? "      --  " : $"{Length(f.Pose.Center-code.Center)*1000,8:F1}  ";
            }
            Console.WriteLine(line);
        }
        // Image row order: read the wrong way up, a code must come back flagged as mirrored (the headset uses this
        // to correct its assumption about the camera image); read the right way, not mirrored.
        {
            var code=Make("LW2",new Point3(0.05,0.03,0.8),15,0,0);
            var image=Render(new[]{code});
            var topDown=new byte[image.Length];
            for(int row=0;row<Height;row++) Array.Copy(image,row*Width*4,topDown,(Height-1-row)*Width*4,Width*4);
            var wrongWay=QrFrameDecoder.Decode(image,Camera,CodeSize,ref luminance,rowsTopDown:true);
            var rightWay=QrFrameDecoder.Decode(topDown,Camera,CodeSize,ref luminance,rowsTopDown:true);
            var pass=wrongWay.Count==1 && wrongWay[0].Mirrored && rightWay.Count==1 && !rightWay[0].Mirrored && Length(rightWay[0].Pose.Center-code.Center)<0.01;
            if(!pass) failures++;
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")} row order: wrong way up read as mirrored={wrongWay.FirstOrDefault().Mirrored} ({wrongWay.Count} codes); right way up mirrored={rightWay.FirstOrDefault().Mirrored} ({rightWay.Count} codes)");
        }
        // A frame without any code must decode to nothing.
        if(QrFrameDecoder.Decode(Render(new Code[0]),Camera,CodeSize,ref luminance).Count!=0) { Console.WriteLine("FAIL empty frame decoded something"); failures++; }
        else Console.WriteLine("PASS empty frame: no codes");
        Console.WriteLine(failures==0 ? "All camera marker tests passed. Synthetic images; real camera noise, blur and lens distortion are not modeled." : $"{failures} failures");
        return failures==0 ? 0 : 1;
    }

    // Pinhole ray through a viewport point (0..1, origin bottom-left), as PassthroughCameraAccess.ViewportPointToRay with no crop.
    static Point3 Ray(double vx,double vy) => Camera.Ray(vx*Width,vy*Height);

    static Code Make(string text,Point3 center,double yawDegrees,double pitchDegrees,double rollDegrees)
    {
        using var generator=new QRCodeGenerator();
        using var data=generator.CreateQrCode(text,QRCodeGenerator.ECCLevel.M); // as Tools/MarkerSheet
        const int quiet=4;
        var n=data.ModuleMatrix.Count-2*quiet;
        var modules=new bool[n,n];
        for(int y=0;y<n;y++) for(int x=0;x<n;x++) modules[y,x]=data.ModuleMatrix[y+quiet][x+quiet];
        // Facing the camera: right = +x, up = +y. Roll in the code plane, then pitch about x, then yaw about y.
        Point3 right=new Point3(1,0,0), up=new Point3(0,1,0);
        right=Yaw(Pitch(Roll(right,rollDegrees),pitchDegrees),yawDegrees);
        up=Yaw(Pitch(Roll(up,rollDegrees),pitchDegrees),yawDegrees);
        return new Code {Text=text,Modules=modules,Center=center,Right=right,Up=up};
    }

    static byte[] Render(Code[] codes)
    {
        var rgba=new byte[Width*Height*4];
        var random=new Random(7);
        for(int row=0;row<Height;row++) // row 0 is the bottom (Unity texture order)
        for(int col=0;col<Width;col++)
        {
            double sum=0;
            for(int s=0;s<4;s++)
            {
                var d=Ray((col+0.25+0.5*(s%2))/Width,(row+0.25+0.5*(s/2))/Height);
                sum+=Shade(codes,d);
            }
            var value=(byte)Math.Clamp(sum/4+random.Next(-6,7),0,255);
            var i=(row*Width+col)*4;
            rgba[i]=value; rgba[i+1]=value; rgba[i+2]=value; rgba[i+3]=255;
        }
        return rgba;
    }

    static double Shade(Code[] codes,Point3 d)
    {
        double nearest=double.MaxValue, shade=140; // wall
        foreach(var c in codes)
        {
            var normal=Cross(c.Right,c.Up);
            var denominator=Dot(d,normal);
            if(Math.Abs(denominator)<1e-9) continue;
            var t=Dot(c.Center,normal)/denominator;
            if(t<=0 || t>=nearest) continue;
            var offset=new Point3(d.X*t,d.Y*t,d.Z*t)-c.Center;
            double a=Dot(offset,c.Right), b=Dot(offset,c.Up);
            if(Math.Abs(a)>0.108 || Math.Abs(b)>0.108) continue; // outside the letter sheet (code centered, 3.3 cm side margins)
            nearest=t;
            var n=c.Modules.GetLength(0); var module=CodeSize/n;
            var x=(int)Math.Floor((a+CodeSize/2)/module); var y=(int)Math.Floor((CodeSize/2-b)/module);
            shade=x>=0 && y>=0 && x<n && y<n && c.Modules[y,x] ? 30 : 225;
        }
        return shade;
    }

    // Grayscale PNG of a rendered frame (top row first), for looking at failures.
    static void SavePng(string path,byte[] rgba)
    {
        var raw=new System.IO.MemoryStream();
        for(int y=Height-1;y>=0;y--) { raw.WriteByte(0); for(int x=0;x<Width;x++) raw.WriteByte(rgba[(y*Width+x)*4]); }
        var packed=new System.IO.MemoryStream();
        using(var z=new System.IO.Compression.ZLibStream(packed,System.IO.Compression.CompressionLevel.Fastest,true)) raw.WriteTo(z);
        using var file=System.IO.File.Create(path);
        file.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string type,byte[] data)
        {
            var length=BitConverter.GetBytes(data.Length); Array.Reverse(length); file.Write(length);
            var body=System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray(); file.Write(body);
            uint c=0xFFFFFFFF;
            foreach(var b in body) { c^=b; for(int k=0;k<8;k++) c=(c&1)!=0 ? 0xEDB88320^(c>>1) : c>>1; }
            var crc=BitConverter.GetBytes(~c); Array.Reverse(crc); file.Write(crc);
        }
        var header=new byte[13];
        var w=BitConverter.GetBytes(Width); Array.Reverse(w); w.CopyTo(header,0);
        var h=BitConverter.GetBytes(Height); Array.Reverse(h); h.CopyTo(header,4);
        header[8]=8; header[9]=0;
        Chunk("IHDR",header); Chunk("IDAT",packed.ToArray()); Chunk("IEND",new byte[0]);
    }

    static Point3 Roll(Point3 p,double degrees) { var r=degrees*Math.PI/180; return new Point3(p.X*Math.Cos(r)-p.Y*Math.Sin(r),p.X*Math.Sin(r)+p.Y*Math.Cos(r),p.Z); }
    static Point3 Pitch(Point3 p,double degrees) { var r=degrees*Math.PI/180; return new Point3(p.X,p.Y*Math.Cos(r)-p.Z*Math.Sin(r),p.Y*Math.Sin(r)+p.Z*Math.Cos(r)); }
    static Point3 Yaw(Point3 p,double degrees) { var r=degrees*Math.PI/180; return new Point3(p.X*Math.Cos(r)+p.Z*Math.Sin(r),p.Y,-p.X*Math.Sin(r)+p.Z*Math.Cos(r)); }
    static double Dot(Point3 a,Point3 b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    static Point3 Cross(Point3 a,Point3 b) => new Point3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    static double Length(Point3 a) => Math.Sqrt(Dot(a,a));
    static Point3 Scale(Point3 a,double s) => new Point3(a.X*s,a.Y*s,a.Z*s);
}
