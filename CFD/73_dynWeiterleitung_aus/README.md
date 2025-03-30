
## Dynamische Weiterleitungen ##

### 73_dynWeiterleitung_aus ###
Das Skript *73 schaltet die Nebenstelle auf den Status "Verfügbar" um um die Weiterleitung zu deaktivieren.

Nutzungsbeispiel: *73 anrufen     um die aktuelle Nebenstelle in den Status Verfügbar umzuschalten (die Weiterleitung zu deaktivieren)


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
