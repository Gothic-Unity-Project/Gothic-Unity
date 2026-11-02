using System;
using System.Collections.Generic;
using System.Text;

namespace Gothic.Core.Domain.Meshes
{
    /// <summary>
    /// DeveloperConfig.EnableZenMaterialCountFix: a world mesh's materials are a small BINARY archive inside the
    /// MeshAndBsp chunk. ZenKit reads its header's second "END" with read_line_then_ignore("\n"), which skips every
    /// '\n' after it - also the first byte of the material count when it's 0x0A (10, 266, 522, ... materials). The
    /// count and everything after is read one byte off and the native loader dies with std::bad_alloc (Mroczne
    /// Tajemnice's ANCIENTTEMPLE.ZEN has exactly 266). "END\0" ends the line without skipping anything.
    /// </summary>
    public static class ZenWorldArchiveFixDomain
    {
        private const ushort _meshMaterialsChunk = 0xB020;
        private static readonly byte[] _meshAndBspTag = Encoding.ASCII.GetBytes("[MeshAndBsp");
        private static readonly byte[] _objectsTag = Encoding.ASCII.GetBytes("objects ");
        private static readonly byte[] _endTag = Encoding.ASCII.GetBytes("END\n\n");
        private static readonly byte[] _asciiFormatTag = Encoding.ASCII.GetBytes("\nASCII\n");

        // Chunks of a world's mesh (0xB0..) and BSP tree (0xC0..) inside the MeshAndBsp entry.
        private static readonly HashSet<ushort> _meshAndBspChunks = new()
        {
            0xB000, 0xB010, 0xB020, 0xB025, 0xB026, 0xB030, 0xB040, 0xB050, 0xB060,
            0xC000, 0xC010, 0xC040, 0xC045, 0xC050, 0xC0FF
        };
        // Last chunk of the BSP tree - the end of the MeshAndBsp entry.
        private const ushort _bspEndChunk = 0xC0FF;
        // How far behind a too short chunk the real next one is searched (the missing data is a few hundred bytes).
        private const int _maxChunkLengthError = 1024 * 1024;

        /// <summary>
        /// DeveloperConfig.EnableZenChunkLengthFix: a chunk of the world mesh whose stored length is wrong - ZenKit jumps
        /// to the wrong spot, reads the data there as the next chunk and hangs (no exception: the game froze in
        /// PreCaching). Renovation 2026's DRAGONISLAND.ZEN: the materials chunk's length leaves out its last material
        /// (75890 instead of 76067). The engine reads the materials one by one and doesn't notice.
        /// The chunks are walked; a chunk not followed by a known chunk gets the length up to the next spot from which
        /// the chain continues. Returns the fixed copy or null when the chain is fine (or the world isn't an ASCII
        /// archive - the binary ones store the entry differently).
        /// </summary>
        public static byte[] TryFixMeshChunkLengths(byte[] data, out string reason)
        {
            // BIN_SAFE worlds (vanilla) have "[MeshAndBsp" as a string too, but not the raw chunks right after it.
            var tag = IndexOf(data, _asciiFormatTag, 0, Math.Min(data.Length, 256)) < 0
                ? -1
                : IndexOf(data, _meshAndBspTag, 0, Math.Min(data.Length, 4096));
            if (tag < 0)
            {
                reason = "no ASCII [MeshAndBsp] entry";
                return null;
            }

            // The entry's stored size can be wrong as well (DRAGONISLAND.ZEN: short by the same material) - ZenKit
            // doesn't use it, the chunks are walked up to the BSP's end chunk within the file.
            var blob = Array.IndexOf(data, (byte)'\n', tag) + 1;
            var end = data.Length;
            byte[] fixedData = null;
            var fixes = new List<string>();

            var chunk = blob + 8;
            while (chunk + 6 <= end)
            {
                var source = fixedData ?? data;
                var id = BitConverter.ToUInt16(source, chunk);
                if (id == _bspEndChunk)
                    break;

                var length = BitConverter.ToInt32(source, chunk + 2);
                var next = chunk + 6L + length;
                if (length >= 0 && next + 6 <= end && _meshAndBspChunks.Contains(BitConverter.ToUInt16(source, (int)next)))
                {
                    chunk = (int)next;
                    continue;
                }

                var realNext = FindChunkChain(source, (int)Math.Min(end, chunk + 6L + Math.Max(0, length)), end);
                if (realNext < 0)
                {
                    reason = $"chunk 0x{id:X4} at {chunk}: stored length {length} leads nowhere, no chunk found behind it";
                    return null;
                }

                fixedData ??= (byte[])data.Clone();
                var realLength = realNext - chunk - 6;
                BitConverter.GetBytes(realLength).CopyTo(fixedData, chunk + 2);
                fixes.Add($"chunk 0x{id:X4} length {length} -> {realLength}");
                chunk = realNext;
            }

            reason = fixedData == null ? "chunk lengths are fine" : string.Join(", ", fixes);
            return fixedData;
        }

