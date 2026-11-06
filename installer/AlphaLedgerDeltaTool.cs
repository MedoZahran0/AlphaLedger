using System.IO.Compression;
using System.Security.Cryptography;

const int anchorSize = 64;
const int maximumAnchorsPerHash = 16;
const ulong anchorMask = 0xFFF;
const double maximumDeltaRatio = 0.8;
ReadOnlySpan<byte> magic = "ALDELTA1"u8;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: AlphaLedgerDeltaTool <base.exe> <target.exe> <output.delta>");
    return 2;
}

var basePath = Path.GetFullPath(args[0]);
var targetPath = Path.GetFullPath(args[1]);
var outputPath = Path.GetFullPath(args[2]);
if (!File.Exists(basePath) || !File.Exists(targetPath))
{
    Console.Error.WriteLine("Both base and target executables must exist.");
    return 2;
}

var baseBytes = File.ReadAllBytes(basePath);
var targetBytes = File.ReadAllBytes(targetPath);
var baseAnchors = IndexAnchors(baseBytes);
var operations = new List<DeltaOperation>();
var targetCursor = 0;
while (targetCursor < targetBytes.Length)
{
    if (!TryFindNextMatch(baseBytes, targetBytes, baseAnchors, targetCursor, out var match))
    {
        operations.Add(new DeltaOperation(DeltaOperationKind.Literal, 0, targetCursor, targetBytes.Length - targetCursor));
        break;
    }

    if (match.TargetOffset > targetCursor)
    {
        operations.Add(new DeltaOperation(
            DeltaOperationKind.Literal,
            0,
            targetCursor,
            match.TargetOffset - targetCursor));
    }
    operations.Add(new DeltaOperation(DeltaOperationKind.Copy, match.BaseOffset, 0, match.Length));
    targetCursor = match.TargetOffset + match.Length;
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
using (var output = File.Create(outputPath))
using (var compressed = new GZipStream(output, CompressionLevel.SmallestSize))
using (var writer = new BinaryWriter(compressed))
{
    writer.Write(magic);
    writer.Write(SHA256.HashData(baseBytes));
    writer.Write(SHA256.HashData(targetBytes));
    writer.Write((ulong)targetBytes.Length);
    writer.Write((uint)operations.Count);
    foreach (var operation in operations)
    {
        writer.Write((byte)operation.Kind);
        if (operation.Kind == DeltaOperationKind.Copy)
        {
            writer.Write((ulong)operation.Offset);
            writer.Write((uint)operation.Length);
        }
        else
        {
            writer.Write((uint)operation.Length);
            writer.Write(targetBytes.AsSpan(operation.TargetOffset, operation.Length));
        }
    }
}

var deltaSize = new FileInfo(outputPath).Length;
var ratio = deltaSize / (double)Math.Max(1, targetBytes.Length);
Console.WriteLine($"Delta size: {deltaSize:N0} bytes ({ratio:P1} of full executable)");
if (ratio >= maximumDeltaRatio)
{
    File.Delete(outputPath);
    Console.WriteLine("Delta is not sufficiently smaller than the full executable; skipping it.");
}

return 0;

static Dictionary<ulong, List<int>> IndexAnchors(byte[] data)
{
    var anchors = new Dictionary<ulong, List<int>>();
    if (data.Length < anchorSize)
        return anchors;

    var hash = GetWindowHash(data, 0);
    var highestPower = GetHighestPower();
    for (var offset = 0; offset <= data.Length - anchorSize; offset++)
    {
        if ((hash & anchorMask) == 0)
        {
            if (!anchors.TryGetValue(hash, out var offsets))
            {
                offsets = [];
                anchors.Add(hash, offsets);
            }
            if (offsets.Count < maximumAnchorsPerHash)
                offsets.Add(offset);
        }

        if (offset < data.Length - anchorSize)
            hash = RollWindowHash(hash, data[offset], data[offset + anchorSize], highestPower);
    }

    return anchors;
}

static bool TryFindNextMatch(
    byte[] baseBytes,
    byte[] targetBytes,
    Dictionary<ulong, List<int>> baseAnchors,
    int targetCursor,
    out DeltaMatch match)
{
    match = default;
    if (targetBytes.Length - targetCursor < anchorSize)
        return false;

    var hash = GetWindowHash(targetBytes, targetCursor);
    var highestPower = GetHighestPower();
    var lastAnchorOffset = targetBytes.Length - anchorSize;
    for (var targetOffset = targetCursor; targetOffset <= lastAnchorOffset; targetOffset++)
    {
        if ((hash & anchorMask) == 0 &&
            baseAnchors.TryGetValue(hash, out var candidates))
        {
            foreach (var baseOffset in candidates)
            {
                if (!baseBytes.AsSpan(baseOffset, anchorSize)
                    .SequenceEqual(targetBytes.AsSpan(targetOffset, anchorSize)))
                    continue;

                var matchTargetOffset = targetOffset;
                var matchBaseOffset = baseOffset;
                while (matchTargetOffset > targetCursor &&
                       matchBaseOffset > 0 &&
                       targetBytes[matchTargetOffset - 1] == baseBytes[matchBaseOffset - 1])
                {
                    matchTargetOffset--;
                    matchBaseOffset--;
                }

                var matchLength = anchorSize;
                while (targetOffset + matchLength < targetBytes.Length &&
                       baseOffset + matchLength < baseBytes.Length &&
                       targetBytes[targetOffset + matchLength] == baseBytes[baseOffset + matchLength])
                {
                    matchLength++;
                }

                match = new DeltaMatch(matchTargetOffset, matchBaseOffset, matchLength);
                return true;
            }
        }

        if (targetOffset < lastAnchorOffset)
            hash = RollWindowHash(hash, targetBytes[targetOffset], targetBytes[targetOffset + anchorSize], highestPower);
    }

    return false;
}

static ulong GetWindowHash(byte[] data, int offset)
{
    const ulong rollingBase = 257;
    var hash = 0UL;
    for (var index = offset; index < offset + anchorSize; index++)
        hash = unchecked(hash * rollingBase + data[index]);
    return hash;
}

static ulong RollWindowHash(ulong hash, byte first, byte next, ulong highestPower)
{
    const ulong rollingBase = 257;
    return unchecked((hash - first * highestPower) * rollingBase + next);
}

static ulong GetHighestPower()
{
    const ulong rollingBase = 257;
    var power = 1UL;
    for (var index = 1; index < anchorSize; index++)
        power = unchecked(power * rollingBase);
    return power;
}

enum DeltaOperationKind : byte
{
    Copy = 0,
    Literal = 1
}

readonly record struct DeltaOperation(DeltaOperationKind Kind, int Offset, int TargetOffset, int Length);
readonly record struct DeltaMatch(int TargetOffset, int BaseOffset, int Length);
