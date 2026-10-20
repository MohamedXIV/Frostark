using UnityEngine;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Test-only read-only sensor source and command-request recorder.
    /// Not a real Needs system or inventory state; never use as gameplay authority.
    /// </summary>
    public sealed class GoapHungerProbeFacts : MonoBehaviour
    {
        public int Hunger { get; set; }
        public bool FoodAvailable { get; set; }
        public int RequestedMeals { get; private set; }

        public void RequestMeal() => this.RequestedMeals++;
    }
}
