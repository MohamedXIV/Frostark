using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace FrostArk.Unity.Tests
{
    // An isolated EditMode fixture, NOT FrostArk's persisted Needs or food inventory.
    // It deliberately provides read-only sensor facts and records command requests.
    // It must never be used as an alternative authoritative hunger clock.
    public sealed class GoapHungerProbeFacts : MonoBehaviour
    {
        public int Hunger { get; set; }
        public bool FoodAvailable { get; set; }
        public int RequestedMeals { get; private set; }

        public void RequestMeal() => RequestedMeals++;
    }

    // Binary urgency fact (1 = critical); avoid asking the planner to apply 75\n    // one-unit Decrease effects for a scalar hunger level of 95.\n    public sealed class GoapHungerKey : WorldKeyBase { }
    public sealed class GoapFoodAvailableKey : WorldKeyBase { }
    public sealed class GoapSatisfyHungerGoal : GoalBase { }

    public sealed class GoapHungerProbeSensor : MultiSensorBase
    {
        public GoapHungerProbeSensor()
        {
            this.AddLocalWorldSensor<GoapHungerKey>((actor, references) =>
                references.GetCachedComponent<GoapHungerProbeFacts>().Hunger >= 80 ? 1 : 0);
            this.AddLocalWorldSensor<GoapFoodAvailableKey>((actor, references) =>
                references.GetCachedComponent<GoapHungerProbeFacts>().FoodAvailable ? 1 : 0);
        }

        public override void Created() { }
        public override void Update() { }
    }

    public sealed class GoapRequestMealAction : GoapActionBase<GoapRequestMealAction.Data>
    {
        public override bool IsValid(IActionReceiver actor, Data data) =>
            data.Facts != null && data.Facts.FoodAvailable && data.Facts.Hunger >= 80;

        public override IActionRunState Perform(IMonoAgent actor, Data data, IActionContext context) =>
            ActionRunState.Completed;

        public override void Complete(IMonoAgent actor, Data data)
        {
            // A request only. Actual food consumption and Need change must be
            // committed by a future validated FrostArk Core command, not GOAP.
            data.Facts.RequestMeal();
        }

        public sealed class Data : IActionData
        {
            public ITarget Target { get; set; }

            [GetComponent]
            public GoapHungerProbeFacts Facts { get; set; }
        }
    }

    public sealed class GoapHungerProbeCapability : CapabilityFactoryBase
    {
        public override ICapabilityConfig Create()
        {
            var builder = new CapabilityBuilder("FrostArk.HungerProbe");
            builder.AddGoal<GoapSatisfyHungerGoal>()
                .AddCondition<GoapHungerKey>(Comparison.SmallerThanOrEqual, 0);
            builder.AddAction<GoapRequestMealAction>()
                .AddCondition<GoapFoodAvailableKey>(Comparison.GreaterThanOrEqual, 1)
                .AddEffect<GoapHungerKey>(EffectType.Decrease)
                .SetRequiresTarget(false);
            builder.AddMultiSensor<GoapHungerProbeSensor>();
            return builder.Build();
        }
    }

    // Programmatic factory uses upstream V3 types and the REAL GOAP resolver.
    public sealed class GoapHungerProbeAgentTypeFactory : AgentTypeFactoryBase
    {
        public override IAgentTypeConfig Create()
        {
            var builder = this.CreateBuilder("FrostArk.HungerProbe");
            builder.AddCapability<GoapHungerProbeCapability>();
            return builder.Build();
        }
    }

    [TestFixture]
    public sealed class GoapHungerPlanningEditModeTests
    {
        private GameObject goapHost;
        private GameObject crew;
        private GoapHungerProbeFacts facts;
        private GoapActionProvider provider;
        private AgentBehaviour actor;

        [SetUp]
        public void SetUp()
        {
            // Deliberately start inactive so the GOAP configuration is present
            // BEFORE GoapBehaviour.Awake builds the graph.
            this.goapHost = new GameObject("GOAP hunger spike runner");
            this.goapHost.SetActive(false);
            this.goapHost.AddComponent<ManualControllerBehaviour>();
            var agentTypeFactory = this.goapHost.AddComponent<GoapHungerProbeAgentTypeFactory>();
            var goap = this.goapHost.AddComponent<GoapBehaviour>();
            goap.agentTypeConfigFactories.Add(agentTypeFactory);
            this.goapHost.SetActive(true);

            this.crew = new GameObject("GOAP hunger spike crew");
            this.crew.SetActive(false);
            this.facts = this.crew.AddComponent<GoapHungerProbeFacts>();
            this.provider = this.crew.AddComponent<GoapActionProvider>();
            this.actor = this.crew.AddComponent<AgentBehaviour>();
            this.crew.SetActive(true);

            this.actor.ActionProvider = this.provider;
            this.actor.RunInUnityUpdate = false;
            this.provider.AgentType = goap.GetAgentType("FrostArk.HungerProbe");
        }

        [TearDown]
        public void TearDown()
        {
            if (this.crew != null)
                Object.DestroyImmediate(this.crew);
            if (this.goapHost != null)
                Object.DestroyImmediate(this.goapHost);
        }

        [Test]
        public void CriticalHungerWithFoodResolvesOfficialGoapActionWithoutMutatingNeeds()
        {
            this.facts.Hunger = 95;
            this.facts.FoodAvailable = true;

            this.provider.RequestGoal<GoapSatisfyHungerGoal>();

            // This action must come from CrashKonijn's planner, not test code.
            Assert.That(this.provider.CurrentPlan, Is.Not.Null);
            Assert.That(this.provider.CurrentPlan.Action, Is.InstanceOf<GoapRequestMealAction>());
            Assert.That(this.facts.RequestedMeals, Is.Zero);

            this.actor.Run(0.1f);

            Assert.That(this.facts.RequestedMeals, Is.EqualTo(1),
                "The GOAP action should request a world command exactly once.");
            Assert.That(this.facts.Hunger, Is.EqualTo(95),
                "GOAP must not pretend it persisted a Need change.");
        }

        [Test]
        public void CriticalHungerWithNoFoodDoesNotEmitMealCommand()
        {
            this.facts.Hunger = 95;
            this.facts.FoodAvailable = false;

            this.provider.RequestGoal<GoapSatisfyHungerGoal>();
            this.actor.Run(0.1f);

            Assert.That(this.facts.RequestedMeals, Is.Zero);
        }
    }
}
