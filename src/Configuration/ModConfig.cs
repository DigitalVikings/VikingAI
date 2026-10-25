using BepInEx.Configuration;

namespace VikingAI.Configuration
{
    public static class ModConfig
    {
        // 1 - General & Navigation
        public static ConfigEntry<bool> ModEnabled { get; private set; } = null!;
        public static ConfigEntry<bool> EnableUnifiedNavGraph { get; private set; } = null!;
        public static ConfigEntry<bool> EnableTileSeamCostFix { get; private set; } = null!;
        public static ConfigEntry<float> TileSeamCostModifier { get; private set; } = null!;
        public static ConfigEntry<int> MaxAsyncTileBuildsPerTick { get; private set; } = null!;

        // 2 - Locomotion & Followers
        public static ConfigEntry<bool> EnableContextSteering { get; private set; } = null!;
        public static ConfigEntry<bool> EnableCrowdSeparation { get; private set; } = null!;
        public static ConfigEntry<bool> EnableFollowerFormations { get; private set; } = null!;
        public static ConfigEntry<bool> EnablePlayerYieldOnApproach { get; private set; } = null!;
        public static ConfigEntry<float> DoorAutoCloseDelay { get; private set; } = null!;

        // 3 - Vanilla Enemy Behavior (Opt-In/Out)
        public static ConfigEntry<bool> VanillaEnemiesUsePackTokens { get; private set; } = null!;
        public static ConfigEntry<float> MaxSimultaneousAttackTokens { get; private set; } = null!;
        public static ConfigEntry<bool> VanillaHumanoidsStrafeInCombat { get; private set; } = null!;
        public static ConfigEntry<bool> VanillaEnemiesJumpLedges { get; private set; } = null!;
        public static ConfigEntry<bool> VanillaEnemiesBreachWeakWalls { get; private set; } = null!;
        public static ConfigEntry<bool> VanillaEnemiesUseSensoryEngine { get; private set; } = null!;

        // 4 - Sensory & Stealth Engine
        public static ConfigEntry<bool> EnableSensoryPerceptionEngine { get; private set; } = null!;
        public static ConfigEntry<float> VisionConeHalfAngle { get; private set; } = null!;
        public static ConfigEntry<float> MaxVisualRange { get; private set; } = null!;
        public static ConfigEntry<float> MaxHearingRange { get; private set; } = null!;
        public static ConfigEntry<float> SuspicionGainRate { get; private set; } = null!;
        public static ConfigEntry<float> AlertGainRate { get; private set; } = null!;
        public static ConfigEntry<float> DetectionDecayRate { get; private set; } = null!;
        public static ConfigEntry<float> SearchDurationSeconds { get; private set; } = null!;

        // 5 - Virtual Simulation
        public static ConfigEntry<bool> EnableUnloadedZoneSimulation { get; private set; } = null!;
        public static ConfigEntry<bool> EnableFollowerPortalTransit { get; private set; } = null!;
        public static ConfigEntry<float> VirtualTickIntervalSeconds { get; private set; } = null!;

