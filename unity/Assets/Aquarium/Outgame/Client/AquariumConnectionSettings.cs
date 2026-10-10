using UnityEngine;

namespace Aquarium.Outgame.Client
{
    /// <summary>Shared non-secret configuration for the local development server/player pair.</summary>
    [CreateAssetMenu(menuName = "Aquarium/Connection Settings")]
    public sealed class AquariumConnectionSettings : ScriptableObject
    {
        [SerializeField] private string endpoint = "http://127.0.0.1:8081";
        [SerializeField] private string developmentPlayer = "unity-dev";

        public string Endpoint => endpoint;
        public string DevelopmentPlayer => developmentPlayer;
    }
}
