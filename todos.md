1. brt options:
- add LiveryBanListBlue eg; "Egyptian Air Force, ISRAIL_UN"
- add UnitBanListBlue eg; "F-16C bl.50, S-300PS"
- add LiveryBanListRed eg; "Israeli Air Force, US, RU"
- add UnitBanListRed eg; "F-16C bl.50"


2. all missions should be in late activation mode. create f10 menu  radio items to select what missions to activate. when activating remove that mission from the f10 menu


3. add for objective another parameter:
- objective001.StartActive parameter should be set to true or false depending if the mission creator wants the mission to auto start or wait for player to activate it


4. create api for generating missions
- Invoke-WebRequest -Method POST -Uri "http://localhost:5000/Generator/from-brt" -ContentType "application/json" -Body '{"path":"/app/Missions/Default.brt"}' -OutFile "$env:USERPROFILE\Saved Games\DCS\Missions\Default.miz"


5. spawn more missions and test that they are distributed correctly across all red zones


6. make sure gaza and west bank do not get any SAMS. no Jets or helis. no tanks. no sa5,sa6 etc. only manpads and trucks with machine guns. Spawn SAMS and Jets and all only in north of the map in lebanon and Syria. Also then. dont spawn them close to the Israeli border 



ban any enemy sams and aircraft in gaza and west bank. there should not be any SAMS or TANKS or AIRCRAFT in gaza and in west bank. only up north in lebanon and syria. IN REAL LIFE THEY DONT POSSES SUCH EQUIPMENT.

THIS PROJECT IS ALL ABOUT REALISM



update the n8n system promt to interact with the program correctly@n8n.md 