        public static void Init(ConfigFile config)
        {
            // 1 - General & Navigation
            ModEnabled = Bind(
                config, "1 - General & Navigation", "Enabled", true,
                "Master switch to enable or disable all features of VikingAI.",
                order: 100, isAdminOnly: true);

            EnableUnifiedNavGraph = Bind(
                config, "1 - General & Navigation", "EnableUnifiedNavGraph", true,
                "Enables the 2.5D clearance navigation graph overlay for door traversal, ledge jumping, and siege breach costing.",
                order: 90, isAdminOnly: true);

            EnableTileSeamCostFix = Bind(
                config, "1 - General & Navigation", "EnableTileSeamCostFix", true,
                "Reduces vanilla Pathfinding's 10x tile-border link penalty so creatures cross 32m tile boundaries without zig-zagging.",
                order: 80, isAdminOnly: true);

            TileSeamCostModifier = Bind(
                config, "1 - General & Navigation", "TileSeamCostModifier", 1.2f,
                "Traversal cost multiplier for 32m tile boundary links (vanilla default is 10.0).",
                order: 70, new AcceptableValueRange<float>(1.0f, 10.0f), isAdminOnly: true);

            MaxAsyncTileBuildsPerTick = Bind(
                config, "1 - General & Navigation", "MaxAsyncTileBuildsPerTick", 3,
                "Maximum number of stale NavMesh tiles processed when multiple agent sizes are active simultaneously.",
                order: 60, new AcceptableValueRange<int>(1, 8), isAdminOnly: true);

            // 2 - Locomotion & Followers
            EnableContextSteering = Bind(
                config, "2 - Locomotion & Followers", "EnableContextSteering", true,
                "Enables the 16-direction context steering pipeline, eliminating sharp turn-stutter and doorway clipping.",
                order: 100, isAdminOnly: true);

            EnableCrowdSeparation = Bind(
                config, "2 - Locomotion & Followers", "EnableCrowdSeparation", true,
                "Applies local spatial-hash separation forces so packs and followers do not collapse into a single overlapping collider.",
                order: 90, isAdminOnly: true);

            EnableFollowerFormations = Bind(
                config, "2 - Locomotion & Followers", "EnableFollowerFormations", true,
                "Distributes tamed followers and companions into polar formation slots behind the player instead of converging on one point.",
                order: 80, isAdminOnly: true);

            EnablePlayerYieldOnApproach = Bind(
                config, "2 - Locomotion & Followers", "EnablePlayerYieldOnApproach", true,
                "Causes tamed animals and friendly NPCs to step laterally aside when the player walks toward them in hallways or doorways.",
                order: 70, isAdminOnly: true);

            DoorAutoCloseDelay = Bind(
                config, "2 - Locomotion & Followers", "DoorAutoCloseDelay", 2.0f,
                "Seconds after passing through a doorway before an NPC or companion automatically closes the door behind them.",
                order: 60, new AcceptableValueRange<float>(0.5f, 10.0f), isAdminOnly: true);

            // 3 - Vanilla Enemy Behavior (Opt-In/Out)
            VanillaEnemiesUsePackTokens = Bind(
                config, "3 - Vanilla Enemy Behavior", "VanillaEnemiesUsePackTokens", false,
                "If enabled, vanilla hostile creatures coordinate attacks via Attack Tokens and flanking rings instead of independent random circling.",
                order: 100, isAdminOnly: true);

            MaxSimultaneousAttackTokens = Bind(
                config, "3 - Vanilla Enemy Behavior", "MaxSimultaneousAttackTokens", 2.5f,
                "Maximum simultaneous melee attack token budget per targeted player (Humanoid = 1.0, Large/Troll = 1.5).",
                order: 90, new AcceptableValueRange<float>(1.0f, 8.0f), isAdminOnly: true);

            VanillaHumanoidsStrafeInCombat = Bind(
                config, "3 - Vanilla Enemy Behavior", "VanillaHumanoidsStrafeInCombat", false,
                "If enabled, vanilla humanoid enemies (Skeletons, Draugr, Fulings) lock their facing onto the target and strafe/backstep in combat.",
                order: 80, isAdminOnly: true);

            VanillaEnemiesJumpLedges = Bind(
                config, "3 - Vanilla Enemy Behavior", "VanillaEnemiesJumpLedges", false,
                "If enabled, agile vanilla creatures can jump up 0.4m-1.6m ledges when a player perches on a rock or step.",
                order: 70, isAdminOnly: true);

            VanillaEnemiesBreachWeakWalls = Bind(
                config, "3 - Vanilla Enemy Behavior", "VanillaEnemiesBreachWeakWalls", false,
                "If enabled, raiding/hostile creatures evaluate structural HP/DPS breach costs to focus on weak doors/walls when sealed out.",
                order: 60, isAdminOnly: true);

            VanillaEnemiesUseSensoryEngine = Bind(
                config, "3 - Vanilla Enemy Behavior", "VanillaEnemiesUseSensoryEngine", false,
                "If enabled, vanilla wild creatures use the 4-stage physical sun/shadow, foliage cover, and sound investigation stealth model.",
                order: 50, isAdminOnly: true);

            // 4 - Sensory & Stealth Engine
            EnableSensoryPerceptionEngine = Bind(
                config, "4 - Sensory & Stealth Engine", "EnableSensoryPerceptionEngine", true,
                "Master switch for the Character-to-Character sensory perception, sunlight/shadow raycast, and foliage concealment engine.",
                order: 100, isAdminOnly: true);

            VisionConeHalfAngle = Bind(
                config, "4 - Sensory & Stealth Engine", "VisionConeHalfAngle", 60.0f,
                "Half-angle in degrees of an unalerted agent's forward vision cone (60 = 120-degree cone).",
                order: 90, new AcceptableValueRange<float>(15.0f, 180.0f), isAdminOnly: true);

            MaxVisualRange = Bind(
                config, "4 - Sensory & Stealth Engine", "MaxVisualRange", 40.0f,
                "Maximum distance in meters at which an agent can visually spot a target under full illumination.",
                order: 80, new AcceptableValueRange<float>(10.0f, 100.0f), isAdminOnly: true);

            MaxHearingRange = Bind(
                config, "4 - Sensory & Stealth Engine", "MaxHearingRange", 25.0f,
                "Maximum omnidirectional distance in meters at which an agent can hear movement and equipment noise.",
                order: 70, new AcceptableValueRange<float>(5.0f, 80.0f), isAdminOnly: true);

            SuspicionGainRate = Bind(
                config, "4 - Sensory & Stealth Engine", "SuspicionGainRate", 2.5f,
                "Detection gained per second when a target is heard or faintly glimpsed.",
                order: 60, new AcceptableValueRange<float>(0.1f, 10.0f), isAdminOnly: true);

            AlertGainRate = Bind(
                config, "4 - Sensory & Stealth Engine", "AlertGainRate", 3.0f,
                "Detection gained per second when a target is clearly seen inside the vision cone.",
                order: 50, new AcceptableValueRange<float>(0.1f, 10.0f), isAdminOnly: true);

            DetectionDecayRate = Bind(
                config, "4 - Sensory & Stealth Engine", "DetectionDecayRate", 0.5f,
                "Detection lost per second when a target breaks both line of sight and hearing.",
                order: 40, new AcceptableValueRange<float>(0.05f, 5.0f), isAdminOnly: true);

            SearchDurationSeconds = Bind(
                config, "4 - Sensory & Stealth Engine", "SearchDurationSeconds", 8.0f,
                "Seconds an investigating or searching agent inspects the last known target position before returning to idle.",
                order: 30, new AcceptableValueRange<float>(2.0f, 60.0f), isAdminOnly: true);

            // 5 - Virtual Simulation
            EnableUnloadedZoneSimulation = Bind(
                config, "5 - Virtual Simulation", "EnableUnloadedZoneSimulation", true,
                "Enables 1 Hz ZDO simulation and deterministic catch-up for registered settlement NPCs and patrols in unloaded zones.",
                order: 100, isAdminOnly: true);

            EnableFollowerPortalTransit = Bind(
                config, "5 - Virtual Simulation", "EnableFollowerPortalTransit", true,
                "Automatically brings active party companions (with CanTraversePortals enabled on their profile) through portals and dungeon transitions.",
                order: 90, isAdminOnly: true);

            VirtualTickIntervalSeconds = Bind(
                config, "5 - Virtual Simulation", "VirtualTickIntervalSeconds", 1.0f,
                "Interval in seconds between background ZDO ticks for unloaded virtual agents.",
                order: 80, new AcceptableValueRange<float>(0.25f, 10.0f), isAdminOnly: true);
        }

        private static ConfigEntry<T> Bind<T>(
            ConfigFile config,
            string section,
            string key,
            T defaultValue,
            string description,
            int order = 0,
            AcceptableValueBase? acceptableValues = null,
            bool isAdminOnly = false)
        {
            var tags = new ConfigurationManagerAttributes
            {
                Order = order,
                Category = section,
                IsAdminOnly = isAdminOnly
            };
            var configDesc = new ConfigDescription(description, acceptableValues, tags);
            return config.Bind(section, key, defaultValue, configDesc);
        }
    }
}
