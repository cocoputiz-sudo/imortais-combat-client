using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Runtime.CompilerServices;

// Allgemeine Informationen ÃƒÂ¼ber eine Assembly werden ÃƒÂ¼ber die folgenden
// Attribute gesteuert. Ãƒâ€žndern Sie diese Attributwerte, um die Informationen zu ÃƒÂ¤ndern,
// die einer Assembly zugeordnet sind.
[assembly: AssemblyTitle("IMORTAIS Combat Client")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("IMORTAIS")]
[assembly: AssemblyProduct("IMORTAIS Combat Client")]
[assembly: AssemblyCopyright("Copyright Ã‚Â©  2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: SupportedOSPlatform("windows")]

// Durch Festlegen von ComVisible auf FALSE werden die Typen in dieser Assembly
// fÃƒÂ¼r COM-Komponenten unsichtbar.  Wenn Sie auf einen Typ in dieser Assembly von
// COM aus zugreifen mÃƒÂ¼ssen, sollten Sie das ComVisible-Attribut fÃƒÂ¼r diesen Typ auf "True" festlegen.
[assembly: ComVisible(false)]

//Um mit dem Erstellen lokalisierbarer Anwendungen zu beginnen, legen Sie
//<UICulture>ImCodeVerwendeteKultur</UICulture> in der .csproj-Datei
//in einer <PropertyGroup> fest.  Wenn Sie in den Quelldateien beispielsweise Deutsch
//(Deutschland) verwenden, legen Sie <UICulture> auf \"de-DE\" fest.  Heben Sie dann die Auskommentierung
//des nachstehenden NeutralResourceLanguage-Attributs auf.  Aktualisieren Sie "en-US" in der nachstehenden Zeile,
//sodass es mit der UICulture-Einstellung in der Projektdatei ÃƒÂ¼bereinstimmt.

//[assembly: NeutralResourcesLanguage("en-US", UltimateResourceFallbackLocation.Satellite)]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None, //Speicherort der designspezifischen RessourcenwÃƒÂ¶rterbÃƒÂ¼cher
                                     //(wird verwendet, wenn eine Ressource auf der Seite nicht gefunden wird,
                                     // oder in den Anwendungsressourcen-WÃƒÂ¶rterbÃƒÂ¼chern nicht gefunden werden kann.)
    ResourceDictionaryLocation.SourceAssembly //Speicherort des generischen RessourcenwÃƒÂ¶rterbuchs
                                              //(wird verwendet, wenn eine Ressource auf der Seite nicht gefunden wird,
                                              // designspezifischen RessourcenwÃƒÂ¶rterbuch nicht gefunden werden kann.)
)]

// Tests
[assembly: InternalsVisibleTo("StatisticsAnalysisTool.Tests")]

// Versionsinformationen fÃƒÂ¼r eine Assembly bestehen aus den folgenden vier Werten:
//
//      Hauptversion
//      Nebenversion
//      Buildnummer
//      Revision
//
// Sie kÃƒÂ¶nnen alle Werte angeben oder Standardwerte fÃƒÂ¼r die Build- und Revisionsnummern verwenden,
// indem Sie "*" wie unten gezeigt eingeben:
// [assembly: AssemblyVersion("0.4.4.0")]
[assembly: AssemblyVersion("0.4.4.0")]
[assembly: AssemblyFileVersion("0.4.4.0")]
[assembly: AssemblyInformationalVersion("0.4.4")]
