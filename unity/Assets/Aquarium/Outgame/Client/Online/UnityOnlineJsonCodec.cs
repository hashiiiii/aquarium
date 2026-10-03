using Aquarium.Online;
using UnityEngine;

namespace Aquarium.Runtime
{
    /// <summary>Connect protobuf JSON DTO adapter; 64-bit wire integers stay strings.</summary>
    public sealed class UnityOnlineJsonCodec : IOnlineJsonCodec
    {
        public string Serialize<T>(T value) => JsonUtility.ToJson(value);
        public T Deserialize<T>(string json) => JsonUtility.FromJson<T>(json);
    }
}
