using System;
using System.IO;
using LabWalk.Core;
using UnityEngine;

namespace LabWalk
{
    [Serializable]
    public sealed class ModelManifest
    {
        public int schemaVersion = 1;
        public string modelId = "sample-room";
        public string displayName = "Sample room";
        public string file = "sample-room.glb";
        public string sourceUnits = "meters";
        public string coordinateUnits = "gltfMeters";
        // Reference positions are ALWAYS final Unity-local meters, after the importer axis conversion.
        public Vector3 referenceA = Vector3.zero;
        public Vector3 referenceB = new Vector3(0,0,2);
        public float knownDistanceMeters = 2;

        public float ValidateAndGetScale()
        {
            if (schemaVersion!=1) throw new InvalidDataException("Unsupported manifest version.");
            if (string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(displayName))
                throw new InvalidDataException("Set modelId and displayName.");
            if (string.IsNullOrWhiteSpace(file) || Path.GetFileName(file)!=file || file.IndexOfAny(new[]{'/', '\\', ':'})>=0 ||
                !(file.EndsWith(".glb",StringComparison.OrdinalIgnoreCase)||file.EndsWith(".3dm",StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("file must be a .glb or .3dm filename in the Models folder.");
            var a=ToPoint(referenceA); var b=ToPoint(referenceB);
            AlignmentMath.Solve(a,b,a,b);
            if (knownDistanceMeters<=0 || float.IsNaN(knownDistanceMeters) || float.IsInfinity(knownDistanceMeters))
                throw new InvalidDataException("knownDistanceMeters must be positive.");
            if(file.EndsWith(".3dm",StringComparison.OrdinalIgnoreCase))
            {
                if(coordinateUnits=="rhinoDocument") return 1; // Reader obtains units from the 3DM header.
                if(coordinateUnits=="sourceUnits") return (float)AlignmentMath.MetersPerUnit(sourceUnits);
                throw new InvalidDataException("For .3dm set coordinateUnits=rhinoDocument (recommended) or sourceUnits (explicit override).");
            }
            return (float)AlignmentMath.ImportScale(coordinateUnits,sourceUnits);
        }
        public static Point3 ToPoint(Vector3 v) { return new Point3(v.x,v.y,v.z); }
        public static Vector3 ToVector(Point3 v) { return new Vector3((float)v.X,(float)v.Y,(float)v.Z); }
    }
}
