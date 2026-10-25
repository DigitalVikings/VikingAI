using System.Collections.Generic;
using System.Runtime.CompilerServices;
using VikingAI.Configuration;
using VikingAI.Profiles;

namespace VikingAI.Tactics
{
    /// <summary>
    /// Evaluates registered IAIBrainModule instances in utility-score order.
    /// Ensures only a single winning brain module issues pathfinding/locomotion commands per tick,
    /// eliminating Postfix double-pathing bugs.
    /// </summary>
    public static class BrainPipeline
    {
        private static readonly List<IAIBrainModule> _modules = new List<IAIBrainModule>(8);
        private static readonly ConditionalWeakTable<BaseAI, IAIBrainModule> _activeModuleByAi =
            new ConditionalWeakTable<BaseAI, IAIBrainModule>();

        public static void RegisterModule(IAIBrainModule module)
        {
            if (module == null) return;
            _modules.RemoveAll(m => m.Id == module.Id);
            _modules.Add(module);
        }

        public static IAIBrainModule? GetActiveModule(BaseAI ai)
        {
            if (ai != null && _activeModuleByAi.TryGetValue(ai, out var mod))
            {
                return mod;
            }
            return null;
        }

        public static bool TryExecuteModules(BaseAI ai, Character character, float dt)
        {
            if (!ModConfig.ModEnabled.Value || _modules.Count == 0)
            {
                return false;
            }

            AgentBehaviorProfile profile = AgentProfileRegistry.GetProfile(character);
            IAIBrainModule? bestModule = null;
            float bestScore = 0.0f;

            for (int i = 0; i < _modules.Count; i++)
            {
                IAIBrainModule candidate = _modules[i];
                float score = candidate.EvaluateUtility(ai, character, profile);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestModule = candidate;
                }
            }

            _activeModuleByAi.Remove(ai);
            if (bestModule == null)
            {
                return false;
            }

            _activeModuleByAi.Add(ai, bestModule);
            return bestModule.Tick(ai, character, profile, dt);
        }
    }
}
