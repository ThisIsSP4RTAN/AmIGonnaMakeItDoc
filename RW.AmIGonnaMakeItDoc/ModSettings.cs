using UnityEngine;
using Verse;

namespace AmIGonnaMakeItDoc
{
    public class PrognosisSettings : ModSettings
    {
        public bool requireDoctorGate = true;
        public int requiredMedicineLevel = 12;

        public bool enableRiskAlert = true;
        public int riskAlertSeverityPercent = 80; // 0..100

        public override void ExposeData()
        {
            Scribe_Values.Look(ref requireDoctorGate, "requireDoctorGate", true);
            Scribe_Values.Look(ref requiredMedicineLevel, "requiredMedicineLevel", 12);

            Scribe_Values.Look(ref enableRiskAlert, "enableRiskAlert", true);
            Scribe_Values.Look(ref riskAlertSeverityPercent, "riskAlertSeverityPercent", 80);
        }
    }

    public class Prognosis_Mod : Mod
    {
        public static PrognosisSettings Settings;

        public Prognosis_Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PrognosisSettings>();
        }

        public override string SettingsCategory() => "Am I Gonna Make It, Doc?";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var s = Settings;
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            // Toggle: require a sufficiently skilled tend for prognosis to appear
            listing.CheckboxLabeled(
                "Require skilled tend for prognosis?",
                ref s.requireDoctorGate,
                "If off, the prognosis line always appears (no doctor skill gate)."
            );

            // Only show the slider when the gate is enabled
            if (s.requireDoctorGate)
            {
                listing.Gap(6f);
                listing.Label("Required Medicine level: " + s.requiredMedicineLevel);
                Rect sliderRect = listing.GetRect(24f);
                float lvlF = Widgets.HorizontalSlider(sliderRect, s.requiredMedicineLevel, 0f, 20f, true, null, "0", "20", 1f);
                s.requiredMedicineLevel = Mathf.Clamp(Mathf.RoundToInt(lvlF), 0, 20);
            }

            listing.GapLine(12f);

            // Risk alert
            listing.CheckboxLabeled(
                "Enable 'At risk' alert (raid-style)?",
                ref s.enableRiskAlert,
                "Show a red letter if a disease is 'At risk' and severity crosses the threshold."
            );

            if (s.enableRiskAlert)
            {
                listing.Gap(6f);
                listing.Label("Alert at disease severity ≥ " + s.riskAlertSeverityPercent + "%");
                Rect sliderRect = listing.GetRect(24f);
                float pF = Widgets.HorizontalSlider(sliderRect, s.riskAlertSeverityPercent, 0f, 100f, true, null, "0%", "100%", 1f);
                s.riskAlertSeverityPercent = Mathf.Clamp(Mathf.RoundToInt(pF), 0, 100);
            }

            listing.End();
        }
    }
}