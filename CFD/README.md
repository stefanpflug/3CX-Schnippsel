
Die importierebaren ZIP Dateien liegen im Ordner /RELEASE

## Profilstatus wechseln ##
### 39_Profilstatus_mitExtension ###
Das Skript *39 ermöglicht das wechseln des Profilstatus einer beliebigen Nebenstelle
Mittels DTMF wird zuerst die Nebenstelle abgefragt und dann die ID des Status (0-4, wobei 0 = Verfügbar).
Bei Tischtelefonen kann dies wie unten beschrieben auf BLF Tasten gelegt werden. 
Bei Apps und DECT Telefonen kann die Zielrufnummer nur über die Telefontastatur eingegeben werden.

Nutzungsbeispiel: *39 anrufen, "40#2#" eingeben um für die Nebenstelle 40 auf "Bitte nicht stören umzuschalten.


## 99_Mailbox_NurAnsage ##
Das Skript *99 stellt eine Ansage ohne Aufnahmemöglichkeit zur Verfügung, die der Nutzer (im Gegensatz zur IVR Lösung) selbst besprechen/verändern kann.
Der Aufruf erfolgt als Ziel über die Anrufweiterleitung in der Nebenstelle. Die Ansagen werden aus den im Status des Benutzers hinterlegten Ansagen bezogen.
Ist dort keine gefüllt, so erfolgt ein Fallback auf die Standardansage (Register Mailbox).

Da in v20 die ursprünglich angerufene Nebenstelle als Quelle der Ansagen nur indirekt mit schwarzer Magie ermittelt werden kann, 
kann dieses CFA nur bei direkter Weiterleitung aus der Nebenstelle (10 -> *99) verwendet werden. Also kein Nebenstellenhopping (10->11->*99), nicht als IVR Ziel, ...)

## Dynamische Weiterleitungen ##
### 72_dynWeiterleitung_an ###
Das Skript *72 fragt mittels DTFM die Zielrufnummer der Weiterleitung ab, trägt diese in das Weiterleitungsziel des "Benutzerdefinierter Status 2" und schaltet den Status der anrufenden Nebenstelle auf diesen um.
Das Ziel wird mittels DTMF übergeben. Bei Tischtelefonen kann dies wie unten beschrieben auf BLF Tasten gelegt werden. 
Bei Apps und DECT Telefonen kann die Zielrufnummer nur über die Telefontastatur eingegeben werden.

Nutzungsbeispiel: *72 anrufen, "0172123456#" eingeben um eine Weiterleitung auf die Nummer 0172123456 einzurichten

### 73_dynWeiterleitung_aus ###
Das Skript *73 schaltet die Nebenstelle auf den Status "Verfügbar" um um die Weiterleitung zu deaktivieren.

Nutzungsbeispiel: *73 anrufen     um die aktuelle Nebenstelle in den Status Verfügbar umzuschalten (die Weiterleitung zu deaktivieren)


### 721_dynWeiterleitung_mitExtension_an ###
Das Skript *721 arbeitet wie 72_dynWeiterleitung_an, allerdings wird zuerst die zu ändernde Nebenstelle mittels DTMF abgefragt.
Anschließend wird mittels DTFM die Zielrufnummer der Weiterleitung abgefragt, diese in das Weiterleitungsziel des "Benutzerdefinierter Status 2" eingetragen und schaltet den Status der angegebenen Nebenstelle auf diesen umgeschaltet.
Die Nebenstelle und das Ziel wird mittels DTMF übergeben. Bei Tischtelefonen kann dies wie unten beschrieben auf BLF Tasten gelegt werden. 
Bei Apps und DECT Telefonen kann die Zielrufnummer nur über die Telefontastatur eingegeben werden.

Nutzungsbeispiel: *721 anrufen, "80#83#" eingeben um für die Nebenstelle 80 eine Weiterleitung auf die Nummer IVR 83 einzurichten
Dies kann z.B. verwendet werden um die Dummy Nebenstelle 80 zwischen dem Anrufbeantworter "81 TAG", dem Anrufbeantworter "82 Nacht" oder "83 Urlaub" umzuschalten.




## Funktion auf BLF Tasten von Tischtelefonen legen ##
Die CFDs können ohne weitere Interaktion auf BLFs von Tischtelefonen provisioniert werden. Der Syntax ist vom Hersteller abhängig.

- Yealink BLF Tasten als indiv. Kurzwahl definieren :
	z.B. Wtlg Handy: *72,0172123456#<br>
	z.B. Wtlg Nst10: *72,10#<br>
	z.B. Wtlg aus: *73<br>
- Fanvil BLF Tasten als indiv. Kurzwahl definieren :<br>
	z.B. Wtlg Handy: *72,0172123456#<br>
	z.B. Wtlg Nst10: *72,10#<br>
	z.B. Wtlg aus: *73<br>
- Snom (ungetestet) BLF Tasten als indiv. Kurzwahl definieren:<br>
	z.B. Wtlg Handy: *72;dtmf=0172123456#<br>
	z.B. Wtlg Nst10: *72;dtmf=10#<br>
	z.B. Wtlg aus: *73<br>


*Quellen / Nützliche Tools*
- [Ausgangspunkt war der Schnippsel von fxbastler aus dem 3CX Forum](https://www.3cx.de/forum/threads/rufweiterleitung.101354/page-2#post-430429)
- [Text-To-Speech German Vicky](https://ttsmp3.com/text-to-speech/German/)
- [g711.org mp3 zu wav konvertieren](https://g711.org/)
- [3CX Call Control API Doku](https://downloads-global.3cx.com/downloads/misc/callcontrolapi/3CXCallControlAPI_v20.zip)
