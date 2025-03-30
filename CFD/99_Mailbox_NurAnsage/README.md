# 3CX


### 99_Mailbox_NurAnsage ###
Das Skript *99 stellt eine Ansage ohne Aufnahmemöglichkeit zur Verfügung, die der Nutzer (im Gegensatz zur IVR Lösung) selbst besprechen/verändern kann.
Der Aufruf erfolgt als Ziel über die Anrufweiterleitung in der Nebenstelle. Die Ansagen werden aus den im Status des Benutzers hinterlegten Ansagen bezogen.
Ist dort keine gefüllt, so erfolgt ein Fallback auf die Standardansage (Register Mailbox).

Da in v20 die ursprünglich angerufene Nebenstelle als Quelle der Ansagen nur indirekt mit schwarzer Magie ermittelt werden kann, 
kann dieses CFA nur bei direkter Weiterleitung aus der Nebenstelle (10 -> *99) verwendet werden. Also kein Nebenstellenhopping (10->11->*99), nicht als IVR Ziel, ...)