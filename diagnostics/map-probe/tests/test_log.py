import contextlib
import importlib.util
import io
from pathlib import Path
import unittest
root = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('analyzer', root/'Analyze-MapProbe.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class LogTests(unittest.TestCase):
    def report(self, lines):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            for run, result in m.parse(lines).items():
                m.summarize(run, result)
        return out.getvalue()
    def test_complete_actual_offline_probe_output(self):
        lines = (root/'artifacts/offline-fixture.log').read_text().splitlines()
        result = self.report(lines)
        self.assertIn('Complete managed samples: 3/3', result)
        self.assertIn('delta +0', result)
        self.assertNotIn('WARN', result)
    def test_missing_record(self):
        lines = (root/'artifacts/offline-fixture.log').read_text().splitlines()
        index = next(i for i,l in enumerate(lines) if 'kind=cell' in l)
        del lines[index]
        self.assertIn('WARN', self.report(lines))
    def test_timestamp_prefix(self):
        lines = (root/'artifacts/offline-fixture.log').read_text().splitlines()
        self.assertIn('Complete managed samples: 3/3', self.report(['[time] '+l for l in lines]))
    def test_descriptive_50_microgram_change(self):
        lines = (root/'artifacts/offline-fixture.log').read_text().splitlines()
        changed = []
        for line in lines:
            if 'kind=cell' in line and '|seq=0|' in line and '|dx=0|dy=0|' in line:
                fields = line.split('|')
                fields = ['kg=0' if f.startswith('kg=') else 'kgBits=0x00000000' if f.startswith('kgBits=') else 'element=Vacuum' if f.startswith('element=') else f for f in fields]
                line = '|'.join(fields)
            changed.append(line)
        report = self.report(changed)
        self.assertIn('delta +50', report)
        self.assertIn('not evidence of cause', report)

    def test_readonly_surface(self):
        text = (root/'MapProbe.cs').read_text()
        for name in ['GUILayout.SelectionGrid', 'SimMessages.', 'ConduitFlow', '[Serialize]', 'Game.CanSave', 'SaveGame', 'massConsumedCallbackManager', 'massEmitCallbackManager']:
            # Only executable lines: comments may explain the restrictions.
            executable = '\n'.join(l for l in text.splitlines() if not l.lstrip().startswith('//'))
            self.assertNotIn(name, executable)
        import re
        self.assertIsNone(re.search(r'Grid\.\w+\[[^\]]+\]\s*=(?!=)', text))

if __name__ == '__main__': unittest.main()
