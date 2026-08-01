
# Developer Notes
How to create a self contained executable

Clean and Publish

In Visual Studio, go to Build -> Clean Solution.
Open the Terminal (View -> Terminal).
Run this simple command:
     
`dotnet publish -c Release`

Find your Single EXE

When the command finishes, look in this exact folder:

\bin\Release\net8.0-windows\win-x64\publish\

Inside that publish folder, you will find only one file: BedrockServerManager.exe. You can copy that single .exe to any Windows computer and run it directly without installing anything!
