using System;
using FrostArk.Core.Application;
using FrostArk.Core.Combat;
using FrostArk.Core.Commands;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;
using UnityEngine;
using UnityEngine.AI;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Scene proxy for a commandable on-foot character (protagonist or crew).
    /// Manages Unity NavMesh locomotion, selection visuals, interaction approach,
    /// and combat target engagement (range, line-of-fire, and authoritative firing).
    /// Does not store authoritative character health, inventory, or skills.
    /// </summary>
    [SelectionBase]
    [RequireComponent(typeof(NavMeshAgent))]
    public class TacticalActorProxy : MonoBehaviour
    {
        [SerializeField]
        private string actorId = "actor:protagonist";

        [SerializeField]
        private GameObject selectionMarker;

        [Header("Combat Configuration")]
        [SerializeField]
        private string weaponItemId = "item:weapon:rifle";

        [SerializeField]
        private float weaponRange = 15.0f;

        [SerializeField]
        private int damagePerShot = 250;

        [SerializeField]
        private Transform muzzlePoint;

        [SerializeField]
        private LayerMask lineOfFireMask = ~0;

        [Header("Environmental & Tactical Configuration")]
        [SerializeField]
        [Range(0, 1000)]
        private int coldExposurePermille = 0;

        private NavMeshAgent _agent;
        private TacticalInteractableProxy _pendingInteractable;
        private TacticalCombatTargetProxy _pendingTarget;
        private WorldSession _session;
        private EntityId _actorIdEntity;
        private EntityId _weaponIdEntity;
        private float _baseSpeed = 3.5f;

        public EntityId ActorId
        {
            get
            {
                if (!_actorIdEntity.IsValid)
                {
                    if (string.IsNullOrEmpty(actorId))
                        actorId = "actor:" + gameObject.name;
                    _actorIdEntity = EntityId.Parse(actorId);
                }
                return _actorIdEntity;
            }
            private set => _actorIdEntity = value;
        }

        public EntityId WeaponId
        {
            get
            {
                if (!_weaponIdEntity.IsValid)
                {
                    if (string.IsNullOrEmpty(weaponItemId))
                        weaponItemId = "item:weapon:rifle";
                    _weaponIdEntity = EntityId.Parse(weaponItemId);
                }
                return _weaponIdEntity;
            }
            private set => _weaponIdEntity = value;
        }

        public bool IsSelected { get; private set; }
        public bool IsRetreating { get; private set; }
        public int ColdExposurePermille => coldExposurePermille;
        public NavMeshAgent Agent => _agent != null ? _agent : (_agent = GetComponent<NavMeshAgent>());
        public TacticalInteractableProxy PendingInteraction => _pendingInteractable;
        public TacticalCombatTargetProxy PendingTarget => _pendingTarget;
        public float WeaponRange => weaponRange;
        public int DamagePerShot => damagePerShot;
        public CombatTargetingStatus LastTargetingStatus { get; private set; }

        public Vector3 MuzzlePosition =>
            muzzlePoint != null ? muzzlePoint.position : transform.position + Vector3.up * 1.2f;

        public bool IsMoving
        {
            get
            {
                if (_agent == null || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh)
                    return false;
                return _agent.hasPath && _agent.remainingDistance > _agent.stoppingDistance + 0.1f;
            }
        }

        public event Action<TacticalActorProxy, TacticalCombatTargetProxy> OnShotFired;
        public event Action<TacticalActorProxy, TacticalCombatTargetProxy, int> OnTargetHit;
        public event Action<TacticalActorProxy, string> OnAttackFailed;
        public event Action<TacticalActorProxy, long> OnWeaponReloaded;
        public event Action<TacticalActorProxy, string> OnReloadFailed;
        public event Action<TacticalActorProxy> OnRetreatStarted;
        public event Action<TacticalActorProxy> OnRetreatArrived;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (_agent != null)
                _baseSpeed = _agent.speed;
            var _ = ActorId;
            var __ = WeaponId;
            if (selectionMarker == null)
            {
                var ring = transform.Find("SelectionRing");
                if (ring != null)
                    selectionMarker = ring.gameObject;
            }
            SetSelected(false);
        }

        public void SetActorId(EntityId id)
        {
            _actorIdEntity = id;
            actorId = id.ToString();
        }

        public void SetWeapon(EntityId weaponId, float range = 15.0f, int damage = 250)
        {
            _weaponIdEntity = weaponId;
            weaponItemId = weaponId.ToString();
            weaponRange = range;
            damagePerShot = damage;
        }

        public void SetMuzzlePoint(Transform muzzle)
        {
            muzzlePoint = muzzle;
        }

        public void SetLineOfFireMask(LayerMask mask)
        {
            lineOfFireMask = mask;
        }

        public void SetSession(WorldSession session)
        {
            _session = session;
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (selectionMarker == null)
            {
                var ring = transform.Find("SelectionRing");
                if (ring != null)
                    selectionMarker = ring.gameObject;
            }
            if (selectionMarker != null)
                selectionMarker.SetActive(selected);
        }

        public void SetSelectionMarker(GameObject marker)
        {
            selectionMarker = marker;
            if (selectionMarker != null)
                selectionMarker.SetActive(IsSelected);
        }

        public void SetColdExposurePermille(int value)
        {
            coldExposurePermille = Mathf.Clamp(value, 0, 1000);
            UpdateMovementSpeed();
        }

        private void UpdateMovementSpeed()
        {
            if (Agent != null)
            {
                float coldFactor = ColdCombatModifiers.CalculateSpeedMultiplier(coldExposurePermille);
                float sprint = IsRetreating ? 1.25f : 1.0f;
                Agent.speed = _baseSpeed * coldFactor * sprint;
            }
        }

        public virtual bool MoveTo(Vector3 destination)
        {
            CancelPendingInteraction();
            CancelPendingCombat();
            if (IsRetreating)
            {
                IsRetreating = false;
                UpdateMovementSpeed();
            }

            if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh)
            {
                Agent.isStopped = false;
                Agent.stoppingDistance = 0.1f;
                return Agent.SetDestination(destination);
            }

            return false;
        }

        public virtual bool RetreatTo(Vector3 destination)
        {
            CancelPendingInteraction();
            CancelPendingCombat();
            IsRetreating = true;
            UpdateMovementSpeed();

            if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh)
            {
                Agent.isStopped = false;
                Agent.stoppingDistance = 0.5f;
                bool dispatched = Agent.SetDestination(destination);
                if (dispatched)
                {
                    OnRetreatStarted?.Invoke(this);
                }
                return dispatched;
            }

            return false;
        }

        public virtual bool ApproachAndInteract(TacticalInteractableProxy interactable)
        {
            if (interactable == null || interactable.IsDepleted)
                return false;

            CancelPendingCombat();
            if (IsRetreating)
            {
                IsRetreating = false;
                UpdateMovementSpeed();
            }
            _pendingInteractable = interactable;

            if (interactable.CanInteract(this))
            {
                ExecutePendingInteraction();
                return true;
            }

            if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh)
            {
                Agent.isStopped = false;
                Agent.stoppingDistance = Mathf.Max(0.5f, interactable.InteractionRange * 0.8f);
                return Agent.SetDestination(interactable.transform.position);
            }

            return false;
        }

        public void CancelPendingInteraction()
        {
            _pendingInteractable = null;
            if (Agent != null && Agent.isActiveAndEnabled)
            {
                Agent.stoppingDistance = 0.1f;
            }
        }

        public virtual bool AttackTarget(TacticalCombatTargetProxy target, WorldSession session = null)
        {
            if (target == null || target.IsDead || !target.IsHostile)
                return false;

            CancelPendingInteraction();
            if (IsRetreating)
            {
                IsRetreating = false;
                UpdateMovementSpeed();
            }
            _pendingTarget = target;
            if (session != null)
                _session = session;

            var status = TacticalCombatRaycast.EvaluateLineOfFire(
                MuzzlePosition, target, weaponRange, lineOfFireMask, GetComponent<Collider>(), out _);
            LastTargetingStatus = status;

            if (status == CombatTargetingStatus.Clear)
            {
                FaceTarget(target);
                TryExecuteShot();
                return true;
            }

            if (status == CombatTargetingStatus.OutOfRange || status == CombatTargetingStatus.Obstructed || status == CombatTargetingStatus.Missed)
            {
                if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh)
                {
                    Agent.isStopped = false;
                    Agent.stoppingDistance = (status == CombatTargetingStatus.Obstructed) ? 1.0f : Mathf.Max(1.0f, weaponRange * 0.75f);
                    return Agent.SetDestination(target.transform.position);
                }
            }

            if (status == CombatTargetingStatus.InvalidTarget)
            {
                CancelPendingCombat();
                return false;
            }

            return true;
        }

        public void CancelPendingCombat()
        {
            _pendingTarget = null;
            if (Agent != null && Agent.isActiveAndEnabled)
            {
                Agent.stoppingDistance = 0.1f;
            }
        }

        private void Update()
        {
            UpdateRetreat();
            UpdateInteraction();
            UpdateCombat();
        }

        private void UpdateRetreat()
        {
            if (!IsRetreating)
                return;

            if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh)
            {
                if (!Agent.pathPending && Agent.remainingDistance <= Agent.stoppingDistance)
                {
                    IsRetreating = false;
                    UpdateMovementSpeed();
                    OnRetreatArrived?.Invoke(this);
                }
            }
        }

        private void UpdateInteraction()
        {
            if (_pendingInteractable == null)
                return;

            if (_pendingInteractable.IsDepleted)
            {
                CancelPendingInteraction();
                return;
            }

            if (_pendingInteractable.CanInteract(this))
            {
                ExecutePendingInteraction();
            }
        }

        private void UpdateCombat()
        {
            if (_pendingTarget == null)
                return;

            if (_pendingTarget.IsDead)
            {
                CancelPendingCombat();
                return;
            }

            var status = TacticalCombatRaycast.EvaluateLineOfFire(
                MuzzlePosition, _pendingTarget, weaponRange, lineOfFireMask, GetComponent<Collider>(), out _);
            LastTargetingStatus = status;

            if (status == CombatTargetingStatus.Clear)
            {
                if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh)
                {
                    Agent.isStopped = true;
                    Agent.ResetPath();
                }
                FaceTarget(_pendingTarget);
                TryExecuteShot();
            }
            else if (status == CombatTargetingStatus.OutOfRange || status == CombatTargetingStatus.Obstructed || status == CombatTargetingStatus.Missed)
            {
                if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh)
                {
                    float stopDist = (status == CombatTargetingStatus.Obstructed) ? 1.0f : Mathf.Max(1.0f, weaponRange * 0.75f);
                    if (!Agent.hasPath || Vector3.Distance(Agent.destination, _pendingTarget.transform.position) > 1.0f || Mathf.Abs(Agent.stoppingDistance - stopDist) > 0.05f)
                    {
                        Agent.isStopped = false;
                        Agent.stoppingDistance = stopDist;
                        Agent.SetDestination(_pendingTarget.transform.position);
                    }
                }
            }
            else if (status == CombatTargetingStatus.InvalidTarget)
            {
                CancelPendingCombat();
            }
        }

        public bool TryExecuteReload()
        {
            if (_session == null)
            {
                OnReloadFailed?.Invoke(this, "NoSessionBound");
                return false;
            }

            var combat = _session.Capture().State.Combat;
            var weapon = combat.Get(WeaponId);
            if (weapon == null)
            {
                OnReloadFailed?.Invoke(this, "UnknownWeapon");
                return false;
            }

            if (weapon.Definition.MagazineCapacity <= 0)
            {
                OnReloadFailed?.Invoke(this, "WeaponCannotReload");
                return false;
            }

            if (weapon.MagazineRounds >= weapon.Definition.MagazineCapacity)
            {
                OnReloadFailed?.Invoke(this, "MagazineAlreadyFull");
                return false;
            }

            if (combat.Tick < weapon.NextReadyTick)
            {
                OnReloadFailed?.Invoke(this, "WeaponCoolingDown");
                return false;
            }

            long needed = weapon.Definition.MagazineCapacity - weapon.MagazineRounds;
            long packAmmo = _session.Capture().State.Inventory.GetAmount(
                InventoryContainer.Pack, weapon.Definition.AmmoItemId);
            if (packAmmo <= 0)
            {
                OnReloadFailed?.Invoke(this, "InsufficientInventory");
                return false;
            }

            var cmdId = EntityId.Parse($"cmd:reload:{ActorId.Value}:{_session.Revision}:{Guid.NewGuid():N}");
            var reloadCmd = new ReloadWeaponCommand(cmdId, WeaponId, _session.Revision);
            var result = _session.Submit(reloadCmd);

            if (result.Success)
            {
                long loaded = Math.Min(needed, packAmmo);
                OnWeaponReloaded?.Invoke(this, loaded);
                return true;
            }
            else
            {
                OnReloadFailed?.Invoke(this, result.Failure.ToString());
                return false;
            }
        }

        public bool TryExecuteShot()
        {
            if (_pendingTarget == null || _pendingTarget.IsDead)
            {
                CancelPendingCombat();
                return false;
            }

            if (_session == null)
            {
                OnAttackFailed?.Invoke(this, "NoSessionBound");
                return false;
            }

            var combat = _session.Capture().State.Combat;
            var weapon = combat.Get(WeaponId);
            if (weapon == null)
            {
                OnAttackFailed?.Invoke(this, "UnknownWeapon");
                return false;
            }

            if (combat.Tick < weapon.NextReadyTick)
            {
                // Weapon is currently cooling down
                return false;
            }

            if (weapon.Definition.MagazineCapacity > 0 && weapon.MagazineRounds < weapon.Definition.AmmoPerShot)
            {
                // Auto-attempt reload if magazine is depleted
                if (TryExecuteReload())
                {
                    return false;
                }

                OnAttackFailed?.Invoke(this, "MagazineEmpty");
                CancelPendingCombat();
                return false;
            }

            // Submit authoritative FireWeaponCommand
            var cmdId = EntityId.Parse($"cmd:fire:{ActorId.Value}:{_session.Revision}:{Guid.NewGuid():N}");
            var fireCmd = new FireWeaponCommand(cmdId, WeaponId, _session.Revision);
            var fireResult = _session.Submit(fireCmd);

            if (!fireResult.Success)
            {
                OnAttackFailed?.Invoke(this, fireResult.Failure.ToString());
                if (fireResult.Failure == CommandFailure.InsufficientInventory ||
                    fireResult.Failure == CommandFailure.WeaponNotCarried ||
                    fireResult.Failure == CommandFailure.MagazineEmpty)
                {
                    CancelPendingCombat();
                }
                return false;
            }

            OnShotFired?.Invoke(this, _pendingTarget);

            // Ballistic raycast: require actual target hitbox collision to authorize damage
            bool hitConfirmed = TacticalCombatRaycast.TryVerifyProjectileHit(
                MuzzlePosition, _pendingTarget, weaponRange, lineOfFireMask, GetComponent<Collider>(), out _);

            if (hitConfirmed)
            {
                int effectiveDamage = CombatMitigation.CalculateMitigatedWounds(damagePerShot, _pendingTarget.ArmorPermille);

                if (effectiveDamage > 0)
                {
                    // Ballistic raycast hit target hitbox! Submit authoritative damage command
                    var dmgCmdId = EntityId.Parse($"cmd:dmg:{_pendingTarget.TargetId.Value}:{_session.Revision}:{Guid.NewGuid():N}");
                    var dmgCmd = new DamageCrewCommand(dmgCmdId, _pendingTarget.TargetId, effectiveDamage, _session.Revision);
                    var dmgResult = _session.Submit(dmgCmd);

                    if (dmgResult.Success)
                    {
                        var crew = dmgResult.State.CrewDuty.Get(_pendingTarget.TargetId);
                        if (crew != null)
                        {
                            _pendingTarget.SyncFromWounds(crew.Wounds);
                        }

                        OnTargetHit?.Invoke(this, _pendingTarget, effectiveDamage);

                        if (_pendingTarget.IsDead)
                        {
                            CancelPendingCombat();
                        }
                    }
                    else
                    {
                        // Failed authoritative damage MUST NEVER reduce HP, kill targets, or emit successful hit events
                        OnAttackFailed?.Invoke(this, dmgResult.Failure.ToString());
                        if (dmgResult.Failure == CommandFailure.CrewAlreadyIncapacitated)
                        {
                            CancelPendingCombat();
                        }
                    }
                }
                else
                {
                    // Full physical armor deflection
                    OnAttackFailed?.Invoke(this, "ArmorAbsorbed");
                    OnTargetHit?.Invoke(this, _pendingTarget, 0);
                }
            }
            else
            {
                OnAttackFailed?.Invoke(this, "BallisticMiss");
            }

            return true;
        }

        private void FaceTarget(TacticalCombatTargetProxy target)
        {
            if (target == null) return;
            Vector3 lookPos = new Vector3(target.transform.position.x, transform.position.y, target.transform.position.z);
            Vector3 dir = lookPos - transform.position;
            if (dir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(dir);
            }
        }

        private void ExecutePendingInteraction()
        {
            if (_pendingInteractable != null)
            {
                var target = _pendingInteractable;
                _pendingInteractable = null;

                if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh)
                {
                    Agent.isStopped = true;
                    Agent.ResetPath();
                    Agent.stoppingDistance = 0.1f;
                }

                target.Interact(this);
            }
        }
    }
}
