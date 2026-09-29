using System;
using System.Collections.Generic;
using LabWalk.Core;
using ZXing;
using ZXing.Common;
using ZXing.Multi.QrCode.Internal;
using ZXing.QrCode.Internal;

namespace LabWalk
{
    // Pinhole model of one camera image: pixel u to the right, v up from the bottom edge (Unity texture order).
    // Camera space is x right, y up, z forward, as in Meta's PassthroughCameraAccess.
    public struct PinholeCamera
    {
        public int Width, Height;
        public double Fx, Fy, Cx, Cy;
        public Point3 Ray(double u, double v) { return new Point3((u-Cx)/Fx,(v-Cy)/Fy,1); }
        public bool Project(Point3 p, out double u, out double v)
        {
            u=v=0;
            if(p.Z<=1e-6) return false;
            u=Fx*p.X/p.Z+Cx; v=Fy*p.Y/p.Z+Cy;
            return true;
        }
    }

    // A QR code found and located in one camera image, in camera space (meters).
    public struct QrFinding
    {
        public string Text;
        public QrPose.Result Pose;
        public int Version;
        public int Candidates; // poses that fit the three finder centers
        public bool Mirrored;  // read as a mirror image: printed codes never are, so the image rows are the other way up
    }

    // Finds, reads and locates the printed QR markers in a camera image. ZXing finds the three finder patterns;
    // QrPose turns them into the code's 3D pose (using the printed size); the modules are then sampled through
    // that pose with full perspective and decoded with ZXing's decoder. ZXing's own sampling assumes a flat
    // parallelogram for small (version 1) codes, which misreads codes seen at an angle; sampling through the
    // pose fixes that, and a successful decode (with error correction) also confirms which pose is right.
    // No Unity types, so it runs on a worker thread and in the desktop tests (Tools/ScanTests).
    public static class QrFrameDecoder
    {
        static readonly Dictionary<DecodeHintType,object> Hints=new Dictionary<DecodeHintType,object> {
            {DecodeHintType.POSSIBLE_FORMATS,new List<BarcodeFormat>{BarcodeFormat.QR_CODE}},
            {DecodeHintType.TRY_HARDER,true},
        };

        // rgba: width * height * 4 bytes, rows bottom-up (rowsTopDown: top-down). codeSize: printed code edge in meters
        // (modules only). luminance: optional reusable buffer.
        public static List<QrFinding> Decode(byte[] rgba, PinholeCamera camera, double codeSize, ref byte[] luminance, bool rowsTopDown=false)
        {
            int width=camera.Width, height=camera.Height, count=width*height;
            if(luminance==null || luminance.Length!=count) luminance=new byte[count];
            // ZXing expects rows top-down.
            for(int y=0;y<height;y++)
            {
                var source=(rowsTopDown ? y : height-1-y)*width*4; var target=y*width;
                for(int x=0;x<width;x++,source+=4)
                    luminance[target+x]=(byte)((rgba[source]*77+rgba[source+1]*150+rgba[source+2]*29)>>8);
            }
            var black=new HybridBinarizer(new RGBLuminanceSource(luminance,width,height,RGBLuminanceSource.BitmapFormat.Gray8)).BlackMatrix;
            var findings=new List<QrFinding>();
            if(black==null) return findings;
            // With several codes in view ZXing can group finders from different codes; after each successful read,
            // blank that code out and search again so the remaining finders group correctly.
            for(int pass=0;pass<4;pass++)
            {
                var read=false;
                foreach(var triplet in FindTriplets(black))
                {
                    if(!TryRead(black,camera,codeSize,triplet,out var finding)) continue;
                    Erase(black,camera,finding);
                    read=true;
                    if(!findings.Exists(f=>f.Text==finding.Text)) findings.Add(finding);
                }
                if(!read) break;
            }
            return findings;
        }

        // Clears the code and one module around it from the black-and-white image.
        static void Erase(BitMatrix black, PinholeCamera camera, QrFinding finding)
        {
            var p=finding.Pose; var n=17+4*finding.Version;
            var right=p.TopRight-p.TopLeft; var down=p.BottomLeft-p.TopLeft;
            var step=1.0/(n-7);
            var corners=new (double x,double y)[4];
            var extent=new[]{ (-4.5,-4.5), (n-2.5,-4.5), (n-2.5,n-2.5), (-4.5,n-2.5) }; // in modules from the top-left finder center
            for(int i=0;i<4;i++)
            {
                double a=extent[i].Item1*step, b=extent[i].Item2*step;
                var q=new Point3(p.TopLeft.X+right.X*a+down.X*b,p.TopLeft.Y+right.Y*a+down.Y*b,p.TopLeft.Z+right.Z*a+down.Z*b);
                if(!camera.Project(q,out var u,out var v)) return;
                corners[i]=(u,camera.Height-v);
            }
            int minX=black.Width, minY=black.Height, maxX=0, maxY=0;
            foreach(var c in corners) { minX=Math.Min(minX,(int)c.x); maxX=Math.Max(maxX,(int)c.x+1); minY=Math.Min(minY,(int)c.y); maxY=Math.Max(maxY,(int)c.y+1); }
            minX=Math.Max(0,minX); minY=Math.Max(0,minY); maxX=Math.Min(black.Width-1,maxX); maxY=Math.Min(black.Height-1,maxY);
            for(int y=minY;y<=maxY;y++)
            for(int x=minX;x<=maxX;x++)
            {
                // Inside the convex quad when on the same side of all four edges.
                int sign=0; var inside=true;
                for(int i=0;i<4 && inside;i++)
                {
                    var a=corners[i]; var b=corners[(i+1)%4];
                    var cross=(b.x-a.x)*(y+0.5-a.y)-(b.y-a.y)*(x+0.5-a.x);
                    var s=cross>0 ? 1 : cross<0 ? -1 : 0;
                    if(s!=0) { if(sign==0) sign=s; else if(s!=sign) inside=false; }
                }
                if(inside) black[x,y]=false;
            }
        }

