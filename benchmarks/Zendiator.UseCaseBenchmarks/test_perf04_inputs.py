"""Keep the generated opt-in supplement matched to the ordinary named producer."""

from pathlib import Path
import unittest


class Perf04InputSourceTests(unittest.TestCase):
    def test_opt_in_preserves_the_complete_named_producer_body(self):
        root = Path(__file__).resolve().parent
        ordinary = (root / "Work.cs").read_text(encoding="utf-8-sig")
        ordinary = ordinary.split("public sealed class WorkStreamCursor", 1)[1]
        ordinary = ordinary.split("// Reused only by sequential typed-terminal", 1)[0].strip()
        opted = (root / "Perf04GeneratedProducer.cs").read_text(encoding="utf-8-sig")
        self.assertEqual(opted.count("[Zendiator.GeneratePooledProducer]"), 1)
        self.assertNotIn("AsyncMethodBuilder", opted)
        opted = opted.split("public sealed partial class Perf04GeneratedWorkStreamCursor", 1)[1]
        opted = opted.replace("    [Zendiator.GeneratePooledProducer]\n", "").strip()
        self.assertEqual(ordinary, opted)


if __name__ == "__main__":
    unittest.main()
