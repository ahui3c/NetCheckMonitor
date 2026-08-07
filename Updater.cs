using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

[assembly: System.Reflection.AssemblyTitle("NetCheckUpdater")]
[assembly: System.Reflection.AssemblyProduct("NetCheckMonitor")]
[assembly: System.Reflection.AssemblyCompany("廖阿輝")]

namespace NetCheckUpdater
{
    internal sealed class ManifestFile
    {
        public string path { get; set; }
        public string sha256 { get; set; }
    }

    internal sealed class Manifest
    {
        public int schema { get; set; }
        public string version { get; set; }
        public List<ManifestFile> files { get; set; }
    }

    internal static class Program
    {
        private static string logPath;
        private static string targetDirectory;
        private static string sourceDirectory;
        private static string backupDirectory;
        private static string mainExecutableName;
        private static bool resumeRequested;
        private static bool previousProcessExited;
        private static readonly List<string> replaced = new List<string>();
        private static readonly HashSet<string> originallyExisting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Dictionary<string, string> options = ParseArguments(args);
                sourceDirectory = RequiredDirectory(options, "source");
                targetDirectory = RequiredDirectory(options, "target");
                logPath = Required(options, "log");
                string mainName = Required(options, "main");
                mainExecutableName = mainName;
                string healthFile = Required(options, "health");
                string version = Required(options, "version");
                string manifestDigest = Required(options, "manifest-digest");
                resumeRequested = options.ContainsKey("resume") && String.Equals(options["resume"], "1", StringComparison.Ordinal);
                int pid;
                if (!Int32.TryParse(Required(options, "wait-pid"), out pid) || pid <= 0) throw new InvalidDataException("Invalid process ID.");
                long startedTicks;
                if (!Int64.TryParse(Required(options, "wait-start"), out startedTicks)) throw new InvalidDataException("Invalid process start time.");
                if (!String.Equals(Path.GetFileName(mainName), mainName, StringComparison.Ordinal)) throw new InvalidDataException("Invalid main executable name.");
                if (!Path.GetFullPath(sourceDirectory).StartsWith(Path.GetFullPath(Path.GetDirectoryName(sourceDirectory)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid source directory.");

                backupDirectory = Path.Combine(Path.GetDirectoryName(sourceDirectory), "backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
                Directory.CreateDirectory(backupDirectory);
                Manifest manifest = LoadAndValidateManifest(sourceDirectory, version, manifestDigest);
                Log("WAIT", "STARTED", "Pid=" + pid + ";Version=" + version);
                WaitForProcess(pid, startedTicks, 120000);
                previousProcessExited = true;
                Log("WAIT", "SUCCESS", "Pid=" + pid);
                Apply(manifest);
                Log("REPLACE", "SUCCESS", "Files=" + manifest.files.Count + ";Version=" + version);

                if (String.Equals(Environment.GetEnvironmentVariable("NETCHECK_UPDATER_TEST_NO_LAUNCH"), "1", StringComparison.Ordinal))
                {
                    File.WriteAllText(healthFile, version, Encoding.UTF8);
                    CleanupBackup();
                    Log("COMPLETE", "SUCCESS", "TestMode=1;Version=" + version);
                    return 0;
                }

                string executable = Path.Combine(targetDirectory, mainName);
                string launchArguments = (resumeRequested ? "--resume-update " : "") + "--update-health-file " + Quote(healthFile);
                Process launched = Process.Start(new ProcessStartInfo(executable, launchArguments) { UseShellExecute = false, WorkingDirectory = targetDirectory });
                if (launched == null) throw new InvalidOperationException("The updated application did not start.");
                Log("RELAUNCH", "STARTED", "Pid=" + launched.Id + ";Version=" + version);
                DateTime deadline = DateTime.UtcNow.AddSeconds(45);
                while (DateTime.UtcNow < deadline)
                {
                    if (File.Exists(healthFile))
                    {
                        CleanupBackup();
                        Log("COMPLETE", "SUCCESS", "Pid=" + launched.Id + ";Version=" + version);
                        return 0;
                    }
                    if (launched.HasExited) throw new InvalidOperationException("The updated application exited before reporting a healthy startup.");
                    Thread.Sleep(250);
                }
                Log("HEALTH", "FAILED", "The updated application did not report a healthy startup within 45 seconds. Backup retained at " + backupDirectory);
                return 3;
            }
            catch (Exception ex)
            {
                Log("UPDATE", "FAILED", ex.GetType().Name + ": " + ex.Message);
                try
                {
                    Rollback();
                    Log("ROLLBACK", "SUCCESS", "Files=" + replaced.Count);
                    if (previousProcessExited && !String.IsNullOrWhiteSpace(mainExecutableName))
                    {
                        string restored = Path.Combine(targetDirectory, mainExecutableName);
                        if (File.Exists(restored))
                        {
                            Process restarted = Process.Start(new ProcessStartInfo(restored, resumeRequested ? "--resume-update" : "") { UseShellExecute = false, WorkingDirectory = targetDirectory });
                            Log("ROLLBACK_RELAUNCH", restarted == null ? "FAILED" : "SUCCESS", restarted == null ? "No process returned" : "Pid=" + restarted.Id);
                        }
                    }
                }
                catch (Exception rollbackError) { Log("ROLLBACK", "FAILED", rollbackError.GetType().Name + ": " + rollbackError.Message); }
                return 2;
            }
        }

        private static void Apply(Manifest manifest)
        {
            foreach (ManifestFile file in manifest.files)
            {
                string sourceRelative = NormalizeRelative(file.path);
                string relative = String.Equals(sourceRelative, "NetCheckMonitor.exe", StringComparison.OrdinalIgnoreCase) ? mainExecutableName : sourceRelative;
                string source = Path.Combine(sourceDirectory, sourceRelative);
                string destination = Path.Combine(targetDirectory, relative);
                string backup = Path.Combine(backupDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (File.Exists(destination)) { File.Copy(destination, backup, true); originallyExisting.Add(relative); }
                replaced.Add(relative);
                string pending = destination + ".update-new-" + Guid.NewGuid().ToString("N");
                File.Copy(source, pending, true);
                try
                {
                    if (File.Exists(destination))
                    {
                        try { File.Replace(pending, destination, null, true); }
                        catch (PlatformNotSupportedException) { File.Copy(pending, destination, true); File.Delete(pending); }
                        catch (IOException) { File.Copy(pending, destination, true); File.Delete(pending); }
                    }
                    else File.Move(pending, destination);
                }
                finally { try { if (File.Exists(pending)) File.Delete(pending); } catch { } }
            }
            string manifestSource = Path.Combine(sourceDirectory, "update-manifest.json");
            string manifestDestination = Path.Combine(targetDirectory, "update-manifest.json");
            if (File.Exists(manifestDestination)) { File.Copy(manifestDestination, Path.Combine(backupDirectory, "update-manifest.json"), true); originallyExisting.Add("update-manifest.json"); }
            replaced.Add("update-manifest.json");
            File.Copy(manifestSource, manifestDestination, true);
        }

        private static void Rollback()
        {
            for (int i = replaced.Count - 1; i >= 0; i--)
            {
                string relative = replaced[i];
                string destination = Path.Combine(targetDirectory, relative);
                string backup = Path.Combine(backupDirectory, relative);
                if (originallyExisting.Contains(relative) && File.Exists(backup)) File.Copy(backup, destination, true);
                else if (File.Exists(destination)) File.Delete(destination);
            }
        }

        private static Manifest LoadAndValidateManifest(string root, string version, string manifestDigest)
        {
            string path = Path.Combine(root, "update-manifest.json");
            if (!File.Exists(path)) throw new InvalidDataException("Update manifest is missing.");
            if (String.IsNullOrWhiteSpace(manifestDigest) || !String.Equals(Sha256(path), manifestDigest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update manifest digest mismatch.");
            Manifest manifest = new JavaScriptSerializer().Deserialize<Manifest>(File.ReadAllText(path, Encoding.UTF8));
            if (manifest == null || manifest.schema != 1 || !String.Equals(manifest.version, version, StringComparison.OrdinalIgnoreCase) || manifest.files == null || manifest.files.Count == 0) throw new InvalidDataException("Invalid update manifest.");
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ManifestFile file in manifest.files)
            {
                string relative = NormalizeRelative(file.path);
                if (!unique.Add(relative)) throw new InvalidDataException("Duplicate update manifest path.");
                string full = Path.Combine(root, relative);
                if (!File.Exists(full) || !String.Equals(Sha256(full), file.sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update payload verification failed: " + relative);
            }
            if (!unique.Contains("NetCheckMonitor.exe") || !unique.Contains("NetCheckUpdater.exe")) throw new InvalidDataException("Required executables are missing from the manifest.");
            return manifest;
        }

        private static void WaitForProcess(int pid, long startedTicks, int timeoutMs)
        {
            Process process;
            try { process = Process.GetProcessById(pid); }
            catch (ArgumentException) { return; }
            using (process)
            {
                if (Math.Abs(process.StartTime.ToUniversalTime().Ticks - startedTicks) > TimeSpan.FromSeconds(2).Ticks) return;
                if (!process.WaitForExit(timeoutMs)) throw new TimeoutException("The previous application instance did not exit in time.");
            }
        }

        private static Dictionary<string, string> ParseArguments(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length) throw new ArgumentException("Invalid updater arguments.");
                result[args[i].Substring(2)] = args[++i];
            }
            return result;
        }
        private static string Required(Dictionary<string, string> options, string name)
        {
            string value;
            if (!options.TryGetValue(name, out value) || String.IsNullOrWhiteSpace(value)) throw new ArgumentException("Missing updater argument: " + name);
            return value;
        }
        private static string RequiredDirectory(Dictionary<string, string> options, string name)
        {
            string path = Path.GetFullPath(Required(options, name));
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
            return path.TrimEnd(Path.DirectorySeparatorChar);
        }
        private static string NormalizeRelative(string path)
        {
            string relative = (path ?? "").Replace('/', Path.DirectorySeparatorChar);
            if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains("..")) throw new InvalidDataException("Unsafe update path.");
            string combined = Path.GetFullPath(Path.Combine(targetDirectory ?? sourceDirectory, relative));
            string root = Path.GetFullPath(targetDirectory ?? sourceDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe update path.");
            return relative;
        }
        private static string Sha256(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (Stream stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static string Quote(string value) { return "\"" + (value ?? "").Replace("\"", "\\\"") + "\""; }
        private static void CleanupBackup() { try { if (!String.IsNullOrWhiteSpace(backupDirectory) && Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, true); } catch { } }
        private static void Log(string stage, string status, string detail)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(logPath)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                bool header = !File.Exists(logPath) || new FileInfo(logPath).Length == 0;
                using (var writer = new StreamWriter(logPath, true, new UTF8Encoding(true)))
                {
                    if (header) writer.WriteLine("Timestamp,Stage,Status,Detail");
                    writer.WriteLine(Csv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + "," + Csv(stage) + "," + Csv(status) + "," + Csv((detail ?? "").Replace('\r', ' ').Replace('\n', ' ')));
                }
            }
            catch { }
        }
        private static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
    }
}
