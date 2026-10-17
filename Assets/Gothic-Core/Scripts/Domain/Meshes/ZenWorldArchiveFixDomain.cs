using System;
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
