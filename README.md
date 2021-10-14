# RPIHistory

## Dependencies
* unity version 2017.3.0f — https://unity3d.com/get-unity/download/archive
* win 10 — VS 2015 — https://go.microsoft.com/fwlink/?LinkId=691978&clcid=0x409
* OSX — Xamarin Studio
  * ≥10.10  — https://www.xamarin.com/download
  * 10.9 — use same installer as above, then replace Xamarin Studio in Applications directory with http://download.xamarin.com/studio/Mac/XamarinStudio-5.9.8.0-0.dmg

## Tools
* Knowledge explorer — (creates new data sets for other knowledge domains)* https://github.com/smiled0g/knowledge-explorer/releases

## Run

* C# Backend
  * Navigate to \RPIHistory\backend\Data Entry\Data Entry\Lost Manuscript II Data Entry\bin\Release
  * Run "Dialogue Data Entry.exe" as administrator
  * Always start the backend before starting the frontend
* Unity Frontend
  * Open \RPIHistory\frontend\Assets\Scenes\rpi_history.unity
  * Currently can only be run from editor

* Changing XMLs
  * Place your XML in \RPIHistory\frontend\Assets\StreamingAssets\xml.
  * In the Unity Editor, select the loader game object.
  * In the object's Inspector, Under Load XML (Script), change the Xml_location to that of the new XML.
    * Do not use the Load XML File button, as it will set the filepath incorrectly.
    * All locations are relative to the Folder \frontend\Assets\StreamingAssets. 
    * Example: /xml/rpi_history_domain.xml

## build

* RPI Backend
  * Win 10 — build & run SLN from VS 2015 as administrator "NarrativeBackendRPI/Data Entry/Data Entry/Zeno Data Entry.sln"
  * start server: query > chat > start server

## notes
* navigation
  * press enter to minimize map at start
  * buttons and nodes can be used to navigate narrative data
  * map will scale and zoom to selected data
* debug key bindings
  * "SHIFT + D + L + 1|2|3" to load "Roman Empire|WWII|Analogy" domains
  * map
    * "SHIFT + M + L + N|E|K" to load cached "Terrain|Satelite|SateliteDark" map images
    * "SHIFT + M + S" to save currently displayed map as "Resources\maps\google_staticmap_lastSaved.png"
  * "SHIFT + C + S" to capture screenshots
  * "CTRL + D" show debug gismos
