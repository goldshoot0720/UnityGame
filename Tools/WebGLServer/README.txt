萌友遊戲大廳 MoeGames 網頁版

瀏覽器不允許直接雙擊 index.html 執行 Unity 網頁遊戲，必須透過網頁伺服器開啟。
本資料夾已附上一鍵啟動腳本：

  Windows：雙擊 start.bat（使用 Windows 內建 PowerShell，免安裝）
  macOS  ：雙擊 start.command（第一次若被阻擋：右鍵 →「打開」）
  Linux  ：在終端機執行 ./start.sh

啟動後會自動開啟瀏覽器 http://localhost:8080/
（8080 被佔用時 Windows 版會自動換下一個埠；也可指定：start.bat 9000 / ./start.sh 9000）
結束遊戲：關閉伺服器視窗，或在視窗中按 Ctrl+C。
