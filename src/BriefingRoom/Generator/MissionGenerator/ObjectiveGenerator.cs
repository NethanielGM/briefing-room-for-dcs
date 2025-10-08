/*
==========================================================================
This file is part of Briefing Room for DCS World, a mission
generator for DCS World, by @akaAgar (https://github.com/akaAgar/briefing-room-for-dcs)

Briefing Room for DCS World is free software: you can redistribute it
and/or modify it under the terms of the GNU General Public License
as published by the Free Software Foundation, either version 3 of
the License, or (at your option) any later version.

Briefing Room for DCS World is distributed in the hope that it will
be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with Briefing Room for DCS World. If not, see https://www.gnu.org/licenses/
==========================================================================
*/

using BriefingRoom4DCS.Data;
using BriefingRoom4DCS.Generator.Mission.Objectives;
using BriefingRoom4DCS.Generator.UnitMaker;
using BriefingRoom4DCS.Mission;
using BriefingRoom4DCS.Template;
using System;
using System.Collections.Generic;
using System.Linq;


namespace BriefingRoom4DCS.Generator.Mission
{
    internal class ObjectiveGenerator
    {

        internal static Tuple<Coordinates, List<List<Waypoint>>> GenerateObjective(
            DCSMission mission,
            MissionTemplateObjectiveRecord task,
            Coordinates lastCoordinates,
            ref int objectiveIndex)
        {
            var waypointList = new List<List<Waypoint>>();
            var (featuresID, targetDB, targetBehaviorDB, taskDB, objectiveOptions) = GetObjectiveData(mission.LangKey, task);
            var useHintCoordinates = task.CoordinatesHint.ToString() != "0,0";
            lastCoordinates = useHintCoordinates ? task.CoordinatesHint : lastCoordinates;
            var objectiveCoordinates = GetSpawnCoordinates(ref mission, lastCoordinates, mission.PlayerAirbase, targetDB, useHintCoordinates);


            waypointList.Add(CreateObjective(
                task,
                taskDB,
                targetDB,
                targetBehaviorDB,
                ref objectiveIndex,
                ref objectiveCoordinates,
                objectiveOptions,
                ref mission,
                featuresID));

            var preValidSpawns = targetDB.ValidSpawnPoints.ToList();

            foreach (var subTasks in task.SubTasks)
            {
                objectiveIndex++;
                waypointList.Add(GenerateSubTask(
                    mission,
                    subTasks,
                    objectiveCoordinates,
                    preValidSpawns, targetBehaviorDB.Location,
                    featuresID, ref objectiveIndex));

            }
            return new(objectiveCoordinates, waypointList);
        }

        private static List<Waypoint> GenerateSubTask(
            DCSMission mission,
            MissionTemplateSubTaskRecord task,
            Coordinates coreCoordinates,
            List<SpawnPointType> preValidSpawns,
            DBEntryObjectiveTargetBehaviorLocation mainObjLocation,
            string[] featuresID,
            ref int objectiveIndex)
        {
            var (targetDB, targetBehaviorDB, taskDB, objectiveOptions, _) = ObjectiveUtils.GetCustomObjectiveData(mission.LangKey, task);

            preValidSpawns.AddRange(targetDB.ValidSpawnPoints);
            if (preValidSpawns.Contains(SpawnPointType.Sea) && preValidSpawns.Any(x => Constants.LAND_SPAWNS.Contains(x)))
                throw new BriefingRoomException(mission.LangKey, "LandSeaSubMix");
            if (Constants.AIRBASE_LOCATIONS.Contains(targetBehaviorDB.Location) && !Constants.AIRBASE_LOCATIONS.Contains(mainObjLocation))
                throw new BriefingRoomException(mission.LangKey, "AirbaseSubMix");
            var objectiveCoords = ObjectiveUtils.GetNearestSpawnCoordinates(ref mission, coreCoordinates, targetDB.ValidSpawnPoints);
            return CreateObjective(
                task,
                taskDB,
                targetDB,
                targetBehaviorDB,
                ref objectiveIndex,
                ref objectiveCoords,
                objectiveOptions,
                ref mission,
                featuresID);
        }

        private static Coordinates GetSpawnCoordinates(ref DCSMission mission, Coordinates lastCoordinates, DBEntryAirbase playerAirbase, DBEntryObjectiveTarget targetDB, bool usingHint)
        {
            Coordinates? spawnPoint = SpawnPointSelector.GetRandomSpawnPoint(
                ref mission,
                targetDB.ValidSpawnPoints,
                playerAirbase.Coordinates,
                usingHint ? Toolbox.ANY_RANGE : mission.TemplateRecord.FlightPlanObjectiveDistance,
                lastCoordinates,
                usingHint ? Toolbox.HINT_RANGE : mission.TemplateRecord.FlightPlanObjectiveSeparation,
                GeneratorTools.GetSpawnPointCoalition(mission.TemplateRecord, Side.Enemy));

            if (!spawnPoint.HasValue)
                throw new BriefingRoomException(mission.LangKey, "FailedToSpawnObjectiveGroup", String.Join(", ", targetDB.ValidSpawnPoints.Select(x => x.ToString()).ToList()));

            Coordinates objectiveCoordinates = spawnPoint.Value;
            return objectiveCoordinates;
        }

        internal static (string[] featuresID, DBEntryObjectiveTarget targetDB, DBEntryObjectiveTargetBehavior targetBehaviorDB, DBEntryObjectiveTask taskDB, ObjectiveOption[] objectiveOptions) GetObjectiveData(string langKey, MissionTemplateObjectiveRecord objectiveTemplate)
        {
            var (targetDB, targetBehaviorDB, taskDB, objectiveOptions, presetDB) = ObjectiveUtils.GetCustomObjectiveData(langKey, objectiveTemplate);
            var featuresID = (objectiveTemplate.HasPreset ? presetDB.Features.Concat(objectiveTemplate.Features.ToArray()) : objectiveTemplate.Features).Distinct().ToArray();

            ObjectiveUtils.ObjectiveNullCheck(langKey, targetDB, targetBehaviorDB, taskDB);
            return (featuresID, targetDB, targetBehaviorDB, taskDB, objectiveOptions);
        }

        private static List<Waypoint> CreateObjective(
            MissionTemplateSubTaskRecord task,
            DBEntryObjectiveTask taskDB,
            DBEntryObjectiveTarget targetDB,
            DBEntryObjectiveTargetBehavior targetBehaviorDB,
            ref int objectiveIndex,
            ref Coordinates objectiveCoords,
            ObjectiveOption[] objectiveOptions,
            ref DCSMission mission,
            string[] featuresID
        )
        {
            return taskDB.ID switch
            {
                "Escort" => Escort.CreateObjective(task, taskDB, targetDB, targetBehaviorDB, ref objectiveIndex, ref objectiveCoords, objectiveOptions, ref mission, featuresID),
                "Hold" or "HoldSuperiority" => Hold.CreateObjective(task, taskDB, targetDB, targetBehaviorDB, ref objectiveIndex, ref objectiveCoords, objectiveOptions, ref mission, featuresID),
                "TransportTroops" or "TransportCargo" or "ExtractTroops" => Transport.CreateObjective(task, taskDB, targetDB, targetBehaviorDB, ref objectiveIndex, ref objectiveCoords, objectiveOptions, ref mission, featuresID),
                _ => Basic.CreateObjective(task, taskDB, targetDB, targetBehaviorDB, ref objectiveIndex, ref objectiveCoords, objectiveOptions, ref mission, featuresID)
            };
        }
    }
}
