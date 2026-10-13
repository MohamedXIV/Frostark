using UnityEngine;

namespace FrostArk.Unity
{
    /// <summary>
    /// Visible feedback placed at ordered move destination.
    /// Fades or disappears after display duration.
    /// </summary>
    public class TacticalDestinationMarker : MonoBehaviour
    {
        [SerializeField]
        private float displayDuration = 1.5f;

        private float _timer;

        public void Show(Vector3 worldPoint)
        {
            transform.position = worldPoint + Vector3.up * 0.05f;
            gameObject.SetActive(true);
            _timer = displayDuration;
        }

        private void Update()
        {
            if (_timer > 0f)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    gameObject.SetActive(false);
                }
            }
        }
    }
}
