@echo off
rem Opens the BlendFarm ports in Windows Firewall, once per computer.
rem The rules are for the ports, not for one exe, so future BlendFarm versions
rem in any folder work without asking for an admin password again.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Asking for admin rights to add the firewall rules...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

netsh advfirewall firewall delete rule name="BlendFarm Server" >nul 2>&1
netsh advfirewall firewall delete rule name="BlendFarm Discovery" >nul 2>&1
netsh advfirewall firewall add rule name="BlendFarm Server" dir=in action=allow protocol=TCP localport=15000
netsh advfirewall firewall add rule name="BlendFarm Discovery" dir=in action=allow protocol=UDP localport=16342

echo.
echo Done. Other computers can now reach BlendFarm on this one (TCP 15000, UDP 16342).
pause
