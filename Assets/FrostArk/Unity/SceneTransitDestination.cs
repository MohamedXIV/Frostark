using System;
using FrostArk.Core.Save;
using FrostArk.Core.World;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// An inspector-authored scene reference. Deliberately use a build-listed
    /// asset path and stable domain IDs, never a runtime Unity scene handle.
    /// </summary>
    [Serializable]
    public sealed class SceneTransitDestination
    {
        [SerializeField] private PhysicalTheater theater;
        [SerializeField] private string scenePath;
        [SerializeField] private string sceneId;
        [SerializeField] private string siteId;
        [SerializeField] private string anchorId;

        public PhysicalTheater Theater => theater;
        public string ScenePath => scenePath;
        public EntityId SceneId => EntityId.TryParse(sceneId, out var id) ? id : default;
        public EntityId SiteId => EntityId.TryParse(siteId, out var id) ? id : default;
        public EntityId AnchorId => EntityId.TryParse(anchorId, out var id) ? id : default;

        public bool IsConfigured =>
            (theater == PhysicalTheater.Ship || theater == PhysicalTheater.Shore) &&
            !string.IsNullOrWhiteSpace(scenePath) &&
            SceneId.IsValid && AnchorId.IsValid &&
            (theater != PhysicalTheater.Shore || SiteId.IsValid);

        public bool Matches(PhysicalSceneCheckpoint checkpoint) =>
            checkpoint != null && IsConfigured &&
            checkpoint.Theater == theater &&
            checkpoint.SceneId == SceneId &&
            checkpoint.SpawnAnchorId == AnchorId &&
            (theater == PhysicalTheater.Ship || checkpoint.SiteId == SiteId);

        public PhysicalSceneCheckpoint MakeCheckpoint(System.Collections.Generic.IEnumerable<EntityId> actors)
        {
            if (!IsConfigured)
                throw new InvalidOperationException("Missing destination scene, theater or authored anchor.");
            return theater == PhysicalTheater.Shore
                ? PhysicalSceneCheckpoint.Shore(SceneId, SiteId, AnchorId, actors)
                : PhysicalSceneCheckpoint.Ship(SceneId, AnchorId);
        }

        public void Configure(PhysicalTheater kind, string path, EntityId scene,
            EntityId anchor, EntityId site = default)
        {
            theater = kind;
            scenePath = path;
            sceneId = scene.Value;
            anchorId = anchor.Value;
            siteId = site.IsValid ? site.Value : null;
            if (!IsConfigured)
                throw new ArgumentException("Destination needs a valid build scene, theater and stable IDs.");
        }
    }
}
