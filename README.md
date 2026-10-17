# DEV

My personal projects.

| Project | Description | Stack |
|---|---|---|
| [ZX980](ZX980) | Firmware for the ZX908 GPS tracker (Quectel EC800M 4G module): reads the GPS position and sends it over HTTP to the server | QuecPython |
| [ZX980_Server](ZX980_Server) | Server that receives the tracker's positions, stores them in SQLite and shows them on a web dashboard with a map | Python 3 (standard library), FreeBSD |
| [ZX980_Android](ZX980_Android) | Android app that shows the ZX980_Server dashboard: map, track, status, battery, network | Kotlin, osmdroid |
| [iz_pid](iz_pid) | BBL Tuner: Betaflight Blackbox log analysis and PID tuning suggestions, as a standalone Windows app | C# / .NET Framework 4.x |

## ZX980 tracker

The three ZX980 projects work together:

```
ZX980 (tracker) --HTTP POST, port 8080--> ZX980_Server --dashboard + JSON API, port 8090--> web browser / ZX980_Android
```

Each project has its own README with installation and usage details.
