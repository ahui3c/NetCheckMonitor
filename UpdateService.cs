using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace NetCheck
{
    internal sealed class UpdatePackage
    {
        public string Tag;
        public string Version;
        public string ReleaseUrl;
        public string UpdateRoot;
        public string ExtractDirectory;
        public string PackagePath;
        public string Digest;
    }

    internal sealed class UpdateCheckResult
    {
        public string LatestTag;
        public string ReleaseUrl;
        public bool IsNewer;
        public UpdatePackage Package;
    }

    internal sealed class GitHubReleaseAsset
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
        public long size { get; set; }
        public string digest { get; set; }
        public string state { get; set; }
    }

    internal sealed class GitHubReleaseInfo
    {
        public string tag_name { get; set; }
        public string html_url { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public List<GitHubReleaseAsset> assets { get; set; }
    }

    internal sealed class UpdateManifestFile
    {
        public string path { get; set; }
        public string sha256 { get; set; }
    }

    internal sealed class UpdateManifest
    {
        public int schema { get; set; }
        public string version { get; set; }
        public List<UpdateManifestFile> files { get; set; }
    }

    internal static class UpdateService
    {
        internal const string LatestReleaseApiUrl = "https://api.github.com/repos/ahui3c/NetCheckMonitor/releases/latest";
        internal const string PackageAssetName = "NetCheckMonitor-Portable.zip";
        internal const string ManifestName = "update-manifest.json";
        internal const long MaximumPackageBytes = 100L * 1024L * 1024L;

        internal static UpdateCheckResult CheckAndPrepare(string currentVersion, Action<int> reportProgress)
        {
            GitHubReleaseInfo release = ReadLatestReleaseInfo();
            var result = new UpdateCheckResult { LatestTag = release.tag_name, ReleaseUrl = release.html_url, IsNewer = IsNewerVersion(release.tag_name, currentVersion) };
            if (!result.IsNewer) return result;
            if (release.draft || release.prerelease) throw new InvalidDataException(L.T("最新版本不是正式公開版本。", "The latest release is not a public production release."));

            GitHubReleaseAsset asset = null;
            foreach (GitHubReleaseAsset candidate in release.assets ?? new List<GitHubReleaseAsset>())
                if (String.Equals(candidate.name, PackageAssetName, StringComparison.Ordinal) && String.Equals(candidate.state, "uploaded", StringComparison.OrdinalIgnoreCase)) { asset = candidate; break; }
            if (asset == null || String.IsNullOrWhiteSpace(asset.browser_download_url)) throw new InvalidDataException(L.T("Release 中找不到正式可攜版 ZIP。", "The release does not contain the portable ZIP asset."));
            if (asset.size <= 0 || asset.size > MaximumPackageBytes) throw new InvalidDataException(L.T("更新檔案大小不合理。", "The update package size is invalid."));
            if (String.IsNullOrWhiteSpace(asset.digest) || !asset.digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L.T("Release 沒有提供 SHA-256 驗證值。", "The release does not provide a SHA-256 digest."));

            EnsureInstallDirectoryWritable();
            CleanupOldUpdateDirectories();
            string updateRoot = Path.Combine(UpdateCacheRoot(), SafeTag(release.tag_name) + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(updateRoot);
            string packagePath = Path.Combine(updateRoot, PackageAssetName);
            Download(asset.browser_download_url, packagePath, asset.size, reportProgress);
            string expectedHash = asset.digest.Substring(asset.digest.IndexOf(':') + 1).Trim();
            string actualHash = Sha256(packagePath);
            if (!String.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L.T("更新 ZIP 的 SHA-256 驗證失敗。", "The update ZIP failed SHA-256 verification."));

            result.Package = ValidateDownloadedPackage(packagePath, asset.digest, release.tag_name, release.html_url, updateRoot);
            Record("VERIFY", "SUCCESS", "Version=" + result.Package.Version + ";Digest=" + result.Package.Digest);
            return result;
        }

        internal static GitHubReleaseInfo ReadLatestReleaseInfo()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create(LatestReleaseApiUrl);
            request.Method = "GET";
            request.UserAgent = "NetCheckMonitor/" + AboutForm.AppVersion;
            request.Accept = "application/vnd.github+json";
            request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                var release = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.Deserialize<GitHubReleaseInfo>(reader.ReadToEnd());
                if (release == null || String.IsNullOrWhiteSpace(release.tag_name)) throw new InvalidDataException(L.T("GitHub 回傳資料中沒有版本號。", "The GitHub response did not contain a version tag."));
                return release;
            }
        }

        internal static UpdatePackage ValidateDownloadedPackage(string packagePath, string digest, string tag, string releaseUrl, string updateRoot)
        {
            string expectedHash = (digest ?? "").Trim();
            if (expectedHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) expectedHash = expectedHash.Substring(7);
            if (expectedHash.Length != 64 || !String.Equals(expectedHash, Sha256(packagePath), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package digest mismatch.");
            string version = NormalizeVersion(tag);
            if (String.IsNullOrWhiteSpace(version)) throw new InvalidDataException("Invalid release version.");
            string extractDirectory = Path.Combine(updateRoot, "payload");
            ExtractSecurely(packagePath, extractDirectory);
            string manifestPath = Path.Combine(extractDirectory, ManifestName);
            if (!File.Exists(manifestPath)) throw new InvalidDataException("Update manifest is missing.");
            var manifest = new JavaScriptSerializer().Deserialize<UpdateManifest>(File.ReadAllText(manifestPath, Encoding.UTF8));
            ValidateManifest(manifest, extractDirectory, version);

            string mainPath = Path.Combine(extractDirectory, "NetCheckMonitor.exe");
            string updaterPath = Path.Combine(extractDirectory, "NetCheckUpdater.exe");
            if (!File.Exists(mainPath) || !File.Exists(updaterPath)) throw new InvalidDataException("Required update executables are missing.");
            Version assemblyVersion = AssemblyName.GetAssemblyName(mainPath).Version;
            Version releaseVersion;
            if (!Version.TryParse(version, out releaseVersion) || assemblyVersion.Major != releaseVersion.Major || assemblyVersion.Minor != releaseVersion.Minor || assemblyVersion.Build != releaseVersion.Build)
                throw new InvalidDataException("Executable version does not match the release tag.");
            string product = FileVersionInfo.GetVersionInfo(mainPath).ProductName;
            if (!String.Equals(product, "NetCheckMonitor", StringComparison.Ordinal)) throw new InvalidDataException("Unexpected executable product name.");
            return new UpdatePackage { Tag = tag, Version = version, ReleaseUrl = releaseUrl, UpdateRoot = updateRoot, ExtractDirectory = extractDirectory, PackagePath = packagePath, Digest = "sha256:" + expectedHash.ToLowerInvariant() };
        }

        internal static bool IsNewerVersion(string tag, string currentVersion)
        {
            Version latest, current;
            return Version.TryParse(NormalizeVersion(tag), out latest) && Version.TryParse(currentVersion, out current) && latest > current;
        }

        internal static string UpdateLogPath()
        {
            string appDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string directory = Path.Combine(appDirectory, "NetCheck_Data");
            try { Directory.CreateDirectory(directory); }
            catch { directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Data"); Directory.CreateDirectory(directory); }
            return Path.Combine(directory, "NetCheck_Update.csv");
        }

        internal static void Record(string stage, string status, string detail)
        {
            try
            {
                string path = UpdateLogPath();
                bool header = !File.Exists(path) || new FileInfo(path).Length == 0;
                using (var writer = new StreamWriter(path, true, new UTF8Encoding(true)))
                {
                    if (header) writer.WriteLine("Timestamp,Stage,Status,Detail");
                    writer.WriteLine(Csv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + "," + Csv(stage) + "," + Csv(status) + "," + Csv(SingleLine(detail)));
                }
            }
            catch { }
        }

        private static void Download(string url, string destination, long expectedSize, Action<int> reportProgress)
        {
            Record("DOWNLOAD", "STARTED", "Size=" + expectedSize);
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = "NetCheckMonitor/" + AboutForm.AppVersion;
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            request.AllowAutoRedirect = true;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream input = response.GetResponseStream())
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                long total = 0;
                byte[] buffer = new byte[81920];
                int read;
                int previous = -1;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > MaximumPackageBytes) throw new InvalidDataException("Update package exceeded the size limit.");
                    output.Write(buffer, 0, read);
                    int progress = expectedSize > 0 ? (int)Math.Min(100, total * 100L / expectedSize) : 0;
                    if (progress != previous && reportProgress != null) { previous = progress; reportProgress(progress); }
                }
                output.Flush(true);
                if (expectedSize > 0 && total != expectedSize) throw new InvalidDataException("Downloaded size does not match the release asset.");
            }
            Record("DOWNLOAD", "SUCCESS", "Size=" + expectedSize);
        }

        private static void ExtractSecurely(string packagePath, string destination)
        {
            Directory.CreateDirectory(destination);
            string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                if (archive.Entries.Count > 32) throw new InvalidDataException("Update ZIP contains too many entries.");
                long expandedBytes = 0;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (String.IsNullOrWhiteSpace(entry.FullName)) continue;
                    expandedBytes += entry.Length;
                    if (entry.Length > MaximumPackageBytes || expandedBytes > MaximumPackageBytes * 2) throw new InvalidDataException("Update ZIP expands beyond the safety limit.");
                    string target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe path in update ZIP.");
                    if (String.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream input = entry.Open())
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                }
            }
        }

        private static void ValidateManifest(UpdateManifest manifest, string root, string version)
        {
            if (manifest == null || manifest.schema != 1 || !String.Equals(manifest.version, version, StringComparison.OrdinalIgnoreCase) || manifest.files == null) throw new InvalidDataException("Invalid update manifest.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UpdateManifestFile file in manifest.files)
            {
                if (file == null || String.IsNullOrWhiteSpace(file.path) || String.IsNullOrWhiteSpace(file.sha256)) throw new InvalidDataException("Invalid update manifest entry.");
                string normalized = file.path.Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(normalized) || normalized.Contains("..") || !paths.Add(normalized)) throw new InvalidDataException("Unsafe update manifest path.");
                string full = Path.GetFullPath(Path.Combine(root, normalized));
                string rootPrefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) throw new InvalidDataException("Update manifest file is missing.");
                if (!String.Equals(file.sha256, Sha256(full), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update payload hash mismatch: " + normalized);
            }
            if (!paths.Contains("NetCheckMonitor.exe") || !paths.Contains("NetCheckUpdater.exe")) throw new InvalidDataException("Update manifest does not contain required executables.");
        }

        private static void EnsureInstallDirectoryWritable()
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string test = Path.Combine(directory, ".netcheck-update-write-" + Guid.NewGuid().ToString("N") + ".tmp");
            try { File.WriteAllText(test, "write-test", Encoding.ASCII); }
            catch (Exception ex) { throw new UnauthorizedAccessException(L.T("目前程式資料夾無法寫入，不能自動更新：", "The application folder is not writable, so automatic update cannot continue: ") + ex.Message, ex); }
            finally { try { if (File.Exists(test)) File.Delete(test); } catch { } }
        }

        private static string UpdateCacheRoot() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetCheck", "Updates"); }
        private static void CleanupOldUpdateDirectories()
        {
            try
            {
                string root = UpdateCacheRoot();
                if (!Directory.Exists(root)) return;
                foreach (string directory in Directory.GetDirectories(root))
                    try { if (Directory.GetCreationTimeUtc(directory) < DateTime.UtcNow.AddDays(-7)) Directory.Delete(directory, true); } catch { }
            }
            catch { }
        }
        private static string NormalizeVersion(string tag)
        {
            string value = (tag ?? "").Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            int suffix = value.IndexOf('-');
            if (suffix >= 0) value = value.Substring(0, suffix);
            Version parsed;
            return Version.TryParse(value, out parsed) ? parsed.ToString() : null;
        }
        private static string SafeTag(string tag)
        {
            var builder = new StringBuilder();
            foreach (char c in tag ?? "update") builder.Append(Char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_');
            return builder.ToString();
        }
        internal static string Sha256(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (Stream stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
        private static string SingleLine(string value)
        {
            string text = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length > 1000 ? text.Substring(0, 1000) : text;
        }
    }

    internal static class UpdateStartup
    {
        internal static bool ResumeAfterUpdate { get; private set; }
        internal static string HealthFile { get; private set; }

        internal static void Configure(string[] args)
        {
            for (int i = 0; i < (args ?? new string[0]).Length; i++)
            {
                if (String.Equals(args[i], "--resume-update", StringComparison.OrdinalIgnoreCase)) ResumeAfterUpdate = true;
                else if (String.Equals(args[i], "--update-health-file", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) HealthFile = args[++i];
            }
        }

        internal static void SignalHealthy()
        {
            if (String.IsNullOrWhiteSpace(HealthFile)) return;
            try { File.WriteAllText(HealthFile, AboutForm.AppVersion, new UTF8Encoding(false)); }
            catch { }
        }
    }
}
