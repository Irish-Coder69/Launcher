; Launcher Application Installer
; NSIS Configuration Script
; Requires NSIS 3.0 or later

;--------------------------------
; Variables
;--------------------------------
!define PRODUCT_NAME "Launcher"
!define PRODUCT_VERSION "1.1.6"
!define PRODUCT_PUBLISHER "Windsor Industries"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!ifndef OUTDIR
!define OUTDIR "Output"
!endif

;--------------------------------
; General
;--------------------------------
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetDatablockOptimize on
SetOverwrite ifnewer
CRCCheck on
Unicode True

;--------------------------------
; MUI Settings
;--------------------------------
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_NOREBOOTSUPPORT

;--------------------------------
; Pages
;--------------------------------
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "LICENSE.txt"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

;--------------------------------
; Language
;--------------------------------
!insertmacro MUI_LANGUAGE "English"

;--------------------------------
; Installer Attributes
;--------------------------------
Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "${OUTDIR}\Launcher-${PRODUCT_VERSION}-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\${PRODUCT_NAME}"
ShowInstDetails show
ShowUnInstDetails show

;--------------------------------
; Version Information
;--------------------------------
VIProductVersion "${PRODUCT_VERSION}.0"
VIAddVersionKey ProductName "${PRODUCT_NAME}"
VIAddVersionKey ProductVersion "${PRODUCT_VERSION}"
VIAddVersionKey CompanyName "${PRODUCT_PUBLISHER}"
VIAddVersionKey FileVersion "${PRODUCT_VERSION}"
VIAddVersionKey FileDescription "${PRODUCT_NAME} - Application Launcher"
VIAddVersionKey LegalCopyright "Copyright 2026 ${PRODUCT_PUBLISHER}"
VIAddVersionKey OriginalFilename "Launcher-${PRODUCT_VERSION}-Setup.exe"

;--------------------------------
; Section Description Strings
;--------------------------------
LangString DESC_SEC01 ${LANG_ENGLISH} "Installs the Launcher native app, script bridge, configuration, and supporting files."
LangString DESC_SEC02 ${LANG_ENGLISH} "Creates shortcuts in the Windows Start Menu."
LangString DESC_SEC03 ${LANG_ENGLISH} "Creates a shortcut on the Desktop."

;--------------------------------
; Sections
;--------------------------------
Section "!${PRODUCT_NAME} (required)" SEC01
  SetOutPath "$INSTDIR"
  SetOverwrite try
  
  ; Core files
  File "..\launcher.ps1"
  File "..\launcher.config.json"
  File /oname=Launcher.exe "..\native\publish\win-x64\Launcher.App.exe"

  ; Update subfolder
  SetOutPath "$INSTDIR\update"
  File "..\update\Check-LauncherUpdate.ps1"
  File "..\update\Install-LauncherUpdate.ps1"
  File "..\update\Update-Integration.ps1"
  File "..\update\versions.json"
  SetOutPath "$INSTDIR"

  ; Write version file for update checks
  FileOpen $0 "$INSTDIR\version.txt" w
  FileWrite $0 "${PRODUCT_VERSION}"
  FileClose $0

SectionEnd

Section "Start Menu Shortcuts" SEC02
  CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
  CreateShortcut "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk" "$INSTDIR\Launcher.exe" "" "$INSTDIR\Launcher.exe" 0
  CreateShortcut "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall ${PRODUCT_NAME}.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

Section "Desktop Shortcut" SEC03
  CreateShortcut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\Launcher.exe" "" "$INSTDIR\Launcher.exe" 0
SectionEnd

Section -Post
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\Launcher.exe"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoRepair" 1
SectionEnd

;--------------------------------
; Section Descriptions
;--------------------------------
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC01} $(DESC_SEC01)
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC02} $(DESC_SEC02)
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC03} $(DESC_SEC03)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

;--------------------------------
; Uninstaller Sections
;--------------------------------
Section Uninstall
  ; Core files
  Delete "$INSTDIR\launcher.ps1"
  Delete "$INSTDIR\launcher.config.json"
  Delete "$INSTDIR\Launcher.exe"
  Delete "$INSTDIR\version.txt"
  Delete "$INSTDIR\launcher.last.log"
  Delete "$INSTDIR\Uninstall.exe"

  ; Update subfolder
  Delete "$INSTDIR\update\Check-LauncherUpdate.ps1"
  Delete "$INSTDIR\update\Install-LauncherUpdate.ps1"
  Delete "$INSTDIR\update\Update-Integration.ps1"
  Delete "$INSTDIR\update\versions.json"
  RMDir "$INSTDIR\update"

  RMDir "$INSTDIR"

  ; Shortcuts
  RMDir /r "$SMPROGRAMS\${PRODUCT_NAME}"
  Delete "$DESKTOP\${PRODUCT_NAME}.lnk"

  ; Registry
  DeleteRegKey HKCU "${PRODUCT_UNINST_KEY}"

  SetAutoClose true
SectionEnd

;--------------------------------
; Installer Functions
;--------------------------------
Function .onInit
  ReadRegStr $0 HKCU "${PRODUCT_UNINST_KEY}" "UninstallString"
  ${If} $0 != ""
    MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION \
      "${PRODUCT_NAME} is already installed.$\n$\nClick OK to remove the previous version first, or CANCEL to abort." \
      IDOK removePreviousVersion
    Abort
removePreviousVersion:
    ExecWait '$0 _?=$INSTDIR'
  ${EndIf}
FunctionEnd

Function .onInstSuccess
  MessageBox MB_YESNO|MB_ICONQUESTION \
    "Installation complete!$\n$\nWould you like to launch ${PRODUCT_NAME} now?" \
    IDYES launchInstalledApp
  Goto doneLaunching

launchInstalledApp:
  ; Launch the native WPF app directly.
  ExecShell "open" "$INSTDIR\Launcher.exe"
doneLaunching:
FunctionEnd

;--------------------------------
; Uninstaller Functions
;--------------------------------
Function un.onInit
  MessageBox MB_ICONQUESTION|MB_YESNO|MB_DEFBUTTON2 \
    "Are you sure you want to completely remove $(^Name) and all of its components?" \
    IDYES +2
  Abort
FunctionEnd
