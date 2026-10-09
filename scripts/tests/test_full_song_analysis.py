"""Check complete-run rejection and preservation of short software stalls."""
import csv
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec=importlib.util.spec_from_file_location('analyse_full_song',Path(__file__).resolve().parents[1]/'performance/analyse_full_song.py')
analyser=importlib.util.module_from_spec(spec);spec.loader.exec_module(analyser)


class FullSongAnalysisTests(unittest.TestCase):
    def fixture(self, completed='1', dropped='0'):
        temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup);folder=Path(temporary.name)
        with (folder/'engine-session-1.csv').open('w',newline='') as f:
            writer=csv.writer(f);writer.writerow(['completed','total_notes','judged_notes','max_combo','last_play_time_ms','song_duration_ms','dropped_rows'])
            writer.writerow([completed,'311','311','311','5050','5050',dropped])
        fields=['scene','playing','play_time_ms','interval_ms','combo','bpm','render_time','pacing_ms','message_ms','draw_ms','present_ms','outside_ms']
        with (folder/'engine-trace-1.csv').open('w',newline='') as f:
            writer=csv.DictWriter(f,fieldnames=fields);writer.writeheader()
            for t,combo,dt,render in [(4000,309,8,100),(5000,310,50,100),(5050,311,8,200)]:
                writer.writerow(dict.fromkeys(fields,'0')|{'scene':'4','playing':'1','play_time_ms':t,'combo':combo,'interval_ms':dt,'bpm':'100','render_time':render,'present_ms':dt})
        return folder

    def test_stall_at_target_combo_is_preserved(self):
        result=analyser.read_case(self.fixture())
        self.assertEqual(result['target_time'],5000)
        self.assertEqual(result['target_max'],50)
        self.assertEqual(len(result['hitches']),1)
        self.assertEqual(result['plateaus'],[(4000,5000,309,310)])

    def test_early_exit_is_not_accepted_as_full_song(self):
        with self.assertRaisesRegex(ValueError,'Incomplete recording'):analyser.read_case(self.fixture(completed='0'))

    def test_trace_overflow_is_not_accepted_as_complete(self):
        with self.assertRaisesRegex(ValueError,'Incomplete recording'):analyser.read_case(self.fixture(dropped='1'))


if __name__=='__main__':unittest.main()
