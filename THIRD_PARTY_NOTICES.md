# Third-party components

The application is a graphical frontend. Third-party executables are downloaded directly from their upstream releases at the user's request or when required for the first download. They are not included in the application ZIP.

| Component | Source | License information |
| --- | --- | --- |
| yt-dlp | https://github.com/yt-dlp/yt-dlp | Source: Unlicense. The official Windows bundle includes other packages with their own licenses; see the project's LICENSE and THIRD_PARTY_LICENSES.txt. |
| FFmpeg and ffprobe | https://github.com/yt-dlp/FFmpeg-Builds | The selected win64 GPL build is distributed under GPL terms and includes libraries with their own licenses. See the build's license notices and https://ffmpeg.org/legal.html. |
| Deno | https://github.com/denoland/deno | MIT, with bundled third-party notices. See the Deno repository LICENSE.md. |
| .NET Framework / Windows Forms | Microsoft Windows | Supplied by the Windows runtime, not redistributed in this package. |

The installed tools directory retains notices found in downloaded archives and records version/source URLs in its installation manifest. Refer to the upstream release for the complete corresponding source and license information. The GUI's source code is licensed under the MIT license in `LICENSE`.
