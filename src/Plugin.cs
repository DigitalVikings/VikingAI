using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using VikingAI.Configuration;
using VikingAI.FlowField;
using VikingAI.Navigation;
using VikingAI.NPC.Affordances;
using VikingAI.NPC.Utility;
using VikingAI.Simulation;
using VikingAI.Tactics;
using VikingAI.Telemetry;

namespace VikingAI
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid, BepInDependency.DependencyFlags.HardDependency)]
    public class VikingAIPlugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.digitalvikings.vikingai";
        public const string ModName = "VikingAI";
        public const string ModVersion = "0.1.0";

        public static VikingAIPlugin Instance { get; private set; } = null!;
        public static ManualLogSource Log { get; internal set; } = null!;
        private Harmony _harmony = null!;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo($"{ModName} v{ModVersion} initializing...");

            // 1. Initialize server-synced configuration
            ModConfig.Init(Config);

            // 2. Register core BrainPipeline modules
            BrainPipeline.RegisterModule(new PackFlankBrainModule());
            BrainPipeline.RegisterModule(new InvestigateBrainModule());
            BrainPipeline.RegisterModule(new SmartAffordanceBrainModule());

            // 3. Attach debug overlay component & register console commands
            gameObject.AddComponent<NavDebugOverlay>();
            VikingAIConsoleCommand.Register();

            // 4. Apply Harmony patches
            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll();

            Log.LogInfo($"{ModName} v{ModVersion} loaded successfully.");

            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.jotunn.jotunn"))
            {
                HookJotunnSync();
            }
        }

        private void Update()
        {
            if (!ModConfig.ModEnabled.Value) return;

            VirtualAgentDirector.UpdateDirector(Time.deltaTime);

            if (NavDebugOverlay.ActiveMode != DebugDrawMode.Off)
            {
                NavGraphManager.DrawDebugTelemetry();
                TacticalFlowField.DrawDebugTelemetry();
                AffordanceRegistry.DrawDebugTelemetry();
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void HookJotunnSync()
        {
            Jotunn.Managers.SynchronizationManager.OnConfigurationSynchronized += (sender, args) =>
            {
                Log.LogInfo("Server configuration synchronized successfully.");
            };
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
