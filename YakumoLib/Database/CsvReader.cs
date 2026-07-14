using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using YakumoLib.Assets;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace YakumoLib.Database
{
    public sealed class CsvReader
    {
        private static bool IsPatchFile(string filePath) =>
                Path.GetFileName(filePath).StartsWith("@patch_", StringComparison.OrdinalIgnoreCase);

        public void LoadAssetInfo(
            Dictionary<UUID, AssetEntry> assetsById,
        string assetsDirectory,
        IProgress<(string fileName, int done, int total, int size)>? progress = null)
        {
            var csvFiles = Directory.EnumerateFiles(assetsDirectory, "*.csv", SearchOption.TopDirectoryOnly).ToArray();
            var perFileResults = new ConcurrentBag<Dictionary<UUID, CsvAssetLine>>();

            var baseFiles = csvFiles.Where(f => !IsPatchFile(f)).ToArray();
            var patchFiles = csvFiles.Where(f => IsPatchFile(f)).ToArray();



            /*foreach (var localResults in perFileResults)
            {
                foreach (var (id, csvLine) in localResults)
                {
                    if (assetsById.TryGetValue(id, out var entry))
                    {
                        assetsById[id] = entry with
                        {
                            Size = csvLine.Size,
                            SubEntries = csvLine.SubEntries
                        };
                    }
                }
            }*/

            var baseResults = ParseCSVThreaded(baseFiles, progress, totalCount: csvFiles.Length, doneOffset: 0);
            var patchResults = ParseCSVThreaded(patchFiles, progress, totalCount: csvFiles.Length, doneOffset: baseFiles.Length);

            MergeInto(assetsById, baseResults);
            MergeInto(assetsById, patchResults);



        }

        private List<Dictionary<UUID, CsvAssetLine>> ParseCSVThreaded(
            string[] files,
            IProgress<(string, int, int, int)>? progress,
            int totalCount,
            int doneOffset)
        {
            var results = new ConcurrentBag<Dictionary<UUID, CsvAssetLine>>();
            int filesDone = doneOffset;

            Parallel.ForEach(files, file =>
            {
                var fileName = Path.GetFileName(file);
                var localResults = new Dictionary<UUID, CsvAssetLine>();

                try
                {
                    using var reader = new StreamReader(file);
                    string? line;
                    int lineNumber = 0;

                    while ((line = reader.ReadLine()) != null)
                    {
                        lineNumber++;
                        if (lineNumber == 1)
                            continue; // header

                        if (string.IsNullOrEmpty(line))
                            continue;

                        try
                        {
                            var csvLine = GetCSVInfo(line, file);
                            var id = ExtractIDFromCSVPath(csvLine.Path);
                            localResults[id] = csvLine;
                        }
                        catch (Exception ex)
                        {
                            string buf = ex.Message;

                            continue;
                        }

                    }




                }
                catch
                {

                }


                results.Add(localResults);

                int done = Interlocked.Increment(ref filesDone);
                FileInfo fInfo = new FileInfo(file);
                progress?.Report((fileName, done, totalCount, (int)fInfo.Length)); // TOOD: Length should be Long

            });

            return results.ToList();
        }

        private static void MergeInto(
            Dictionary<UUID, AssetEntry> assetsById,
            List<Dictionary<UUID, CsvAssetLine>> parsedFiles)
        {
            foreach (var localResults in parsedFiles)
            {
                foreach (var (id, csvLine) in localResults)
                {
                    if (assetsById.TryGetValue(id, out var entry))
                    {
                        assetsById[id] = entry with { Size = csvLine.Size, CompressedSize = csvLine.CompressedSize, SubEntries = csvLine.SubEntries, Offset = csvLine.Offset, SourceArchive = csvLine.SourceArchive, Pruned = false };
                    }
                }
            }
        }


        private static CsvAssetLine GetCSVInfo(string line, string sourceArchive)
        {
            ReadOnlySpan<char> span = line;

            var path = NextToken(ref span, ',');
            long offset = long.Parse(NextToken(ref span, ','), CultureInfo.InvariantCulture);
            NextToken(ref span, ','); // Unknown

            long size = long.Parse(NextToken(ref span, ','), CultureInfo.InvariantCulture);
            long compressedSize = long.Parse(NextToken(ref span, ','), CultureInfo.InvariantCulture);
            int fileCount = int.Parse(NextToken(ref span, ','), CultureInfo.InvariantCulture);
            NextToken(ref span, ','); // Unknown

            var subEntries = new List<SubAssetEntry>(fileCount);
            var subBlob = span;

            while (subBlob.Length > 1)
            {
                var subName = NextToken(ref subBlob, '/');
                if (subName.IsEmpty)
                    break;

                var rawGlobal = NextToken(ref subBlob, '/');
                var rawLocal = NextToken(ref subBlob, '/');
                var rawSize = NextToken(ref subBlob, '/');
                var rawCompressed = NextToken(ref subBlob, '/');

                if (!long.TryParse(rawGlobal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var subGlobalOffset) ||
                    !long.TryParse(rawLocal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var subLocalOffset) ||
                    !long.TryParse(rawSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var subUncompressedSize) ||
                    !long.TryParse(rawCompressed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var subCompressedSize))
                {
                    var error = 
                        $"{sourceArchive} — malformed sub-entry for '{subName}': " +
                        $"global='{rawGlobal.ToString()}' local='{rawLocal.ToString()}' " +
                        $"size='{rawSize.ToString()}' compressed='{rawCompressed.ToString()}' " +
                        $"remaining='{subBlob.ToString()}'";
                    break; // buffer is desynced past this point, stop this row rather than cascade garbage
                }

                var subOffset = subGlobalOffset != 0 ? subGlobalOffset : subLocalOffset;

                subEntries.Add(new SubAssetEntry
                {
                    CompressedSize = subCompressedSize,
                    Size = subUncompressedSize,
                    Offset = subOffset,
                    FileName = subName.ToString(),
                    IsCompressed = subCompressedSize != 0,
                    IsGlobal = subGlobalOffset != 0,
                    SourceArchive = Path.GetFileNameWithoutExtension(sourceArchive),
                    ContentDirectory = Path.GetDirectoryName(sourceArchive) ?? ""
                });
            }


            return new CsvAssetLine
            {
                Path = path.ToString(),
                Offset = offset,
                Size = size,
                CompressedSize = compressedSize,
                FileCount = fileCount,
                SubEntries = subEntries,
                SourceArchive = Path.GetFileNameWithoutExtension(sourceArchive),
            };
        }
        private static ReadOnlySpan<char> NextToken(ref ReadOnlySpan<char> span, char delimiter)
        {
            int idx = span.IndexOf(delimiter);
            if (idx < 0)
            {
                var rest = span;
                span = ReadOnlySpan<char>.Empty;
                return rest;
            }

            var token = span[..idx];
            span = span[(idx + 1)..];
            return token;
        }

        private static UUID ExtractIDFromCSVPath(string path)
        {
            var segments = path.Split('/');
            

            var uuidString = segments[5];
            try
            {
                return UUIDParser.Parse(uuidString);
            }
            catch
            {
                uuidString = segments[6]; // language thing
                return UUIDParser.Parse(uuidString);
            }

            

        }

    }
}
