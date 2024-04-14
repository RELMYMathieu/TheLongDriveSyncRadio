using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HarmonyLib;
using MelonLoader;
using Steamworks;
using TheLongDriveSyncRadio;

namespace TheLongDriveRadioSync
{
    public class ModMain : MelonMod
    {
        public static sns Sns;

        private static List<string> _audioFiles = new List<string>();
        public static List<AudioFilePacket> AudioFilesData = new List<AudioFilePacket>();

        private const int TargetSceneIndex = 1;

        public override void OnInitializeMelon()
        {
            var harmony = new HarmonyLib.Harmony("tld.HOUAHOUA.SyncRadio");
            harmony.PatchAll();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            MelonLogger.Msg($"Scene {buildIndex} loaded: {sceneName}");
            if (buildIndex == TargetSceneIndex)
            {
                checkMedia = true;
            }
        }

        private bool checkMedia;

        public override void OnFixedUpdate()
        {
            if (!SteamManager.Initialized || settingsscript.s == null || !checkMedia)
                return;

            checkMedia = false;
            _audioFiles = new List<string>();
            AudioFilesData = new List<AudioFilePacket>();

            var customRadioPath = settingsscript.s.S.SCustomRadioPath;
            MelonLogger.Msg("CLOG scanning custom radio folder: " + customRadioPath);

            // Print files in the custom radio folder
            foreach (string file in Directory.GetFiles(customRadioPath))
            {
                if (!_audioFiles.Contains(file))
                {
                    _audioFiles.Add(file);
                    string format = Path.GetExtension(file).TrimStart('.').ToUpper();
                    MelonLogger.Msg($"Found file: {Path.GetFileName(file)} (Format: {format})");
                }
            }

            // Print files in subdirectories of the custom radio folder
            foreach (string directory in Directory.GetDirectories(customRadioPath, "*", SearchOption.AllDirectories))
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    if (!_audioFiles.Contains(file))
                    {
                        _audioFiles.Add(file);
                        string format = Path.GetExtension(file).TrimStart('.').ToUpper();
                        MelonLogger.Msg($"Found file: {Path.GetFileName(file)} (Format: {format})");
                    }
                }
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
        }
    }

        [Serializable]
    public struct RadioPacket
    {
        public string fileName;
    }

    [Serializable]
    public struct AudioFilePacket
    {
        public string fileName;
        public byte[] data;
    }

    [Serializable]
    public struct PacketPart
    {
        public uint size;
        public uint pId;
        public uint pIndex;
        public byte[] data;
    }

    [Serializable]
    public struct AudioFileRequestPacket
    {
        public string[] excludes;
    }

    public static class SendP2P
    {
        public static void Send<T>(CSteamID _id, T obj)
        {
            var data = new[] { (byte)sns.msgType.radio }.Concat(NetworkHelper.ObjectToByteArray(obj)).ToArray();
            if (!SteamNetworking.SendP2PPacket(_id, data, (uint)data.Length, EP2PSend.k_EP2PSendReliable))
                MelonLogger.Error("Failed to send packet");
        }
    }

    [HarmonyPatch(typeof(sns), "RGeneralSyncMessage")]
    class Patch
    {
        private static Random _rand = new Random();
        public static List<CSteamID> AlreadySend = new List<CSteamID>();

        private static Dictionary<uint, List<PacketPart>> _recv = new Dictionary<uint, List<PacketPart>>();

        private static void SendFile(CSteamID _id, AudioFileRequestPacket o)
        {
            foreach (var audioFile in ModMain.AudioFilesData)
            {
                if (o.excludes.Contains(audioFile.fileName))
                {
                    MelonLogger.Msg("Skipping: " + audioFile.fileName);
                    continue;
                }

                MelonLogger.Msg($"Sending \"{audioFile.fileName}\" to {_id.m_SteamID}");
                var fullAudioPacket = NetworkHelper.ObjectToByteArray(audioFile);
                var chunkSize = 512 * 1024; // 512KB
                var numChunks = (int)Math.Ceiling((double)fullAudioPacket.Length / chunkSize);

                var packetId = (uint)_rand.Next();

                for (uint i = 0; i < numChunks; i++)
                {
                    int offset = (int)i * chunkSize;
                    int size = Math.Min(chunkSize, fullAudioPacket.Length - offset);
                    var data = new byte[size];
                    Array.Copy(fullAudioPacket, offset, data, 0, size);

                    var packet = new PacketPart
                    {
                        size = (uint)numChunks,
                        pId = packetId,
                        pIndex = i,
                        data = data
                    };
                    Thread.Sleep(500);
                    MelonLogger.Msg($"Sending packet: {i}, out of {numChunks - 1}");
                    SendP2P.Send(_id, packet);
                }

                Thread.Sleep(2000);
            }
        }

        private static bool Prefix(sns __instance, CSteamID _id, byte[] _bytes, sns.msgType _type)
        {
            if (_type != sns.msgType.radio)
                return true;

            try
            {
                if (_bytes == null || _bytes.Length <= 1)
                {
                    MelonLogger.Error("Invalid packet data received.");
                    return false;
                }

                var obj = NetworkHelper.ByteArrayToObject(_bytes.Skip(1).ToArray());

                var objType = obj.GetType();

                if (objType == typeof(AudioFileRequestPacket))
                {
                    if (AlreadySend.Contains(_id))
                        return false;
                    AlreadySend.Add(_id);

                    var o = (AudioFileRequestPacket)obj;
                    var t = new Thread(() => SendFile(_id, o));
                    t.Start();
                }

                if (objType == typeof(RadioPacket))
                {
                    var o = (RadioPacket)obj;
                    if (ModMain.AudioFilesData.All(a => a.fileName != o.fileName))
                        return false;

                    MelonLogger.Msg("Playing: " + o.fileName);
                    mainscript.M.customRadio.LoadOneSong(settingsscript.s.S.SCustomRadioPath + "/" + o.fileName);
                }

                if (objType == typeof(PacketPart))
                {
                    var o = (PacketPart)obj;
                    if (_recv.ContainsKey(o.pId))
                    {
                        if (_recv[o.pId].Any(x => x.pIndex == o.pIndex))
                            return false;

                        _recv[o.pId].Add(o);
                    }
                    else
                    {
                        if (o.pIndex > 10)
                            return false;
                        _recv.Add(o.pId, new List<PacketPart> { o });
                    }

                    MelonLogger.Msg($"Got Packet: {o.pId} for index: {o.pIndex} out of {o.size - 1}");

                    if (_recv[o.pId].Count < o.size)
                        return false;

                    MelonLogger.Msg("Trying to recombine data");

                    var fullPacket = _recv[o.pId].OrderBy(p => p.pIndex).ToArray();
                    var data = new byte[0];

                    for (int i = 0; i < fullPacket.Length; i++)
                    {
                        data = data.Concat(fullPacket[i].data).ToArray();
                    }

                    _recv.Remove(o.pId);

                    try
                    {
                        obj = NetworkHelper.ByteArrayToObject(data);
                        objType = obj.GetType();
                        MelonLogger.Msg("Done recombining data");
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Error("Error recombining data: " + e);
                    }
                }

                if (objType == typeof(AudioFilePacket))
                {
                    var o = (AudioFilePacket)obj;

                    MelonLogger.Msg($"Wrote {o.fileName}, with size: {o.data.Length / 1048576}MiB to disk");
                    File.WriteAllBytes(settingsscript.s.S.SCustomRadioPath + "/" + o.fileName, o.data);
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error("Error processing packet: " + e);
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
            ModMain.Sns = __instance;
            Patch.AlreadySend = new List<CSteamID>();
            if (__instance.lobby.isServer)
                return;

            var requestFiles = new AudioFileRequestPacket
            {
                excludes = ModMain.AudioFilesData.Select(a => a.fileName).ToArray()
            };

            MelonLogger.Msg("Requested audio files from host");
            SendP2P.Send(SteamMatchmaking.GetLobbyOwner(__instance.lobby.lobbyID), requestFiles);
        }
    }

    [HarmonyPatch(typeof(custommusicscript), "LoadOneSong", new Type[] { typeof(string), typeof(int) })]
    class PatchRadioCustomSend
    {
        private static string _lastSentSong = string.Empty;

        private static void Prefix(custommusicscript __instance, string path)
        {
            if (ModMain.Sns.lobby.isServer)
            {
                string songFileName = Path.GetFileName(path);
                if (_lastSentSong != songFileName)
                {
                    _lastSentSong = songFileName;
                    MelonLogger.Msg($"Sending radio custom packet: {songFileName}");
                    for (int iMember = 0; iMember < SteamMatchmaking.GetNumLobbyMembers(ModMain.Sns.lobby.lobbyID); ++iMember)
                    {
                        CSteamID lobbyMemberByIndex = SteamMatchmaking.GetLobbyMemberByIndex(ModMain.Sns.lobby.lobbyID, iMember);
                        if (lobbyMemberByIndex != SteamUser.GetSteamID())
                            SendP2P.Send(lobbyMemberByIndex, new RadioPacket { fileName = songFileName });
                    }
                }
            }
        }
    }
}