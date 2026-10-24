# DEV

 projects.

| Project | Description | Stack |
|---|---|---|
| [ZX980](ZX980) | Firmware for the ZX908 GPS tracker (Quectel EC800M 4G module): reads the GPS position and sends it over HTTP to the server | QuecPython |
| [ZX980_Server](ZX980_Server) | Server that receives the tracker's positions, stores them in SQLite and shows them on a web dashboard with a map | Python 3 (standard library), FreeBSD |
| [ZX980_Android](ZX980_Android) | Android app that shows the ZX980_Server dashboard: map, track, status, battery, network | Kotlin, osmdroid |
| [iz_pid](iz_pid) | BBL Tuner: Betaflight Blackbox log analysis and PID tuning suggestions, as a standalone Windows app | C# / .NET Framework 4.x |
| [list_songs](list_songs) | Sorts music dropped into a `Candidats/` folder: renames and files it by artist/album, fetches cover art, rebuilds an HTML/CSV catalogue | Python 3, beets |
| [miniscreenWin](miniscreenWin) | Windows driver for the WeAct Studio 0.96" USB screen: live clock + CPU/RAM/GPU dashboard, images, animated GIFs, text | Go |

The three ZX980 projects work together:

Each project has its own README with installation and usage details.
