# Native delivery probe

This interactive fixture exercises the actual WindowsTextDelivery,
AutomationWorker, and GlobalInput code against synthetic WPF text fields.
It does not start the dictation app, record audio, run models, or insert into
an external application.

## Run

Build or publish the project into a task-local folder under C:\Git. Start
DeliveryProbe.exe, optionally providing an absolute JSON report path:

~~~powershell
DeliveryProbe.exe --report C:\AI\LocalDictation\diagnostics\delivery-probe.json
~~~

Click Run checks in the fixture. It manages its own fields and temporary
Review window while the real product adapter performs bounded native input.
Do not interact with other applications until the PASS or FAIL result appears.

The fixture checks insertion between preserved prefix/suffix text, pausing
at another field, resuming from a cursor inside the owned draft, draft
withdrawal for Review, approved reinsertion, manual-edit conflict, and a
closed destination. The JSON contains synthetic fixture text and results only.

A passing fixture does not establish compatibility with Codex, Notepad, or
every other application's accessibility provider. Those need their own
application-level acceptance checks.
