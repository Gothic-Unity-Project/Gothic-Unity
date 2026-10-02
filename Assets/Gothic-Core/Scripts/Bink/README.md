# Bink video decoder (Gothic.Core.Bink)

Plays Gothic's original `.bik` videos (intro, chapter videos, endings) without converting them to MP4.

- `BinkVideo` decodes the frames as raw YUV420 planes and the audio as float PCM. It has no Unity dependencies.
- `Gothic.Core.Adapters.Video.BinkPlayer` uploads the planes to textures (shader `Gothic/Bink YUV`) and streams the
  audio into an `AudioClip`.

## Origin and licenses

The decoder files in this folder (`BinkVideo.cs`, `BinkVideo.Audio.cs`, `BinkBitStream.cs`, `BinkFrame.cs`,
`BinkTables.cs`) are a C# port of:

- **OpenGothic** `common/bink` - https://github.com/Try/OpenGothic/tree/master/common/bink
  - Repository license: MIT, Copyright (c) Try (OpenGothic contributors).
  - Its README says: "Implementation is based on ffmpeg library source code (LGPLv2.1+)" and "fell free to use it
    for your open-source game remake".
- **FFmpeg** `libavcodec/bink.c`, `binkaudio.c`, `binkdata.h`, fft/rdft/dct - https://ffmpeg.org
  - License: **GNU Lesser General Public License 2.1 or later**.

So these five files are **LGPL-2.1-or-later**. Gothic UnZENity itself is GPLv3. Using LGPL 2.1+ code in a GPLv3
project is allowed: the LGPL permits relicensing under the GPL.

Please keep:
- this README;
- the origin header in each decoder file;
- the decoder separate from project code, so its origin stays traceable.

The tables in `BinkTables.cs` are the codec's constant tables, generated 1:1 from the OpenGothic source.

The Unity side (`BinkPlayer`, the `Gothic/Bink YUV` shader, `VRCinema`) is our own code under the project license.
The YUV->RGB constants follow OpenGothic's `shader/bink/bink.frag` (BT.601).
