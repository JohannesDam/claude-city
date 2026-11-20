using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using GB = Game.Buildings;
using GP = Game.Prefabs;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        static readonly string[] EduLevels = { "elementary", "highschool", "college", "university" };

        /// <summary>
        /// The numbers of the in-game Education info view, computed by the game itself: the private
        /// EducationInfoviewUISystem.ResetResults / UpdateStudentCounts / UpdateEligibility are invoked
        /// (they schedule their jobs and Complete() them) after completing all tracked jobs, then
        /// m_Results is read: [5..8] eligible (expected number of citizens that would enrol, from
        /// ApplyToSchoolSystem.GetEnteringProbability), [9..12] students, [13..16] capacity (prefab + upgrades; schools at 0% efficiency skipped).
        /// </summary>
        int[] EducationInfoviewResults(out string error)
        {
            error = null;
            var sys = world.GetExistingSystemManaged<EducationInfoviewUISystem>();
            if (sys == null) { error = "EducationInfoviewUISystem not found"; return null; }
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var t = typeof(EducationInfoviewUISystem);
            var reset = t.GetMethod("ResetResults", F | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            var students = t.GetMethod("UpdateStudentCounts", F | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            var eligible = t.GetMethod("UpdateEligibility", F | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            var resultsField = t.GetField("m_Results", F | BindingFlags.DeclaredOnly);
            if (reset == null || students == null || eligible == null || resultsField == null) { error = "EducationInfoviewUISystem members changed"; return null; }
            em.CompleteAllTrackedJobs(); // the UI jobs are scheduled on the UI system's own dependency only
            try
            {
                reset.Invoke(sys, null);
                students.Invoke(sys, null);
                eligible.Invoke(sys, null);
            }
            catch (TargetInvocationException e) { error = (e.InnerException ?? e).Message; return null; }
            var arr = (NativeArray<int>)resultsField.GetValue(sys);
            if (!arr.IsCreated) { error = "results not allocated"; return null; }
            return arr.ToArray();
        }

        /// <summary>
        /// GET /education: per level eligible (expected applicants, as the Education info view) vs
        /// students vs seats, the shortfall, how many schools are full, and how many seats unbuilt
        /// classroom extensions on existing schools would add (GET /extensions?filter=School).
        /// </summary>
        object Education(Pending p)
        {
            var res = EducationInfoviewResults(out var err);
            var schools = CollectServices("education");
            var levels = new List<object>();
            var advice = new List<object>();
            for (int i = 0; i < 4; i++)
            {
                var name = EduLevels[i];
                var mine = schools.Where(s => s.Type == name).ToList();
                int seatsPrefab = mine.Sum(s => s.UserCap), studentsCount = mine.Sum(s => s.Users);
                int full = mine.Count(s => s.Status.Contains("full")), nearly = mine.Count(s => s.Status.Contains("nearly full"));
                // unbuilt, unlocked upgrades of these schools that add seats
                int extSeats = 0, extCount = 0; long extCost = 0;
                var extNames = new Dictionary<string, int>();
                foreach (var s in mine)
                {
                    var op = em.GetComponentData<GP.PrefabRef>(s.Entity).m_Prefab;
                    foreach (var kv in OfferedUpgrades(op))
                    {
                        var up = kv.Key;
                        if (IsLocked(up) || !em.HasComponent<GP.SchoolData>(up)) continue;
                        if (ForbidMultiple(up) && CountInstalled(s.Entity, up) > 0) continue;
                        int add = em.GetComponentData<GP.SchoolData>(up).m_StudentCapacity;
                        if (add <= 0) continue;
                        extSeats += add; extCount++; extCost += UpgradeCost(up);
                        var un = PrefabName(up);
                        extNames[un] = extNames.TryGetValue(un, out var c) ? c + 1 : 1;
                        break; // one suggestion per school
                    }
                }
                var d = new Dictionary<string, object>
                {
                    { "level", name },
                    { "schools", mine.Count }, { "full", full }, { "nearlyFull", nearly },
                    { "students", studentsCount }, { "seats", seatsPrefab },
                };
                if (res != null)
                {
                    int eligible = res[5 + i], st = res[9 + i], cap = res[13 + i];
                    d["eligible"] = eligible;
                    d["infoviewStudents"] = st;
                    d["infoviewCapacity"] = cap;
                    d["shortfall"] = math.max(0, eligible - cap);
                    d["availabilityPct"] = eligible > 0 ? Math.Round(100.0 * cap / eligible, 1) : (double?)null;
                    if (eligible > cap)
                    {
                        string fix = extSeats > 0 ? $"; {extCount} classroom extension(s) on existing schools add {extSeats} seats for {extCost}" : "";
                        advice.Add($"{name}: {eligible} eligible vs {cap} seats -> short {eligible - cap}{fix}");
                    }
                }
                if (extCount > 0)
                    d["extensionsPossible"] = new Dictionary<string, object> { { "count", extCount }, { "seats", extSeats }, { "cost", extCost }, { "prefabs", extNames } };
                levels.Add(d);
            }
            var r = new Dictionary<string, object>
            {
                { "levels", levels },
                { "note", "eligible = expected applicants as the in-game Education info view (citizens of moved-in households, students counted at their level, others weighted by ApplyToSchoolSystem.GetEnteringProbability); infoviewStudents/infoviewCapacity skip schools at 0% efficiency (as the info view); seats = prefab+upgrade capacity of all schools" },
            };
            if (err != null) r["infoviewError"] = err;
            if (advice.Count > 0) r["advice"] = advice;
            return r;
        }
    }
}