        static List<FinderPatternInfo> FindTriplets(BitMatrix black)
        {
            var triplets=new List<FinderPatternInfo>();
            try { var multi=new MultiFinderPatternFinder(black,null).findMulti(Hints); if(multi!=null) triplets.AddRange(multi); }
            catch(ReaderException) { }
            if(triplets.Count==0)
            {
                // The single-code detector's first three points are the finders (bottom-left, top-left, top-right).
                try
                {
                    var single=new Detector(black).detect(Hints);
                    if(single?.Points!=null && single.Points.Length>=3 && single.Points[0] is FinderPattern bl && single.Points[1] is FinderPattern tl && single.Points[2] is FinderPattern tr)
                        triplets.Add(new FinderPatternInfo(new[]{bl,tl,tr}));
                }
                catch(ReaderException) { }
            }
            return triplets;
        }

        static bool TryRead(BitMatrix black, PinholeCamera camera, double codeSize, FinderPatternInfo info, out QrFinding finding)
        {
            finding=default;
            var height=camera.Height;
            Point3 RayTo(ResultPoint p) => camera.Ray(p.X,height-p.Y);
            double Pixels(ResultPoint a, ResultPoint b) { var dx=a.X-b.X; var dy=a.Y-b.Y; return Math.Sqrt(dx*dx+dy*dy); }
            var finders=new[]{ info.BottomLeft, info.TopLeft, info.TopRight };
            var sizes=new double[3]; double module=0;
            for(int i=0;i<3;i++) { sizes[i]=finders[i].EstimatedModuleSize; module+=sizes[i]; }
            module/=3;
            if(module<=0) return false;
            var estimate=QrPose.VersionFromSpan((Pixels(finders[1],finders[0])+Pixels(finders[1],finders[2]))/2/module);
            // Try the estimated version first, then its neighbors; markers are small codes (Meta's limit is version 10).
            foreach(var version in new[]{ estimate, estimate-1, estimate+1 })
            {
                if(version<1 || version>10) continue;
                var candidates=QrPose.SolveAll(new Point3(0,0,0),RayTo(finders[0]),RayTo(finders[1]),RayTo(finders[2]),QrPose.FinderSpacing(codeSize,version));
                // Candidates in order of how well their depths explain the finder sizes; the first that decodes wins.
                candidates.Sort((a,b)=>QrPose.SizeMismatch(new Point3(0,0,0),a,sizes).CompareTo(QrPose.SizeMismatch(new Point3(0,0,0),b,sizes)));
                foreach(var pose in candidates)
                {
                    var bits=Sample(black,camera,pose,version);
                    if(bits==null) continue;
                    DecoderResult result=null;
                    try { result=new Decoder().decode(bits,Hints); }
                    catch(ReaderException) { }
                    if(result==null || string.IsNullOrEmpty(result.Text)) continue;
                    finding=new QrFinding { Text=result.Text.Trim(), Pose=pose, Version=version, Candidates=candidates.Count, Mirrored=result.Other is QRCodeDecoderMetaData meta && meta.IsMirrored };
                    return true;
                }
            }
            return false;
        }

        // Reads each module at the image point where the pose puts its center.
        static BitMatrix Sample(BitMatrix black, PinholeCamera camera, QrPose.Result pose, int version)
        {
            var n=17+4*version;
            var right=pose.TopRight-pose.TopLeft; var down=pose.BottomLeft-pose.TopLeft;
            var step=1.0/(n-7); // finder centers are n - 7 modules apart
            var bits=new BitMatrix(n);
            for(int row=0;row<n;row++)
            for(int col=0;col<n;col++)
            {
                double a=(col+0.5-3.5)*step, b=(row+0.5-3.5)*step;
                var p=new Point3(pose.TopLeft.X+right.X*a+down.X*b,pose.TopLeft.Y+right.Y*a+down.Y*b,pose.TopLeft.Z+right.Z*a+down.Z*b);
                if(!camera.Project(p,out var u,out var v)) return null;
                int x=(int)Math.Floor(u), y=(int)Math.Floor(camera.Height-v);
                if(x<0 || y<0 || x>=black.Width || y>=black.Height) return null;
                if(black[x,y]) bits[col,row]=true;
            }
            return bits;
        }
    }
}
