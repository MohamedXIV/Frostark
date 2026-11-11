using System;
using FrostArk.Core.World;
using UnityEngine;
using UnityEngine.AI;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// One physical crew object for the whole run. Ship-grid and shore-NavMesh
    /// locomotion are mutually exclusive motors on this same GameObject; no
    /// scene proxy copy is created when the actor changes theater.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PersistentCrewActor : MonoBehaviour
    {
        [SerializeField] private string actorId;

        private ShipGridWalker _shipMotor;
        private ShipCrewSelectionProxy _shipSelection;
        private NavMeshAgent _shoreAgent;
        private TacticalActorProxy _shoreActor;

        public EntityId ActorId
        {
            get
            {
                if (_shipSelection != null)
                    return _shipSelection.ActorId;
                return string.IsNullOrEmpty(actorId) ? default : EntityId.Parse(actorId);
            }
        }

        public ShipGridWalker ShipMotor => _shipMotor;
        public ShipCrewSelectionProxy ShipSelection => _shipSelection;
        public NavMeshAgent ShoreAgent => _shoreAgent;
        public TacticalActorProxy ShoreActor => _shoreActor;
        public bool IsOnShip { get; private set; }

        public void Configure(EntityId id, ShipGridWalker shipMotor,
            ShipCrewSelectionProxy shipSelection, NavMeshAgent shoreAgent,
            TacticalActorProxy shoreActor, GameObject selectionMarker)
        {
            if (!id.IsValid)
                throw new ArgumentException("Valid persistent crew ID required.", nameof(id));
            if (shipMotor == null || shipSelection == null || shoreAgent == null || shoreActor == null)
                throw new ArgumentNullException(nameof(shipMotor));

            actorId = id.Value;
            _shipMotor = shipMotor;
            _shipSelection = shipSelection;
            _shoreAgent = shoreAgent;
            _shoreActor = shoreActor;

            if (selectionMarker != null)
            {
                _shipSelection.SetSelectionMarker(selectionMarker);
                _shoreActor.SetSelectionMarker(selectionMarker);
            }

            SetShipMode();
        }

        public void SetShipMode()
        {
            if (_shoreAgent != null)
                _shoreAgent.enabled = false;
            if (_shipMotor != null)
                _shipMotor.enabled = true;
            IsOnShip = true;
        }

        /// <summary>
        /// Switch the same object to the shore NavMesh. The caller supplies the
        /// validated gangway exit point; a failed placement restores ship mode.
        /// </summary>
        public bool SetShoreMode(Vector3 worldPoint)
        {
            if (_shoreAgent == null || _shipMotor == null)
                return false;

            _shipMotor.enabled = false;
            _shoreAgent.enabled = true;
            if (!_shoreAgent.Warp(worldPoint) || !_shoreAgent.isOnNavMesh)
            {
                _shoreAgent.enabled = false;
                _shipMotor.enabled = true;
                IsOnShip = true;
                return false;
            }

            IsOnShip = false;
            return true;
        }

        /// <summary>
        /// Place the same object at an authored ship cell after a shore return.
        /// The position is the physical end of the gangway walk, not a scene load.
        /// </summary>
        public void SetShipPosition(Vector3 worldPoint)
        {
            transform.position = worldPoint;
            SetShipMode();
        }
    }
}
