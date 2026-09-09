using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TastyNutrientPaste
{
    // 让营养膏合成机根据已完成的科技产出对应版本的营养膏。
    // 原理：原版 Building_NutrientPasteDispenser.TryDispenseFood 硬编码
    //       ThingDefOf.MealNutrientPaste 作为产出。这里用一个 Transpiler
    //       把 `ldsfld ThingDefOf::MealNutrientPaste` 替换为我们的静态字段
    //       currentPasteDef（由 Prefix 根据研究进度刷新）。
    [StaticConstructorOnStartup]
    public static class DeliciousDispatcher
    {
        public static ThingDef currentPasteDef;

        static DeliciousDispatcher()
        {
            currentPasteDef = ThingDef.Named("MealNutrientPaste");

            var harmony = new Harmony("tastypaste.deliciousnutrientpaste");
            ApplyPatch(harmony, typeof(Building_NutrientPasteDispenser), "TryDispenseFood");
            ApplyPatch(harmony, typeof(FoodUtility), "BestFoodSourceOnMap");
            ApplyPatch(harmony, typeof(JobDriver_FoodDeliver), "GetReport");
            ApplyPatch(harmony, typeof(JobDriver_FoodFeedPatient), "GetReport");
        }

        static void ApplyPatch(Harmony harmony, Type targetType, string methodName)
        {
            try
            {
                var target = targetType.GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static);
                if (target == null)
                {
                    Log.Warning("[DeliciousNutrientPaste] 未找到目标方法: " + targetType.Name + "." + methodName);
                    return;
                }
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(DeliciousDispatcher), "UpdateDefPrefix"),
                    transpiler: new HarmonyMethod(typeof(DeliciousDispatcher), "SwapPasteDefTranspiler"));
            }
            catch (Exception e)
            {
                Log.Warning("[DeliciousNutrientPaste] 补丁失败: " + targetType.Name + "." + methodName + " :: " + e);
            }
        }

        // 每次取用营养膏/寻找食物来源前，根据当前研究进度刷新产出物。
        static void UpdateDefPrefix()
        {
            currentPasteDef = ResolveForResearch();
        }

        static ThingDef ResolveForResearch()
        {
            var rpL2 = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("DeliciousPasteL2");
            if (rpL2 != null && rpL2.IsFinished)
                return ThingDef.Named("MealNutrientPaste_DeliciousL2");

            var rpL1 = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("DeliciousPasteL1");
            if (rpL1 != null && rpL1.IsFinished)
                return ThingDef.Named("MealNutrientPaste_DeliciousL1");

            return ThingDef.Named("MealNutrientPaste");
        }

        static IEnumerable<CodeInstruction> SwapPasteDefTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var targetField = typeof(ThingDefOf).GetField("MealNutrientPaste",
                BindingFlags.Public | BindingFlags.Static);
            var replField = typeof(DeliciousDispatcher).GetField("currentPasteDef",
                BindingFlags.Public | BindingFlags.Static);

            foreach (var ci in instructions)
            {
                if (ci.opcode == OpCodes.Ldsfld && ReferenceEquals(ci.operand, targetField))
                    ci.operand = replField;
                yield return ci;
            }
        }
    }
}