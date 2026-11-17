import unittest
from pathlib import Path
from local_dictation.comparison import comparison_identity
from local_dictation.application import create_app
from local_dictation.config import RuntimeConfig
from local_dictation.sessions import AudioSession
from test_sessions import FakeEngine

class ProvenanceTests(unittest.IsolatedAsyncioTestCase):
    async def test_start_and_comparison_keep_original_pinned_revisions(self):
        events=[]
        async def send(e):events.append(e)
        options={"model":"fast","verification_model":"verify","primary_revision":"a"*40,
                 "verification_revision":"b"*40}
        session=AudioSession(RuntimeConfig(Path("."),Path(".")),FakeEngine(),options,send)
        options["primary_revision"]="c"*40
        session.feed(b"\0\x10"*16000);session.finish();await session.run()
        start=events[0]
        self.assertEqual(start["model_revision"],"a"*40)
        self.assertEqual(start["verification_revision"],"b"*40)
        result=next(e for e in events if e["type"]=="verification.completed")
        self.assertEqual(result["primary_revision"],"a"*40)
        self.assertEqual(result["comparison_id"],comparison_identity(
            "fast","a"*40,"verify","b"*40,result["primary"],result["verification"]))

    def test_identity_is_exact_and_revision_sensitive(self):
        args=("fast","a"*40,"verify","b"*40,"Žltý 😀\n","Žltý strom.")
        self.assertEqual(comparison_identity(*args),comparison_identity(*args))
        changed=list(args);changed[1]="c"*40
        self.assertNotEqual(comparison_identity(*args),comparison_identity(*changed))
        changed=list(args);changed[4]="Žltý 😀"
        self.assertNotEqual(comparison_identity(*args),comparison_identity(*changed))
