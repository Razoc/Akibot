# AkiBot

<a href="https://recomplauncher.com"><picture><source media="(prefers-color-scheme: dark)" srcset="https://recomplauncher.com/assets/press/launcher/badge-light.svg"><img alt="Get it on RecompLauncher" src="https://recomplauncher.com/assets/press/launcher/badge-dark.svg" height="48"></picture></a>

Fork personnel du [portage PC natif d'**Astro Bot**](https://github.com/imnotsharingmyname/pcportastrobot)
(PS5) par imnotsharingmyname. Le but d'AkiBot : **plus de FPS, moins de saccades** et des réglages adaptés, en
travaillant sur la couche de traduction (CPU et GPU), jamais sur le jeu lui-même.

> [!IMPORTANT]
> **Aucun fichier du jeu n'est dans ce dépôt, et il n'y en aura jamais** : ni exécutable, ni données, ni
> shaders, ni clés, ni sauvegardes. Il faut ton propre dump décrypté d'Astro Bot, issu de ta propre copie.
> Le setup le convertit sur ton PC sans le modifier.

## État actuel

Version **0.3.6** : moins de plantages liés à la carte graphique et à la mémoire vidéo, et des rapports enfin utiles.
- Les **avis et rapports envoyés depuis RecompLauncher contiennent maintenant le résumé de session** (PC, réglages,
  FPS et 1 % bas par niveau, pires saccades, erreurs, fermeture) : RecompLauncher ne joint qu'un seul `.log`, et le
  résumé était en `.txt`. Il s'appelle désormais `Logs\akibot-summary.log`, écrit en dernier à la fermeture du jeu, et
  il est aussi recopié à la fin de `Logs\hybrid-run.err.log`.
- **Dispatch overlap coupé** (repris du portage AstroHybrid 1.2.13) : contre l'erreur 321 (carte bloquée jusqu'à la
  réinitialisation par Windows) et les gels de ~300 ms. Mêmes FPS dans nos essais. `AKIBOT_DISPATCH_OVERLAP=1` le remet.
