"""Stream complete autoplay traces into frame-time, FPS and combo reports."""
from __future__ import annotations
import argparse
import csv
import math
from pathlib import Path
import statistics
from collections import defaultdict


def percentile(values, p):
    ordered=sorted(values)
    if not ordered:return 0.0
    index=(len(ordered)-1)*p;low=math.floor(index);high=math.ceil(index)
    return ordered[low]+(ordered[high]-ordered[low])*(index-low)


def read_case(folder, combo=310):
    traces=list(folder.glob('engine-trace-*.csv'));sessions=list(folder.glob('engine-session-*.csv'))
    if len(traces)!=1 or len(sessions)!=1:raise ValueError(f'Expected one trace and completion certificate: {folder}')
    with sessions[0].open(newline='') as f:certificate=next(csv.DictReader(f))
    if certificate['completed']!='1' or certificate['dropped_rows']!='0' or certificate['total_notes']!=certificate['judged_notes']:
        raise ValueError(f'Incomplete recording: {folder}: {certificate}')
    intervals=[];bins=defaultdict(list);hitches=[];target=[];plateaus=[];bpm_events=[]
    previous=None;plateau_start=None;last_bpm=None;target_time=None;max_balance=0;total_frames=0
    with traces[0].open(newline='',encoding='utf-8-sig') as f:
        reader=csv.DictReader(f);raw_fields=reader.fieldnames
        for row in reader:
            total_frames+=1
            if row['scene']!='4' or row['playing']!='1':continue
            t=float(row['play_time_ms']);dt=float(row['interval_ms']);current_combo=int(row['combo'])
            if current_combo>=combo and target_time is None:target_time=t
            if combo-10<=current_combo<=combo+10:target.append(row)
            bpm=float(row['bpm']);render=float(row['render_time'])
            if bpm!=last_bpm:bpm_events.append((t,bpm,current_combo));last_bpm=bpm
            if previous and render==previous[1] and t>=3000:
                if plateau_start is None:plateau_start=(previous[0],previous[2])
            elif plateau_start is not None:
                if previous[0]-plateau_start[0]>=20:plateaus.append((plateau_start[0],previous[0],plateau_start[1],previous[2]))
                plateau_start=None
            previous=(t,render,current_combo)
            if t<3000:continue
            intervals.append(dt);bins[int(t/1000)].append((dt,current_combo,float(row['message_ms']),float(row['draw_ms']),float(row['present_ms']),float(row['outside_ms'])))
            max_balance=max(max_balance,abs(dt-sum(float(row[k]) for k in ['pacing_ms','message_ms','draw_ms','present_ms','outside_ms'])))
            if dt>1000/120:hitches.append(row)
    if not intervals or target_time is None:raise ValueError(f'Recording does not reach combo {combo}: {folder}')
    per_second=[]
    for second,values in sorted(bins.items()):
        dts=[v[0] for v in values]
        per_second.append(dict(second=second,frames=len(values),fps=len(values)*1000/sum(dts),p99_ms=percentile(dts,.99),max_ms=max(dts),
            combo_start=values[0][1],combo_end=values[-1][1],message_max_ms=max(v[2] for v in values),draw_max_ms=max(v[3] for v in values),
            present_max_ms=max(v[4] for v in values),outside_max_ms=max(v[5] for v in values)))
    nearby=[r for r in target if abs(float(r['play_time_ms'])-target_time)<=2000]
    metadata={}
    if (folder.parent/'run-metadata.csv').exists():
        with (folder.parent/'run-metadata.csv').open(newline='',encoding='utf-8') as f:metadata={r['setting']:r['value'] for r in csv.DictReader(f)}
    frontend=[]
    if (folder/'frontend-fps.csv').exists():
        with (folder/'frontend-fps.csv').open(newline='') as f:frontend=list(csv.DictReader(f))
    return dict(name=folder.name,metadata=metadata,folder=folder,trace=traces[0],certificate=certificate,total_frames=total_frames,
        raw_fields=raw_fields,frames=len(intervals),seconds=sum(intervals)/1000,fps=1000/statistics.mean(intervals),p99=percentile(intervals,.99),
        p999=percentile(intervals,.999),maximum=max(intervals),over_833=sum(v>1000/120 for v in intervals),
        over_125=sum(v>12.5 for v in intervals),over_167=sum(v>1000/60 for v in intervals),
        per_second=per_second,hitches=hitches,target=nearby,target_time=target_time,
        target_max=max(float(r['interval_ms']) for r in nearby),plateaus=plateaus,bpm_events=bpm_events,balance=max_balance,frontend=frontend)


