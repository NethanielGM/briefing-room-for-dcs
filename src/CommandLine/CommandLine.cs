﻿/*
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

using BriefingRoom4DCS.Mission;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BriefingRoom4DCS.CommandLineTool
{
    public class CommandLine
    {
        private static readonly string LOG_FILE = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BriefingRoomCommandLineDebugLog.txt");

        private readonly StreamWriter LogWriter;

        [STAThread]
        private static async Task Main(string[] args)
        {
#if DEBUG
            if (args.Length == 0) args = new string[] { "Default.brt" };
#endif

            try
            {
                await new CommandLine().DoCommandLineAsync(args);
            }
            catch (Exception e)
            {
                Console.WriteLine($"CRITICAL ERROR: {e.Message}");
            }
        }

        public CommandLine()
        {
            if (File.Exists(LOG_FILE)) File.Delete(LOG_FILE);
            LogWriter = File.AppendText(LOG_FILE);
            LogWriter.Flush();
        }

        private void WriteToDebugLog(string message, LogMessageErrorLevel errorLevel = LogMessageErrorLevel.Info)
        {
            switch (errorLevel)
            {
                case LogMessageErrorLevel.Error: message = $"ERROR: {message}"; break;
                case LogMessageErrorLevel.Warning: message = $"WARNING: {message}"; break;
            }

            LogWriter.WriteLine(message);
            Console.WriteLine(message);
        }

        public async Task<bool> DoCommandLineAsync(string[] args)
        {
			// Parse arguments: templates and optional output directory (--out or -o)
			string outputDirectory = AppDomain.CurrentDomain.BaseDirectory;
			var templateFilesList = new System.Collections.Generic.List<string>();
			var invalidTemplateFilesList = new System.Collections.Generic.List<string>();

			for (int i = 0; i < args.Length; i++)
			{
				string arg = args[i];
				if (string.IsNullOrWhiteSpace(arg)) continue;

				// Support --out <dir>, -o <dir>, --out=<dir>, -o=<dir>
				if (string.Equals(arg, "--out", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "-o", StringComparison.OrdinalIgnoreCase))
				{
					if (i + 1 >= args.Length)
					{
						WriteToDebugLog("Missing directory after --out option.", LogMessageErrorLevel.Error);
						return false;
					}
					outputDirectory = args[++i];
					continue;
				}
				if (arg.StartsWith("--out=", StringComparison.OrdinalIgnoreCase))
				{
					outputDirectory = arg.Substring("--out=".Length);
					continue;
				}
				if (arg.StartsWith("-o=", StringComparison.OrdinalIgnoreCase))
				{
					outputDirectory = arg.Substring("-o=".Length);
					continue;
				}

				// Treat as template candidate
				if (File.Exists(arg)) templateFilesList.Add(arg); else invalidTemplateFilesList.Add(arg);
			}

			foreach (string filePath in invalidTemplateFilesList)
				WriteToDebugLog($"Template file {filePath} doesn't exist.", LogMessageErrorLevel.Warning);

			var templateFiles = templateFilesList.ToArray();

			if (templateFiles.Length == 0)
			{
				WriteToDebugLog("No valid mission template files given as parameters.", LogMessageErrorLevel.Error);
				WriteToDebugLog("");
				WriteToDebugLog("Command-line format is BriefingRoomCommandLine.exe [--out <directory>] <MissionTemplate.brt> [<MissionTemplate2.brt> <MissionTemplate3.brt>...]");
				return false;
			}

			// Ensure output directory exists (create if needed)
			try
			{
				if (string.IsNullOrWhiteSpace(outputDirectory)) outputDirectory = AppDomain.CurrentDomain.BaseDirectory;
				if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);
			}
			catch (Exception)
			{
				WriteToDebugLog($"Failed to create output directory {outputDirectory}", LogMessageErrorLevel.Error);
				return false;
			}

            var briefingRoom = new BriefingRoom();

            foreach (string t in templateFiles)
            {
                if (Path.GetExtension(t).ToLower() == ".cbrt") // Template file is a campaign template
                {
                    DCSCampaign campaign = briefingRoom.GenerateCampaign(t);
                    if (campaign == null)
                    {
                        Console.WriteLine($"Failed to generate a campaign from template {Path.GetFileName(t)}");
                        continue;
                    }

					string campaignDirectory;
					if (templateFiles.Length == 1) // Single template file provided, use  campaign name as campaign path.
						campaignDirectory = Path.Combine(outputDirectory, RemoveInvalidPathCharacters(campaign.Name));
					else // Multiple template files provided, use the template name as campaign name so we know from which template campaign was generated.
						campaignDirectory = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(t));
					campaignDirectory = GetUnusedFileName(campaignDirectory);

					await campaign.ExportToDirectory(EnsureTrailingDirectorySeparator(campaignDirectory));
					WriteToDebugLog($"Campaign {Path.GetFileName(campaignDirectory)} exported to directory from template {Path.GetFileName(t)}");
                }
                else // Template file is a mission template
                {
                    DCSMission mission = briefingRoom.GenerateMission(t);
                    if (mission == null)
                    {
                        Console.WriteLine($"Failed to generate a mission from template {Path.GetFileName(t)}");
                        continue;
                    }

					string mizFileName;
					if (templateFiles.Length == 1) // Single template file provided, use "theater + mission name" as file name.
						mizFileName = Path.Combine(outputDirectory, $"{mission.TheaterID} - {RemoveInvalidPathCharacters(mission.Briefing.Name)}.miz");
					else // Multiple template files provided, use the template name as file name so we know from which template mission was generated.
						mizFileName = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(t) + ".miz");
                    mizFileName = GetUnusedFileName(mizFileName);

                    var savedMission = await mission.SaveToMizFile(mizFileName);
                    if (!savedMission)
                    {
                        WriteToDebugLog($"Failed to export .miz file from template {Path.GetFileName(t)}", LogMessageErrorLevel.Warning);
                        continue;
                    }
                    else
                        WriteToDebugLog($"Mission {Path.GetFileName(mizFileName)} exported to .miz file from template {Path.GetFileName(t)}. Found in {mizFileName}");
                }
            }

            return true;
        }

        private static string RemoveInvalidPathCharacters(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "_";
            return string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        }

        private static string GetUnusedFileName(string filePath)
        {
            if (!File.Exists(filePath)) return filePath; // File doesn't exist, use the desired name

            string newName;
            int extraNameCount = 2;

            do
            {
                newName = Path.Combine(Path.GetDirectoryName(filePath), $"{Path.GetFileNameWithoutExtension(filePath)} ({extraNameCount}){Path.GetExtension(filePath)}");
                extraNameCount++;
            } while (File.Exists(newName));

            return newName;
        }

		private static string EnsureTrailingDirectorySeparator(string path)
		{
			if (string.IsNullOrEmpty(path)) return path;
			char sep = Path.DirectorySeparatorChar;
			if (path.EndsWith(sep) || path.EndsWith(Path.AltDirectorySeparatorChar)) return path;
			return path + sep;
		}
    }
}
