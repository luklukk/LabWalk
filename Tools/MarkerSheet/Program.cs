// Generates the printable Lab Walk calibration markers as one HTML page per QR code.
// Usage: dotnet run --project Tools/MarkerSheet -- <output.html> [LW1 LW2 LW3 ...]
// Each QR code's text is its marker ID; the Rhino model needs a point named "LabWalk marker <ID>" at its center.
using System;
using System.IO;
using System.Linq;
using System.Text;
using QRCoder;

static class Program
{
    const double CodeCm=15; // printed black-square width; Horizon OS tracking favors large codes

    static void Main(string[] args)
    {
        var output=Path.GetFullPath(args.Length>0 ? args[0] : "LabWalk-markers.html");
        var ids=args.Length>1 ? args.Skip(1).ToArray() : new[]{"LW1","LW2","LW3"};
        var html=new StringBuilder();
        html.Append(@"<!doctype html><html lang=""en""><head><meta charset=""utf-8""><title>Lab Walk markers</title><style>
@page { size: auto; margin: 0.6cm; }
body { font-family: system-ui, sans-serif; margin: 0; color: #000; background: #fff; }
.page { page-break-after: always; break-after: page; text-align: center; padding-top: 0.4cm; }
.page:last-child { page-break-after: auto; break-after: auto; }
h1 { font-size: 28pt; margin: 0 0 0.3cm; }
.code { display: block; margin: 2.9cm auto; width: " + CodeCm + @"cm; height: " + CodeCm + @"cm; }
.note { font-size: 10pt; max-width: 17cm; margin: 0 auto; text-align: left; line-height: 1.35; }
.scale { margin: 0.4cm auto 0; width: 10cm; height: 0.3cm; background: #000; }
.scale-label { font-size: 9pt; }
@media screen { .page { border-bottom: 1px dashed #999; padding-bottom: 1cm; } }
</style></head><body>
");
        using var generator=new QRCodeGenerator();
        foreach(var id in ids)
        {
            using var data=generator.CreateQrCode(id,QRCodeGenerator.ECCLevel.M);
            // ModuleMatrix includes QRCoder's 4-module quiet zone; draw only the code and let the paper be the margin.
            var m=data.ModuleMatrix; int quiet=4, n=m.Count-2*quiet;
            var svg=new StringBuilder($@"<svg class=""code"" viewBox=""0 0 {n} {n}"" shape-rendering=""crispEdges"" xmlns=""http://www.w3.org/2000/svg""><rect width=""{n}"" height=""{n}"" fill=""#fff""/><path fill=""#000"" d=""");
            for(int y=0;y<n;y++) for(int x=0;x<n;x++) if(m[y+quiet][x+quiet]) svg.Append($"M{x} {y}h1v1h-1z");
            svg.Append(@"""/></svg>");
            html.Append($@"<section class=""page""><h1>Lab Walk marker {id}</h1>{svg}
<div class=""note""><b>Print at 100% / actual size.</b> The black square must measure {CodeCm:0} cm; check with the bar below.
Keep the white border around the code. Tape it flat and level on the wall, in even light, away from glare.<br><br>
<b>Record its position:</b> measure from the floor to the <i>bottom edge</i> of the black square and add {CodeCm/2:0.0} cm;
measure along the wall from the room corner to the <i>nearer side edge</i> and add {CodeCm/2:0.0} cm. Those two numbers give the code's center.
The Rhino model needs a point named <b>LabWalk marker {id}</b> exactly there, on the wall surface.</div>
<div class=""scale""></div><div class=""scale-label"">10 cm</div></section>
");
        }
        html.Append("</body></html>\n");
        File.WriteAllText(output,html.ToString());
        Console.WriteLine($"Wrote {ids.Length} markers ({string.Join(", ",ids)}) to {output}");
    }
}
