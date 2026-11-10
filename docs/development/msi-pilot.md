# Windows Installer pilot

This is a first-install test of the existing native Windows client.
It tests normal MSI installation separately from Defender's later launch decision.
It does not change Defender, UAC, certificate trust, API settings, or model files.

## Observed installation result

On 2026-11-10 at 21:59 Europe/Bratislava, the actual installer was started with
RunAs and Windows Installer returned 1603. The MSI service failed while computing
its secure-repair content hash for app\LocalDictation.exe: Access is denied,
Windows error 5. It did not reach successful file installation.

Defender event 1121, record 21943, identifies C:\Windows\System32\msiexec.exe as
the caller and rule 01443614-CD74-433A-B99E-2ECDC07BFC25 as the blocker.
This is direct evidence that normal MSI installation did not resolve the block.
Do not change secure-repair protections to make this package install.

Post-attempt checks found product state -1 (unknown), no target installation
directory and no Start menu shortcut. No client execution was attempted after
the failed install. External application data was not an installation target.

Full local evidence is retained in the installer output directory:
install-request.json, install-result.json and install.log. These files are not
checked into Git.

## What gets installed

- Local Dictation 0.1.0, Windows x64.
- Eleven client files in C:\Program Files\Local Dictation.
- A common Start menu shortcut.
- A Windows Apps / installed-programs entry with standard MSI uninstall support.

The client is not launched by the installer. User data stays at the existing
C:\AI\LocalDictation\data location, or the existing LOCAL_DICTATION_HOME override.
The local API remains an external service; this package does not install or
restart it. The global .NET 10 Desktop Runtime is required and was verified
present on this machine.

## Build without installing

Run from the Local-Dictation repository on Windows with py (Python 3.12 or newer):

~~~powershell
py -B -m unittest discover -s tests/deployment -p test_client_msi.py -v

py -B scripts/build_client_msi.py --app-directory 'C:\AI\LocalDictation\app' --output-directory 'C:\AI\build\Local-Dictation\msi-pilot-20261110\installer'
~~~

The output directory must not already exist. The builder copies only the explicit
client-file allowlist and creates the MSI using the Windows Installer API.
No new SDK, WiX installation, private certificate export, or network download
is required.

This pilot uses an uncompressed MSI source image. Keep these together:

~~~text
installer\
    LocalDictation-0.1.0-x64.msi
    package-info.json
    app\
        LocalDictation.exe
        ...ten other required client files
~~~

The MSI alone is not a self-contained distribution.
Required files are marked vital: a failed copy must not be ignored as a
successful complete installation.

Do not change the pilot payload and treat it as a serviced release under the
same product identity. Upgrade/downgrade authoring and general distribution
hardening are outside this first-install experiment.

## Install

First confirm C:\Program Files\Local Dictation does not already contain an
unknown/manual installation. This pilot was prepared for an absent destination.

Run the following from PowerShell. Windows will request administrator approval.
Enter credentials in the Windows prompt, never in a command or chat.

~~~powershell
$msi = 'C:\AI\build\Local-Dictation\msi-pilot-20261110\installer\LocalDictation-0.1.0-x64.msi'
$log = 'C:\AI\build\Local-Dictation\msi-pilot-20261110\installer\install.log'

Start-Process 'C:\Windows\System32\msiexec.exe' -Verb RunAs -ArgumentList @('/i',('"' + $msi + '"'),'/passive','/norestart','/l*v',('"' + $log + '"')) -Wait
~~~

The package is unsigned; the publisher must not be represented as verified.
An admin approval authorizes installation, not a change to company security policy.

## Verify separately

1. Check the installer exit/result and log.
2. Confirm all eleven files, the common shortcut and product registration.
3. Launch the client as Peter with --no-hotkey, while TypeWhisper remains usable.
4. Verify an actual responsive native window. A registered MSI alone is not proof
   that Defender permits execution or that dictation works.
5. If execution is blocked, record the exact target/rule and preserve the log.
   Do not disable security controls or substitute an alternate process host.

Local install-request.json, install-result.json and install.log remain outside Git.
The observed result above is distinct from the successful package-validation
checks; validation does not establish successful installation or launch.

## Uninstall the pilot

Use Windows Settings > Apps > Installed apps > Local Dictation > Uninstall,
or the exact product code below:

~~~powershell
Start-Process 'C:\Windows\System32\msiexec.exe' -Verb RunAs -ArgumentList @('/x','{6CBA1D36-B8B7-4EE0-B283-E58EAC43DF5C}','/passive','/norestart') -Wait
~~~

Standard MSI removal targets its registered client files and shortcut.
The package has no RemoveFile wildcard or custom action and does not register
the external C:\AI\LocalDictation\data directory for deletion.

## Validation completed before the installation attempt

- Three contract tests read the actual database via Windows Installer COM.
- Payload count, target directory, x64 components, vital files, valid short
  filenames, standard actions and absence of custom actions were checked.
- Missing required payload and an existing output directory are rejected.
- Windows Installer VerifyPackage and OpenPackage succeeded.
- LaunchConditions, CostInitialize, FileCost and CostFinalize succeeded without
  running installation actions.
- An independent review found two authoring issues; both were reproduced with
  failing assertions and fixed before the privileged installation attempt.

## References

- https://learn.microsoft.com/en-us/windows/win32/msi/file-table
- https://learn.microsoft.com/en-us/windows/win32/msi/word-count-summary
- https://learn.microsoft.com/en-us/windows/win32/msi/installexecutesequence-table
