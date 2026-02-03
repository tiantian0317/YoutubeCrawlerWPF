@echo off
cd /d "c:\Users\Administrator\GIT\YoutubeExplode\YoutubeCrawlerWPF"
dotnet build YoutubeCrawlerWPF.csproj > build_error.txt 2>&1
type build_error.txt
pause
