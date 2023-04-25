using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using Steamworks;
using TheLongDriveSyncRadio;


namespace TheLongDriveSyncRadio
{
    public class ModMain : MelonMod
    {
        private static List<string> _audioFiles = new List<string>();
        public static List<AudioFilePacket> AudioFilesData = new List<AudioFilePacket>();

        public override void OnInitializeMelon()
        {
            var harmony = new HarmonyLib.Harmony("tld.DeltaNeverUsed.SyncRadio");
            
            harmony.PatchAll();
        }

        
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            checkMedia = true;
        }

        private bool checkMedia;

        public override void OnFixedUpdate()
        {
            if (!SteamManager.Initialized)
                return;
            
            
            
            if (settingsscript.s == null || !checkMedia)
                return;

            checkMedia = false;
            _audioFiles = new List<string>();
            AudioFilesData = new List<AudioFilePacket>();
            
            var customRadioPath = settingsscript.s.S.SCustomRadioPath;
            MelonLogger.Msg("CLOG scanning custom radio folder: " + customRadioPath);
            foreach (string file in Directory.GetFiles(customRadioPath))
                _audioFiles.Add(file);
            
            
            
            foreach (string directory in Directory.GetDirectories(customRadioPath, "*", SearchOption.AllDirectories))
            {
                foreach (string file in Directory.GetFiles(directory))
                    _audioFiles.Add(file);
            }

            foreach (var path in _audioFiles)
            {
                var readData = File.ReadAllBytes(path);

                if (readData.Length > 33554432)
                {
                    MelonLogger.Warning($"{Path.GetFileName(path)} Will not be synced automatically, file size is too big.");
                    continue;
                }

                var a = new AudioFilePacket
                {
                    fileName = Path.GetFileName(path),
                    data = readData
                };

                AudioFilesData.Add(a);
            }
            
            return;
        }
    }

    [Serializable]
    public class RadioPacket
    {
        
    }

    [Serializable]
    public class AudioFilePacket
    {
        public string fileName;
        public byte[] data;
    }
    [Serializable]
    public class AudioFileRequestPacket
    {
        public string[] excludes;
    }

    public static class SendP2P
    {
        public static void Send<T>(CSteamID _id, T obj)
        {
            var data = new [] { (byte)sns.msgType.radio }.Concat(NetworkHelper.ObjectToByteArray(obj)).ToArray();
            SteamNetworking.SendP2PPacket(_id, data, (uint) data.Length, EP2PSend.k_EP2PSendReliable);
        }
    }

    [HarmonyPatch(typeof(sns), "RGeneralSyncMessage")]
    class Patch
    {
        private static bool Prefix(sns __instance, CSteamID _id, byte[] _bytes, sns.msgType _type)
        {
            if (_type != sns.msgType.radio)
                return true;
            

            try
            {
                var obj = NetworkHelper.ByteArrayToObject(_bytes);
                MelonLogger.Msg("Bunger");
                
                var objType = obj.GetType();
                if (objType == typeof(AudioFileRequestPacket))
                {
                    var o = (AudioFileRequestPacket)obj;
                    foreach (var audioFile in ModMain.AudioFilesData)
                    {
                        if (o.excludes.Any(x => x == audioFile.fileName))
                        {
                            MelonLogger.Msg("Skipping: " + audioFile.fileName);
                            continue;
                        }
                        
                        MelonLogger.Msg($"Sending files to {_id.m_SteamID}");
                        SendP2P.Send(_id, o);
                    }
                }

                if (objType == typeof(AudioFilePacket))
                {
                    var o = (AudioFilePacket)obj;
                    
                    MelonLogger.Msg($"Wrote { o.fileName }, with size: { o.data.Length / 1048576 }MiB to disk");
                    File.WriteAllBytes(settingsscript.s.S.SCustomRadioPath + "/" + o.fileName, o.data);
                }

            }
            catch (Exception _)
            {
                return true;
            }

            return false;
        }
    }
    
    [HarmonyPatch(typeof(sns), "SAskStartStuff")]
    class PatchStart
    {
        private static void Prefix(sns __instance)
        {
            var requestFiles = new AudioFileRequestPacket
            {
                excludes = ModMain.AudioFilesData.Select(a => a.fileName).ToArray()
            };

            var data = NetworkHelper.ObjectToByteArray(requestFiles);
            
            MelonLogger.Msg("Requested audio files from host");
            SendP2P.Send(SteamMatchmaking.GetLobbyOwner(__instance.lobby.lobbyID), requestFiles);
        }
    }
}