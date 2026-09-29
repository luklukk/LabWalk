using System;

namespace LabWalk.Core
{
    // Locates a printed QR code in 3D from a single camera image. The inputs are rays from the camera through
    // the centers of the code's three finder patterns (the big corner squares). Those centers form a right
    // isosceles triangle of known size, so the distance along each ray follows from a small P3P solve
    // (Gauss-Newton on the three depths, started from the code's apparent size). The code's center is the
    // midpoint of the two finders that meet the right angle's hypotenuse, which is also unaffected by mirroring.
    public static class QrPose
    {
        // Finder centers sit 3.5 modules in from the code's edges: each leg spans (N - 7) of the N modules.
        public static double FinderSpacing(double codeSize, int version)
        {
            if(version<1 || version>40) throw new ArgumentOutOfRangeException(nameof(version));
            var modules=17+4*version;
            return codeSize*(modules-7)/modules;
        }

        // QR version from the finder spacing measured in modules (10 + 4 * version).
        public static int VersionFromSpan(double modulesBetweenFinders)
        {
            var version=(int)Math.Round((modulesBetweenFinders-10)/4);
            return Math.Max(1,Math.Min(40,version));
        }

        public struct Result
        {
            public Point3 Center, BottomLeft, TopLeft, TopRight;
            public double Distance;      // camera to code center, meters
            public double EdgeError;     // largest mismatch of the solved triangle's sides, meters
        }

        // origin: camera position; bottomLeft/topLeft/topRight: ray directions through the finder centers
        // (any length); leg: distance between adjacent finder centers (FinderSpacing).
        // Three points can fit two (rarely more) poses. moduleSizes (pixels per module seen at each finder, same
        // order) pick among them: a nearer finder looks bigger, so apparent size times depth should be equal.
        public static bool TrySolve(Point3 origin, Point3 bottomLeft, Point3 topLeft, Point3 topRight, double leg, double[] moduleSizes, out Result result)
        {
            result=default;
            var candidates=SolveAll(origin,bottomLeft,topLeft,topRight,leg);
            if(candidates.Count==0) return false;
            var best=double.MaxValue;
            foreach(var c in candidates)
            {
                var score=SizeMismatch(origin,c,moduleSizes);
                if(score<best) { best=score; result=c; }
            }
            return true;
        }

        // Spread of log(apparent size x depth) over the three finders: 0 when the depths explain the sizes exactly.
        public static double SizeMismatch(Point3 origin, Result r, double[] moduleSizes)
        {
            if(moduleSizes==null || moduleSizes.Length<3 || moduleSizes[0]<=0 || moduleSizes[1]<=0 || moduleSizes[2]<=0) return 0;
            var v=new[]{ Math.Log(moduleSizes[0]*Length(r.BottomLeft-origin)), Math.Log(moduleSizes[1]*Length(r.TopLeft-origin)), Math.Log(moduleSizes[2]*Length(r.TopRight-origin)) };
            var mean=(v[0]+v[1]+v[2])/3;
            return (v[0]-mean)*(v[0]-mean)+(v[1]-mean)*(v[1]-mean)+(v[2]-mean)*(v[2]-mean);
        }

