# Local Dictation test-signing result

Date: 2026-11-10

## Result

The requested self-signed test identity was created. Both first-party DLLs were
signed in an isolated copy of the client. The EXE could not be signed because
Defender denied SignTool access to it.

This is a partial signing result, not a runnable or fully signed release.
Original client binaries, source, runtime data and Windows trust stores were
not changed.

## Local artifacts

Test copy:

    C:\AI\LocalDictation\signing\test-20261110\app

Public certificate (no private key):

    C:\AI\LocalDictation\signing\test-20261110\LocalDictation-TestSigning.cer

Local metadata and signing output are in the same test-20261110 parent directory.
The certificate and generated binaries are not checked into Git.

Certificate subject:

    CN=Local Dictation Test Signing - Peter Chnurik

Thumbprint:

    CEFFD453668583F3A031ACCCD6C91D974BEB07FF

Public CER SHA-256:

    8358C0B88BBC976875C7ADA52D6DF629B9728578650689399DC69ECED91F4B7C

The RSA-3072 / SHA-256 certificate has the Code Signing EKU and a non-exportable
private key in Peter's CurrentUser\My store. It expires on 2026-12-10.
No private-key file or PFX was exported. No external timestamp was requested.

## Verification

- LocalDictation.dll and LocalDictation.Core.dll: SignTool reported success.
- Both DLLs report the expected signer thumbprint.
- SignTool verification finds the signature but fails on the untrusted
  self-signed certificate chain, as expected before explicitly trusting it.
- LocalDictation.exe: SignTool returned Access is denied.
- Defender event 1121, record 21919, at 19:33:39 Europe/Bratislava identifies
  signtool.exe as the caller, the test-copy EXE as the target, and ASR rule
  01443614-CD74-433A-B99E-2ECDC07BFC25 as the blocker.
- The new certificate was absent from CurrentUser and LocalMachine Root,
  TrustedPeople and TrustedPublisher stores after the attempt.

A certificate-trust import will not add a signature to the unsigned EXE or
authorize SignTool to open a file denied by ASR. Resolve the denied access through
an authorized policy/build-signing route before repeating EXE signing. Do not
disable Defender or use another process host to run the blocked payload.

## How to approve this certificate's trust

These instructions are provided at Peter's explicit request. They have not been
executed. On this managed company PC, the operator needs authority to approve
test signing trust; an installation admin account alone does not establish that
scope.

Use Peter's normal PowerShell session for the user-scoped test. CurrentUser
means the account running PowerShell. Opening it as a different admin account
would change that admin profile's trust instead of Peter's.

The following first checks the exact public certificate. The final command adds
this one identity to Peter's Trusted Root Certification Authorities store.
It trusts signatures made with this test key, not only one file hash. It does
not change Defender or ASR policy.

~~~powershell
$cerPath = 'C:\AI\LocalDictation\signing\test-20261110\LocalDictation-TestSigning.cer'
$expectedThumbprint = 'CEFFD453668583F3A031ACCCD6C91D974BEB07FF'
$expectedSha256 = '8358C0B88BBC976875C7ADA52D6DF629B9728578650689399DC69ECED91F4B7C'

if ((Get-FileHash -LiteralPath $cerPath -Algorithm SHA256).Hash -ne $expectedSha256) {
    throw 'Unexpected public certificate file.'
}

$publicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($cerPath)
if ($publicCertificate.Thumbprint -ne $expectedThumbprint -or $publicCertificate.HasPrivateKey) {
    throw 'Unexpected certificate identity or private-key content.'
}

Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\CurrentUser\Root'
~~~

Review and personally confirm any Windows trust warning. If company policy
rejects the operation, report the rejection rather than changing policy.
Do not import the private key or grant machine-wide trust as an automatic fallback.

After approved import, verify an already signed DLL:

~~~powershell
Get-AuthenticodeSignature -LiteralPath 'C:\AI\LocalDictation\signing\test-20261110\app\LocalDictation.dll'
~~~

Expected after effective certificate trust: Status Valid and the thumbprint above.
This verifies a DLL signature; it is not an EXE launch acceptance test.

## Complete EXE signing after approved access

Run as Peter, whose user store owns the non-exportable key. The following changes
only the isolated test-copy EXE; run it only after its access restriction has
been resolved through the authorized route.

~~~powershell
$signTool = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe'
$exePath = 'C:\AI\LocalDictation\signing\test-20261110\app\LocalDictation.exe'

& $signTool sign /fd SHA256 /s My /sha1 'CEFFD453668583F3A031ACCCD6C91D974BEB07FF' $exePath
if ($LASTEXITCODE -ne 0) { throw 'EXE signing failed.' }

& $signTool verify /pa /v $exePath
if ($LASTEXITCODE -ne 0) { throw 'EXE signature verification failed.' }
~~~

A complete signed payload still requires ordinary-user native acceptance and
policy approval. No successful client startup has been observed.

## Remove test trust

After testing, remove only the exact test identity from the same user trust store:

~~~powershell
Remove-Item -LiteralPath 'Cert:\CurrentUser\Root\CEFFD453668583F3A031ACCCD6C91D974BEB07FF'
~~~

This removes the user trust entry. It does not delete app data or the signing key.
Keep the public certificate and logs as evidence until the test is closed.

## Microsoft references

- SignTool: https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool
- Test certificate trust for Authenticode:
  https://learn.microsoft.com/en-us/windows/win32/dxtecharts/authenticode-signing-for-game-developers#example-using-a-test-certificate
- ASR rule:
  https://learn.microsoft.com/en-us/defender-endpoint/attack-surface-reduction-rules-reference#block-executable-files-from-running-unless-they-meet-a-prevalence-age-or-trusted-list-criterion
