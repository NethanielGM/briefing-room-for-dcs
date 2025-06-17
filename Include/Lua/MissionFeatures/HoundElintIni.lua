do
  Elint_blue = HoundElint:create(briefingRoom.playerCoalition)
  Elint_blue:addPlatform(briefingRoom.mission.missionFeatures.unitNames.houndElint[1])
  -- it's recommended to have at least two active platform to make system faster and more accurate
  Elint_blue:addPlatform(briefingRoom.mission.missionFeatures.unitNames.houndElint[1])
  Elint_blue:systemOn()
  -- This is a basic setup with map markers only
  -- additional stuff (uncomment if desired)
  Elint_blue:enableController() -- This will enable Voice+text controller messages
  -- Elint_blue:enableAtis() -- ATIS requires STTS/GRPC, as it is voice only
end