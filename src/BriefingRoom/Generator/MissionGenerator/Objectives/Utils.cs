using BriefingRoom4DCS.Data;

using BriefingRoom4DCS.Generator.UnitMaker;
using BriefingRoom4DCS.Mission;
using BriefingRoom4DCS.Template;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BriefingRoom4DCS.Generator.Mission.Objectives
{
    internal class ObjectiveUtils
    {
        internal static (string luaUnit, int unitCount, MinMaxI unitCountMinMax, List<UnitFamily> objectiveTargetUnitFamily, GroupFlags groupFlags) GetUnitData(MissionTemplateSubTaskRecord task, DBEntryObjectiveTarget targetDB, DBEntryObjectiveTargetBehavior targetBehaviorDB, ObjectiveOption[] objectiveOptions)
        {
            GroupFlags groupFlags = 0;
            if (objectiveOptions.Contains(ObjectiveOption.Invisible)) groupFlags |= GroupFlags.Invisible;
            if (objectiveOptions.Contains(ObjectiveOption.ShowTarget)) groupFlags = GroupFlags.NeverHidden;
            else if (objectiveOptions.Contains(ObjectiveOption.HideTarget)) groupFlags = GroupFlags.AlwaysHidden;
            if (objectiveOptions.Contains(ObjectiveOption.EmbeddedAirDefense)) groupFlags |= GroupFlags.EmbeddedAirDefense;
            List<UnitFamily> unitFamilies;
            switch (true)
            {
                case true when targetDB.ID == "VehicleAny":
                    unitFamilies = Toolbox.RandomFrom(Constants.MIXED_VEHICLE_SETS);
                    break;
                case true when targetDB.ID == "InfantryAny":
                    unitFamilies = Toolbox.RandomFrom(Constants.MIXED_INFANTRY_SETS);
                    break;
                case true when targetDB.ID == "GroundAny":
                    unitFamilies = Toolbox.RandomFrom(Constants.MIXED_INFANTRY_SETS).Concat(Toolbox.RandomFrom(Constants.MIXED_VEHICLE_SETS)).ToList();
                    break;
                default:
                    unitFamilies = [Toolbox.RandomFrom(targetDB.UnitFamilies)];
                    break;
            }

            return (targetBehaviorDB.UnitLua[(int)targetDB.DCSUnitCategory],
                targetDB.UnitCount[(int)task.TargetCount].GetValue(),
                targetDB.UnitCount[(int)task.TargetCount],
                unitFamilies,
                groupFlags
            );
        }

        internal static Coordinates PlaceInAirbase(ref DCSMission mission, Dictionary<string, object> extraSettings, DBEntryObjectiveTargetBehavior targetBehaviorDB, Coordinates objectiveCoordinates, int unitCount, DBEntryJSONUnit unitDB)
        {
            int airbaseID = 0;
            var parkingSpotIDsList = new List<int>();
            var parkingSpotCoordinatesList = new List<Coordinates>();
            var enemyCoalition = mission.TemplateRecord.ContextPlayerCoalition.GetEnemy();
            var playerAirbaseDCSID = mission.PlayerAirbase.DCSID;
            var spawnAnywhere = mission.TemplateRecord.SpawnAnywhere;
            var targetAirbaseOptions =
                (from DBEntryAirbase airbaseDB in mission.AirbaseDB
                 where airbaseDB.DCSID != playerAirbaseDCSID && (spawnAnywhere || airbaseDB.Coalition == enemyCoalition)
                 select airbaseDB).OrderBy(x => x.Coordinates.GetDistanceFrom(objectiveCoordinates));

            BriefingRoomException exception = null;
            foreach (var targetAirbase in targetAirbaseOptions)
            {
                try
                {
                    airbaseID = targetAirbase.DCSID;
                    var parkingSpots = SpawnPointSelector.GetFreeParkingSpots(
                        ref mission,
                        targetAirbase.DCSID,
                        unitCount, (DBEntryAircraft)unitDB,
                        targetBehaviorDB.Location == DBEntryObjectiveTargetBehaviorLocation.AirbaseParkingNoHardenedShelter);

                    parkingSpotIDsList = parkingSpots.Select(x => x.DCSID).ToList();
                    parkingSpotCoordinatesList = parkingSpots.Select(x => x.Coordinates).ToList();

                    extraSettings.Add("GroupAirbaseID", airbaseID);
                    extraSettings.Add("ParkingID", parkingSpotIDsList);
                    extraSettings.Add("UnitCoords", parkingSpotCoordinatesList);
                    return Toolbox.RandomFrom(parkingSpotCoordinatesList);
                }
                catch (BriefingRoomException e)
                {
                    exception = e;
                    throw;
                }
            }
            throw exception;
        }

        internal static void AddEmbeddedAirDefenseUnits(ref DCSMission mission, DBEntryObjectiveTarget targetDB, DBEntryObjectiveTargetBehavior targetBehaviorDB, DBEntryObjectiveTask taskDB, Coordinates objectiveCoordinates, GroupFlags groupFlags, Dictionary<string, object> extraSettings)
        {
            // Static targets (aka buildings) need to have their "embedded" air defenses spawned in another group
            var airDefenseUnits = GeneratorTools.GetEmbeddedAirDefenseUnits(mission.LangKey, mission.TemplateRecord, taskDB.TargetSide, UnitCategory.Static);

            if (airDefenseUnits.Count > 0)
                UnitGenerator.AddUnitGroup(
                    ref mission,
                    airDefenseUnits,
                    taskDB.TargetSide, UnitFamily.VehicleAAA,
                    targetBehaviorDB.GroupLua[(int)targetDB.DCSUnitCategory], targetBehaviorDB.UnitLua[(int)targetDB.DCSUnitCategory],
                    objectiveCoordinates + Coordinates.CreateRandom(100, 500),
                    groupFlags,
                    extraSettings);
        }

        internal static void CreateLua(ref DCSMission mission, DBEntryObjectiveTarget targetDB, DBEntryObjectiveTask taskDB, int objectiveIndex, string objectiveName, GroupInfo? targetGroupInfo, string taskString, MissionTemplateSubTaskRecord task, Dictionary<string, object> extraSettings)
        {
            // Add Lua table for this objective
            string objectiveLua = $"briefingRoom.mission.objectives[{objectiveIndex + 1}] = {{ ";
            objectiveLua += $"complete = false, ";
            objectiveLua += $"failed = false, ";
            objectiveLua += $"groupName = \"{targetGroupInfo.Value.Name}\", ";
            objectiveLua += $"hideTargetCount = false, ";
            objectiveLua += $"name = \"{objectiveName}\", ";
            objectiveLua += $"targetCategory = Unit.Category.{targetDB.UnitCategory.ToLuaName()}, ";
            objectiveLua += $"taskType = \"{taskDB.ID}\", ";
            objectiveLua += $"task = \"{taskString}\", ";
            objectiveLua += $"unitsCount = #dcsExtensions.getUnitNamesByGroupNameSuffix(\"-TGT-{objectiveName}\"), ";
            objectiveLua += $"unitNames = dcsExtensions.getUnitNamesByGroupNameSuffix(\"-TGT-{objectiveName}\"), ";
            objectiveLua += $"progressionHidden = {(task.ProgressionActivation ? "true" : "false")},";
            objectiveLua += $"progressionHiddenBrief = {(task.ProgressionOptions.Contains(ObjectiveProgressionOption.ProgressionHiddenBrief) ? "true" : "false")},";
            objectiveLua += $"progressionCondition = \"{(!string.IsNullOrEmpty(task.ProgressionOverrideCondition) ? task.ProgressionOverrideCondition : string.Join(task.ProgressionDependentIsAny ? " or " : " and ", task.ProgressionDependentTasks.Select(x => x + 1).ToList()))}\", ";
            objectiveLua += $"startMinutes = {(task.ProgressionActivation ? "-1" : "0")},";
            objectiveLua += $"f10MenuText = \"$LANG_OBJECTIVE$ {objectiveName}\",";
            objectiveLua += $"f10Commands = {{}}";
            // StartActive flag generated from template when provided
            var startActive = (extraSettings.ContainsKey("StartActive") && (bool)extraSettings["StartActive"]) || !task.ProgressionActivation;
            objectiveLua += $", startActive = {(startActive ? "true" : "false")}";
            objectiveLua += "}\n";

            // Add F10 sub-menu for this objective
            mission.AppendValue("ScriptObjectives", objectiveLua);

            // Add objective trigger Lua for this objective
            foreach (var CompletionTriggerLua in taskDB.CompletionTriggersLua)
            {
                string triggerLua = Toolbox.ReadAllTextIfFileExists(Path.Combine(BRPaths.INCLUDE_LUA_OBJECTIVETRIGGERS, CompletionTriggerLua));
                GeneratorTools.ReplaceKey(ref triggerLua, "ObjectiveIndex", objectiveIndex + 1);
                foreach (KeyValuePair<string, object> extraSetting in extraSettings)
                    if (extraSetting.Value is not Array)
                        GeneratorTools.ReplaceKey(ref triggerLua, extraSetting.Key, extraSetting.Value);
                mission.AppendValue("ScriptObjectivesTriggers", triggerLua);
            }
        }

        internal static void CreateTaskString(ref DCSMission mission, int pluralIndex, ref string taskString, string objectiveName, UnitFamily objectiveTargetUnitFamily, MissionTemplateSubTaskRecord task, Dictionary<string, object> extraSettings)
        {
            // Get tasking string for the briefing
            if (string.IsNullOrEmpty(taskString)) taskString = "Complete objective $OBJECTIVENAME$";
            GeneratorTools.ReplaceKey(ref taskString, "ObjectiveName", objectiveName);
            GeneratorTools.ReplaceKey(ref taskString, "UnitFamily", Database.Instance.Common.Names.UnitFamilies[(int)objectiveTargetUnitFamily].Get(mission.LangKey).Split(",")[pluralIndex]);
            foreach (KeyValuePair<string, object> extraSetting in extraSettings)
                if (extraSetting.Value is not Array)
                    GeneratorTools.ReplaceKey(ref taskString, extraSetting.Key, extraSetting.Value);
            if (!task.ProgressionOptions.Contains(ObjectiveProgressionOption.ProgressionHiddenBrief))
                mission.Briefing.AddItem(DCSMissionBriefingItemType.Task, taskString);
        }

        internal static Waypoint GenerateObjectiveWaypoint(ref DCSMission mission, MissionTemplateSubTaskRecord objectiveTemplate, Coordinates objectiveCoordinates, Coordinates ObjectiveDestinationCoordinates, string objectiveName, List<int> groupIds = null, bool scriptIgnore = false, bool hiddenMapMarker = false)
        {
            var (targetDB, targetBehaviorDB, taskDB, objectiveOptions, presetDB) = GetCustomObjectiveData(mission.LangKey, objectiveTemplate);
            var targetBehaviorLocation = targetBehaviorDB.Location;
            if (targetDB == null) throw new BriefingRoomException(mission.LangKey, "TargetNotFound", targetDB.UIDisplayName);

            Coordinates waypointCoordinates = objectiveCoordinates;
            bool onGround = !targetDB.UnitCategory.IsAircraft() || Constants.AIR_ON_GROUND_LOCATIONS.Contains(targetBehaviorLocation); // Ground targets = waypoint on the ground

            if (objectiveOptions.Contains(ObjectiveOption.InaccurateWaypoint) && (!taskDB.UICategory.ContainsValue("Transport") || objectiveName.EndsWith("Pickup")))
            {
                waypointCoordinates += Coordinates.CreateRandom(3.0, 6.0) * Toolbox.NM_TO_METERS;
                if (mission.TemplateRecord.OptionsMission.Contains("MarkWaypoints"))
                    DrawingMaker.AddDrawing(ref mission, $"Target Zone {objectiveName}", DrawingType.Circle, waypointCoordinates, "Radius".ToKeyValuePair(6.0 * Toolbox.NM_TO_METERS));
            }
            else if (taskDB.UICategory.ContainsValue("Transport"))
            {
                var dist = Database.Instance.Common.DropOffDistanceMeters;
                if (taskDB.IsEscort() & !onGround)
                    dist = dist * 10;
                DrawingMaker.AddDrawing(ref mission, $"Target Zone {objectiveName}", DrawingType.Circle, waypointCoordinates, "Radius".ToKeyValuePair(dist));
            }
            else if (targetBehaviorLocation == DBEntryObjectiveTargetBehaviorLocation.Patrolling)
                DrawingMaker.AddDrawing(ref mission, $"Target Zone {objectiveName}", DrawingType.Circle, waypointCoordinates, "Radius".ToKeyValuePair(ObjectiveDestinationCoordinates.GetDistanceFrom(objectiveCoordinates)));
            return new Waypoint(objectiveName, waypointCoordinates, onGround, groupIds, scriptIgnore, objectiveTemplate.Options.Contains(ObjectiveOption.NoAircraftWaypoint), hiddenMapMarker);
        }

        internal static Coordinates GetNearestSpawnCoordinates(ref DCSMission mission, Coordinates coreCoordinates, SpawnPointType[] validSpawnPoints, bool remove = true)
        {
            Coordinates? spawnPoint = SpawnPointSelector.GetNearestSpawnPoint(
                mission,
                validSpawnPoints,
                coreCoordinates, remove);

            if (!spawnPoint.HasValue)
                throw new BriefingRoomException(mission.LangKey, "FailedToLaunchNearbyObjective", String.Join(",", validSpawnPoints.Select(x => x.ToString()).ToList()));

            Coordinates objectiveCoordinates = spawnPoint.Value;
            return objectiveCoordinates;
        }

        internal static void AssignTargetSuffix(ref GroupInfo? targetGroupInfo, string objectiveName, Boolean isStatic)
        {
            var i = 0;
            targetGroupInfo.Value.DCSGroups.ForEach(x =>
            {
                x.Name += $"{(i == 0 ? "" : i)}-TGT-{objectiveName}";
                if (isStatic) x.Units.ForEach(u => u.Name += $"{(i == 0 ? "" : i)}-TGT-{objectiveName}");
                i++;
            });
        }

        internal static (DBEntryObjectiveTarget targetDB, DBEntryObjectiveTargetBehavior targetBehaviorDB, DBEntryObjectiveTask taskDB, ObjectiveOption[] objectiveOptions, DBEntryObjectivePreset presetDB) GetCustomObjectiveData(string langKey, MissionTemplateSubTaskRecord objectiveTemplate)
        {
            var targetDB = Database.Instance.GetEntry<DBEntryObjectiveTarget>(objectiveTemplate.Target);
            var targetBehaviorDB = Database.Instance.GetEntry<DBEntryObjectiveTargetBehavior>(objectiveTemplate.TargetBehavior);
            var taskDB = Database.Instance.GetEntry<DBEntryObjectiveTask>(objectiveTemplate.Task);
            var objectiveOptions = objectiveTemplate.Options.ToArray();
            DBEntryObjectivePreset presetDB = null;

            if (objectiveTemplate.HasPreset)
            {
                presetDB = Database.Instance.GetEntry<DBEntryObjectivePreset>(objectiveTemplate.Preset);
                if (presetDB != null)
                {
                    targetDB = Database.Instance.GetEntry<DBEntryObjectiveTarget>(Toolbox.RandomFrom(presetDB.Targets));
                    targetBehaviorDB = Database.Instance.GetEntry<DBEntryObjectiveTargetBehavior>(Toolbox.RandomFrom(presetDB.TargetsBehaviors));
                    taskDB = Database.Instance.GetEntry<DBEntryObjectiveTask>(presetDB.Task);
                    objectiveOptions = presetDB.Options.Concat(objectiveTemplate.Options).Distinct().ToArray();
                }
            }

            ObjectiveNullCheck(langKey, targetDB, targetBehaviorDB, taskDB);
            return (targetDB, targetBehaviorDB, taskDB, objectiveOptions, presetDB);
        }

        internal static void ObjectiveNullCheck(string langKey, DBEntryObjectiveTarget targetDB, DBEntryObjectiveTargetBehavior targetBehaviorDB, DBEntryObjectiveTask taskDB)
        {
            if (targetDB == null) throw new BriefingRoomException(langKey, "TargetNotFound", targetDB.UIDisplayName);
            if (targetBehaviorDB == null) throw new BriefingRoomException(langKey, "BehaviorNotFound", targetBehaviorDB.UIDisplayName);
            if (taskDB == null) throw new BriefingRoomException(langKey, "TaskNotFound", taskDB.UIDisplayName);
            if (!taskDB.ValidUnitCategories.Contains(targetDB.UnitCategory))
                throw new BriefingRoomException(langKey, "TaskTargetsInvalid", taskDB.UIDisplayName, targetDB.UnitCategory);
        }

        internal static Tuple<int, Coordinates> GetTransportOrigin(
            ref DCSMission mission,
            DBEntryObjectiveTargetBehaviorLocation Location,
            Coordinates objectiveCoordinates,
            bool isEscort = false,
            UnitCategory? unitCategory = null)
        {
            switch (Location)
            {
                case DBEntryObjectiveTargetBehaviorLocation.PlayerAirbase:
                    return GetAirbaseCargoSpot(ref mission, mission.PlayerAirbase.Coordinates);
                case DBEntryObjectiveTargetBehaviorLocation.Airbase:
                    return GetAirbaseCargoSpot(ref mission, objectiveCoordinates, mission.PlayerAirbase.DCSID);
                case DBEntryObjectiveTargetBehaviorLocation.NearAirbase:
                    var (airbaseId, coords) = GetAirbaseCargoSpot(ref mission, objectiveCoordinates);
                    coords = GetNearestSpawnCoordinates(ref mission, coords, GetSpawnPointTypes(isEscort, unitCategory), true);
                    return new Tuple<int, Coordinates>(airbaseId, coords);
                default:
                    return new Tuple<int, Coordinates>(-1, objectiveCoordinates);
            }
        }

        // Likely will extend this in future for Escort setup
        internal static Tuple<int, Coordinates> GetTransportDestination(
            ref DCSMission mission,
            DBEntryObjectiveTargetBehaviorLocation destination,
            Coordinates originCoordinates,
            MinMaxD transportDistance,
            int originAirbaseId,
            bool isEscort = false,
            UnitCategory? unitCategory = null,
            bool enemyAllowed = false
            )
        {
            Coordinates coordinates;
            if (transportDistance.Max == 0)
                if (isEscort)
                    coordinates = originCoordinates.CreateRandomNM(GetStandardMoveDistanceRange(unitCategory.HasValue ? unitCategory.Value : UnitCategory.Plane)); // Escort default distance
                else
                    coordinates = originCoordinates.CreateRandomNM(10, 50); // Default transport distance if none set
            else
                coordinates = originCoordinates.CreateRandomNM(transportDistance); // Consider biasing this towards player base or objective in future
            switch (destination)
            {
                case DBEntryObjectiveTargetBehaviorLocation.PlayerAirbase:
                    return GetAirbaseCargoSpot(ref mission, mission.PlayerAirbase.Coordinates);
                case DBEntryObjectiveTargetBehaviorLocation.Airbase:
                    return GetAirbaseCargoSpot(ref mission, coordinates, originAirbaseId, enemyAllowed);
                default:
                    var spawnTypes = GetSpawnPointTypes(isEscort, unitCategory);
                    coordinates = GetNearestSpawnCoordinates(ref mission, coordinates, spawnTypes, false);

                    return new Tuple<int, Coordinates>(-1, coordinates);
            }
        }

        internal static SpawnPointType[] GetSpawnPointTypes(bool isEscort = false, UnitCategory? unitCategory = null)
        {
            var spawnTypes = new[] { SpawnPointType.LandLarge, SpawnPointType.LandMedium };
            if (isEscort && unitCategory.HasValue)
            {
                if (unitCategory.Value.IsAircraft())
                    spawnTypes = new[] { SpawnPointType.Air };
                else if (unitCategory.Value == UnitCategory.Ship)
                    spawnTypes = new[] { SpawnPointType.Sea };
            }
            return spawnTypes;
        }

        internal static MinMaxD GetStandardMoveDistanceRange(UnitCategory unitCategory)
        {
            return unitCategory switch
            {
                UnitCategory.Plane => new MinMaxD(30, 60),
                UnitCategory.Ship => new MinMaxD(15, 40),
                UnitCategory.Helicopter => new MinMaxD(10, 20),
                UnitCategory.Infantry => new MinMaxD(1, 5),
                _ => new MinMaxD(5, 10)
            };
        }

        internal static Tuple<int, Coordinates> GetAirbaseCargoSpot(ref DCSMission mission, Coordinates coords, int ignoreAirbaseId = -1, bool enemyAllowed = false)
        {
            var side = enemyAllowed ? Toolbox.RandomFrom(new[] { Side.Enemy,  Side.Ally }) : Side.Ally;
            var targetCoalition = GeneratorTools.GetSpawnPointCoalition(mission.TemplateRecord, side, true);
            var (airbaseDB, _, spawnPoints) = SpawnPointSelector.GetAirbaseAndParking(mission, coords, 1, targetCoalition.Value, (DBEntryAircraft)Database.Instance.GetEntry<DBEntryJSONUnit>("Mi-8MT"), new[] { ignoreAirbaseId });
            if (spawnPoints.Count == 0) // Failed to generate target group
                throw new BriefingRoomException(mission.LangKey, "FailedToFindCargoSpawn");
            var airbaseCoords = Toolbox.RandomFrom(spawnPoints);
            mission.PopulatedAirbaseIds[targetCoalition.Value].Add(airbaseDB.DCSID);
            return new Tuple<int, Coordinates>(airbaseDB.DCSID, airbaseCoords);
        }


    }
}