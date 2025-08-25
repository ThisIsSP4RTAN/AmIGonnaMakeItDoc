using System;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AmIGonnaMakeItDoc
{
    [StaticConstructorOnStartup]
    public static class Prognosis_Bootstrap
    {
        static Prognosis_Bootstrap()
        {
            try { new Harmony("net.S4.amigonnamakeitdoc").PatchAll(); }
            catch { /* silent */ }
        }
    }

    internal static class Prognosis_DoctorGate
    {
        private static int RequiredMedicineLevel
        {
            get
            {
                var s = Prognosis_Mod.Settings;
                return s != null ? Mathf.Clamp(s.requiredMedicineLevel, 0, 20) : 8;
            }
        }
        private static bool GateEnabled
        {
            get
            {
                var s = Prognosis_Mod.Settings;
                return s == null ? true : s.requireDoctorGate;
            }
        }

        public static void RecordDoctorSkillForAllDiseases(Pawn patient, int level)
        {
            PrognosisDoctorMemory.RecordForAllImmunizableOn(patient, level);
        }

        // Gate is now per disease
        public static bool Meets(Pawn patient, HediffDef def)
        {
            if (!GateEnabled) return true;
            if (patient == null || patient.Dead || def == null) return false;

            int lvl, tick;
            if (!PrognosisDoctorMemory.TryGet(patient, def, out lvl, out tick)) return false;
            return lvl >= RequiredMedicineLevel;
        }
    }

    // Primary hook: derived tend driver cleanup
    [HarmonyPatch]
    [HarmonyBefore("net.S4.undraftaftertend.safehooks", "net.S4.undraftafterrepair")]
    [HarmonyPriority(Priority.Last)]
    internal static class Prognosis_Tend_Cleanup_Memorize_Derived
    {
        [HarmonyPrepare]
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(JobDriver_TendPatient), "Cleanup", new Type[] { typeof(JobCondition) }) != null;
        }

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(JobDriver_TendPatient), "Cleanup", new Type[] { typeof(JobCondition) });
        }

        [HarmonyPostfix]
        public static void Postfix(JobDriver_TendPatient __instance, JobCondition condition)
        {
            try
            {
                if (condition != JobCondition.Succeeded || __instance == null) return;

                Pawn doctor = __instance.pawn;
                Pawn patient = __instance.job != null ? (__instance.job.GetTarget(TargetIndex.A).Thing as Pawn) : null;
                if (doctor == null || patient == null) return;

                int level = 0;
                if (doctor.skills != null)
                {
                    var med = doctor.skills.GetSkill(SkillDefOf.Medicine);
                    if (med != null) level = med.Level;
                }
                Prognosis_DoctorGate.RecordDoctorSkillForAllDiseases(patient, level);
            }
            catch { /* silent */ }
        }
    }

    // Fallback: base cleanup (covers edge paths)
    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.Cleanup))]
    [HarmonyBefore("net.S4.undraftaftertend.safehooks", "net.S4.undraftafterrepair")]
    [HarmonyPriority(Priority.Last)]
    internal static class Prognosis_Tend_Cleanup_Memorize_Base
    {
        [HarmonyPostfix]
        public static void Postfix(JobDriver __instance, JobCondition condition)
        {
            try
            {
                if (condition != JobCondition.Succeeded) return;
                if (__instance == null || __instance.job == null) return;
                if (__instance.job.def != JobDefOf.TendPatient) return;

                Pawn doctor = __instance.pawn;
                Pawn patient = __instance.job.GetTarget(TargetIndex.A).Thing as Pawn;
                if (doctor == null || patient == null) return;

                int level = 0;
                if (doctor.skills != null)
                {
                    var med = doctor.skills.GetSkill(SkillDefOf.Medicine);
                    if (med != null) level = med.Level;
                }
                Prognosis_DoctorGate.RecordDoctorSkillForAllDiseases(patient, level);
            }
            catch { /* silent */ }
        }
    }

    // Tooltip verdict (uses per-disease gate)
    [HarmonyPatch]
    public static class Prognosis_TooltipPatch
    {
        [HarmonyPrepare]
        public static bool Prepare()
        {
            return AccessTools.PropertyGetter(typeof(HediffComp_Immunizable), "CompTipStringExtra") != null;
        }

        [HarmonyTargetMethod]
        public static MethodInfo TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(HediffComp_Immunizable), "CompTipStringExtra");
        }

        [HarmonyPostfix]
        public static void Postfix(HediffComp_Immunizable __instance, ref string __result)
        {
            try
            {
                if (__instance == null || __instance.parent == null) return;

                var pawn = __instance.Pawn;
                var def = __instance.Def;
                if (pawn == null || def == null) return;

                // require a record for THIS disease
                if (!Prognosis_DoctorGate.Meets(pawn, def)) return;

                var handler = pawn.health != null ? pawn.health.immunity : null;
                if (handler == null) return;

                handler.TryAddImmunityRecord(def, def);
                var rec = handler.GetImmunityRecord(def);
                if (rec == null) return;

                float curImm = rec.immunity;
                if (curImm >= 1f) return; // fully immune -> no line

                float immPerDay = rec.ImmunityChangePerTick(pawn, true, __instance.parent) * 60000f;
                float sevPerDay = __instance.SeverityChangePerDay();
                float curSev = __instance.parent.Severity;

                string verdict = Verdict(curImm, immPerDay, curSev, sevPerDay);
                if (!string.IsNullOrEmpty(verdict))
                    Append(ref __result, "Prognosis: " + verdict);
            }
            catch { /* silent */ }
        }

        private static void Append(ref string s, string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (!string.IsNullOrEmpty(s)) s += "\n";
            s += line;
        }

        private static string Verdict(float curImm, float immPerDay, float curSev, float sevPerDay)
        {
            if (immPerDay <= 0f && sevPerDay <= 0f) return "Stable";
            if (immPerDay <= 0f && sevPerDay > 0f) return "At risk";
            if (sevPerDay <= 0f) return "Improving";

            float dImm = DaysSafe((1f - curImm) / Math.Max(1e-6f, immPerDay));
            float dMax = DaysSafe((1f - curSev) / Math.Max(1e-6f, sevPerDay));
            return dImm < dMax ? "Likely immune" : "At risk";
        }

        private static float DaysSafe(float days)
        {
            if (float.IsNaN(days) || float.IsInfinity(days) || days < 0f) return float.PositiveInfinity;
            return days;
        }
    }
}
