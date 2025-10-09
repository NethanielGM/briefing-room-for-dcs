[$INDEX$] = 
		{
			["rules"] = 
			{
				[1] = 
				{
					["group"] = $TRIGGROUP$,
					["predicate"] = "c_part_of_group_in_zone",
					["zone"] = $ZONEID$,
				}, -- end of [1]
			}, -- end of ["rules"]
			["comment"] = "BR - Activate group $ACTIVATIONGROUPID$ when any part of group $TRIGGROUP$ is in zone $ZONEID$",
			["eventlist"] = "",
			["actions"] = 
			{
				[1] = 
				{
					["group"] = $ACTIVATIONGROUPID$,
					["predicate"] = "a_activate_group",
				}, -- end of [1]
			}, -- end of ["actions"]
			["predicate"] = "triggerOnce",
		},