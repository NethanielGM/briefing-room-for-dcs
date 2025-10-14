﻿using BriefingRoom4DCS.Data;
using BriefingRoom4DCS.Mission;
using BriefingRoom4DCS.Mission.DCSLuaObjects;
using BriefingRoom4DCS.Template;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BriefingRoom4DCS.Generator.UnitMaker
{
    internal readonly struct GroupInfo
    {
        internal Coordinates Coordinates { get { return new Coordinates(DCSGroup.X, DCSGroup.Y); } }
        internal int GroupID { get { return DCSGroup.GroupId; } }
        internal string Name { get { return DCSGroup.Name; } }
        internal string[] UnitNames { get { return DCSGroup.Units.Select(x => x.Name).ToArray(); } }

        internal double Frequency { get { return DCSGroup.Frequency; } }

        internal DBEntryJSONUnit UnitDB { get; }

        internal DCSGroup DCSGroup { get { return DCSGroups.First(); } }
        internal List<DCSGroup> DCSGroups { get; }

        internal GroupInfo(ref DCSGroup dCSGroup, DBEntryJSONUnit unitDB = null)
        {
            UnitDB = unitDB;
            DCSGroups = new List<DCSGroup> { dCSGroup };
        }

        internal GroupInfo(ref List<DCSGroup> dCSGroups, DBEntryJSONUnit unitDB = null)
        {
            UnitDB = unitDB;
            DCSGroups = dCSGroups;
        }
    }

    internal static class UnitGenerator
    {
        private const double AIRCRAFT_UNIT_SPACING = 50.0;
        private const double SHIP_UNIT_SPACING = 100.0;
        private const double STATIC_UNIT_SPACING = 30.0;
        private const double VEHICLE_UNIT_SPACING = 10.0;
        private readonly static List<string> IGNORE_PROPS = new() { "Skill", "Pylons" };

        internal static Tuple<List<string>, List<DBEntryJSONUnit>> GetUnits(
            ref DCSMission mission,
            List<UnitFamily> families,
            int unitCount, Side side,
            GroupFlags GroupFlags,
            ref Dictionary<string, object> extraSettings,
            bool allowStatic,
            bool forceTryTemplate = false,
            bool allowDefaults = true)
        {
            if (unitCount <= 0) throw new BriefingRoomException("en", "Asking for a zero units");
            if (families.Count <= 0) throw new BriefingRoomException("en", "No Unit Families Provided");
            var coalitionForSide = (side == Side.Ally) ? mission.TemplateRecord.ContextPlayerCoalition : mission.TemplateRecord.ContextPlayerCoalition.GetEnemy();
            DBEntryCoalition unitsCoalitionDB = mission.CoalitionsDB[(int)coalitionForSide];
            List<string> units = new();
            Country country = Country.ALL;
            var combinedBanList = mission.TemplateRecord.OptionsUnitBanList
                .Concat(coalitionForSide == Coalition.Blue ? mission.TemplateRecord.OptionsUnitBanListBlue : mission.TemplateRecord.OptionsUnitBanListRed)
                .Distinct().ToList();

            if (side == Side.Neutral)
            {
                var ignoreCountries = mission.CoalitionsDB.SelectMany(x => x.Countries).Where(x => x != Country.ALL).ToList();
                var combinedBanListNeutral = mission.TemplateRecord.OptionsUnitBanList;
                (country, units) = GeneratorTools.GetNeutralRandomUnits(mission.LangKey, families, ignoreCountries, mission.TemplateRecord.ContextDecade, unitCount, mission.TemplateRecord.Mods, mission.TemplateRecord.OptionsMission.Contains("AllowLowPoly"), combinedBanListNeutral);
                if (!units.Where(x => x != null).Any()) return new(new List<string>(), new List<DBEntryJSONUnit>());
            }
            else if (forceTryTemplate || families.All(x => Constants.TEMPLATE_ALWAYS_FAMILIES.Contains(x)) || (families.All(x => Constants.TEMPLATE_PREFERENCE_FAMILIES.Contains(x)) && Toolbox.RandomChance(3)))
            {
                var response = unitsCoalitionDB.GetRandomTemplate(families, mission.TemplateRecord.ContextDecade, mission.TemplateRecord.Mods, combinedBanList);
                if (response != null)
                {
                    (country, var unitTemplate) = response;
                    extraSettings["TemplatePositionMap"] = unitTemplate.Units;
                    units = unitTemplate.Units.Select(x => x.DCSID).ToList();
                }
            }
            if (!units.Where(x => x != null).Any())
            {
                (country, units) = unitsCoalitionDB.GetRandomUnits(
                    mission.LangKey,
                    families,
                    mission.TemplateRecord.ContextDecade,
                    unitCount, mission.TemplateRecord.Mods,
                    combinedBanList,
                    mission.TemplateRecord.OptionsMission.Contains("AllowLowPoly"),
                    mission.TemplateRecord.OptionsMission.Contains("BlockSuppliers"),
                    lowUnitVariation: GroupFlags.HasFlag(GroupFlags.LowUnitVariation),
                    allowStatic: allowStatic,
                    allowDefaults: allowDefaults);
                units = Toolbox.ShuffleList(units.ToList());
            }


            if (country != Country.ALL && unitsCoalitionDB.Countries.Contains(country))
                extraSettings["Country"] = country;

            if (GroupFlags.HasFlag(GroupFlags.EmbeddedAirDefense))
            {
                var airDefenseUnits = GeneratorTools.GetEmbeddedAirDefenseUnits(mission.LangKey, mission.TemplateRecord, side, families.First().GetUnitCategory(), country != Country.ALL ? country : null);
                if (airDefenseUnits.Count > 0)
                    units.AddRange(airDefenseUnits);
            }
            return new(units, Database.Instance.GetEntries<DBEntryJSONUnit>(units));
        }

        internal static Tuple<List<string>, List<DBEntryJSONUnit>> GetUnitsForTemplateLocation(
            ref DCSMission mission,
            DBEntryTheaterTemplateLocation templateLocation,
            Side side,
            List<UnitFamily> families,
            ref Dictionary<string, object> extraSettings
        )
        {
            Boolean tryUseAll = false;
            var coalitionForSide = (side == Side.Ally) ? mission.TemplateRecord.ContextPlayerCoalition : mission.TemplateRecord.ContextPlayerCoalition.GetEnemy();
            DBEntryCoalition unitsCoalitionDB = mission.CoalitionsDB[(int)coalitionForSide];
            Country country = Country.ALL;
            var combinedBanList = mission.TemplateRecord.OptionsUnitBanList
                .Concat(coalitionForSide == Coalition.Blue ? mission.TemplateRecord.OptionsUnitBanListBlue : mission.TemplateRecord.OptionsUnitBanListRed)
                .Distinct().ToList();
            var unitMap = templateLocation.GetRequiredFamilyMap();
            if (templateLocation.LocationType == TheaterTemplateLocationType.SAM)
            {
                //Always pull from a template to get the right units for SAM Sites
                var response = unitsCoalitionDB.GetRandomTemplate(families, mission.TemplateRecord.ContextDecade, mission.TemplateRecord.Mods, combinedBanList);
                if (response != null)
                {
                    (country, var unitTemplate) = response;
                    foreach (var unit in unitTemplate.Units)
                    {
                        var unitDB = Database.Instance.GetEntry<DBEntryJSONUnit>(unit.DCSID);
                        if (unitDB == null)
                        {
                            BriefingRoom.PrintTranslatableWarning(mission.LangKey, "UnitNotFoundInTemplate", unit.DCSID);
                            continue;
                        }
                        var matchingFamilies = unitDB.Families.Intersect(unitMap.Keys).ToList();
                        if (matchingFamilies.Count > 0)
                        {
                            unitMap[matchingFamilies.First()].Add(unit.DCSID);
                        }
                    }
                    tryUseAll = true;
                }
            }

            foreach (var unitFamily in unitMap.Keys)
            {
                if (unitMap[unitFamily].Count > 0) continue;
                var (newCountry, unitSet) = unitsCoalitionDB.GetRandomUnits(
                    mission.LangKey,
                    new List<UnitFamily> { unitFamily },
                    mission.TemplateRecord.ContextDecade,
                    1, mission.TemplateRecord.Mods,
                    combinedBanList,
                    mission.TemplateRecord.OptionsMission.Contains("AllowLowPoly"),
                    mission.TemplateRecord.OptionsMission.Contains("BlockSuppliers"),
                    requiredCountry: country != Country.ALL ? country : null,
                    lowUnitVariation: false,
                    allowStatic: true,
                    allowDefaults: true);
                unitMap[unitFamily] = unitSet;
                if (country == Country.ALL && newCountry != Country.ALL)
                    country = newCountry;
            }


            var (units, convertedUnitTemplate) = templateLocation.CreateTemplatePositionMap(unitMap, tryUseAll);
            extraSettings["TemplatePositionMap"] = convertedUnitTemplate;
            if (country != Country.ALL && unitsCoalitionDB.Countries.Contains(country))
                extraSettings["Country"] = country;
            return new(units, Database.Instance.GetEntries<DBEntryJSONUnit>(units));
        }

        internal static GroupInfo? AddUnitGroupTemplate(
            ref DCSMission mission,
            DBEntryTemplate unitTemplate,
            Side side,
            string groupTypeLua,
            string unitTypeLua,
            Coordinates coordinates,
            GroupFlags GroupFlags,
            Dictionary<string, object> extraSettings)
        {
            extraSettings["TemplatePositionMap"] = unitTemplate.Units;
            return AddUnitGroup(ref mission, unitTemplate.Units.Select(x => x.DCSID).ToList(), side, unitTemplate.Family, groupTypeLua, unitTypeLua, coordinates, GroupFlags, extraSettings);
        }

        internal static GroupInfo? AddUnitGroup(
            ref DCSMission mission,
            UnitFamily family, int unitCount, Side side,
            string groupLua, string unitLua,
            Coordinates coordinates,
            GroupFlags GroupFlags,
            Dictionary<string, object> extraSettings,
            bool allowStatic,
            bool forceTryTemplate = false) => AddUnitGroup(ref mission, new List<UnitFamily> { family }, unitCount, side, groupLua, unitLua, coordinates, GroupFlags, extraSettings, allowStatic, forceTryTemplate);

        internal static GroupInfo? AddUnitGroup(
            ref DCSMission mission,
            List<UnitFamily> families, int unitCount, Side side,
            string groupLua, string unitLua,
            Coordinates coordinates,
            GroupFlags GroupFlags,
            Dictionary<string, object> extraSettings,
            bool allowStatic,
            bool forceTryTemplate = false)
        {
            if (unitCount <= 0) throw new BriefingRoomException("en", "Asking for a zero units");
            if (families.Count <= 0) throw new BriefingRoomException("en", "No Unit Families Provided");
            var (units, _) = GetUnits(ref mission, families, unitCount, side, GroupFlags, ref extraSettings, allowStatic, forceTryTemplate: forceTryTemplate);
            return AddUnitGroup(ref mission, units, side, families.First(), groupLua, unitLua, coordinates, GroupFlags, extraSettings);
        }

        internal static GroupInfo? AddUnitGroup(
            ref DCSMission mission,
            string unit, Side side, UnitFamily unitFamily,
            string groupTypeLua, string unitTypeLua,
            Coordinates coordinates,
            GroupFlags GroupFlags,
            Dictionary<string, object> extraSettings) => AddUnitGroup(ref mission, new List<string> { unit }, side, unitFamily, groupTypeLua, unitTypeLua, coordinates, GroupFlags, extraSettings);

        internal static GroupInfo? AddUnitGroup(
            ref DCSMission mission,
            List<string> units, Side side, UnitFamily unitFamily,
            string groupTypeLua, string unitTypeLua,
            Coordinates coordinates,
            GroupFlags GroupFlags,
            Dictionary<string, object> extraSettings)
        {
            if (units.Count == 0) return null;

            var coalition = (side == Side.Ally) ? mission.TemplateRecord.ContextPlayerCoalition : (side == Side.Neutral ? Coalition.Neutral : mission.TemplateRecord.ContextPlayerCoalition.GetEnemy());
            var defaultCountry = coalition switch
            {
                Coalition.Blue => Country.CombinedJointTaskForcesBlue,
                Coalition.Red => Country.CombinedJointTaskForcesRed,
                _ => Toolbox.RandomFrom(mission.CoalitionsCountries[(int)Coalition.Neutral]),
            };
            var country = (Country)extraSettings.GetValueOrDefault("Country", defaultCountry);
            var isUsingSkynet = mission.TemplateRecord.MissionFeatures.Contains("SkynetIADS");
            var groupName = GeneratorTools.GetGroupName(mission.LangKey, mission.GroupID, unitFamily, side, isUsingSkynet);
            UnitCallsign? callsign = null;
            GetLayout(units.Count, unitFamily, extraSettings);

            if (unitFamily.GetUnitCategory() == UnitCategory.Static || unitFamily.GetUnitCategory() == UnitCategory.Cargo && unitFamily != UnitFamily.FOB)
                return AddStaticGroup(
                    ref mission,
                    country,
                    coalition,
                    unitFamily,
                    side,
                    units,
                    groupName,
                    callsign,
                    groupTypeLua,
                    unitTypeLua,
                    coordinates,
                    GroupFlags,
                    extraSettings
                );

            var firstUnitID = mission.UnitID;
            var firstUnitDB = units.Select(x => Database.Instance.GetEntry<DBEntryJSONUnit>(x)).FirstOrDefault(x => x != null, null) ?? throw new BriefingRoomException(mission.LangKey, "CantFindUnits", units[0]);
            if (unitFamily.GetUnitCategory().IsAircraft())
            {
                callsign = CallsignGenerator.GetCallsign(ref mission, (DBEntryAircraft)firstUnitDB, country, side, isUsingSkynet, extraSettings.GetValueOrDefault("OverrideCallsignName", "").ToString(), (int)extraSettings.GetValueOrDefault("OverrideCallsignNumber", 1));
                groupName = callsign.Value.GroupName;
                if (extraSettings.ContainsKey("PlayerStartingType") && extraSettings.GetValueOrDefault("PlayerStartingType").ToString() == "TakeOffParking")
                    groupName += "(C)";
                if (extraSettings.ContainsKey("PlayerStartingType") && extraSettings.GetValueOrDefault("PlayerStartingType").ToString() == "Turning Point")
                    groupName += "(A)";

                var aircraftUnitDB = (DBEntryAircraft)firstUnitDB;
                var payloadName = extraSettings.GetValueOrDefault("Payload", "").ToString();
                extraSettings["Pylons"] = !String.IsNullOrEmpty(payloadName) && payloadName != "default" ? aircraftUnitDB.GetPylonsObject(payloadName) : aircraftUnitDB.GetPylonsObject((DCSTask)extraSettings.GetValueOrDefault("DCSTask", DCSTask.Nothing), mission.TemplateRecord.ContextDecade);
            }

            GetLivery(ref mission, firstUnitDB, country, coalition, ref extraSettings);

            var dCSGroup = CreateGroup(
                ref mission,
                groupTypeLua,
                coordinates,
                groupName,
                GroupFlags.HasFlag(GroupFlags.AlwaysHidden),
                unitFamily,
                firstUnitDB,
                extraSettings
            );


            var (dcsUnits, unitsIDList) = AddUnits(
                ref mission,
                units,
                groupName,
                callsign,
                unitTypeLua,
                coordinates,
                GroupFlags,
                side,
                extraSettings
            );


            if (unitsIDList.Count == 0) return null; // No valid units added to this group
            dCSGroup.Units = dcsUnits;


            if (unitFamily.GetUnitCategory().IsAircraft())
            {
                if (GroupFlags.HasFlag(GroupFlags.ImmediateAircraftSpawn))
                {
                    dCSGroup.Name += "-IQ-";
                    if (GroupFlags.HasFlag(GroupFlags.ScrambleStart))
                    {
                        dCSGroup.LateActivation = false;
                        dCSGroup.Uncontrolled = false;
                    }
                }
                else if (GroupFlags.HasFlag(GroupFlags.RadioAircraftSpawn))
                    mission.AppendValue("AircraftRadioActivator", $"{{{mission.GroupID}, \"{groupName}\"}},");
                else if (GroupFlags.HasFlag(GroupFlags.TimedAircraftSpawn))
                {
                    var timeMins = (int)extraSettings.GetValueOrDefault("TimeQueueTime", new MinMaxI(1, 60).GetValue());
                    dCSGroup.Name += $"-TQ-{timeMins}-";
                    if(extraSettings.ContainsKey("ObjectiveName"))
                        dCSGroup.Name += $"-{extraSettings["ObjectiveName"]}";
                }
                else if (GroupFlags.HasFlag(GroupFlags.ProgressionAircraftSpawn)) { }
                else if (groupTypeLua != "AircraftUncontrolled" && !groupTypeLua.Contains("Player"))
                    dCSGroup.Name += "-RQ-";
                else if (GroupFlags.HasFlag(GroupFlags.StaticAircraft))
                    dCSGroup.Name += "-STATIC-";
            }

            if (GroupFlags.HasFlag(GroupFlags.Immortal))
                dCSGroup.Waypoints[0].Tasks.Add(new DCSWrappedWaypointTask("SetImmortal", new Dictionary<string, object> { { "value", true } }, _auto: false, _priority: 1));

            if (GroupFlags.HasFlag(GroupFlags.Inert))
                dCSGroup.Waypoints[0].Tasks.Add(new DCSWrappedWaypointTask("Option", new Dictionary<string, object> { { "value", 4 }, { "name", 0 } }));

            if (GroupFlags.HasFlag(GroupFlags.Invisible))
                dCSGroup.Waypoints[0].Tasks.Add(new DCSWrappedWaypointTask("SetInvisible", new Dictionary<string, object> { { "value", true } }, _auto: false, _priority: 1));

            dCSGroup.Waypoints[0].X = dCSGroup.Units[0].Coordinates.X;
            dCSGroup.Waypoints[0].Y = dCSGroup.Units[0].Coordinates.Y;

            AddUnitGroupToTable(ref mission, country, unitFamily.GetDCSUnitCategory(), dCSGroup);

            BriefingRoom.PrintToLog($"Added group of {units.Count} {coalition} {unitFamily} at {dCSGroup.Units[0].Coordinates}");
            mission.GroupID++;

            return new GroupInfo(ref dCSGroup, firstUnitDB);
        }


        private static DCSGroup CreateGroup(
            ref DCSMission mission,
            string groupTypeLua,
            Coordinates coordinates,
            string groupName,
            bool hidden,
            UnitFamily unitFamily,
            DBEntryJSONUnit firstUnitDB,
            Dictionary<string, object> extraSettings
            )
        {
            string groupYml = File.ReadAllText(Path.Combine(BRPaths.INCLUDE_YAML_GROUP, Toolbox.AddMissingFileExtension(groupTypeLua, ".yml")));
            foreach (KeyValuePair<string, object> extraSetting in extraSettings) // Replace custom values first so they override other replacements
                if (extraSetting.Value is not Array) // Array extra settings are treated on a per-unit basis
                    GeneratorTools.ReplaceKey(ref groupYml, extraSetting.Key, extraSetting.Value);

            GeneratorTools.ReplaceKey(ref groupYml, "GroupID", mission.GroupID);
            GeneratorTools.ReplaceKey(ref groupYml, "GroupX", coordinates.X);
            GeneratorTools.ReplaceKey(ref groupYml, "GroupY", coordinates.Y);
            GeneratorTools.ReplaceKey(ref groupYml, "GroupX2", coordinates.X); // GroupX2 and GroupY2 are replaced by the default coordinates only if they were not replaced earlier in extraSetting replacements. 
            GeneratorTools.ReplaceKey(ref groupYml, "GroupY2", coordinates.Y);
            GeneratorTools.ReplaceKey(ref groupYml, "Name", groupName);
            GeneratorTools.ReplaceKey(ref groupYml, "Hidden", hidden);
            GeneratorTools.ReplaceKey(ref groupYml, "UnitID", mission.UnitID); // Must be after units are added

            if (unitFamily.GetUnitCategory().IsAircraft())
            {
                var aircraftDB = (DBEntryAircraft)firstUnitDB;
                GeneratorTools.ReplaceKey(ref groupYml, "Altitude", aircraftDB.CruiseAlt);
                GeneratorTools.ReplaceKey(ref groupYml, "EPLRS", aircraftDB.EPLRS);
                GeneratorTools.ReplaceKey(ref groupYml, "RadioBand", (int)aircraftDB.Radio.Modulation);
                GeneratorTools.ReplaceKey(ref groupYml, "RadioFrequency", aircraftDB.Radio.Frequency);
                GeneratorTools.ReplaceKey(ref groupYml, "Speed", aircraftDB.CruiseSpeed);
            }

            if (unitFamily.GetUnitCategory() == UnitCategory.Vehicle || unitFamily.GetUnitCategory() == UnitCategory.Infantry)
            {
                GeneratorTools.ReplaceKey(ref groupYml, "Formation", Toolbox.RandomFrom(new List<string>{
                    "Rank",
                    "Cone",
                    "Vee",
                    "Diamond",
                    "EchelonL",
                    "EchelonR"
                    }));
            }

            var dCSGroup = DCSGroup.YamlToGroup(groupYml);
            // default late activation; specific cases below will override
            dCSGroup.LateActivation = true;

            if (unitFamily.GetUnitCategory().IsAircraft() && extraSettings.ContainsKey("GroupAirbaseID") && dCSGroup.Waypoints[0].AirdromeId == default)
            {
                dCSGroup.Waypoints[0].AirdromeId = (int)extraSettings.GetValueOrDefault("GroupAirbaseID", 0);
                var isHotStart = new List<UnitFamily> { UnitFamily.PlaneAWACS, UnitFamily.PlaneTankerBasket, UnitFamily.PlaneTankerBoom, UnitFamily.PlaneSEAD, UnitFamily.PlaneDrone }.Contains(unitFamily);
                dCSGroup.Waypoints[0].Type = isHotStart ? "TakeOffParkingHot" : "TakeOffParking";
                dCSGroup.Waypoints[0].Action = isHotStart ? "From Parking Area Hot" : "From Parking Area";
                dCSGroup.LateActivation = false;
                if (!isHotStart)
                    dCSGroup.Uncontrolled = true;
            }

            // Ensure player groups are never late-activated
            if (groupTypeLua.StartsWith("AircraftPlayer", StringComparison.InvariantCulture))
            {
                dCSGroup.LateActivation = false;
                dCSGroup.Uncontrolled = false;
            }

            return dCSGroup;

        }


        private static (List<DCSUnit> dCSUnits, List<int> unitsIDList) AddUnits(
            ref DCSMission mission,
            List<string> unitSets,
            string groupName,
            UnitCallsign? callsign,
            string unitTypeLua,
            Coordinates coordinates,
            GroupFlags GroupFlags,
            Side side,
            Dictionary<string, object> extraSettings
            )
        {
            int unitLuaIndex = 1;
            var unitsIDList = new List<int>();
            var dCSUnits = new List<DCSUnit>();
            foreach (var unitSet in unitSets)
            {
                DBEntryJSONUnit unitDB = Database.Instance.GetEntry<DBEntryJSONUnit>(unitSet);
                if (unitDB == null)
                {
                    BriefingRoom.PrintTranslatableWarning(mission.LangKey, "UnitNotFound", unitSet);
                    continue;
                }
                dCSUnits.Add(AddUnit(
                    ref mission,
                   unitDB.DCSID,
                   groupName,
                   callsign,
                   unitLuaIndex,
                   unitDB,
                   unitTypeLua,
                   coordinates,
                   GroupFlags,
                   side,
                   extraSettings,
                   unitSets.Count == 1
                   ));

                unitsIDList.Add(mission.UnitID);
                unitLuaIndex++;
                mission.UnitID++;
            }
            return (dCSUnits, unitsIDList);
        }

        private static GroupInfo? AddStaticGroup(
            ref DCSMission mission,
            Country country,
            Coalition coalition,
            UnitFamily unitFamily,
            Side side,
            List<string> unitSets,
            string groupName,
            UnitCallsign? callsign,
            string groupTypeLua,
            string unitTypeLua,
            Coordinates coordinates,
            GroupFlags GroupFlags,
            Dictionary<string, object> extraSettings
        )
        {
            List<int> unitsIDList = new();
            var DCSGroups = new List<DCSGroup>();
            int unitIndex = 1;
            foreach (var unitSet in unitSets)
            {
                var unitDB = Database.Instance.GetEntry<DBEntryJSONUnit>(unitSet);
                if (unitDB == null)
                {
                    BriefingRoom.PrintTranslatableWarning(mission.LangKey, "UnitNotFound", unitSet);
                    continue;
                }
                var groupHeading = GetGroupHeading(coordinates, extraSettings);
                var (unitCoordinates, unitHeading) = SetUnitCoordinatesAndHeading(ref mission, unitDB, unitIndex, coordinates, groupHeading, (List<DBEntryTemplateUnit>)extraSettings.GetValueOrDefault("TemplatePositionMap", new List<DBEntryTemplateUnit>()));
                var firstUnitID = mission.UnitID;
                var dCSGroup = CreateGroup(
                    ref mission,
                    groupTypeLua,
                    unitCoordinates,
                    groupName,
                    GroupFlags.HasFlag(GroupFlags.AlwaysHidden),
                    UnitFamily.StaticStructureMilitary,
                    unitDB,
                    extraSettings
                );
                var dCSUnit = AddUnit(
                    ref mission,
                    unitDB.DCSID,
                    groupName,
                    callsign,
                    unitIndex,
                    unitDB,
                    unitTypeLua,
                    coordinates,
                    GroupFlags,
                    side,
                    extraSettings
                    );

                unitsIDList.Add(mission.UnitID);
                unitIndex++;
                mission.UnitID++;

                dCSGroup.Units = new List<DCSUnit> { dCSUnit };

                DCSGroups.Add(dCSGroup);
                AddUnitGroupToTable(ref mission, country, DCSUnitCategory.Static, dCSGroup);

                BriefingRoom.PrintToLog($"Added group of {unitDB.DCSID} {coalition} {unitFamily} at {coordinates}");

                mission.GroupID++;
            }

            if (GroupFlags.HasFlag(GroupFlags.EmbeddedAirDefense) && unitFamily != UnitFamily.StaticStructureOffshore)
            {
                List<string> airDefenseUnits = GeneratorTools.GetEmbeddedAirDefenseUnits(mission.LangKey, mission.TemplateRecord, side, unitFamily.GetUnitCategory());
                if (airDefenseUnits.Count > 0)
                {
                    var dCSGroup = CreateGroup(
                            ref mission,
                            "Vehicle",
                            coordinates,
                            groupName,
                            GroupFlags.HasFlag(GroupFlags.AlwaysHidden),
                            UnitFamily.VehicleAAA,
                            null,
                            extraSettings
                        );
                    var (unitsLuaTable, embeddedunitsIDList) = AddUnits(
                        ref mission,
                        airDefenseUnits,
                        groupName,
                        callsign,
                        "Vehicle",
                        coordinates,
                        GroupFlags,
                        side,
                        extraSettings
                    );
                    dCSGroup.Units = unitsLuaTable;
                    mission.GroupID++;
                    unitsIDList.AddRange(embeddedunitsIDList);
                    AddUnitGroupToTable(ref mission, country, DCSUnitCategory.Vehicle, dCSGroup);
                    BriefingRoom.PrintToLog($"Added group of Embedded Air Defense for Static {coalition} {unitFamily} at {coordinates}");
                }
            }

            var firstUnitDB = Database.Instance.GetEntry<DBEntryJSONUnit>(unitSets.First());
            return new GroupInfo(ref DCSGroups, firstUnitDB);
        }


        private static DCSUnit AddUnit(
            ref DCSMission mission,
            string DCSID,
            string groupName,
            UnitCallsign? callsign,
            int unitLuaIndex,
            DBEntryJSONUnit unitDB,
            string unitType,
            Coordinates coordinates,
            GroupFlags GroupFlags,
            Side side,
            Dictionary<string, object> extraSettings,
            bool singleUnit = false)
        {
            if (!string.IsNullOrEmpty(unitDB.Module) && DBEntryDCSMod.CORE_MODS.Contains(unitDB.Module, StringComparer.InvariantCultureIgnoreCase))
            {
                DBEntryDCSMod mod = Database.Instance.GetEntry<DBEntryDCSMod>(unitDB.Module);
                if (mod != null && !string.IsNullOrEmpty(mod.Module))
                    mission.ModUnits.Add(mod.Module);
            }
            var unit = new DCSUnit(unitType);

            var groupHeading = GetGroupHeading(coordinates, extraSettings);
            var (unitCoordinates, unitHeading) = SetUnitCoordinatesAndHeading(ref mission, unitDB, unitLuaIndex, coordinates, groupHeading, (List<DBEntryTemplateUnit>)extraSettings.GetValueOrDefault("TemplatePositionMap", new List<DBEntryTemplateUnit>()), singleUnit);

            foreach (KeyValuePair<string, object> extraSetting in extraSettings.Where(x => !IGNORE_PROPS.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value))
            {
                var prop = unit.GetType().GetProperty(extraSetting.Key, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                prop?.SetValue(unit, extraSetting.Value);
            }
            unit.Heading = unitHeading;
            unit.DCSID = DCSID;
            unit.UnitId = mission.UnitID;
            unit.Coordinates = ((List<Coordinates>)extraSettings.GetValueOrDefault("UnitCoords", new List<Coordinates>())).ElementAtOrDefault(unitLuaIndex - 1, unitCoordinates);
            unit.PlayerCanDrive = true;

            if (Toolbox.IsAircraft(unitDB.Category) && (unitLuaIndex == 1) && GroupFlags.HasFlag(GroupFlags.FirstUnitIsClient))
                unit.Skill = mission.SinglePlayerMission ? "Player" : "Client";
            else
                unit.Skill = extraSettings.GetValueOrDefault("Skill", GeneratorTools.GetDefaultSkillLevel(mission.TemplateRecord, side)).ToString(); ;

            unit.LiveryId = extraSettings.GetValueOrDefault("Livery", "default").ToString();
            unit.ShapeName = unitDB.Shape;

            if (Toolbox.IsAircraft(unitDB.Category))
            {
                var aircraftUnitDB = (DBEntryAircraft)unitDB;
                unit.Callsign = callsign.Value.GetLua(unitLuaIndex);
                unit.Name = callsign.Value.GetUnitName(unitLuaIndex);
                unit.OnboardNum = Toolbox.RandomInt(1, 1000).ToString("000");
                unit.PropsLua = aircraftUnitDB.ExtraProps;
                unit.RadioPresets = aircraftUnitDB.PanelRadios.Select(x => x.SetOverrides(
                    (double)extraSettings.GetValueOrDefault("RadioFrequencyDouble", aircraftUnitDB.Radio.Frequency),
                    (int)extraSettings.GetValueOrDefault("RadioBand", (int)aircraftUnitDB.Radio.Modulation),
                    (double?)extraSettings.GetValueOrDefault("AirbaseRadioFrequency", null),
                    (int?)extraSettings.GetValueOrDefault("AirbaseRadioModulation", null)
                    )).ToList();
                unit.PayloadCommon = aircraftUnitDB.PayloadCommon;
                unit.Pylons = (Dictionary<int, Dictionary<string, object>>)extraSettings.GetValueOrDefault("Pylons", new Dictionary<int, Dictionary<string, object>>());
                unit.Parking = ((List<int>)extraSettings.GetValueOrDefault("ParkingID", new List<int>())).ElementAtOrDefault(unitLuaIndex - 1);
            }
            else
                unit.Name = $"{groupName} {unitLuaIndex}";

            return unit;
        }

        private static void GetLivery(ref DCSMission mission, DBEntryJSONUnit unitDB, Country country, Coalition coalition, ref Dictionary<string, object> extraSettings)
        {
            var LiveryId = extraSettings.GetValueOrDefault("Livery", "default").ToString();
            if (LiveryId != "default")
                return;
            var options = unitDB.Liveries.GetValueOrDefault(country, new List<Tuple<string, string>> { Tuple.Create("default", "default") });
            if ((country == Country.CombinedJointTaskForcesBlue || country == Country.CombinedJointTaskForcesRed) && options.Count == 1)
            {
                foreach (var coalitionCountry in mission.CoalitionsCountries[(int)coalition])
                {
                    options.AddRange(unitDB.Liveries.GetValueOrDefault(coalitionCountry, new List<Tuple<string, string>> { }));
                }
            }
            if (DBEntryTheater.DESERT_MAPS.Contains(mission.TheaterID))
            {
                var subset = options.Where(x => x.Item2.ToLower().Contains("desert")).ToList();
                if (subset.Count > 0)
                    options = subset;
            }
            else
            {
                var subset = options.Where(x => !x.Item2.ToLower().Contains("desert")).ToList();
                if (subset.Count > 0)
                    options = subset;
            }

            var season = Toolbox.GetSeasonFromMonth(int.Parse(mission.GetValue("DateMonth")));
            var seasonSubset = options.Where(x => x.Item2.ToLower().Contains(season.ToString().ToLower())).ToList();
            if (seasonSubset.Count > 0)
                options = seasonSubset;
            // apply livery ban lists by coalition if provided
            var liveryBans = coalition == Coalition.Blue ? mission.TemplateRecord.OptionsLiveryBanListBlue : mission.TemplateRecord.OptionsLiveryBanListRed;
            if (liveryBans != null && liveryBans.Count > 0)
            {
                options = options.Where(x => !liveryBans.Any(b => x.Item2.Contains(b, StringComparison.InvariantCultureIgnoreCase) || x.Item1.Contains(b, StringComparison.InvariantCultureIgnoreCase))).ToList();
                if (options.Count == 0)
                    options = unitDB.Liveries.GetValueOrDefault(country, new List<Tuple<string, string>> { Tuple.Create("default", "default") });
            }
            extraSettings["Livery"] = Toolbox.RandomFrom(options).Item1;
        }


        private static void AddUnitGroupToTable(ref DCSMission mission, Country country, DCSUnitCategory category, DCSGroup dCSGroup)
        {
            if (!mission.UnitLuaTables.ContainsKey(country)) mission.UnitLuaTables.Add(country, new Dictionary<DCSUnitCategory, List<DCSGroup>>());
            if (!mission.UnitLuaTables[country].ContainsKey(category)) mission.UnitLuaTables[country].Add(category, new List<DCSGroup>());
            mission.UnitLuaTables[country][category].Add(dCSGroup);
        }

        internal static string GetUnitsLuaTable(ref DCSMission mission, Coalition coalition)
        {
            string unitsLuaTable = "";

            for (int countryIndex = 0; countryIndex < mission.CoalitionsCountries[(int)coalition].Length; countryIndex++) // Check all countries in this coalition
            {
                Country country = mission.CoalitionsCountries[(int)coalition][countryIndex];


                unitsLuaTable += $"[{countryIndex + 1}] =\n";
                unitsLuaTable += "{\n";
                unitsLuaTable += $"[\"id\"] = {(int)country},\n";
                unitsLuaTable += $"[\"name\"] = \"{country}\",\n";


                if (mission.UnitLuaTables.ContainsKey(country))
                {
                    foreach (DCSUnitCategory unitCategory in Toolbox.GetEnumValues<DCSUnitCategory>()) // Check all coalitions
                    {
                        if (!mission.UnitLuaTables[country].ContainsKey(unitCategory)) continue; // No unit for this unit category

                        unitsLuaTable += $"[\"{unitCategory.ToString().ToLower()}\"] =\n";
                        unitsLuaTable += "{\n";
                        unitsLuaTable += "[\"group\"] =\n";
                        unitsLuaTable += "{\n";
                        for (int groupIndex = 0; groupIndex < mission.UnitLuaTables[country][unitCategory].Count; groupIndex++)
                        {
                            unitsLuaTable += $"[{groupIndex + 1}] = {mission.UnitLuaTables[country][unitCategory][groupIndex].ToLuaString()},";
                        }

                        unitsLuaTable += $"}}, -- end of [\"group\"]\n";
                        unitsLuaTable += $"}}, -- end of [\"{unitCategory.ToString().ToLower()}\"]\n";
                    }
                }
                unitsLuaTable += $"}}, -- end of [{countryIndex + 1}]\n";
            }

            return unitsLuaTable;
        }

        internal static string GetRequiredModules(ref DCSMission mission)
        {
            var str = "";
            foreach (var unitId in mission.ModUnits.Distinct().ToList())
            {
                str += $"[\"{unitId}\"] = \"{unitId}\",";
            }
            return str;
        }

        internal static string GetRequiredModulesBriefing(ref DCSMission mission)
        {
            if (mission.ModUnits.Count == 0)
                return "";
            return "<p><strong>Required Mods:</strong><br/>" + string.Join("<br/>", mission.ModUnits.Distinct().ToList()) + "</p>";
        }

        private static double GetGroupHeading(Coordinates groupCoordinates, Dictionary<string, object> extraSettings)
        {
            if (!extraSettings.ContainsKey("GroupX2"))
                return 0.0;
            // return 3.141593;
            var waypointCoor = new Coordinates((double)extraSettings["GroupX2"], (double)extraSettings["GroupY2"]);
            return Coordinates.ToAngleInRadians(groupCoordinates, waypointCoor);
        }

        private static (Coordinates unitCoordinates, double unitHeading) SetUnitCoordinatesAndHeading(
            ref DCSMission mission,
            DBEntryJSONUnit unitDB,
            int unitIndex,
            Coordinates groupCoordinates,
            double groupHeading,
            List<DBEntryTemplateUnit> templatePositionMap,
            bool singleUnit = false)
        {
            var unitCoordinates = groupCoordinates;
            var unitHeading = groupHeading;

            if (unitDB.IsAircraft)
                unitCoordinates = groupCoordinates + new Coordinates(AIRCRAFT_UNIT_SPACING, AIRCRAFT_UNIT_SPACING) * unitIndex;
            else
            {
                var posIndex = unitIndex - 1;
                var hasTemplatePosition = templatePositionMap.Count > posIndex;
                if (hasTemplatePosition) // Unit has a fixed set of coordinates (for SAM sites, etc.)
                {
                    unitCoordinates = TransformFromOffset(unitHeading, groupCoordinates, templatePositionMap[posIndex].DCoordinates);
                }
                else if (!singleUnit) // No fixed coordinates, generate random coordinates
                {
                    switch (unitDB.Category)
                    {
                        case UnitCategory.Ship:
                            if (posIndex > 0)
                                unitCoordinates = groupCoordinates.CreateNearRandom(SHIP_UNIT_SPACING, SHIP_UNIT_SPACING * 10);
                            break;
                        case UnitCategory.Cargo:
                        case UnitCategory.Static:
                            if (posIndex > 0)
                                unitCoordinates = groupCoordinates.CreateNearRandom(STATIC_UNIT_SPACING, STATIC_UNIT_SPACING * 10);
                            break;
                        default:
                            var spType = mission.UsedSpawnPoints.Find(x => x.Coordinates.Equals(groupCoordinates)).PointType;
                            switch (spType)
                            {
                                case SpawnPointType.LandSmall:
                                    unitCoordinates = groupCoordinates.CreateNearRandom(VEHICLE_UNIT_SPACING, VEHICLE_UNIT_SPACING * 1.5);
                                    break;
                                case SpawnPointType.LandMedium:
                                    unitCoordinates = groupCoordinates.CreateNearRandom(VEHICLE_UNIT_SPACING, VEHICLE_UNIT_SPACING * 10);
                                    break;
                                default:
                                    unitCoordinates = groupCoordinates.CreateNearRandom(VEHICLE_UNIT_SPACING, VEHICLE_UNIT_SPACING * 20);
                                    break;
                            }
                            break;
                    }
                }

                if (hasTemplatePosition) // Unit has a fixed heading (for SAM sites, etc.)
                    unitHeading = Toolbox.ClampAngle(unitHeading + templatePositionMap[posIndex].Heading);
                else if (unitDB.Category != UnitCategory.Ship)
                    unitHeading = Toolbox.RandomDouble(Toolbox.TWO_PI);
            }
            return (unitCoordinates, unitHeading);
        }

        private static Coordinates TransformFromOffset(double groupHeading, Coordinates groupCoordinates, Coordinates offsetCoordinates)
        {
            // it seems that for some reason X&Y are reversed when it comes to this stuff and needs to rotated backawards from heading.
            // Why don't know Maybe ED will announce its a bug and poof or soviet russia x is y and y is x
            // Its late my head hurts, be ware all who venture here.
            double sinTheta = Math.Sin(Toolbox.TWO_PI / 2 - groupHeading);
            double cosTheta = Math.Cos(Toolbox.TWO_PI / 2 - groupHeading);
            return groupCoordinates + new Coordinates(
                (offsetCoordinates.X * cosTheta) + (offsetCoordinates.Y * sinTheta),
                (-offsetCoordinates.X * sinTheta) + (offsetCoordinates.Y * cosTheta));
        }

        private static void GetLayout(int unitCount, UnitFamily family, Dictionary<string, object> extraSettings)
        {
            if (extraSettings.ContainsKey("TemplatePositionMap"))
                return;

            var options = Database.Instance.GetAllEntries<DBEntryLayout>().Where(x => (x.Categories.Contains(family.GetUnitCategory())) && unitCount > x.MinUnits && unitCount < x.Units.Count).ToList();
            if (options.Count == 0 || Toolbox.RandomChance(3)) // Random added until we have a decent count of layouts
                return;
            var selected = Toolbox.RandomFrom(options);
            BriefingRoom.PrintToLog($"Using Layout {selected.UIDisplayName.Get("en")}");
            extraSettings["TemplatePositionMap"] = selected.Units;

        }
    }
}