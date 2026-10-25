using System;
using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;
using VikingAI.Core;

namespace VikingAI.Telemetry
{
    /// <summary>
    /// Registers the `vikingai` developer console command via Jötunn CommandManager.
    /// Usage: `vikingai debug [grid|flow|steering|tokens|affordances|senses|all|off]` or `vikingai stats`.
    /// </summary>
    public sealed class VikingAIConsoleCommand : ConsoleCommand
    {
        public override string Name => "vikingai";
        public override string Help => "VikingAI diagnostic telemetry and debug visualization (subcommands: debug, stats)";
        public override bool IsCheat => false;

        public static void Register()
        {
            CommandManager.Instance.AddConsoleCommand(new VikingAIConsoleCommand());
        }

        public override void Run(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.instance.Print("Usage: vikingai debug [grid|flow|steering|tokens|affordances|senses|all|off] | vikingai stats");
                return;
            }

            string sub = args[0].ToLowerInvariant();
            if (sub == "debug")
            {
                string modeArg = args.Length > 1 ? args[1].ToLowerInvariant() : "all";
                switch (modeArg)
                {
                    case "off":
                    case "none":
                        NavDebugOverlay.ActiveMode = DebugDrawMode.Off;
                        break;
                    case "grid":
                        ToggleFlag(DebugDrawMode.Grid);
                        break;
                    case "flow":
                        ToggleFlag(DebugDrawMode.Flow);
                        break;
                    case "steering":
                        ToggleFlag(DebugDrawMode.Steering);
                        break;
                    case "tokens":
                        ToggleFlag(DebugDrawMode.Tokens);
                        break;
                    case "affordances":
                        ToggleFlag(DebugDrawMode.Affordances);
                        break;
                    case "senses":
                        ToggleFlag(DebugDrawMode.Senses);
                        break;
                    case "all":
                        NavDebugOverlay.ActiveMode = DebugDrawMode.All;
                        break;
                    default:
                        Console.instance.Print($"Unknown debug mode '{modeArg}'. Valid modes: grid, flow, steering, tokens, affordances, senses, all, off");
                        return;
                }

                Console.instance.Print($"[VikingAI] Debug overlay mode: {NavDebugOverlay.ActiveMode}");
            }
            else if (sub == "stats")
            {
                Console.instance.Print(
                    $"[VikingAI] Tracked pieces — Doors: {PieceRegistry.Doors.Count}, " +
                    $"Smelters: {PieceRegistry.Smelters.Count}, Containers: {PieceRegistry.Containers.Count}, " +
                    $"Fireplaces: {PieceRegistry.Fireplaces.Count}, Plants: {PieceRegistry.Plants.Count}, " +
                    $"Pickables: {PieceRegistry.Pickables.Count}, Beds: {PieceRegistry.Beds.Count}, Chairs: {PieceRegistry.Chairs.Count}");
            }
            else
            {
                Console.instance.Print("Unknown subcommand. Usage: vikingai debug [mode] | vikingai stats");
            }
        }

        public override List<string> CommandOptionList()
        {
            return new List<string>
            {
                "debug",
                "stats"
            };
        }

        private static void ToggleFlag(DebugDrawMode flag)
        {
            if ((NavDebugOverlay.ActiveMode & flag) != 0)
            {
                NavDebugOverlay.ActiveMode &= ~flag;
            }
            else
            {
                NavDebugOverlay.ActiveMode |= flag;
            }
        }
    }
}
