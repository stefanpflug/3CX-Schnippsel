# 3CX

Die importierebaren ZIP Dateien liegen im Ordner /RELEASE

##Dynamische Weiterleitungen##
###72_dynWeiterleitung_an###
Das Skript *72 fragt mittels DTFM die Zielrufnummer der Weiterleitung ab, trägt diese in das Weiterleitungsziel des "Benutzerdefinierter Status 2" und schaltet den Status der anrufenden Nebenstelle auf diesen um.
Das Ziel wird mittels DTMF übergeben. Bei Tischtelefonen kann dies wie unten beschrieben auf BLF Tasten gelegt werden. 
Bei Apps und DECT Telefonen kann die Zielrufnummer nur über die Telefontastatur eingegeben werden.

Nutzungsbeispiel: *72 anrufen, "0172123456#" eingeben um eine Weiterleitung auf die Nummer 0172123456 einzurichten

###73_dynWeiterleitung_aus###
Das Skript *73 schaltet die Nebenstelle auf den Status "Verfügbar" um um die Weiterleitung zu deaktivieren.

Nutzungsbeispiel: *73 anrufen     um die aktuelle Nebenstelle in den Status Verfügbar umzuschalten (die Weiterleitung zu deaktivieren)


###721_dynWeiterleitung_mitExtension_an###
Das Skript *721 arbeitet wie 72_dynWeiterleitung_an, allerdings wird zuerst die zu ändernde Nebenstelle mittels DTMF abgefragt.
Anschließend wird mittels DTFM die Zielrufnummer der Weiterleitung abgefragt, diese in das Weiterleitungsziel des "Benutzerdefinierter Status 2" eingetragen und schaltet den Status der angegebenen Nebenstelle auf diesen umgeschaltet.
Die Nebenstelle und das Ziel wird mittels DTMF übergeben. Bei Tischtelefonen kann dies wie unten beschrieben auf BLF Tasten gelegt werden. 
Bei Apps und DECT Telefonen kann die Zielrufnummer nur über die Telefontastatur eingegeben werden.

Nutzungsbeispiel: *721 anrufen, "80#83#" eingeben um für die Nebenstelle 80 eine Weiterleitung auf die Nummer IVR 83 einzurichten
Dies kann z.B. verwendet werden um die Dummy Nebenstelle 80 zwischen dem Anrufbeantworter "81 TAG", dem Anrufbeantworter "82 Nacht" oder "83 Urlaub" umzuschalten.



##Eigene Änderungen##
Der CFD erlaubt es nicht Projekte mit einem "*" im Wählcode zu erstellen.
Nach dem Build entpackt man einmal die ZIP Datei im Ordner \Output\Release und ändert in der manifest.xml die Extension von z.B. 72 auf *72
Anschließend verpackt man das ganze wieder in die ZIP Datei.


##Funktion auf BLF Tasten von Tischtelefonen legen##
Die CFDs können ohne weitere Interaktion auf BLFs von Tischtelefonen provisioniert werden. Der Syntax ist vom Hersteller abhängig.

- Yealink BLF Tasten als indiv. Kurzwahl definieren :
	z.B. Wtlg Handy: *72,0172123456#
	z.B. Wtlg Nst10: *72,10#
	z.B. Wtlg aus: *73
- Fanvil BLF Tasten als indiv. Kurzwahl definieren :
	z.B. Wtlg Handy: *72,0172123456#
	z.B. Wtlg Nst10: *72,10#
	z.B. Wtlg aus: *73
- Snom (ungetestet) BLF Tasten als indiv. Kurzwahl definieren:
	z.B. Wtlg Handy: *72;dtmf=0172123456#
	z.B. Wtlg Nst10: *72;dtmf=10#
	z.B. Wtlg aus: *73

