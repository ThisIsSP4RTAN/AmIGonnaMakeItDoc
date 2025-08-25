using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AmIGonnaMakeItDoc
{
    [HarmonyPatch]
    public static class Prognosis_RiskAlert
    {
        // Prevent duplicate letters per (pawn, disease)
        private static readonly HashSet<string> alerted = new HashSet<string>();

        [HarmonyTargetMethod]
        public static System.Reflection.MethodBase TargetMethod()
        {
            // internal void ImmunityHandlerTickInterval(int delta)
            return AccessTools.Method(typeof(ImmunityHandler), "ImmunityHandlerTickInterval", new Type[] { typeof(int) });
        }

        [HarmonyPostfix]
        public static void Postfix(ImmunityHandler __instance, int delta)
        {
            var settings = Prognosis_Mod.Settings;
            if (settings == null || !settings.enableRiskAlert) return;
            if (__instance == null || __instance.pawn == null) return;

            Pawn pawn = __instance.pawn;
            var list = __instance.ImmunityListForReading;
            if (list == null || list.Count == 0) return;

            float threshold = settings.riskAlertSeverityPercent / 100f;

            for (int i = 0; i < list.Count; i++)
            {
                var rec = list[i];
                if (rec == null || rec.hediffDef == null) continue;

                Hediff disease = (pawn.health != null && pawn.health.hediffSet != null)
                    ? pawn.health.hediffSet.GetFirstHediffOfDef(rec.hediffDef)
                    : null;

                string key = pawn.thingIDNumber.ToString() + "#" + rec.hediffDef.defName;

                // If disease is gone, clear flag so future infections can alert again
                if (disease == null)
                {
                    alerted.Remove(key);
                    continue;
                }

                // Gate dependency: only alert if the pawn qualifies for a prognosis line
                if (!Prognosis_DoctorGate.Meets(pawn, rec.hediffDef))
                {
                    alerted.Remove(key); // allow alert later once gate is met
                    continue;
                }

                var hwc = disease as HediffWithComps;
                if (hwc == null) continue;
                var immComp = hwc.TryGetComp<HediffComp_Immunizable>();
                if (immComp == null) continue;

                float curSev = hwc.Severity;

                // Engine rates
                float immPerDay = rec.ImmunityChangePerTick(pawn, true, hwc) * 60000f;
                float sevPerDay = immComp.SeverityChangePerDay();
                float curImm = rec.immunity;

                bool atRisk = IsAtRisk(curImm, immPerDay, curSev, sevPerDay);

                if (atRisk && curSev >= threshold && !alerted.Contains(key))
                {
                    alerted.Add(key);
                    SendRiskLetter(pawn, rec.hediffDef, curSev);
                }
            }
        }

        private static bool IsAtRisk(float curImm, float immPerDay, float curSev, float sevPerDay)
        {
            if (immPerDay <= 0f && sevPerDay > 0f) return true;  // disease rises, no immunity gain
            if (sevPerDay <= 0f) return false;                    // disease not growing

            float dImm = (1f - curImm) / Math.Max(1e-6f, immPerDay);
            float dMax = (1f - curSev) / Math.Max(1e-6f, sevPerDay);
            return dImm >= dMax;
        }

        private static void SendRiskLetter(Pawn pawn, HediffDef def, float curSev)
        {
            // Avoid TaggedString/string ternary by resolving first
            string defLabelCap = def != null ? def.LabelCap.ToString() : "disease";
            string title = "At risk: " + pawn.LabelShortCap + " - " + defLabelCap;

            string defLabelLower = def != null ? def.label : "a disease";
            string body = pawn.LabelShortCap + " is at risk from " + defLabelLower
                          + " (severity " + ((int)(curSev * 100f)).ToString() + "%).";

            Find.LetterStack.ReceiveLetter(title, body, LetterDefOf.ThreatBig, pawn);
        }
    }
}
