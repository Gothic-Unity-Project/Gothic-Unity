# Bink video decoder (Gothic.Core.Bink)

Plays Gothic's original `.bik` videos (intro, chapter videos, endings) without converting them to MP4.

- `BinkVideo` decodes the frames as raw YUV420 planes and the audio as float PCM. It has no Unity dependencies.
- `Gothic.Core.Adapters.Video.BinkPlayer` uploads the planes to textures (shader `Gothic/Bink YUV`) and streams the
  audio into an `AudioClip`.

No RAD Game Tools code or SDK (`binkw32.dll`) is used, and no video is shipped: the `.bik` files are read at runtime
from the player's own Gothic installation. "Bink" is a trademark of RAD Game Tools / Epic Games; the name is only used
to describe the file format.

## Origin and licenses

The decoder files in this folder (`BinkVideo.cs`, `BinkVideo.Audio.cs`, `BinkBitStream.cs`, `BinkFrame.cs`,
`BinkTables.cs`) are a C# port of:

- **OpenGothic** `common/bink` - https://github.com/Try/OpenGothic/tree/master/common/bink
  - Copyright (c) 2019 Try. Repository license: MIT (full text below).
  - Its README says: "Implementation is based on ffmpeg library source code (LGPLv2.1+)" and "fell free to use it
    for your open-source game remake".
- **FFmpeg** - https://ffmpeg.org - which OpenGothic's codec is based on. License: **GNU Lesser General Public
  License 2.1 or later**. The ported parts and their copyright holders:
  - `libavcodec/bink.c` (video): Copyright (c) 2009 Konstantin Shishkov, Copyright (C) 2011 Peter Ross
  - `libavcodec/binkaudio.c` (audio): Copyright (c) 2007-2011 Peter Ross, Copyright (c) 2009 Daniel Verkamp
  - `libavcodec/binkdata.h` (tables), `libavcodec/binkdsp.c` (IDCT): Copyright (c) 2009 Konstantin Shishkov
  - `libavcodec/fft_template.c` (FFT): Copyright (c) 2008 Loren Merritt, Copyright (c) 2002 Fabrice Bellard,
    partly based on libdjbfft by D. J. Bernstein
  - `libavcodec/rdft.c` (RDFT): Copyright (c) 2009 Alex Converse
  - `libavcodec/dct.c` (DCT): Copyright (c) 2009 Peter Ross, Copyright (c) 2010 Alex Converse,
    Copyright (c) 2010 Vitor Sessak
  - `libavcodec/wma_freqs.c` (`ff_wma_critical_freqs` table): the FFmpeg project

So these five files are **LGPL-2.1-or-later**. Gothic UnZENity itself is GPLv3. Using LGPL 2.1+ code in a GPLv3
project is allowed: LGPL 2.1 section 3 permits applying the GNU GPL (version 2 or later) to the code instead.
The LGPL 2.1 text: https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html - the GPLv3 text is the project's
`LICENSE` file.

Please keep:
- this README, including the copyright list and the MIT notice below;
- the origin header in each decoder file;
- the decoder separate from project code, so its origin stays traceable.

The tables in `BinkTables.cs` are the codec's constant tables, generated 1:1 from the OpenGothic source.

The Unity side (`BinkPlayer`, the `Gothic/Bink YUV` shader, `VRCinema`) is our own code under the project license.
The YUV->RGB constants follow OpenGothic's `shader/bink/bink.frag` (BT.601).

## OpenGothic license (MIT)

```
MIT License

Copyright (c) 2019 Try

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
