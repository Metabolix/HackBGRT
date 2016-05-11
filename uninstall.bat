@ECHO OFF
CD %~dp0

IF NOT EXIST install.bat (
	ECHO The uninstaller needs install.bat!
	EXIT /B 1
)

CALL install.bat uninstall
