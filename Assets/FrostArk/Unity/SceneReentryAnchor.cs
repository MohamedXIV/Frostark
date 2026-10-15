using System;
using FrostArk.Core.World;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Authored scene-local physical spawn marker, resolved only after a
    /// destination scene is loaded. IDs are stable save identifiers, not
    /// Unity instance IDs or serialized world coordinates.
    /// </summary>
    public sealed class SceneReentryAnchor : MonoBehaviour
    {
        [SerializeField] private string sceneId;
        [SerializeField] private string anchorId;

        public bool Matches(EntityId scene, EntityId anchor) =>
            scene.IsValid && anchor.IsValid &&
            string.Equals(sceneId, scene.Value, StringComparison.Ordinal) &&
            string.Equals(anchorId, anchor.Value, StringComparison.Ordinal);

        public void Configure(EntityId scene, EntityId anchor)
        {
            if (!scene.IsValid || !anchor.IsValid)
                throw new ArgumentException("Scene and re-entry anchor must have stable IDs.");
            sceneId = scene.Value;
            anchorId = anchor.Value;
        }
    }
}
