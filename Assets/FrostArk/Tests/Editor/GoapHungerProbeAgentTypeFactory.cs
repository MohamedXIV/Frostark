using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

namespace FrostArk.Unity.Tests
{
    /// <summary>Test-only code-built GOAP agent type for the hunger probe.</summary>
    public sealed class GoapHungerProbeAgentTypeFactory : AgentTypeFactoryBase
    {
        public override IAgentTypeConfig Create()
        {
            var builder = this.CreateBuilder("FrostArk.HungerProbe");
            builder.AddCapability<GoapHungerProbeCapability>();
            return builder.Build();
        }
    }
}
