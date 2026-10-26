using System.Collections.Generic;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// The one docked gangway. It commands the persistent world to walk the
    /// selected actor to the gangway and cross; it never loads a scene or warps
    /// a party to an anchor.
    /// </summary>
    public sealed class PersistentGangway : MonoBehaviour
    {
        [SerializeField] private PersistentWorldRuntime runtime;

        public PersistentWorldRuntime Runtime => runtime;

        public void Configure(PersistentWorldRuntime owner)
        {
            runtime = owner != null ? owner :
                throw new System.ArgumentNullException(nameof(owner));
        }

        public bool TryUse(IReadOnlyList<EntityId> selectedActorIds) =>
            runtime != null && runtime.TryCrossSelected(selectedActorIds);
    }
}
