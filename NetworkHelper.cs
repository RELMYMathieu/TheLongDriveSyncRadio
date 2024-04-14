using MelonLoader;
using Newtonsoft.Json;
using System;
using System.Text;

public static class NetworkHelper
{
    // Convert an object to a byte array using JSON serialization
    public static byte[] ObjectToByteArray(Object obj)
    {
        try
        {
            string json = JsonConvert.SerializeObject(obj);
            return Encoding.UTF8.GetBytes(json);
        }
        catch (Exception e)
        {
            MelonLogger.Error("Error serializing object: " + e);
            return null;
        }
    }

    // Convert a byte array to an Object using JSON deserialization
    public static Object ByteArrayToObject(byte[] arrBytes)
    {
        try
        {
            string json = Encoding.UTF8.GetString(arrBytes);
            return JsonConvert.DeserializeObject(json);
        }
        catch (Exception e)
        {
            MelonLogger.Error("Error deserializing object: " + e);
            return null;
        }
    }
}