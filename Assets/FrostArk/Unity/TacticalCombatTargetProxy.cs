using System;
using FrostArk.Core.World;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Scene proxy for an attackable combat target (hostile actor, sentry, or dummy).
    /// Provides hitbox collision bounds, aim point sampling, persistent identity,
    /// and visual death/damage state synchronization with authoritative WorldState.
    /// </summary>
    [SelectionBase]
    public class TacticalCombatTargetProxy : MonoBehaviour
    {
        [SerializeField]
        private string targetId = "actor:enemy";

        [SerializeField]
        private bool isHostile = true;

        [SerializeField]
        private Collider hitbox;

        [SerializeField]
        private Transform aimPoint;

        [SerializeField]
        private int maxHealth = 1000;

        [SerializeField]
        private int currentHealth = 1000;

        [SerializeField]
        [Range(0, 1000)]
        private int armorPermille = 0;

        private EntityId _targetIdEntity;
        private Renderer _renderer;

        public EntityId TargetId
        {
            get
            {
                if (!_targetIdEntity.IsValid)
                {
                    if (string.IsNullOrEmpty(targetId))
                        targetId = "target:" + gameObject.name;
                    _targetIdEntity = EntityId.Parse(targetId);
                }
                return _targetIdEntity;
            }
            private set => _targetIdEntity = value;
        }

        public bool IsHostile => isHostile;
        public Collider Hitbox => hitbox != null ? hitbox : (hitbox = GetComponentInChildren<Collider>());
        public Transform AimPoint => aimPoint != null ? aimPoint : transform;
        public int CurrentHealth => currentHealth;
        public int MaxHealth => maxHealth;
        public bool IsDead => currentHealth <= 0;
        public int ArmorPermille => Mathf.Clamp(armorPermille, 0, 1000);

        public event Action<TacticalCombatTargetProxy, int> OnDamaged;
        public event Action<TacticalCombatTargetProxy> OnEliminated;

        private void Awake()
        {
            if (hitbox == null)
                hitbox = GetComponentInChildren<Collider>();
            if (aimPoint == null)
                aimPoint = transform;
            _renderer = GetComponentInChildren<Renderer>();
            var _ = TargetId;
        }

        public void SetTargetId(EntityId id)
        {
            _targetIdEntity = id;
            targetId = id.ToString();
        }

        public void SetHostile(bool hostile)
        {
            isHostile = hostile;
        }

        public void SetHitbox(Collider col)
        {
            hitbox = col;
        }

        public void SetAimPoint(Transform aim)
        {
            aimPoint = aim;
        }

        public void SetHealth(int health, int max = 1000)
        {
            maxHealth = max;
            currentHealth = Mathf.Clamp(health, 0, maxHealth);
            if (IsDead && Hitbox != null)
                Hitbox.enabled = false;
        }

        public void SetArmorPermille(int value)
        {
            armorPermille = Mathf.Clamp(value, 0, 1000);
        }

        public void SyncFromWounds(int wounds)
        {
            int nextHealth = Mathf.Max(0, maxHealth - wounds);
            int delta = currentHealth - nextHealth;
            currentHealth = nextHealth;

            if (delta > 0)
                OnDamaged?.Invoke(this, delta);

            if (IsDead)
            {
                if (Hitbox != null)
                    Hitbox.enabled = false;
                UpdateDeadVisuals();
                OnEliminated?.Invoke(this);
            }
        }

        public void TakeDamage(int damagePermille)
        {
            if (IsDead || damagePermille <= 0)
                return;

            currentHealth = Mathf.Max(0, currentHealth - damagePermille);
            OnDamaged?.Invoke(this, damagePermille);

            if (IsDead)
            {
                if (Hitbox != null)
                    Hitbox.enabled = false;
                UpdateDeadVisuals();
                OnEliminated?.Invoke(this);
            }
        }

        private void UpdateDeadVisuals()
        {
            if (_renderer != null)
            {
                _renderer.material.color = new Color(0.3f, 0.3f, 0.3f, 0.8f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = isHostile ? Color.red : Color.green;
            Vector3 center = AimPoint != null ? AimPoint.position : transform.position;
            Gizmos.DrawWireSphere(center, 0.3f);
        }
    }
}
