using System;
using System.Collections.Generic;
using LabWalk.Core;

public static class CoreTests
{
    static void Near(double expected,double actual,string label)
    { if(Math.Abs(expected-actual)>0.000001) throw new Exception(label+": expected "+expected+", got "+actual); }
    static void Point(Point3 expected,Point3 actual,string label)
    { Near(expected.X,actual.X,label+" X"); Near(expected.Y,actual.Y,label+" Y"); Near(expected.Z,actual.Z,label+" Z"); }
    static void Reject(Action action)
    { try { action(); } catch(ArgumentException) { return; } throw new Exception("Expected invalid input to be rejected."); }
    public static string[] Run()
    {
        var passed=new List<string>();
        var zero=new Point3(0,0,0); var forward=new Point3(0,0,2);
        Near(0.001,AlignmentMath.ImportScale("sourceUnits","millimeters"),"mm");
        Near(0.3048,AlignmentMath.ImportScale("sourceUnits","feet"),"ft");
        Near(0.0254,AlignmentMath.ImportScale("sourceUnits","inches"),"in");
        Near(0.01,AlignmentMath.ImportScale("sourceUnits","centimeters"),"cm");
        passed.Add("Unit conversion: mm, cm, inches, feet");
        Near(1,AlignmentMath.ImportScale("gltfMeters","millimeters"),"No double conversion");
        passed.Add("GLB meters are not scaled again for a millimeter Rhino source");
        Reject(()=>AlignmentMath.ImportScale("guess","meters"));
        Reject(()=>AlignmentMath.ImportScale("gltfMeters","unknown"));
        passed.Add("Unknown units/coordinate policy rejected");
        var identity=AlignmentMath.Solve(zero,forward,zero,forward);
        Point(forward,identity.Transform(forward),"identity");
        passed.Add("Identity alignment");
        var mA=new Point3(4,1,-2); var mB=new Point3(4,1,0);
        var rA=new Point3(-3,0,5); var rB=new Point3(-1,0,5);
        var pose=AlignmentMath.Solve(mA,mB,rA,rB);
        Point(rA,pose.Transform(mA),"offset origin"); Point(rB,pose.Transform(mB),"direction");
        Near(Math.PI/2,pose.YawRadians,"yaw sign");
        passed.Add("Offset model origin, floor height, 90 degree yaw");
        var opposite=AlignmentMath.Solve(zero,forward,zero,new Point3(0,0,-2));
        Point(new Point3(0,0,-2),opposite.Transform(forward),"opposite");
        passed.Add("180 degree alignment");
        var mismatch=AlignmentMath.Solve(zero,forward,zero,new Point3(0,0,3));
        Point(forward,mismatch.Transform(forward),"must preserve scale");
        Near(.5,mismatch.RelativeBaselineError,"50 percent discrepancy");
        passed.Add("Mismatched reference distances report error without rescaling");
        Reject(()=>AlignmentMath.Solve(zero,zero,zero,forward));
        Reject(()=>AlignmentMath.Solve(zero,forward,zero,new Point3(.1,0,0)));
        Reject(()=>AlignmentMath.Solve(zero,forward,zero,new Point3(0,.2,2)));
        Reject(()=>AlignmentMath.Solve(new Point3(double.NaN,0,0),forward,zero,forward));
        passed.Add("Degenerate, non-floor and non-finite reference pairs rejected");
        var random=new Random(8042);
        for(var i=0;i<1000;i++)
        {
            var yaw=random.NextDouble()*2*Math.PI-Math.PI;
            var shift=new Point3(random.NextDouble()*100,random.NextDouble()*3,random.NextDouble()*100);
            var a=new Point3(random.NextDouble()*10,0,random.NextDouble()*10);
            var b=a+new Point3(0.5+random.NextDouble()*10,0,0.5+random.NextDouble()*10);
            var ra=AlignmentMath.Rotate(a,yaw)+shift; var rb=AlignmentMath.Rotate(b,yaw)+shift;
            var solution=AlignmentMath.Solve(a,b,ra,rb);
            Point(ra,solution.Transform(a),"random A"); Point(rb,solution.Transform(b),"random B");
            var p=new Point3(1.37,2.58,-4.1);
            Point(AlignmentMath.Rotate(p,yaw)+shift,solution.Transform(p),"random point");
        }
        passed.Add("1,000 randomized rigid transforms preserve both references and an independent point");
        for(var i=0;i<1000;i++)
        {
            // Wall markers at different heights; exact data must be recovered with zero residual.
            var yaw=random.NextDouble()*2*Math.PI-Math.PI;
            var shift=new Point3(random.NextDouble()*40-20,random.NextDouble()*0.4-0.2,random.NextDouble()*40-20);
            var model=new Point3[3]; var real=new Point3[3];
            for(var k=0;k<3;k++)
            {
                model[k]=new Point3(random.NextDouble()*8,0.5+random.NextDouble()*2,random.NextDouble()*8);
                real[k]=AlignmentMath.Rotate(model[k],yaw)+shift;
            }
            if((model[1]-model[0]).HorizontalLength<0.6 && (model[2]-model[0]).HorizontalLength<0.6) continue;
            var fit=AlignmentMath.SolveFromPoints(model,real);
            var p=new Point3(3.1,0,-1.7);
            Point(AlignmentMath.Rotate(p,yaw)+shift,fit.Transform(p),"marker fit point");
            if(fit.MaxResidualMeters>1e-6 || fit.RelativeBaselineError>1e-9) throw new Exception("Exact marker data must fit exactly.");
        }
        passed.Add("1,000 randomized three-marker fits recover yaw, height and translation exactly");
        var noisyModel=new[]{new Point3(0,1.5,0),new Point3(5,1.5,0),new Point3(0,1.2,6)};
        var noisyReal=new[]{new Point3(0.01,1.5,0),new Point3(5,1.5,-0.01),new Point3(0,1.2,6)};
        var noisy=AlignmentMath.SolveFromPoints(noisyModel,noisyReal);
        if(noisy.MaxResidualMeters<0.001 || noisy.MaxResidualMeters>0.02 || noisy.Residuals.Length!=3) throw new Exception("Noisy fit residuals out of range.");
        var misplaced=AlignmentMath.SolveFromPoints(noisyModel,new[]{noisyModel[0],new Point3(5.4,1.5,0),noisyModel[2]});
        if(misplaced.MaxResidualMeters<0.1) throw new Exception("A 40 cm marker error must show a large residual.");
        passed.Add("Marker measurement noise and a misplaced marker are reported as residuals, never absorbed by scale");
        Reject(()=>AlignmentMath.SolveFromPoints(new[]{zero},new[]{zero}));
        Reject(()=>AlignmentMath.SolveFromPoints(new[]{zero,new Point3(0,1,0.1)},new[]{zero,new Point3(0,1,0.1)}));
        Reject(()=>AlignmentMath.SolveFromPoints(new[]{zero,forward},new[]{zero}));
        passed.Add("Single, clustered and mismatched marker sets rejected");
        return passed.ToArray();
    }
}
