using System;
using System.Collections.Generic;
using System.IO;
using Harmony;
using HarmonyLib;
using MelonLoader;
using Steamworks;
using UnityEngine;
using UnityEngine.Serialization;


namespace TheLongDriveSyncRadio
{
    public class ModMain : MelonMod
    {
        private List<string> _audioFiles = new List<string>();
        private List<AudioFilePacket> _audioFilesData = new List<AudioFilePacket>();

        public override void OnInitializeMelon()
        {
            var harmony = new HarmonyLib.Harmony("tld.DeltaNeverUsed.SyncRadio");
            
            harmony.PatchAll();

            var customRadioPath = settingsscript.s.S.SCustomRadioPath;
            Debug.Log("CLOG scanning custom radio folder: " + customRadioPath));
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
                
                if (readData.Length > 8388608 )
                    continue;

                var a = new AudioFilePacket
                {
                    fileName = Path.GetFileName(path),
                    data = readData
                };

                _audioFilesData.Add(a);
            }
        }
        
        public override void OnFixedUpdate()
        {
            if (!SteamManager.Initialized)
                return;
            
            //MelonLogger.Msg("User: " + SteamFriends.GetPersonaName());
        }
    }

    class RadioPacket
    {
        
    }

    [Serializable]
    class AudioFilePacket
    {
        public string fileName;
        public byte[] data;
    }
    
    [HarmonyLib.HarmonyPatch(typeof(sns), nameof(sns.RGeneralSyncMessage))]
    class Patch
    {
        static bool Prefix( CSteamID _id, byte[] _bytes, sns.msgType _type)
        {
            try
            {
                var obj = NetworkHelper.ByteArrayToObject(_bytes);
                if (obj.GetType() == typeof(RadioPacket))
                    return true;
                
                
            }
            catch (Exception e)
            {
                return true;
            }

            return false;
        }
    }
}