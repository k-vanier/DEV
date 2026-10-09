# BBL Tuner

**Analyse des logs Blackbox Betaflight et proposition de réglages PID, dans une application Windows autonome.**

BBL Tuner ouvre un fichier `.bbl` / `.bfl`, retrouve chaque vol qu'il contient, décode les données gyro et consigne, puis propose des PID corrigés. Il peut aussi appliquer ces valeurs directement à ta config Betaflight (`diff all`).

![Recommandations](docs/recommandations.png)

---

## Fonctionnalités

- **Décodeur Blackbox natif.** Il lit tous les vols d'un même fichier et gère tous les encodages et prédicteurs Betaflight ainsi que le mode `blackbox_high_resolution`. Les trames corrompues sont ignorées et la lecture se resynchronise.
- **Step response** par déconvolution de Wiener, la même méthode que PID-Analyzer : dépassement (pic), latence à 50 % et nombre de fenêtres exploitées, par axe.
- **Réponse temporelle.** La consigne est superposée au gyro filtré, avec la courbe d'erreur. Navigation par manœuvre, fenêtres de 0,5 s à « Tout », et affichage optionnel des termes P/I/D/F.
- **Gyro / consigne.** Traces complètes du vol, avec les gaz et les moteurs, et zoom à la souris.
- **Bruit.** Spectres (Welch) du gyro brut, du gyro filtré et des moteurs, détection des pics de résonance.
- **Recommandations PID** pour P, I, D, D min/max et Feedforward, par axe. Chaque proposition est expliquée, et les variations sont limitées à ±25 % par itération.
- **CLI Betaflight** prêt à coller (avec `simplified_pids_mode = OFF` pour que les sliders n'écrasent pas les valeurs).
- **Correction de config.** Ouvre un `diff all`, choisit automatiquement le profil qui correspond au vol, et montre les changements ligne par ligne. Il enregistre ensuite une copie `_corrige.txt` sans jamais modifier l'original.
- Interface **en thème sombre**, et aucune installation nécessaire.

| Step response | Réponse temporelle |
|---|---|
| ![Step response](docs/step-response.png) | ![Réponse temporelle](docs/reponse-temporelle.png) |

| Gyro / consigne | Bruit |
|---|---|
| ![Gyro / consigne](docs/gyro-consigne.png) | ![Bruit](docs/bruit.png) |

---

## Installation

Il n'y a rien à installer. L'application fonctionne sous Windows 10/11, avec .NET Framework 4.x, déjà présent dans le système.

1. Télécharge `BBLTuner.exe` depuis la page [Releases](../../releases), ou compile-le (voir plus bas).
2. Lance-le.

## Utilisation

1. **Récupère le log.** Dans Betaflight Configurator, onglet *Blackbox*, clique sur *Activate Mass Storage Device Mode*, ou utilise la carte SD. Copie le fichier `.BBL`.
2. **Ouvre-le dans BBL Tuner.** Utilise le bouton *Ouvrir un fichier .bbl…* ou glisse-dépose le fichier sur la fenêtre. Chaque armement devient une ligne dans la liste des vols.
3. **Choisis un vol.** Il est analysé immédiatement. Les vols trop courts ou inexploitables sont grisés.
4. **Lis l'onglet *Recommandations*.** Copie le bloc de l'onglet *CLI* dans le CLI Betaflight, puis tape `save`.
5. **Ou corrige directement ta config.** Dans l'onglet *Config*, ouvre ton fichier `diff all`, vérifie le profil, puis clique sur *Enregistrer la config corrigée…*.

Tu peux aussi passer les fichiers en ligne de commande :

```bash
BBLTuner.exe mon_log.bbl ma_config.txt
```

### Conseils pour un log exploitable

- Mets `blackbox_sample_rate` à **1 kHz minimum** (2 kHz est idéal), avec le mode de debug `GYRO_SCALED` ou `NONE`.
- Fais un vol d'au moins **20 à 30 secondes** avec des **mouvements francs** : flips, rolls, changements de direction secs. Un vol en stationnaire ne donne pas assez de données pour la step response.
- Règle les **filtres** avant les PID : un gyro bruyant fausse l'analyse du D.
- Procède **par petites itérations** : applique, vole, relogue, réanalyse.

> ⚠️ Les propositions sont un point de départ calculé à partir des données, pas une garantie. Vérifie la température des moteurs après chaque changement, surtout quand le D augmente.

---

## Compilation

Aucun SDK n'est nécessaire : le script utilise le compilateur C# fourni avec Windows.

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

Le script produit :

- `bin/BBLTuner.exe`, l'application ;
- `bin/BblTests.exe`, l'outil de test en ligne de commande.

### Tests

```bash
bin/BblTests.exe check
```

`check` encode des vols simulés avec un encodeur reproduisant celui du firmware Betaflight. Il les redécode et vérifie que chaque valeur relue est identique à celle écrite. Il contrôle enfin que l'analyse retrouve le dépassement théorique attendu.

Les autres commandes :

| Commande | Rôle |
|---|---|
| `analyze <log.bbl>` | Rapport d'analyse texte d'un vrai log |
| `config <config.txt>` | Simulation de correction d'une config, sans écriture |
| `seg <log.bbl> <n° vol>` | Amplitudes du signal par segments de 1 s |
| `gen <fichier.bbl>` | Génère le fichier de démo |

---

## Structure du projet

| Fichier | Rôle |
|---|---|
| `src/Bbl.cs` | Décodeur du format Blackbox (multi-logs, prédicteurs, encodages, resynchronisation) |
| `src/Dsp.cs` | FFT, Welch, déconvolution de Wiener, intercorrélation, décimation |
| `src/Analysis.cs` | Métriques par axe et moteur de recommandation PID |
| `src/Config.cs` | Lecture et correction des configs Betaflight (`diff all`) |
| `src/MainForm.cs` | Fenêtre principale, liste des vols, onglets d'analyse |
| `src/RespTab.cs` | Onglet *Réponse temporelle* |
| `src/TraceTab.cs` | Onglet *Gyro / consigne* |
| `src/ConfigTab.cs` | Onglet *Config* |
| `src/Theme.cs`, `src/UiKit.cs`, `src/DarkUi.cs` | Thème sombre et composants d'interface |
| `src/Program.cs` | Point d'entrée et journal d'erreurs |
| `tests/Tests.cs` | Encodeur synthétique et banc de test |
| `demo/demo_3_vols.bbl` | Log de démo : vol sous-amorti, vol trop court, vol sur-amorti |

## Références

- [Betaflight – PID Tuning Guide](https://betaflight.com/docs/wiki/guides/current/PID-Tuning-Guide)
- [Betaflight Blackbox Log Viewer](https://github.com/betaflight/blackbox-log-viewer), pour la référence du format
- [PID-Analyzer](https://github.com/Plasmatree/PID-Analyzer), pour la méthode de step response