        /// <summary>
        /// A known chunk at the position whose own length leads to another known chunk (or it's the BSP's end chunk).
        /// </summary>
        private static bool IsChunkChain(byte[] data, int position, int end)
        {
            if (position < 0 || position + 6 > end || !_meshAndBspChunks.Contains(BitConverter.ToUInt16(data, position)))
                return false;
            if (BitConverter.ToUInt16(data, position) == _bspEndChunk)
                return true;

            // long: a garbage length mustn't overflow into a valid looking position.
            var next = position + 6L + BitConverter.ToInt32(data, position + 2);
            return next >= position + 6 && next + 6 <= end &&
                   _meshAndBspChunks.Contains(BitConverter.ToUInt16(data, (int)next));
        }

        private static int FindChunkChain(byte[] data, int start, int end)
        {
            var searchEnd = Math.Min(end - 6, start + _maxChunkLengthError);
            for (var i = start; i <= searchEnd; i++)
            {
                if (data[i + 1] is 0xB0 or 0xC0 && IsChunkChain(data, i, end))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// The world's bytes with the materials archive header fixed, or null when the world doesn't need it (or isn't
        /// an ASCII archive - the binary ones store the chunk differently).
        /// </summary>
        public static byte[] TryFixMaterialCountHeader(byte[] data, out string reason)
        {
            var tag = IndexOf(data, _meshAndBspTag, 0, Math.Min(data.Length, 4096));
            if (tag < 0)
            {
                reason = "no ASCII [MeshAndBsp] entry";
                return null;
            }

            // u32 version, u32 size, then the mesh's chunks: u16 id, u32 length.
            var blob = Array.IndexOf(data, (byte)'\n', tag) + 1;
            var blobSize = BitConverter.ToInt32(data, blob + 4);
            var blobEnd = Math.Min(data.Length, blob + 8 + blobSize);
            var chunk = blob + 8;
            while (chunk + 6 <= blobEnd && BitConverter.ToUInt16(data, chunk) != _meshMaterialsChunk)
                chunk += 6 + BitConverter.ToInt32(data, chunk + 2);
            if (chunk + 6 > blobEnd)
            {
                reason = "no materials chunk";
                return null;
            }

            var chunkLength = BitConverter.ToInt32(data, chunk + 2);
            var body = chunk + 6;
            var bodyEnd = Math.Min(blobEnd, body + chunkLength);
            var objects = IndexOf(data, _objectsTag, body, Math.Min(bodyEnd, body + 256));
            var end = objects < 0 ? -1 : IndexOf(data, _endTag, objects, Math.Min(bodyEnd, objects + 64));
            if (end < 0 || end + _endTag.Length + 4 > bodyEnd)
            {
                reason = "no binary materials archive header";
                return null;
            }

            var count = BitConverter.ToInt32(data, end + _endTag.Length);
            if (data[end + _endTag.Length] != (byte)'\n')
            {
                reason = $"material count {count} isn't affected";
                return null;
            }

            // "END\n\n" -> "END\0": one byte shorter, the chunk and the MeshAndBsp blob shrink with it.
            var fixedData = new byte[data.Length - 1];
            Buffer.BlockCopy(data, 0, fixedData, 0, end + 3);
            fixedData[end + 3] = 0;
            Buffer.BlockCopy(data, end + _endTag.Length, fixedData, end + 4, data.Length - end - _endTag.Length);
            BitConverter.GetBytes(chunkLength - 1).CopyTo(fixedData, chunk + 2);
            BitConverter.GetBytes(blobSize - 1).CopyTo(fixedData, blob + 4);

            reason = $"material count {count} starts with a newline byte";
            return fixedData;
        }

        private static int IndexOf(byte[] data, byte[] pattern, int start, int end)
        {
            for (var i = start; i <= end - pattern.Length; i++)
            {
                var j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j])
                    j++;
                if (j == pattern.Length)
                    return i;
            }
            return -1;
        }
    }
}
