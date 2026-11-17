# Akibot

Fork personnel d'[AstroHybrid](https://github.com/imnotsharingmyname/pcportastrobot), le portage PC natif
d'**Astro Bot** (PS5). Le but d'Akibot : **plus de FPS, moins de saccades** et des réglages adaptés, en
travaillant sur la couche de traduction (CPU et GPU), jamais sur le jeu lui-même.

> [!IMPORTANT]
> **Aucun fichier du jeu n'est dans ce dépôt, et il n'y en aura jamais** : ni exécutable, ni données, ni
> shaders, ni clés, ni sauvegardes. Il faut ton propre dump décrypté d'Astro Bot, issu de ta propre copie.
> Le setup le convertit sur ton PC sans le modifier.

## État actuel

Version **0.1.0** : nouveau lanceur Akibot (installation en un exe, français/anglais, mises à jour auto). Le moteur
est pour l'instant **identique à AstroHybrid 1.2.1**. Les optimisations Akibot arriveront ici au fur
et à mesure, avec des mesures avant/après.

### Testé sur

| | |
|---|---|
| Jeu | **PPSA21564 v01.007.000** (l'amont teste PPSA21567 v01.019.000) |
| CPU | AMD Ryzen 7 9800X3D |
| GPU | NVIDIA RTX 5070 |
| RAM | 32 Go |
| Écran | 2560×1440, 165 Hz |

### Compatibilité de la v01.007.000

| Fonction | Résultat |
|---|---|
| Validation et setup du dump | ✅ accepté, `AstroBot.exe` construit sans erreur |
| Lancement | ✅ démarre, image correcte, 60 FPS dans l'intro |
| Vitesse temps réel | ✅ |
| Ultrawide 21:9 / 32:9 | ✅ |
| Correctifs du command buffer | ✅ |
| Éclairage rapide (`AstroBot-NonRT`) | ❌ code différent dans cette version, patch ignoré |
| Soft-assert du command buffer | ❌ idem |
| Patch physique des tissus | ❌ idem |

## Jouer

1. Télécharge **Akibot.exe** depuis la [dernière release](https://github.com/Razoc/Akibot/releases/latest)
   (un seul fichier, environ 1,4 Mo).
2. Lance-le. Choisis la langue et le dossier d'installation (par exemple `C:\Akibot`, hors OneDrive, environ
   3 Go libres). Akibot télécharge le runtime (190 Mo) et vérifie son empreinte SHA-256.
3. Indique le dossier de ton dump (celui qui contient `eboot.bin`, `sce_module` et `sce_sys`). La conversion
   prend environ une minute ; ton dump n'est jamais modifié.
4. Règle les graphismes et la manette si tu veux, puis appuie sur **Jouer**.

Akibot vérifie les nouvelles versions à chaque démarrage et se met à jour tout seul. Windows SmartScreen peut
avertir au premier lancement (exe non signé) : « Informations complémentaires » puis « Exécuter quand même ».

Sans fenêtre : `Akibot.exe --install "<dossier>"`, puis `Akibot.exe --setup "<dossier du dump>"` depuis le
dossier d'installation.

> English: download **Akibot.exe** from the latest release, run it, pick a language and an install folder,
> then point it at your own Astro Bot dump. Akibot updates itself.

## Feuille de route

- [ ] Mesurer une base de FPS fiable (Sky Garden et d'autres niveaux) sur le PC de test.
- [ ] Compiler AnyPS5 et `kyty_gpu.dll` depuis les sources et retrouver les perfs de la release.
- [ ] Profiler le « draw commit » du command processor (~22 ms par image sur un seul thread CPU, ~4 900 draws
      par image) et chercher des gains.
- [ ] Porter l'éclairage rapide et le soft-assert sur la v01.007.000.
- [ ] Préréglages pour les CPU X3D et les RTX 50.

## Contribuer

Les idées, mesures et pull requests sont les bienvenues :

- Ouvre une issue avec ta config (CPU, GPU, RAM, pilote), la version de ton dump (`sce_sys/param.json` →
  `titleId` et `contentVersion`) et ce que tu as mesuré.
- Joins les logs (`Logs\setup.log`, `Logs\hybrid-run.err.log`) **après avoir retiré tes chemins perso**.
- Ne publie **jamais** de fichier du jeu, ni `AstroBot.exe`, ni le dossier `Build\` : c'est du code du jeu
  converti.

Les améliorations utiles à tous seront aussi proposées au projet d'origine.

## Crédits

Akibot n'existe que grâce à ces projets :

- **[AstroHybrid](https://github.com/imnotsharingmyname/pcportastrobot)** (imnotsharingmyname) : le portage
  dont ce dépôt est un fork. Son README d'origine (en anglais) est dans [Docs/README-amont.md](Docs/README-amont.md).
- **AnyPS5** (boykopovar) : convertit le code x86-64 du jeu en exécutable Windows et réimplémente les
  bibliothèques système PS5.
- **KytyPS5 / Kyty** : la pile GPU traduite vers Vulkan (`kyty_gpu.dll`).
- **NVIDIA Streamline** : DLSS, Frame Generation et Reflex.

Astro Bot est une marque de Sony Interactive Entertainment. Ce projet n'est ni affilié à Sony ni approuvé par
Sony.
