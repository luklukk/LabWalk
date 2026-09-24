using System;
using System.IO;
using UnityEngine;

namespace LabWalk
{
    // Small on-device log for headset tests: files/labwalk-log.txt (readable with MQDH's file manager or adb pull).
    // Keeps the previous session as labwalk-log.previous.txt. Never contains model contents or camera data.
    public static class DiagnosticsLog
    {
        const long MaxBytes=512*1024;
        static string path;
        static readonly object gate=new object();

        public static string FilePath => path;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Start()
        {
            try
            {
                path=Path.Combine(Application.persistentDataPath,"labwalk-log.txt");
                if(File.Exists(path)) File.Copy(path,Path.Combine(Application.persistentDataPath,"labwalk-log.previous.txt"),true);
                File.WriteAllText(path,$"Lab Walk {Application.version} ({Application.platform}) session start {DateTime.Now:O}\n");
            }
            catch(Exception e) { path=null; Debug.LogWarning("Diagnostics log unavailable: "+e.Message); }
        }

        public static void Write(string line)
        {
            Debug.Log("[LabWalk] "+line);
            if(path==null) return;
            try
            {
                lock(gate)
                {
                    if(new FileInfo(path).Length>MaxBytes) return;
                    File.AppendAllText(path,$"{DateTime.Now:HH:mm:ss.fff} f{Time.frameCount} {line}\n");
                }
            }
            catch { }
        }

        public static string Pose(Transform t) => t ? $"pos {t.position:F3} yaw {t.eulerAngles.y:F2}" : "none";
    }
}
