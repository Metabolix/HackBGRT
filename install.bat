@ECHO OFF
CD %~dp0

IF NOT "%1" == "uninstall" (
	IF NOT EXIST bootx64.efi (
		ECHO Missing bootx64.efi, you're doing something wrong.
		GOTO fail_before_esp
	)
)

SET ESP_UNMOUNT=1
SET ESP=-
FOR /F "delims=" %%I IN ('CMD /C "MOUNTVOL | FINDSTR /C:EFI | FINDSTR /C::"') DO (
	ECHO %%I
	SET ESP_STR=%%I
	SET ESP=%ESP_STR:~-3,2%
	SET ESP_UNMOUNT=0
)
IF %ESP% == - MOUNTVOL S: /S >NUL && SET ESP=S:
IF %ESP% == - MOUNTVOL B: /S >NUL && SET ESP=B:
IF %ESP% == - MOUNTVOL A: /S >NUL && SET ESP=A:
IF %ESP% == - MOUNTVOL X: /S >NUL && SET ESP=X:

IF %ESP% == - (
	ECHO The EFI System Partition is not mounted.
	GOTO fail_before_esp
)

SET HackBGRT=%ESP%\EFI\HackBGRT
SET MSBOOT=%ESP%\EFI\Microsoft\Boot

IF NOT EXIST %MSBOOT% (
	ECHO %MSBOOT% does not exist.
	ECHO If the path seems incorrect, report a bug.
	GOTO fail
)

IF "%1" == "uninstall" (
	IF NOT EXIST %HackBGRT%\bootmgfw-original.efi (
		ECHO Missing %HackBGRT%\bootmgfw-original.efi!
		GOTO fail
	)
	COPY %HackBGRT%\bootmgfw-original.efi %MSBOOT%\bootmgfw.efi >NUL || (
		ECHO Failed to restore the original bootmgfw.efi.
		GOTO fail
	)
	ECHO The original bootmgfw.efi has been restored.
	IF EXIST %HackBGRT% (
		DEL /P %HackBGRT%
	)
	EXIT /B
)

IF NOT EXIST %HackBGRT% (
	MKDIR %HackBGRT%
)
IF NOT EXIST %HackBGRT%\bootmgfw-original.efi (
	COPY %MSBOOT%\bootmgfw.efi %HackBGRT%\bootmgfw-original.efi >NUL || (
		ECHO Couldn't copy the original bootmgfw.efi.
		GOTO fail
	)
)

ECHO Copying files...
COPY /Y LICENSE %HackBGRT%\ >NUL
COPY /Y README.md %HackBGRT%\ >NUL
COPY /Y README.efilib %HackBGRT%\ >NUL
COPY /Y install.bat %HackBGRT%\ >NUL
COPY /Y uninstall.bat %HackBGRT%\ >NUL
COPY /Y bootx64.efi %HackBGRT%\ >NUL || GOTO fail
IF NOT EXIST %HackBGRT%\splash.bmp (
	COPY splash.bmp %HackBGRT%\ >NUL || GOTO fail
)
IF EXIST %HackBGRT%\config.txt (
	ECHO Copying configuration as config-new.txt.
	ECHO Be sure to check for any format changes!
	COPY /Y config.txt %HackBGRT%\config-new.txt >NUL || GOTO fail
) ELSE (
	COPY /Y config.txt %HackBGRT%\config.txt >NUL || GOTO fail
)

ECHO Draw or copy your preferred image to splash.bmp.
START /WAIT mspaint %HackBGRT%\splash.bmp

ECHO Check the configuration in config.txt.
IF EXIST %HackBGRT%\config-new.txt (
	ECHO See config-new.txt for reference.
	START notepad %HackBGRT%\config-new.txt
)
START /WAIT notepad %HackBGRT%\config.txt

ECHO Replacing bootmgfw.efi.
COPY /Y bootx64.efi %MSBOOT%\bootmgfw.efi >NUL || (
	ECHO Failed to copy the boot loader!
	ECHO Restoring the original bootmgfw.efi...
	COPY %HackBGRT%\bootmgfw-original.efi %MSBOOT%\bootmgfw.efi >NUL || (
		ECHO Restoration failed You will need to fix this!
	)
	GOTO fail
)

IF %ESP_UNMOUNT% == 1 (
	MOUNTVOL %ESP% /D
)

ECHO Installation is ready.
ECHO If your CPU is not x86-64, you should definitely uninstall now.
ECHO Remember to disable Secure Boot, or HackBGRT will not boot.
PAUSE
EXIT /B

:fail
IF %ESP_UNMOUNT% == 1 (
	MOUNTVOL %ESP% /D
)

:fail_before_esp
ECHO Exiting due to errors.
PAUSE
EXIT /B 1
