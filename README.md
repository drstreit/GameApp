# GameApp
Capture game screenshots into a dir via global hotkey and process that information to a GSheet for further processing
The application uses several Console apps to perform different tasks:
ConsoleMain allows you to pick a running applciation to define the screen capture area. E.g. start Notepad and pull/resize it on top of your game windos (needs to run as "Windowed/Fullscreen" (New World default mode)) to define exactrly the area to capture - e.g. the list of trades. A global hotkey (CTRL-Tab) will be installed that triggers the capture of that area from a game. Status: Finished, needs polish.
TextExtract will monitor a directory (currently: d:\tmp) for BMP files. It will extract text found there (using Tesseract) and write that text into a text file. Status: Finished, needs polish.
InsertTextIntoGSheet monitors a directory for texct files and stores the content into GSheet. It will use a copy of the NW Trademaster Sheet to compare the items found with the trademaster item list. This (semi-) automates the filling of current prices into the sheet to calculate valuable craftings or trades.

