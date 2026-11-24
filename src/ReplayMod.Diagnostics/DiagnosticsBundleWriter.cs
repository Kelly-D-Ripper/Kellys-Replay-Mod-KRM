using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ReplayMod.Diagnostics;

// Plain managed data only. Captured Unity state must be converted to text before
// it reaches this worker; neither this class nor its input accesses game objects.
public sealed class DiagnosticsBundleInput
{
    public string OutputDirectory;
    public string Snapshot;
    public string RecentWarnings;
    public string ReplayPath;
    public readonly List<DiagnosticsLogSource> Logs = new List<DiagnosticsLogSource>();
    public readonly List<DiagnosticsLogSource> HashFiles = new List<DiagnosticsLogSource>();
    public DiagnosticsRedactor Redactor;
}

public sealed class DiagnosticsLogSource
{
    public string Name;
    public string Path;
    public DiagnosticsLogSource(string name, string path) { Name = name; Path = path; }
}

public sealed class DiagnosticsBundleResult
{
    public string Path;
    public string Notice;
    public int UnavailableSources;
}

public sealed class DiagnosticsRedactor
{
    private readonly KeyValuePair<string,string>[] replacements;
    private readonly string[] personalNames;
    private static readonly Regex assignments = new Regex(
        @"(?im)(\b(?:password|passwd|pwd|token|access[ _-]?token|refresh[ _-]?token|api[ _-]?key|apikey|secret|client[ _-]?secret|authorization|cookie|session[ _-]?id|discord[ _-]?token|join[ _-]?password|server[ _-]?password)\b[""']?\s*[=:]\s*)(?:""(?:\\.|[^""\\\r\n])*""|'(?:\\.|[^'\\\r\n])*'|[^\r\n]+)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex bearer = new Regex(@"(?i)\b(?:Bearer|Basic)\s+[A-Za-z0-9+/=_\-.]+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex credentialUrl = new Regex(@"(?i)(https?://)[^\s/@:]+:[^\s/@]+@", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex discord = new Regex(@"(?i)(?:mfa\.[\w-]{20,}|[\w-]{23,30}\.[\w-]{6}\.[\w-]{25,})", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex knownKeys = new Regex(@"\b(?:sk-[A-Za-z0-9_-]{16,}|gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,})\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex windowsUser = new Regex(@"(?i)[a-z]:[\\/]Users[\\/][^\\/\s<>""']+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex unixUser = new Regex(@"/(?:Users|home)/[^/\s<>""']+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex ip = new Regex(@"(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?(?![\w.])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex ipv6 = new Regex(@"(?<![\w:])(?:[A-Fa-f0-9]{0,4}:){2,8}[A-Fa-f0-9]{0,4}(?:%[A-Za-z0-9_.-]+)?(?![\w:])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex steamId = new Regex(@"\b7656119\d{10}\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex externalWindowsPath = new Regex(@"(?i)[a-z]:[\\/](?:[^\r\n<>""'|;]*[\\/])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    public DiagnosticsRedactor(IEnumerable<KeyValuePair<string,string>> roots, params string[] names)
    {
        replacements = (roots ?? Enumerable.Empty<KeyValuePair<string,string>>()).Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .OrderByDescending(p => p.Key.Length).ToArray();
        personalNames = (names ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n) && n.Length >= 3).Distinct().ToArray();
    }

    public string Scrub(string value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        try
        {
            string result = value;
            foreach (var pair in replacements)
            {
                result = ReplaceOrdinalIgnoreCase(result, pair.Key, pair.Value);
                result = ReplaceOrdinalIgnoreCase(result, pair.Key.Replace('\\','/'), pair.Value);
            }
            result = assignments.Replace(result, "$1[REDACTED]");
            result = bearer.Replace(result, "[REDACTED AUTH]");
            result = credentialUrl.Replace(result, "$1[REDACTED]@");
            result = discord.Replace(result, "[REDACTED TOKEN]");
            result = knownKeys.Replace(result, "[REDACTED TOKEN]");
            result = windowsUser.Replace(result, "<USER>");
            result = unixUser.Replace(result, "<USER>");
            result = externalWindowsPath.Replace(result, "<PATH>/");
            result = ip.Replace(result, "<IP>");
            result = ipv6.Replace(result, match => System.Net.IPAddress.TryParse(match.Value,out var address) && address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetworkV6 ? "<IP>" : match.Value);
            result = steamId.Replace(result, "<PLAYER-ID>");
            foreach (var name in personalNames)
                result = Regex.Replace(result, @"(?i)(?<![\w])" + Regex.Escape(name) + @"(?![\w])", "<PRIVATE>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
            return result;
        }
        catch (RegexMatchTimeoutException) { return "[Entry omitted: redaction time limit reached.]"; }
    }

    private static string ReplaceOrdinalIgnoreCase(string value, string find, string replace)
    {
        int offset = 0, next; var builder = new StringBuilder();
        while ((next = value.IndexOf(find, offset, StringComparison.OrdinalIgnoreCase)) >= 0)
        { builder.Append(value, offset, next-offset).Append(replace); offset = next + find.Length; }
        return offset == 0 ? value : builder.Append(value, offset, value.Length-offset).ToString();
    }
}

public static class DiagnosticsBundleWriter
{
    public const int MaximumLogBytes = 2 * 1024 * 1024;
    public const int MaximumBundleTextBytes = 8 * 1024 * 1024;
    public const int MaximumSnapshotBytes = 128 * 1024;
    public const int MaximumWarningBytes = 512 * 1024;
    private const int MaximumHashBytes = 64 * 1024 * 1024;
    private static readonly Encoding utf8 = new UTF8Encoding(false);

    public static DiagnosticsBundleResult Write(DiagnosticsBundleInput input)
    {
        if (input == null || input.Redactor == null || string.IsNullOrEmpty(input.OutputDirectory)) throw new ArgumentException("Diagnostics input is incomplete.");
        Directory.CreateDirectory(input.OutputDirectory);
        string path = System.IO.Path.Combine(input.OutputDirectory, "ReplayMod-support-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0,8) + ".zip");
        string temp = path + ".tmp";
        var summary = new StringBuilder(); var watch = Stopwatch.StartNew(); int remaining = MaximumBundleTextBytes-64*1024, unavailable = 0;
        summary.AppendLine("ReplayMod support bundle").AppendLine("Created UTC: " + DateTime.UtcNow.ToString("O"))
            .AppendLine("Contains a runtime snapshot, allowlisted ReplayMod settings, plugin versions, replay header/file metadata, assembly hashes and bounded recent logs.")
            .AppendLine("Does not contain replay payloads, camera projects, screenshots or arbitrary configuration files. Nothing is uploaded.")
            .AppendLine("Common credentials, personal profile paths/names and IP addresses are redacted. Review this ZIP before sharing; custom log text may still identify a player.")
            .AppendLine("Log tails are capped at 2 MiB each. Bundle text is capped at 8 MiB. Collection stops after a 15 second worker budget.")
            .AppendLine();
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                WriteText(zip, "runtime.txt", input.Snapshot, input.Redactor, MaximumSnapshotBytes, ref remaining);
                WriteText(zip, "recent-warnings.txt", input.RecentWarnings, input.Redactor, MaximumWarningBytes, ref remaining);
                if (!string.IsNullOrEmpty(input.ReplayPath))
                {
                    try { WriteText(zip, "replay-metadata.txt", ReadReplayMetadata(input.ReplayPath), input.Redactor, 16*1024, ref remaining); summary.AppendLine("Replay metadata: collected (header only)"); }
                    catch (Exception ex) { unavailable++; summary.AppendLine("Replay metadata unavailable: " + ex.GetType().Name + ": " + ex.Message); }
                }
                else summary.AppendLine("Replay metadata: no current replay or recording file");
                foreach (var log in input.Logs.Take(4))
                {
                    if (watch.Elapsed.TotalSeconds >= 15 || remaining < 4096) { unavailable++; summary.AppendLine(log.Name + ": omitted (collection budget reached)" ); continue; }
                    try
                    {
                        bool trimmed; long length; var text = ReadTail(log.Path, MaximumLogBytes, out trimmed, out length);
                        WriteText(zip, "logs/" + SafeEntryName(log.Name), text, input.Redactor, MaximumLogBytes, ref remaining);
                        summary.AppendLine(log.Name + ": collected from " + log.Path + "; source bytes=" + length + "; tail truncated=" + trimmed);
                    }
                    catch (Exception ex) { unavailable++; summary.AppendLine(log.Name + ": unavailable (" + ex.GetType().Name + ": " + ex.Message + ")"); }
                }
                var hashes = new StringBuilder();
                foreach (var file in input.HashFiles.Take(2))
                {
                    if (watch.Elapsed.TotalSeconds >= 15) { unavailable++; summary.AppendLine(file.Name + " hash: omitted (collection budget reached)"); continue; }
                    try { hashes.AppendLine(file.Name + " SHA256=" + HashFile(file.Path, watch)).AppendLine("Path=" + file.Path); }
                    catch (Exception ex) { unavailable++; hashes.AppendLine(file.Name + " hash unavailable: " + ex.GetType().Name + ": " + ex.Message); }
                }
                WriteText(zip, "assembly-hashes.txt", hashes.ToString(), input.Redactor, 16*1024, ref remaining);
                summary.AppendLine().AppendLine("Unavailable or omitted sources: " + unavailable).AppendLine("Worker elapsed milliseconds: " + watch.ElapsedMilliseconds);
                int summaryAllowance = 64*1024;
                WriteText(zip, "README.txt", summary.ToString(), input.Redactor, summaryAllowance, ref summaryAllowance);
            }
            File.Move(temp, path);
            return new DiagnosticsBundleResult {Path=path,UnavailableSources=unavailable,Notice=unavailable==0 ? "Logs exported." : "Logs exported. " + unavailable + " source(s) unavailable; details are in README.txt."};
        }
        catch { try { if (File.Exists(temp)) File.Delete(temp); } catch { } throw; }
    }

    public static string ReadTail(string path, int maximumBytes, out bool truncated, out long sourceLength)
    {
        if (maximumBytes <= 0 || maximumBytes > MaximumLogBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16384, FileOptions.SequentialScan))
        {
            sourceLength = file.Length; int size = (int)Math.Min(sourceLength, maximumBytes);
            truncated = sourceLength > size; file.Position = sourceLength-size;
            var bytes = new byte[size]; int read=0;
            while (read<size) {int count=file.Read(bytes,read,size-read);if(count==0)break;read+=count;}
            string text = utf8.GetString(bytes,0,read);
            if (truncated)
            {
                int end = text.IndexOf('\n');
                // A cut-off credential key must not leave its value as the first
                // line of the exported tail. Drop the incomplete first line.
                text = end<0 ? "[Tail omitted: its first line exceeds the collection limit.]" : text.Substring(end+1);
                text = "[Older log text omitted; recent tail follows.]\n" + text;
            }
            return text;
        }
    }

    private static string ReadReplayMetadata(string path)
    {
        using (var file = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new BinaryReader(file, utf8))
        {
            // Header.Read uses an unbounded BinaryReader.ReadString. Keep this
            // diagnostic read bounded even for an invalid/corrupt replay file.
            int app=reader.ReadInt32();byte major=reader.ReadByte(),minor=reader.ReadByte(),patch=reader.ReadByte(),key=reader.ReadByte();
            if(app!=12549)throw new InvalidDataException("Unknown replay app ID");
            if(major<1||major>ReplayMod.Core.ReplayRecordingHeader.CurrentFormat)throw new InvalidDataException("Unsupported replay format version: "+major);
            int length=0, shift=0; byte part;
            do {if(shift>=35)throw new InvalidDataException("Invalid map-name length");part=reader.ReadByte();length|=(part&127)<<shift;shift+=7;}while((part&128)!=0);
            if(length<0||length>4096)throw new InvalidDataException("Replay map name exceeds the header limit");
            byte[] map=reader.ReadBytes(length);if(map.Length!=length)throw new EndOfStreamException();
            long ticks=reader.ReadInt64();int reserved=reader.ReadInt32();double duration=reader.ReadDouble();long events=reader.ReadInt64();
            string provenance="";
            if(major>=6)
            {
                file.Position=0;var header=ReplayMod.Core.ReplayRecordingHeader.Read(reader,false);
                provenance="\nRecordingCommitted="+header.Complete+"\nEventTransport=length+CRC32\nRecordingProvenance="+header.Metadata;
            }
            return "Filename=" + System.IO.Path.GetFileName(path) + "\nPath=" + path + "\nSizeBytes=" + file.Length + "\nLastWriteUTC=" + File.GetLastWriteTimeUtc(path).ToString("O")+
                "\nAppId="+app+"\nReplayFormatVersion="+major+"."+minor+"."+patch+"\nFormatMajor="+major+"\nKeyType="+key+"\nMap="+utf8.GetString(map)+"\nRecordingDateTicks="+ticks+
                "\nReservedHeaderValue="+reserved+"\nDurationSeconds="+duration.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"\nEventCount="+events+"\nHeaderBytesRead="+file.Position+provenance+"\nRecording payload excluded.\n";
        }
    }

    private static string HashFile(string path, Stopwatch watch)
    {
        using (var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete,65536))
        using (var sha=SHA256.Create())
        {
            long remaining=file.Length;if(remaining>MaximumHashBytes)throw new InvalidDataException("Assembly exceeds the 64 MiB hash limit");
            byte[] buffer=new byte[65536];
            while(remaining>0)
            {
                if(watch.Elapsed.TotalSeconds>=15)throw new TimeoutException("Collection budget reached");
                int count=file.Read(buffer,0,(int)Math.Min(remaining,buffer.Length));if(count==0)throw new EndOfStreamException();
                sha.TransformBlock(buffer,0,count,buffer,0);remaining-=count;
            }
            sha.TransformFinalBlock(Array.Empty<byte>(),0,0);return BitConverter.ToString(sha.Hash).Replace("-","");
        }
    }

    private static string SafeEntryName(string name) => Regex.Replace(name ?? "log.txt", @"[^A-Za-z0-9_.-]", "_");
    private static void WriteText(ZipArchive zip,string name,string text,DiagnosticsRedactor redactor,int maximumBytes,ref int remaining)
    {
        // Cap input before regular expressions, then cap the UTF-8 output again.
        text=text??"";int firstLimit=Math.Min(maximumBytes,remaining);
        if(utf8.GetByteCount(text)>firstLimit)
        {
            text=LimitUtf8(text,firstLimit);
            int noticeEnd=text.IndexOf('\n'),partialEnd=noticeEnd<0?-1:text.IndexOf('\n',noticeEnd+1);
            // Only complete lines enter the sanitizer after an input truncation;
            // this prevents a cut-off secret assignment from losing its key.
            text=partialEnd<0?"[Text omitted: a line exceeds the collection limit.]":text.Substring(0,noticeEnd+1)+text.Substring(partialEnd+1);
        }
        text=redactor.Scrub(text);
        text=LimitUtf8(text,Math.Min(maximumBytes,remaining));byte[] bytes=utf8.GetBytes(text);remaining-=bytes.Length;
        using(var target=zip.CreateEntry(name,CompressionLevel.Fastest).Open())target.Write(bytes,0,bytes.Length);
    }

    public static string LimitUtf8(string text,int maximumBytes)
    {
        if(maximumBytes<=0)return "";
        if(utf8.GetByteCount(text)<=maximumBytes)return text;
        const string notice="[Text truncated to collection limit.]\n";
        int capacity=Math.Max(0,maximumBytes-utf8.GetByteCount(notice));int low=0,high=Math.Min(text.Length,capacity);
        while(low<high){int mid=(low+high+1)/2;if(utf8.GetByteCount(text.Substring(text.Length-mid))<=capacity)low=mid;else high=mid-1;}
        int start=text.Length-low;if(start<text.Length&&char.IsLowSurrogate(text[start]))start++;
        return (maximumBytes>=utf8.GetByteCount(notice)?notice:"")+text.Substring(start);
    }
}
