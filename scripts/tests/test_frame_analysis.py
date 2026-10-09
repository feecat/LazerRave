"""Regression checks for steady-state frame filtering and hitch detection."""
import csv
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec=importlib.util.spec_from_file_location('analyse_frames',Path(__file__).resolve().parents[1]/'performance/analyse_frames.py')
analyser=importlib.util.module_from_spec(spec);spec.loader.exec_module(analyser)


class FrameAnalysisTests(unittest.TestCase):
    def trace(self, intervals, loading=False):
        directory=tempfile.TemporaryDirectory();self.addCleanup(directory.cleanup)
        path=Path(directory.name)/'engine-trace.csv'
        fields=['scene','playing','play_time_ms','interval_ms','pacing_ms','message_ms','draw_ms','present_ms','outside_ms','dropped_rows']
        with path.open('w',newline='',encoding='utf-8') as f:
            writer=csv.DictWriter(f,fieldnames=fields);writer.writeheader()
            if loading:writer.writerow(dict.fromkeys(fields,'0')|{'scene':'4','play_time_ms':'1000','interval_ms':'500','draw_ms':'500'})
            for interval in intervals:
                writer.writerow(dict.fromkeys(fields,'0')|{'scene':'4','playing':'1','play_time_ms':'5000','interval_ms':interval,'present_ms':interval})
        return path

    def test_120_hz_stream_is_not_reported_as_hitch(self):
        result=analyser.analyse(self.trace([1000/120]*1000))
        self.assertAlmostEqual(result['fps'],120)
        self.assertEqual(result['over_budget'],0)
        self.assertLess(result['balance_error'],.0001)

    def test_single_stall_survives_high_average_fps(self):
        result=analyser.analyse(self.trace([1000/120]*999+[50]))
        self.assertGreater(result['fps'],119)
        self.assertEqual(result['maximum'],50)
        self.assertEqual(result['over_budget'],1)
        self.assertEqual(len(result['hitches']),1)

    def test_loading_stall_is_excluded_from_gameplay(self):
        result=analyser.analyse(self.trace([1000/120]*100,loading=True))
        self.assertEqual(result['frames'],100)
        self.assertEqual(result['over_budget'],0)

if __name__=='__main__':unittest.main()
