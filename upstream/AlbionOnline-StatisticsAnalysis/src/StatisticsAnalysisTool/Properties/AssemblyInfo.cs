using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Runtime.CompilerServices;

// Allgemeine Informationen ÃƒÆ’Ã‚Â¼ber eine Assembly werden ÃƒÆ’Ã‚Â¼ber die folgenden
// Attribute gesteuert. ÃƒÆ’Ã¢â‚¬Å¾ndern Sie diese Attributwerte, um die Informationen zu ÃƒÆ’Ã‚Â¤ndern,
// die einer Assembly zugeordnet sind.
[assembly: AssemblyTitle("IMORTAIS Combat Client")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("IMORTAIS")]
[assembly: AssemblyProduct("IMORTAIS Combat Client")]
[assembly: AssemblyCopyright("Copyright Ãƒâ€šÃ‚Â©  2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: SupportedOSPlatform("windows")]

// Durch Festlegen von ComVisible auf FALSE werden die Typen in dieser Assembly
// fÃƒÆ’Ã‚Â¼r COM-Komponenten unsichtbar.  Wenn Sie auf einen Typ in dieser Assembly von
// COM aus zugreifen mÃƒÆ’Ã‚Â¼ssen, sollten Sie das ComVisible-Attribut fÃƒÆ’Ã‚Â¼r diesen Typ auf "True" festlegen.
[assembly: ComVisible(false)]

//Um mit dem Erstellen lokalisierbarer Anwendungen zu beginnen, legen Sie
//<UICulture>ImCodeVerwendeteKultur</UICulture> in der .csproj-Datei
//in einer <PropertyGroup> fest.  Wenn Sie in den Quelldateien beispielsweise Deutsch
//(Deutschland) verwenden, legen Sie <UICulture> auf \"de-DE\" fest.  Heben Sie dann die Auskommentierung
//des nachstehenden NeutralResourceLanguage-Attributs auf.  Aktualisieren Sie "en-US" in der nachstehenden Zeile,
//sodass es mit der UICulture-Einstellung in der Projektdatei ÃƒÆ’Ã‚Â¼bereinstimmt.

//[assembly: NeutralResourcesLanguage("en-US", UltimateResourceFallbackLocation.Satellite)]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None, //Speicherort der designspezifischen RessourcenwÃƒÆ’Ã‚Â¶rterbÃƒÆ’Ã‚Â¼cher
                                     //(wird verwendet, wenn eine Ressource auf der Seite nicht gefunden wird,
                                     // oder in den Anwendungsressourcen-WÃƒÆ’Ã‚Â¶rterbÃƒÆ’Ã‚Â¼chern nicht gefunden werden kann.)
    ResourceDictionaryLocation.SourceAssembly //Speicherort des generischen RessourcenwÃƒÆ’Ã‚Â¶rterbuchs
                                              //(wird verwendet, wenn eine Ressource auf der Seite nicht gefunden wird,
                                              // designspezifischen RessourcenwÃƒÆ’Ã‚Â¶rterbuch nicht gefunden werden kann.)
)]

// Tests
[assembly: InternalsVisibleTo("StatisticsAnalysisTool.Tests")]

// Versionsinformationen fÃƒÆ’Ã‚Â¼r eine Assembly bestehen aus den folgenden vier Werten:
//
//      Hauptversion
//      Nebenversion
//      Buildnummer
//      Revision
//
// Sie kÃƒÆ’Ã‚Â¶nnen alle Werte angeben oder Standardwerte fÃƒÆ’Ã‚Â¼r die Build- und Revisionsnummern verwenden,
// indem Sie "*" wie unten gezeigt eingeben:
// AssemblyVersion is intentionally stable across 0.4.x releases.
[assembly: AssemblyVersion("0.4.4.0")]
[assembly: AssemblyFileVersion("0.5.3.0")]
[assembly: AssemblyInformationalVersion("0.5.3")]
