using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace MafiaAz {
    public class Asset { public string path; public string sha256; }
    public class PatchFile {
        public string path, originalSha256, previousPatchedSha256, rawOriginalSha256, rawTargetSha256, installedSha256;
        public int originalBytes, rawTargetBytes;
        public Asset prefix, trailer, delta;
    }
    public class Manifest { public int format, entries; public string version, game, language, baseLanguage; public PatchFile[] files; }
    public sealed class Package : IDisposable {
        readonly Stream stream; readonly ZipArchive zip;
        public readonly Manifest Manifest;
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16000000 };
        public Package() {
            stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MafiaAz.Payload");
            if (stream == null) throw new InvalidDataException("Installer data is missing.");
            zip = new ZipArchive(stream, ZipArchiveMode.Read);
            using (var r = new StreamReader(zip.GetEntry("manifest.json").Open(), Encoding.UTF8)) Manifest = Json.Deserialize<Manifest>(r.ReadToEnd());
            if (Manifest.format != 1 || Manifest.files == null || Manifest.files.Length != 304) throw new InvalidDataException("Unsupported patch manifest.");
            if (Manifest.files.Select(f => f.path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Manifest.files.Length) throw new InvalidDataException("Duplicate patch paths.");
        }
        public byte[] Read(Asset a) {
            if (a == null || a.path != "assets/" + a.sha256 + ".bin") throw new InvalidDataException("Invalid payload asset.");
            var entry = zip.GetEntry(a.path);
            if (entry == null || entry.Length > 100000000) throw new InvalidDataException("Patch data is missing or too large.");
            byte[] b; using (var input = entry.Open()) using (var output = new MemoryStream()) { input.CopyTo(output); b = output.ToArray(); }
            if (Util.Hash(b) != a.sha256) throw new InvalidDataException("Patch data checksum mismatch.");
            return b;
        }
        public byte[] Rebuild(byte[] original, PatchFile f) {
            if (Util.Hash(original) != f.originalSha256) throw new InvalidDataException("Unsupported original file: " + f.path);
            var raw = Sds.Decode(original);
            if (Util.Hash(raw) != f.rawOriginalSha256) throw new InvalidDataException("Original archive checksum mismatch: " + f.path);
            byte[] target = Sds.ApplyDelta(raw, Read(f.delta));
            if (target.Length != f.rawTargetBytes || Util.Hash(target) != f.rawTargetSha256) throw new InvalidDataException("Rebuilt text checksum mismatch: " + f.path);
            var result = Sds.Encode(Read(f.prefix), target, Read(f.trailer));
            if (!String.IsNullOrEmpty(f.installedSha256) && Util.Hash(result) != f.installedSha256) throw new InvalidDataException("Rebuilt archive checksum mismatch: " + f.path);
            return result;
        }
        public bool IsPatched(string hash, PatchFile f) { return hash == f.previousPatchedSha256 || (!String.IsNullOrEmpty(f.installedSha256) && hash == f.installedSha256); }
        public void Dispose() { zip.Dispose(); stream.Dispose(); }
    }
    public static class Util {
        public static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        public static string FileHash(string file) { using (var sha = SHA256.Create()) using (var s = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
        public static string Root(string selected) {
            if (String.IsNullOrWhiteSpace(selected)) throw new IOException("Select mafia2.exe or the Mafia II game folder first.");
            var root = Path.GetFullPath(selected.Trim().Trim('"'));
            if (File.Exists(root)) {
                if (!Path.GetFileName(root).Equals("mafia2.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Select the game's mafia2.exe file.");
                root = Path.GetDirectoryName(root);
            }
            if (File.Exists(Path.Combine(root, "mafia2.exe")) && Path.GetFileName(root).Equals("pc", StringComparison.OrdinalIgnoreCase)) root = Path.GetDirectoryName(root);
            if (!File.Exists(Path.Combine(root, "pc", "mafia2.exe"))) throw new IOException("Mafia II Classic was not found. Select mafia2.exe in the game's pc folder, or its parent game folder.");
            return root.TrimEnd(Path.DirectorySeparatorChar);
        }
        public static string Inside(string root, string relative) {
            if (Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(p => p == ".." || p == "." || p == "" || p.Contains(":"))) throw new IOException("Unsafe patch path.");
            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Patch target is outside the selected folder.");
            for (var candidate = full; !candidate.Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase); candidate = Path.GetDirectoryName(candidate))
                if ((File.Exists(candidate) || Directory.Exists(candidate)) && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0) throw new IOException("Patch path contains a junction or symbolic link: " + candidate);
            return full;
        }
        public static string GameFile(string root, PatchFile f) {
            if (!(f.path.StartsWith("pc/sds_en/", StringComparison.Ordinal) || f.path.StartsWith("pc/sds/", StringComparison.Ordinal)) || !f.path.EndsWith(".sds", StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected game archive path.");
            var full = Inside(root, f.path);
            for (var dir = Path.GetDirectoryName(full); !dir.Equals(root, StringComparison.OrdinalIgnoreCase); dir = Path.GetDirectoryName(dir))
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) throw new IOException("Patch target contains a junction or symbolic link: " + dir);
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new IOException("Archive is a symbolic link: " + f.path);
            return full;
        }
        public static void Closed(string root) {
            foreach (var process in Process.GetProcessesByName("mafia2")) using (process) {
                try { if (process.MainModule.FileName.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Close Mafia II before installing or restoring the patch."); }
                catch (System.ComponentModel.Win32Exception) { throw new IOException("Mafia II is running. Close it before continuing."); }
            }
        }
        public static void AtomicWrite(string target, byte[] bytes) {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            var temp = Path.Combine(Path.GetDirectoryName(target), "az-" + Guid.NewGuid().ToString("N").Substring(0,12) + ".tmp");
            try { File.WriteAllBytes(temp, bytes); if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public static class Sds {
        static int Int(byte[] b, int p) { if (p < 0 || p > b.Length - 4) throw new InvalidDataException("Truncated archive."); return checked((int)BitConverter.ToUInt32(b, p)); }
        static byte[] Part(byte[] b, int p, int n) { if (p < 0 || n < 0 || p > b.Length - n) throw new InvalidDataException("Archive range is invalid."); var r = new byte[n]; Buffer.BlockCopy(b, p, r, 0, n); return r; }
        static uint Fnv(byte[] b, int p, int n) { uint h = 2166136261; unchecked { for (int i = p; i < p + n; i++) h = (h * 16777619) ^ b[i]; } return h; }
        static uint Adler(byte[] b) { uint a = 1, c = 0; foreach (byte v in b) { a = (a + v) % 65521; c = (c + a) % 65521; } return (c << 16) | a; }
        static void Check(byte[] b, int p, int len) { if (Fnv(b, p, len) != BitConverter.ToUInt32(b, p + len)) throw new InvalidDataException("SDS header checksum mismatch."); }
        static byte[] Inflate(byte[] z) {
            if (z.Length < 6 || (z[0] & 15) != 8 || ((z[0] << 8) + z[1]) % 31 != 0 || (z[1] & 32) != 0) throw new InvalidDataException("Unsupported zlib stream.");
            using (var source = new MemoryStream(z, 2, z.Length - 6)) using (var d = new DeflateStream(source, CompressionMode.Decompress)) using (var output = new MemoryStream()) {
                d.CopyTo(output); var bytes = output.ToArray(); var n = z.Length;
                uint checksum = ((uint)z[n-4] << 24) | ((uint)z[n-3] << 16) | ((uint)z[n-2] << 8) | z[n-1];
                if (Adler(bytes) != checksum) throw new InvalidDataException("Zlib checksum mismatch."); return bytes;
            }
        }
        public static byte[] Decode(byte[] b) {
            if (b.Length < 72 || Encoding.ASCII.GetString(b, 0, 4) != "SDS\0" || Int(b, 4) != 19 || Encoding.ASCII.GetString(b, 8, 4) != "PC\0\0") throw new InvalidDataException("Only Mafia II Classic PC v19 archives are supported.");
            Check(b, 0, 12); Check(b, 16, 52); int p = Int(b, 20), end = Int(b, 24);
            if (Int(b, p) != 1819952469 || b[p+8] != 4 || end > b.Length) throw new InvalidDataException("Invalid SDS blocks."); p += 9;
            using (var raw = new MemoryStream()) {
                while (p < end) { int size = Int(b, p); byte type = b[p+4]; p += 5; if (size == 0) break;
                    if (p > end - size) throw new InvalidDataException("Truncated SDS block.");
                    if (type == 0) raw.Write(b, p, size);
                    else if (type == 1) { int outSize = Int(b, p), headerSize = Int(b, p+4), count = BitConverter.ToUInt16(b, p+10), cp = p + headerSize; long before = raw.Length;
                        if (headerSize != 32 || count < 1 || count > 8) throw new InvalidDataException("Unsupported SDS compression.");
                        for (int j = 0; j < count; j++) { int len = BitConverter.ToUInt16(b, p+16+j*2); byte[] decoded = Inflate(Part(b, cp, len)); raw.Write(decoded, 0, decoded.Length); cp += len; }
                        if (cp != p+size || raw.Length - before != outSize) throw new InvalidDataException("SDS block size mismatch.");
                    } else throw new InvalidDataException("Unknown SDS block."); p += size;
                }
                byte[] bytes = raw.ToArray(); int rp = 0, resources = Int(b, 64);
                for (int i = 0; i < resources; i++) { int size = Int(bytes, rp+4); if (size < 30 || rp > bytes.Length-size) throw new InvalidDataException("Invalid SDS resource."); Check(bytes, rp, 26); rp += size; }
                if (rp != bytes.Length) throw new InvalidDataException("SDS resource count mismatch."); return bytes;
            }
        }
        public static byte[] ApplyDelta(byte[] source, byte[] delta) {
            if (delta.Length < 16 || Encoding.ASCII.GetString(delta, 0, 4) != "AZD1" || Int(delta, 4) != source.Length) throw new InvalidDataException("Invalid patch delta.");
            int expected = Int(delta, 8), count = Int(delta, 12), p = 16;
            if (expected > 300000000 || count > 10000000) throw new InvalidDataException("Patch delta is too large.");
            using (var result = new MemoryStream(expected)) { for (int i = 0; i < count; i++) {
                if (p >= delta.Length) throw new InvalidDataException("Truncated patch delta."); byte op = delta[p++];
                if (op == 0) { int len = Int(delta, p); p += 4; if (len > delta.Length-p) throw new InvalidDataException("Truncated literal."); result.Write(delta, p, len); p += len; }
                else if (op == 1) { int offset = Int(delta, p), len = Int(delta, p+4); p += 8; if (offset > source.Length-len) throw new InvalidDataException("Invalid copy range."); result.Write(source, offset, len); }
                else throw new InvalidDataException("Unknown delta instruction.");
                if (result.Length > expected) throw new InvalidDataException("Delta output is too long.");
            } if (p != delta.Length || result.Length != expected) throw new InvalidDataException("Delta output size mismatch."); return result.ToArray(); }
        }
        public static byte[] Encode(byte[] prefix, byte[] raw, byte[] trailer) {
            prefix = (byte[])prefix.Clone(); if (prefix.Length != Int(prefix, 20)) throw new InvalidDataException("Invalid target prefix.");
            using (var m = new MemoryStream()) using (var w = new BinaryWriter(m)) {
                w.Write(prefix); w.Write(1819952469); w.Write(16384); w.Write((byte)4);
                // Native SDS uncompressed blocks make output byte-identical on every supported Windows/.NET version.
                for (int p = 0; p < raw.Length; p += 16384) { int length = Math.Min(16384, raw.Length-p); w.Write(length); w.Write((byte)0); w.Write(raw,p,length); }
                w.Write(0); w.Write((byte)0); int xmlOffset = checked((int)m.Position); w.Write(trailer); byte[] result = m.ToArray();
                Buffer.BlockCopy(BitConverter.GetBytes(xmlOffset),0,result,24,4); Buffer.BlockCopy(BitConverter.GetBytes(Fnv(result,16,52)),0,result,68,4); return result;
            }
        }
    }
    public sealed class PatchEngine {
        readonly Package package; readonly Action<int, string> progress;
        string Backup(string root) { return Util.Inside(root, "MafiaII.Azerbaijani.Backup/" + package.Manifest.version); }
        string BackupFile(string root, PatchFile f) { return Util.Inside(Backup(root), "original/" + f.path); }
        public PatchEngine(Package p, Action<int, string> report) { package = p; progress = report; }
        void Notify(int percent, string text) { if (progress != null) progress(percent, text); }
        Dictionary<string, string> Preflight(string root, bool restore) {
            Util.Closed(root); var current = new Dictionary<string, string>(); int index = 0;
            foreach (var f in package.Manifest.files) {
                string target = Util.GameFile(root, f); if (!File.Exists(target)) throw new IOException("Required game file is missing: " + f.path);
                string futureStage=Util.Inside(root,".az-000000000000/"+f.path), futureBackup=BackupFile(root,f);
                if(new[]{target,futureStage,futureBackup}.Any(p=>p.Length>=260 || Path.GetDirectoryName(p).Length>237)) throw new IOException("The game folder path is too long for this Windows installer. Use a shorter location such as C:\\Games\\Mafia II. No game files were changed.");
                string hash = Util.FileHash(target); current.Add(f.path, hash);
                if (hash != f.originalSha256 && !package.IsPatched(hash, f)) throw new IOException("This game build or an existing mod is incompatible: " + f.path + ". No files were changed.");
                if ((File.GetAttributes(target) & FileAttributes.ReadOnly) != 0) throw new IOException("Game file is read-only: " + f.path);
                if (restore || hash != f.originalSha256) {
                    string backup = BackupFile(root, f);
                    if (!File.Exists(backup) || Util.FileHash(backup) != f.originalSha256) throw new IOException("An Azerbaijani patch is already present, but this installer's original backup is missing. Restore the original English files using the previous patch's restore tool first: " + f.path);
                } else if (File.Exists(BackupFile(root, f)) && Util.FileHash(BackupFile(root, f)) != f.originalSha256) throw new IOException("An existing original backup failed verification: " + f.path);
                Notify(++index*25/package.Manifest.files.Length, "Checking " + f.path);
            }
            return current;
        }
        public void Install(string selected) {
            string root = Util.Root(selected); var current = Preflight(root, false);
            if (current.All(kv => package.IsPatched(kv.Value, package.Manifest.files.First(f => f.path == kv.Key)))) { Notify(100, "The patch is already installed. Original backups are verified."); return; }
            long required = package.Manifest.files.Sum(f => (long)f.originalBytes + f.rawTargetBytes) + 100000000;
            var drive = new DriveInfo(Path.GetPathRoot(root)); if (drive.AvailableFreeSpace < required) throw new IOException("Not enough free disk space. Free at least 1 GB and try again.");
            string stage = Util.Inside(root, ".az-" + Guid.NewGuid().ToString("N").Substring(0,12)); Directory.CreateDirectory(stage); var committed = new List<PatchFile>();
            try {
                int index = 0;
                foreach (var f in package.Manifest.files) { string source = current[f.path] == f.originalSha256 ? Util.GameFile(root,f) : BackupFile(root,f); var result = package.Rebuild(File.ReadAllBytes(source), f); string staged = Util.Inside(stage,f.path); Directory.CreateDirectory(Path.GetDirectoryName(staged)); File.WriteAllBytes(staged,result); Notify(25 + ++index*40/package.Manifest.files.Length, "Preparing " + f.path); }
                index = 0;
                foreach (var f in package.Manifest.files) { string backup = BackupFile(root,f); if (!File.Exists(backup)) Util.AtomicWrite(backup, File.ReadAllBytes(Util.GameFile(root,f))); if (Util.FileHash(backup) != f.originalSha256) throw new IOException("Backup verification failed: " + f.path); Notify(65 + ++index*15/package.Manifest.files.Length, "Backing up " + f.path); }
                // Recheck immediately before committing. No game archive has been changed up to this point.
                Util.Closed(root); foreach (var f in package.Manifest.files) if (Util.FileHash(Util.GameFile(root,f)) != current[f.path]) throw new IOException("A game file changed while the patch was prepared. Close game/mod tools and try again.");
                Directory.CreateDirectory(Backup(root)); File.WriteAllText(Path.Combine(Backup(root),"install-state.json"), Package.Json.Serialize(new {version=package.Manifest.version, status="committing"}), Encoding.UTF8);
                index = 0;
                foreach (var f in package.Manifest.files) { var target=Util.GameFile(root,f); Util.AtomicWrite(target,File.ReadAllBytes(Util.Inside(stage,f.path))); committed.Add(f); if (!String.IsNullOrEmpty(f.installedSha256) && Util.FileHash(target) != f.installedSha256) throw new IOException("Installed file verification failed: " + f.path); Notify(80 + ++index*20/package.Manifest.files.Length,"Installing " + f.path); }
                File.WriteAllText(Path.Combine(Backup(root),"install-state.json"),Package.Json.Serialize(new {version=package.Manifest.version,status="installed"}),Encoding.UTF8);
                Notify(100,"Installed. Keep the game's language set to English. Original backups are ready for Restore.");
            } catch {
                foreach (var f in committed.AsEnumerable().Reverse()) { try { Util.AtomicWrite(Util.GameFile(root,f),File.ReadAllBytes(BackupFile(root,f))); } catch { /* Keep all verified backups for recovery through Restore. */ } }
                throw;
            } finally { if (Directory.Exists(stage)) Directory.Delete(stage,true); }
        }
        public void Restore(string selected) {
            string root=Util.Root(selected); var current=Preflight(root,true); Util.Closed(root); int index=0;
            foreach (var f in package.Manifest.files) { string target=Util.GameFile(root,f); if(Util.FileHash(target)!=current[f.path])throw new IOException("A game archive changed during restore: "+f.path); Util.AtomicWrite(target,File.ReadAllBytes(BackupFile(root,f))); if(Util.FileHash(target)!=f.originalSha256)throw new IOException("Restore verification failed: "+f.path); Notify(25 + ++index*75/package.Manifest.files.Length,"Restoring "+f.path); }
            File.WriteAllText(Path.Combine(Backup(root),"install-state.json"),Package.Json.Serialize(new {version=package.Manifest.version,status="restored"}),Encoding.UTF8); Notify(100,"Original English files restored. Backups have been kept.");
        }
        public void Verify(string selected) { Preflight(Util.Root(selected), false); Notify(100,"All required game files are compatible."); }
    }
}