- **Buffers de commandes des workers à 16 Mio** (repris d'AstroHybrid) : ils pouvaient manquer dans les scènes
  chargées (`booster_construction`) puis faire planter le jeu. Les installations existantes sont mises à niveau toutes
  seules au prochain Jouer (~40 Mo de mémoire en plus).
- Nouveau **runtime 0.3.3** : quand la mémoire vidéo est pleine, un gros buffer qui n'y tient pas va en mémoire système
  au lieu d'arrêter le jeu, et les textures inutilisées sont libérées d'abord (2 correctifs Kyty repris d'AstroHybrid).
- Connu : un gel d'environ 0,6 s à l'entrée de Sky Garden (déjà présent avant). Sur les PC de 6 cœurs ou moins, le
  processeur reste la limite : mesuré, pas encore de solution rapide.

0.3.5 : la **v01.007.000** a enfin son **éclairage rapide**. Le patch communautaire de
[KytyPS5-Modules](https://github.com/KytyPS5/KytyPS5-Modules) (rendu non-RT, minuteries du jeu découplées des FPS,
limiteur CPU du jeu retiré, correctifs de plantage Havok) est appliqué par AkiBot à ta copie du jeu, avec le
**soft-assert du command buffer** (le plantage « too many objects/batches in this scene » devient un simple message).
Sky Garden : **58,8 → 61 FPS, 1 % bas 44 → 48**. Nouveau **runtime 0.3.2** avec la **préparation des shaders avant
de jouer** : au premier Jouer (et après une mise à jour du runtime ou du pilote graphique), une fenêtre « Préparation
des shaders » construit les 789 shaders et 1 254 pipelines de notre liste 01.007, puis le jeu démarre tout seul. Plus de
centaines de draws sautés ni de saccades à la découverte des niveaux (1 seul draw sauté dans Sky Garden juste après,
contre ~450 sans liste).

0.3.4 : configuration facile de RecompLauncher de retour (choix du dossier du dump, conversion
automatique) ; réglages choisis pour le PC au premier lancement (60 FPS si le processeur a 12 threads ou plus, sinon
30 ; DLSS sur les cartes RTX ; génération d'images jamais activée par défaut) ; mise à l'échelle simplifiée (celle du
jeu, DLSS, DLAA, AMD FSR 3.1) ; objectif de génération d'images à 120 FPS par défaut. 0.3.3 : résumé de session
`Logs\akibot-summary.log`. AkiBot s'installe et se lance depuis [RecompLauncher](https://recomplauncher.com) (voir
[Avec RecompLauncher](#avec-recomplauncher)).

Nouveautés de la **0.3.0** : premier runtime 100 % AkiBot, DLSS / FSR / génération d'images et menu en jeu.

- **Menu en jeu** (F1 ou Échap, manette comprise) avec toutes les options du lanceur. Limite de FPS, compteur,
  plein écran et vitesse du jeu changent en direct ; le reste est enregistré pour le prochain lancement (« * »).
- **DLSS** (Qualité, Équilibré, Performance, Ultra Performance, DLAA) et **génération d'images NVIDIA** (×2 à ×4)
  qui marchent maintenant aussi sur la **v01.007.000** (passe d'upscale de cette version retrouvée).
- **AMD FSR 3.1** (mise à l'échelle) et **génération d'images AMD FSR 3** (×2), sur toutes les cartes.
- Le compteur de FPS compte les images générées (NVIDIA et AMD).
- **Runtime AkiBot** : `kyty_gpu.dll` recompilée avec Streamline et FidelityFX, module hôte AnyPS5 au nom
  d'AkiBot. Les autres bibliothèques PS5 restent celles d'origine (elles contiennent des correctifs absents des
  sources publiées).
- **Plus aucun .bat** : AkiBot.exe démarre le jeu lui-même. Les installations 0.1.x / 0.2.x sont migrées
  automatiquement au premier démarrage (raccourcis et anciens scripts compris).
- Journal de lancement `Logs\akibot-launch.log` (options passées au jeu) pour comparer un lancement qui plante.

| Sky Garden, 1440p (9800X3D + RTX 5070) | FPS du jeu | FPS à l'écran |
|---|---|---|
| Natif | ~59 | ~59 |
| DLSS + génération NVIDIA ×4 | ~39 | ~156 |
| FSR 3.1 + génération AMD ×2 | ~58 | ~116 |

> [!WARNING]
> Sur la **v01.007.000**, un plantage aléatoire rare (environ 1 lancement sur 15, au chargement d'un niveau)
> existe depuis le début, sans lien avec AkiBot 0.3.0. Relancer suffit. La solution durable est la mise à jour
> 01.019.000 du jeu.

<p align="center">
  <img src="docs/screenshots/akibot-accueil.png" width="49%" alt="Lanceur AkiBot : accueil"/>
  <img src="docs/screenshots/akibot-graphismes.png" width="49%" alt="Lanceur AkiBot : graphismes"/>
</p>

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
| Fin du tutoriel (passage vers l'espace) | ✅ depuis AkiBot 0.1.1 (surveillance des écritures mémoire coupée automatiquement ; avant : écran noir ou plantage) |
| Manette sans gyroscope (Xbox) | ✅ View/Back puis stick droit pour incliner |
| Vitesse temps réel | ✅ |
| Ultrawide 21:9 / 32:9 | ✅ |
| Correctifs du command buffer | ✅ |
| Éclairage rapide (`AstroBot-NonRT`) | ✅ depuis 0.3.5 : patch communautaire 01.007 (voir ci-dessous) |
| Soft-assert du command buffer | ✅ depuis 0.3.5 (fonction retrouvée par sa forme) |
| Correctifs de plantage Havok | ✅ depuis 0.3.5 (dans le patch communautaire) |
| Patch physique des tissus | ❌ code différent dans cette version, patch ignoré |

### Patch communautaire 01.007 (0.3.5)

Avec **Éclairage → rapide** (le réglage par défaut), AkiBot fabrique `AstroBot-NonRT-SoftAssert.exe` à partir de ta
copie du jeu : le patch `PPSA21564_01_007_000_Astro_Bot_1.json` de KytyPS5-Modules, porté sur l'exécutable converti
(son code va dans une section `.akipat` ajoutée), avec ses réglages par défaut + les correctifs Havok :

- rendu d'éclairage différé non tuilé (non-RT) ;
- minuteries du jeu découplées des FPS, pas de temps limité à 1/30 s, limiteur CPU du jeu retiré ;
- rendu fixé en 1920×1080 (comme le jeu d'origine en jeu) et textures 4K chargées en 2K (moins de mémoire vidéo) ;
- correctifs de plantage Havok (forme de contact manquante, débordement de la pile des requêtes d'arbre).

Rien n'est écrit si un seul octet d'origine diffère (autre version, ou conversion pour processeur Intel : dans ce cas
l'éclairage d'origine est gardé). Le résultat est vérifié par son empreinte SHA-256. Le détail est dans
`Logs\setup.log`.

| Sky Garden 1080p, même séance (9800X3D + RTX 5070) | FPS | 1 % bas |
|---|---|---|
| Éclairage d'origine | 58,8 / 58,9 | 44,3 / 45,4 |
| Patch communautaire | 60,3 / 61,0 | 47,3 / 48,4 |
| Patch + correctifs Havok | 61,5 / 62,1 | 47,9 / 49,3 |

Le limiteur 120 FPS du patch n'est pas activé : le jeu reste plafonné vers 60 FPS par le processeur (Kyty), il
n'apporte rien pour l'instant.

### Préparation des shaders (0.3.5, runtime 0.3.2)

Le runtime contient `Build\RunHybrid\PipelineLists\PPSA21567.kyplst`, une liste faite sur nos parties en 01.007 :
789 shaders, 1 583 pipelines, **seulement des positions et des empreintes, aucun code du jeu** (vérifié par
`kyty_pipeline_list verify` à chaque fabrication du runtime). Kyty retrouve chaque shader dans ta copie du jeu par son
empreinte.

- Au premier Jouer (aussi depuis RecompLauncher), AkiBot lance Kyty en mode préparation (`KYTY_PIPELINE_LIST_PREPARE`) :
  il construit tout, enregistre ses caches et s'arrête ; le jeu démarre ensuite tout seul. Quelques secondes à une
  minute selon le cache du pilote graphique.
- « Jouer sans attendre » saute l'étape (elle reviendra au prochain lancement) ; fermer la fenêtre ne lance rien.
- Elle revient seulement si le runtime, la liste ou le pilote graphique changent (repère
  `Build\RunHybrid\_PipelineCache\akibot-prepared.txt`). Journal : `Logs\shader-prep.log`.
  `AKIBOT_SHADER_PREP=0` la désactive.
- Sur une autre version du jeu, Kyty ignore la liste si moins de la moitié des shaders correspondent : rien n'est
  préparé, le jeu se lance comme avant.

### Mesures de référence (Sky Garden)

Lancement direct dans le niveau (`-lvl underwater_aerial_garden`), 2560×1440 natif, sans DLSS ni génération
d'images, sans limite de FPS, éclairage d'origine. FPS relevé chaque seconde pendant 120 s après 25 s de chauffe.

| Essai | Moyenne | Médiane | Pire seconde | Secondes sous 50 FPS |
|---|---|---|---|---|
| AkiBot 0.1.1 (correctif actif), cache froid | 63,6 | 65 | 15 | 2 |
| Sans le correctif (`APS5_NO_WRITE_WATCH=0`) | 61,5 | 64 | 0 | 7 |
| AkiBot 0.1.1 (correctif actif), cache chaud | 61,9 | 63 | 42 | 3 |

**~62 FPS de moyenne** sur Ryzen 7 9800X3D + RTX 5070 (l'amont mesure 32-34 FPS au même endroit avec un
Ryzen 5 5500 + RTX 5060). Le correctif 0.1.1 ne coûte pas de FPS. Le jeu reste limité par le CPU (préparation des
draws sur un seul thread, plafond ~70 FPS) : c'est la cible des optimisations à venir.

`kyty_gpu.dll` recompilée par AkiBot contre celle de la release, mesurées l'une après l'autre dans la même
séance : **58,5 contre 58,3 FPS** de moyenne (1 % bas 56 contre 55). Performances identiques ; l'écart avec les
~62 FPS ci-dessus vient de l'état du PC ce jour-là. Toujours comparer deux versions dans la même séance.

Pour refaire la mesure sur ton PC : `powershell -ExecutionPolicy Bypass -File Tools\akibot\bench_level.ps1 -InstallDir C:\AkiBot`
(le jeu s'ouvre et se ferme tout seul ; résultat dans la console et un CSV à côté du script).

## Jouer

1. Télécharge **AkiBot.exe** depuis la [dernière release](https://github.com/Razoc/AkiBot/releases/latest)
   (un seul fichier, environ 1,4 Mo).
2. Lance-le. Choisis la langue et le dossier d'installation (par exemple `C:\AkiBot`, hors OneDrive, environ
   3 Go libres). AkiBot télécharge le runtime AkiBot (192 Mo) et vérifie son empreinte SHA-256.
3. Indique le dossier de ton dump (celui qui contient `eboot.bin`, `sce_module` et `sce_sys`). La conversion
   prend environ une minute ; ton dump n'est jamais modifié.
4. Règle les graphismes et la manette si tu veux, puis appuie sur **Jouer** (en bas à gauche).

AkiBot vérifie les nouvelles versions à chaque démarrage et se met à jour tout seul (la 0.2.0 propose
la 0.3.0, qui installe ensuite son nouveau runtime). Windows SmartScreen peut
avertir au premier lancement (exe non signé) : « Informations complémentaires » puis « Exécuter quand même ».

Sans fenêtre : `AkiBot.exe --install "<dossier>"`, puis `AkiBot.exe --setup "<dossier du dump>"` depuis le
dossier d'installation. `AkiBot.exe --play` lance le jeu avec les réglages enregistrés ;
`AkiBot.exe --run <arguments>` le lance avec ses propres arguments (ex. `-lvl underwater_aerial_garden`).

### Avec RecompLauncher

AkiBot fournit un `recomplauncher.json` dans le zip `AkiBot-v<version>-Windows-x64.zip` de chaque release :
[RecompLauncher](https://recomplauncher.com) installe AkiBot et affiche les réglages graphiques sur la page du jeu.
La configuration facile demande le dossier de ton dump (celui qui contient `eboot.bin` ; choisir le fichier
`eboot.bin` ou le dossier au-dessus marche aussi), puis AkiBot installe son runtime et convertit le jeu
(`AkiBot.exe --prepare`). Au premier clic sur Jouer, AkiBot choisit des réglages adaptés au PC ; ensuite, Jouer
lance la partie directement (`AkiBot.exe --play`). En mode Big Picture, le jeu s'ouvre en plein écran.

> English: download **AkiBot.exe** from the latest release, run it, pick a language and an install folder,
> then point it at your own Astro Bot dump. AkiBot updates itself.

## Compiler `kyty_gpu.dll`

Prérequis : Visual Studio 2022 Build Tools (C++ x64) + SDK Windows, **LLVM 22.1.3** (clang-cl ; les versions
plus récentes ne lisent pas le profil PGO), SDK Vulkan 1.4.363.0, CMake, Ninja, Python 3, Git.

```
git clone https://github.com/Jetsku/KytyPS5.git kyty
cd kyty
git config core.autocrlf false
git checkout u59-windows-20261024-int21
git submodule update --init --recursive
git am --keep-cr ..\AkiBot\Source\kyty-hybrid\*.patch
powershell -ExecutionPolicy Bypass -File ..\AkiBot\Tools\hybrid\build-kyty-pgo.ps1
```

- Place `kyty` à côté du dépôt AkiBot (le script cherche `..\kyty`) ou indique `KYTY_SRC`. Résultat :
  `..\kyty-gpu\kyty_gpu.dll`, en environ 3 min sur un 9800X3D.
- `core.autocrlf false` est obligatoire : sinon les patchs ne s'appliquent pas. Si ton clone d'AkiBot a été fait
  avec `autocrlf true`, applique les patchs tirés de Git (`git show HEAD:Source/kyty-hybrid/<patch>`).
- Sans `KYTY_STREAMLINE_DIR` (SDK Streamline 2.14.16), la DLL est construite sans DLSS ni génération d'images
  NVIDIA ; sans `KYTY_FFX_DIR` (AMD FidelityFX SDK 1.1.4), sans FSR.
- Paquet runtime complet : `powershell -ExecutionPolicy Bypass -File Tools\release\make_runtime.ps1 -Version 0.3.3`.
- Pour tester : garde une copie de `Build\RunHybrid\kyty_gpu.dll`, remplace-la, mesure avec `bench_level.ps1`.

## Feuille de route

- [x] Mesurer une base de FPS fiable dans Sky Garden (~62 FPS en 1440p natif, voir plus haut). Autres niveaux à faire.
- [x] Lanceur refait : fenêtre 1280×720 épurée (0.2.0).
- [x] Compiler `kyty_gpu.dll` depuis les sources et retrouver les perfs de la release.
- [x] Compiler `kyty_gpu.dll` avec Streamline et FidelityFX ; runtime AkiBot (0.3.0).
- [x] DLSS et génération d'images NVIDIA sur la v01.007.000 ; AMD FSR 3.1 et génération d'images AMD (0.3.0).
- [x] Menu en jeu F1 / Échap (0.3.0).
- [ ] Comprendre pourquoi la génération NVIDIA ×4 fait tomber le jeu de ~59 à ~39 FPS.
- [ ] Profiler le « draw commit » du command processor (~22 ms par image sur un seul thread CPU, ~4 900 draws
      par image) et chercher des gains.
- [x] Porter l'éclairage rapide et le soft-assert sur la v01.007.000 (0.3.5, patch communautaire).
- [ ] Porter le patch communautaire pour les processeurs Intel (exécutable converti différemment).
- [ ] Préréglages pour les CPU X3D et les RTX 50.
- [x] Résumé de session joint aux avis et rapports RecompLauncher (0.3.6).
- [x] Correctifs de stabilité repris d'AstroHybrid : dispatch overlap, buffers de commandes 16 Mio, mémoire vidéo
      pleine (0.3.6, runtime 0.3.3).
- [ ] Gel d'environ 0,6 s à l'entrée de Sky Garden.

## Contribuer

Les idées, mesures et pull requests sont les bienvenues :

- Ouvre une issue avec ta config (CPU, GPU, RAM, pilote), la version de ton dump (`sce_sys/param.json` →
  `titleId` et `contentVersion`) et ce que tu as mesuré.
- Joins les logs (`Logs\akibot-summary.log` d'abord, puis `Logs\setup.log`, `Logs\akibot-launch.log`,
  `Logs\hybrid-run.err.log`) **après avoir retiré tes chemins perso**. Depuis RecompLauncher, le rapport du jeu les contient déjà.
- Ne publie **jamais** de fichier du jeu, ni `AstroBot.exe`, ni le dossier `Build\` : c'est du code du jeu
  converti.

Les améliorations utiles à tous seront aussi proposées au projet d'origine.

## Crédits

AkiBot n'existe que grâce à ces projets :

- **[pcportastrobot](https://github.com/imnotsharingmyname/pcportastrobot)** (imnotsharingmyname) : le
  portage dont ce dépôt est un fork ; la 0.3.6 reprend plusieurs de ses correctifs de stabilité. Son README d'origine (en anglais) est dans [Docs/README-amont.md](Docs/README-amont.md).
- **AnyPS5** (boykopovar) : convertit le code x86-64 du jeu en exécutable Windows et réimplémente les
  bibliothèques système PS5.
- **KytyPS5 / Kyty** : la pile GPU traduite vers Vulkan (`kyty_gpu.dll`).
- **NVIDIA Streamline** : DLSS, Frame Generation et Reflex.
- **AMD FidelityFX SDK** (licence MIT) : FSR 3.1 et génération d'images FSR 3.
- **[KytyPS5-Modules](https://github.com/KytyPS5/KytyPS5-Modules)** et ses contributeurs : le patch communautaire
  Astro Bot 01.007 (rendu non-RT, delta time, correctifs Havok) appliqué depuis la 0.3.5.

Astro Bot est une marque de Sony Interactive Entertainment. Ce projet n'est ni affilié à Sony ni approuvé par
Sony.
