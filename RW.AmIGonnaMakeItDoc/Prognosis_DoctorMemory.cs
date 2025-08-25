using System.Collections.Generic;
using Verse;
using RimWorld;

namespace AmIGonnaMakeItDoc
{
    // One per save file
    public class PrognosisDoctorMemory : GameComponent
    {
        // key: "<pawnLoadId>|<hediffDefName>"  -> TendInfo(level, lastTick)
        private Dictionary<string, TendInfo> tendByKey = new Dictionary<string, TendInfo>();

        // scribe helpers
        private List<string> _keys;
        private List<TendInfo> _vals;

        // periodic cleanup
        private int lastCleanupTick;

        public PrognosisDoctorMemory() { }
        public PrognosisDoctorMemory(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref tendByKey, "prognosisTendByKey",
                LookMode.Value, LookMode.Deep, ref _keys, ref _vals);
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (Find.TickManager == null) return;

            int ticks = Find.TickManager.TicksGame;
            if (ticks - lastCleanupTick < 2500) return; // ~hourly
            lastCleanupTick = ticks;

            CleanupOrphansAndRecovered();
        }

        private void CleanupOrphansAndRecovered()
        {
            // Alive anywhere (maps, caravans, temp)
            var alive = PawnsFinder.AllMapsWorldAndTemporary_Alive;
            var aliveById = new Dictionary<string, Pawn>(alive.Count);
            for (int i = 0; i < alive.Count; i++)
            {
                Pawn p = alive[i];
                if (p != null) aliveById[p.GetUniqueLoadID()] = p;
            }

            if (tendByKey == null || tendByKey.Count == 0) return;

            List<string> remove = new List<string>();
            foreach (var kv in tendByKey)
            {
                string pawnId; string defName;
                SplitKey(kv.Key, out pawnId, out defName);
                Pawn p;
                if (!aliveById.TryGetValue(pawnId, out p))
                {
                    // not alive anywhere -> remove
                    remove.Add(kv.Key);
                    continue;
                }

                // remove if that disease no longer exists or is fully immune
                if (!HasActiveImmunizable(p, defName))
                    remove.Add(kv.Key);
            }

            for (int i = 0; i < remove.Count; i++) tendByKey.Remove(remove[i]);
        }

        private static bool HasActiveImmunizable(Pawn p, string defName)
        {
            if (p == null || p.Dead || p.health == null || p.health.hediffSet == null) return false;
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            if (def == null) return false;

            var h = p.health.hediffSet.GetFirstHediffOfDef(def) as HediffWithComps;
            if (h == null) return false;

            var imm = h.TryGetComp<HediffComp_Immunizable>();
            return imm != null && !imm.FullyImmune;
        }

        private static string MakeKey(Pawn p, HediffDef def)
        {
            return (p != null ? p.GetUniqueLoadID() : "null") + "|" + (def != null ? def.defName : "null");
        }

        private static void SplitKey(string key, out string pawnId, out string defName)
        {
            int i = key.IndexOf('|');
            if (i < 0) { pawnId = key; defName = ""; return; }
            pawnId = key.Substring(0, i);
            defName = key.Substring(i + 1);
        }

        private static PrognosisDoctorMemory GetOrCreate()
        {
            if (Current.Game == null) return null;
            var comp = Current.Game.GetComponent<PrognosisDoctorMemory>();
            if (comp == null)
            {
                comp = new PrognosisDoctorMemory(Current.Game);
                Current.Game.components.Add(comp);
            }
            return comp;
        }

        public static void RecordForAllImmunizableOn(Pawn patient, int level)
        {
            var comp = GetOrCreate();
            if (comp == null || patient == null || patient.health == null || patient.health.hediffSet == null) return;

            int now = Find.TickManager.TicksGame;
            var hediffs = patient.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                var hwc = hediffs[i] as HediffWithComps;
                if (hwc == null) continue;
                var imm = hwc.TryGetComp<HediffComp_Immunizable>();
                if (imm == null || imm.FullyImmune) continue;

                string key = MakeKey(patient, hwc.def);
                comp.tendByKey[key] = new TendInfo { level = level, lastTick = now };
            }
        }

        public static bool TryGet(Pawn patient, HediffDef def, out int level, out int lastTick)
        {
            level = 0; lastTick = 0;
            var comp = GetOrCreate();
            if (comp == null || patient == null || def == null) return false;

            TendInfo info;
            if (!comp.tendByKey.TryGetValue(MakeKey(patient, def), out info) || info == null) return false;
            level = info.level;
            lastTick = info.lastTick;
            return true;
        }

        public static void ClearFor(Pawn patient, HediffDef def)
        {
            var comp = GetOrCreate();
            if (comp == null || patient == null || def == null) return;
            comp.tendByKey.Remove(MakeKey(patient, def));
        }

        // Stored value
        public class TendInfo : IExposable
        {
            public int level;
            public int lastTick;

            public void ExposeData()
            {
                Scribe_Values.Look(ref level, "lvl", 0);
                Scribe_Values.Look(ref lastTick, "tick", 0);
            }
        }
    }
}