using System;

namespace LabWalk.Core
{
    // Independent of Unity so the geometry can be tested without an editor/headset.
    public struct Point3
    {
        public double X, Y, Z;
        public Point3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Point3 operator +(Point3 a, Point3 b) { return new Point3(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
        public static Point3 operator -(Point3 a, Point3 b) { return new Point3(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        public double HorizontalLength { get { return Math.Sqrt(X*X+Z*Z); } }
        public bool IsFinite { get { return Finite(X) && Finite(Y) && Finite(Z); } }
        static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
    }

    public struct AlignmentResult
    {
        public Point3 Translation;
        public double YawRadians;
        public double ModelBaselineMeters, PhysicalBaselineMeters;
        // Marker fits only: distance between each measured point and its transformed model point.
        public double RmsResidualMeters, MaxResidualMeters;
        public double[] Residuals;
        public double RelativeBaselineError
        { get { return Math.Abs(PhysicalBaselineMeters-ModelBaselineMeters)/ModelBaselineMeters; } }
        public Point3 Transform(Point3 p) { return AlignmentMath.Rotate(p,YawRadians)+Translation; }
    }

    public static class AlignmentMath
    {
        public static Point3 Rotate(Point3 p, double yaw)
        {
            var c=Math.Cos(yaw); var s=Math.Sin(yaw);
            return new Point3(c*p.X+s*p.Z,p.Y,-s*p.X+c*p.Z);
        }

        // Both reference pairs are floor points. Gravity fixes pitch/roll; solve yaw and translation only.
        public static AlignmentResult Solve(Point3 modelA, Point3 modelB, Point3 realA, Point3 realB)
        {
            if (!modelA.IsFinite || !modelB.IsFinite || !realA.IsFinite || !realB.IsFinite)
                throw new ArgumentException("Reference points must be finite.");
            var m=modelB-modelA; var r=realB-realA;
            if (m.HorizontalLength < 0.25 || r.HorizontalLength < 0.25)
                throw new ArgumentException("Choose reference points at least 25 cm apart; 2 m is preferable.");
            if (Math.Abs(m.Y)>0.02 || Math.Abs(r.Y)>0.02)
                throw new ArgumentException("Both reference points must lie on the same floor plane.");
            var yaw=Math.Atan2(r.X,r.Z)-Math.Atan2(m.X,m.Z);
            return new AlignmentResult {
                Translation=realA-Rotate(modelA,yaw), YawRadians=yaw,
                ModelBaselineMeters=m.HorizontalLength, PhysicalBaselineMeters=r.HorizontalLength
            };
        }

        // Least-squares rigid fit of two or more corresponding 3D points (e.g. wall markers).
        // Gravity fixes pitch/roll, so only yaw about the vertical axis and a 3D translation are solved;
        // scale is never fitted. The baselines compare the widest horizontal marker spread in the model
        // and in the room, so a mis-measured marker or wrong units shows up as RelativeBaselineError.
        public static AlignmentResult SolveFromPoints(Point3[] model, Point3[] real)
        {
            if (model==null || real==null || model.Length!=real.Length) throw new ArgumentException("Point lists must correspond.");
            if (model.Length<2) throw new ArgumentException("At least two markers are required.");
            for (int i=0;i<model.Length;i++)
                if (!model[i].IsFinite || !real[i].IsFinite) throw new ArgumentException("Marker points must be finite.");
            var mc=Centroid(model); var rc=Centroid(real);
            double dot=0, cross=0;
            for (int i=0;i<model.Length;i++)
            {
                var m=model[i]-mc; var r=real[i]-rc;
                dot+=r.X*m.X+r.Z*m.Z;
                cross+=r.X*m.Z-r.Z*m.X;
            }
            var modelSpread=HorizontalSpread(model); var realSpread=HorizontalSpread(real);
            if (modelSpread<0.5 || realSpread<0.5)
                throw new ArgumentException("Markers must be at least 50 cm apart horizontally; spread them across the room.");
            var yaw=Math.Atan2(cross,dot);
            var result=new AlignmentResult {
                YawRadians=yaw, Translation=rc-Rotate(mc,yaw),
                ModelBaselineMeters=modelSpread, PhysicalBaselineMeters=realSpread,
                Residuals=new double[model.Length]
            };
            double sum=0;
            for (int i=0;i<model.Length;i++)
            {
                var d=result.Transform(model[i])-real[i];
                var e=Math.Sqrt(d.X*d.X+d.Y*d.Y+d.Z*d.Z);
                result.Residuals[i]=e; sum+=e*e;
                if (e>result.MaxResidualMeters) result.MaxResidualMeters=e;
            }
            result.RmsResidualMeters=Math.Sqrt(sum/model.Length);
            return result;
        }

        static Point3 Centroid(Point3[] points)
        {
            var c=new Point3(0,0,0);
            foreach (var p in points) c=c+p;
            return new Point3(c.X/points.Length,c.Y/points.Length,c.Z/points.Length);
        }

        static double HorizontalSpread(Point3[] points)
        {
            double best=0;
            for (int i=0;i<points.Length;i++)
                for (int j=i+1;j<points.Length;j++)
                    best=Math.Max(best,(points[j]-points[i]).HorizontalLength);
            return best;
        }

        public static double MetersPerUnit(string unit)
        {
            switch (unit)
            {
                case "meters": return 1;
                case "millimeters": return 0.001;
                case "centimeters": return 0.01;
                case "inches": return 0.0254;
                case "feet": return 0.3048;
                default: throw new ArgumentException("Unsupported units: " + unit);
            }
        }

        public static double ImportScale(string coordinates, string sourceUnits)
        {
            var sourceScale=MetersPerUnit(sourceUnits); // Validate even when GLB is already in meters.
            if (coordinates=="gltfMeters") return 1;
            if (coordinates=="sourceUnits") return sourceScale;
            throw new ArgumentException("coordinateUnits must be gltfMeters or sourceUnits.");
        }
    }
}
