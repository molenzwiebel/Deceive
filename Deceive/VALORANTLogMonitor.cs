using System;
using System.IO;
using System.Linq;

namespace Deceive
{
    /// <summary>
    /// Extracts current VALORANT version from the logs, so that we can show a fake
    /// player with the proper version and avoid "Version Mismatch" from being shown.
    /// 
    /// This isn't technically ncessary, but people keep coming in and asking whether
    /// the scary red text means Deceive doesn't work, so might as well do this and
    /// get a slightly better user experience.
    /// </summary>
    internal static class VALORANTLogMonitor
    {
        private static string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VALORANT/Saved/Logs"
        );

        private static string LogFile = Path.Combine(
            LogFolder,
            "ShooterGame.log"
        );

        private static string LogTmpFile = Path.Combine(
            LogFolder,
            "ShooterGame.log.tmp"
        );

        public static string? GetVALORANTVersion()
        {
            if (!File.Exists(LogFile)) return null;

            // need to copy it over in case valorant has the log file locked
            File.Copy(LogFile, LogTmpFile, true);
            var contents = File.ReadAllText(LogTmpFile);
            File.Delete(LogTmpFile);

            var lines = contents.Split('\n');

            // Format as shown in the log:
            // [2022.04.21 - 21.30.19:394][  0]LogShooter: Display: Branch: release-04.07
            // [2022.04.21 - 21.30.19:394][  0]LogShooter: Display: Changelist: 699063
            // [2022.04.21 - 21.30.19:394][  0]LogShooter: Display: Build version: 15
            try
            {
                var branch = lines.FirstOrDefault(x => x.Contains("LogShooter: Display: Branch: "))?.Trim()?.Split(": ");
                var changelist = lines.FirstOrDefault(x => x.Contains("LogShooter: Display: Changelist: "))?.Trim()?.Split(": ");
                var buildVersion = lines.FirstOrDefault(x => x.Contains("LogShooter: Display: Build version: "))?.Trim()?.Split(": ");

                if (branch == null || changelist == null || buildVersion == null) return null;

                // expected format is "release-04.07-shipping-15-699063"
                return branch.Last() + "-shipping-" + buildVersion.Last() + "-" + changelist.Last();
            }
            catch
            {
                // don't crash on an exception
                return null;
            }
        }
    }
}
