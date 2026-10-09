"""Summarise engine per-frame traces without treating CPU timings as display timings."""
from __future__ import annotations
import argparse
import csv
import math
from pathlib import Path
import statistics


def percentile(values, p):
    values=sorted(values)
    if not values:return 0.0
    index=(len(values)-1)*p
    low=math.floor(index);high=math.ceil(index)
    return values[low]+(values[high]-values[low])*(index-low)


def analyse(path, refresh=120, warmup=3000):
    with path.open(encoding='utf-8-sig',newline='') as f:all_rows=list(csv.DictReader(f))
    rows=[r for r in all_rows if int(r['scene'])==4 and r['playing']=='1' and float(r['play_time_ms'])>=warmup]
    if not rows:raise ValueError(f'No steady gameplay frames: {path}')
    intervals=[float(r['interval_ms']) for r in rows]
    budget=1000/refresh
    stages=['pacing_ms','message_ms','draw_ms','present_ms','outside_ms']
    balance=max(abs(float(r['interval_ms'])-sum(float(r[s]) for s in stages)) for r in rows)
    valid_statistics=0; display_gaps=[]; last=None
    for row in rows:
        if row.get('statistics_status')!='0':last=None;continue
        valid_statistics+=1
        current=(int(row['displayed_present']),int(row['display_refresh']))
        if last and current[0]>last[0] and current[1]-last[1]>current[0]-last[0]:
            display_gaps.append((row['play_time_ms'],current[0]-last[0],current[1]-last[1]))
        last=current
    return dict(path=path,rows=rows,frames=len(rows),seconds=sum(intervals)/1000,fps=1000/statistics.mean(intervals),
                median=percentile(intervals,.5),p95=percentile(intervals,.95),p99=percentile(intervals,.99),p999=percentile(intervals,.999),maximum=max(intervals),
                over_budget=sum(v>budget*1.5 for v in intervals),over_16=sum(v>1000/60 for v in intervals),
                hitches=[r for r in rows if float(r['interval_ms'])>budget*1.5],
                present_median=percentile([float(r['present_ms']) for r in rows],.5),
                balance_error=balance,dropped=max(int(r['dropped_rows']) for r in rows),stats_valid=valid_statistics,display_gaps=display_gaps)


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('paths',nargs='+',type=Path)
    p.add_argument('--refresh',type=float,default=120);p.add_argument('--warmup-ms',type=float,default=3000)
    p.add_argument('--output',type=Path,required=True);args=p.parse_args()
    if args.refresh<=0:p.error('refresh must be positive')
    files=sorted({f for path in args.paths for f in ([path] if path.is_file() else path.rglob('engine-trace-*.csv'))})
    # Runtime directories contain copies of traces already stored with each case.
    files=[f for f in files if 'runtime' not in f.parts]
    results=[analyse(f,args.refresh,args.warmup_ms) for f in files]
    if not results:raise RuntimeError('No traces')
    lines=['# 帧时间对比记录','',f'刷新率基准：{args.refresh:g} Hz。仅统计 scene=4、playing=1 且演奏时间超过 {args.warmup_ms/1000:g} 秒的帧。',
           '软件提交间隔由程序计时，不代表显示器实际显示间隔，也不能证明没有撕裂或重复帧。加载、准备与结束过渡已排除。','',
           '| 测试 | 有效秒数 | 帧数 | 平均 FPS | P95 ms | P99 ms | P99.9 ms | 最大 ms | >1.5 帧预算 | >16.67 ms | Present 中位 ms |',
           '| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |']
    for r in results:
        lines.append(f"| {r['path'].parent.name} | {r['seconds']:.1f} | {r['frames']} | {r['fps']:.2f} | {r['p95']:.3f} | {r['p99']:.3f} | {r['p999']:.3f} | {r['maximum']:.3f} | {r['over_budget']} | {r['over_16']} | {r['present_median']:.3f} |")
    lines.extend(['','## 计时完整性',''])
    for r in results:lines.append(f"- {r['path'].parent.name}：阶段之和与帧间隔最大误差 {r['balance_error']:.4f} ms；丢弃记录 {r['dropped']}。来源：`{r['path']}`。")
    lines.extend(['','## 交换链显示统计',''])
    for r in results:
        lines.append(f"- {r['path'].parent.name}：有效显示统计 {r['stats_valid']} 条；刷新计数推进超过显示提交 ID 推进的事件 {len(r['display_gaps'])} 次。")
    lines.extend(['','未采集到统计或接口返回错误时，不能解释为零掉帧。上述事件只用于筛选可疑刷新间隔；窗口遮挡、尺寸变化和采样间隔仍需结合原始记录判断。',
                  '统计字段依据 [DXGI_FRAME_STATISTICS](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ns-dxgi-dxgi_frame_statistics)。',
                  '','## 边界','',
                  '- 前端模式使用完整 OsuGame、真实 BMS、自动演奏和原生子窗口；host 模式使用空白 Win32 父窗口；standalone 使用经典独立路径。',
                  '- 自动演奏可以比较渲染与资源开销，不能代表人工输入延迟。',
                  '- 此处的 Present 耗时包含图形提交中的等待，不能直接归为 GPU 运算时间。',
                  '- 短时采样未出现长帧时，仅说明本次样本未复现，不能认定偶发问题已解决。'])
    args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text('\n'.join(lines)+'\n',encoding='utf-8')
    hitch_path=args.output.with_suffix('.csv')
    with hitch_path.open('w',newline='',encoding='utf-8') as f:
        fields=['case','play_time_ms','interval_ms','pacing_ms','message_ms','draw_ms','present_ms','outside_ms','phase','bga1','bga2']
        writer=csv.DictWriter(f,fieldnames=fields);writer.writeheader()
        for r in results:
            for row in r['hitches']:writer.writerow({'case':r['path'].parent.name,**{key:row[key] for key in fields[1:]}})
    print('\n'.join(lines[:9+len(results)]))
    print('Report:',args.output,'Hitch details:',hitch_path)

if __name__=='__main__':main()