def save_plot(cases,path):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    fig,axes=plt.subplots(4,1,figsize=(13,12),layout='constrained')
    for case in cases:
        series=case['per_second'];name=case['name']
        axes[0].plot([v['second'] for v in series],[v['max_ms'] for v in series],label=name)
        axes[1].plot([v['second'] for v in series],[v['fps'] for v in series],label=name)
        front=case['frontend']
        if front:axes[2].plot([float(v['elapsed_s']) for v in front],[float(v['draw_fps']) for v in front],label=name)
        # Max within each 10 ms bucket preserves short spikes in the focused graph.
        buckets=defaultdict(list)
        for row in case['target']:
            relative=float(row['play_time_ms'])-case['target_time'];buckets[round(relative/10)*10].append(float(row['interval_ms']))
        axes[3].plot([k/1000 for k in sorted(buckets)],[max(buckets[k]) for k in sorted(buckets)],label=name)
    axes[0].axhline(1000/120,linestyle='--',color='grey',label='120 Hz frame budget')
    axes[0].set(title='Maximum engine software frame interval in each second',ylabel='Milliseconds',xlabel='Song time (s)')
    axes[1].set(title='Engine software submission FPS (not display refresh rate)',ylabel='FPS',xlabel='Song time (s)',yscale='log')
    axes[2].set(title='Frontend draw FPS, sampled once per second',ylabel='FPS',xlabel='Session elapsed (s)')
    axes[3].set(title='Frame intervals near the first reach of combo 310',ylabel='Milliseconds',xlabel='Seconds relative to combo 310')
    for ax in axes:ax.grid(alpha=.25);ax.legend(fontsize=8)
    fig.savefig(path,dpi=150);plt.close(fig)


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('cases',nargs='+',type=Path)
    parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    cases=[read_case(p) for p in args.cases];args.output.mkdir(parents=True,exist_ok=True)
    charts='、'.join(sorted({Path(c['metadata']['chart']).stem for c in cases if 'chart' in c['metadata']})) or '见运行元数据'
    lines=['# 完整自动演奏性能记录','',
        f'曲目：{charts}。每组等待自然演奏结束，完成标记、判定对象总数和记录丢失数均通过校验。原始 CSV 包含加载、准备和结束阶段；下表统计正常演奏中超过最初 3 秒的帧。',
        'FPS 和帧时间是软件提交计时，不能据此排除显示重复、撕裂或实际显示掉帧。经典路径的高 FPS 不代表显示器刷新率。','',
        '| 模式 | 完整判定数 | 原始帧数 | 有效演奏秒数 | 平均 FPS | P99 ms | P99.9 ms | 最大 ms | >8.33 ms | >12.5 ms | >16.67 ms |',
        '| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |']
    for c in cases:
        cert=c['certificate']
        lines.append(f"| {c['name']} | {cert['judged_notes']}/{cert['total_notes']} | {c['total_frames']} | {c['seconds']:.2f} | {c['fps']:.2f} | {c['p99']:.3f} | {c['p999']:.3f} | {c['maximum']:.3f} | {c['over_833']} | {c['over_125']} | {c['over_167']} |")
        for name,rows in [('per-second',c['per_second']),('combo-310',c['target']),('long-frames',c['hitches'])]:
            path=args.output/f"{c['name']}-{name}.csv"
            with path.open('w',newline='',encoding='utf-8') as f:
                if rows:
                    writer=csv.DictWriter(f,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)
                else:csv.writer(f).writerow(c['raw_fields'])
    lines.extend(['','## Combo 310 附近',''])
    for c in cases:
        lines.append(f"- {c['name']}：首次达到 310 combo 的演奏时间为 {c['target_time']/1000:.4f} 秒；相邻目标区间最大软件帧间隔 {c['target_max']:.4f} ms。")
        lines.append('  音符渲染时间保持不变的片段（仅列出超过 20 ms）：')
        for start,end,first,last in c['plateaus']:
            lines.append(f'  - {start/1000:.4f}～{end/1000:.4f} 秒，持续 {end-start:.2f} ms，combo {first}～{last}。')
        lines.append(f"  帧阶段之和误差最大 {c['balance']:.4f} ms；完成时最大 combo {c['certificate']['max_combo']}；逐帧记录丢弃 {c['certificate']['dropped_rows']} 条。")
    lines.extend(['','## 实际 BPM 变化',''])
    for c in cases:
        lines.append(f"- {c['name']}：" + '；'.join(f'{t/1000:.4f} 秒 BPM={bpm:g}（combo {combo}）' for t,bpm,combo in c['bpm_events']) + '。')
        path=args.output/f"{c['name']}-bpm-events.csv"
        with path.open('w',newline='',encoding='utf-8') as f:
            writer=csv.writer(f);writer.writerow(['play_time_ms','bpm','combo']);writer.writerows(c['bpm_events'])
    lines.extend(['','## 记录边界','',
        '- 自动演奏不能代替人工输入延迟测试。',
        '- long-frames.csv 保留超过 120 Hz 单帧预算（8.33 ms）的软件帧；空文件保留 CSV 表头。',
        '- 逐帧记录仅在诊断模式启用，内存缓冲，正常退出后统一写盘；实时监控通过只读共享内存获取。',
        '- 原生交换链显示统计不可用时，不能将软件帧时间正常解释为显示无卡顿。',
        '- 前端 FPS 每秒采样；其瞬时帧时间是采样值，不能用于排除采样间隔中的短暂长帧。','',
        '![完整 FPS 与帧时间曲线](full-song-frames.png)',''])
    path=args.output/'REPORT.md';path.write_text('\n'.join(lines),encoding='utf-8')
    save_plot(cases,args.output/'full-song-frames.png')
    print('\n'.join(lines[:9+len(cases)]));print('Report:',path)


if __name__=='__main__':main()