        // Every distinct exact pose, found by Gauss-Newton from several starting depth patterns.
        public static System.Collections.Generic.List<Result> SolveAll(Point3 origin, Point3 bottomLeft, Point3 topLeft, Point3 topRight, double leg)
        {
            var found=new System.Collections.Generic.List<Result>();
            var u=new[]{ Unit(bottomLeft), Unit(topLeft), Unit(topRight) };
            if(!u[0].IsFinite || !u[1].IsFinite || !u[2].IsFinite || leg<=0) return found;
            var hypotenuseAngle=Math.Acos(Math.Max(-1,Math.Min(1,Dot(u[0],u[2]))));
            if(hypotenuseAngle<1e-6) return found;
            var start=leg*Math.Sqrt(2)/(2*Math.Sin(hypotenuseAngle/2));
            var tilt=leg/start; // relative depth change across one leg if the code were turned 90 degrees
            foreach(var k in new[]{ 0.4, 0.9 })
            foreach(var (a,b,c) in new[]{ (0.0,0.0,0.0), (1,0,-1), (-1,0,1), (1,0,1), (-1,0,-1), (0,1,0), (0,-1,0), (1,1,-1), (-1,-1,1), (1,-1,-1), (-1,1,1) })
            {
                if(!Refine(origin,u,leg,new[]{ start*(1+k*tilt*a), start*(1+k*tilt*b), start*(1+k*tilt*c) },out var r)) continue;
                if(r.EdgeError>leg*1e-4) continue;
                var duplicate=false;
                foreach(var f in found) if(Length(f.Center-r.Center)<leg*1e-3) { duplicate=true; break; }
                if(!duplicate) found.Add(r);
            }
            return found;
        }

        static bool Refine(Point3 origin, Point3[] u, double leg, double[] d, out Result result)
        {
            result=default;
            // Pairs (i, j, squared length): the two legs meet at the top-left finder; the hypotenuse joins the others.
            var pairs=new[]{ (1,0,leg*leg), (1,2,leg*leg), (0,2,2*leg*leg) };
            for(int iteration=0;iteration<60;iteration++)
            {
                var f=new double[3]; var j=new double[3,3];
                for(int k=0;k<3;k++)
                {
                    var (a,b,lengthSquared)=pairs[k];
                    var diff=Scale(u[a],d[a])-Scale(u[b],d[b]);
                    f[k]=Dot(diff,diff)-lengthSquared;
                    j[k,a]+=2*Dot(diff,u[a]);
                    j[k,b]-=2*Dot(diff,u[b]);
                }
                if(!Solve3(j,new[]{-f[0],-f[1],-f[2]},out var step)) return false;
                var size=0.0;
                for(int k=0;k<3;k++) { d[k]+=step[k]; size=Math.Max(size,Math.Abs(step[k])); }
                if(size<1e-10) break;
            }
            if(d[0]<=0.01 || d[1]<=0.01 || d[2]<=0.01) return false;
            var p=new[]{ origin+Scale(u[0],d[0]), origin+Scale(u[1],d[1]), origin+Scale(u[2],d[2]) };
            var edgeError=0.0;
            foreach(var (a,b,lengthSquared) in pairs)
                edgeError=Math.Max(edgeError,Math.Abs(Length(p[a]-p[b])-Math.Sqrt(lengthSquared)));
            var center=Scale(p[0]+p[2],0.5);
            result=new Result { Center=center, BottomLeft=p[0], TopLeft=p[1], TopRight=p[2], Distance=Length(center-origin), EdgeError=edgeError };
            return result.Center.IsFinite;
        }

        static double Dot(Point3 a, Point3 b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
        static double Length(Point3 a) { return Math.Sqrt(Dot(a,a)); }
        static Point3 Scale(Point3 a, double s) { return new Point3(a.X*s,a.Y*s,a.Z*s); }
        static Point3 Unit(Point3 a) { var l=Length(a); return l>0 ? Scale(a,1/l) : new Point3(double.NaN,double.NaN,double.NaN); }

        // Cramer's rule for the 3x3 Gauss-Newton step.
        static bool Solve3(double[,] m, double[] v, out double[] x)
        {
            x=new double[3];
            var det=Det(m);
            if(Math.Abs(det)<1e-18) return false;
            for(int c=0;c<3;c++)
            {
                var t=(double[,])m.Clone();
                for(int r=0;r<3;r++) t[r,c]=v[r];
                x[c]=Det(t)/det;
            }
            return true;
        }
        static double Det(double[,] m)
        {
            return m[0,0]*(m[1,1]*m[2,2]-m[1,2]*m[2,1])-m[0,1]*(m[1,0]*m[2,2]-m[1,2]*m[2,0])+m[0,2]*(m[1,0]*m[2,1]-m[1,1]*m[2,0]);
        }
    }
}
