using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Markup;

namespace AstroHybrid.App.Services
{
    // Akibot translations (French and English). The language is chosen once at startup: from the user's choice
    // saved in %LOCALAPPDATA%\Akibot\akibot.json, else from the Windows display language. Changing it takes
    // effect on the next start.
    //   Code: Loc.T("key") or Loc.T("key", arg0, arg1)   XAML: Text="{svc:Tr key}"
    public static class Loc
    {
        public const string French = "fr";
        public const string English = "en";

        public static string Language { get; private set; } = English;

        public static void Init(string? language)
        {
            Language = language == French || language == English ? language! : DetectWindowsLanguage();
        }

        public static string DetectWindowsLanguage() =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? French : English;

        public static string T(string key, params object[] args)
        {
            string text;
            if (Strings.TryGetValue(key, out var pair))
                text = Language == French ? pair.Fr : pair.En;
            else if (Language == French && (LocTables.French.TryGetValue(key, out var fr) || LocTables.CodeFrench.TryGetValue(key, out fr)))
                text = fr; // original English interface text used as the key
            else if (Language != French && (LocTables.EnglishOverrides.TryGetValue(key, out var en) || LocTables.CodeEnglishOverrides.TryGetValue(key, out en)))
                text = en;
            else
                text = key;
            return args.Length == 0 ? text : string.Format(text, args);
        }

        private static readonly Dictionary<string, (string Fr, string En)> Strings = new Dictionary<string, (string, string)>
        {
            // Common
            ["App.Name"] = ("Akibot", "Akibot"),
            ["App.Tagline"] = ("Astro Bot en natif sur PC", "Astro Bot running natively on PC"),
            ["Common.Browse"] = ("Parcourir…", "Browse…"),
            ["Common.Cancel"] = ("Annuler", "Cancel"),
            ["Common.Close"] = ("Fermer", "Close"),
            ["Common.Next"] = ("Suivant", "Next"),
            ["Common.Back"] = ("Retour", "Back"),
            ["Common.Error"] = ("Erreur", "Error"),
            ["Common.Yes"] = ("Oui", "Yes"),
            ["Common.No"] = ("Non", "No"),
            ["Common.UnexpectedError"] = ("Une erreur inattendue s'est produite :\n{0}", "An unexpected error occurred:\n{0}"),
            ["Common.StartupError"] = ("Erreur au démarrage :\n{0}", "Startup error:\n{0}"),

            // Install window (first run)
            ["Install.Title"] = ("Installation d'Akibot", "Akibot setup"),
            ["Install.Welcome"] = ("Bienvenue !", "Welcome!"),
            ["Install.Intro"] = (
                "Akibot fait tourner ta propre copie PS5 d'Astro Bot nativement sous Windows. Aucun fichier du jeu n'est fourni : il te faudra ton propre dump à l'étape suivante.",
                "Akibot runs your own PS5 copy of Astro Bot natively on Windows. No game files are included: you will need your own dump in the next step."),
            ["Install.Language"] = ("Langue", "Language"),
            ["Install.Folder"] = ("Dossier d'installation", "Install folder"),
            ["Install.FolderHint"] = (
                "Choisis un dossier simple, hors OneDrive, par exemple C:\\Akibot. Prévois environ {0} libres.",
                "Pick a plain folder outside OneDrive, for example C:\\Akibot. Allow about {0} of free space."),
            ["Install.FreeSpace"] = ("Espace libre sur ce disque : {0}", "Free space on this drive: {0}"),
            ["Install.Shortcuts"] = ("Créer un raccourci sur le Bureau et dans le menu Démarrer", "Create Desktop and Start menu shortcuts"),
            ["Install.Install"] = ("Installer", "Install"),
            ["Install.PickFolder"] = ("Choisis où installer Akibot", "Choose where to install Akibot"),
            ["Install.Downloading"] = ("Téléchargement du runtime ({0} / {1})…", "Downloading the runtime ({0} / {1})…"),
            ["Install.Verifying"] = ("Vérification de l'intégrité (SHA-256)…", "Checking integrity (SHA-256)…"),
            ["Install.Extracting"] = ("Extraction des fichiers…", "Extracting files…"),
            ["Install.Finishing"] = ("Finalisation…", "Finishing…"),
            ["Install.Done"] = ("Installation terminée. Étape suivante : ton dump du jeu.", "Install complete. Next step: your game dump."),
            ["Install.Failed"] = ("L'installation a échoué : {0}", "Install failed: {0}"),
            ["Install.BadFolder"] = ("Ce dossier n'est pas utilisable : {0}", "This folder cannot be used: {0}"),
            ["Install.OneDrive"] = ("Évite un dossier synchronisé par OneDrive : les gros fichiers du jeu y posent problème.", "Avoid a OneDrive-synced folder: the game's large files cause problems there."),
            ["Install.NotEmpty"] = ("Ce dossier contient déjà des fichiers. Installer Akibot dedans quand même ?", "This folder already has files in it. Install Akibot there anyway?"),
            ["Install.HashMismatch"] = ("Le fichier téléchargé est corrompu (SHA-256 différent). Réessaie plus tard.", "The downloaded file is corrupt (SHA-256 mismatch). Try again later."),
            ["Install.LanguageRestart"] = ("La langue sera appliquée au prochain démarrage.", "The language applies on the next start."),

            // Runtime update
            ["Runtime.UpdateAvailable"] = (
                "Une nouvelle version du runtime ({0}) est disponible (installée : {1}). L'installer maintenant ? Le jeu sera reconstruit depuis ton dump (environ 2 minutes).",
                "A new runtime version ({0}) is available (installed: {1}). Install it now? The game will be rebuilt from your dump (about 2 minutes)."),
            ["Runtime.Rebuilding"] = ("Reconstruction du jeu depuis ton dump…", "Rebuilding the game from your dump…"),

            // Self-update
            ["Update.Available"] = ("Akibot {0} est disponible (tu as la {1}).", "Akibot {0} is available (you have {1})."),
            ["Update.AskInstall"] = ("Akibot {0} est disponible (tu as la {1}). Mettre à jour maintenant ? Akibot va redémarrer.", "Akibot {0} is available (you have {1}). Update now? Akibot will restart."),
            ["Update.UpToDate"] = ("Akibot est à jour ({0}).", "Akibot is up to date ({0})."),
            ["Update.Checking"] = ("Recherche de mises à jour…", "Checking for updates…"),
            ["Update.Downloading"] = ("Téléchargement de la mise à jour…", "Downloading the update…"),
            ["Update.Failed"] = ("La mise à jour a échoué : {0}", "Update failed: {0}"),
            ["Update.NoDigest"] = ("La release ne publie pas d'empreinte SHA-256 : mise à jour refusée par sécurité.", "The release has no SHA-256 digest: update refused for safety."),
        };
    }

    // XAML: Text="{svc:Tr Install.Title}" with xmlns:svc="clr-namespace:AstroHybrid.App.Services"
    [MarkupExtensionReturnType(typeof(string))]
    public class TrExtension : MarkupExtension
    {
        public TrExtension() { }
        public TrExtension(string key) { Key = key; }
        [ConstructorArgument("key")]
        public string Key { get; set; } = "";
        public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
    }
}
