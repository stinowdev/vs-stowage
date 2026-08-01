using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace Stowage.Patches;

internal static class PatchSupport
{
    public static int TryPatch(
        Harmony harmony,
        ILogger logger,
        MethodInfo? target,
        HarmonyMethod? prefix = null,
        HarmonyMethod? postfix = null)
    {
        if (target == null)
        {
            logger.Warning(
                "[{0}] Could not resolve a required Harmony target. The affected bag migration behavior is disabled.",
                StowageModSystem.ModId);
            return 0;
        }

        try
        {
            harmony.Patch(target, prefix: prefix, postfix: postfix);
            return 1;
        }
        catch (Exception e)
        {
            logger.Warning(
                "[{0}] Could not patch {1}.{2}: {3}",
                StowageModSystem.ModId,
                target.DeclaringType?.FullName,
                target.Name,
                e.Message);
            return 0;
        }
    }
}
