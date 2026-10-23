using System;
using System.Linq;
using FrostArk.Core.World;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    public enum GangwayTransitMode
    {
        DisembarkToShore,
        ReturnToShip
    }

    /// <summary>
    /// In-world physical gangway interactable. Enables click-to-command embarkation
    /// and disembarkation between the resident ship and shore environments without
    /// artificial level-switching buttons or external menu frameworks.
    /// </summary>
    [SelectionBase]
    public sealed class ShoreGangwayInteractable : TacticalInteractableProxy
    {
        [Header("Transit Binding")]
        [SerializeField] private ShoreSceneTransitCoordinator transitCoordinator;
        [SerializeField] private SceneTransitDestination destination;
        [SerializeField] private GangwayTransitMode mode = GangwayTransitMode.DisembarkToShore;

        public GangwayTransitMode Mode => mode;
        public SceneTransitDestination Destination => destination;

        public void Configure(ShoreSceneTransitCoordinator coordinator,
            SceneTransitDestination targetDestination, GangwayTransitMode transitMode)
        {
            transitCoordinator = coordinator;
            destination = targetDestination;
            mode = transitMode;
            SetTargetId(EntityId.Parse("gangway:" + transitMode.ToString().ToLowerInvariant()));
        }

        private void Start()
        {
            if (transitCoordinator == null)
                transitCoordinator = FindAnyObjectByType<ShoreSceneTransitCoordinator>();
        }

        public override bool CanInteract(TacticalActorProxy actor)
        {
            if (destination == null || !destination.IsConfigured)
                return false;
            if (transitCoordinator == null)
                transitCoordinator = FindAnyObjectByType<ShoreSceneTransitCoordinator>();
            if (transitCoordinator == null || transitCoordinator.IsBusy)
                return false;
            return base.CanInteract(actor);
        }

        public override bool Interact(TacticalActorProxy actor)
        {
            if (!CanInteract(actor))
                return false;

            if (mode == GangwayTransitMode.DisembarkToShore)
            {
                var opId = EntityId.Parse("transition:gangway:depart:" + Guid.NewGuid().ToString("N"));
                var commander = FindAnyObjectByType<TacticalCommandController>();
                if (commander != null && commander.Selection.Count > 0)
                    return transitCoordinator.TryDepartSelected(destination, opId);
                if (actor != null)
                    return transitCoordinator.TryDepart(destination, new[] { actor.ActorId }, opId);
                return transitCoordinator.TryDepartSelected(destination, opId);
            }
            else
            {
                var opId = EntityId.Parse("transition:gangway:return:" + Guid.NewGuid().ToString("N"));
                return transitCoordinator.TryReturn(destination, opId);
            }
        }

        /// <summary>
        /// Direct command entry point for squad orders or direct click dispatch.
        /// </summary>
        public bool TryInteractDirect(EntityId[] partyIds)
        {
            if (transitCoordinator == null)
                transitCoordinator = FindAnyObjectByType<ShoreSceneTransitCoordinator>();
            if (transitCoordinator == null || transitCoordinator.IsBusy)
                return false;

            if (mode == GangwayTransitMode.DisembarkToShore)
            {
                var opId = EntityId.Parse("transition:gangway:direct-depart:" + Guid.NewGuid().ToString("N"));
                if (partyIds != null && partyIds.Length > 0)
                    return transitCoordinator.TryDepart(destination, partyIds, opId);
                return transitCoordinator.TryDepartSelected(destination, opId);
            }
            else
            {
                var opId = EntityId.Parse("transition:gangway:direct-return:" + Guid.NewGuid().ToString("N"));
                return transitCoordinator.TryReturn(destination, opId);
            }
        }
    }
}
