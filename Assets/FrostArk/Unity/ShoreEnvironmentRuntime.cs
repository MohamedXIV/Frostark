using System;
using Unity.AI.Navigation;
using UnityEngine;

namespace FrostArk.Unity
{
    /// <summary>
    /// Environment-only shore scene builder. It owns no session, camera, light
    /// or actor; the persistent run root remains the sole owner of those.
    /// </summary>
    public sealed class ShoreEnvironmentRuntime : MonoBehaviour
    {
        public bool IsReady { get; private set; }
        public string Error { get; private set; }

        private void Start()
        {
            try
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "ShoreIceGround";
                ground.transform.SetParent(transform, false);
                ground.transform.localPosition = new Vector3(15f, -0.1f, 0f);
                ground.transform.localScale = new Vector3(30f, 0.2f, 24f);
                ground.AddComponent<ShoreWalkableSurface>();

                var marker = new GameObject("ShoreDockMarker");
                marker.transform.SetParent(transform, false);
                marker.transform.localPosition = new Vector3(1f, 0f, 3f);

                var surface = gameObject.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.Children;
                surface.BuildNavMesh();
                if (surface.navMeshData == null)
                    throw new InvalidOperationException("Shore NavMesh bake produced no data.");

                IsReady = true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Debug.LogException(ex, this);
            }
        }
    }
}
