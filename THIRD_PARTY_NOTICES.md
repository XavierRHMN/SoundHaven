# Third-party notices

SoundHaven includes unmodified third-party libraries distributed under their
respective licenses. The release keeps these libraries as separate assemblies.

## MIT-licensed libraries

- Avalonia, Avalonia.Desktop, and Avalonia.Fonts.Inter 11.3.18  
  <https://github.com/AvaloniaUI/Avalonia>
- Svg.Controls.Avalonia 11.3.9.5  
  <https://github.com/wieslawsoltes/Svg.Skia>
- Material.Avalonia and Material.Avalonia.DataGrid 3.14.2  
  <https://github.com/AvaloniaCommunity/Material.Avalonia>
- Microsoft.Data.Sqlite and Microsoft.Extensions.Caching.Memory 9.0.18  
  <https://github.com/dotnet/efcore> and <https://github.com/dotnet/runtime>
- NAudio 2.3.0  
  <https://github.com/naudio/NAudio>
- YoutubeExplode 6.6.0  
  <https://github.com/Tyrrrz/YoutubeExplode>

The copyright holders named by the linked projects license those components
under these terms:

> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in
> all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

## Apache-licensed libraries

SoundHaven includes SQLitePCLRaw 3.0.3, copyright SourceGear, LLC, under the
Apache License, version 2.0.

- Source: <https://github.com/ericsink/SQLitePCL.raw>
- License: `licenses/SQLitePCLRaw-Apache-2.0.txt`

## Public-domain components

The SQLite native library in SourceGear.sqlite3 3.50.4.5 is in the public
domain. See <https://sqlite.org/copyright.html>.

## TagLibSharp

SoundHaven uses TagLibSharp 2.3.0 for audio metadata and embedded artwork
(including downloaded M4A thumbnails). TagLibSharp is licensed under the GNU
Lesser General Public License, version 2.1 only (LGPL-2.1-only). SoundHaven
itself is licensed under LGPL-2.1-or-later to keep that combination simple for
binary redistribution.

- Source: <https://github.com/mono/taglib-sharp/tree/2.3.0.0>
- License: `licenses/TagLibSharp-LGPL-2.1.txt`

You may replace the separately distributed TagLibSharp assembly with a
compatible modified build as permitted by the LGPL. SoundHaven does not impose
restrictions on reverse engineering performed to debug such modifications.

## Platform components

The self-contained Windows package also carries Microsoft .NET runtime files
under the licenses included in those files. Windows Media Foundation is an
operating-system component and is not bundled by SoundHaven.
