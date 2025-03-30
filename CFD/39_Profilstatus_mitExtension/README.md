# Profilstatus wechseln
### 39_Profilstatus_mitExtension ###
Das Skript *39 ermöglicht das wechseln des Profilstatus einer beliebigen Nebenstelle
Mittels DTMF wird zuerst die Nebenstelle abgefragt und dann die ID des Status (0-4, wobei 0 = Verfügbar).
Bei Tischtelefonen kann dies wie unten beschrieben auf BLF Tasten gelegt werden. 
Bei Apps und DECT Telefonen kann die Zielrufnummer nur über die Telefontastatur eingegeben werden.

Nutzungsbeispiel: *39 anrufen, "40#2#" eingeben um für die Nebenstelle 40 auf "Bitte nicht stören umzuschalten.



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


