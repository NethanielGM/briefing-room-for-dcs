using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriefingRoom4DCS.Data;
using BriefingRoom4DCS.Generator.UnitMaker;
using BriefingRoom4DCS.Mission;
using BriefingRoom4DCS.Mission.DCSLuaObjects;
using BriefingRoom4DCS.Template;

namespace BriefingRoom4DCS.Generator.Mission.Objectives
{
    internal class Transport
    {
        internal static List<Waypoint> CreateObjective(
    MissionTemplateSubTaskRecord task,
    DBEntryObjectiveTask taskDB,
    DBEntryObjectiveTarget targetDB,
    DBEntryObjectiveTargetBehavior targetBehaviorDB,
    ref int objectiveIndex,
    ref Coordinates objectiveCoordinates,
    ObjectiveOption[] objectiveOptions,
    ref DCSMission mission,
    string[] featuresID)
        {
            var extraSettings = new Dictionary<string, object>();
            List<string> units = [];
            List<DBEntryJSONUnit> unitDBs = [];
            var (luaUnit, unitCount, unitCountMinMax, objectiveTargetUnitFamilies, groupFlags) = ObjectiveUtils.GetUnitData(task, targetDB, targetBehaviorDB, objectiveOptions);
            var isInverseTransportWayPoint = false;
            (units, unitDBs) = UnitGenerator.GetUnits(ref mission, objectiveTargetUnitFamilies, unitCount, taskDB.TargetSide, groupFlags, ref extraSettings, targetBehaviorDB.IsStatic);
            var objectiveTargetUnitFamily = objectiveTargetUnitFamilies.First();
            if (units.Count == 0 || unitDBs.Count == 0)
                throw new BriefingRoomException(mission.LangKey, "NoUnitsForTimePeriod", taskDB.TargetSide, objectiveTargetUnitFamily);
            var unitDB = unitDBs.First();

            // Set destination point for moving unit groups
            Coordinates destinationPoint = objectiveCoordinates +
                (
                    targetDB.UnitCategory switch
                    {
                        UnitCategory.Plane => Coordinates.CreateRandom(30, 60),
                        UnitCategory.Helicopter => Coordinates.CreateRandom(10, 20),
                        _ => objectiveTargetUnitFamily == UnitFamily.InfantryMANPADS || objectiveTargetUnitFamily == UnitFamily.Infantry ? Coordinates.CreateRandom(1, 5) : Coordinates.CreateRandom(5, 10)
                    } * Toolbox.NM_TO_METERS
                );
            if (targetDB.DCSUnitCategory == DCSUnitCategory.Vehicle)
                destinationPoint = ObjectiveUtils.GetNearestSpawnCoordinates(ref mission, destinationPoint, targetDB, false);


            var groupLua = targetBehaviorDB.GroupLua[(int)targetDB.DCSUnitCategory];
            if (targetBehaviorDB.Location == DBEntryObjectiveTargetBehaviorLocation.GoToPlayerAirbase)
            {
                destinationPoint = mission.PlayerAirbase.ParkingSpots.Length > 1 ? Toolbox.RandomFrom(mission.PlayerAirbase.ParkingSpots).Coordinates : mission.PlayerAirbase.Coordinates;
            }
            else if (targetBehaviorDB.Location == DBEntryObjectiveTargetBehaviorLocation.GoToAirbase)
            {
                var targetCoalition = GeneratorTools.GetSpawnPointCoalition(mission.TemplateRecord, taskDB.TargetSide, forceSide: true);
                var destinationAirbase = mission.AirbaseDB.Where(x => x.Coalition == targetCoalition.Value).OrderBy(x => destinationPoint.GetDistanceFrom(x.Coordinates)).First();
                destinationPoint = destinationAirbase.Coordinates;
                extraSettings.Add("EndAirbaseId", destinationAirbase.DCSID);
                mission.PopulatedAirbaseIds[targetCoalition.Value].Add(destinationAirbase.DCSID);
            }
            
        

            extraSettings.Add("playerCanDrive", false);

            var unitCoordinates = objectiveCoordinates;
            var objectiveName = mission.WaypointNameGenerator.GetWaypointName();
            var objectiveWaypoints = new List<Waypoint>();
                if (targetBehaviorDB.ID.StartsWith("RelocateToNewPosition"))
                {
                    Coordinates? spawnPoint = SpawnPointSelector.GetRandomSpawnPoint(
                    ref mission,
                    targetDB.ValidSpawnPoints,
                    objectiveCoordinates,
                    mission.TemplateRecord.FlightPlanObjectiveSeparation,
                    coalition: GeneratorTools.GetSpawnPointCoalition(mission.TemplateRecord, Side.Ally));
                    if (!spawnPoint.HasValue) // Failed to generate target group
                        throw new BriefingRoomException(mission.LangKey, "FailedToFindCargoSpawn");
                    unitCoordinates = spawnPoint.Value;
                }
                else
                {
                    var coords = targetBehaviorDB.Location == DBEntryObjectiveTargetBehaviorLocation.GoToPlayerAirbase ? mission.PlayerAirbase.Coordinates : unitCoordinates;
                    var (_, _, spawnPoints) = SpawnPointSelector.GetAirbaseAndParking(mission, coords, 1, GeneratorTools.GetSpawnPointCoalition(mission.TemplateRecord, Side.Ally, true).Value, (DBEntryAircraft)Database.Instance.GetEntry<DBEntryJSONUnit>("Mi-8MT"));
                    if (spawnPoints.Count == 0) // Failed to generate target group
                        throw new BriefingRoomException(mission.LangKey, "FailedToFindCargoSpawn");
                    unitCoordinates = spawnPoints.First();
                }
                if (targetBehaviorDB.ID.StartsWith("RecoverToBase") || (taskDB.IsEscort() & !targetBehaviorDB.ID.StartsWith("ToFrontLine")))
                {
                    (unitCoordinates, objectiveCoordinates) = (objectiveCoordinates, unitCoordinates);
                    isInverseTransportWayPoint = true;
                }
                var cargoWaypoint = ObjectiveUtils.GenerateObjectiveWaypoint(ref mission, task, unitCoordinates, unitCoordinates, $"{objectiveName} Pickup", scriptIgnore: true);
                mission.Waypoints.Add(cargoWaypoint);
                objectiveWaypoints.Add(cargoWaypoint);

                // Units shouldn't really move from pickup point if not escorted.
                groupLua = Database.Instance.GetEntry<DBEntryObjectiveTargetBehavior>("Idle").GroupLua[(int)targetDB.DCSUnitCategory];

            GroupInfo? targetGroupInfo = UnitGenerator.AddUnitGroup(
                ref mission,
                units,
                taskDB.TargetSide,
                objectiveTargetUnitFamily,
                groupLua, luaUnit,
                unitCoordinates,
                groupFlags,
                extraSettings);

            if (!targetGroupInfo.HasValue) // Failed to generate target group
                throw new BriefingRoomException(mission.LangKey, "FailedToGenerateGroupObjective");

            if (task.ProgressionActivation)
            {
                targetGroupInfo.Value.DCSGroups.ForEach((grp) =>
                {
                    grp.LateActivation = true;
                    grp.Visible = task.ProgressionOptions.Contains(ObjectiveProgressionOption.PreProgressionSpottable);
                });
            }

            if (targetDB.UnitCategory == UnitCategory.Infantry)
            {
                var pos = unitCoordinates.CreateNearRandom(new MinMaxD(5, 50));
                targetGroupInfo.Value.DCSGroup.Waypoints.First().Tasks.Add(new DCSWaypointTask("EmbarkToTransport", new Dictionary<string, object>{
                    {"x", pos.X},
                    { "y", pos.Y},
                    {"zoneRadius", Database.Instance.Common.DropOffDistanceMeters}
                    }, _auto: false));

            }

            var isStatic = objectiveTargetUnitFamily.GetUnitCategory() == UnitCategory.Cargo;
            ObjectiveUtils.AssignTargetSuffix(ref targetGroupInfo, objectiveName, isStatic);
            var luaExtraSettings = new Dictionary<string, object>();
            mission.Briefing.AddItem(DCSMissionBriefingItemType.TargetGroupName, $"-TGT-{objectiveName}");
            var length = isStatic ? targetGroupInfo.Value.DCSGroups.Count : targetGroupInfo.Value.UnitNames.Length;
            var pluralIndex = length == 1 ? 0 : 1;
            var taskString = GeneratorTools.ParseRandomString(taskDB.BriefingTask[pluralIndex].Get(mission.LangKey), mission).Replace("\"", "''");
            ObjectiveUtils.CreateTaskString(ref mission, pluralIndex, ref taskString, objectiveName, objectiveTargetUnitFamily, task, luaExtraSettings);
            ObjectiveUtils.CreateLua(ref mission, targetDB, taskDB, objectiveIndex, objectiveName, targetGroupInfo, taskString, task, luaExtraSettings);

            // Add briefing remarks for this objective task
            var remarksString = taskDB.BriefingRemarks.Get(mission.LangKey);
            if (!string.IsNullOrEmpty(remarksString))
            {
                string remark = Toolbox.RandomFrom(remarksString.Split(";"));
                GeneratorTools.ReplaceKey(ref remark, "ObjectiveName", objectiveName);
                GeneratorTools.ReplaceKey(ref remark, "DropOffDistanceMeters", Database.Instance.Common.DropOffDistanceMeters.ToString());
                GeneratorTools.ReplaceKey(ref remark, "UnitFamily", Database.Instance.Common.Names.UnitFamilies[(int)objectiveTargetUnitFamily].Get(mission.LangKey).Split(",")[pluralIndex]);
                mission.Briefing.AddItem(DCSMissionBriefingItemType.Remark, remark);
            }

            // Add feature ogg files
            foreach (string oggFile in taskDB.IncludeOgg)
                mission.AddMediaFile($"l10n/DEFAULT/{oggFile}", Path.Combine(BRPaths.INCLUDE_OGG, oggFile));


            // Add objective features Lua for this objective
            mission.AppendValue("ScriptObjectivesFeatures", ""); // Just in case there's no features
            var featureList = taskDB.RequiredFeatures.Concat(featuresID).ToHashSet();
            var playerHasPlanes = mission.TemplateRecord.PlayerFlightGroups.Any(x => Database.Instance.GetEntry<DBEntryJSONUnit>(x.Aircraft).Category == UnitCategory.Plane) || mission.TemplateRecord.AirbaseDynamicSpawn != DsAirbase.None;

            foreach (string featureID in featureList)
                FeaturesObjectives.GenerateMissionFeature(ref mission, featureID, objectiveName, objectiveIndex, targetGroupInfo.Value, taskDB.TargetSide, objectiveOptions, overrideCoords: targetBehaviorDB.ID.StartsWith("ToFrontLine") ? objectiveCoordinates : null);

            mission.ObjectiveCoordinates.Add(isInverseTransportWayPoint ? unitCoordinates : objectiveCoordinates);
            var objCoords = objectiveCoordinates;
            var furthestWaypoint = targetGroupInfo.Value.DCSGroup.Waypoints.Aggregate(objectiveCoordinates, (furthest, x) => objCoords.GetDistanceFrom(x.Coordinates) > objCoords.GetDistanceFrom(furthest) ? x.Coordinates : furthest);
            var waypoint = ObjectiveUtils.GenerateObjectiveWaypoint(ref mission, task, objectiveCoordinates, furthestWaypoint, objectiveName, targetGroupInfo.Value.DCSGroups.Select(x => x.GroupId).ToList(), hiddenMapMarker: task.ProgressionOptions.Contains(ObjectiveProgressionOption.ProgressionHiddenBrief));
            mission.Waypoints.Add(waypoint);
            objectiveWaypoints.Add(waypoint);
            mission.MapData.Add($"OBJECTIVE_AREA_{objectiveIndex}", new List<double[]> { waypoint.Coordinates.ToArray() });
            mission.ObjectiveTargetUnitFamilies.Add(objectiveTargetUnitFamily);
            if (!targetGroupInfo.Value.UnitDB.IsAircraft)
                mission.MapData.Add($"UNIT-{targetGroupInfo.Value.UnitDB.Families[0]}-{taskDB.TargetSide}-{targetGroupInfo.Value.GroupID}", new List<double[]> { targetGroupInfo.Value.Coordinates.ToArray() });
            return objectiveWaypoints;

        }
    }
}