using System;
using System.IO;
using UnityEngine;

namespace LabWalk
{
    [Serializable]
    public sealed class SavedPlacement
    {
        public int schemaVersion=1;
        public string anchorUuid;
        public string modelFingerprint;
        public string savedUtc;
        public Vector3 localPosition;
        public Quaternion localRotation=Quaternion.identity;
    }

    public sealed class PlacementStore
    {
        readonly string path=Path.Combine(Application.persistentDataPath,"placement.json");
        public SavedPlacement Read()
        {
            if(!File.Exists(path)) return null;
            var data=JsonUtility.FromJson<SavedPlacement>(File.ReadAllText(path));
            if(data==null || data.schemaVersion!=1 || !Guid.TryParse(data.anchorUuid,out _) || string.IsNullOrEmpty(data.modelFingerprint))
                throw new InvalidDataException("Saved placement is invalid. Use Realign to replace it.");
            var rotationLength=Quaternion.Dot(data.localRotation,data.localRotation);
            if(!ModelManifest.ToPoint(data.localPosition).IsFinite ||
               float.IsNaN(rotationLength) || float.IsInfinity(rotationLength) ||
               Math.Abs(rotationLength-1)>0.01f)
                throw new InvalidDataException("Saved placement has an invalid pose. Use Realign.");
            return data;
        }
        public void Write(SavedPlacement data)
        {
            var temp=path+".tmp";
            File.WriteAllText(temp,JsonUtility.ToJson(data,true));
            if(File.Exists(path)) File.Replace(temp,path,path+".bak");
            else File.Move(temp,path);
        }
    }
}
